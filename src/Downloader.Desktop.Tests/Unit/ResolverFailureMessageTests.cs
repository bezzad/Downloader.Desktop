using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// What a user is told when a plugin claimed their link and then could not download it.
/// <para>
/// This used to be nothing useful: the failure was swallowed, the page URL was downloaded as an ordinary
/// link, and whatever the HTML turned into was reported instead — so a page a plugin had explicitly
/// refused (live stream, protected, needs a session) came back as an invalid link. The resolver's own
/// reason now reaches the row, with one exception: "this site wants a signed-in session" is replaced by
/// the app's localized wording, because the old phrasing told people to sign in when they already were.
/// </para>
/// </summary>
public class ResolverFailureMessageTests
{
    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("This site hands the video only to a signed-in session, and this link was added without one.", true)]
    [InlineData("Send the page from the Downloader browser session helper", true)]
    [InlineData("This looks like a live stream, which can't be downloaded as a file.", false)]
    [InlineData("The video is protected and can't be downloaded.", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_session_failure_is_told_apart_from_every_other_reason(string? message, bool expected)
    {
        Assert.Equal(expected, DownloadManager.LooksLikeNeedsBrowserSession(message));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_claimed_link_that_the_plugin_refuses_fails_with_the_plugin_s_reason()
    {
        Services.Localizer.Instance.Load("en");
        var plugins = new PluginManager();
        plugins.RegisterPlugin(new RefusingPlugin("This looks like a live stream, which can't be downloaded as a file."));
        var manager = new DownloadManager(plugins);
        // The queue must actually run — the resolver refuses the link before any network call, which is
        // the whole point: nothing here reaches the wire.
        manager.Initialize(Config.New());

        var item = new DownloadItem { Url = "https://videos.example/live/1", SaveFolder = System.IO.Path.GetTempPath() };
        manager.Add(item, autoStart: true);
        var vm = manager.Items[0];
        await WaitForFailure(vm);

        Assert.Equal(global::Downloader.DownloadStatus.Failed, vm.Status);
        Assert.Equal("This looks like a live stream, which can't be downloaded as a file.", vm.ErrorMessage);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_site_that_wants_a_session_is_told_to_send_the_page_from_the_extension()
    {
        Services.Localizer.Instance.Load("en");
        var plugins = new PluginManager();
        plugins.RegisterPlugin(new RefusingPlugin(
            "This site hands the video only to a signed-in session, and this link was added without one."));
        var manager = new DownloadManager(plugins);
        // The queue must actually run — the resolver refuses the link before any network call, which is
        // the whole point: nothing here reaches the wire.
        manager.Initialize(Config.New());

        manager.Add(new DownloadItem { Url = "https://videos.example/watch/2", SaveFolder = System.IO.Path.GetTempPath() },
            autoStart: true);
        var vm = manager.Items[0];
        await WaitForFailure(vm);

        Assert.Equal(Services.Localizer.Instance["Error_SiteNeedsBrowserSession"], vm.ErrorMessage);
        Assert.Contains("extension", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An UNCLAIMED link is untouched by all of this: a resolver that never claimed it must not be
    /// able to fail it, and the download proceeds exactly as before.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_unclaimed_link_still_falls_through_to_a_plain_download()
    {
        var plugins = new PluginManager();
        plugins.RegisterPlugin(new RefusingPlugin("never asked"));
        var manager = new DownloadManager(plugins);

        // The resolver claims only videos.example, so this one resolves to "no plan" rather than throwing.
        var plan = manager.ResolvePlanAsync("https://files.example/app.zip", CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.Null(plan);
    }

    private static async Task WaitForFailure(ViewModels.DownloadItemViewModel vm)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (vm.Status != global::Downloader.DownloadStatus.Failed && DateTime.UtcNow < deadline)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private sealed class RefusingResolver(string reason) : ILinkResolver
    {
        public bool CanResolve(string url) => url.Contains("videos.example", StringComparison.Ordinal);
        public Task<DownloadPlan> ResolveAsync(string url, CancellationToken ct) =>
            throw new InvalidOperationException(reason);
    }

    private sealed class RefusingPlugin(string reason) : IDownloaderPlugin
    {
        public string Id => "test.refusing";
        public string Name => "Refusing Plugin";
        public string Version => "1.0.0";
        public string Author => "tester";
        public string Description => "claims a site and then refuses it";
        public void Initialize(IPluginContext context) => context.RegisterResolver(new RefusingResolver(reason));
    }
}
