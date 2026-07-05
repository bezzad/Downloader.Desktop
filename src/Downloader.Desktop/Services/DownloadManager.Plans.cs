using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Downloader;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Services;

/// <summary>
/// Multi-part plan execution (HLS segments, video+audio mux, …). When a plugin resolver returns a plan
/// with more than one part or a post-process step, <see cref="Start"/> hands off here instead of the
/// single-file engine path. Parts download sequentially into a hidden <c>.&lt;name&gt;.parts</c> folder
/// next to the target, then the matching <see cref="IPostProcessor"/> assembles the final file.
///
/// <para>Pause/resume/cancel reuse the existing per-row <c>vm.Download</c> handle: each part's engine is
/// published to it, so the manager's guarded Pause/Resume/Cancel act on the current part. Engine pause
/// suspends the awaited part (the loop just waits); cancel makes the current part's task return, and the
/// runner sees <c>Status == Stopped</c> and cleans up. Completed parts are detected by files on disk, so
/// an app restart resumes from the first incomplete part with no extra bookkeeping.</para>
/// </summary>
public partial class DownloadManager
{
    /// <summary>Aggregate progress reported by <see cref="ExecutePlanAsync"/> (UI-free).</summary>
    internal readonly struct PlanProgress
    {
        public PlanProgress(double percent, double speed, long downloaded, long total)
        {
            Percent = percent; Speed = speed; Downloaded = downloaded; Total = total;
        }
        public double Percent { get; }
        public double Speed { get; }
        public long Downloaded { get; }
        public long Total { get; }
    }

    /// <summary>VM-coupled wrapper: runs a plan for a row and marks it Completed/Failed. Delegates the
    /// actual download+assemble to the UI-free <see cref="ExecutePlanAsync"/>.</summary>
    private async Task RunPlanAsync(DownloadItemViewModel vm, PersistedPlan plan, string folder,
        string suggestedName, CancellationToken ct)
    {
        var item = vm.GetItem();
        var finalName = SanitizeFileName(FirstNonEmpty(item.FileName, suggestedName, plan.SuggestedFileName, "download"));
        folder ??= ".";
        var processor = plan.PostProcessKind != PostProcessKind.None
            ? _plugins?.FindPostProcessor(plan.ToPostProcess())
            : null;

        OnUi(() =>
        {
            if (string.IsNullOrWhiteSpace(item.FileName))
                vm.FileName = finalName;
        });

        try
        {
            var finalPath = await ExecutePlanAsync(
                plan, folder, finalName, processor,
                onPartService: svc => OnUi(() => vm.Download = svc),
                onStage: stage => OnUi(() => vm.PlanStage = stage),
                onProgress: p => { if (vm.Status == DownloadStatus.Running) vm.StageProgress(p.Percent, p.Speed, p.Downloaded, p.Total); },
                isCancelled: () => vm.Status == DownloadStatus.Stopped,
                ct).ConfigureAwait(false);

            if (finalPath == null)
                return; // user cancelled (parts folder already removed) — status stays Stopped

            var size = SafeLength(finalPath);
            OnUi(() =>
            {
                vm.PlanStage = null;
                item.PlanJson = null;
                if (size > 0) { vm.Size = size; vm.Downloaded = size; }
                vm.Progress = 100;
                vm.Status = DownloadStatus.Completed;
                AppLog.Info($"Completed (plan): {finalName}");
                if (NotifyCompleteEnabled)
                    NotificationService.NotifyCompleted(finalName);
                OfferPostDownloadAction(vm);
                FinishTerminal(vm);
            });
        }
        catch (Exception ex)
        {
            OnUi(() =>
            {
                vm.PlanStage = null;
                vm.ErrorMessage = Describe(ex);
                vm.Status = DownloadStatus.Failed;
                AppLog.Error($"Plan failed: {finalName}", ex);
                if (NotifyFailedEnabled)
                    NotificationService.NotifyFailed(finalName, vm.ErrorMessage);
                FinishTerminal(vm);
            });
        }
    }

    /// <summary>
    /// UI-free core: downloads a plan's parts sequentially into <c>&lt;folder&gt;/.&lt;name&gt;.parts</c>
    /// (skipping already-complete ones), then assembles the final file. Returns the final path, or
    /// <c>null</c> if <paramref name="isCancelled"/> reported a user cancel (parts folder is removed).
    /// Throws on a real failure (a part that didn't finish, or a missing post-processor) — the parts
    /// folder is kept so Retry can reuse completed parts.
    /// </summary>
    internal async Task<string> ExecutePlanAsync(
        PersistedPlan plan, string folder, string finalName, IPostProcessor processor,
        Action<DownloadService> onPartService, Action<string> onStage,
        Action<PlanProgress> onProgress, Func<bool> isCancelled, CancellationToken ct)
    {
        folder ??= ".";
        finalName = SanitizeFileName(finalName);
        var finalPath = Path.Combine(folder, finalName);
        var partsDir = Path.Combine(folder, "." + finalName + ".parts");
        Directory.CreateDirectory(partsDir);

        var parts = plan.Parts;
        var hasPostProcess = plan.PostProcessKind != PostProcessKind.None;
        var downloadShare = hasPostProcess ? 0.90 : 1.0; // reserve the last 10% for assembly
        var allSized = parts.Count > 0 && parts.All(p => p.ExpectedSize is > 0);
        long totalExpected = allSized ? parts.Sum(p => p.ExpectedSize.Value) : 0;

        var partPaths = new List<string>(parts.Count);
        long completedBytes = 0;

        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var partPath = Path.Combine(partsDir, $"{i:D4}_{SanitizeFileName(UrlResolver.NameFromUrl(part.Url) ?? "part")}");
            partPaths.Add(partPath);

            if (IsPartComplete(partPath, part.ExpectedSize))
            {
                completedBytes += part.ExpectedSize ?? SafeLength(partPath);
                continue;
            }

            var index = i;
            onStage?.Invoke(string.Format(Localizer.Instance["Plan_Part"], index + 1, parts.Count));

            var cfg = _config?.Settings?.ToConfiguration() ?? new DownloadConfiguration();
            ApplyHeaders(cfg, part.Headers);
            var svc = new DownloadService(cfg, AppLog.Factory);

            var baseBytes = completedBytes;
            Exception partError = null;
            svc.DownloadFileCompleted += (_, e) => partError = e.Error;
            svc.DownloadProgressChanged += (_, e) =>
            {
                double pct = allSized && totalExpected > 0
                    ? (baseBytes + e.ReceivedBytesSize) / (double)totalExpected * 100.0 * downloadShare
                    : (index + e.ProgressPercentage / 100.0) / parts.Count * 100.0 * downloadShare;
                onProgress?.Invoke(new PlanProgress(pct, e.BytesPerSecondSpeed, baseBytes + e.ReceivedBytesSize, totalExpected));
            };

            onPartService?.Invoke(svc); // Pause/Resume/Cancel now target this part
            await svc.DownloadFileTaskAsync(new[] { part.Url }, partPath, ct).ConfigureAwait(false);

            if (isCancelled != null && isCancelled())
            {
                TryDeleteDir(partsDir);
                return null;
            }
            if (partError != null)
                throw partError; // engine reports a part failure via the event, not by throwing
            if (!PartDownloadedOk(partPath, part.ExpectedSize))
                throw new IOException($"Part {index + 1}/{parts.Count} did not finish downloading.");

            MarkPartDone(partPath, part.ExpectedSize);
            completedBytes += part.ExpectedSize ?? SafeLength(partPath);
        }

        // ---- Assemble ----
        onStage?.Invoke(Localizer.Instance["Plan_Assembling"]);
        if (hasPostProcess)
        {
            if (processor == null)
                throw new InvalidOperationException(Localizer.Instance["Plan_NoProcessor"]);
            var tmpOut = finalPath + ".assembling";
            var progress = new Progress<double>(p =>
                onProgress?.Invoke(new PlanProgress((downloadShare + Math.Clamp(p, 0, 1) * (1 - downloadShare)) * 100.0, 0, 0, totalExpected)));
            var produced = await processor.ProcessAsync(partPaths, plan.ToPostProcess(), tmpOut, progress, ct).ConfigureAwait(false);
            AtomicMove(produced, finalPath);
        }
        else
        {
            ConcatFiles(partPaths, finalPath); // multi-part, no post-process → raw concat
        }

        TryDeleteDir(partsDir);
        return finalPath;
    }

    // ---- Part-completion detection & bookkeeping ----

    private static string DoneMarker(string partPath) => partPath + ".done";

    /// <summary>Restart-skip check: a part from a PRIOR run is complete when its size matches the expected
    /// size (when known), or — when the size is unknown — when a <c>.done</c> marker was written after it
    /// finished (so a half-written part with no size isn't mistaken for complete).</summary>
    internal static bool IsPartComplete(string partPath, long? expectedSize)
    {
        if (!File.Exists(partPath))
            return false;
        if (expectedSize is > 0)
            return SafeLength(partPath) == expectedSize.Value;
        return File.Exists(DoneMarker(partPath));
    }

    /// <summary>Post-download verification for the part JUST fetched (before its <c>.done</c> marker is
    /// written): the file exists and either matches the expected size or, when the size is unknown, is
    /// non-empty.</summary>
    private static bool PartDownloadedOk(string partPath, long? expectedSize)
    {
        if (!File.Exists(partPath))
            return false;
        var len = SafeLength(partPath);
        return expectedSize is > 0 ? len == expectedSize.Value : len > 0;
    }

    private static void MarkPartDone(string partPath, long? expectedSize)
    {
        if (expectedSize is > 0)
            return; // a size match is the completion signal; no marker needed
        try { File.WriteAllText(DoneMarker(partPath), "1"); } catch { /* best-effort */ }
    }

    // ---- Assembly helpers ----

    internal static void ConcatFiles(IReadOnlyList<string> inputs, string outputPath)
    {
        var tmp = outputPath + ".assembling";
        using (var outStream = File.Create(tmp))
            foreach (var input in inputs)
                using (var inStream = File.OpenRead(input))
                    inStream.CopyTo(outStream);
        AtomicMove(tmp, outputPath);
    }

    private static void AtomicMove(string from, string to)
    {
        if (!string.Equals(from, to, StringComparison.Ordinal))
        {
            if (File.Exists(to))
                File.Delete(to);
            File.Move(from, to);
        }
    }

    // ---- Small utilities ----

    private static void ApplyHeaders(DownloadConfiguration cfg, IReadOnlyDictionary<string, string> headers)
    {
        if (headers == null || headers.Count == 0)
            return;
        cfg.RequestConfiguration ??= new RequestConfiguration();
        foreach (var (key, value) in headers)
        {
            try { cfg.RequestConfiguration.Headers.Add(key, value); }
            catch { /* skip a header the framework restricts (must be set via a property) */ }
        }
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    /// <summary>Deletes the hidden <c>.&lt;name&gt;.parts</c> scratch folder for an item, if any (called on
    /// remove so a cancelled multi-part download doesn't leave segments behind).</summary>
    private static void TryDeletePartsFolder(DownloadItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.FileName) || string.IsNullOrWhiteSpace(item.SaveFolder))
            return;
        TryDeleteDir(Path.Combine(item.SaveFolder, "." + SanitizeFileName(item.FileName) + ".parts"));
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "download";

    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "download";
        var cleaned = new string(name.Select(c => InvalidNameChars.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "download" : cleaned;
    }
}
