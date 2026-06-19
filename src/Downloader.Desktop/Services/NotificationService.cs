using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;

namespace Downloader.Desktop.Services;

/// <summary>
/// Best-effort desktop notifications for download completion/failure. Uses the OS-native mechanism
/// where one ships with the system (Linux <c>notify-send</c>, macOS <c>osascript</c>) and falls back
/// to an in-app Avalonia toast (Windows and anywhere the native call fails). No external dependency
/// is required, so the app stays fully self-contained (#11, #17).
/// </summary>
public static class NotificationService
{
    /// <summary>Master switch, mirrors the user setting.</summary>
    public static bool Enabled { get; set; } = true;

    private static WindowNotificationManager _inApp;

    /// <summary>Attach the in-app toast host to the main window (called once at startup).</summary>
    public static void Attach(TopLevel topLevel)
    {
        if (topLevel != null)
            _inApp = new WindowNotificationManager(topLevel) { MaxItems = 3, Position = NotificationPosition.BottomRight };
    }

    public static void NotifyCompleted(string fileName) =>
        Notify("Download complete", string.IsNullOrWhiteSpace(fileName) ? "A download finished." : $"{fileName} finished.", false);

    public static void NotifyFailed(string fileName, string reason) =>
        Notify("Download failed", $"{(string.IsNullOrWhiteSpace(fileName) ? "A download" : fileName)} failed. {reason}".Trim(), true);

    public static void Notify(string title, string message, bool isError)
    {
        if (!Enabled)
            return;

        if (TryNative(title, message, isError))
            return;

        ShowInApp(title, message, isError);
    }

    /// <summary>
    /// Shows an in-app toast the user can click to run <paramref name="onClick"/> (e.g. "Update available
    /// — click to install"). Always uses the in-app host (we need the click handler) and ignores the
    /// notifications on/off switch, since it's an actionable prompt rather than a passive alert.
    /// </summary>
    public static void ShowAction(string title, string message, Action onClick)
    {
        void Show() => _inApp?.Show(new Notification(
            title, message, NotificationType.Information, TimeSpan.FromSeconds(20), onClick));

        if (Dispatcher.UIThread.CheckAccess())
            Show();
        else
            Dispatcher.UIThread.Post(Show);
    }

    private static bool TryNative(string title, string message, bool isError)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                // Standard freedesktop icon names — themed by the desktop environment. Success uses the
                // green "checkmark" emblem (not the blue info icon); failures use the red error icon.
                var icon = isError ? "dialog-error" : "emblem-default";
                Run("notify-send", new[] { "-i", icon, "Downloader", $"{title}: {message}" });
                return true;
            }

            if (OperatingSystem.IsMacOS())
            {
                // Post in-process so the banner shows the app's own icon. (osascript "display
                // notification" always shows Script Editor's generic script icon and can't be
                // overridden.) Falls through to the in-app toast if the native call fails.
                return MacNotifier.TryNotify("Downloader", title, message);
            }
        }
        catch
        {
            // fall through to the in-app toast
        }

        return false; // Windows + fallbacks use the in-app toast
    }

    private static void ShowInApp(string title, string message, bool isError)
    {
        void Show()
        {
            _inApp?.Show(new Notification(title, message,
                isError ? NotificationType.Error : NotificationType.Success));
        }

        if (Dispatcher.UIThread.CheckAccess())
            Show();
        else
            Dispatcher.UIThread.Post(Show);
    }

    private static void Run(string file, string[] args)
    {
        var psi = new ProcessStartInfo { FileName = file, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        Process.Start(psi);
    }
}
