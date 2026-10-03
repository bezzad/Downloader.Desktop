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

    /// <summary>Shows the menu in the corner by the tray on the screen the pointer is likely on.</summary>
    public void ShowNearTray()
    {
        _vm?.Refresh();
        Show();
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
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
