using System;
using Avalonia.Controls;
using Avalonia.Input;

namespace Downloader.Desktop.Views;

/// <summary>
/// Transparent edge/corner overlay that restores native window resizing on a borderless window
/// (<c>SystemDecorations="None"</c>) via <see cref="Window.BeginResizeDrag"/>. Drop it as the last
/// child of a resizable window's root panel so it sits on top; its center passes clicks through.
/// </summary>
public partial class ResizeGrips : UserControl
{
    public ResizeGrips()
    {
        InitializeComponent();
    }

    private void OnPressed(object sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (TopLevel.GetTopLevel(this) is Window { CanResize: true, WindowState: WindowState.Normal } w
            && sender is Control { Tag: string tag }
            && Enum.TryParse<WindowEdge>(tag, out var edge))
        {
            w.BeginResizeDrag(edge, e);
        }
    }
}
