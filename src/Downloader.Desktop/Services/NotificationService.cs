using System;
using System.Diagnostics;

namespace Downloader.Desktop.Services;

/// <summary>
/// User-facing messages (download complete/failed, updates, plugins, errors). Every message is shown as
/// a <b>native OS notification</b> on all platforms (Linux <c>notify-send</c>, macOS in-process banner,
/// Windows toast) — there are no in-app toasts. This is deliberately focus-agnostic: the OS decides how
/// to surface it whether the app is focused, unfocused, or hidden in the tray, so there is no window-focus
/// state to track and no double-fire ambiguity. Any action a message might suggest (install an update,
/// run a post-download action) lives in the app window itself — the Settings → Plugins "Update" button,
/// the update dialog / "Update Downloader" nav button, the per-row post-download action button — so a
/// notification never needs to carry a clickable callback. No external dependency; the app stays
/// self-contained.
/// </summary>
public static class NotificationService
{
    /// <summary>Master switch for passive alerts, mirrors the user setting.</summary>
    public static bool Enabled { get; set; } = true;

    public static void NotifyCompleted(string fileName) =>
        Notify("Download complete", string.IsNullOrWhiteSpace(fileName) ? "A download finished." : $"{fileName} finished.", false);

    public static void NotifyFailed(string fileName, string reason) =>
        Notify("Download failed", $"{(string.IsNullOrWhiteSpace(fileName) ? "A download" : fileName)} failed. {reason}".Trim(), true);

    public static void NotifyAllCompleted(int count) =>
        Notify("All downloads complete", count > 0 ? $"All {count} downloads finished." : "All downloads finished.", false);

    /// <summary>Passive alert (download complete/failed, update available, …), gated by the on/off switch.</summary>
    public static void Notify(string title, string message, bool isError)
    {
        if (!Enabled)
            return;
        Native(title, message, isError);
    }

    /// <summary>Direct feedback to something the user just did (e.g. "Plugin installed"). Always shown,
    /// independent of the on/off switch.</summary>
    public static void Inform(string title, string message, bool isError) => Native(title, message, isError);

    private static void Native(string title, string message, bool isError)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                // Standard freedesktop icon names — themed by the desktop environment. Success uses the
                // green "checkmark" emblem (not the blue info icon); failures use the red error icon.
                var icon = isError ? "dialog-error" : "emblem-default";
                Run("notify-send", new[] { "-i", icon, "Downloader", $"{title}: {message}" });
                return;
            }

            if (OperatingSystem.IsMacOS())
            {
                // Post in-process so the banner shows the app's own icon. (osascript "display
                // notification" always shows Script Editor's generic script icon and can't be overridden.)
                MacNotifier.TryNotify("Downloader", title, message);
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                // A real Windows toast, posted in-process through the shell notification API (no new
                // NuGet dependency and — deliberately — no child process; see WindowsNotifier).
                WindowsNotifier.TryNotify("Downloader", title, message, isError);
            }
        }
        catch (Exception ex)
        {
            // Best-effort: a missing notification daemon (Linux) or a blocked toast API (Windows) must
            // never crash the app — just record it.
            AppLog.Error("Failed to post OS notification", ex);
        }
    }

    private static void Run(string file, string[] args) => ShellLauncher.Run(file, args);
}
