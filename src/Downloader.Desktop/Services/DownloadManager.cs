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

        FailStalledDownloads();
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
        var vm = AddCore(item, probeName: true);
        if (autoStart)
            PumpQueue(item.QueueId); // starts now if a slot is free, otherwise stays queued
        NotifyList();
        return vm;
    }

    /// <summary>How many rows a bulk add appends per UI-thread slice before yielding.</summary>
    internal const int AddSliceSize = 50;

    /// <summary>
    /// Adds many items without freezing the UI (the 2k-link "Download → 2 min hang"): rows are appended
    /// in slices of <see cref="AddSliceSize"/>, notifications fire once per slice (not per row), the
    /// queue pump runs once per slice, and a 1 ms await between slices lets the dispatcher breathe —
    /// the caller can close the Add dialog first and watch the rows stream in. Large batches also skip
    /// the per-item network name probe (mirror of the Add dialog's no-probing rule); names resolve when
    /// each download actually starts.
    /// </summary>
    public async Task AddRangeAsync(IReadOnlyList<DownloadItem> items, bool autoStart)
    {
        if (items == null || items.Count == 0)
            return;

        var probeNames = items.Count <= AddSliceSize; // small adds keep today's instant name preview
        var queues = new HashSet<string>();
        for (var i = 0; i < items.Count; i += AddSliceSize)
        {
            var end = Math.Min(i + AddSliceSize, items.Count);
            RunBatch(() =>
            {
                for (var j = i; j < end; j++)
                {
                    var item = items[j];
                    AddCore(item, probeNames);
                    queues.Add(item.QueueId);
                }
            });
            if (autoStart)
                foreach (var q in queues)
                    PumpQueue(q);
            await Task.Delay(1); // yield the UI thread between slices
        }
    }

    private DownloadItemViewModel AddCore(DownloadItem item, bool probeName)
    {
        if (string.IsNullOrWhiteSpace(item.QueueId) && _config != null)
            item.QueueId = _config.DefaultQueue.Id;

        var vm = new DownloadItemViewModel(item, this);
        Items.Add(vm);

        if (string.IsNullOrWhiteSpace(item.FileName))
        {
            // Free, instant: any name embedded in the URL — so no row ever shows "Fetching name…"
            // even in a huge batch where the network probe is skipped.
            var quick = UrlResolver.NameFromUrl(item.Url);
            if (!string.IsNullOrWhiteSpace(quick))
                vm.PreviewName = quick;
            // Full name+size probe in the background (#4) — single adds only; a 2k batch must not
            // fire 2k probes.
            if (probeName)
                _ = ResolvePreviewNameAsync(vm);
        }

        NotifyList(); // coalesced to once per slice inside RunBatch
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
        // Which address leads THIS attempt. The engine pins each chunk to one of the urls it is given and
        // probes the file with the first one only, so a refused lead is not something the extra urls can
        // rescue — the app has to lead with a different one and try again (see TryNextUrl).
        urls = OrderUrlsForAttempt(urls, vm.UrlAttempt);

        var folder = string.IsNullOrWhiteSpace(item.SaveFolder)
            ? _config?.Settings?.DefaultSavePath
            : item.SaveFolder;
        item.SaveFolder = folder;
        item.LastTry = DateTime.Now;
        vm.ErrorMessage = null;
        vm.AlreadyExisted = false;
        vm.IsRefreshingLink = false;
        // Capture the known size before progress events overwrite it — only meaningful when resuming
        // (we already had bytes + a real size). Used to spot an expired link returning a tiny file.
        vm.PreAttemptSize = item.Downloaded > 0 ? item.Size : null;
        vm.Status = DownloadStatus.Running;
        _allCompleteFired = false; // a new run means "all complete" can fire again when it drains
        EnsureUiPump();
        AppLog.Info($"Starting: {urls[0]}{(urls.Length > 1 ? $" (+{urls.Length - 1} mirror[s])" : "")}");

        var configuration = _config?.Settings?.ToConfiguration() ?? new DownloadConfiguration();
        // A server that refused several simultaneous requests gets exactly one, this attempt only. The
        // configured maximum stays what it is: it is a ceiling for downloads that can use it, not a number
        // every server must accept (issue #9).
        if (vm.ForceSingleConnection)
        {
            configuration.ChunkCount = 1;
            configuration.ParallelDownload = false;
        }
        // A per-item speed cap set in the details dialog wins over the global limit and survives restarts.
        if (item.HasCustomSpeedLimit)
            configuration.MaximumBytesPerSecond = item.CustomSpeedLimitBytesPerSecond <= 0
                ? 0 : item.CustomSpeedLimitBytesPerSecond;
        // Cookies/headers/referer supplied with the link (issue #7) apply to THIS download's requests,
        // overriding the global request settings — the resolve and the bytes now use the same context.
        ApplyRequestContext(configuration, item.Request);
        EnsureCookieFile(item);
        vm.Configuration = configuration; // keep a handle so the details dialog can tweak it live
        // Captured NOW: the engine mutates this configuration while it runs, so it cannot be asked
        // afterwards how many connections the attempt actually planned to use.
        vm.PlannedConnections = ConnectionsInFlight(configuration);
        vm.LastProgressUtc = DateTime.UtcNow; // the watchdog measures silence from here
        var fileName = item.FileName;

        // Build + start entirely off the UI thread. Resolving redirects and the engine's synchronous
        // setup must not run on the dispatcher, or selecting many items and pressing Start freezes it.
        // Whatever this row was doing before may not be finished with the file yet. The engine raises its
        // completion BEFORE the downloaded file is in place, and the row disposes the engine from inside
        // that event — so a Retry/Resume that builds a new engine straight away races the old one's final
        // flush and cleanup over the SAME .download path, and the loser's bytes go nowhere: the new engine
        // reports every byte received while the folder is empty and the row never leaves "downloading".
        var previousAttempt = vm.Attempt;
        try
        {
            var attempt = Task.Run(async () =>
            {
                // Let the previous attempt actually finish before touching its file. Bounded, because a
                // wedged attempt must not block this one for ever — resuming onto a file the old engine
                // is still holding is no worse than what happened before this wait existed.
                if (previousAttempt is { IsCompleted: false })
                    await Task.WhenAny(previousAttempt, Task.Delay(TimeSpan.FromSeconds(10)))
                        .ConfigureAwait(false);

                // The user may stop/remove the row while this off-thread setup runs (Stop right after
                // Add): Cancel then finds no engine handle to cancel and just marks the row Stopped —
                // so never start an engine for a row that's no longer Running.
                if (vm.Status != DownloadStatus.Running)
                    return;

                // A plugin transfer provider that claims the URL (e.g. "websitezip:", "magnet:") owns the
                // whole download — checked before link resolution so a claimed scheme never round-trips
                // through resolvers. RunTransferAsync owns the row's terminal state.
                var transferProvider = _plugins?.FindTransferProvider(urls[0]);
                if (transferProvider != null)
                {
                    // The OWNING plugin, which on this route is the one whose transfer provider claimed
                    // the link — it has no resolver to be found by. Without this the finished row never
                    // offers that plugin's post-download action.
                    item.ResolverPluginId ??= _plugins.FindTransferProviderPluginId(urls[0])
                                              ?? _plugins.FindResolverPluginId(urls[0]);
                    await RunTransferAsync(vm, transferProvider, urls[0], folder).ConfigureAwait(false);
                    return;
                }

                // Resolve the plan: reuse a persisted one (restart / resume of a multi-part download) or
                // ask the plugins fresh. A multi-part / post-process plan is run by the plan runner; a
                // single-part plan just rewrites the URL + name and falls through to the normal engine path.
                var persisted = PersistedPlan.FromJson(item.PlanJson);
                if (persisted == null)
                {
                    var plan = await ResolvePlanAsync(urls[0], default, item.CookieFilePath, item.VariantId, item.Request).ConfigureAwait(false);
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

                // DownloadService (not the single-URL DownloadBuilder) so the item's other addresses can
                // spread a download's chunks, and the engine's internal logs flow into our log file via the
                // shared logger factory. NOTE: those extra urls are load spreading, NOT failover — a chunk
                // is pinned to one of them and the file probe uses the lead only. Falling back to another
                // address is the app's job, above.
                var download = new DownloadService(configuration, AppLog.Factory);
                // Subscribe before starting so no early event is missed (handlers marshal to UI themselves).
                Attach(vm, download);

                // Re-check after the (slow) redirect resolution: a Stop that arrived meanwhile marked
                // the row Stopped without an engine to cancel — release this one instead of starting it.
                if (vm.Status != DownloadStatus.Running)
                {
                    ReleaseEngine(vm);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(fileName))
                    await download.DownloadFileTaskAsync(urls, Path.Combine(folder ?? string.Empty, fileName))
                        .ConfigureAwait(false);
                else
                    await download.DownloadFileTaskAsync(urls, new DirectoryInfo(folder ?? "."))
                        .ConfigureAwait(false);
            });
            vm.Attempt = attempt; // the next attempt for this row waits on it
            await attempt.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to start: {urls[0]}", ex);
            OnUi(() =>
            {
                // DescribeFailure, not Describe: a resolver that claimed this link has its own reason to
                // give, and an extension hand-off must not be described as an expired link.
                vm.ErrorMessage = DescribeFailure(ex, item);
                vm.Status = DownloadStatus.Failed;
                NotifyList();
            });
        }
        finally
        {
            // A browser-supplied cookie file is a transient secret — delete it right after this attempt
            // (success or failure), whether or not the plugin path actually consumed it.
            DeleteCookieFile(item);
        }
    }

    /// <summary>
    /// Applies a download's own cookies, headers and referer (issue #7) to the engine configuration it is
    /// about to run with. Per-item values win over the global settings already in <paramref name="cfg"/>.
    /// Pure and side-effect-free apart from <paramref name="cfg"/>, so it is unit-tested directly.
    /// </summary>
    internal static void ApplyRequestContext(DownloadConfiguration cfg, RequestContext ctx)
    {
        if (cfg == null || ctx == null || ctx.IsEmpty)
            return;

        cfg.RequestConfiguration ??= new RequestConfiguration();
        var req = cfg.RequestConfiguration;

        if (ctx.Headers is { Count: > 0 })
            foreach (var (key, value) in ctx.Headers)
                SetHeader(req, key, value);

        // Set after the headers so an explicit `referer` field wins over a Referer header, and both win
        // over the global DownloadSettings.Referer that ToConfiguration() already applied.
        if (!string.IsNullOrWhiteSpace(ctx.Referer))
            req.Referer = ctx.Referer;

        if (ctx.Cookies is { Count: > 0 })
        {
            req.CookieContainer ??= new System.Net.CookieContainer();
            foreach (var cookie in ctx.Cookies)
                TryAddCookie(req.CookieContainer, cookie);
        }
    }

    /// <summary>Sets one request header, routing the four the engine models as properties (a
    /// <see cref="System.Net.WebHeaderCollection"/> either rejects those or the engine ignores them).</summary>
    internal static void SetHeader(RequestConfiguration req, string key, string value)
    {
        if (req == null || string.IsNullOrWhiteSpace(key))
            return;

        switch (key.Trim().ToLowerInvariant())
        {
            case "user-agent": req.UserAgent = value; return;
            case "referer":
            case "referrer": req.Referer = value; return;
            case "accept": req.Accept = value; return;
            case "content-type": req.ContentType = value; return;
        }

        // Indexer, not Add: a per-item header replaces a global one of the same name instead of appending.
        try { (req.Headers ??= new System.Net.WebHeaderCollection())[key] = value; }
        catch { /* skip a header the framework restricts — never fail the download over one header */ }
    }

    /// <summary>Adds one browser cookie to the jar the engine will send. A cookie the framework rejects
    /// (bad name/domain/value) is skipped, never fatal. Values are never logged.</summary>
    private static void TryAddCookie(System.Net.CookieContainer jar, CookieDto c)
    {
        if (jar == null || string.IsNullOrEmpty(c?.Name) || string.IsNullOrEmpty(c.Domain))
            return;
        try
        {
            var cookie = new System.Net.Cookie(
                c.Name,
                c.Value ?? string.Empty,
                string.IsNullOrEmpty(c.Path) ? "/" : c.Path,
                c.Domain)
            {
                Secure = c.Secure
            };
            if (c.Expires is > 0)
                cookie.Expires = DateTimeOffset.FromUnixTimeSeconds(c.Expires.Value).UtcDateTime;
            jar.Add(cookie);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Skipped a cookie the framework rejected: {ex.GetType().Name}");
        }
    }

    /// <summary>Re-creates the transient Netscape cookie file for this attempt when the item still has
    /// cookies but the previous attempt's file was already deleted — so a retry isn't silently anonymous.</summary>
    private static void EnsureCookieFile(DownloadItem item)
    {
        if (item?.Request?.Cookies is not { Count: > 0 })
            return;
        if (!string.IsNullOrEmpty(item.CookieFilePath) && File.Exists(item.CookieFilePath))
            return;
        try { item.CookieFilePath = CookieFile.WriteTempFile(item.Request.Cookies); }
        catch (Exception ex) { AppLog.Warn($"Couldn't write temp cookie file: {ex.Message}"); }
    }

    /// <summary>Flattens a download's request context into the single header bag a resolver sees: the item's
    /// headers plus its referer as a normal <c>Referer</c> entry (the referer field wins). Returns
    /// null when there is nothing to send, so plugins keep their "no options" fast path.</summary>
    internal static IReadOnlyDictionary<string, string> ResolveHeaders(RequestContext context)
    {
        if (context == null)
            return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (context.Headers is { Count: > 0 })
            foreach (var (key, value) in context.Headers)
                if (!string.IsNullOrWhiteSpace(key))
                    headers[key] = value;
        // Last, so the purpose-built `referer` field wins over a Referer header — same precedence as
        // ApplyRequestContext uses on the engine side.
        if (!string.IsNullOrWhiteSpace(context.Referer))
            headers["Referer"] = context.Referer;

        // A plugin's own HttpClient never sees RequestConfiguration.CookieContainer (that's the engine's),
        // so the only way a resolver — and through it the assembly-time key fetch — can present the
        // download's session is as a Cookie header. Synthesize it here rather than teaching ResolveOptions
        // about cookies: one place, no SDK change, and it is exactly the wire form. An explicit Cookie
        // header the caller supplied wins, since it was stated deliberately.
        if (context.Cookies is { Count: > 0 } && !headers.ContainsKey("Cookie"))
        {
            var pairs = context.Cookies
                .Where(c => !string.IsNullOrWhiteSpace(c?.Name))
                .Select(c => $"{c.Name}={c.Value}")
                .ToList();
            if (pairs.Count > 0)
                headers["Cookie"] = string.Join("; ", pairs);
        }

        return headers.Count > 0 ? headers : null;
    }

    /// <summary>Best-effort delete + clear of an item's transient extension-supplied cookie file.</summary>
    internal static void DeleteCookieFile(DownloadItem item)
    {
        var path = item?.CookieFilePath;
        if (string.IsNullOrEmpty(path))
            return;
        item.CookieFilePath = null;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { AppLog.Warn($"Couldn't delete temp cookie file: {ex.Message}"); }
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
        string url, System.Threading.CancellationToken cancellationToken, string cookieFilePath = null,
        string variantId = null, RequestContext context = null)
    {
        if (_plugins == null)
            return null;
        try
        {
            var headers = ResolveHeaders(context);
            var options = string.IsNullOrEmpty(cookieFilePath) && string.IsNullOrEmpty(variantId) && headers == null
                ? null
                : new Plugins.ResolveOptions
                {
                    CookieFilePath = cookieFilePath,
                    VariantId = variantId,
                    Headers = headers
                };
            return await _plugins.ResolveAsync(url, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A resolver that CLAIMED this link and then failed has something to say — that the page is a
            // live stream, that the site wants a session, that the tool couldn't be verified. Falling
            // through to "use the link as-is" downloads the page's HTML instead and reports whatever that
            // turns into, so the real reason never reaches the user. Only an UNCLAIMED link falls through.
            if (_plugins.FindResolver(url) != null)
            {
                AppLog.Error($"Plugin resolve failed for {url}", ex);
                throw new PluginResolveException(ex.Message, ex);
            }

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
    /// <summary>The message a failed row shows. An expired link that the app could not refresh by itself
    /// gets wording that names the real problem and points at the fix (paste a fresh link in Details, #6)
    /// instead of a bare "Network error: 403".</summary>
    private static string DescribeFailure(Exception ex) => DescribeFailure(ex, item: null);

    /// <summary>As above, but knowing which download failed. A download the browser extension handed over
    /// while its OWN copy kept running must not be described as an expired link the user has to replace:
    /// the user has not lost anything, the browser is still fetching it. Naming the wrong problem sends
    /// people hunting for a fresh link they never needed (issue #9).</summary>
    /// <param name="refusedEvenAlone">The download had already been retried over a single connection and
    /// was refused again — which changes what the failure means, and what the user should do about it.</param>
    private static string DescribeFailure(Exception ex, DownloadItem item, bool refusedEvenAlone = false)
    {
        // A resolver's own explanation is already the clearest thing anyone can say about the link, so it
        // is passed through verbatim — except for the one case that used to be worded misleadingly: a site
        // that wants a signed-in session. The people who see it ARE signed in; what is missing is the
        // session reaching the app, which is what sending the page from the extension does.
        if (Unwrap(ex).Any(e => e is EmptyDownloadException))
            return Localizer.Instance["Error_NothingDownloaded"];

        if (Unwrap(ex).Any(e => e is DownloadStalledException))
            return Localizer.Instance["Error_DownloadStalled"];

        if (ex is PluginResolveException resolve)
            return LooksLikeNeedsBrowserSession(resolve.Message)
                ? Localizer.Instance["Error_SiteNeedsBrowserSession"]
                : resolve.Message;

        // A download that already spent its single-connection retry and still failed with a 403 was
        // refused by the server, not by a dead link. Telling that user to find a fresh link sends them
        // hunting for something they never lost — what they need is a lower connection count (issue #9).
        if (refusedEvenAlone && LooksLikeConcurrencyRefusal(ex, connectionsInFlight: 2))
            return Localizer.Instance["Error_ServerRefusedConnections"];

        if (!LooksLikeExpiredLinkError(ex))
            return Describe(ex);
        return item?.FromBrowserDownload == true
            ? Localizer.Instance["Error_BrowserHandoffRefused"]
            : Localizer.Instance["Error_LinkExpiredRefresh"];
    }

    /// <summary>Does a resolver's failure mean "this site only serves a signed-in session"? Matched on the
    /// wording plugins use, so the app can say the one useful thing (send it from the extension) in the
    /// user's own language instead of repeating the plugin's English.</summary>
    internal static bool LooksLikeNeedsBrowserSession(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;
        var lower = message.ToLowerInvariant();
        return lower.Contains("signed-in session") || lower.Contains("browser session")
            || lower.Contains("signed in session");
    }

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

    /// <summary>How many times the app re-resolves an item's original link by itself after an expired-link
    /// failure before giving up and asking the user for a fresh one (issue #6).</summary>
    public const int MaxAutoLinkRefreshAttempts = 2;

    /// <summary>Pure helper (testable): does this failure mean "the link is no longer valid" — the signature
    /// on a time-limited URL expired, or the server withdrew it? Those are the statuses a CDN answers with
    /// once a signed link times out (401/403 typically, 410 when it is explicit, 404 on some CDNs). A
    /// timeout, a socket error or a 5xx is a transient problem with a still-valid link and is NOT matched.</summary>
    public static bool LooksLikeExpiredLinkError(Exception ex)
    {
        foreach (var e in Unwrap(ex))
            if (e is System.Net.Http.HttpRequestException { StatusCode: { } status } &&
                status is System.Net.HttpStatusCode.Unauthorized
                    or System.Net.HttpStatusCode.Forbidden
                    or System.Net.HttpStatusCode.NotFound
                    or System.Net.HttpStatusCode.Gone)
                return true;
        return false;
    }

    /// <summary>Flattens an exception into itself, its inner exceptions and any aggregated ones — the engine
    /// wraps a chunk's failure before it reaches the completion event.</summary>
    private static IEnumerable<Exception> Unwrap(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            yield return e;
            if (e is AggregateException agg)
                foreach (var inner in agg.InnerExceptions)
                foreach (var nested in Unwrap(inner))
                    yield return nested;
        }
    }

    /// <summary>Pure helper (testable): may a download with NO bytes yet still be worth one automatic link
    /// refresh? Normally no — a link that never delivered a byte is a bad link, not an expired one. The
    /// exception is a download the browser extension took over: the browser was fetching that link moments
    /// earlier, so the usual cause of an immediate 401/403/410 is a single-use address the browser already
    /// spent, and re-resolving the original link mints a fresh one (issue #9, Softpedia "Secure Download").</summary>
    public static bool WorthRefreshingFromZeroBytes(DownloadItem item) => item?.FromBrowserDownload == true;

    /// <summary>
    /// An expired signed link is usually reachable again by re-resolving the ORIGINAL url the user pasted:
    /// it redirects to a freshly signed target. <see cref="Start"/> always re-resolves from
    /// <c>item.Urls</c>, so re-queueing the item is the whole fix — and the partial file is kept, so the
    /// download continues where it stopped. Bounded by <see cref="MaxAutoLinkRefreshAttempts"/>, and only
    /// for a download that already has bytes: a link that never worked is a bad link, not an expired one.
    /// Returns true when the item was re-queued (the caller must then NOT mark it Failed).
    /// </summary>
    private bool TryAutoRefreshLink(DownloadItemViewModel vm, Exception error)
    {
        if (!LooksLikeExpiredLinkError(error))
            return false;
        if (!WorthRefreshingFromZeroBytes(vm.GetItem()) && vm.GetItem().Downloaded <= 0)
            return false;
        if (vm.LinkRefreshAttempts >= MaxAutoLinkRefreshAttempts)
            return false;

        vm.LinkRefreshAttempts++;
        vm.IsRefreshingLink = true; // labels the queued gap; not an error, so no red banner
        // Queued, not Failed: the pump picks up a Created row, and the grid shows the honest "Queued"
        // badge instead of flashing a failure the app is about to fix by itself.
        vm.Status = DownloadStatus.Created;
        vm.Speed = 0;
        AppLog.Info($"Link looks expired, refreshing it (attempt {vm.LinkRefreshAttempts}" +
                    $"/{MaxAutoLinkRefreshAttempts}): {vm.Url}");
        ReleaseEngine(vm); // the failed engine is done; Start builds a fresh one for the new attempt
        // Re-queue after this completion callback has finished rather than starting a download from
        // inside the old engine's event handler.
        Dispatcher.UIThread.Post(() => RequeueForRefresh(vm));
        return true;
    }

    /// <summary>
    /// The single place a failed attempt becomes a failed row. An expired link is refreshed automatically
    /// first (issue #6) — that is a retry, not a failure, so the row keeps no error and nothing is notified.
    /// </summary>
    /// <returns>True when the failure was absorbed by an automatic link refresh (the row is queued for
    /// another attempt); false when the row was marked Failed.</returns>
    private bool HandleFailure(DownloadItemViewModel vm, Exception error, string fallbackMessage, string logPrefix)
    {
        // Another ADDRESS first, then a fresh signature for the current one: when a download carries both
        // the end of the browser's redirect chain and the link that was clicked, the second address is
        // usually the one that works, while re-resolving a spent single-use address just spends it again.
        if (TryNextUrl(vm, error))
            return true;
        // Then the same address over a single connection: a 403 raised while several chunks were in
        // flight is often the server refusing the CONCURRENCY, not the address (issue #9 — the reporter
        // measured one mirror serving happily at 1-3 connections and refusing at 4+).
        if (TryReduceConnections(vm, error))
            return true;
        if (TryAutoRefreshLink(vm, error))
            return true;

        // "The server refused the connections" is only the better explanation while nothing has suggested
        // the link itself is gone. Once the app has re-resolved the address even once and still been
        // refused, the link is the story — telling that user to lower a setting sends them after something
        // that was never the problem.
        var refusedEvenAlone = vm.ForceSingleConnection && vm.LinkRefreshAttempts == 0;
        vm.ErrorMessage = error != null
            ? DescribeFailure(error, vm.GetItem(), refusedEvenAlone)
            : fallbackMessage;
        vm.Status = DownloadStatus.Failed;
        AppLog.Error($"{logPrefix}: {vm.FileName ?? vm.Url}", error);
        if (NotifyFailedEnabled)
            NotificationService.NotifyFailed(vm.FileName ?? vm.Url, vm.ErrorMessage);
        return false;
    }

    /// <summary>The ONE address this attempt uses.
    /// <para>
    /// The engine takes a list and spreads a download's chunks across all of it, which is load spreading
    /// between equivalent mirrors — and the addresses a download actually carries are not equivalent. They
    /// are "the link the user clicked" and "where the browser ended up", and handing both over meant a
    /// dead or refusing address kept receiving chunks: downloads finished with an empty file and a green
    /// row, and a retry inherited the same poison. One address per attempt, walked in order by
    /// <see cref="TryNextUrl"/>, tries every address the download has while keeping each attempt's outcome
    /// attributable to the address that produced it.
    /// </para>
    /// Pure and total: an out-of-range or negative attempt falls back to the first address, so a stale
    /// counter can never leave a download with nothing to request.</summary>
    internal static string[] OrderUrlsForAttempt(string[] urls, int attempt)
    {
        if (urls.Length == 0)
            return urls;
        var index = attempt >= 0 && attempt < urls.Length ? attempt : 0;
        return new[] { urls[index] };
    }

    /// <summary>Pure helper (testable): could a DIFFERENT address plausibly succeed where this failure
    /// happened? A server that refused, lost or hid the address, or never answered at all, says nothing
    /// about the other addresses the download carries. A cancel, a disk error or a timeout does — retrying
    /// those against another address would just repeat the same problem more slowly.</summary>
    public static bool CanRetryWithAnotherUrl(Exception ex)
    {
        if (ex == null)
            return false;
        foreach (var e in Unwrap(ex))
        {
            // Finished with nothing to show for it, or stopped responding altogether: a different
            // address is precisely what might help in both cases.
            if (e is EmptyDownloadException or DownloadStalledException)
                return true;
            if (e is OperationCanceledException or IOException or UnauthorizedAccessException)
                return false;
            if (e is System.Net.Http.HttpRequestException { StatusCode: { } status })
                return status is System.Net.HttpStatusCode.Unauthorized
                    or System.Net.HttpStatusCode.Forbidden
                    or System.Net.HttpStatusCode.NotFound
                    or System.Net.HttpStatusCode.Gone;
            // No status at all: the request never completed (connection refused, DNS, reset). Another
            // address is exactly what might work.
            if (e is System.Net.Http.HttpRequestException or System.Net.Sockets.SocketException)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Promote the next address and queue another attempt. This is the failover the engine does NOT do:
    /// its extra urls spread a download's chunks, but a refused lead fails the whole download while a
    /// perfectly good second address sits unused — which is how handing the app "a fallback" silently
    /// achieved nothing (issue #9, v2.8.0).
    /// </summary>
    /// <returns>True when another attempt was queued (the caller must then NOT mark the row Failed).</returns>
    private bool TryNextUrl(DownloadItemViewModel vm, Exception error)
    {
        var urls = vm.GetItem()?.Urls;
        if (urls == null || urls.Count <= 1)
            return false;
        // One leading attempt per address, so a set of dead addresses fails once instead of looping.
        if (vm.UrlAttempt + 1 >= urls.Count)
            return false;
        if (!CanRetryWithAnotherUrl(error))
            return false;

        vm.UrlAttempt++;
        vm.Speed = 0;
        vm.Status = DownloadStatus.Created; // queued, not failed: the app is still working on it
        AppLog.Info($"Address {vm.UrlAttempt} of {urls.Count} was refused; trying the next one");
        ReleaseEngine(vm);
        Dispatcher.UIThread.Post(() => RequeueForRefresh(vm));
        return true;
    }

    /// <summary>Delete the in-progress file the engine writes (<c>&lt;name&gt;.download</c>), so the next
    /// attempt builds a fresh package instead of resuming the old one's layout. Best-effort: a file that
    /// cannot be deleted just means the retry resumes, which is what would have happened anyway.</summary>
    private static void DiscardPartialFile(DownloadItemViewModel vm)
    {
        var path = vm?.GetItem()?.FilePath;
        if (string.IsNullOrWhiteSpace(path))
            return;
        foreach (var candidate in new[] { path + ".download", path })
        {
            try
            {
                if (File.Exists(candidate))
                    File.Delete(candidate);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn($"Couldn't clear the partial file before retrying: {ex.Message}");
            }
        }
        vm.Downloaded = 0;
        vm.Progress = 0;
    }

    /// <summary>Pure helper (testable): does this failure look like the server refusing the number of
    /// simultaneous connections rather than the address itself? Only a 403 qualifies — the status a server
    /// uses to refuse a request it understood — and only when more than one connection was actually in
    /// flight. A 401/404/410 is about the address, and a 403 to a lone request is a real refusal, so
    /// neither is worth spending an attempt on.</summary>
    public static bool LooksLikeConcurrencyRefusal(Exception ex, int connectionsInFlight)
    {
        if (ex == null || connectionsInFlight <= 1)
            return false;
        foreach (var e in Unwrap(ex))
        {
            if (e is System.Net.Http.HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden })
                return true;
            // Finishing with NOTHING over several connections says the same thing: a server that refuses
            // ranged requests answers each chunk with a refusal, and what reaches here is an empty
            // download rather than any one status. Over a single connection it means something else, and
            // the guard above already excluded that.
            if (e is EmptyDownloadException)
                return true;
        }
        return false;
    }

    /// <summary>How many requests this attempt had in flight at once, from the configuration it ran with.</summary>
    private static int ConnectionsInFlight(DownloadConfiguration configuration)
    {
        if (configuration == null || !configuration.ParallelDownload)
            return 1;
        return configuration.ParallelCount > 0 ? configuration.ParallelCount : Math.Max(1, configuration.ChunkCount);
    }

    /// <summary>Queue one more attempt over a single connection. Bounded to once per download: if a lone
    /// request is refused too, the server means it.</summary>
    /// <returns>True when another attempt was queued (the caller must then NOT mark the row Failed).</returns>
    private bool TryReduceConnections(DownloadItemViewModel vm, Exception error)
    {
        if (vm.ForceSingleConnection) // already tried; a second refusal is the server's real answer
            return false;
        // Only on a download that has not yet been through the expired-link machinery. The two explanations
        // for a 403 — "too many connections" and "this address is gone" — are indistinguishable in the
        // response, so they are kept disjoint: the connection backoff gets the FIRST attempt, and once a
        // link has been re-resolved even once, the link path owns the download's fate.
        if (vm.LinkRefreshAttempts > 0)
            return false;
        if (!LooksLikeConcurrencyRefusal(error, vm.PlannedConnections))
            return false;

        vm.ForceSingleConnection = true;
        vm.Speed = 0;
        vm.Status = DownloadStatus.Created;
        // The partial file has to go with it. A resumed download keeps the chunk layout its package was
        // created with, so asking for one connection while a half-finished eight-chunk file is on disk
        // changes nothing — the retry would re-open the same eight ranges and be refused again. What is
        // discarded is whatever the refusing server let through, which is nothing anyone can use.
        DiscardPartialFile(vm);
        AppLog.Info("The server refused several connections at once; retrying with one");
        ReleaseEngine(vm);
        Dispatcher.UIThread.Post(() => RequeueForRefresh(vm));
        return true;
    }

    /// <summary>Re-queues an item for another attempt WITHOUT resetting its automatic-refresh counter (that
    /// reset is reserved for the user's own Retry/Resume), so a dead link can't retry forever.</summary>
    private void RequeueForRefresh(DownloadItemViewModel vm)
    {
        // This runs one dispatcher hop after the failure that asked for it, and in that gap the freed
        // queue slot can already have started the next attempt (the same failure frees the slot). Marking
        // an attempt that is ALREADY RUNNING as queued again made the pump start a second engine for the
        // same row: two downloads wrote the same .download file, one deleted the other's, and the row sat
        // Running for ever with no error and no file — the failover hang behind issue #9.
        if (vm.Status is DownloadStatus.Running or DownloadStatus.Completed)
            return;

        vm.GetItem().PlanJson = null;
        vm.Status = DownloadStatus.Created;
        EnsureQueueRunning(vm.GetItem().QueueId);
        PumpQueue(vm.GetItem().QueueId);
        NotifyList();
    }

    public void Pause(DownloadItemViewModel vm)
    {
        // Only a running download can be paused. Guard so a bulk "Pause" over a mixed selection can't
        // touch completed/failed/queued rows.
        if (vm.Status != DownloadStatus.Running)
            return;
        vm.Download?.Pause();
        // A multi-part plan has several part engines in flight; vm.Download is only the newest of them, so
        // this is what makes Pause actually stop the transfer (and stop the runner starting the next part).
        vm.PlanControl?.Pause();
        vm.ActiveTransfer?.Pause();
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

        // The user asked for this attempt, so the automatic budgets start over (issue #6): a link that
        // was dead yesterday may well be fine today, and so may the address that was refused.
        vm.LinkRefreshAttempts = 0;
        vm.UrlAttempt = 0;
        vm.ForceSingleConnection = false;

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
        // Cancel every in-flight part, un-pausing them first: a suspended engine never completes its task,
        // so stopping a PAUSED plan would otherwise leave the runner waiting on it forever.
        vm.PlanControl?.CancelAll();
        vm.TransferCancellation?.Cancel();
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
        vm.LinkRefreshAttempts = 0; // a user-initiated retry restarts the automatic budgets (#6)
        vm.UrlAttempt = 0;          // …including which address leads, so Retry starts from the first again
        vm.ForceSingleConnection = false;
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
            vm.TransferCancellation?.Cancel();
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

    /// <summary>Test seam: fire the stats pump event so status-bar readouts recompute.</summary>
    public void RaiseStatsForTest() => StatsChanged?.Invoke();

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

    /// <summary>Test seam: simulate a failed attempt reaching the completion handler (same code path the
    /// engine's completion event uses), so the expired-link auto-refresh can be exercised without a server.
    /// Returns true when the failure was turned into an automatic link refresh instead of a failure.</summary>
    public bool RaiseFailedForTest(DownloadItemViewModel vm, Exception error)
    {
        var refreshed = HandleFailure(vm, error, "connection lost", "Failed (test)");
        FinishTerminal(vm);
        return refreshed;
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
        if (vm.Status == DownloadStatus.Paused && (vm.Download != null || vm.ActiveTransfer != null))
        {
            vm.Download?.Resume();
            vm.PlanControl?.Resume(); // every paused part, and re-open the gate on starting new ones
            vm.ActiveTransfer?.Resume();
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
        // This engine's events are only meaningful while it IS the row's attempt. A superseded engine
        // (one we failed over from, or backed off from) can still deliver a completion afterwards, and
        // acting on it wrote the outcome of an abandoned attempt over the live one — a row marked
        // Completed with no file, because the attempt that actually produced the file had not finished.
        var generation = ++vm.AttemptGeneration;
        bool Stale() => vm.AttemptGeneration != generation;

        download.DownloadStarted += (_, e) => OnUi(() =>
        {
            if (Stale()) return;
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
            if (Stale()) return;
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
            if (Stale()) return;
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
                    HandleFailure(vm, e.Error,
                        "The connection was lost or timed out before the download finished. Please try again.",
                        "Failed (interrupted)");
                }
            }
            else if (e.Error != null)
            {
                HandleFailure(vm, e.Error, null, "Failed");
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
            else if (NothingWasDownloaded(vm, e, out var savedPath))
            {
                // The engine reported success but there is no file, or an empty one. This really happens:
                // when a download carries several addresses the engine spreads its chunks across them, and
                // a lead address that refuses every request can leave it "finished" having written nothing
                // — a green row and an empty folder, which is worse than an honest failure. Route it
                // through the normal failure path so the next address is tried (issue #9).
                AppLog.Error($"Completed with no data: {vm.FileName ?? vm.Url} (expected at {savedPath})");
                HandleFailure(vm, EmptyDownloadError(), Localizer.Instance["Error_NothingDownloaded"],
                    "Completed with no data");
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
                vm.LinkRefreshAttempts = 0; // it worked — the budgets are spent only on live trouble
                vm.UrlAttempt = 0;
                vm.ForceSingleConnection = false;
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

    /// <summary>How long a running download may show no sign of life before the app gives up on the
    /// attempt. Generous on purpose: the engine has its own per-block and per-request timeouts, so this
    /// only catches a download nothing else will ever end.</summary>
    internal static TimeSpan StallTimeout { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>Pure helper (testable): has this attempt gone silent for long enough to give up on?
    /// <para>
    /// Only a row that is Running with a live engine and no post-processing stage qualifies. Assembling a
    /// multi-part download or running ffmpeg can take minutes without a single byte of progress, and a
    /// paused or queued row is not running at all — failing either of those would be the watchdog causing
    /// the problem it exists to catch.
    /// </para></summary>
    public static bool IsStalled(DownloadStatus status, bool hasLiveEngine, string planStage,
        DateTime lastProgressUtc, DateTime nowUtc)
    {
        if (status != DownloadStatus.Running || !hasLiveEngine)
            return false;
        if (!string.IsNullOrEmpty(planStage))
            return false; // post-processing: no bytes move, and that is normal
        return nowUtc - lastProgressUtc >= StallTimeout;
    }

    /// <summary>
    /// Ends attempts that have gone silent. The engine can finish without ever raising a completion —
    /// against a server that refuses every request it sometimes emits nothing at all — leaving a row
    /// Running for ever with no error, no file and nothing to retry. Nothing else in the app can observe
    /// that, so the pump does: a silent attempt is failed through the normal path, which then tries the
    /// next address or reports it honestly.
    /// </summary>
    private void FailStalledDownloads()
    {
        var now = DateTime.UtcNow;
        // ToList: HandleFailure can re-queue an item and change the collection while we walk it.
        foreach (var vm in Items.ToList())
        {
            if (!IsStalled(vm.Status, vm.Download != null, vm.PlanStage, vm.LastProgressUtc, now))
                continue;

            AppLog.Error($"No progress for {StallTimeout.TotalSeconds:0}s and no completion: {vm.FileName ?? vm.Url}");
            vm.LastProgressUtc = now; // don't re-trigger while the failure is being handled
            ReleaseEngine(vm);
            HandleFailure(vm, new DownloadStalledException(
                    $"the download stopped responding for {StallTimeout.TotalSeconds:0} seconds"),
                Localizer.Instance["Error_DownloadStalled"], "Stalled");
            NotifyList();
        }
    }

    /// <summary>Pure helper (testable): did a "successful" download actually produce nothing? True when the
    /// file the engine says it wrote is missing or empty. A file that is merely SMALLER than expected is
    /// not judged here — a server may legitimately report a size it then serves differently, and the
    /// resumed-download case has its own check.</summary>
    public static bool LooksEmptyAfterCompletion(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false; // nothing to judge; the engine never told us where it wrote
        try
        {
            var info = new FileInfo(path);
            return !info.Exists || info.Length == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // can't tell — never fail a download on a filesystem hiccup
        }
    }

    /// <summary>Where the engine says the finished file is, from the completion event or the package.</summary>
    private static bool NothingWasDownloaded(DownloadItemViewModel vm,
        System.ComponentModel.AsyncCompletedEventArgs e, out string savedPath)
    {
        // First NON-BLANK, not first non-null: the engine's package routinely carries an EMPTY file name
        // when the download produced nothing, and `??` happily accepts "" — which read as "no path to
        // judge" and let the very case this guard exists for slip through as a success.
        savedPath = FirstNonBlank(
            (e.UserState as DownloadPackage)?.FileName,
            vm.Download?.Package?.FileName,
            vm.GetItem()?.FilePath);
        // A download the engine SKIPPED because the file already exists is handled well before this
        // (TryMarkAlreadyExists) and never reaches here, so an empty file at this point is a real miss.
        return LooksEmptyAfterCompletion(savedPath);
    }

    private static string FirstNonBlank(params string[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

    /// <summary>The failure a no-data completion is reported as: its own type, so the recovery path can
    /// treat it like a refused address while the row still says what really happened.</summary>
    private static Exception EmptyDownloadError() =>
        new EmptyDownloadException("the download finished without producing a file");

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

    /// <summary>Pure heuristic (testable): the URL itself looks like a WEB PAGE — no extension on the last
    /// path segment, or an HTML-ish one. For such a URL a small HTML result is the EXPECTED content, so the
    /// expired-link check must not flag it (pasting "https://host/docs/" used to always end "Failed — link
    /// expired"). Signed/expiring file links carry real extensions (.zip, .mp4, …) and stay protected.</summary>
    public static bool UrlLooksLikePage(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) ||
            (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
            return false;
        var last = u.AbsolutePath.TrimEnd('/').Split('/')[^1];
        var dot = last.LastIndexOf('.');
        var ext = dot < 0 ? "" : last[(dot + 1)..].ToLowerInvariant();
        return ext is "" or "html" or "htm" or "php" or "asp" or "aspx" or "jsp" or "cfm" or "shtml";
    }

    /// <summary>Reads the head of the just-completed file and applies <see cref="LooksExpiredOrInvalid"/>.</summary>
    private bool IsExpiredOrInvalidLink(DownloadItemViewModel vm, System.ComponentModel.AsyncCompletedEventArgs e)
    {
        // The user asked for a page-like URL — HTML output is what that URL means, not an expired link.
        if (UrlLooksLikePage(vm.GetItem().Urls?.FirstOrDefault()))
            return false;

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
        // Release the engine as soon as the row reaches an end state (Completed/Failed/Stopped) — this is
        // the fix for the reported leak (#11): thousands of finished rows each kept their DownloadService
        // (package + chunk buffers) alive, so memory climbed to GBs and only a restart cleared it. Paused
        // is NOT terminal, so its engine is kept for Resume (and the engine's Pause() never fires this).
        if (vm.Status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Stopped)
            ReleaseEngine(vm);

        TryStartNextInQueue(vm.GetItem().QueueId);
        if (vm.Status == DownloadStatus.Completed)
            MaybeAllCompleted();
        NotifyList();
    }

    /// <summary>Dispose a finished row's engine so its package + buffers can be garbage-collected. The row's
    /// display/resume state (name, size, downloaded, progress, status, folder, urls) is model-backed on the
    /// VM and survives; a later Resume/Retry rebuilds a fresh DownloadService in <see cref="Start"/> exactly
    /// like a first start (engine auto-resume + the on-disk .download file continue the bytes).</summary>
    private static void ReleaseEngine(DownloadItemViewModel vm)
    {
        var engine = vm.Download;
        if (engine == null)
            return;
        vm.Download = null; // drop the reference first so no late staged flush touches a disposed instance
        try { engine.Dispose(); }
        catch { /* best-effort — releasing memory must never surface an error to the user */ }
    }

    /// <summary>If the resolving plugin offers an action for this completed item (e.g. "Add to Ollama"),
    /// surface it as an actionable notification. The row button appears via <see cref="PostDownloadActionLabel"/>.</summary>
    private void OfferPostDownloadAction(DownloadItemViewModel vm)
    {
        var label = PostDownloadActionLabel(vm);
        if (label == null)
            return;
        vm.RaisePostActionChanged();
        // The in-window action lives on the completed row (its action button, shown via
        // PostDownloadActionLabel) — the notification just tells the user it's available.
        NotificationService.Notify(
            label,
            string.Format(Localizer.Instance["PostAction_OfferMsg"], vm.FileName ?? vm.Url, label),
            isError: false);
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
