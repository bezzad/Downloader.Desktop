using System;
using Avalonia;
using Avalonia.Controls;

namespace Downloader.Desktop.Views;

/// <summary>
/// Pure geometry for manual window resizing (used by <see cref="ResizeGrips"/>). Kept free of any live
/// <c>Window</c> reads so a fast multi-frame drag can be unit-tested. Everything is anchored to a single
/// drag-start snapshot (pointer, window position, window size); each frame is computed from that fixed
/// snapshot plus the current pointer's screen-space delta — NOT from the window's current (moving) bounds,
/// which is what let West/North resizes compound error and walk the window off-screen.
/// </summary>
public static class WindowResize
{
    public readonly record struct Result(PixelPoint Position, double Width, double Height);

    /// <summary>
    /// Compute the window's new position (device px) and size (DIPs) for a resize drag.
    /// </summary>
    /// <param name="startPointer">Pointer position in screen/device pixels at press time.</param>
    /// <param name="startPos">Window top-left in device pixels at press time.</param>
    /// <param name="startWidth">Window width in DIPs at press time.</param>
    /// <param name="startHeight">Window height in DIPs at press time.</param>
    /// <param name="pointer">Current pointer position in screen/device pixels.</param>
    /// <param name="scale">Window render scaling (DIP → device px factor).</param>
    public static Result Compute(
        WindowEdge edge,
        PixelPoint startPointer, PixelPoint startPos,
        double startWidth, double startHeight,
        PixelPoint pointer, double scale,
        double minW, double minH, double maxW, double maxH)
    {
        if (scale <= 0) scale = 1;

        bool west = edge is WindowEdge.West or WindowEdge.NorthWest or WindowEdge.SouthWest;
        bool east = edge is WindowEdge.East or WindowEdge.NorthEast or WindowEdge.SouthEast;
        bool north = edge is WindowEdge.North or WindowEdge.NorthWest or WindowEdge.NorthEast;
        bool south = edge is WindowEdge.South or WindowEdge.SouthWest or WindowEdge.SouthEast;

        double startWpx = startWidth * scale;
        double startHpx = startHeight * scale;
        double dx = pointer.X - startPointer.X;
        double dy = pointer.Y - startPointer.Y;

        double minWpx = minW * scale;
        double maxWpx = double.IsFinite(maxW) ? maxW * scale : double.MaxValue;
        double minHpx = minH * scale;
        double maxHpx = double.IsFinite(maxH) ? maxH * scale : double.MaxValue;

        // The edges opposite the dragged one stay pinned to their start position.
        double rightEdge = startPos.X + startWpx;
        double bottomEdge = startPos.Y + startHpx;

        int posX = startPos.X;
        int posY = startPos.Y;
        double wpx = startWpx, hpx = startHpx;

        if (east)
            wpx = Math.Clamp(startWpx + dx, minWpx, maxWpx);
        else if (west)
        {
            wpx = Math.Clamp(startWpx - dx, minWpx, maxWpx);
            posX = (int)Math.Round(rightEdge - wpx); // keep the right edge fixed after clamping
        }

        if (south)
            hpx = Math.Clamp(startHpx + dy, minHpx, maxHpx);
        else if (north)
        {
            hpx = Math.Clamp(startHpx - dy, minHpx, maxHpx);
            posY = (int)Math.Round(bottomEdge - hpx); // keep the bottom edge fixed after clamping
        }

        return new Result(new PixelPoint(posX, posY), wpx / scale, hpx / scale);
    }

    /// <summary>
    /// Last-resort guard: nudge <paramref name="pos"/> so the window (size in device px) keeps at least
    /// <paramref name="margin"/> px overlapping the union of the given screen working areas, so a resize can
    /// never leave it entirely off every screen. No-op when <paramref name="areas"/> is empty.
    /// </summary>
    public static PixelPoint ClampOnScreen(PixelPoint pos, int widthPx, int heightPx, PixelRect[] areas, int margin = 40)
    {
        if (areas is null || areas.Length == 0)
            return pos;

        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        foreach (var a in areas)
        {
            left = Math.Min(left, a.X);
            top = Math.Min(top, a.Y);
            right = Math.Max(right, a.X + a.Width);
            bottom = Math.Max(bottom, a.Y + a.Height);
        }

        int x = Math.Clamp(pos.X, left - widthPx + margin, right - margin);
        int y = Math.Clamp(pos.Y, top, bottom - margin); // never above the top; title bar must stay reachable
        return new PixelPoint(x, y);
    }
}
