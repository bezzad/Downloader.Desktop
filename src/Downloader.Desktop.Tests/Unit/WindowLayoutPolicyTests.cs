using System;
using Avalonia;
using Avalonia.Controls;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// What a remembered main-window layout means when it is applied (issue #15).
///
/// The failure that matters here is not "the size came back slightly wrong" — it is a window that
/// opens where no screen exists any more, or smaller than its own minimum, leaving the user with
/// nothing to click. That decision is deliberately a pure function over plain rectangles so the
/// multi-monitor and shrunken-desktop branches are exercised on every OS, not just whichever one
/// happens to have two screens plugged in.
/// </summary>
public class WindowLayoutPolicyTests
{
    private const double MinWidth = 840, MinHeight = 500;
    private const double FallbackWidth = 1000, FallbackHeight = 620;

    private static readonly PixelRect Primary = new(0, 0, 1920, 1040);

    private static WindowLayoutPolicy.Resolution Resolve(
        WindowLayout saved, double scale = 1.0, params PixelRect[] areas) =>
        WindowLayoutPolicy.Resolve(saved, areas, MinWidth, MinHeight, FallbackWidth, FallbackHeight, scale);

    private static WindowLayout Saved(double x, double y, double w = 1200, double h = 700, bool max = false) =>
        new() { X = x, Y = y, Width = w, Height = h, IsMaximized = max };

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Nothing_remembered_means_the_built_in_defaults()
    {
        var result = Resolve(null, 1.0, Primary);

        Assert.False(result.IsMaximized);
        Assert.Equal(FallbackWidth, result.Width);
        Assert.Equal(FallbackHeight, result.Height);
        Assert.Null(result.Position);   // let WindowStartupLocation centre it, as on a first run
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_layout_that_still_fits_is_applied_unchanged()
    {
        var result = Resolve(Saved(220, 140), 1.0, Primary);

        Assert.Equal(1200, result.Width);
        Assert.Equal(700, result.Height);
        Assert.Equal(new PixelPoint(220, 140), result.Position);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Maximized_is_carried_through_with_the_normal_bounds_intact()
    {
        // Both facts must survive: the window reopens maximized AND restoring down lands where the
        // user left it, which is the whole reason the normal geometry is stored separately.
        var result = Resolve(Saved(220, 140, max: true), 1.0, Primary);

        Assert.True(result.IsMaximized);
        Assert.Equal(1200, result.Width);
        Assert.Equal(700, result.Height);
        Assert.Equal(new PixelPoint(220, 140), result.Position);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_window_saved_on_a_monitor_that_is_now_gone_comes_back_to_the_primary()
    {
        // Saved while docked to a second monitor to the RIGHT; the laptop is now on its own.
        var result = Resolve(Saved(2400, 300), 1.0, Primary);

        Assert.True(result.Position.HasValue);
        var position = result.Position.Value;
        Assert.True(Primary.Contains(position), $"expected a position on the primary screen, got {position}");
        Assert.Equal(new PixelPoint((1920 - 1200) / 2, (1040 - 700) / 2), position);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_monitor_to_the_left_of_the_primary_is_a_real_place_to_be()
    {
        // Negative coordinates are ordinary on a multi-monitor desktop. A naive Math.Max(0, x) clamp
        // would drag this window back onto the primary for no reason.
        var left = new PixelRect(-1920, 0, 1920, 1040);

        var result = Resolve(Saved(-1700, 200), 1.0, Primary, left);

        Assert.Equal(new PixelPoint(-1700, 200), result.Position);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_window_hanging_off_an_edge_keeps_its_place_while_its_title_bar_is_reachable()
    {
        // Deliberate placements like this are common; only an ungrabbable window is a problem.
        var result = Resolve(Saved(1800, 90), 1.0, Primary);

        Assert.Equal(new PixelPoint(1800, 90), result.Position);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_window_whose_title_bar_is_off_every_screen_is_brought_back()
    {
        // Only a sliver of the left edge overlaps — too little to grab and drag back.
        var result = Resolve(Saved(1900, 500), 1.0, Primary);

        Assert.Equal(new PixelPoint((1920 - 1200) / 2, (1040 - 700) / 2), result.Position);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_window_pushed_above_the_top_of_the_desktop_is_brought_back()
    {
        var result = Resolve(Saved(300, -400), 1.0, Primary);

        Assert.Equal(new PixelPoint((1920 - 1200) / 2, (1040 - 700) / 2), result.Position);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_size_bigger_than_the_desktop_is_trimmed_to_fit()
    {
        var small = new PixelRect(0, 0, 1280, 720);

        var result = Resolve(Saved(0, 0, 2560, 1400), 1.0, small);

        Assert.Equal(1280, result.Width);
        Assert.Equal(720, result.Height);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_high_dpi_screen_is_measured_in_the_same_units_as_the_window()
    {
        // 2560x1440 device px at 200% is a 1280x720 DIP desktop — measuring the window's DIP size
        // against raw device pixels would wrongly conclude a 1600 DIP window fits.
        var hidpi = new PixelRect(0, 0, 2560, 1440);

        var result = Resolve(Saved(0, 0, 1600, 900), 2.0, hidpi);

        Assert.Equal(1280, result.Width);
        Assert.Equal(720, result.Height);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_size_below_the_windows_minimum_is_raised_to_it()
    {
        var result = Resolve(Saved(100, 100, 300, 200), 1.0, Primary);

        Assert.Equal(MinWidth, result.Width);
        Assert.Equal(MinHeight, result.Height);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_desktop_smaller_than_the_windows_minimum_still_yields_a_usable_window()
    {
        var tiny = new PixelRect(0, 0, 640, 400);

        var result = Resolve(Saved(0, 0, 1200, 700), 1.0, tiny);

        // The minimum wins: a window below MinWidth/MinHeight is what the window itself refuses anyway.
        Assert.Equal(MinWidth, result.Width);
        Assert.Equal(MinHeight, result.Height);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(0d, 0d)]
    [InlineData(-10d, 700d)]
    [InlineData(double.NaN, 700d)]
    [InlineData(double.PositiveInfinity, 700d)]
    [InlineData(1200d, double.NaN)]
    public void A_corrupt_size_falls_back_to_the_defaults(double width, double height)
    {
        var result = Resolve(Saved(200, 200, width, height), 1.0, Primary);

        Assert.Equal(FallbackWidth, result.Width);
        Assert.Equal(FallbackHeight, result.Height);
        Assert.Null(result.Position);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_corrupt_size_still_reopens_maximized()
    {
        // IsMaximized is stored separately from the geometry, so a bad number should not cost the user
        // the one thing issue #15 is actually about.
        var result = Resolve(Saved(200, 200, 0, 0, max: true), 1.0, Primary);

        Assert.True(result.IsMaximized);
        Assert.Equal(FallbackWidth, result.Width);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(double.NaN, 100d)]
    [InlineData(100d, double.NaN)]
    public void A_corrupt_position_leaves_the_window_to_place_itself(double x, double y)
    {
        var result = Resolve(Saved(x, y), 1.0, Primary);

        Assert.Null(result.Position);
        Assert.Equal(1200, result.Width);   // the size is still perfectly good
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void With_no_screens_known_only_the_minimum_size_is_enforced()
    {
        // Headless, and some Linux sessions, report no screens. Guessing a position there would be
        // worse than letting the window place itself.
        var result = Resolve(Saved(300, 300, 400, 300));

        Assert.Equal(MinWidth, result.Width);
        Assert.Equal(MinHeight, result.Height);
        Assert.Null(result.Position);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_normal_window_is_captured_whole()
    {
        var captured = WindowLayoutPolicy.Capture(
            WindowState.Normal, new PixelPoint(120, 80), 1100, 640, previous: null);

        Assert.False(captured.IsMaximized);
        Assert.Equal(1100, captured.Width);
        Assert.Equal(640, captured.Height);
        Assert.Equal(120, captured.X);
        Assert.Equal(80, captured.Y);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Maximizing_does_not_overwrite_the_normal_bounds()
    {
        var normal = WindowLayoutPolicy.Capture(
            WindowState.Normal, new PixelPoint(120, 80), 1100, 640, previous: null);

        // The maximized frame fills the screen; storing it would make "restore down" useless after a
        // restart, which is the subtle half of this feature.
        var captured = WindowLayoutPolicy.Capture(
            WindowState.Maximized, new PixelPoint(0, 0), 1920, 1040, previous: normal);

        Assert.True(captured.IsMaximized);
        Assert.Equal(1100, captured.Width);
        Assert.Equal(640, captured.Height);
        Assert.Equal(120, captured.X);
        Assert.Equal(80, captured.Y);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Maximized_with_nothing_remembered_yet_keeps_the_only_geometry_there_is()
    {
        var captured = WindowLayoutPolicy.Capture(
            WindowState.Maximized, new PixelPoint(0, 0), 1920, 1040, previous: null);

        Assert.True(captured.IsMaximized);
        Assert.Equal(1920, captured.Width);
        Assert.Equal(1040, captured.Height);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(WindowState.Minimized)]
    [InlineData(WindowState.FullScreen)]
    public void Minimizing_changes_nothing_about_what_is_remembered(WindowState state)
    {
        var previous = WindowLayoutPolicy.Capture(
            WindowState.Maximized, new PixelPoint(0, 0), 1920, 1040,
            previous: new WindowLayout { Width = 1100, Height = 640, X = 120, Y = 80 });

        var captured = WindowLayoutPolicy.Capture(state, new PixelPoint(-32000, -32000), 160, 40, previous);

        // A minimized window's own geometry is meaningless (Windows parks it off-screen), and recording
        // it would reopen the app into something the user cannot see.
        Assert.Same(previous, captured);
        Assert.True(captured.IsMaximized);
        Assert.Equal(1100, captured.Width);
    }
}
