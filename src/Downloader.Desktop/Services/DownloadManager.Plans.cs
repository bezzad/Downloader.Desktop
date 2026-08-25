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
/// <para>Pause/resume/cancel act through the row's <see cref="PlanController"/>, which holds EVERY part
/// engine currently in flight — a segment plan runs <see cref="DownloadManager.SegmentParallelism"/> at a
/// time, so pausing only <c>vm.Download</c> (the most recently started part) left the others transferring
/// while the row read "Paused" and its progress bar sat frozen (issue #7 follow-up). Engine pause suspends
/// the awaited part (the loop just waits); the controller's paused gate additionally stops the runner from
/// starting the NEXT part, which is what actually silences the network. Cancel makes the parts' tasks
/// return, and the runner sees <c>Status == Stopped</c> and cleans up. Completed parts are detected by
/// files on disk, so an app restart resumes from the first incomplete part with no extra bookkeeping.</para>
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
        var finalName = SanitizeFileName(NormalizeAssembledName(
            FirstNonEmpty(item.FileName, suggestedName, plan.SuggestedFileName, "download"), plan));
        folder ??= ".";
        var processor = plan.PostProcessKind != PostProcessKind.None
            ? _plugins?.FindPostProcessor(plan.ToPostProcess())
            : null;

        // Live per-segment board for the details dialog (waiting / downloading / done rows).
        var runState = new PlanRunState(plan.Parts.Count);
        // Pause/Resume/Cancel for this row act on every in-flight part through here, not on vm.Download.
        var controller = new PlanController();
        OnUi(() =>
        {
            // Keep the row in sync with the ACTUAL output name (a playlist-derived "video.m3u8" gets
            // normalized to "video.mp4" above — the row must not keep showing the playlist name).
            if (!string.Equals(item.FileName, finalName, StringComparison.Ordinal))
                vm.FileName = finalName;
            vm.PlanRun = runState;
            vm.PlanControl = controller;
        });

        try
        {
            var finalPath = await ExecutePlanAsync(
                plan, folder, finalName, processor,
                onPartService: svc => OnUi(() => vm.Download = svc),
                onStage: stage => OnUi(() => vm.PlanStage = stage),
                onProgress: p => { if (vm.Status == DownloadStatus.Running) vm.StageProgress(p.Percent, p.Speed, p.Downloaded, p.Total); },
                isCancelled: () => vm.Status == DownloadStatus.Stopped,
                ct, runState, item.Request,
                // Paused is the row's state, not the controller's, so a pause that arrives while the row is
                // between parts is still honored. isCancelled semantics are untouched.
                isPaused: () => vm.Status == DownloadStatus.Paused,
                controller).ConfigureAwait(false);

            if (finalPath == null)
            {
                OnUi(() => { vm.PlanRun = null; vm.PlanControl = null; });
                return; // user cancelled (parts folder already removed) — status stays Stopped
            }

            var size = SafeLength(finalPath);
            OnUi(() =>
            {
                vm.PlanStage = null;
                vm.PlanRun = null;
                vm.PlanControl = null;
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
                vm.PlanRun = null;
                vm.PlanControl = null;
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
    /// <summary>Parts at/below this size (or of Segment kind) download single-chunk — multipart chunking
    /// per tiny HLS segment is pure overhead (N range requests for a file that fits in one read).</summary>
    internal const long SmallPartBytes = 8 * 1024 * 1024;

    /// <summary>How many segment parts may download concurrently (each single-chunk).</summary>
    internal const int SegmentParallelism = 4;

    /// <summary>How often a paused plan re-checks whether it may continue. A poll rather than a signal: the
    /// wait is idle either way and this stays trivially correct against pause/resume/cancel racing it.</summary>
    internal const int PausePollMs = 200;

    internal async Task<string> ExecutePlanAsync(
        PersistedPlan plan, string folder, string finalName, IPostProcessor processor,
        Action<DownloadService> onPartService, Action<string> onStage,
        Action<PlanProgress> onProgress, Func<bool> isCancelled, CancellationToken ct,
        PlanRunState runState = null, RequestContext context = null,
        Func<bool> isPaused = null, PlanController controller = null)
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

        var partPaths = parts.Select((p, i) =>
                Path.Combine(partsDir, $"{i:D4}_{SanitizeFileName(UrlResolver.NameFromUrl(p.Url) ?? "part")}"))
            .ToList();

        // Progress aggregation shared by both modes: fraction done per part (1.0 = complete).
        var partFraction = new double[parts.Count];
        var partSpeed = new double[parts.Count];
        long doneBytes = 0;
        var progressGate = new object();

        void ReportProgress()
        {
            double pct;
            if (allSized && totalExpected > 0)
            {
                var bytes = doneBytes + (long)parts.Select((p, i) =>
                    partFraction[i] < 1 ? partFraction[i] * (p.ExpectedSize ?? 0) : 0).Sum();
                pct = bytes / (double)totalExpected * 100.0 * downloadShare;
                onProgress?.Invoke(new PlanProgress(pct, partSpeed.Sum(), bytes, totalExpected));
            }
            else
            {
                pct = partFraction.Sum() / parts.Count * 100.0 * downloadShare;
                onProgress?.Invoke(new PlanProgress(pct, partSpeed.Sum(), 0, 0));
            }
        }

        controller ??= new PlanController();
        isPaused ??= () => controller.IsPaused;

        bool Cancelled() => isCancelled != null && isCancelled();

        // The gate that actually silences the network while paused: in-flight parts are suspended by
        // PlanController.Pause, and this stops the runner claiming a slot for the NEXT one. Without it the
        // loop kept working through the playlist behind a frozen "Paused" row (issue #7 follow-up).
        async Task WaitWhilePausedAsync()
        {
            while (isPaused() && !Cancelled())
                await Task.Delay(PausePollMs, ct).ConfigureAwait(false);
        }

        async Task DownloadPartAsync(int index)
        {
            var part = parts[index];
            var partPath = partPaths[index];
            runState?.SetActive(index);
            if (part.ExpectedSize is > 0)
                runState?.SetTotal(index, part.ExpectedSize.Value);

            var cfg = _config?.Settings?.ToConfiguration() ?? new DownloadConfiguration();
            // The item's own cookies/headers/referer first, then the resolver's per-part headers on top —
            // the resolver knows more about that specific segment, so its value wins on a key collision.
            ApplyRequestContext(cfg, context);
            ApplyHeaders(cfg, part.Headers);
            // Tiny segment parts get exactly one connection/chunk — the speed win for HLS comes from
            // downloading several SEGMENTS at once, never from splitting one segment into N chunks.
            if (IsSingleChunkPart(part))
            {
                cfg.ChunkCount = 1;
                cfg.ParallelDownload = false;
            }

            var svc = new DownloadService(cfg, AppLog.Factory);
            controller.Add(svc);
            Exception partError = null;
            svc.DownloadFileCompleted += (_, e) => partError = e.Error;
            svc.DownloadProgressChanged += (_, e) =>
            {
                partFraction[index] = Math.Clamp(e.ProgressPercentage / 100.0, 0, 1);
                partSpeed[index] = e.BytesPerSecondSpeed;
                runState?.SetTotal(index, e.TotalBytesToReceive);
                runState?.Report(index, partFraction[index], e.BytesPerSecondSpeed, e.ReceivedBytesSize);
                lock (progressGate) ReportProgress();
            };

            // The row's live handle, for what only needs "the current part" (details dialog, status).
            // Pause/Resume/Cancel go through the controller instead — see the class remarks.
            onPartService?.Invoke(svc);
            try
            {
                await svc.DownloadFileTaskAsync(new[] { part.Url }, partPath, ct).ConfigureAwait(false);
            }
            finally
            {
                controller.Remove(svc); // stop pausing an engine that has already finished
            }

            partSpeed[index] = 0;
            if (isCancelled != null && isCancelled())
                return; // outer loop handles cleanup
            if (partError != null)
                throw partError; // the engine reports a part failure via the event, not by throwing
            if (!PartDownloadedOk(partPath, part.ExpectedSize))
                throw new IOException($"Part {index + 1}/{parts.Count} did not finish downloading.");

            MarkPartDone(partPath, part.ExpectedSize);
            partFraction[index] = 1;
            runState?.SetDone(index, part.ExpectedSize ?? SafeLength(partPath));
            System.Threading.Interlocked.Add(ref doneBytes, part.ExpectedSize ?? SafeLength(partPath));
        }

        // Which parts still need fetching (restart-resume skips completed ones).
        var pending = new List<int>();
        for (var i = 0; i < parts.Count; i++)
        {
            if (IsPartComplete(partPaths[i], parts[i].ExpectedSize))
            {
                partFraction[i] = 1;
                runState?.SetDone(i, parts[i].ExpectedSize ?? SafeLength(partPaths[i]));
                doneBytes += parts[i].ExpectedSize ?? SafeLength(partPaths[i]);
            }
            else
            {
                pending.Add(i);
            }
        }

        // Segment-only plans download several parts concurrently (each single-chunk); everything else
        // stays strictly sequential (big video+audio parts already use engine multipart internally).
        var parallel = pending.Count > 2 && pending.All(i => parts[i].Kind == PartKind.Segment);

        var doneCount = parts.Count - pending.Count;
        void StagePart() =>
            // Keep it simple ("Part 12/36") — the details dialog shows the per-segment parallelism
            // (the earlier "×4" suffix confused the author). Parallelism is visible, not narrated.
            onStage?.Invoke(string.Format(Localizer.Instance["Plan_Part"],
                Math.Min(doneCount + 1, parts.Count), parts.Count));

        if (parallel)
        {
            using var slots = new SemaphoreSlim(SegmentParallelism);
            var running = new List<Task>();
            StagePart();
            foreach (var index in pending)
            {
                await WaitWhilePausedAsync().ConfigureAwait(false);
                if (Cancelled())
                    break;
                await slots.WaitAsync(ct).ConfigureAwait(false);
                running.Add(Task.Run(async () =>
                {
                    try
                    {
                        await DownloadPartAsync(index).ConfigureAwait(false);
                        System.Threading.Interlocked.Increment(ref doneCount);
                        StagePart();
                    }
                    finally { slots.Release(); }
                }, ct));
            }
            try
            {
                await Task.WhenAll(running).ConfigureAwait(false);
            }
            catch when (isCancelled != null && isCancelled())
            {
                // fall through to the cancel cleanup below
            }
            if (Cancelled())
            {
                controller.CancelAll();
                TryDeleteDir(partsDir);
                return null;
            }
        }
        else
        {
            foreach (var index in pending)
            {
                await WaitWhilePausedAsync().ConfigureAwait(false);
                if (Cancelled())
                {
                    TryDeleteDir(partsDir);
                    return null;
                }
                StagePart();
                await DownloadPartAsync(index).ConfigureAwait(false);
                if (Cancelled())
                {
                    TryDeleteDir(partsDir);
                    return null;
                }
                doneCount++;
            }
        }

        // ---- Assemble ----
        onStage?.Invoke(Localizer.Instance["Plan_Assembling"]);
        if (hasPostProcess)
        {
            if (processor == null)
                throw new InvalidOperationException(Localizer.Instance["Plan_NoProcessor"]);
            // The temp output keeps a standard media extension LAST (video.assembling.mp4) — ffmpeg
            // picks its muxer from the extension and refuses a bare ".assembling" (author-hit bug).
            var tmpOut = AssemblingPath(finalPath);
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

    /// <summary>Pure: a part downloads single-chunk when it's a segment (HLS) or known-small — multipart
    /// chunking per tiny file is overhead, not speed.</summary>
    internal static bool IsSingleChunkPart(PersistedPart part) =>
        part.Kind == PartKind.Segment || part.ExpectedSize is > 0 and <= SmallPartBytes;

    /// <summary>Temp assembling path with the extension LAST: "video.mp4" → "video.assembling.mp4"
    /// (extension-less names get plain ".assembling").</summary>
    internal static string AssemblingPath(string finalPath)
    {
        var ext = Path.GetExtension(finalPath);
        return string.IsNullOrEmpty(ext)
            ? finalPath + ".assembling"
            : Path.Combine(Path.GetDirectoryName(finalPath) ?? "",
                Path.GetFileNameWithoutExtension(finalPath) + ".assembling" + ext);
    }

    /// <summary>Post-processed plans must not keep a playlist (or missing) extension — the assembled
    /// output is real media. `.m3u8`/`.m3u`/empty → `.mp4`, unless the plugin's suggested name carries a
    /// different concrete extension (which wins). A user's own non-playlist extension is preserved.</summary>
    internal static string NormalizeAssembledName(string name, PersistedPlan plan)
    {
        if (string.IsNullOrWhiteSpace(name) || plan == null || plan.PostProcessKind == PostProcessKind.None)
            return name;

        // A manifest extension is never the right name for the ASSEMBLED file — the Add dialog's name
        // preview probes the manifest URL, so an untouched name can easily arrive as "stream.mpd".
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (!IsManifestExtension(ext))
            return name; // already a concrete media extension

        var suggestedExt = Path.GetExtension(plan.SuggestedFileName ?? "").ToLowerInvariant();
        var newExt = IsManifestExtension(suggestedExt) ? ".mp4" : suggestedExt;
        var stem = Path.GetFileNameWithoutExtension(name);
        return string.IsNullOrEmpty(stem) ? "download" + newExt : stem + newExt;
    }

    /// <summary>An empty or adaptive-streaming-manifest extension (HLS <c>.m3u8</c>/<c>.m3u</c>, DASH
    /// <c>.mpd</c>) — i.e. one the assembled output must not keep.</summary>
    private static bool IsManifestExtension(string ext) => ext is "" or ".m3u8" or ".m3u" or ".mpd";

    // ---- Part-completion detection & bookkeeping ----

    private static string DoneMarker(string partPath) => partPath + ".done";

    /// <summary>Restart-skip check: a part from a PRIOR run is complete when its size matches the expected
    /// size (when known), or — when the size is unknown — when a <c>.done</c> marker was written after it
    /// finished (so a half-written part with no size isn't mistaken for complete).</summary>
    internal static bool IsPartComplete(string partPath, long? expectedSize)
    {
        // A completed part always carries a .done marker (written only after the engine finished with no
        // error). We do NOT gate on expectedSize: it comes from yt-dlp's filesize_approx for extracted
        // streams and is only an estimate — an exact-match gate re-downloaded a finished part forever.
        _ = expectedSize;
        return File.Exists(partPath) && SafeLength(partPath) > 0 && File.Exists(DoneMarker(partPath));
    }

    /// <summary>Post-download verification for the part JUST fetched (before its <c>.done</c> marker is
    /// written): the engine already reported success (no error), so the part just needs to exist and be
    /// non-empty. expectedSize is NOT an equality gate — it's an approximation for extracted streams.</summary>
    private static bool PartDownloadedOk(string partPath, long? expectedSize)
    {
        _ = expectedSize;
        return File.Exists(partPath) && SafeLength(partPath) > 0;
    }

    private static void MarkPartDone(string partPath, long? expectedSize)
    {
        _ = expectedSize;
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

    internal static void ApplyHeaders(DownloadConfiguration cfg, IReadOnlyDictionary<string, string> headers)
    {
        if (headers == null || headers.Count == 0)
            return;
        cfg.RequestConfiguration ??= new RequestConfiguration();
        foreach (var (key, value) in headers)
            SetHeader(cfg.RequestConfiguration, key, value);
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

/// <summary>
/// The live control surface of a running multi-part plan: every part engine currently in flight, plus a
/// paused gate the runner checks before it starts another part.
///
/// <para>This exists because a plan's parts are not one download. A segment plan runs several engines at
/// once, and the row's <c>vm.Download</c> only ever points at the most recently started one — so pausing
/// through it stopped one segment while the rest kept transferring, and the runner, whose only stop signal
/// was <c>Stopped</c>, worked on through the remaining playlist. Meanwhile the row displayed "Paused" with
/// a frozen bar, because staged progress is dropped for a non-Running row. Pause therefore has to reach the
/// whole set AND stop new parts being claimed; either half alone leaves bytes flowing.</para>
///
/// <para>Thread-safe: parts start and finish on runner tasks while the user pauses from the UI thread.</para>
/// </summary>
public sealed class PlanController
{
    private readonly object _gate = new();
    private readonly List<DownloadService> _active = new();
    private volatile bool _paused;

    /// <summary>True while the user has paused this plan: no further part may be started.</summary>
    public bool IsPaused => _paused;

    /// <summary>Registers a part engine as in-flight. If the plan was paused between building this engine
    /// and starting it, it is paused immediately so the race can't leak a running segment.</summary>
    public void Add(DownloadService service)
    {
        if (service == null)
            return;
        lock (_gate)
            _active.Add(service);
        if (_paused)
            TryPause(service);
    }

    /// <summary>Removes a part engine that has finished (or failed) from the in-flight set.</summary>
    public void Remove(DownloadService service)
    {
        if (service == null)
            return;
        lock (_gate)
            _active.Remove(service);
    }

    /// <summary>Snapshot of the engines currently in flight.</summary>
    public IReadOnlyList<DownloadService> Active
    {
        get { lock (_gate) return _active.ToArray(); }
    }

    /// <summary>Pauses every in-flight part and closes the gate on starting new ones.</summary>
    public void Pause()
    {
        _paused = true;
        foreach (var service in Active)
            TryPause(service);
    }

    /// <summary>Resumes every paused part and re-opens the gate.</summary>
    public void Resume()
    {
        _paused = false;
        foreach (var service in Active)
            try { service.Resume(); } catch { /* engine already finished */ }
    }

    /// <summary>Cancels every in-flight part. Un-pauses first: a suspended engine never completes its task,
    /// so cancelling a paused plan without this would leave the runner awaiting it forever.</summary>
    public void CancelAll()
    {
        _paused = false;
        foreach (var service in Active)
        {
            try { service.Resume(); } catch { /* not paused */ }
            try { _ = service.CancelTaskAsync(); } catch { /* already finished */ }
        }
    }

    private static void TryPause(DownloadService service)
    {
        try { service.Pause(); } catch { /* engine already finished */ }
    }
}
