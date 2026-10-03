using Avalonia;
using Downloader.Desktop.Views;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>Where the tray menu goes: the corner by the taskbar, found from where the working area is
/// inset from the screen.</summary>
public class TrayMenuPlacementTests
{
    private static readonly PixelRect Screen = new(0, 0, 1920, 1080);
    private static readonly PixelSize Menu = new(240, 200);
    private const int M = TrayMenuPlacement.Margin;

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_bottom_taskbar_puts_the_menu_bottom_right()
    {
        // Windows default: a 48 px taskbar at the bottom.
        var at = TrayMenuPlacement.Place(Screen, new PixelRect(0, 0, 1920, 1032), Menu, rtl: false);
        Assert.Equal(new PixelPoint(1920 - 240 - M, 1032 - 200 - M), at);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_top_bar_puts_the_menu_top_right()
    {
        // GNOME: a 32 px top bar.
        var at = TrayMenuPlacement.Place(Screen, new PixelRect(0, 32, 1920, 1048), Menu, rtl: false);
        Assert.Equal(new PixelPoint(1920 - 240 - M, 32 + M), at);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_left_taskbar_puts_the_menu_low_on_the_left()
    {
        var at = TrayMenuPlacement.Place(Screen, new PixelRect(64, 0, 1856, 1080), Menu, rtl: false);
        Assert.Equal(new PixelPoint(64 + M, 1080 - 200 - M), at);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_right_taskbar_puts_the_menu_low_on_the_right()
    {
        var at = TrayMenuPlacement.Place(Screen, new PixelRect(0, 0, 1856, 1080), Menu, rtl: false);
        Assert.Equal(new PixelPoint(1856 - 240 - M, 1080 - 200 - M), at);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void No_inset_falls_back_to_bottom_right()
    {
        var at = TrayMenuPlacement.Place(Screen, Screen, Menu, rtl: false);
        Assert.Equal(new PixelPoint(1920 - 240 - M, 1080 - 200 - M), at);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Right_to_left_puts_it_at_the_start_end()
    {
        var bottom = TrayMenuPlacement.Place(Screen, new PixelRect(0, 0, 1920, 1032), Menu, rtl: true);
        Assert.Equal(new PixelPoint(M, 1032 - 200 - M), bottom);
        var top = TrayMenuPlacement.Place(Screen, new PixelRect(0, 32, 1920, 1048), Menu, rtl: true);
        Assert.Equal(new PixelPoint(M, 32 + M), top);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_second_monitor_is_placed_in_its_own_coordinates()
    {
        var second = new PixelRect(1920, 0, 2560, 1440);
        var at = TrayMenuPlacement.Place(second, new PixelRect(1920, 0, 2560, 1392), Menu, rtl: false);
        Assert.Equal(new PixelPoint(1920 + 2560 - 240 - M, 1392 - 200 - M), at);
    }
}
