using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Downloader.Desktop.Services;

public enum UpdateState { Idle, Checking, Downloading, Ready }

/// <summary>
/// Telegram-style update coordinator. Checks GitHub, silently downloads the new build in the
/// background, and when it's ready surfaces a persistent "Update Downloader" button (nav rail) plus a
/// NATIVE system notification. The actual swap happens on app exit — so whether the user clicks the
/// button (which quits) or just closes the app, the staged update is applied and the app relaunches.
///
/// State is exposed via <see cref="State"/>/<see cref="Progress"/> + the <see cref="Changed"/> event so
/// the nav button and the Settings page can reflect it. All UI-facing state changes are marshaled to the
/// UI thread (the old flow called quit from a background continuation, which hung the window).
/// </summary>
public static class UpdateFlow
{
    public static UpdateState State { get; private set; } = UpdateState.Idle;
    public static double Progress { get; private set; }
    public static string AvailableVersion { get; private set; }

    private static string _archivePath;
    private static bool _busy;

    /// <summary>Raised (on the UI thread) whenever State/Progress changes.</summary>
    public static event Action Changed;

    /// <summary>Set by the app to perform a real quit (bypassing close-to-tray).</summary>
    public static Action RequestQuit;

    public static bool IsReady => State == UpdateState.Ready;

    /// <summary>True when an external package manager owns updates (snap) — the in-app updater is disabled.</summary>
    public static bool IsManagedExternally =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SNAP"));

    private static void Raise(UpdateState state, double progress)
    {
        State = state;
        Progress = progress;
        void Fire() => Changed?.Invoke();
        if (Dispatcher.UIThread.CheckAccess()) Fire(); else Dispatcher.UIThread.Post(Fire);
    }

    /// <summary>Checks GitHub; if a newer version exists, downloads it in the background automatically.</summary>
    public static async Task CheckAsync(bool manual)
    {
        if (IsManagedExternally || _busy || State == UpdateState.Ready)
        {
            if (manual && IsManagedExternally)
                NotificationService.Notify("Updates managed by your package manager",
                    "This build updates through the store it was installed from.", isError: false);
            return;
        }

        _busy = true;
        try
        {
            Raise(UpdateState.Checking, 0);
            var info = await UpdateService.CheckAsync().ConfigureAwait(false);
            if (info == null)
            {
                Raise(UpdateState.Idle, 0);
                if (manual)
                    NotificationService.Notify("You're up to date",
                        $"Downloader v{UpdateService.CurrentVersion} is the latest version.", isError: false);
                return;
            }

            AvailableVersion = info.Version;

            // No matching asset for this OS/arch — just open the release page.
            if (string.IsNullOrWhiteSpace(info.AssetUrl))
            {
                OpenUrl(info.ReleaseUrl);
                Raise(UpdateState.Idle, 0);
                return;
            }

            await DownloadAsync(info).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error("Update check/download failed", ex);
            Raise(UpdateState.Idle, 0);
        }
        finally
        {
            _busy = false;
        }
    }

    private static async Task DownloadAsync(UpdateInfo info)
    {
        Raise(UpdateState.Downloading, 0);
        var temp = Path.Combine(Path.GetTempPath(), info.AssetName);

        using (var dl = new DownloadService(new DownloadConfiguration()))
        {
            dl.DownloadProgressChanged += (_, e) =>
            {
                // Throttle UI churn: only bump on whole-percent changes.
                if (e.ProgressPercentage - Progress >= 1 || e.ProgressPercentage >= 100)
                    Raise(UpdateState.Downloading, e.ProgressPercentage);
            };
            await dl.DownloadFileTaskAsync(info.AssetUrl, temp).ConfigureAwait(false);
        }

        if (!File.Exists(temp))
        {
            OpenUrl(info.ReleaseUrl);
            Raise(UpdateState.Idle, 0);
            return;
        }

        _archivePath = temp;
        Raise(UpdateState.Ready, 100);

        // System (native) notification — visible even if the app is in the tray.
        NotificationService.Notify("Update ready",
            $"Downloader {info.Tag} downloaded. Click \"Update Downloader\" or just close the app to install.",
            isError: false);
    }

    /// <summary>Triggers a real app quit; the staged update is applied on exit (see ApplyPendingOnExit).</summary>
    public static void ApplyAndRestart()
    {
        if (State != UpdateState.Ready)
            return;
        void Quit() => RequestQuit?.Invoke();
        if (Dispatcher.UIThread.CheckAccess()) Quit(); else Dispatcher.UIThread.Post(Quit);
    }

    /// <summary>
    /// Called from the app's shutdown hook. If an update is staged, spawn the detached swap script (it
    /// waits for this process to exit, extracts over the app folder, then relaunches). Safe to call on
    /// every shutdown — it no-ops unless an update is ready.
    /// </summary>
    public static void ApplyPendingOnExit()
    {
        if (State != UpdateState.Ready || string.IsNullOrWhiteSpace(_archivePath))
            return;
        try { UpdateService.ApplyDownloadedArchive(_archivePath); }
        catch (Exception ex) { AppLog.Error("Failed to stage update on exit", ex); }
    }

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // best-effort
        }
    }
}
