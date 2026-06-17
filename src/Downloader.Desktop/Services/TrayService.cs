using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Downloader.Desktop.Services;

/// <summary>
/// Owns the system-tray icon and its menu (Open / Disable notifications / Quit) and lets the app keep
/// running in the background after the main window is closed. Created on demand so the user can turn the
/// whole behavior off in Settings. Cross-platform via Avalonia's <see cref="TrayIcon"/>.
/// </summary>
public static class TrayService
{
    private static TrayIcon _tray;
    private static Window _window;
    private static Action _onQuit;
    private static NativeMenuItem _disableNotifItem;

    /// <summary>Persist callback when notifications are toggled from the tray menu.</summary>
    public static Action<bool> NotificationsToggled;

    public static bool IsActive => _tray != null;

    /// <summary>Wire the window + quit action once at startup (before Enable/Disable).</summary>
    public static void Init(Window window, Action onQuit)
    {
        _window = window;
        _onQuit = onQuit;
    }

    public static void Enable()
    {
        if (_tray != null || _window == null)
            return;

        try
        {
            BuildTray();
        }
        catch
        {
            // Some platforms / sessions have no usable tray — fail soft so close-to-tray falls back to a
            // normal close (IsActive stays false) instead of stranding the window with no way back.
            _tray = null;
        }
    }

    private static void BuildTray()
    {
        _tray = new TrayIcon
        {
            Icon = LoadIcon(),
            ToolTipText = "Downloader",
            IsVisible = true
        };

        var menu = new NativeMenu();

        var open = new NativeMenuItem("Open Downloader");
        open.Click += (_, _) => ShowWindow();

        _disableNotifItem = new NativeMenuItem(NotifItemHeader());
        _disableNotifItem.Click += (_, _) =>
        {
            NotificationService.Enabled = !NotificationService.Enabled;
            _disableNotifItem.Header = NotifItemHeader();
            NotificationsToggled?.Invoke(NotificationService.Enabled);
        };

        var quit = new NativeMenuItem("Quit Downloader");
        quit.Click += (_, _) => _onQuit?.Invoke();

        menu.Items.Add(open);
        menu.Items.Add(_disableNotifItem);
        menu.Items.Add(quit);
        _tray.Menu = menu;

        _tray.Clicked += (_, _) => ShowWindow();

        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _tray });
    }

    public static void Disable()
    {
        if (_tray != null)
        {
            _tray.IsVisible = false;
            _tray.Dispose();
            _tray = null;
            _disableNotifItem = null;
        }
        if (Application.Current != null)
            TrayIcon.SetIcons(Application.Current, new TrayIcons());
    }

    /// <summary>Brings the main window back from the tray.</summary>
    public static void ShowWindow()
    {
        if (_window == null)
            return;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private static string NotifItemHeader() =>
        NotificationService.Enabled ? "Disable notifications" : "Enable notifications";

    private static WindowIcon LoadIcon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://Downloader.Desktop/Assets/downloader.png"));
        return new WindowIcon(stream);
    }
}
