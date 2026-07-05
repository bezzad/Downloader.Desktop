using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests;

public class DialogHelperTests
{
    [AvaloniaFact]
    public void ApplyPersistedSize_clamps_below_minimum_up_to_minimum()
    {
        var config = Config.New();
        config.WindowSizes[DialogHelper.AddDownloadWindowKey] = new WindowSize { Width = 100, Height = 50 };

        var window = new Window { MinWidth = 480, MinHeight = 360 };
        DialogHelper.ApplyPersistedSize(window, DialogHelper.AddDownloadWindowKey, config);

        Assert.Equal(480, window.Width);
        Assert.Equal(360, window.Height);
    }

    [AvaloniaFact]
    public void ApplyPersistedSize_clamps_above_screen_working_area_down_to_it()
    {
        var config = Config.New();
        config.WindowSizes[DialogHelper.AddDownloadWindowKey] = new WindowSize { Width = 100_000, Height = 100_000 };

        var window = new Window { MinWidth = 480, MinHeight = 360 };
        DialogHelper.ApplyPersistedSize(window, DialogHelper.AddDownloadWindowKey, config);

        var screen = window.Screens?.ScreenFromWindow(window) ?? window.Screens?.Primary;
        Assert.NotNull(screen);
        Assert.True(window.Width <= screen.WorkingArea.Width);
        Assert.True(window.Height <= screen.WorkingArea.Height);
    }

    [AvaloniaFact]
    public void ApplyPersistedSize_is_noop_when_no_saved_size()
    {
        var config = Config.New();
        var window = new Window { MinWidth = 480, MinHeight = 360, Width = 560, Height = 460 };

        DialogHelper.ApplyPersistedSize(window, DialogHelper.AddDownloadWindowKey, config);

        Assert.Equal(560, window.Width);
        Assert.Equal(460, window.Height);
    }

    [AvaloniaFact]
    public void SavePersistedSize_writes_current_size_under_the_given_key()
    {
        var config = Config.New();
        var window = new Window { Width = 700, Height = 500 };

        DialogHelper.SavePersistedSize(window, DialogHelper.DetailsWindowKey, config);

        Assert.True(config.WindowSizes.ContainsKey(DialogHelper.DetailsWindowKey));
        Assert.Equal(700, config.WindowSizes[DialogHelper.DetailsWindowKey].Width);
        Assert.Equal(500, config.WindowSizes[DialogHelper.DetailsWindowKey].Height);
    }

    [AvaloniaFact]
    public void PageDialog_key_is_shared_across_settings_and_queues()
    {
        // PageDialogView hosts Queues/Scheduler/Settings; all three share one remembered size
        // (DialogHelper.PageDialogWindowKey), never one key per page.
        var config = Config.New();
        var settingsWindow = new Window { Width = 900, Height = 640 };
        DialogHelper.SavePersistedSize(settingsWindow, DialogHelper.PageDialogWindowKey, config);

        var queuesWindow = new Window { MinWidth = 560, MinHeight = 420 };
        DialogHelper.ApplyPersistedSize(queuesWindow, DialogHelper.PageDialogWindowKey, config);

        Assert.Equal(900, queuesWindow.Width);
        Assert.Equal(640, queuesWindow.Height);
    }
}
