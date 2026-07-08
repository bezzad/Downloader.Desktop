using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Downloader;
using Downloader.Desktop.Plugins;

namespace Downloader.Desktop.Services;

/// <summary>
/// Downloads a plugin's declared <see cref="PluginBinaryDependency"/> entries (e.g. ffmpeg, yt-dlp) using
/// the app's own resumable Downloader engine, then hands each off to its <c>FinishInstallAsync</c> for
/// extraction/placement. Resumability comes entirely from engine configuration
/// (<see cref="DownloadConfiguration.EnableAutoResumeDownload"/> + the default ".download" temp extension)
/// downloading to the SAME fixed destination every attempt — a stale ".download" sidecar left by a
/// cancelled/killed run is picked up automatically the next time this runs, no manual offset bookkeeping.
/// </summary>
public static class PluginDependencyInstaller
{
    /// <summary>Ensure every not-yet-available dependency in <paramref name="dependencies"/> is downloaded
    /// and installed. Throws <see cref="OperationCanceledException"/> if <paramref name="ct"/> is
    /// cancelled mid-flight (partial downloads are left in place for the next attempt to resume).</summary>
    public static async Task EnsureAllAsync(
        IReadOnlyList<PluginBinaryDependency> dependencies,
        IProgress<PluginDependencyProgress> progress,
        CancellationToken ct)
    {
        var pending = dependencies.Where(d => !SafeIsAvailable(d)).ToList();
        for (var i = 0; i < pending.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var dep = pending[i];
            progress?.Report(new PluginDependencyProgress(dep.DisplayName, 0, i + 1, pending.Count));
            await DownloadOneAsync(dep, i, pending.Count, progress, ct).ConfigureAwait(false);
            await dep.FinishInstallAsync(ct).ConfigureAwait(false);
        }
    }

    private static bool SafeIsAvailable(PluginBinaryDependency dep)
    {
        try { return dep.IsAvailable(); }
        catch { return false; }
    }

    private static async Task DownloadOneAsync(PluginBinaryDependency dep, int index, int total,
        IProgress<PluginDependencyProgress> progress, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(dep.DownloadDestination);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var cfg = new DownloadConfiguration { EnableAutoResumeDownload = true };
        var svc = new DownloadService(cfg, AppLog.Factory);

        Exception error = null;
        var cancelledByEngine = false;
        svc.DownloadFileCompleted += (_, e) => { error = e.Error; cancelledByEngine = e.Cancelled; };
        svc.DownloadProgressChanged += (_, e) =>
            progress?.Report(new PluginDependencyProgress(dep.DisplayName, e.ProgressPercentage, index + 1, total));

        using var reg = ct.Register(() => { try { _ = svc.CancelTaskAsync(); } catch { /* best-effort */ } });

        await svc.DownloadFileTaskAsync(new[] { dep.DownloadUrl.ToString() }, dep.DownloadDestination, ct)
            .ConfigureAwait(false);

        if (ct.IsCancellationRequested || cancelledByEngine)
            throw new OperationCanceledException(ct);
        if (error != null)
            throw new InvalidOperationException($"Failed to download {dep.DisplayName}: {error.Message}", error);
    }
}
