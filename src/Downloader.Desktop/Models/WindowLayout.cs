namespace Downloader.Desktop.Models;

/// <summary>
/// The main window's remembered layout: whether it was maximized, plus the size and position it had
/// while NORMAL (so "restore down" after a restart lands where the user left it).
/// <para>A null <see cref="Config.MainWindow"/> means "nothing remembered" — the window then opens at
/// the defaults declared in MainWindow.axaml. Values are plain doubles so the record round-trips
/// through <c>config.json</c> without any Avalonia type.</para>
/// </summary>
public class WindowLayout
{
    public bool IsMaximized { get; set; }

    /// <summary>Normal-state width. Never the maximized frame's width.</summary>
    public double Width { get; set; }

    /// <summary>Normal-state height. Never the maximized frame's height.</summary>
    public double Height { get; set; }

    /// <summary>Normal-state left edge, in screen pixels (can be negative on a monitor left of the primary).</summary>
    public double X { get; set; }

    /// <summary>Normal-state top edge, in screen pixels.</summary>
    public double Y { get; set; }
}
