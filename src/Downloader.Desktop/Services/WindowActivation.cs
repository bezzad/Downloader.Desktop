using Avalonia.Controls;
using Avalonia.Threading;

namespace Downloader.Desktop.Services;

/// <summary>
/// The one way to surface the main window from the tray / a second-instance launch (#6). Safe from any
/// thread. Show and Activate run in SEPARATE dispatcher ticks: right after Show() the native window may
/// not be mapped yet, so an immediate Activate/topmost-flip silently no-ops and the window comes up
/// BEHIND everything — the "first click lights the taskbar but shows no window, second click works"
/// symptom. Deferring the activation to the next tick lets the platform map the window first.
/// </summary>
public static class WindowActivation
{
    public static void BringToFront(Window window)
    {
        if (window == null)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            window.Show();
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;

            Dispatcher.UIThread.Post(() =>
            {
                window.Activate();
                // A brief topmost flip nudges the window to the foreground across window managers.
                window.Topmost = true;
                window.Topmost = false;
            }, DispatcherPriority.Background);
        });
    }
}
