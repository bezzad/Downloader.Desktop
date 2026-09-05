using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The dialog a confirm-mode programmatic add opens (issue #13), really opened.
///
/// The point of routing this through <c>/api/add</c> rather than the URL-only legacy endpoint is that
/// the dialog must carry the WHOLE hand-off — its cookies, referer and headers included — or confirming
/// would produce a download that fails where the silent one would have worked. So these assert the
/// context on the item the confirm actually creates, not just that a window appeared.
/// </summary>
public class ConfirmAddDialogTests : IDisposable
{
    private sealed class StubFileService : IFileService
    {
        private readonly Config _config;
        public StubFileService(Config config) => _config = config;
        public Task<Config> LoadFromFileAsync() => Task.FromResult(_config);
        public Task SaveToFileAsync(Config itemToSave) => Task.CompletedTask;
    }

    public ConfirmAddDialogTests()
    {
        LocalApiService.Stop();
        LocalApiService.ClearPendingAdds();
    }

    public void Dispose()
    {
        LocalApiService.Stop();
        LocalApiService.ClearPendingAdds();
        LocalApiService.OnAddConfirmationRequested = null;
        LocalApiService.Manager = null;
        LocalApiService.Config = null;
    }

    private static Config QuietConfig()
    {
        var config = Config.New();
        config.DefaultQueue.IsRunning = false;          // nothing may hit the network here
        config.Settings.EnableBrowserIntegration = false;
        config.Settings.EnableSystemTray = false;
        config.Settings.AutoUpdate = false;
        config.Settings.DefaultSavePath = System.IO.Path.GetTempPath();
        return config;
    }

    private static (MainViewModel Main, DownloadManager Manager) Shell(Config config, Window window)
    {
        var manager = new DownloadManager();
        var main = new MainViewModel(new StubFileService(config), manager, new PluginManager()) { View = window };

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (main.Downloads == null)
        {
            Dispatcher.UIThread.RunJobs();
            if (DateTime.UtcNow > deadline) Assert.Fail("the shell never finished initialising");
            Thread.Sleep(5);
        }
        Dispatcher.UIThread.RunJobs();
        return (main, manager);
    }

    /// <summary>Waits for the Add dialog to be on screen and hands back its view model.</summary>
    private static async Task<AddDownloadItemView> WaitForDialog(Window main)
    {
        AddDownloadItemView view = null;
        for (var i = 0; i < 200 && view == null; i++)
        {
            DesktopLifetimeScope.Pump(1);
            view = main.OwnedWindows.OfType<AddDownloadItemView>().FirstOrDefault();
            if (view == null) await Task.Delay(5);
        }
        Assert.True(view != null, "the Add dialog never appeared");
        return view;
    }

    private static ApiAddRequest FullRequest() => new()
    {
        Url = "https://10.255.255.1/a.zip",
        Filename = "renamed.zip",
        Path = System.IO.Path.GetTempPath(),
        Mirrors = { "https://10.255.255.2/a.zip" },
        Referer = "https://10.255.255.1/page",
        Headers = { ["X-Token"] = "abc" },
        Cookies = { new CookieDto { Name = "SID", Value = "v", Domain = "10.255.255.1", Path = "/" } },
        FromBrowser = true,
        Confirm = true,
        Start = false
    };

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task Confirming_creates_the_download_with_every_field_and_its_context_intact()
    {
        Localizer.Instance.Load("en");
        using var scope = new DesktopLifetimeScope();
        var config = QuietConfig();
        var (main, manager) = Shell(config, scope.MainWindow);
        var req = FullRequest();

        // A ticket the API really registered, so the outcome is observable the way a caller sees it.
        var (ticket, _) = LocalApiService.RegisterPendingAdd();
        var flow = main.CaptureAddRequest(req, ticket);
        var view = await WaitForDialog(scope.MainWindow);

        // Pre-filled with what the caller asked for — editable, but not empty.
        var vm = (AddDownloadItemViewModel)view.DataContext;
        Assert.Equal("https://10.255.255.1/a.zip", vm.Urls.Trim());
        Assert.Equal("renamed.zip", vm.Filename);
        Assert.Equal(System.IO.Path.GetTempPath(), vm.StorageFolderPath);

        // The user confirms.
        view.Close(vm.BuildItems());
        await flow.WaitAsync(TimeSpan.FromSeconds(15));
        DesktopLifetimeScope.Pump();

        var item = Assert.Single(manager.Items).GetItem();
        Assert.Equal("https://10.255.255.1/a.zip", item.Url);
        Assert.Equal("renamed.zip", item.FileName);
        // Mirrors are the link's fallbacks, and they only survive because the URL was left alone.
        Assert.Contains("https://10.255.255.2/a.zip", item.Urls);
        // The half a silent add would have carried and a URL-only dialog would have lost.
        Assert.Equal("https://10.255.255.1/page", item.Referer);
        Assert.Equal("abc", item.Request.Headers["X-Token"]);
        Assert.Equal("SID", Assert.Single(item.Request.Cookies).Name);
        Assert.True(item.FromBrowserDownload);

        // …and the ticket now names that download, which is how the extension learns it may cancel the
        // browser's own copy.
        var resolved = LocalApiService.LookupPendingAdd(ticket);
        Assert.Equal(LocalApiService.PendingAddState.Added, resolved.State);
        Assert.Equal(item.Id.ToString(), resolved.Id);
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task Cancelling_the_dialog_creates_nothing_and_says_so()
    {
        Localizer.Instance.Load("en");
        using var scope = new DesktopLifetimeScope();
        var config = QuietConfig();
        var (main, manager) = Shell(config, scope.MainWindow);

        // A ticket the API really registered, so the cancel is observable the way a caller sees it.
        var (ticket, opened) = LocalApiService.RegisterPendingAdd();
        Assert.True(opened);

        var flow = main.CaptureAddRequest(FullRequest(), ticket);
        var view = await WaitForDialog(scope.MainWindow);
        view.Close();                                  // dismissed — no result
        await flow.WaitAsync(TimeSpan.FromSeconds(15));
        DesktopLifetimeScope.Pump();

        Assert.Empty(manager.Items);
        Assert.Equal(LocalApiService.PendingAddState.Cancelled, LocalApiService.LookupPendingAdd(ticket).State);
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void A_CLI_add_stays_silent_even_with_the_setting_on()
    {
        // A script cannot answer a modal. Whatever the user set, and whatever the payload says, this
        // path adds without asking — the alternative is a CLI invocation that hangs for ever.
        Localizer.Instance.Load("en");
        using var scope = new DesktopLifetimeScope();
        var config = QuietConfig();
        config.Settings.ConfirmProgrammaticAdds = true;
        var (main, manager) = Shell(config, scope.MainWindow);

        main.SilentAdd("""{"url":"https://10.255.255.1/cli.zip","confirm":true,"start":false}""");
        DesktopLifetimeScope.Pump();

        Assert.Single(manager.Items);
        Assert.Empty(scope.MainWindow.OwnedWindows);   // no dialog was opened
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_url_the_user_retyped_does_not_inherit_the_old_link_fallbacks()
    {
        // Mirrors and a caller-picked variant belong to the link the request was ABOUT. Once the user
        // addresses a different file, carrying them over would point the download at the wrong bytes.
        Localizer.Instance.Load("en");
        using var scope = new DesktopLifetimeScope();
        var config = QuietConfig();
        var (main, manager) = Shell(config, scope.MainWindow);

        var flow = main.CaptureAddRequest(FullRequest(), "T2");
        var view = await WaitForDialog(scope.MainWindow);
        var vm = (AddDownloadItemViewModel)view.DataContext;
        vm.Urls = "https://10.255.255.3/other.zip";
        view.Close(vm.BuildItems());
        await flow.WaitAsync(TimeSpan.FromSeconds(15));
        DesktopLifetimeScope.Pump();

        var item = Assert.Single(manager.Items).GetItem();
        Assert.Equal("https://10.255.255.3/other.zip", item.Url);
        Assert.Single(item.Urls);                       // no inherited mirror
        // The context still travels: it is the session the request was made in, not a property of the URL.
        Assert.Equal("https://10.255.255.1/page", item.Referer);
    }
}
