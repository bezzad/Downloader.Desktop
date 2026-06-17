using System;
using System.IO;
using System.Threading.Tasks;

namespace Downloader.Desktop.Services;

/// <summary>
/// Glues <see cref="UpdateService"/> to the UI: checks for a new release, shows an in-app toast that
/// asks the user to update, and on accept downloads the platform archive in the background (via the
/// Downloader engine) then relaunches into it. Kept tiny and static so both startup (auto) and the
/// Settings "Check for updates" button can call it.
/// </summary>
public static class UpdateFlow
{
    private static bool _busy;

    /// <summary>Set by the app so the flow can shut down cleanly before the updater script relaunches.</summary>
    public static Action RequestShutdown;

    /// <summary>Checks GitHub; toasts if newer (manual checks also toast "up to date").</summary>
    public static async Task CheckAsync(bool manual)
    {
        var info = await UpdateService.CheckAsync().ConfigureAwait(false);
        if (info == null)
        {
            if (manual)
                NotificationService.Notify("You're up to date",
                    $"Downloader v{UpdateService.CurrentVersion} is the latest version.", isError: false);
            return;
        }

        AppLog.Info($"Update available: {info.Tag}");
        NotificationService.ShowAction("Update available",
            $"Downloader {info.Tag} is available. Click to download and install.",
            () => _ = StartAsync(info));
    }

    /// <summary>Downloads the archive in the background and triggers the relaunch-into-new-version.</summary>
    public static async Task StartAsync(UpdateInfo info)
    {
        if (_busy || info == null)
            return;
        _busy = true;
        try
        {
            // No matching asset for this OS/arch — just open the release page so the user can grab it.
            if (string.IsNullOrWhiteSpace(info.AssetUrl))
            {
                OpenUrl(info.ReleaseUrl);
                return;
            }

            NotificationService.Notify("Downloading update",
                $"Getting {info.AssetName} in the background — the app keeps running.", isError: false);

            var temp = Path.Combine(Path.GetTempPath(), info.AssetName);
            using (var dl = new DownloadService(new DownloadConfiguration()))
                await dl.DownloadFileTaskAsync(info.AssetUrl, temp).ConfigureAwait(false);

            if (UpdateService.ApplyDownloadedArchive(temp))
            {
                NotificationService.Notify("Restarting to update",
                    "Download complete. The app will restart to finish updating.", isError: false);
                RequestShutdown?.Invoke();
            }
            else
            {
                OpenUrl(info.ReleaseUrl);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Update failed", ex);
            OpenUrl(info.ReleaseUrl);
        }
        finally
        {
            _busy = false;
        }
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
