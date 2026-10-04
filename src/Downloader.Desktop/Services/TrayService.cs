using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;

namespace Downloader.Desktop.Services;

/// <summary>
/// Owns the system-tray icon and its menu (Show downloads / Settings / Notifications / Exit) and lets the
/// app keep running in the background after the main window is closed. On Windows a click on the icon
/// opens the app-drawn <see cref="TrayMenuView"/> and the native menu is the right click. On Linux the
/// shell opens the native menu itself on a click (served where the snap allows it, see
/// <see cref="TrayDbusMenu"/>) and reports only a double click, which brings the window back — as on
/// macOS. Created on demand so the user can turn the whole behavior off in Settings. Cross-platform via
/// Avalonia's <see cref="TrayIcon"/>.
/// </summary>
public static class TrayService
{
    private static TrayIcon _tray;
    private static Window _window;
    private static Action _onQuit;
    private static NativeMenu _nativeMenu;
    private static TrayMenuView _popup;
    private static IDisposable _linuxMenu;

    /// <summary>The popup, once it has been opened (for tests).</summary>
    internal static TrayMenuView Popup => _popup;

    /// <summary>Persist callback when notifications are toggled from the tray menu.</summary>
    public static Action<bool> NotificationsToggled;

    public static bool IsActive => _tray != null;

    /// <summary>The menu's items and commands, shared by the popup (and testable without a tray).</summary>
    internal static TrayMenuViewModel Menu { get; private set; }

    /// <summary>Wire the window and the menu's actions once at startup (before Enable/Disable).
    /// <paramref name="showDownloads"/>/<paramref name="showSettings"/> bring the window back on that page;
    /// unset, both just bring the window back.</summary>
    public static void Init(Window window, Action onQuit, Action showDownloads = null, Action showSettings = null)
    {
        _window = window;
        _onQuit = onQuit;
        Menu = new TrayMenuViewModel(
            showDownloads ?? ShowWindow,
            showSettings ?? ShowWindow,
            enabled =>
            {
                RefreshNativeMenu();
                NotificationsToggled?.Invoke(enabled);
            },
            () => _onQuit?.Invoke());
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

        var menu = _nativeMenu = BuildNativeMenu();
        Localizer.Instance.PropertyChanged += OnLanguageChanged;

        // On-device diagnosis (tray-popup-menu, step 1): with DLDESKTOP_TRAY_DIAG set, log when the
        // native menu opens/closes and on which thread each event arrives; "nomenu" also leaves the menu
        // off, to learn whether the shell then reports clicks to us at all.
        var diag = ParseDiag(Environment.GetEnvironmentVariable(DiagVariable));
        if (diag != TrayDiag.None)
        {
            AppLog.Info($"TRAY-DIAG: mode={diag}, os={Environment.OSVersion}, desktop={Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")}, session={Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")}");
            menu.Opening += (_, _) => AppLog.Info($"TRAY-DIAG: native menu opening (thread {Environment.CurrentManagedThreadId})");
            menu.Closed += (_, _) => AppLog.Info($"TRAY-DIAG: native menu closed (thread {Environment.CurrentManagedThreadId})");
        }
        if (diag != TrayDiag.NoMenu)
        {
            // Linux: serve the menu where the snap's AppArmor lets the shell read it; elsewhere (or when
            // Avalonia's internals moved) Avalonia's own exporter carries it, as before.
            _linuxMenu = OperatingSystem.IsLinux() ? TrayDbusMenu.TryExport(_tray, menu) : null;
            if (_linuxMenu == null)
                _tray.Menu = menu;
        }

        // Keep this subscription unconditional (skill: settled). On Ubuntu GNOME it is the DOUBLE click
        // (StatusNotifierItem.Activate) — the AppIndicator extension handles a single click itself.
        _tray.Clicked += (_, _) => { AppLog.Info($"UI: tray icon clicked (thread {Environment.CurrentManagedThreadId})"); OnClicked(); };

        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _tray });
    }

    public static void Disable()
    {
        if (_tray != null)
        {
            _tray.IsVisible = false;
            _tray.Dispose();
            _tray = null;
            try { _linuxMenu?.Dispose(); }
            catch (Exception ex) { AppLog.Warn($"Tray menu exporter did not dispose cleanly: {ex.Message}"); }
            _linuxMenu = null;
            _nativeMenu = null;
            Localizer.Instance.PropertyChanged -= OnLanguageChanged;
        }
        _popup?.Close();
        _popup = null;
        if (Application.Current != null)
            TrayIcon.SetIcons(Application.Current, new TrayIcons());
    }

    /// <summary>Brings the main window back from the tray. Safe to call from any thread — the shared
    /// helper marshals to the UI thread and defers activation past the show (see WindowActivation, #6).</summary>
    public static void ShowWindow() => WindowActivation.BringToFront(_window);

    /// <summary>A click the app is told about. On Windows that is a single left click, and it opens the
    /// app-drawn menu. On Linux it is a DOUBLE click (the shell opens the native menu on a single one) and on
    /// macOS the native menu is the click, so there it brings the window back.</summary>
    internal static void OnClicked() => OnClicked(ClickOpensPopup());

    /// <summary>Whether the click the app hears opens the app-drawn menu on this platform (Windows only).</summary>
    internal static bool ClickOpensPopup() => OperatingSystem.IsWindows();

    /// <summary>The click with the platform's answer passed in, so both branches are testable anywhere.</summary>
    internal static void OnClicked(bool opensPopup)
    {
        if (!opensPopup)
            ShowWindow();
        else
            Dispatcher.UIThread.Post(ShowMenuPopup);
    }

    /// <summary>Opens the app-drawn menu in the corner by the tray (UI thread only).</summary>
    internal static void ShowMenuPopup()
    {
        if (Menu == null)
            return;

        _popup ??= new TrayMenuView { DataContext = Menu };
        _popup.ShowNearTray();
    }

    /// <summary>The native menu: same four items, translated. The notifications item shows a check
    /// mark while on — <c>NativeMenuItemToggleType</c> does not exist in Avalonia 12.</summary>
    internal static NativeMenu BuildNativeMenu()
    {
        var menu = new NativeMenu();
        var show = new NativeMenuItem();
        show.Click += (_, _) => Menu?.ShowDownloadsCommand.Execute(null);
        var settings = new NativeMenuItem();
        settings.Click += (_, _) => Menu?.ShowSettingsCommand.Execute(null);
        var notifications = new NativeMenuItem();
        notifications.Click += (_, _) =>
        {
            if (Menu != null)
                Menu.NotificationsEnabled = !Menu.NotificationsEnabled;
        };
        var exit = new NativeMenuItem();
        exit.Click += (_, _) => Menu?.ExitCommand.Execute(null);

        menu.Items.Add(show);
        menu.Items.Add(settings);
        menu.Items.Add(notifications);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exit);
        ApplyLabels(menu);
        return menu;
    }

    /// <summary>Writes the current-language labels (and the notifications check mark) onto a native menu.</summary>
    internal static void ApplyLabels(NativeMenu menu)
    {
        var l = Localizer.Instance;
        ((NativeMenuItem)menu.Items[0]).Header = l["Tray_ShowDownloads"];
        ((NativeMenuItem)menu.Items[1]).Header = l["Tray_Settings"];
        ((NativeMenuItem)menu.Items[2]).Header = (NotificationService.Enabled ? "✓ " : "") + l["Tray_Notifications"];
        ((NativeMenuItem)menu.Items[4]).Header = l["Tray_Exit"];
    }

    private static void RefreshNativeMenu()
    {
        if (_nativeMenu != null)
            ApplyLabels(_nativeMenu);
        Menu?.Refresh();
    }

    private static void OnLanguageChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        Dispatcher.UIThread.Post(RefreshNativeMenu);

    /// <summary>Environment variable that switches on the tray diagnosis (see <see cref="ParseDiag"/>).</summary>
    internal const string DiagVariable = "DLDESKTOP_TRAY_DIAG";

    internal enum TrayDiag { None, Log, NoMenu }

    /// <summary>Unset/empty/"0" = off; "nomenu" = log and attach no native menu; anything else = log.</summary>
    internal static TrayDiag ParseDiag(string value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "0" => TrayDiag.None,
        "nomenu" => TrayDiag.NoMenu,
        _ => TrayDiag.Log
    };

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
