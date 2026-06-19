using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Downloader.Desktop.Views;

/// <summary>
/// Transparent edge/corner overlay that restores window resizing on a borderless window
/// (<c>WindowDecorations="None"</c>). Drop it as the last child of a resizable window's root panel so
/// it sits on top; its center passes clicks through.
///
/// <para>
/// Resizing is implemented manually (capture the pointer, then move/resize the window per edge)
/// instead of via <see cref="Window.BeginResizeDrag"/>, because that native call is a no-op on macOS
/// for borderless windows — the symptom being a resize cursor that appears but does nothing. The
/// manual path works the same on Windows, Linux and macOS.
/// </para>
/// </summary>
public partial class ResizeGrips : UserControl
{
    private Window _window;
    private WindowEdge _edge;
    private bool _resizing;

    public ResizeGrips()
    {
        InitializeComponent();
    }

    private void OnPressed(object sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (TopLevel.GetTopLevel(this) is Window { CanResize: true, WindowState: WindowState.Normal } w
            && sender is Control { Tag: string tag } grip
            && Enum.TryParse<WindowEdge>(tag, out var edge))
        {
            _window = w;
            _edge = edge;
            _resizing = true;
            e.Pointer.Capture(grip);
            e.Handled = true;
        }
    }

    private void OnMoved(object sender, PointerEventArgs e)
    {
        if (!_resizing || _window is null)
            return;

        // Pointer position in DIPs relative to the window's current top-left.
        var p = e.GetPosition(_window);
        var scale = _window.RenderScaling;

        bool west = _edge is WindowEdge.West or WindowEdge.NorthWest or WindowEdge.SouthWest;
        bool east = _edge is WindowEdge.East or WindowEdge.NorthEast or WindowEdge.SouthEast;
        bool north = _edge is WindowEdge.North or WindowEdge.NorthWest or WindowEdge.NorthEast;
        bool south = _edge is WindowEdge.South or WindowEdge.SouthWest or WindowEdge.SouthEast;

        double width = _window.Bounds.Width;
        double height = _window.Bounds.Height;
        int posX = _window.Position.X;
        int posY = _window.Position.Y;

        double minW = _window.MinWidth > 0 ? _window.MinWidth : 1;
        double minH = _window.MinHeight > 0 ? _window.MinHeight : 1;
        double maxW = double.IsFinite(_window.MaxWidth) && _window.MaxWidth > 0 ? _window.MaxWidth : double.MaxValue;
        double maxH = double.IsFinite(_window.MaxHeight) && _window.MaxHeight > 0 ? _window.MaxHeight : double.MaxValue;

        // East/South keep the opposite edge fixed, so the size is just the pointer offset.
        if (east)
        {
            width = Math.Clamp(p.X, minW, maxW);
        }
        else if (west)
        {
            // West moves the left edge to the pointer: grow/shrink and shift the origin to match,
            // clamped so the edge never crosses the min/max size.
            var newW = Math.Clamp(width - p.X, minW, maxW);
            posX -= (int)Math.Round((newW - width) * scale);
            width = newW;
        }

        if (south)
        {
            height = Math.Clamp(p.Y, minH, maxH);
        }
        else if (north)
        {
            var newH = Math.Clamp(height - p.Y, minH, maxH);
            posY -= (int)Math.Round((newH - height) * scale);
            height = newH;
        }

        _window.Position = new PixelPoint(posX, posY);
        _window.Width = width;
        _window.Height = height;
        e.Handled = true;
    }

    private void OnReleased(object sender, PointerReleasedEventArgs e)
    {
        if (!_resizing)
            return;

        _resizing = false;
        _window = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }
}
