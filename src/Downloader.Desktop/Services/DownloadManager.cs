using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Services;

/// <summary>
/// Default <see cref="IDownloadManager"/>. Builds <see cref="IDownload"/> instances via
/// <see cref="DownloadBuilder"/>, marshals engine events onto the UI thread, and updates the
/// matching <see cref="DownloadItemViewModel"/>. Queue concurrency / scheduling are layered on later.
/// </summary>
public class DownloadManager : IDownloadManager
{
    private Config _config;

    public ObservableCollection<DownloadItemViewModel> Items { get; } = new();

    public event Action StatsChanged;
    public event Action ListChanged;

    public double TotalSpeed
    {
        get
        {
            double sum = 0;
            foreach (var i in Items)
                if (i.Status == DownloadStatus.Running)
                    sum += i.Speed;
            return sum;
        }
    }

    public int ActiveCount => Items.Count(i => i.Status == DownloadStatus.Running);
    public int QueuedCount => Items.Count(i => i.Status is DownloadStatus.Created or DownloadStatus.None);
    public int CompletedCount => Items.Count(i => i.Status == DownloadStatus.Completed);

    // ---------------- UI update pump (perf) ----------------
    // A single dispatcher timer flushes all rows' staged progress to the UI at a fixed rate, so the
    // main thread does a bounded amount of work per tick regardless of how many downloads/connections
    // are running. It runs only while something is active and stops itself when the list goes idle.
    private DispatcherTimer _uiTimer;

    private void EnsureUiPump()
    {
        _uiTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _uiTimer.Tick -= OnUiPumpTick;
        _uiTimer.Tick += OnUiPumpTick;
        if (!_uiTimer.IsEnabled)
            _uiTimer.Start();
    }

    private void OnUiPumpTick(object sender, EventArgs e)
    {
        var flushed = false;
        var active = false;
        foreach (var vm in Items)
        {
            if (vm.Status == DownloadStatus.Running)
                active = true;
            if (vm.FlushProgress())
                flushed = true;
        }

        if (flushed)
            StatsChanged?.Invoke();
        if (!active)
            _uiTimer.Stop();
    }

    private int _suppressNotify;
    private bool _pendingNotify;

    /// <summary>Items added/removed or changed status — refresh the filtered list and numbers.</summary>
    private void NotifyList()
    {
        if (_suppressNotify > 0)
        {
            _pendingNotify = true;
            return;
        }
        StatsChanged?.Invoke();
        ListChanged?.Invoke();
    }

    /// <summary>
    /// Coalesces the many per-item <see cref="NotifyList"/> calls a bulk operation makes into a single
    /// refresh at the end — without this, "select all + Start" re-filters the grid once per row and freezes.
    /// </summary>
    public void Batch(Action action) => RunBatch(action ?? (() => { }));

    private void RunBatch(Action work)
    {
        _suppressNotify++;
        try { work(); }
        finally { _suppressNotify--; }
        if (_pendingNotify)
        {
            _pendingNotify = false;
            NotifyList();
        }
    }

    public void Initialize(Config config)
    {
        _config = config ?? Config.New();

        // The Settings "Max concurrent downloads" is the user-facing limit; keep the primary queue's
        // cap in lockstep so it actually limits how many run at once (a config saved before this was
        // wired up could have a stale queue cap). Extra queues keep their own caps from the Queues page.
        if (_config.Settings != null && _config.DefaultQueue is { } dq)
            dq.MaxConcurrent = Math.Max(1, _config.Settings.MaxConcurrentDownloads);

        foreach (var existing in Items)
            existing.Detach();
        Items.Clear();
        foreach (var item in _config.Downloads ?? new List<DownloadItem>())
        {
            // Nothing is actually running on a fresh launch — show in-progress items as resumable.
            if (item.Status == DownloadStatus.Running)
                item.Status = DownloadStatus.Paused;
            Items.Add(new DownloadItemViewModel(item, this));
        }

        StartScheduler();
    }

    // ---------------- Scheduler ----------------

    private DispatcherTimer _schedulerTimer;
    private readonly HashSet<string> _firedKeys = new();
    private DateTime _firedDay = DateTime.MinValue;

    private void StartScheduler()
    {
        _schedulerTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _schedulerTimer.Tick -= OnSchedulerTick;
        _schedulerTimer.Tick += OnSchedulerTick;
        _schedulerTimer.Start();
    }

    private void OnSchedulerTick(object sender, EventArgs e) => EvaluateSchedules();

    private void EvaluateSchedules()
    {
        if (_config?.Schedules == null)
            return;

        var now = DateTime.Now;
        if (now.Date != _firedDay)
        {
            _firedDay = now.Date;
            _firedKeys.Clear();
        }

        var tod = now.TimeOfDay;
        foreach (var sch in _config.Schedules.ToList())
        {
            if (!sch.Enabled)
                continue;
            if (sch.Days is { Length: > 0 } && !sch.Days.Contains(now.DayOfWeek))
                continue;

            var startKey = sch.Id + ":start";
            var stopKey = sch.Id + ":stop";

            var inWindow = tod >= sch.StartTime && (sch.StopTime == null || tod < sch.StopTime.Value);
            if (inWindow && _firedKeys.Add(startKey))
            {
                TriggerStart(sch);
                if (sch.Once)
                    sch.Enabled = false;
            }

            if (sch.StopTime is { } stop && tod >= stop && _firedKeys.Add(stopKey))
                TriggerStop(sch);
        }
    }

    private void TriggerStart(DownloadSchedule sch)
    {
        if (!string.IsNullOrEmpty(sch.TargetQueueId))
        {
            var queue = FindQueue(sch.TargetQueueId);
            if (queue != null)
                StartQueue(queue);
        }
        else if (sch.TargetItemId is { } id)
        {
            var vm = Items.FirstOrDefault(i => i.GetItem().Id == id);
            if (vm != null && vm.CanResume)
                Resume(vm);
        }
    }

    private void TriggerStop(DownloadSchedule sch)
    {
        if (!string.IsNullOrEmpty(sch.TargetQueueId))
        {
            var queue = FindQueue(sch.TargetQueueId);
            if (queue != null)
                PauseQueue(queue);
        }
        else if (sch.TargetItemId is { } id)
        {
            var vm = Items.FirstOrDefault(i => i.GetItem().Id == id);
            if (vm != null && vm.Status == DownloadStatus.Running)
                Pause(vm);
        }
    }

    public DownloadItemViewModel Add(DownloadItem item, bool autoStart)
    {
        if (string.IsNullOrWhiteSpace(item.QueueId) && _config != null)
            item.QueueId = _config.DefaultQueue.Id;

        var vm = new DownloadItemViewModel(item, this);
        Items.Add(vm);
        if (autoStart)
            PumpQueue(item.QueueId); // starts now if a slot is free, otherwise stays queued

        // Resolve a display name in the background so items still waiting on a queue slot show their
        // file name instead of "Fetching name…" until they actually start (#4).
        if (string.IsNullOrWhiteSpace(item.FileName))
            _ = ResolvePreviewNameAsync(vm);

        NotifyList();
        return vm;
    }

    private static async Task ResolvePreviewNameAsync(DownloadItemViewModel vm)
    {
        var url = vm.GetItem().Url;
        if (string.IsNullOrWhiteSpace(url))
            return;

        var name = await UrlResolver.ResolveFileNameAsync(url).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(name))
            OnUi(() =>
            {
                if (string.IsNullOrWhiteSpace(vm.FileName))
                    vm.PreviewName = name;
            });
    }

    public async void Start(DownloadItemViewModel vm)
    {
        // Never (re)start something that's already running or finished. A completed file must not be
        // re-downloaded from 0%, and a double-start would spin up a second engine for the same row
        // (the second reports progress from 0 — the "100% then begins again from 0" bug).
        if (vm.Status is DownloadStatus.Running or DownloadStatus.Completed)
            return;

        var item = vm.GetItem();
        var urls = item.Urls?.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).ToArray()
                   ?? Array.Empty<string>();
        if (urls.Length == 0)
            return;

        var folder = string.IsNullOrWhiteSpace(item.SaveFolder)
            ? _config?.Settings?.DefaultSavePath
            : item.SaveFolder;
        item.SaveFolder = folder;
        item.LastTry = DateTime.Now;
        vm.ErrorMessage = null;
        vm.Status = DownloadStatus.Running;
        EnsureUiPump();
        AppLog.Info($"Starting: {urls[0]}{(urls.Length > 1 ? $" (+{urls.Length - 1} mirror[s])" : "")}");

        var configuration = _config?.Settings?.ToConfiguration() ?? new DownloadConfiguration();
        vm.Configuration = configuration; // keep a handle so the details dialog can tweak it live
        var fileName = item.FileName;

        // Build + start entirely off the UI thread. Resolving redirects and the engine's synchronous
        // setup must not run on the dispatcher, or selecting many items and pressing Start freezes it.
        try
        {
            await Task.Run(async () =>
            {
                // Follow redirects up-front for the primary URL (handles 307/308, signed links, etc.).
                // The engine also follows redirects, so this is a best-effort optimization only.
                var resolved = await UrlResolver.ResolveAsync(urls[0]).ConfigureAwait(false);
                if (!string.Equals(resolved, urls[0], StringComparison.Ordinal))
                {
                    AppLog.Info($"Resolved redirect: {urls[0]} -> {resolved}");
                    urls[0] = resolved;
                }

                // DownloadService (not the single-URL DownloadBuilder) so mirrors are real fallbacks
                // and the engine's internal logs flow into our log file via the shared logger factory.
                var download = new DownloadService(configuration, AppLog.Factory);
                // Subscribe before starting so no early event is missed (handlers marshal to UI themselves).
                Attach(vm, download);

                if (!string.IsNullOrWhiteSpace(fileName))
                    await download.DownloadFileTaskAsync(urls, Path.Combine(folder ?? string.Empty, fileName))
                        .ConfigureAwait(false);
                else
                    await download.DownloadFileTaskAsync(urls, new DirectoryInfo(folder ?? "."))
                        .ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to start: {urls[0]}", ex);
            OnUi(() =>
            {
                vm.ErrorMessage = Describe(ex);
                vm.Status = DownloadStatus.Failed;
                NotifyList();
            });
        }
    }

    /// <summary>Turns an exception into a short, user-friendly root cause.</summary>
    private static string Describe(Exception ex)
    {
        var e = ex;
        while (e.InnerException != null)
            e = e.InnerException;

        return e switch
        {
            System.Net.Http.HttpRequestException => $"Network error: {e.Message}",
            System.Net.WebException we => $"Network error: {we.Message}",
            UnauthorizedAccessException => "Permission denied writing the file. Try another folder.",
            IOException io => $"Disk error: {io.Message}",
            TaskCanceledException or OperationCanceledException =>
                "The connection timed out — the server stopped responding. Please try again.",
            _ => e.Message
        };
    }

    public void Pause(DownloadItemViewModel vm)
    {
        // Only a running download can be paused. Guard so a bulk "Pause" over a mixed selection can't
        // touch completed/failed/queued rows.
        if (vm.Status != DownloadStatus.Running)
            return;
        vm.Download?.Pause();
        vm.Status = DownloadStatus.Paused;
        vm.Speed = 0;
        NotifyList();
    }

    public void Resume(DownloadItemViewModel vm)
    {
        // Nothing to resume for an already-running or finished download. This also stops a bulk
        // "Start" over a mixed selection from re-running a completed item from 0%.
        if (vm.Status is DownloadStatus.Running or DownloadStatus.Completed)
            return;

        // Mark the item as wanting to run, then let the queue decide whether a slot is free. This is
        // what makes bulk "Start" honor the concurrency cap: a stopped/failed item becomes "queued"
        // (Created) and only actually starts when PumpQueue finds room. Paused items keep their live
        // handle and are resumed in place by the pump.
        if (vm.Status is DownloadStatus.Stopped or DownloadStatus.Failed or DownloadStatus.None)
            vm.Status = DownloadStatus.Created;

        PumpQueue(vm.GetItem().QueueId);
        NotifyList();
    }

    public void Cancel(DownloadItemViewModel vm)
    {
        // "Stop" applies to anything in flight OR still waiting: running/paused → cancel the engine and
        // mark Stopped; queued (Created/None) → also mark Stopped so the pump won't auto-start them.
        // Terminal/idle states (Completed/Failed/already-Stopped) are left alone — so a bulk "Stop"
        // over a mixed selection can't knock a finished download down to Stopped.
        //
        // Stopping the queued rows here is what actually stops the queue: when the running rows'
        // cancellation later fires DownloadFileCompleted → TryStartNextInQueue, there are no remaining
        // queued rows for the pump to start (the whole StopSelected loop runs synchronously before any
        // completion callback is posted to the UI thread). Without this, stopping the running rows just
        // freed slots and the pump immediately started the next queued rows ("3 stop, 3 start").
        if (vm.Status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Stopped)
            return;
        vm.Download?.CancelAsync();
        vm.Status = DownloadStatus.Stopped;
        vm.Speed = 0;
        NotifyList();
    }

    public void Retry(DownloadItemViewModel vm)
    {
        // Retry only applies to a failed/stopped download — never re-run a completed or already-running
        // one from 0%. Re-queue it; the pump starts it when the queue has a free slot (cap-aware).
        if (vm.Status is not (DownloadStatus.Failed or DownloadStatus.Stopped))
            return;
        vm.Status = DownloadStatus.Created;
        PumpQueue(vm.GetItem().QueueId);
        NotifyList();
    }

    public Task Remove(DownloadItemViewModel vm)
    {
        try
        {
            vm.Download?.CancelAsync();
        }
        catch
        {
            // best-effort stop before removal
        }

        vm.Detach();
        Items.Remove(vm);
        NotifyList();
        return Task.CompletedTask;
    }

    public void StartAll() =>
        RunBatch(() =>
        {
            // Re-queue everything resumable (stopped → queued; paused/created stay as-is), then start
            // each queue up to its cap. This is the fix for "Start all ignored the queue limit": work
            // funnels through PumpQueue instead of starting every item directly.
            foreach (var vm in Items.Where(v => v.Status == DownloadStatus.Stopped).ToList())
                vm.Status = DownloadStatus.Created;

            foreach (var queue in _config?.Queues?.ToList() ?? new List<DownloadQueue>())
                StartQueue(queue);
        });

    public void StopAll() =>
        RunBatch(() =>
        {
            foreach (var vm in Items.Where(v => v.Status == DownloadStatus.Running).ToList())
                Pause(vm);
        });

    public void ClearCompleted()
    {
        foreach (var vm in Items.Where(v => v.IsCompleted).ToList())
        {
            vm.Detach();
            Items.Remove(vm);
        }
        NotifyList();
    }

    private void TryStartNextInQueue(string queueId) => PumpQueue(queueId);

    private DownloadQueue FindQueue(string id) =>
        _config?.Queues?.FirstOrDefault(q => q.Id == id);

    public void PumpQueue(string queueId)
    {
        var queue = FindQueue(queueId);
        if (queue == null || !queue.IsRunning)
            return;

        var cap = Math.Max(1, queue.MaxConcurrent);
        int Running() => Items.Count(i => i.GetItem().QueueId == queueId && i.Status == DownloadStatus.Running);

        // Eligible = paused (resume in place, prioritized as they're partway done) or queued
        // (Created/None → start fresh). Start them only while a concurrency slot is free.
        var pending = Items
            .Where(i => i.GetItem().QueueId == queueId &&
                        i.Status is DownloadStatus.Paused or DownloadStatus.Created or DownloadStatus.None)
            .OrderByDescending(i => i.Status == DownloadStatus.Paused)
            .ToList();

        foreach (var vm in pending)
        {
            if (Running() >= cap)
                break;
            StartOrResume(vm);
        }
    }

    /// <summary>Resumes a paused item in place (if it still has a live handle) or starts it fresh.</summary>
    private void StartOrResume(DownloadItemViewModel vm)
    {
        if (vm.Status == DownloadStatus.Paused && vm.Download != null)
        {
            vm.Download.Resume();
            vm.Status = DownloadStatus.Running;
            EnsureUiPump();
        }
        else
        {
            Start(vm);
        }
    }

    public void StartQueue(DownloadQueue queue)
    {
        if (queue == null)
            return;
        queue.IsRunning = true;
        PumpQueue(queue.Id); // resumes paused + starts queued, all capped at MaxConcurrent
        NotifyList();
    }

    public void PauseQueue(DownloadQueue queue)
    {
        if (queue == null)
            return;
        queue.IsRunning = false;
        foreach (var vm in Items.Where(i =>
                     i.GetItem().QueueId == queue.Id && i.Status == DownloadStatus.Running).ToList())
            Pause(vm);
        NotifyList();
    }

    public DownloadQueue AddQueue(string name)
    {
        var queue = new DownloadQueue
        {
            Name = string.IsNullOrWhiteSpace(name) ? "New queue" : name,
            MaxConcurrent = _config?.Settings?.MaxConcurrentDownloads ?? 3
        };
        _config?.Queues?.Add(queue);
        return queue;
    }

    public void RemoveQueue(DownloadQueue queue)
    {
        if (queue == null || _config?.Queues == null || _config.Queues.Count <= 1)
            return;

        var fallback = _config.Queues.FirstOrDefault(q => q.Id != queue.Id);
        foreach (var vm in Items.Where(i => i.GetItem().QueueId == queue.Id).ToList())
            vm.GetItem().QueueId = fallback?.Id;

        _config.Queues.Remove(queue);
        NotifyList();
    }

    private void Attach(DownloadItemViewModel vm, DownloadService download)
    {
        vm.Download = download;

        download.DownloadStarted += (_, e) => OnUi(() =>
        {
            // The engine resolved the real file path (from URL / Content-Disposition) and reports
            // it as the full path in e.FileName. IDownload.Filename stays empty when no name was
            // supplied, so derive the name/folder from e.FileName instead.
            if (!string.IsNullOrWhiteSpace(e.FileName))
            {
                var name = Path.GetFileName(e.FileName);
                if (string.IsNullOrWhiteSpace(vm.FileName) && !string.IsNullOrWhiteSpace(name))
                    vm.FileName = name;

                var dir = Path.GetDirectoryName(e.FileName);
                if (!string.IsNullOrWhiteSpace(dir))
                    vm.GetItem().SaveFolder = dir;
            }

            if (e.TotalBytesToReceive > 0)
                vm.Size = e.TotalBytesToReceive;
            vm.Status = DownloadStatus.Running;
            NotifyList();
        });

        download.DownloadProgressChanged += (_, e) =>
        {
            // Stage only — no UI marshaling here. The shared UI pump flushes the latest values to the
            // grid at a fixed rate, so the main thread stays free no matter how frequently (or from how
            // many connections) the engine raises this event. A paused/stopped row drops staged events
            // in FlushProgress, so its last fill is preserved.
            if (vm.Status != DownloadStatus.Running)
                return;
            vm.StageProgress(e.ProgressPercentage, e.BytesPerSecondSpeed, e.ReceivedBytesSize, e.TotalBytesToReceive);
        };

        download.DownloadFileCompleted += (_, e) => OnUi(() =>
        {
            vm.Speed = 0;
            if (e.Cancelled)
            {
                // A cancel is only a "Stopped/Paused" if the USER asked for it — we set those
                // statuses before calling the engine. A cancel that arrives while still Running was
                // NOT user-initiated (e.g. a timeout) → treat it as a Failure, consistently (#6).
                if (vm.Status is DownloadStatus.Paused or DownloadStatus.Stopped)
                {
                    // user action — keep the status as-is
                }
                else
                {
                    vm.ErrorMessage = e.Error != null
                        ? Describe(e.Error)
                        : "The connection was lost or timed out before the download finished. Please try again.";
                    vm.Status = DownloadStatus.Failed;
                    AppLog.Error($"Failed (interrupted): {vm.FileName ?? vm.Url}", e.Error);
                    NotificationService.NotifyFailed(vm.FileName ?? vm.Url, vm.ErrorMessage);
                }
            }
            else if (e.Error != null)
            {
                vm.ErrorMessage = Describe(e.Error);
                vm.Status = DownloadStatus.Failed;
                AppLog.Error($"Failed: {vm.FileName ?? vm.Url}", e.Error);
                NotificationService.NotifyFailed(vm.FileName ?? vm.Url, vm.ErrorMessage);
            }
            else
            {
                vm.Progress = 100;
                vm.Status = DownloadStatus.Completed;
                AppLog.Info($"Completed: {vm.FileName}");
                NotificationService.NotifyCompleted(vm.FileName);
            }

            // A finished/stopped item frees a slot — let the queue start the next one.
            TryStartNextInQueue(vm.GetItem().QueueId);
            NotifyList();
        });
    }

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
