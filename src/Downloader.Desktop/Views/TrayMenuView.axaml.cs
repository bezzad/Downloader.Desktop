using System;
using Avalonia.Controls;
using Avalonia.Input;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Views;

/// <summary>
/// The app-drawn tray menu (Linux/Windows). Closes when it loses focus, on Esc, or after an item is
/// chosen — it hides rather than closes, so one instance is reused.
/// </summary>
public partial class TrayMenuView : Window
{
    private TrayMenuViewModel _vm;

    public TrayMenuView()
    {
        InitializeComponent();
        Deactivated += (_, _) => Hide();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
                _vm.Done -= Hide;
            _vm = DataContext as TrayMenuViewModel;
            if (_vm != null)
                _vm.Done += Hide;
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Hide();
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>Shows the menu in the corner by the tray. The tray sits on the PRIMARY screen (the taskbar /
    /// top bar's home); the screen this hidden window last landed on says nothing about where the icon is,
    /// and asking it opened the menu on a second monitor.</summary>
    public void ShowNearTray()
    {
        _vm?.Refresh();
        Show();
        var screen = Screens.Primary ?? Screens.ScreenFromWindow(this);
        if (screen != null)
        {
            var size = new Avalonia.PixelSize(
                (int)Math.Ceiling(Bounds.Width * screen.Scaling),
                (int)Math.Ceiling(Bounds.Height * screen.Scaling));
            Position = TrayMenuPlacement.Place(screen.Bounds, screen.WorkingArea, size,
                FlowDirection == Avalonia.Media.FlowDirection.RightToLeft);
        }
        Activate();
    }
}
