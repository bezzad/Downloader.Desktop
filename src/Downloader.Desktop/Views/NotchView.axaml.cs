using System;
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
/// </summary>
public partial class NotchView : Window
{
    private const double CollapsedWidth = 170, CollapsedHeight = 34;
    private const double ExpandedWidth = 380, ExpandedHeight = 190;

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

        Opened += (_, _) => Reposition();
    }

    private void OnPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            TrayService.ShowWindow(); // marshals + activates the main window
    }

    private void SetExpanded(bool expanded)
    {
        if (DataContext is NotchViewModel vm)
            vm.IsExpanded = expanded;
        Width = expanded ? ExpandedWidth : CollapsedWidth;
        Height = expanded ? ExpandedHeight : CollapsedHeight;
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
        var x = screen.WorkingArea.X + (screen.WorkingArea.Width - widthPx) / 2;
        // Y = the top of the SCREEN (not the working area): on macOS the pill tucks under the
        // menu-bar/notch line; on Win/Linux it hugs the very top edge of the desktop.
        Position = new PixelPoint(x, screen.Bounds.Y);
    }
}
