using System;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;

namespace Downloader.Desktop.Services;

/// <summary>
/// Owns the notch overlay window ("dynamic island"). Fail-soft like <see cref="TrayService"/>:
/// a platform where the borderless topmost window can't be created just logs and stays inactive.
/// Runs independently of the main window, so it stays up while the app is hidden in the tray.
/// </summary>
public static class NotchService
{
    private static NotchView _view;
    private static NotchViewModel _vm;

    public static bool IsActive => _view != null;

    public static void Start(IDownloadManager manager)
    {
        if (IsActive)
            return;
        try
        {
            _vm = new NotchViewModel(manager);
            _view = new NotchView { DataContext = _vm };
            _view.Show();
            _view.Reposition();
        }
        catch (Exception ex)
        {
            AppLog.Error("Notch overlay could not start", ex);
            Stop();
        }
    }

    public static void Stop()
    {
        try { _view?.Close(); } catch { /* already closed */ }
        _vm?.Dispose();
        _view = null;
        _vm = null;
    }
}
