using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Downloader.Desktop.Services;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// Details dialog for a single download: top-level info (via <see cref="Item"/>), live per-part
/// (chunk) progress from the engine's ChunkDownloadProgressChanged event, and a few live-editable
/// settings (speed limit) applied to the running download's configuration.
/// </summary>
public class DownloadDetailsViewModel : ViewModelBase
{
    private DownloadService _download;
    private readonly Dictionary<string, ChunkProgressViewModel> _parts = new();
    private DateTime _lastReconcileUtc;

    public DownloadItemViewModel Item { get; }
    public ObservableCollection<ChunkProgressViewModel> Parts { get; } = new();

    private static string L(string key) => Localizer.Instance[key];

    public bool HasParts => Parts.Count > 0;
    public string PartsSummary => Parts.Count == 0 ? string.Empty
        : string.Format(L(_planMode ? "Det_SegCount" : "Det_ConnCount"), Parts.Count);
    public bool HasQueue => !string.IsNullOrWhiteSpace(Item?.QueueName);
    public bool HasConfig => Item?.Configuration != null;
    public int Connections => Item?.Configuration?.ChunkCount ?? 0;

    public DownloadDetailsViewModel()
    {
    }

    public DownloadDetailsViewModel(DownloadItemViewModel item)
    {
        Item = item;

        UseGlobalLimitCommand = ReactiveCommand.Create(UseGlobalSpeedLimit);
        CopyUrlCommand = ReactiveCommand.CreateFromTask(CopyUrlAsync);
        CopyPathCommand = ReactiveCommand.CreateFromTask(CopyPathAsync);
        CopyErrorCommand = ReactiveCommand.CreateFromTask(CopyErrorAsync);
        AddMirrorCommand = ReactiveCommand.Create(() => AddMirror(string.Empty));
        RefreshLinkCommand = ReactiveCommand.CreateFromTask(RefreshLinkAsync);

        // The URL box writes through to the item as the user types, so remember the link that was in
        // force when the dialog opened: an abandoned refresh must leave the download exactly as it was.
        _committedUrl = Item?.Url;

        // Seed the mirror editor from the stored mirrors (everything after the primary URL).
        if (Item?.GetItem().Mirrors is { } existing)
            foreach (var m in existing)
                AddMirror(m, sync: false);

        if (item != null)
            ((INotifyPropertyChanged)item).PropertyChanged += OnItemPropertyChanged;

        // A multi-part plan run feeds per-SEGMENT progress instead of engine chunks (each segment is
        // its own single-chunk engine, so attaching those would show one endlessly-resetting row).
        if (item?.PlanRun != null)
            StartPlanPolling();
        else
            // The engine handle may not exist yet (the manager assigns it only after resolving redirects
            // off the UI thread). Attach now if it's ready, otherwise AttachDownload runs once it's set.
            AttachDownload(item?.Download);

        // If the download is already finished when the dialog opens, reflect its terminal state on
        // every segment instead of leaving them reading "Downloading".
        if (Item?.Status == DownloadStatus.Completed)
            CompleteAllParts();
        else if (Item?.Status is DownloadStatus.Failed or DownloadStatus.Stopped or DownloadStatus.Paused)
            FreezeParts(Item.Status);
    }

    /// <summary>
    /// Subscribes to a (possibly late-arriving) engine handle and seeds its existing connections.
    /// Safe to call repeatedly — it ignores a null handle and re-attaches if the handle changed
    /// (e.g. the dialog was opened before Start finished spinning up the <see cref="DownloadService"/>).
    /// </summary>
    private void AttachDownload(DownloadService download)
    {
        if (download == null || ReferenceEquals(download, _download) || Item?.PlanRun != null)
            return;

        if (_download != null)
        {
            _download.ChunkDownloadProgressChanged -= OnChunkProgress;
            _download.DownloadProgressChanged -= OnOverallProgress;
        }

        _download = download;

        // Seed every connection that already exists (in chunk order, with each chunk's byte size)
        // so the segmented bar shows them immediately when the dialog opens mid-download.
        ReconcileParts();

        // Per-chunk events drive live speed/fill; the overall event lets us reconcile the part set
        // so connections the engine activates AFTER the dialog opened still get added.
        _download.ChunkDownloadProgressChanged += OnChunkProgress;
        _download.DownloadProgressChanged += OnOverallProgress;
    }

    public ICommand UseGlobalLimitCommand { get; }
    public ICommand CopyUrlCommand { get; }
    public ICommand CopyPathCommand { get; }
    public ICommand CopyErrorCommand { get; }
    public ICommand AddMirrorCommand { get; }
    public ICommand RefreshLinkCommand { get; }

    // Transient "copied" feedback for the copy-path button: flips the icon to a checkmark and the
    // tooltip to "Path copied!" for a couple of seconds, then reverts. Guarded so rapid clicks don't
    // stack reverts that snap the state back early.
    private bool _pathCopied;
    private int _copyFeedbackToken;

    /// <summary>True for a short moment after the saved path is copied (drives the checkmark icon).</summary>
    public bool PathCopied
    {
        get => _pathCopied;
        private set => this.RaiseAndSetIfChanged(ref _pathCopied, value);
    }

    /// <summary>Tooltip for the copy-path button: normally "Copy path", "Path copied!" right after a copy.</summary>
    public string CopyPathTooltip => _pathCopied ? L("Action_PathCopied") : L("Action_CopyPath");

    /// <summary>Copies the saved file path to the clipboard and briefly confirms it on the button.</summary>
    /// <summary>Internal (like <see cref="RefreshLinkAsync"/>) so a test can await the copy and
    /// its self-reverting confirmation without driving a ReactiveCommand.</summary>
    internal async Task CopyPathAsync()
    {
        var path = FilePath;
        if (string.IsNullOrEmpty(path))
            return;

        await DialogHelper.CopyTextAsync(path);

        PathCopied = true;
        this.RaisePropertyChanged(nameof(CopyPathTooltip));

        var token = ++_copyFeedbackToken;
        await Task.Delay(2000);
        if (token != _copyFeedbackToken)
            return; // a newer copy took over — let it own the revert

        PathCopied = false;
        this.RaisePropertyChanged(nameof(CopyPathTooltip));
    }

    // Transient "copied" feedback for the copy-URL button (same UX as the copy-path button).
    private bool _urlCopied;
    private int _urlCopyToken;

    /// <summary>True for a short moment after the source URL is copied (drives the checkmark icon).</summary>
    public bool UrlCopied
    {
        get => _urlCopied;
        private set => this.RaiseAndSetIfChanged(ref _urlCopied, value);
    }

    /// <summary>Tooltip for the copy-URL button ("Copy URL" → "URL copied!" right after a copy).</summary>
    public string CopyUrlTooltip => _urlCopied ? L("Action_UrlCopied") : L("Action_CopyUrl");

    /// <summary>Copies the source URL to the clipboard and briefly confirms it on the button.</summary>
    /// <summary>Internal (like <see cref="RefreshLinkAsync"/>) so a test can await the copy and
    /// its self-reverting confirmation without driving a ReactiveCommand.</summary>
    internal async Task CopyUrlAsync()
    {
        var url = Item?.Url;
        if (string.IsNullOrWhiteSpace(url))
            return;

        await DialogHelper.CopyTextAsync(url);

        UrlCopied = true;
        this.RaisePropertyChanged(nameof(CopyUrlTooltip));

        var token = ++_urlCopyToken;
        await Task.Delay(2000);
        if (token != _urlCopyToken)
            return;

        UrlCopied = false;
        this.RaisePropertyChanged(nameof(CopyUrlTooltip));
    }

    // Transient "copied" feedback for the copy-error button (independent token from the path copy).
    private bool _errorCopied;
    private int _errorCopyToken;

    /// <summary>True for a short moment after the error is copied (drives the checkmark icon).</summary>
    public bool ErrorCopied
    {
        get => _errorCopied;
        private set => this.RaiseAndSetIfChanged(ref _errorCopied, value);
    }

    /// <summary>Tooltip for the copy-error button ("Copy error" → "Error copied!" right after a copy).</summary>
    public string CopyErrorTooltip => _errorCopied ? L("Action_ErrorCopied") : L("Action_CopyError");

    /// <summary>Copies the failure message to the clipboard so the user can paste it into a GitHub issue.</summary>
    /// <summary>Internal (like <see cref="RefreshLinkAsync"/>) so a test can await the copy and
    /// its self-reverting confirmation without driving a ReactiveCommand.</summary>
    internal async Task CopyErrorAsync()
    {
        if (string.IsNullOrWhiteSpace(ErrorMessage))
            return;

        await DialogHelper.CopyTextAsync(ErrorMessage);

        ErrorCopied = true;
        this.RaisePropertyChanged(nameof(CopyErrorTooltip));

        var token = ++_errorCopyToken;
        await Task.Delay(2000);
        if (token != _errorCopyToken)
            return;

        ErrorCopied = false;
        this.RaisePropertyChanged(nameof(CopyErrorTooltip));
    }

    /// <summary>Editable mirror URLs (each a row with its own remove button in the UI). (#7)</summary>
    public ObservableCollection<MirrorEntryViewModel> Mirrors { get; } = new();

    public string MirrorsHeader => string.Format(L("Det_Mirrors"), Mirrors.Count);

    private void AddMirror(string url, bool sync = true)
    {
        var entry = new MirrorEntryViewModel(url);
        entry.UrlChanged += SyncMirrors;
        entry.RemoveRequested += e =>
        {
            e.UrlChanged -= SyncMirrors;
            Mirrors.Remove(e);
            this.RaisePropertyChanged(nameof(MirrorsHeader));
            SyncMirrors();
        };
        Mirrors.Add(entry);
        this.RaisePropertyChanged(nameof(MirrorsHeader));
        if (sync)
            SyncMirrors();
    }

    /// <summary>Pushes the editor's mirror URLs back into the download item (keeps the primary URL).</summary>
    private void SyncMirrors() =>
        Item?.GetItem().SetMirrors(Mirrors.Select(m => m.Url).Where(u => !string.IsNullOrWhiteSpace(u)));

    private void OnItemPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        // The title bar shows live "{percent}% {name}", so refresh it as progress/name/status change.
        if (e.PropertyName is nameof(DownloadItemViewModel.Progress) or nameof(DownloadItemViewModel.FileName)
            or nameof(DownloadItemViewModel.DisplayName) or nameof(DownloadItemViewModel.Status))
            Dispatcher.UIThread.Post(() => this.RaisePropertyChanged(nameof(WindowTitle)));

        // The plan runner may start after the dialog opened — switch to segment polling when it does.
        if (e.PropertyName == nameof(DownloadItemViewModel.PlanRun))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (Item?.PlanRun != null)
                    StartPlanPolling();
            });
            return;
        }

        // The engine handle is created after the dialog may have opened — attach as soon as it's set.
        if (e.PropertyName == nameof(DownloadItemViewModel.Download))
        {
            Dispatcher.UIThread.Post(() =>
            {
                AttachDownload(Item?.Download);
                this.RaisePropertyChanged(nameof(HasConfig));
                this.RaisePropertyChanged(nameof(Connections));
            });
            return;
        }

        if (e.PropertyName is nameof(DownloadItemViewModel.Status) or nameof(DownloadItemViewModel.HasError)
            or nameof(DownloadItemViewModel.FileName))
            Dispatcher.UIThread.Post(() =>
            {
                this.RaisePropertyChanged(nameof(CanEdit));
                this.RaisePropertyChanged(nameof(HasError));
                this.RaisePropertyChanged(nameof(ErrorMessage));
                this.RaisePropertyChanged(nameof(FilePath));

                // A completed download's final per-chunk progress events are often dropped by the
                // per-part throttle, leaving segments just shy of full. Snap them all to 100%.
                if (Item?.Status == DownloadStatus.Completed)
                    CompleteAllParts();
                // When it stops without completing, freeze the segments so they stop reading
                // "Downloading" (no further progress events arrive to update them).
                else if (Item?.Status is DownloadStatus.Failed or DownloadStatus.Stopped or DownloadStatus.Paused)
                    FreezeParts(Item.Status);
            });
    }

    /// <summary>Full path the file is saved to (folder + name), shown in the details window (#7).</summary>
    public string FilePath => Item?.GetItem().FilePath;

    /// <summary>Reveals/opens the containing folder (and selects the file).</summary>
    public ICommand OpenFolderCommand => Item?.OpenFolderCommand;

    /// <summary>The source URL can be edited only while the download is not active.</summary>
    public bool CanEdit => Item != null && Item.Status is DownloadStatus.Stopped or DownloadStatus.Failed
        or DownloadStatus.Paused or DownloadStatus.Created or DownloadStatus.None;

    public string EditableUrl
    {
        get => Item?.Url;
        set { if (Item != null) Item.Url = value; this.RaisePropertyChanged(); }
    }

    // ---- Refresh an expired link (issue #6) -------------------------------------------------------
    // A signed / time-limited link often dies before a multi-day download finishes. The user pastes a
    // fresh one here; we check it points at the same file before committing, because the engine only
    // resumes onto the existing partial file when the new link reports the SAME total size — a link to a
    // different file silently throws that partial away.

    private bool _isRefreshing;
    private string _refreshMessage;
    private bool _refreshFailed;
    private string _committedUrl;

    /// <summary>True while the pasted link is being probed (disables the button, shows the busy text).</summary>
    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set => this.RaiseAndSetIfChanged(ref _isRefreshing, value);
    }

    /// <summary>Outcome of the last refresh attempt (checking / done / why it failed). Null = nothing to say.</summary>
    public string RefreshMessage
    {
        get => _refreshMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _refreshMessage, value);
            this.RaisePropertyChanged(nameof(HasRefreshMessage));
        }
    }

    public bool HasRefreshMessage => !string.IsNullOrWhiteSpace(RefreshMessage);

    /// <summary>True when <see cref="RefreshMessage"/> reports a problem (drives the error color).</summary>
    public bool RefreshFailed
    {
        get => _refreshFailed;
        private set
        {
            this.RaiseAndSetIfChanged(ref _refreshFailed, value);
            this.RaisePropertyChanged(nameof(RefreshMessageBrush));
        }
    }

    /// <summary>Red while the refresh message reports a problem, muted otherwise.</summary>
    public Avalonia.Media.IBrush RefreshMessageBrush =>
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(RefreshFailed ? "#D64545" : "#6B7A83"));

    /// <summary>Confirmation seam: replaced in tests so the mismatch prompt needs no window.</summary>
    internal Func<string, string, Task<bool>> ConfirmAsync { get; set; } = DialogHelper.Confirm;

    /// <summary>Probe seam: replaced in tests so no network is touched.</summary>
    internal Func<string, DownloadConfiguration, Task<RemoteFileInfo>> ProbeAsync { get; set; } =
        (url, cfg) => UrlResolver.ResolveFileInfoAsync(url, cfg);

    /// <summary>
    /// Validates the link currently in the box and, if it is usable, makes it this download's source and
    /// continues the download. A link reporting the same size (or no size at all) is applied silently; one
    /// reporting a different size is applied only after the user confirms losing the partial file.
    /// </summary>
    internal async Task RefreshLinkAsync()
    {
        if (Item?.Manager == null)
            return;

        var url = EditableUrl?.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            RefreshFailed = true;
            RefreshMessage = L("Det_RefreshEmpty");
            return;
        }

        IsRefreshing = true;
        RefreshFailed = false;
        RefreshMessage = L("Det_RefreshChecking");
        try
        {
            var configuration = Item.Configuration ?? Item.Manager.Config?.Settings?.ToConfiguration();
            var info = await ProbeAsync(url, configuration).ConfigureAwait(true);
            if (info == null)
            {
                RestoreCommittedUrl();
                RefreshFailed = true;
                RefreshMessage = string.Format(L("Det_RefreshUnreachable"), HostOf(url));
                return;
            }

            var known = Item.Size;
            if (EvaluateNewLink(known, info.FileSize) == LinkRefreshCheck.Mismatch)
            {
                var proceed = await ConfirmAsync(L("Det_RefreshMismatchTitle"),
                    string.Format(L("Det_RefreshMismatchBody"),
                        DownloadItemViewModel.FormatBytes(info.FileSize),
                        DownloadItemViewModel.FormatBytes(known ?? 0))).ConfigureAwait(true);
                if (!proceed)
                {
                    RestoreCommittedUrl();
                    RefreshMessage = null;
                    return;
                }
            }

            EditableUrl = url;
            _committedUrl = url;
            // A saved multi-part plan holds the OLD (expired) segment URLs — drop it so the next start
            // resolves the refreshed link from scratch.
            Item.GetItem().PlanJson = null;
            Item.Manager.Resume(Item); // user-initiated: also resets the automatic-refresh budget
            RefreshFailed = false;
            RefreshMessage = L("Det_RefreshDone");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>Puts back the link that was in force before the user started typing a new one, so an
    /// abandoned or impossible refresh leaves the download untouched.</summary>
    private void RestoreCommittedUrl()
    {
        if (Item == null || string.IsNullOrWhiteSpace(_committedUrl))
            return;
        Item.Url = _committedUrl;
        this.RaisePropertyChanged(nameof(EditableUrl));
    }

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

    /// <summary>Pure helper (testable): can the partial file survive this new link? Only a link reporting
    /// exactly the same total size resumes onto it; a different size means the engine will discard the
    /// partial and start over, so the user must agree first. An unknown size on either side cannot be
    /// compared — the engine's own resume check still protects the file, so it is not worth a prompt.</summary>
    public static LinkRefreshCheck EvaluateNewLink(long? knownSize, long? newSize) =>
        knownSize is not > 0 || newSize is not > 0
            ? LinkRefreshCheck.Unknown
            : knownSize == newSize
                ? LinkRefreshCheck.Match
                : LinkRefreshCheck.Mismatch;

    public bool HasError => Item?.HasError == true;
    public string ErrorMessage => Item?.ErrorMessage;

    /// <summary>Window/title-bar caption: live "{percent}% {file name}" (e.g. "21% movie.mkv").</summary>
    public string WindowTitle
    {
        get
        {
            if (Item == null)
                return L("Det_Title");
            var name = Item.DisplayName;
            return string.IsNullOrWhiteSpace(name) ? L("Det_Title") : $"{Item.Progress:0}% {name}";
        }
    }

    /// <summary>Live speed cap in KB/s (0 = unlimited). Applies to the running download.</summary>
    public long SpeedLimitKb
    {
        get
        {
            var v = Item?.Configuration?.MaximumBytesPerSecond ?? 0;
            return v <= 0 || v == long.MaxValue ? 0 : v / 1024;
        }
        set
        {
            var bytes = value <= 0 ? 0 : value * 1024;
            // Editing the cap here opts this item OUT of the global limit, and persists the choice so it
            // survives Stop → Resume and a restart (Start reads it back from the model).
            if (Item != null)
            {
                Item.HasCustomSpeedLimit = true;
                Item.CustomSpeedLimitBytesPerSecond = bytes;
            }
            if (Item?.Configuration != null)
                Item.Configuration.MaximumBytesPerSecond = bytes; // live-apply to the running engine
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(UsesGlobalSpeedLimit));
        }
    }

    /// <summary>True while this item follows the global speed limit (no per-item override set).</summary>
    public bool UsesGlobalSpeedLimit => Item?.HasCustomSpeedLimit != true;

    /// <summary>Clears the per-item override and re-applies the current global limit to the running engine.</summary>
    public void UseGlobalSpeedLimit()
    {
        if (Item == null)
            return;
        Item.HasCustomSpeedLimit = false;
        Item.CustomSpeedLimitBytesPerSecond = 0;
        var global = Item.Manager?.Config?.Settings?.MaximumBytesPerSecond ?? 0;
        if (Item.Configuration != null)
            Item.Configuration.MaximumBytesPerSecond = global <= 0 ? 0 : global;
        this.RaisePropertyChanged(nameof(SpeedLimitKb));
        this.RaisePropertyChanged(nameof(UsesGlobalSpeedLimit));
    }

    private void OnChunkProgress(object sender, DownloadProgressChangedEventArgs e)
    {
        var now = DateTime.UtcNow;
        // Throttle per connection, not globally: a single shared timestamp would let one busy chunk
        // starve the others, so some segments were never created/updated (e.g. 7 shown for 8 conns).
        var nearDone = e.ProgressPercentage >= 99.99;

        Dispatcher.UIThread.Post(() =>
        {
            var part = GetOrAddPart(e.ProgressId, e.TotalBytesToReceive);
            // Always let a near-complete event through so the final fill is never dropped.
            if (!nearDone && (now - part.LastTickUtc).TotalMilliseconds < 150)
                return;
            part.LastTickUtc = now;
            part.Update(e.ProgressPercentage, e.BytesPerSecondSpeed, e.ReceivedBytesSize, e.TotalBytesToReceive);
        });
    }

    private void OnOverallProgress(object sender, DownloadProgressChangedEventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastReconcileUtc).TotalMilliseconds < 200)
            return;
        _lastReconcileUtc = now;

        Dispatcher.UIThread.Post(ReconcileParts);
    }

    /// <summary>
    /// Brings the segment/grid set in line with the engine's current chunk set. The engine activates
    /// connections lazily (parallel cap / serial mode), so chunks can appear after the dialog opened;
    /// reconciling on each overall-progress tick adds those late segments and keeps every chunk's fill
    /// current from the package — even if a chunk's own per-chunk event was throttled or missed.
    /// </summary>
    private void ReconcileParts()
    {
        var chunks = _download?.Package?.Chunks;
        if (chunks == null)
            return;

        foreach (var c in chunks)
        {
            var part = GetOrAddPart(c.Id, c.Length);
            part.SyncFromPackage(c.Position, c.Length);
        }
    }

    private ChunkProgressViewModel GetOrAddPart(string id, long total = 0)
    {
        id ??= "main";
        if (!_parts.TryGetValue(id, out var part))
        {
            part = new ChunkProgressViewModel(Parts.Count + 1, total);
            _parts[id] = part;
            Parts.Add(part);
            this.RaisePropertyChanged(nameof(HasParts));
            this.RaisePropertyChanged(nameof(PartsSummary));
        }
        else
        {
            part.SetTotal(total);
        }

        return part;
    }

    /// <summary>Snaps every connection segment to 100% (used when the whole download completes).</summary>
    private void CompleteAllParts()
    {
        foreach (var part in Parts)
            part.Complete();
    }

    /// <summary>Freezes every unfinished segment to mirror the parent's stopped/failed/paused state.</summary>
    private void FreezeParts(DownloadStatus status)
    {
        var key = status switch
        {
            DownloadStatus.Failed => "State_Failed",
            DownloadStatus.Stopped => "State_Stopped",
            DownloadStatus.Paused => "State_Paused",
            _ => "State_Pending"
        };
        foreach (var part in Parts)
            part.Freeze(key);
    }

    // ---- Multi-part plan mode: poll the runner's per-segment board ----

    private DispatcherTimer _planTimer;
    private bool _planMode;

    private void StartPlanPolling()
    {
        _planMode = true;
        UpdateFromPlan();
        if (_planTimer == null)
        {
            _planTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _planTimer.Tick += (_, _) => UpdateFromPlan();
        }
        _planTimer.Start();
        this.RaisePropertyChanged(nameof(PartsSummary));
    }

    private void UpdateFromPlan()
    {
        var run = Item?.PlanRun;
        if (run == null)
        {
            // The run ended — the status-change handler completes/freezes the rows; stop polling.
            _planTimer?.Stop();
            return;
        }

        for (var i = 0; i < run.Count; i++)
        {
            var (state, fraction, speed, received, total) = run.Get(i);
            var part = GetOrAddPart($"seg{i}", total);
            switch (state)
            {
                case PlanRunState.PartState.Done:
                    if (part.Progress < 100)
                        part.Complete();
                    break;
                case PlanRunState.PartState.Active:
                    part.Update(fraction * 100, speed, received, total);
                    break;
                // Waiting: leave untouched — 0% shows the localized "Pending".
            }
        }
    }

    /// <summary>Detach engine handlers; call when the dialog closes.</summary>
    public void Cleanup()
    {
        _planTimer?.Stop();
        if (_download != null)
        {
            _download.ChunkDownloadProgressChanged -= OnChunkProgress;
            _download.DownloadProgressChanged -= OnOverallProgress;
        }
        if (Item != null)
            ((INotifyPropertyChanged)Item).PropertyChanged -= OnItemPropertyChanged;
    }
}

/// <summary>One editable mirror URL row in the details dialog.</summary>
/// <summary>Whether a freshly pasted link can continue the existing partial file (issue #6).</summary>
public enum LinkRefreshCheck
{
    /// <summary>The new link reports the same size — the partial file is resumed.</summary>
    Match,

    /// <summary>One of the sizes is unknown — nothing to compare, proceed.</summary>
    Unknown,

    /// <summary>The new link reports a different size — resuming would discard the partial file.</summary>
    Mismatch
}

public class MirrorEntryViewModel : ViewModelBase
{
    private string _url;

    public MirrorEntryViewModel(string url)
    {
        _url = url;
        RemoveCommand = ReactiveCommand.Create(() => RemoveRequested?.Invoke(this));
    }

    public string Url
    {
        get => _url;
        set
        {
            this.RaiseAndSetIfChanged(ref _url, value);
            UrlChanged?.Invoke();
        }
    }

    public ICommand RemoveCommand { get; }

    /// <summary>Raised when the URL text changes (parent re-syncs the item's mirror list).</summary>
    public event Action UrlChanged;

    /// <summary>Raised when the user clicks remove on this row.</summary>
    public event Action<MirrorEntryViewModel> RemoveRequested;
}

/// <summary>Live progress for a single download part (chunk).</summary>
public class ChunkProgressViewModel : ViewModelBase
{
    private double _progress;
    private double _speed;
    private long _received;
    private long _total;
    // When the whole download stops without completing (Failed/Stopped/Paused), the segment must stop
    // reading "Downloading": with no more progress events its last status would otherwise stick. This
    // overrides the progress-derived status until fresh progress (a resume) arrives.
    private string _frozenStatusKey;

    public ChunkProgressViewModel(int index, long total = 0)
    {
        Index = index;
        _total = total;
    }

    public int Index { get; }
    public string Title => $"Part {Index}";

    // Distinct, stable color per connection (assigned by index so it never reshuffles as bars update).
    // All shades stay within one blue→teal family so the strip reads as a cohesive palette, not a
    // rainbow (#4): deep blue → blue → sky → cyan → teal → blue-green.
    private static readonly Avalonia.Media.IBrush[] Palette =
    {
        MakeBrush("#1B6CA8"), MakeBrush("#2A8BD0"), MakeBrush("#3FA7DE"), MakeBrush("#5FC0E8"),
        MakeBrush("#26B6CF"), MakeBrush("#159FB6"), MakeBrush("#1FB39A"), MakeBrush("#7CCBE6"),
        MakeBrush("#3D7FC4"), MakeBrush("#22C3D6"), MakeBrush("#4FB8C8"), MakeBrush("#6FB6E0")
    };

    private static Avalonia.Media.IBrush MakeBrush(string hex) =>
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(hex));

    /// <summary>The fill color for this fragment's bar (stable, distinct per connection).</summary>
    public Avalonia.Media.IBrush Brush => Palette[(Index - 1 + Palette.Length) % Palette.Length];

    /// <summary>Timestamp of this segment's last UI update (per-connection event throttling).</summary>
    public DateTime LastTickUtc { get; set; }

    /// <summary>Records the chunk's known total size (seeded before any progress event arrives).</summary>
    public void SetTotal(long total)
    {
        if (total > 0 && _total != total)
        {
            _total = total;
            this.RaisePropertyChanged(nameof(TotalText));
        }
    }

    public void Update(double progress, double speed, long received, long total)
    {
        _frozenStatusKey = null; // fresh progress means this segment is active again
        _progress = progress;
        _speed = speed;
        _received = received;
        _total = total;
        this.RaisePropertyChanged(nameof(Progress));
        this.RaisePropertyChanged(nameof(SpeedText));
        this.RaisePropertyChanged(nameof(DownloadedText));
        this.RaisePropertyChanged(nameof(TotalText));
        this.RaisePropertyChanged(nameof(StatusText));
    }

    /// <summary>
    /// Updates this segment from the engine package's live chunk position (no speed info). Used to
    /// keep late-activated connections current without relying on their per-chunk progress events.
    /// Never regresses a fill an event already advanced further.
    /// </summary>
    public void SyncFromPackage(long position, long total)
    {
        if (total <= 0)
            return;

        var pct = (double)position / total * 100;
        var changed = false;

        if (pct > _progress) { _progress = pct; changed = true; }
        if (position > _received) { _received = position; changed = true; }
        if (total != _total) { _total = total; changed = true; }

        if (!changed)
            return;

        _frozenStatusKey = null; // advancing again (e.g. resumed) — drop any frozen override

        this.RaisePropertyChanged(nameof(Progress));
        this.RaisePropertyChanged(nameof(DownloadedText));
        this.RaisePropertyChanged(nameof(TotalText));
        this.RaisePropertyChanged(nameof(StatusText));
    }

    /// <summary>Forces this segment to full (download finished).</summary>
    public void Complete()
    {
        _frozenStatusKey = null;
        _progress = 100;
        _speed = 0;
        if (_total > 0)
            _received = _total;
        this.RaisePropertyChanged(nameof(Progress));
        this.RaisePropertyChanged(nameof(SpeedText));
        this.RaisePropertyChanged(nameof(DownloadedText));
        this.RaisePropertyChanged(nameof(StatusText));
    }

    /// <summary>
    /// Freezes an unfinished segment to mirror the parent download's terminal/inactive state
    /// (Failed/Stopped/Paused): zeroes the live speed and overrides the status so it no longer reads
    /// "Downloading" once progress events stop. A fully received segment is left as Completed.
    /// </summary>
    public void Freeze(string statusKey)
    {
        if (_progress >= 99.99)
            return;
        _frozenStatusKey = statusKey;
        _speed = 0;
        this.RaisePropertyChanged(nameof(SpeedText));
        this.RaisePropertyChanged(nameof(StatusText));
    }

    public double Progress => _progress;

    public string StatusText => _progress >= 99.99
        ? Localizer.Instance["State_Completed"]
        : _frozenStatusKey != null ? Localizer.Instance[_frozenStatusKey]
        : _progress > 0 ? Localizer.Instance["State_Downloading"] : Localizer.Instance["State_Pending"];
    public string DownloadedText => FormatBytes(_received);
    public string TotalText => _total > 0 ? FormatBytes(_total) : "—";
    public string SpeedText => _speed > 0 ? FormatBytes((long)_speed) + "/s" : string.Empty;

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {units[unit]}";
    }
}
