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
public partial class DownloadManager : IDownloadManager
{
    private Config _config;

    // Optional: lets a pasted link an enabled plugin claims (e.g. github.com/owner/repo) be resolved to a
    // real downloadable asset URL before the engine runs. Null in tests that don't need plugins.
    private readonly PluginManager _plugins;

    public DownloadManager() { }

    public DownloadManager(PluginManager plugins) => _plugins = plugins;

    public ObservableCollection<DownloadItemViewModel> Items { get; } = new();

    public event Action StatsChanged;
    public event Action ListChanged;
    public event Action AllDownloadsCompleted;
    public event Action QueuesChanged;

    public IReadOnlyList<DownloadQueue> Queues => _config?.Queues ?? new List<DownloadQueue>();

    public Config Config => _config;

    // Guards the "all complete" event so it fires once per batch, not on every item after the list
    // has already drained. Re-armed whenever a download (re)starts.
    private bool _allCompleteFired;

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

    private bool NotifyCompleteEnabled => _config?.Settings is { EnableNotifications: true, NotifyOnComplete: true };
    private bool NotifyFailedEnabled => _config?.Settings is { EnableNotifications: true, NotifyOnFailed: true };

    /// <summary>
    /// After an item finishes, if nothing is left running or waiting (and at least one completed),
    /// raise <see cref="AllDownloadsCompleted"/> exactly once. Consumers handle the user-facing parts
    /// (all-complete notification, shutdown-on-completion) so this stays UI/OS-free and testable.
    /// </summary>
    private void MaybeAllCompleted()
    {
        if (_allCompleteFired)
            return;
        if (ActiveCount > 0 || QueuedCount > 0)
            return;
        if (CompletedCount == 0)
            return;
        _allCompleteFired = true;
        AllDownloadsCompleted?.Invoke();
    }

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
            // Nothing is actually running on a fresh launch. A saved Running OR Paused state can't
            // survive a restart — "Paused" means the live server connection is held open with the stream
            // reader paused, which is impossible once the process exited. So normalize both to Stopped
            // (resumable from disk) rather than showing a misleading "Paused" row.
            if (item.Status is DownloadStatus.Running or DownloadStatus.Paused)
                item.Status = DownloadStatus.Stopped;
            // Backfill the queue for items saved without one (older configs) so they always belong to a
            // queue and show up on the Queues page.
            if (string.IsNullOrWhiteSpace(item.QueueId))
                item.QueueId = _config.DefaultQueue?.Id;
            Items.Add(new DownloadItemViewModel(item, this));
        }

        StartScheduler();
    }

    // ---------------- Scheduler ----------------

    private DispatcherTimer _schedulerTimer;

    private void StartScheduler()
    {
        _schedulerTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _schedulerTimer.Tick -= OnSchedulerTick;
        _schedulerTimer.Tick += OnSchedulerTick;
        _schedulerTimer.Start();
    }

    private void OnSchedulerTick(object sender, EventArgs e) => EvaluateSchedules();

    /// <summary>
    /// Fires each enabled schedule's start/stop at most once per calendar day, tracked via
    /// <see cref="DownloadSchedule.LastFiredStartDate"/>/<see cref="DownloadSchedule.LastFiredStopDate"/>
    /// (persisted with the schedule) instead of an in-memory-only set. This is what stops a relaunch inside
    /// an already-fired-today window from re-firing a start that already happened earlier that day — the
    /// old in-memory tracking reset on every process start, so a restart looked identical to "never fired
    /// today" and could undo an explicit Stop All a few seconds after reopening the app.
    /// </summary>
    internal void EvaluateSchedules()
    {
        if (_config?.Schedules == null)
            return;

        var now = DateTime.Now;
        var today = now.Date;
        var tod = now.TimeOfDay;
        foreach (var sch in _config.Schedules.ToList())
        {
            if (!sch.Enabled)
                continue;
            if (sch.Days is { Length: > 0 } && !sch.Days.Contains(now.DayOfWeek))
                continue;

            var inWindow = tod >= sch.StartTime && (sch.StopTime == null || tod < sch.StopTime.Value);
            if (inWindow && sch.LastFiredStartDate != today)
            {
                sch.LastFiredStartDate = today;
                TriggerStart(sch);
                if (sch.Once)
                    sch.Enabled = false;
            }

            if (sch.StopTime is { } stop && tod >= stop && sch.LastFiredStopDate != today)
            {
                sch.LastFiredStopDate = today;
                TriggerStop(sch);
            }
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

    private async Task ResolvePreviewNameAsync(DownloadItemViewModel vm)
    {
        var url = vm.GetItem().Url;
        if (string.IsNullOrWhiteSpace(url))
            return;

        // Show any name embedded in the URL instantly (free, no network) so the row never lingers
        // on "Fetching name…" while the probe runs.
        var quick = UrlResolver.NameFromUrl(url);
        if (!string.IsNullOrWhiteSpace(quick))
            OnUi(() =>
            {
                if (string.IsNullOrWhiteSpace(vm.FileName))
                    vm.PreviewName = quick;
            });

        // Then a single lightweight probe (Downloader 5.9.0 RemoteFileResolver) yields the
        // authoritative name AND size without starting a download, so queued rows waiting on a slot
        // preview both (#4). Honors the user's request settings (proxy/headers/credentials).
        var info = await UrlResolver
            .ResolveFileInfoAsync(url, _config?.Settings?.ToConfiguration())
            .ConfigureAwait(false);
        if (info == null)
            return;

        OnUi(() =>
        {
            if (!string.IsNullOrWhiteSpace(info.FileName) && string.IsNullOrWhiteSpace(vm.FileName))
                vm.PreviewName = info.FileName;
            if (info.FileSize > 0 && vm.Size is null or 0)
                vm.Size = info.FileSize;
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
        vm.AlreadyExisted = false;
        // Capture the known size before progress events overwrite it — only meaningful when resuming
        // (we already had bytes + a real size). Used to spot an expired link returning a tiny file.
        vm.PreAttemptSize = item.Downloaded > 0 ? item.Size : null;
        vm.Status = DownloadStatus.Running;
        _allCompleteFired = false; // a new run means "all complete" can fire again when it drains
        EnsureUiPump();
        AppLog.Info($"Starting: {urls[0]}{(urls.Length > 1 ? $" (+{urls.Length - 1} mirror[s])" : "")}");

        var configuration = _config?.Settings?.ToConfiguration() ?? new DownloadConfiguration();
        // A per-item speed cap set in the details dialog wins over the global limit and survives restarts.
        if (item.HasCustomSpeedLimit)
            configuration.MaximumBytesPerSecond = item.CustomSpeedLimitBytesPerSecond <= 0
                ? 0 : item.CustomSpeedLimitBytesPerSecond;
        vm.Configuration = configuration; // keep a handle so the details dialog can tweak it live
        var fileName = item.FileName;

        // Build + start entirely off the UI thread. Resolving redirects and the engine's synchronous
        // setup must not run on the dispatcher, or selecting many items and pressing Start freezes it.
        try
        {
            await Task.Run(async () =>
            {
                // Resolve the plan: reuse a persisted one (restart / resume of a multi-part download) or
                // ask the plugins fresh. A multi-part / post-process plan is run by the plan runner; a
                // single-part plan just rewrites the URL + name and falls through to the normal engine path.
                var persisted = PersistedPlan.FromJson(item.PlanJson);
                if (persisted == null)
                {
                    var plan = await ResolvePlanAsync(urls[0], default).ConfigureAwait(false);
                    if (plan?.Parts is { Count: > 0 })
                    {
                        // Remember WHICH plugin resolved this link so its post-download action (e.g.
                        // "Add to Ollama") can be offered on the finished item, incl. after a restart.
                        item.ResolverPluginId = _plugins?.FindResolverPluginId(urls[0]);
                        var pp = PersistedPlan.From(plan);
                        if (pp.NeedsRunner)
                        {
                            item.PlanJson = pp.ToJson();
                            persisted = pp;
                        }
                        else if (!string.IsNullOrWhiteSpace(plan.Parts[0].Url))
                        {
                            urls[0] = plan.Parts[0].Url;
                            if (string.IsNullOrWhiteSpace(fileName))
                                fileName = plan.SuggestedFileName;
                            AppLog.Info($"Plugin resolved {item.Url} -> {urls[0]}");
                        }
                    }
                }

                if (persisted != null)
                {
                    await RunPlanAsync(vm, persisted, folder, fileName, default).ConfigureAwait(false);
                    return; // the plan runner owns completion (marks Completed/Failed itself)
                }

                // Follow redirects up-front for the primary URL (handles 307/308, signed links, etc.).
                // The engine also follows redirects, so this is a best-effort optimization only.
                var resolved = await UrlResolver.ResolveAsync(urls[0], configuration).ConfigureAwait(false);
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

    /// <summary>
    /// If an enabled plugin resolver claims <paramref name="url"/>, resolve it to a real downloadable URL
    /// (and a suggested file name). Returns the input unchanged when no plugin claims it, when there is no
    /// plugin manager, or when resolving fails. Only the first part is used here — multi-part / transfer /
    /// post-process plans (HLS, torrent) need the not-yet-built job coordinator and are downloaded as their
    /// first part for now (logged).
    /// </summary>
    /// <summary>Asks the enabled plugin resolvers to turn <paramref name="url"/> into a concrete plan
    /// (real part URLs + post-process recipe). Returns null when no plugin claims it, there's no plugin
    /// manager, or resolving fails.</summary>
    public async Task<Plugins.DownloadPlan> ResolvePlanAsync(
        string url, System.Threading.CancellationToken cancellationToken)
    {
        if (_plugins == null)
            return null;
        try
        {
            return await _plugins.ResolveAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Plugin resolve failed for {url} — using the link as-is", ex);
            return null;
        }
    }

    /// <summary>Single-part convenience over <see cref="ResolvePlanAsync"/>: if a resolver claims the link,
    /// returns its first part's URL + suggested name; otherwise the input unchanged. Multi-part / post-process
    /// plans are handled by the plan runner (see <c>DownloadManager.Plans.cs</c>), not here.</summary>
    public async Task<(string Url, string FileName)> ResolveViaPluginsAsync(
        string url, string currentFileName, System.Threading.CancellationToken cancellationToken)
    {
        var plan = await ResolvePlanAsync(url, cancellationToken).ConfigureAwait(false);
        if (plan?.Parts == null || plan.Parts.Count == 0)
            return (url, currentFileName);

        var part = plan.Parts[0];
        if (string.IsNullOrWhiteSpace(part.Url))
            return (url, currentFileName);

        var name = string.IsNullOrWhiteSpace(currentFileName) ? plan.SuggestedFileName : currentFileName;
        AppLog.Info($"Plugin resolved {url} -> {part.Url}");
        return (part.Url, name);
    }

    /// <summary>The post-download action label a plugin offers for this COMPLETED item (e.g. "Add to
    /// Ollama"), or null. Only the plugin that resolved the link is consulted.</summary>
    public string PostDownloadActionLabel(DownloadItemViewModel vm)
    {
        var item = vm?.GetItem();
        if (item == null || vm.Status != DownloadStatus.Completed)
            return null;
        return _plugins?.FindPostDownloadAction(item.ResolverPluginId, item.Url, item.FilePath)?.Label;
    }

    /// <summary>Runs the offered post-download action (user click). Failures surface as a friendly
    /// in-app message on the item; the downloaded file is never modified by the host.</summary>
    public async Task RunPostDownloadAction(DownloadItemViewModel vm)
    {
        var item = vm?.GetItem();
        var action = item == null ? null
            : _plugins?.FindPostDownloadAction(item.ResolverPluginId, item.Url, item.FilePath);
        if (action == null)
            return;

        try
        {
            NotificationService.Inform(action.Label, vm.FileName ?? item.Url, isError: false);
            await Task.Run(() => action.ExecuteAsync(item.Url, item.FilePath, null, default)).ConfigureAwait(false);
            OnUi(() => NotificationService.Inform(action.Label, Localizer.Instance["PostAction_Done"], isError: false));
        }
        catch (Exception ex)
        {
            AppLog.Error($"Post-download action '{action.Label}' failed for {item.Url}", ex);
            OnUi(() =>
            {
                vm.ErrorMessage = Describe(ex);
                NotificationService.Inform(action.Label, Describe(ex), isError: true);
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
                "The download timed out — data stopped arriving in time after several retries. Please try again.",
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

        // An explicit per-item Start must run even if the queue was previously paused/stopped — that
        // IsRunning=false is persisted, so otherwise PumpQueue silently swallows the item and it stays
        // stuck as "Queued", only rescued later by the scheduler/StartQueue (the reported bug).
        EnsureQueueRunning(vm.GetItem().QueueId);
        PumpQueue(vm.GetItem().QueueId);
        NotifyList();
    }

    /// <summary>An explicit user start (Resume/Retry/Add) un-pauses the item's queue so the pump runs it.</summary>
    private void EnsureQueueRunning(string queueId)
    {
        var queue = FindQueue(queueId);
        if (queue != null)
            queue.IsRunning = true;
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
        // Re-resolve on retry: a multi-part plan's segment URLs may have expired (signed HLS links), so
        // clear the saved plan and let the next Start ask the resolver again. Completed parts still on
        // disk are reused only when the fresh plan's part paths match (same url → same part file).
        vm.GetItem().PlanJson = null;
        vm.Status = DownloadStatus.Created;
        EnsureQueueRunning(vm.GetItem().QueueId); // see Resume: an explicit start un-pauses the queue
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

        TryDeletePartsFolder(vm.GetItem()); // clean up any half-downloaded multi-part scratch
        vm.Detach();
        Items.Remove(vm);
        NotifyList();
        return Task.CompletedTask;
    }

    /// <summary>Live-apply a new global speed limit (bytes/sec, 0 = unlimited) to every running item that
    /// has NOT opted out with a per-item custom limit. Safe when an item's engine handle isn't up yet.</summary>
    public void ApplyGlobalSpeedLimit(long bytesPerSecond)
    {
        var value = bytesPerSecond <= 0 ? 0 : bytesPerSecond;
        foreach (var vm in Items)
        {
            if (vm.HasCustomSpeedLimit || vm.Configuration == null)
                continue;
            vm.Configuration.MaximumBytesPerSecond = value;
        }
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
            // Stop everything in flight or waiting. Cancel() guards terminal states, so completed/failed
            // rows are left alone. Stopping the queued rows too keeps the pump from refilling freed slots.
            foreach (var vm in Items.Where(v =>
                         v.Status is DownloadStatus.Running or DownloadStatus.Paused
                                  or DownloadStatus.Created or DownloadStatus.None).ToList())
                Cancel(vm);
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

    /// <summary>Test seam: runs the same post-completion bookkeeping the engine's completed handler does,
    /// without a real download (mark Completed → pump the queue → maybe raise all-complete).</summary>
    public void RaiseCompletedForTest(DownloadItemViewModel vm)
    {
        vm.Progress = 100;
        vm.Status = DownloadStatus.Completed;
        FinishTerminal(vm);
    }

    /// <summary>Test seam: simulate a user stop/cancel reaching the terminal bookkeeping (must NOT
    /// arm the all-complete / shutdown trigger even if completed items remain in the list).</summary>
    public void RaiseStoppedForTest(DownloadItemViewModel vm)
    {
        vm.Status = DownloadStatus.Stopped;
        FinishTerminal(vm);
    }

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

        // "Start queue" should run every *remaining* (non-completed) download in it. The pump only
        // picks up Paused/Created/None, so re-queue Stopped/Failed rows to Created first — otherwise
        // selecting a queue after a Stop/Stop-all (rows are Stopped) appeared to do nothing.
        RunBatch(() =>
        {
            foreach (var vm in Items.Where(i =>
                         i.GetItem().QueueId == queue.Id &&
                         i.Status is DownloadStatus.Stopped or DownloadStatus.Failed).ToList())
                vm.Status = DownloadStatus.Created;

            PumpQueue(queue.Id); // resumes paused + starts queued, all capped at MaxConcurrent
        });
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

    public void StopQueue(DownloadQueue queue)
    {
        if (queue == null)
            return;
        queue.IsRunning = false;
        // "Stop queue" stops every item in the queue (running/paused/queued → Stopped), not just a
        // pause of the running ones. Cancel() guards terminal states, so completed/failed are untouched.
        RunBatch(() =>
        {
            foreach (var vm in Items.Where(i =>
                         i.GetItem().QueueId == queue.Id &&
                         i.Status is DownloadStatus.Running or DownloadStatus.Paused
                                  or DownloadStatus.Created or DownloadStatus.None).ToList())
                Cancel(vm);
        });
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
        NotifyList();
        QueuesChanged?.Invoke();
        return queue;
    }

    public void RemoveQueue(DownloadQueue queue)
    {
        if (queue == null || _config?.Queues == null || _config.Queues.Count <= 1)
            return;

        var fallback = _config.Queues.FirstOrDefault(q => q.Id != queue.Id);
        foreach (var vm in Items.Where(i => i.GetItem().QueueId == queue.Id).ToList())
            vm.GetItem().QueueId = fallback?.Id;

        // Deactivate any schedules bound to this queue — a schedule pointing at a deleted queue is dead
        // and would otherwise sit enabled but inert (or act on a stale target). Disable + unbind them.
        foreach (var sch in _config.Schedules?.Where(s => s.TargetQueueId == queue.Id).ToList()
                            ?? new List<DownloadSchedule>())
        {
            sch.Enabled = false;
            sch.TargetQueueId = null;
        }

        _config.Queues.Remove(queue);
        NotifyList();
        QueuesChanged?.Invoke();
    }

    public void MoveToQueue(DownloadItemViewModel vm, string queueId)
    {
        if (vm == null || string.IsNullOrEmpty(queueId))
            return;
        var item = vm.GetItem();
        var from = item.QueueId;
        if (from == queueId)
            return;

        item.QueueId = queueId;
        NotifyList();
        if (!string.IsNullOrEmpty(from))
            PumpQueue(from);   // a freed slot in the old queue may let its next item start
        PumpQueue(queueId);    // if the target queue is running with room, the moved item can start
    }

    public void MovePriority(DownloadItemViewModel vm, int direction)
    {
        if (vm == null || direction == 0)
            return;

        var queueId = vm.GetItem().QueueId;
        var sameQueue = Items.Where(i => i.GetItem().QueueId == queueId).ToList();
        var posInQueue = sameQueue.IndexOf(vm);
        var targetInQueue = posInQueue + Math.Sign(direction);
        if (posInQueue < 0 || targetInQueue < 0 || targetInQueue >= sameQueue.Count)
            return; // already at the top/bottom of its queue

        // Reorder within the master list (pump order = list order). Moving past the neighbour keeps every
        // other item's relative order, so only this queue's priority changes.
        var neighbour = sameQueue[targetInQueue];
        Items.Move(Items.IndexOf(vm), Items.IndexOf(neighbour));
        NotifyList();
        PumpQueue(queueId);
    }

    public void ReorderTo(DownloadItemViewModel vm, DownloadItemViewModel target, bool placeAfter)
    {
        if (vm == null || target == null || ReferenceEquals(vm, target))
            return;

        var from = Items.IndexOf(vm);
        var targetIndex = Items.IndexOf(target);
        if (from < 0 || targetIndex < 0)
            return;

        // The drop position: just after the target row when dropped on its lower half, else just before.
        // Account for the source being removed first when it currently sits above the target.
        var insertIndex = placeAfter ? targetIndex + 1 : targetIndex;
        if (from < insertIndex)
            insertIndex--;
        insertIndex = Math.Max(0, Math.Min(insertIndex, Items.Count - 1));

        // Dragging across queues adopts the queue of the row it lands next to (the target's queue).
        var oldQueueId = vm.GetItem().QueueId;
        var newQueueId = target.GetItem().QueueId;

        if (insertIndex != from)
            Items.Move(from, insertIndex);
        if (!string.IsNullOrEmpty(newQueueId) && newQueueId != oldQueueId)
        {
            vm.GetItem().QueueId = newQueueId;
            vm.RaiseQueueNameChanged();
        }

        NotifyList();
        if (!string.IsNullOrEmpty(oldQueueId) && oldQueueId != newQueueId)
            PumpQueue(oldQueueId);   // a freed slot in the old queue may let its next item start
        if (!string.IsNullOrEmpty(newQueueId))
            PumpQueue(newQueueId);
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
                else if (TryMarkAlreadyExists(vm, e))
                {
                    // The engine skipped the download because the file is already on disk
                    // (FileExistPolicy=IgnoreDownload). That's a success, not a failure (#issue).
                }
                else
                {
                    vm.ErrorMessage = e.Error != null
                        ? Describe(e.Error)
                        : "The connection was lost or timed out before the download finished. Please try again.";
                    vm.Status = DownloadStatus.Failed;
                    AppLog.Error($"Failed (interrupted): {vm.FileName ?? vm.Url}", e.Error);
                    if (NotifyFailedEnabled)
                        NotificationService.NotifyFailed(vm.FileName ?? vm.Url, vm.ErrorMessage);
                }
            }
            else if (e.Error != null)
            {
                vm.ErrorMessage = Describe(e.Error);
                vm.Status = DownloadStatus.Failed;
                AppLog.Error($"Failed: {vm.FileName ?? vm.Url}", e.Error);
                if (NotifyFailedEnabled)
                    NotificationService.NotifyFailed(vm.FileName ?? vm.Url, vm.ErrorMessage);
            }
            else if (IsCorruptedAfterResume(vm, e, out var finalBytes))
            {
                // It had a known size from a previous attempt, was resumed, and "completed" at a SMALLER
                // size than before → bytes are missing (e.g. the link expired and the server returned a
                // stub, or the source file changed). The saved file is corrupted/unhealthy, not a success.
                vm.Size = vm.PreAttemptSize; // restore the real size for display
                vm.ErrorMessage =
                    "This file looks corrupted — it finished smaller than its known size " +
                    $"({DownloadItemViewModel.FormatBytes(finalBytes)} of " +
                    $"{DownloadItemViewModel.FormatBytes(vm.PreAttemptSize ?? 0)}), so it is incomplete and " +
                    "may not open. Re-download it (with a fresh link if the old one expired).";
                vm.Status = DownloadStatus.Failed;
                AppLog.Error($"Corrupted resume: {vm.FileName} finished at {finalBytes} of expected {vm.PreAttemptSize}");
                if (NotifyFailedEnabled)
                    NotificationService.NotifyFailed(vm.FileName ?? vm.Url, vm.ErrorMessage);
            }
            else if (IsExpiredOrInvalidLink(vm, e))
            {
                // The engine "completed", but the payload is a small web page (HTML), not the requested
                // file — the classic expired / anti-bot link that returns an error page with HTTP 200.
                // Treat it as a failure with a clear message instead of a confusing "complete" stub.
                vm.ErrorMessage = Localizer.Instance["Error_LinkExpired"];
                vm.Status = DownloadStatus.Failed;
                AppLog.Error($"Link expired/invalid (server returned a page, not the file): {vm.FileName ?? vm.Url}");
                if (NotifyFailedEnabled)
                    NotificationService.NotifyFailed(vm.FileName ?? vm.Url, vm.ErrorMessage);
            }
            else
            {
                vm.Progress = 100;
                vm.Status = DownloadStatus.Completed;
                AppLog.Info($"Completed: {vm.FileName}");
                if (NotifyCompleteEnabled)
                    NotificationService.NotifyCompleted(vm.FileName);
                OfferPostDownloadAction(vm);
            }

            FinishTerminal(vm);
        });
    }

    private static bool IsCorruptedAfterResume(DownloadItemViewModel vm,
        System.ComponentModel.AsyncCompletedEventArgs e, out long finalBytes)
    {
        finalBytes = (e.UserState as DownloadPackage)?.ReceivedBytesSize
                     ?? vm.Download?.Package?.ReceivedBytesSize
                     ?? 0;
        return LooksCorruptedAfterResume(vm.PreAttemptSize, finalBytes);
    }

    /// <summary>Pure heuristic (testable): a download that was RESUMED (we already knew its size from a prior
    /// attempt) but then "completed" SMALLER than that known size is missing bytes — the saved file is
    /// corrupted/incomplete (e.g. an expired link returned a stub). A FIRST-time download finishing small is
    /// fine and never flagged (knownSizeBeforeAttempt is null). A healthy resume finishes at the full size.</summary>
    public static bool LooksCorruptedAfterResume(long? knownSizeBeforeAttempt, long finalBytes) =>
        knownSizeBeforeAttempt is > 0 && finalBytes > 0 && finalBytes < knownSizeBeforeAttempt.Value;

    /// <summary>An expired/anti-bot link often returns a small HTML error page with HTTP 200 instead of the
    /// file. Above this size we trust it's real content (real media files dwarf an error page).</summary>
    private const long ExpiredSuspectMaxBytes = 512 * 1024;

    /// <summary>Pure heuristic (testable): a "completed" download whose payload is small AND looks like a web
    /// page / markup (HTML/XML) rather than the requested file — typically an expired or anti-bot link. A
    /// genuine small file (non-markup content) is NOT flagged, nor is anything above the suspect size.</summary>
    public static bool LooksExpiredOrInvalid(string contentHead, long totalBytes)
    {
        if (totalBytes <= 0 || totalBytes > ExpiredSuspectMaxBytes)
            return false;
        if (string.IsNullOrWhiteSpace(contentHead))
            return false;

        var s = contentHead.TrimStart().ToLowerInvariant();
        return s.StartsWith("<!doctype html")
               || s.StartsWith("<html")
               || s.StartsWith("<?xml")
               || s.Contains("<head")
               || s.Contains("<body")
               || s.Contains("<title");
    }

    /// <summary>Reads the head of the just-completed file and applies <see cref="LooksExpiredOrInvalid"/>.</summary>
    private bool IsExpiredOrInvalidLink(DownloadItemViewModel vm, System.ComponentModel.AsyncCompletedEventArgs e)
    {
        var path = (e.UserState as DownloadPackage)?.FileName ?? vm.Download?.Package?.FileName;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        long len;
        try { len = new FileInfo(path).Length; }
        catch { return false; }
        if (len <= 0 || len > ExpiredSuspectMaxBytes)
            return false;

        try
        {
            var buffer = new byte[(int)Math.Min(len, 1024)];
            using var fs = File.OpenRead(path);
            var n = fs.Read(buffer, 0, buffer.Length);
            return LooksExpiredOrInvalid(System.Text.Encoding.UTF8.GetString(buffer, 0, n), len);
        }
        catch
        {
            return false; // unreadable — let it complete normally
        }
    }

    /// <summary>
    /// Shared post-terminal bookkeeping for a row that just reached an end state: free its queue slot,
    /// and evaluate "all downloads complete" ONLY when this row actually completed. The latter guard is
    /// what stops "Stop All" (which cancels rows → Stopped) from arming a shutdown just because a
    /// finished item is sitting in the list.
    /// </summary>
    private void FinishTerminal(DownloadItemViewModel vm)
    {
        TryStartNextInQueue(vm.GetItem().QueueId);
        if (vm.Status == DownloadStatus.Completed)
            MaybeAllCompleted();
        NotifyList();
    }

    /// <summary>If the resolving plugin offers an action for this completed item (e.g. "Add to Ollama"),
    /// surface it as an actionable notification. The row button appears via <see cref="PostDownloadActionLabel"/>.</summary>
    private void OfferPostDownloadAction(DownloadItemViewModel vm)
    {
        var label = PostDownloadActionLabel(vm);
        if (label == null)
            return;
        vm.RaisePostActionChanged();
        // The button carries the action's own name ("Add to Ollama") and the message says what clicking
        // does — a generic "Open" button read as open/unzip the file (author-reported confusion).
        NotificationService.ShowAction(
            label,
            string.Format(Localizer.Instance["PostAction_OfferMsg"], vm.FileName ?? vm.Url, label),
            () => _ = RunPostDownloadAction(vm),
            actionText: label);
    }

    /// <summary>
    /// Detects the "file already exists, so the engine ignored the download" case (FileExistPolicy =
    /// IgnoreDownload). The engine reports this as a cancel with no error and never fires DownloadStarted,
    /// which would otherwise look like a timeout failure. When the resolved file is actually present on
    /// disk, mark the row Completed (flagged AlreadyExisted) instead. Returns true if handled.
    /// </summary>
    /// <summary>True when a no-error cancel really means "the file is already on disk and the policy is
    /// to skip it" (FileExistPolicy=IgnoreDownload), not a transfer failure. Pure + testable.</summary>
    public static bool LooksAlreadyDownloaded(FileExistPolicy policy, string path) =>
        policy == FileExistPolicy.IgnoreDownload && !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private bool TryMarkAlreadyExists(DownloadItemViewModel vm, System.ComponentModel.AsyncCompletedEventArgs e)
    {
        var path = (e.UserState as DownloadPackage)?.FileName ?? vm.Download?.Package?.FileName;
        if (!LooksAlreadyDownloaded(_config?.Settings?.FileExistPolicy ?? FileExistPolicy.Delete, path))
            return false;

        // Backfill name/folder/size from the file the engine found (DownloadStarted never fired).
        var name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(vm.FileName) && !string.IsNullOrWhiteSpace(name))
            vm.FileName = name;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            vm.GetItem().SaveFolder = dir;
        try
        {
            var len = new FileInfo(path).Length;
            if (len > 0)
            {
                if (vm.Size is null or 0)
                    vm.Size = len;
                vm.Downloaded = len; // persist full bytes so the bar stays 100% after a restart
            }
        }
        catch { /* size is best-effort */ }

        vm.ErrorMessage = null;
        vm.Progress = 100;
        vm.AlreadyExisted = true;
        vm.Status = DownloadStatus.Completed;
        AppLog.Info($"Already downloaded (file exists, skipped): {vm.FileName}");
        if (NotifyCompleteEnabled)
            NotificationService.NotifyCompleted(vm.FileName);
        return true;
    }

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
