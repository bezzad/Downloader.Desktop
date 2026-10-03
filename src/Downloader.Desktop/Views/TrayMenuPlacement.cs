using Avalonia;

namespace Downloader.Desktop.Views;

/// <summary>
/// Where the tray menu goes on a screen. Avalonia does not report the tray icon's own position, so the
/// honest target is "the corner by the taskbar": the side where the working area is inset from the
/// screen bounds (bottom on default Windows, top on GNOME, left/right for a side taskbar), at its far
/// end (the start end in right-to-left languages). No inset → bottom-right.
/// </summary>
public static class TrayMenuPlacement
{
    /// <summary>Gap between the menu and the working-area edge, in pixels.</summary>
    public const int Margin = 8;

    public static PixelPoint Place(PixelRect bounds, PixelRect workingArea, PixelSize size, bool rtl)
    {
        var top = workingArea.Y - bounds.Y;
        var left = workingArea.X - bounds.X;
        var right = bounds.Right - workingArea.Right;

        var startX = workingArea.X + Margin;
        var endX = workingArea.Right - size.Width - Margin;
        var topY = workingArea.Y + Margin;
        var bottomY = workingArea.Bottom - size.Height - Margin;

        // A side taskbar: the menu hugs that side, low down where the tray area usually is.
        if (left > 0 && left >= right && left > top)
            return new PixelPoint(startX, bottomY);
        if (right > 0 && right > left && right > top)
            return new PixelPoint(endX, bottomY);

        var x = rtl ? startX : endX;
        return new PixelPoint(x, top > 0 ? topY : bottomY);
    }
}
