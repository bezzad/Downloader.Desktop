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

        if (TryNative(title, message))
            return;

        ShowInApp(title, message, isError);
    }

    private static bool TryNative(string title, string message)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                Run("notify-send", new[] { "Downloader", $"{title}: {message}" });
                return true;
            }

            if (OperatingSystem.IsMacOS())
            {
                var script = $"display notification \"{Escape(message)}\" with title \"Downloader\" subtitle \"{Escape(title)}\"";
                Run("osascript", new[] { "-e", script });
                return true;
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

    private static string Escape(string s) => (s ?? string.Empty).Replace("\"", "\\\"");
}
