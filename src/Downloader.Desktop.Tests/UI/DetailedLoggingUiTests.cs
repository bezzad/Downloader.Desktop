using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>User actions and the app's reactions reach the log (<c>detailed-logging</c>), through the
/// real controls and the real manager — and secrets and progress ticks do not.</summary>
public class DetailedLoggingUiTests
{
    private sealed class StubFileService : IFileService
    {
        // Logging on in the saved config — startup applies it, and an "off" here would silence the test.
        public Task<Config> LoadFromFileAsync()
        {
            var config = Config.New();
            config.Settings.EnableLogging = true;
            return Task.FromResult(config);
        }
        public Task SaveToFileAsync(Config itemToSave) => Task.CompletedTask;
    }

    private static Window Show(Control content)
    {
        UiActionLog.Register(); // App.Initialize does this; the headless app is shared, so it is idempotent
        var window = new Window { Width = 400, Height = 300, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Click(Control control) =>
        control.RaiseEvent(new RoutedEventArgs(control is MenuItem ? MenuItem.ClickEvent : Button.ClickEvent, control));

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_button_click_names_the_button_and_the_download_it_acted_on()
    {
        using var log = new LogScope();
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        var row = manager.Add(new DownloadItem { Url = "https://10.255.255.1/movie.mkv", FileName = "movie.mkv" }, false);

        var button = new Button { Content = "Pause", Name = "PauseBtn", DataContext = row };
        var window = Show(new StackPanel { Children = { button } });
        button.Focus();
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
        window.Close();

        Assert.Contains("UI: click \"Pause\" (Button) #PauseBtn in Window on \"movie.mkv\"", log.Text());
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_context_menu_item_is_logged_with_the_download_it_belongs_to()
    {
        using var log = new LogScope();
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        var row = manager.Add(new DownloadItem { Url = "https://10.255.255.1/a.zip", FileName = "a.zip" }, false);

        var item = new MenuItem { Header = "Pause" };
        var target = new Border { Width = 50, Height = 50, DataContext = row, ContextMenu = new ContextMenu { Items = { item } } };
        var window = Show(new UserControl { Content = target });
        target.ContextMenu.Open(target);
        Dispatcher.UIThread.RunJobs();
        Click(item);
        Dispatcher.UIThread.RunJobs();
        target.ContextMenu.Close();
        window.Close();

        var text = log.Text();
        Assert.Contains("UI: click \"Pause\" (MenuItem)", text);
        Assert.Contains("in UserControl on \"a.zip\"", text);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_toggle_switch_logs_its_new_state()
    {
        using var log = new LogScope();
        var toggle = new ToggleSwitch { Content = "Write a log file", IsChecked = false };
        var window = Show(new StackPanel { Children = { toggle } });
        toggle.Focus();
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
        window.Close();

        Assert.True(toggle.IsChecked);
        Assert.Contains("UI: click \"Write a log file\" (ToggleSwitch) → on", log.Text());
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Typed_text_is_never_logged()
    {
        using var log = new LogScope();
        var box = new TextBox();
        var window = Show(new StackPanel { Children = { box } });
        box.Focus();
        window.KeyTextInput("secret-search-word");
        Dispatcher.UIThread.RunJobs();
        window.Close();

        Assert.Equal("secret-search-word", box.Text);
        Assert.DoesNotContain("secret-search-word", log.Text());
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Opening_a_page_is_logged()
    {
        Localizer.Instance.Load("en");
        using var log = new LogScope();
        var manager = new DownloadManager();
        var main = new MainViewModel(new StubFileService(), manager);
        manager.Initialize(Config.New());

        main.ShowSettingViewCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        main.ShowSettingViewCommand.Execute(null); // same page again: no second line
        Dispatcher.UIThread.RunJobs();

        var text = log.Text();
        Assert.Contains("UI: page Settings opened", text);
        Assert.Equal(1, Count(text, "page Settings opened"));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_dialog_is_logged_when_it_opens_and_closes()
    {
        using var log = new LogScope();
        var dialog = new Window();
        DialogHelper.BeginModal(dialog);
        dialog.Show();
        dialog.Close();
        Dispatcher.UIThread.RunJobs();

        var text = log.Text();
        Assert.Contains("UI: dialog Window opened", text);
        Assert.Contains("UI: dialog Window closed", text);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_download_lifecycle_is_logged_without_the_links_secret()
    {
        using var log = new LogScope();
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        var row = manager.Add(new DownloadItem { Url = "https://10.255.255.1/f.zip?token=abc", FileName = "f.zip" }, false);

        manager.Resume(row); // queues and starts (the address is unreachable; nothing leaves the box)
        manager.Pause(row);
        manager.Cancel(row);
        manager.Retry(row);
        manager.Archive(row);
        Dispatcher.UIThread.RunJobs();

        var text = log.Text();
        Assert.Contains("Added: f.zip", text);
        Assert.Contains("Resume: f.zip", text);
        Assert.Contains("starting f.zip", text);
        Assert.Contains("Starting: https://10.255.255.1/f.zip", text);
        Assert.Contains("Paused: f.zip", text);
        Assert.Contains("Stopped: f.zip", text);
        Assert.Contains("Retry: f.zip", text);
        Assert.Contains("Archived: f.zip", text);
        Assert.DoesNotContain("token=abc", text);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_failed_download_logs_the_exception_with_its_stack_trace()
    {
        using var log = new LogScope();
        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false;
        manager.Initialize(config);
        var row = manager.Add(new DownloadItem { Url = "https://10.255.255.1/x.bin", FileName = "x.bin" }, false);
        row.Status = DownloadStatus.Running;

        Exception error;
        try { throw new System.IO.IOException("disk went away"); }
        catch (Exception ex) { error = ex; }
        manager.RaiseFailedForTest(row, error);

        var text = log.Text();
        Assert.Contains("System.IO.IOException: disk went away", text);
        Assert.Contains(nameof(A_failed_download_logs_the_exception_with_its_stack_trace), text); // a stack frame
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_minute_of_progress_writes_no_progress_lines()
    {
        using var log = new LogScope();
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        var row = manager.Add(new DownloadItem { Url = "https://10.255.255.1/big.iso", FileName = "big.iso" }, false);
        row.Status = DownloadStatus.Running;
        var before = log.Text().Length;

        for (var tick = 0; tick < 240; tick++) // one minute of the 250 ms UI pump
        {
            row.StageProgress(tick / 2.4, 1_000_000, tick * 1000L, 1_000_000);
            manager.RunUiPumpTickOnce();
        }

        Assert.Equal(before, log.Text().Length);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_extension_add_leaves_no_cookie_or_header_value_in_the_log()
    {
        using var log = new LogScope();
        var config = Config.New();
        var manager = new DownloadManager();
        manager.Initialize(config);
        var request = ApiAddRequest.FromJson("""
            {"url":"https://10.255.255.1/v.mp4?sig=SIGVALUE","filename":"v.mp4",
             "cookies":[{"name":"sid","value":"COOKIEVALUE","domain":"10.255.255.1","path":"/"}],
             "headers":{"Authorization":"Bearer HEADERVALUE"},"referer":"https://site.example/page?ref=REFVALUE"}
            """);

        var row = manager.Add(LocalApiService.BuildItem(request, config), autoStart: true);
        Dispatcher.UIThread.RunJobs();
        manager.Cancel(row);

        var text = log.Text();
        Assert.Contains("Added: v.mp4", text);
        foreach (var secret in new[] { "COOKIEVALUE", "HEADERVALUE", "SIGVALUE", "REFVALUE" })
            Assert.DoesNotContain(secret, text);
    }

    private static int Count(string text, string part)
    {
        var n = 0;
        for (var i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + 1, StringComparison.Ordinal))
            n++;
        return n;
    }
}
