using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

public class DialogHelperTests
{
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void ApplyPersistedSize_clamps_below_minimum_up_to_minimum()
    {
        var config = Config.New();
        config.WindowSizes[DialogHelper.AddDownloadWindowKey] = new WindowSize { Width = 100, Height = 50 };

        var window = new Window { MinWidth = 480, MinHeight = 360 };
        DialogHelper.ApplyPersistedSize(window, DialogHelper.AddDownloadWindowKey, config);

        Assert.Equal(480, window.Width);
        Assert.Equal(360, window.Height);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
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

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void ApplyPersistedSize_is_noop_when_no_saved_size()
    {
        var config = Config.New();
        var window = new Window { MinWidth = 480, MinHeight = 360, Width = 560, Height = 460 };

        DialogHelper.ApplyPersistedSize(window, DialogHelper.AddDownloadWindowKey, config);

        Assert.Equal(560, window.Width);
        Assert.Equal(460, window.Height);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void SavePersistedSize_writes_current_size_under_the_given_key()
    {
        var config = Config.New();
        var window = new Window { Width = 700, Height = 500 };

        DialogHelper.SavePersistedSize(window, DialogHelper.DetailsWindowKey, config);

        Assert.True(config.WindowSizes.ContainsKey(DialogHelper.DetailsWindowKey));
        Assert.Equal(700, config.WindowSizes[DialogHelper.DetailsWindowKey].Width);
        Assert.Equal(500, config.WindowSizes[DialogHelper.DetailsWindowKey].Height);
    }

    // Regression: clicking Donate inside the About dialog opened Donate *underneath* About.
    // Both are owned by MainWindow, so the second dialog is a sibling of the first and the owner
    // raises the first back on top of it. Only one modal may be on screen at a time.
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Opening_a_modal_closes_the_modal_that_was_already_open()
    {
        DialogHelper.CloseOpenModals();

        var about = new Window();
        var aboutClosed = false;
        about.Closed += (_, _) => aboutClosed = true;
        about.Show();
        DialogHelper.BeginModal(about);

        var donate = new Window();
        donate.Show();
        DialogHelper.BeginModal(donate);

        Assert.True(aboutClosed);
        Assert.Same(donate, Assert.Single(DialogHelper.OpenModals));

        DialogHelper.CloseOpenModals();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Closing_a_modal_stops_tracking_it()
    {
        DialogHelper.CloseOpenModals();

        var view = new Window();
        view.Show();
        DialogHelper.BeginModal(view);
        Assert.Single(DialogHelper.OpenModals);

        view.Close();

        Assert.Empty(DialogHelper.OpenModals);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void CloseOpenModals_closes_every_tracked_modal()
    {
        DialogHelper.CloseOpenModals();

        var first = new Window();
        first.Show();
        DialogHelper.BeginModal(first);

        var second = new Window();
        var secondClosed = false;
        second.Closed += (_, _) => secondClosed = true;
        second.Show();
        DialogHelper.BeginModal(second);

        DialogHelper.CloseOpenModals();

        Assert.True(secondClosed);
        Assert.Empty(DialogHelper.OpenModals);
    }
}
