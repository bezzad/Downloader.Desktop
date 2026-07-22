using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
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
        catch (Exception ex)
        {
            // Some platforms / sessions have no usable tray — fail soft so close-to-tray falls back to a
            // normal close (IsActive stays false) instead of stranding the window with no way back. But
            // NEVER swallow the reason silently: this used to be a bare `catch { }`, which is why "the
            // icon just doesn't show" was unanswerable when it happened under a Rider debug run (no packed
            // app, no icon theme, potentially no SNI host at all) — there was no signal to diagnose from.
            // AppLog only writes when the user has enabled logging in Settings; Debug.WriteLine always
            // shows up in the IDE's Debug/Run output regardless, so a dev session sees it immediately.
            System.Diagnostics.Debug.WriteLine($"[TrayService] Failed to enable the system tray icon: {ex}");
            AppLog.Error("Failed to enable the system tray icon", ex);
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

        // Open the main window when the icon is activated. Do NOT platform-gate this: gating it off on
        // Linux (fa6b925) coincided with the tray icon not appearing at all on the author's Ubuntu box,
        // and this exact handler was present in every build where the icon DID show. The Linux
        // right-click-menu bug is a separate, on-device-only issue — see SKILL.md.
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

    /// <summary>Brings the main window back from the tray. Safe to call from any thread — the shared
    /// helper marshals to the UI thread and defers activation past the show (see WindowActivation, #6).</summary>
    public static void ShowWindow() => WindowActivation.BringToFront(_window);

    private static string NotifItemHeader() =>
        NotificationService.Enabled ? "Disable notifications" : "Enable notifications";

    /// <summary>Tray icons must be SMALL. Public for testing only — see <see cref="TraySize"/>.</summary>
    internal static readonly PixelSize TraySize = new(64, 64);

    private static WindowIcon LoadIcon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://Downloader.Desktop/Assets/downloader512.png"));
        using var bmp = new Bitmap(stream);
        using var scaled = ScaleToTraySize(bmp);
        return new WindowIcon(scaled);
    }

    /// <summary>
    /// The full 1080x1080 app PNG is a ~4.6 MB RGBA pixmap; pushing that to the StatusNotifierItem host
    /// over DBus can make the item render but its menu fail to attach (part of the "no tray menu" bug on
    /// Linux). Downscale to <see cref="TraySize"/> for the tray. Split out from <see cref="LoadIcon"/> so
    /// the actual pixel size is testable — <see cref="WindowIcon"/> doesn't expose its dimensions.
    /// </summary>
    internal static Bitmap ScaleToTraySize(Bitmap source) =>
        source.CreateScaledBitmap(TraySize, BitmapInterpolationMode.HighQuality);
}
