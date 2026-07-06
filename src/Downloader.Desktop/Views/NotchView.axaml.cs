using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Views;

/// <summary>
/// The notch overlay window ("dynamic island"): a slim top-center pill that expands on hover into a
/// compact live-downloads panel. Never takes focus; clicking it surfaces the main window.
/// On macOS the NSWindow level is raised above the menu bar so the pill sits AT the physical notch
/// (the webcam housing) like boring.notch/NotchNook — a normal window would be clamped below the bar.
/// </summary>
public partial class NotchView : Window
{
    // macOS: the collapsed pill is WIDER than the physical notch so the info wings (logo+speed left,
    // percent+clock right) stay visible beside the webcam housing — content centered under the housing
    // would be hidden behind it (author-reported "empty rectangle").
    private static double CollapsedWidth => OperatingSystem.IsMacOS() ? 360 : 230;
    private const double CollapsedHeight = 30;
    internal const double ExpandedWidth = 400;

    // Expanded height, sized to its actual content instead of a fixed guess (was a flat 210, ~1.5x its
    // content). Kept as named constants matching NotchView.axaml's metrics so the relationship between
    // "3 rows" and the window height stays traceable and won't silently drift if a row's content changes.
    // Internal (not private) so the gated mockup-capture test resizes to the real computed value instead
    // of a second hardcoded number that could drift out of sync with this one.
    private const double ExpandedVerticalPadding = 24; // StackPanel Margin="18 10 18 14": top 10 + bottom 14
    private const double HeaderRowHeight = 22;         // clock/percent/speed header Grid
    private const double HeaderToListSpacing = 10;     // StackPanel Spacing between the header and the row list
    private const double RowHeight = 32;               // per row: 7 top margin + ~16 name line + 5 spacing + 4 progress bar
    private const double OverflowFooterHeight = 30;    // "and N more" line + its Spacing, reserved only when it shows
    internal static readonly double ExpandedHeight =
        ExpandedVerticalPadding + HeaderRowHeight + HeaderToListSpacing + RowHeight * NotchViewModel.MaxRows;

    private readonly DispatcherTimer _collapseDelay;

    public NotchView()
    {
        InitializeComponent();

        // A short grace period so skimming the pointer past the pill doesn't flicker it open/closed.
        _collapseDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _collapseDelay.Tick += (_, _) => { _collapseDelay.Stop(); SetExpanded(false); };

        PointerEntered += (_, _) => { _collapseDelay.Stop(); SetExpanded(true); };
        PointerExited += (_, _) => _collapseDelay.Start();
        PointerPressed += OnPressed;

        Opened += (_, _) =>
        {
            ElevateAboveMenuBarOnMac();
            Width = CollapsedWidth;
            Reposition();
        };
    }

    // ---- macOS: raise the NSWindow above the menu bar so it can occupy the notch strip ----

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendLong(IntPtr receiver, IntPtr selector, long arg);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendULong(IntPtr receiver, IntPtr selector, ulong arg);

    private void ElevateAboveMenuBarOnMac()
    {
        if (!OperatingSystem.IsMacOS())
            return;
        try
        {
            var nsWindow = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (nsWindow == IntPtr.Zero)
                return;
            // NSStatusWindowLevel(25)+1: above the menu bar, so the pill hugs the physical notch.
            ObjcMsgSendLong(nsWindow, SelRegisterName("setLevel:"), 26);
            // canJoinAllSpaces(1) | stationary(16) | fullScreenAuxiliary(256): visible on every space,
            // unaffected by Mission Control, allowed next to fullscreen apps.
            ObjcMsgSendULong(nsWindow, SelRegisterName("setCollectionBehavior:"), 1UL | 16UL | 256UL);
        }
        catch (Exception ex)
        {
            AppLog.Error("Notch: could not raise the macOS window level", ex);
        }
    }

    private void OnPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            TrayService.ShowWindow(); // marshals + activates the main window
    }

    private void SetExpanded(bool expanded)
    {
        var vm = DataContext as NotchViewModel;
        if (vm != null)
            vm.IsExpanded = expanded;
        Width = expanded ? ExpandedWidth : CollapsedWidth;
        // Reserve room for the "and N more" footer only when it's actually shown, so ≤3 rows still hug their content.
        Height = expanded
            ? ExpandedHeight + (vm?.HasOverflow == true ? OverflowFooterHeight : 0)
            : CollapsedHeight;
        Reposition();
    }

    /// <summary>Docks the window horizontally centered at the very top of the primary screen.</summary>
    internal void Reposition()
    {
        var screen = Screens?.Primary ?? Screens?.ScreenFromWindow(this);
        if (screen == null)
            return;
        var scale = screen.Scaling <= 0 ? 1 : screen.Scaling;
        var widthPx = (int)Math.Round(Width * scale);
        // Center on the FULL screen bounds (the physical notch/webcam sits at the hardware center),
        // and Y = the very top of the screen: with the raised macOS window level the pill overlaps the
        // menu-bar strip and merges with the notch; on Win/Linux it hugs the top edge of the desktop.
        var x = screen.Bounds.X + (screen.Bounds.Width - widthPx) / 2;
        Position = new PixelPoint(x, screen.Bounds.Y);
    }
}
