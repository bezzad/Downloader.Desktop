using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using System.Text;

namespace Downloader.Desktop.Services;

/// <summary>
/// Posts a native Windows toast notification by spawning PowerShell to invoke the WinRT toast API
/// (<c>Windows.UI.Notifications.ToastNotificationManager</c>) — no new NuGet package, no
/// <c>net*-windows</c> TFM, no <c>UseWindowsForms</c>. This mirrors this file's sibling
/// <see cref="MacNotifier"/> in spirit (a small, dependency-free native call) but as a spawned
/// process rather than P/Invoke, since the toast API is COM/WinRT-based rather than a flat C ABI.
///
/// The notification text is sent via <c>-EncodedCommand</c> (a base64 UTF-16LE script), so the
/// title/message never need to be escaped into a command-line argument. Inside the script, the text
/// is embedded in a single-quoted PowerShell string holding XML; <see cref="SecurityElement.Escape"/>
/// escapes all five XML special characters (including both quote kinds), which happens to also
/// remove every raw <c>'</c> from the text — so it can't break out of that PowerShell string either.
///
/// Unverifiable on this (macOS/Linux) dev box or in CI (no Windows runner) — needs manual
/// confirmation on an actual Windows machine. On any failure this returns false so the caller falls
/// back to the in-app toast, same contract as every other <c>TryNative</c> branch.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsNotifier
{
    public static bool TryNotify(string appName, string title, string message)
    {
        try
        {
            var script = BuildScript(appName, title, message);
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            return proc != null;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildScript(string appName, string title, string message)
    {
        var safeApp = SecurityElement.Escape(appName ?? "Downloader");
        var safeTitle = SecurityElement.Escape(title ?? string.Empty);
        var safeMessage = SecurityElement.Escape(message ?? string.Empty);

        return "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null\n" +
               "[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] > $null\n" +
               "$xml = New-Object Windows.Data.Xml.Dom.XmlDocument\n" +
               $"$xml.LoadXml('<toast><visual><binding template=\"ToastGeneric\"><text>{safeTitle}</text><text>{safeMessage}</text></binding></visual></toast>')\n" +
               "$toast = New-Object Windows.UI.Notifications.ToastNotification $xml\n" +
               $"[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{safeApp}').Show($toast)\n";
    }
}
