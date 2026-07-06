using System;
using System.Linq;
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

    // Drag-start snapshot (captured once in OnPressed) so every frame is computed from a fixed anchor
    // rather than the window's moving bounds — the fix for West/North resizes walking the window off-screen.
    private PixelPoint _startPointer;
    private PixelPoint _startPos;
    private double _startWidth;
    private double _startHeight;

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
            _startPointer = w.PointToScreen(e.GetPosition(w)); // screen/device pixels, stable as the window moves
            _startPos = w.Position;
            _startWidth = w.Bounds.Width;
            _startHeight = w.Bounds.Height;
            e.Pointer.Capture(grip);
            e.Handled = true;
        }
    }

    private void OnMoved(object sender, PointerEventArgs e)
    {
        if (!_resizing || _window is null)
            return;

        var pointer = _window.PointToScreen(e.GetPosition(_window)); // current pointer in screen/device pixels
        var scale = _window.RenderScaling;

        double minW = _window.MinWidth > 0 ? _window.MinWidth : 1;
        double minH = _window.MinHeight > 0 ? _window.MinHeight : 1;
        double maxW = double.IsFinite(_window.MaxWidth) && _window.MaxWidth > 0 ? _window.MaxWidth : double.MaxValue;
        double maxH = double.IsFinite(_window.MaxHeight) && _window.MaxHeight > 0 ? _window.MaxHeight : double.MaxValue;

        var r = WindowResize.Compute(_edge, _startPointer, _startPos, _startWidth, _startHeight,
            pointer, scale, minW, minH, maxW, maxH);

        // Last-resort off-screen guard, so a fast drag can never lose the window entirely.
        var areas = _window.Screens?.All?.Select(s => s.WorkingArea).ToArray() ?? Array.Empty<PixelRect>();
        var pos = WindowResize.ClampOnScreen(r.Position, (int)Math.Round(r.Width * scale),
            (int)Math.Round(r.Height * scale), areas);

        _window.Position = pos;
        _window.Width = r.Width;
        _window.Height = r.Height;
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
