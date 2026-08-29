using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// Every window and page actually loads, binds and renders.
///
/// XAML is resolved at RUNTIME, so this class of bug never shows up at build time: a mistyped
/// resource key, a converter that isn't registered, a removed Avalonia property, a binding against a
/// property the view model no longer has. The app builds clean and then throws (or silently renders
/// nothing) the first time a user opens that screen. These tests instantiate each view against a real
/// view model and pump the dispatcher, which is the cheapest way to catch that.
///
/// They also cover both theme variants, since a theme-scoped resource can be missing in only one of
/// them — the failure mode behind the see-through dialogs (ThemeBackgroundColor was undefined here).
/// </summary>
public class ViewLoadTests
{
    private sealed class StubFileService : IFileService
    {
        public Task<Config> LoadFromFileAsync() => Task.FromResult(Config.New());
        public Task SaveToFileAsync(Config itemToSave) => Task.CompletedTask;
    }

    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
            Dispatcher.UIThread.RunJobs();
    }

    private static (MainViewModel main, DownloadManager manager, Config config) BuildShell()
    {
        Localizer.Instance.Load("en");
        var config = Config.New();
        var manager = new DownloadManager();
        var main = new MainViewModel(new StubFileService(), manager);
        manager.Initialize(config);
        config.DefaultQueue.IsRunning = false;
        Pump();
        return (main, manager, config);
    }

    private static DownloadItemViewModel AddRow(DownloadManager manager, string name, DownloadStatus status)
    {
        var vm = manager.Add(new DownloadItem { Url = "https://10.255.255.1/" + name, FileName = name },
            autoStart: false);
        vm.Status = status;
        return vm;
    }

    // ---- the main window ---------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_main_window_loads_and_shows_the_downloads_list()
    {
        var (main, manager, _) = BuildShell();
        AddRow(manager, "a.bin", DownloadStatus.Running);
        AddRow(manager, "b.bin", DownloadStatus.Completed);

        var window = new MainWindow { DataContext = main };
        window.Show();
        Pump();

        Assert.True(window.IsVisible);
        // The grid is the heart of the screen; if the DataTemplate or its columns fail to resolve it
        // simply is not there.
        var grid = window.GetVisualDescendants().OfType<DataGrid>().FirstOrDefault();
        Assert.NotNull(grid);

        window.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_main_window_renders_in_both_themes()
    {
        var (main, manager, _) = BuildShell();
        AddRow(manager, "a.bin", DownloadStatus.Failed);

        var window = new MainWindow { DataContext = main };
        window.Show();

        var previous = Avalonia.Application.Current!.RequestedThemeVariant;
        try
        {
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                Avalonia.Application.Current.RequestedThemeVariant = variant;
                Pump();
                Assert.True(window.IsVisible);
            }
        }
        finally
        {
            Avalonia.Application.Current.RequestedThemeVariant = previous;
            window.Close();
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Every_management_page_renders_inside_the_main_window()
    {
        var (main, manager, _) = BuildShell();
        AddRow(manager, "a.bin", DownloadStatus.Created);

        var window = new MainWindow { DataContext = main };
        window.Show();
        Pump();

        // The pages swap through the central ContentControl; each one is a separate DataTemplate that
        // can fail to resolve on its own.
        foreach (var navigate in new Action[]
                 {
                     () => main.ShowQueuesCommand.Execute(null),
                     () => main.ShowSchedulerCommand.Execute(null),
                     () => main.ShowSettingViewCommand.Execute(null),
                     () => main.ShowDownloadsCommand.Execute(null),
                 })
        {
            navigate();
            Pump();
            Assert.NotNull(main.CurrentPage);
        }

        window.Close();
    }

    // ---- the pages as standalone controls ---------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_downloads_page_renders_rows_in_every_state()
    {
        var (_, manager, config) = BuildShell();
        foreach (var status in new[]
                 {
                     DownloadStatus.Running, DownloadStatus.Paused, DownloadStatus.Stopped,
                     DownloadStatus.Created, DownloadStatus.Completed, DownloadStatus.Failed,
                 })
            AddRow(manager, status + ".bin", status);

        var page = new DownloadsView { DataContext = new DownloadsViewModel(manager) };
        var host = new Window { Content = page, Width = 900, Height = 600 };
        host.Show();
        Pump();

        // Row icons, status brushes and the progress-bar converters all run per row — a converter
        // that throws on one state would take the whole grid down.
        var grid = page.GetVisualDescendants().OfType<DataGrid>().FirstOrDefault();
        Assert.NotNull(grid);

        host.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_queues_page_renders_a_queue_card()
    {
        var (_, manager, config) = BuildShell();
        AddRow(manager, "a.bin", DownloadStatus.Created);

        var page = new QueuesView { DataContext = new QueuesViewModel(config, manager) };
        var host = new Window { Content = page, Width = 900, Height = 600 };
        host.Show();
        Pump();

        Assert.True(host.IsVisible);
        host.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_scheduler_page_renders_a_schedule_row()
    {
        var (_, manager, config) = BuildShell();
        var vm = new SchedulerViewModel(config, manager);
        vm.NewScheduleCommand.Execute(null);

        var page = new SchedulerView { DataContext = vm };
        var host = new Window { Content = page, Width = 900, Height = 600 };
        host.Show();
        Pump();

        Assert.True(host.IsVisible);
        host.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_settings_page_renders_with_its_plugin_section()
    {
        var (_, manager, config) = BuildShell();

        var page = new SettingView { DataContext = new SettingViewModel(config, manager) };
        var host = new Window { Content = page, Width = 900, Height = 700 };
        host.Show();
        Pump();

        Assert.True(host.IsVisible);
        host.Close();
    }

    // ---- dialogs -----------------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_details_window_loads_for_a_download()
    {
        var (_, manager, _) = BuildShell();
        var row = AddRow(manager, "movie.mkv", DownloadStatus.Stopped);

        var view = new DownloadDetailsView { DataContext = new DownloadDetailsViewModel(row) };
        view.Show();
        Pump();

        Assert.True(view.IsVisible);
        view.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_about_and_donate_windows_load()
    {
        Localizer.Instance.Load("en");

        var about = new AboutView { DataContext = new AboutViewModel() };
        about.Show();
        Pump();
        Assert.True(about.IsVisible);
        about.Close();

        var donate = new DonateView { DataContext = new DonateViewModel() };
        donate.Show();
        Pump();
        Assert.True(donate.IsVisible);
        donate.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_shutdown_countdown_window_loads()
    {
        Localizer.Instance.Load("en");

        var view = new ShutdownView { DataContext = new ShutdownViewModel(30, null, null) };
        view.Show();
        Pump();

        Assert.True(view.IsVisible);
        view.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_update_prompt_window_loads()
    {
        Localizer.Instance.Load("en");

        var view = new UpdatePromptView { DataContext = new UpdatePromptViewModel("9.9.9", null) };
        view.Show();
        Pump();

        Assert.True(view.IsVisible);
        view.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_add_download_window_loads()
    {
        Localizer.Instance.Load("en");
        var config = Config.New();

        var view = new AddDownloadItemView { DataContext = new AddDownloadItemViewModel(config, url: "https://10.255.255.1/file.zip") };
        view.Show();
        Pump();

        Assert.True(view.IsVisible);
        view.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Closing_a_dialog_with_escape_does_not_throw()
    {
        Localizer.Instance.Load("en");
        var (_, manager, _) = BuildShell();
        var row = AddRow(manager, "movie.mkv", DownloadStatus.Stopped);

        var view = new DownloadDetailsView { DataContext = new DownloadDetailsViewModel(row) };
        view.Show();
        Pump();

        // With WindowDecorations=None there is no native close-on-Esc, so each dialog handles the key
        // itself; a missing handler leaves the user with no keyboard way out.
        view.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None,
            Avalonia.Input.PhysicalKey.Escape, string.Empty);
        Pump();

        Assert.False(view.IsVisible);
    }
}
