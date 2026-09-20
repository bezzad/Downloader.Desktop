using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Downloader.Desktop.Models;

namespace Downloader.Desktop.Services;

/// <summary>
/// Decides what the main window's remembered layout means — pure, so every branch (multi-monitor, a
/// disconnected screen, a shrunken desktop, a corrupt record) is unit-testable on any OS.
///
/// <para>Units, because mixing them is the classic trap here: <c>Window.Width/Height</c> are DIPs while
/// <c>Window.Position</c> and <c>Screen.WorkingArea</c> are device pixels. This class keeps sizes in DIPs
/// and positions/areas in device pixels, converting with the window's render <c>scale</c> where the two
/// have to meet.</para>
///
/// <para>The caller (<c>MainViewModel</c>) only reads the screens and assigns the result; no decision
/// lives next to a live <c>Window</c>.</para>
/// </summary>
public static class WindowLayoutPolicy
{
    /// <summary>How much of the window's title strip must stay on a screen for the window to be
    /// reachable: enough to grab and drag it back. Device pixels.</summary>
    public const int MinVisibleWidth = 96;
    public const int TitleStripHeight = 24;

    /// <summary>What to apply to the window. A null <see cref="Position"/> means "don't place it" —
    /// the window's own <c>WindowStartupLocation</c> decides.</summary>
    public readonly record struct Resolution(bool IsMaximized, double Width, double Height, PixelPoint? Position);

    /// <summary>
    /// Records the window's current state. Only a NORMAL window contributes geometry: while maximized
    /// (or minimized, or full-screen) the window's size and position describe a frame the user never
    /// chose, and overwriting the remembered normal bounds with it would break "restore down".
    /// </summary>
    /// <param name="previous">The layout remembered so far; may be null on the very first capture.</param>
    public static WindowLayout Capture(
        WindowState state, PixelPoint position, double width, double height, WindowLayout previous)
    {
        if (state == WindowState.Normal)
            return new WindowLayout
            {
                IsMaximized = false,
                Width = width,
                Height = height,
                X = position.X,
                Y = position.Y,
            };

        if (state == WindowState.Maximized)
            return new WindowLayout
            {
                IsMaximized = true,
                // Keep the normal bounds the user last had. With nothing remembered yet (the window was
                // maximized before it was ever moved) the current frame is the only geometry there is.
                Width = previous?.Width ?? width,
                Height = previous?.Height ?? height,
                X = previous?.X ?? position.X,
                Y = previous?.Y ?? position.Y,
            };

        // Minimized / full-screen: nothing about them is worth remembering, and a minimized state must
        // never be recorded — the app would otherwise reopen into a window the user cannot see.
        return previous;
    }

    /// <summary>
    /// Turns a remembered layout into something safe to apply on THIS machine, right now: clamped to the
    /// window's minimum size, to the screens currently connected, and to a position where the title bar
    /// can still be grabbed.
    /// </summary>
    /// <param name="saved">The remembered layout; null (or unusable) falls back to the defaults.</param>
    /// <param name="workingAreas">Every connected screen's working area, device px. The first is treated
    /// as primary for re-centring. Empty ⇒ nothing is known, so only the minimum size is enforced.</param>
    /// <param name="scale">The window's render scaling (DIP → device px).</param>
    public static Resolution Resolve(
        WindowLayout saved,
        IReadOnlyList<PixelRect> workingAreas,
        double minWidth, double minHeight,
        double fallbackWidth, double fallbackHeight,
        double scale = 1.0)
    {
        if (scale <= 0 || !double.IsFinite(scale))
            scale = 1.0;

        var maximized = saved?.IsMaximized ?? false;

        // An unusable size is not a reason to forget that the window was maximized — that flag is stored
        // separately and can still be honoured.
        if (!IsUsableSize(saved))
            return new Resolution(maximized, fallbackWidth, fallbackHeight, null);

        var (width, height) = ClampSize(saved.Width, saved.Height, workingAreas, minWidth, minHeight, scale);
        var position = ResolvePosition(saved, workingAreas, width, height, scale);
        return new Resolution(maximized, width, height, position);
    }

    /// <summary>
    /// Whether an incoming resize/move is the platform echoing the layout we just applied rather than
    /// the user re-arranging the window.
    ///
    /// <para>A restore is confirmed ASYNCHRONOUSLY: measured on a real Ubuntu/Wayland session, the
    /// first event back carries the NEW position with the size the window had BEFORE the restore, and
    /// recording that pair replaces the remembered size with the old one — the window then reopens at
    /// the wrong size, which is what "it doesn't keep my size" looks like.</para>
    ///
    /// <para>The echo is identified by its SIZE, so a genuine resize — even one made immediately after
    /// launch — is still recorded; the time limit is only a backstop, so a window manager that refuses
    /// our size cannot leave the app deaf to the user for ever.</para>
    /// </summary>
    public static bool IsRestoreEcho(Size current, Size? preRestore, TimeSpan sinceApplied, TimeSpan grace) =>
        preRestore is { } stale && current == stale && sinceApplied >= TimeSpan.Zero && sinceApplied < grace;

    private static bool IsUsableSize(WindowLayout saved) =>
        saved != null &&
        double.IsFinite(saved.Width) && saved.Width > 0 &&
        double.IsFinite(saved.Height) && saved.Height > 0;

    private static (double Width, double Height) ClampSize(
        double width, double height,
        IReadOnlyList<PixelRect> areas,
        double minWidth, double minHeight, double scale)
    {
        if (!double.IsFinite(minWidth) || minWidth < 0) minWidth = 0;
        if (!double.IsFinite(minHeight) || minHeight < 0) minHeight = 0;

        // The desktop may have shrunk since the layout was saved (a smaller screen, a different
        // resolution), so cap at the biggest working area available — but never below the minimum,
        // which the window enforces anyway.
        if (areas is { Count: > 0 })
        {
            double maxWidth = 0, maxHeight = 0;
            foreach (var area in areas)
            {
                maxWidth = Math.Max(maxWidth, area.Width / scale);
                maxHeight = Math.Max(maxHeight, area.Height / scale);
            }
            width = Math.Min(width, Math.Max(minWidth, maxWidth));
            height = Math.Min(height, Math.Max(minHeight, maxHeight));
        }

        return (Math.Max(width, minWidth), Math.Max(height, minHeight));
    }

    private static PixelPoint? ResolvePosition(
        WindowLayout saved, IReadOnlyList<PixelRect> areas, double width, double height, double scale)
    {
        if (!double.IsFinite(saved.X) || !double.IsFinite(saved.Y))
            return null;
        if (areas is not { Count: > 0 })
            return null;   // nothing to check against — let the window place itself

        var x = (int)Math.Round(saved.X);
        var y = (int)Math.Round(saved.Y);
        var widthPx = Math.Max(1, (int)Math.Round(width * scale));
        var heightPx = Math.Max(1, (int)Math.Round(height * scale));

        // A window may sit partly off a screen on purpose; what must never happen is that no part of its
        // title strip is grabbable, because then it cannot be dragged back.
        var strip = new PixelRect(x, y, widthPx, Math.Min(heightPx, TitleStripHeight));
        foreach (var area in areas)
        {
            var overlap = area.Intersect(strip);
            if (overlap.Width >= Math.Min(MinVisibleWidth, widthPx) && overlap.Height >= Math.Min(TitleStripHeight, heightPx))
                return new PixelPoint(x, y);
        }

        // The screen it was on is gone (or moved): put it back where it can be seen.
        return Centre(areas[0], widthPx, heightPx);
    }

    private static PixelPoint Centre(PixelRect area, int widthPx, int heightPx) =>
        new(area.X + Math.Max(0, (area.Width - widthPx) / 2),
            area.Y + Math.Max(0, (area.Height - heightPx) / 2));
}
