using System;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// A single row in the downloads list. Wraps the persisted <see cref="DownloadItem"/> and,
/// while active, a live <see cref="IDownload"/> driven by the <see cref="IDownloadManager"/>.
/// Live progress/speed/status are pushed in by the manager from engine events.
/// </summary>
public class DownloadItemViewModel : ViewModelBase
{
    private readonly DownloadItem _item;
    private readonly IDownloadManager _manager;

    private double _progress;
    private double _speed;
    private DownloadStatus _status;
    private bool _isChecked;
    private string _planStage;

    private DownloadService _download;

    /// <summary>The live engine handle while this item is downloading/paused; null otherwise.
    /// Notifies so a details dialog opened before the download started can attach once it's set.</summary>
    public DownloadService Download
    {
        get => _download;
        set => this.RaiseAndSetIfChanged(ref _download, value);
    }

    /// <summary>The live plugin transfer handle while a plugin-owned transfer (e.g. website crawl) runs;
    /// null otherwise. Transient — pause/resume route here when set instead of <see cref="Download"/>.</summary>
    public Plugins.ITransfer ActiveTransfer { get; set; }

    /// <summary>Cancels the running plugin transfer (the transfer path's equivalent of engine CancelAsync).</summary>
    public System.Threading.CancellationTokenSource TransferCancellation { get; set; }

    // --- UI update coalescing (perf) ---
    // The engine raises progress events very frequently from background threads. Instead of marshaling
    // every event to the UI thread, handlers stage the latest values here (no UI touch); a single
    // shared dispatcher timer in the manager flushes them at a fixed rate. This keeps main-thread work
    // bounded no matter how many downloads/connections are active.
    private double _pendingProgress;
    private double _pendingSpeed;
    private long _pendingDownloaded;
    private long _pendingSize;
    private int _progressDirty;

    /// <summary>Records the latest engine progress without touching the UI (any thread).</summary>
    public void StageProgress(double progress, double speed, long downloaded, long size)
    {
        // Any thread. This is the only heartbeat a running download has: the watchdog reads it to tell a
        // slow transfer from one the engine has stopped reporting on entirely.
        LastProgressUtc = DateTime.UtcNow;
        _pendingProgress = progress;
        _pendingSpeed = speed;
        _pendingDownloaded = downloaded;
        _pendingSize = size;
        System.Threading.Volatile.Write(ref _progressDirty, 1);
    }

    /// <summary>Applies the last staged progress to the bound properties. UI thread only.
    /// Returns true if anything was applied (lets the manager fire stats only when needed).</summary>
    public bool FlushProgress()
    {
        if (System.Threading.Interlocked.Exchange(ref _progressDirty, 0) == 0)
            return false;
        // A row paused/stopped after staging must keep its last fill — never apply a stale event.
        if (_status != DownloadStatus.Running)
            return false;

        Progress = _pendingProgress;
        Speed = _pendingSpeed;
        Downloaded = _pendingDownloaded;
        if (_pendingSize > 0 && _item.Size is null or 0)
            Size = _pendingSize;
        return true;
    }

    /// <summary>The live engine configuration for this download (lets the details dialog tweak it).</summary>
    public DownloadConfiguration Configuration { get; set; }

    /// <summary>Design-time / blank constructor.</summary>
    public DownloadItemViewModel()
    {
        _item = new DownloadItem();
        _status = DownloadStatus.Created;
    }

    public DownloadItemViewModel(DownloadItem item, IDownloadManager manager)
    {
        _item = item ?? new DownloadItem();
        _manager = manager;
        _status = _item.Status;
        _previewName = _item.PreviewName; // restore the cached name for items not yet started
        // A completed item is always full — show 100% even if Downloaded wasn't persisted (e.g. a file
        // that already existed on disk and was skipped). Otherwise compute from bytes.
        _progress = _item.Status == DownloadStatus.Completed ? 100
            : _item.Size is > 0 ? (double)_item.Downloaded / _item.Size.Value * 100 : 0;

        PauseCommand = ReactiveCommand.Create(() => _manager?.Pause(this));
        ResumeCommand = ReactiveCommand.Create(() => _manager?.Resume(this));
        CancelCommand = ReactiveCommand.Create(() => _manager?.Cancel(this));
        RetryCommand = ReactiveCommand.Create(() => _manager?.Retry(this));
        RemoveCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (_manager != null) await _manager.Remove(this);
        });
        OpenFolderCommand = ReactiveCommand.Create(OpenContainingFolder);
        OpenFileCommand = ReactiveCommand.Create(OpenFile);
        CopyUrlCommand = ReactiveCommand.CreateFromTask(() => DialogHelper.CopyTextAsync(Url));
        PostActionCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (_manager != null) await _manager.RunPostDownloadAction(this);
        });

        // Refresh localized row text when the UI language changes.
        Localizer.Instance.PropertyChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        this.RaisePropertyChanged(nameof(StatusText));
        this.RaisePropertyChanged(nameof(DisplayName));
        this.RaisePropertyChanged(nameof(NameTooltip));
        this.RaisePropertyChanged(nameof(Group));
    }

    /// <summary>Detaches global event handlers; called when the row is removed from the list.</summary>
    public void Detach() => Localizer.Instance.PropertyChanged -= OnLanguageChanged;

    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand OpenFileCommand { get; }
    public ICommand CopyUrlCommand { get; }
    public ICommand PostActionCommand { get; }

    /// <summary>Label of the plugin-offered post-download action for this completed item (e.g.
    /// "Add to Ollama"), or null — drives the row button's visibility and tooltip.</summary>
    public string PostActionLabel => _manager?.PostDownloadActionLabel(this);

    public bool HasPostAction => PostActionLabel != null;

    /// <summary>Called by the manager when the offer state changes (completion / plugin toggle).</summary>
    public void RaisePostActionChanged()
    {
        this.RaisePropertyChanged(nameof(PostActionLabel));
        this.RaisePropertyChanged(nameof(HasPostAction));
    }

    public string FileName
    {
        get => _item.FileName;
        set
        {
            if (_item.FileName != value)
            {
                _item.FileName = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(DisplayName));
            this.RaisePropertyChanged(nameof(NameTooltip));
                this.RaisePropertyChanged(nameof(IsNamePending));
                this.RaisePropertyChanged(nameof(FileKind));
            }
        }
    }

    /// <summary>Grouping label for the list. Batched (multi-URL) adds share one; others group under "Downloads".</summary>
    public string Group => string.IsNullOrWhiteSpace(_item.Group) ? L("Group_Downloads") : _item.Group;

    /// <summary>Name of the queue this download belongs to (shown in the list only when more than one
    /// queue exists). Resolved live from the manager so a drag across queues updates it.</summary>
    public string QueueName =>
        _manager?.Queues?.FirstOrDefault(q => q.Id == _item.QueueId)?.Name ?? string.Empty;

    /// <summary>Re-raises <see cref="QueueName"/> after the item is moved to another queue.</summary>
    public void RaiseQueueNameChanged() => this.RaisePropertyChanged(nameof(QueueName));

    /// <summary>Coarse file category (video/audio/image/archive/document/app/disc/file) by extension.</summary>
    public string FileKind => GetFileKind(!string.IsNullOrWhiteSpace(_item.FileName) ? _item.FileName : _previewName);

    public static string GetFileKind(string name)
    {
        var ext = Path.GetExtension(name ?? string.Empty).TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "mp4" or "mkv" or "avi" or "mov" or "webm" or "flv" or "wmv" or "m4v" or "mpeg" or "mpg" or "m3u8" or "ts" => "video",
            "mp3" or "wav" or "flac" or "aac" or "ogg" or "m4a" or "wma" or "opus" => "audio",
            "jpg" or "jpeg" or "png" or "gif" or "bmp" or "webp" or "svg" or "ico" or "tif" or "tiff" or "heic" => "image",
            "zip" or "rar" or "7z" or "tar" or "gz" or "bz2" or "xz" or "zst" => "archive",
            "pdf" or "doc" or "docx" or "txt" or "rtf" or "xls" or "xlsx" or "ppt" or "pptx" or "csv" or "md" or "epub" => "document",
            "exe" or "msi" or "apk" or "deb" or "rpm" or "dmg" or "appimage" or "pkg" => "app",
            "iso" or "img" or "bin" or "vhd" => "disc",
            _ => "file"
        };
    }

    private string _previewName;

    /// <summary>
    /// Display-only name resolved (from URL/Content-Disposition) for a queued download before it
    /// actually starts, so rows waiting on a queue slot still show a name (#4). Persisted on the item
    /// so it survives an app restart, but not forced on the engine — the engine still resolves the
    /// authoritative name when it starts.
    /// </summary>
    public string PreviewName
    {
        get => _previewName;
        set
        {
            if (_previewName != value)
            {
                _previewName = value;
                _item.PreviewName = value; // cache so the name survives a restart
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(DisplayName));
            this.RaisePropertyChanged(nameof(NameTooltip));
                this.RaisePropertyChanged(nameof(IsNamePending));
                this.RaisePropertyChanged(nameof(FileKind));
            }
        }
    }

    /// <summary>Name shown in the list — a placeholder while the engine resolves the real name.</summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(_item.FileName) ? _item.FileName
        : !string.IsNullOrWhiteSpace(_previewName) ? _previewName
        : IsNamePending ? L("Name_Fetching")
        : L("Name_Unnamed");

    /// <summary>Tooltip for the Name cell: the full file name (so a column-trimmed long name is readable on
    /// hover), plus the failure reason on a second line when the download has failed.</summary>
    public string NameTooltip =>
        HasError && !string.IsNullOrWhiteSpace(ErrorMessage)
            ? $"{DisplayName}\n{ErrorMessage}"
            : DisplayName;

    /// <summary>True while we are still waiting for any name (engine or preview).</summary>
    public bool IsNamePending =>
        string.IsNullOrWhiteSpace(_item.FileName) && string.IsNullOrWhiteSpace(_previewName) &&
        _status is DownloadStatus.Running or DownloadStatus.Created or DownloadStatus.None;

    /// <summary>Reason for the last failure (root cause), surfaced to the user.</summary>
    public string ErrorMessage
    {
        get => _item.LastError;
        set
        {
            _item.LastError = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(HasError));
            this.RaisePropertyChanged(nameof(NameTooltip));
        }
    }

    public bool HasError => _status == DownloadStatus.Failed;

    public string Url
    {
        get => _item.Url;
        set
        {
            if (_item.Url != value)
            {
                _item.Url = value;
                this.RaisePropertyChanged();
            }
        }
    }

    public long? Size
    {
        get => _item.Size;
        set
        {
            if (_item.Size != value)
            {
                _item.Size = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(SizeText));
            }
        }
    }

    /// <summary>The known total size captured at the START of the current attempt (transient, not
    /// persisted). Used to detect an expired link that returns a much smaller file when resuming.</summary>
    public long? PreAttemptSize { get; set; }

    /// <summary>How many times the app has already re-resolved this item's link by itself after an
    /// expired-link failure (issue #6). Session-only: it bounds the automatic retries and is reset when the
    /// download completes or when the user starts/retries it.</summary>
    public int LinkRefreshAttempts { get; set; }

    /// <summary>Which of the item's addresses is leading the current attempt (an index into
    /// <see cref="DownloadItem.Urls"/>). A download is handed several addresses — the end of the browser's
    /// redirect chain and the link the user clicked, or a set of mirrors — and only the leading one is
    /// actually requested by the engine's file probe, so a refused lead has to be replaced rather than
    /// waited on. Session-only, and reset whenever the user starts the download themselves.</summary>
    public int UrlAttempt { get; set; }

    /// <summary>Whether this attempt must use a single connection. Some servers serve a file happily over
    /// one or two connections and answer 403 to the fourth — the user's maximum is a ceiling the download
    /// may use, not a number every server has agreed to. Set for one retry after a refusal that looks like
    /// it was about concurrency, and session-only like the counters above.</summary>
    public bool ForceSingleConnection { get; set; }

    /// <summary>Which attempt is the live one. Bumped every time the row starts a fresh engine, and
    /// captured by that engine's event handlers: an engine we have moved on from can still deliver events
    /// afterwards (its own completion arrives while the NEXT attempt is already running), and honouring
    /// them let an abandoned attempt mark the row Completed over a file that was never written.</summary>
    public int AttemptGeneration { get; set; }

    /// <summary>When this download last showed a sign of life. Set when an attempt starts and on every
    /// progress event; the watchdog fails an attempt that has gone quiet, because a download the engine
    /// has stopped reporting on is otherwise indistinguishable from one that is simply slow — and it
    /// never ends on its own.</summary>
    public DateTime LastProgressUtc { get; set; } = DateTime.UtcNow;

    /// <summary>How many connections this attempt SET OUT to use. Read from the configuration before the
    /// engine sees it, because the engine rewrites that object as it goes (a file whose size it cannot
    /// learn is downloaded over one connection, and the count it was given is overwritten with 1) — so by
    /// the time a failure is being interpreted, the configuration no longer says what was attempted.</summary>
    public int PlannedConnections { get; set; } = 1;

    private bool _isRefreshingLink;

    /// <summary>True while the app is fetching a fresh link for this download after the old one expired
    /// (issue #6). Transient: it only labels the short queued gap between the failed attempt and the next
    /// one, and <see cref="DownloadManager.Start"/> clears it.</summary>
    public bool IsRefreshingLink
    {
        get => _isRefreshingLink;
        set
        {
            if (_isRefreshingLink == value) return;
            _isRefreshingLink = value;
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    public long Downloaded
    {
        get => _item.Downloaded;
        set
        {
            if (_item.Downloaded != value)
            {
                _item.Downloaded = value;
                this.RaisePropertyChanged();
            }
        }
    }

    /// <summary>Download progress as a percentage (0–100), bound to the row's progress bar.</summary>
    public double Progress
    {
        get => _progress;
        set
        {
            this.RaiseAndSetIfChanged(ref _progress, value);
            // The status text shows the live "%" while running, so refresh it with progress.
            if (_status == DownloadStatus.Running)
                this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>Current transfer speed in bytes/second.</summary>
    public double Speed
    {
        get => _speed;
        set
        {
            this.RaiseAndSetIfChanged(ref _speed, value);
            this.RaisePropertyChanged(nameof(SpeedText));
            this.RaisePropertyChanged(nameof(TimeLeftText));
        }
    }

    public DownloadStatus Status
    {
        get => _status;
        set
        {
            this.RaiseAndSetIfChanged(ref _status, value);
            _item.Status = value;
            // A completed row is always shown full, regardless of how it got there.
            if (value == DownloadStatus.Completed)
                Progress = 100;
            this.RaisePropertyChanged(nameof(StatusText));
            this.RaisePropertyChanged(nameof(CanPause));
            this.RaisePropertyChanged(nameof(CanResume));
            this.RaisePropertyChanged(nameof(CanRetry));
            this.RaisePropertyChanged(nameof(IsActive));
            this.RaisePropertyChanged(nameof(IsCompleted));
            this.RaisePropertyChanged(nameof(HasError));
            this.RaisePropertyChanged(nameof(DisplayName));
            this.RaisePropertyChanged(nameof(NameTooltip));
            this.RaisePropertyChanged(nameof(IsNamePending));
            this.RaisePropertyChanged(nameof(ShowStatusBadge));
            this.RaisePropertyChanged(nameof(TimeLeftText));
            this.RaisePropertyChanged(nameof(PostActionLabel));
            this.RaisePropertyChanged(nameof(HasPostAction));
        }
    }

    /// <summary>True when this item opts out of the global speed limit (write-through to the model, like Status).</summary>
    public bool HasCustomSpeedLimit
    {
        get => _item.HasCustomSpeedLimit;
        set { _item.HasCustomSpeedLimit = value; this.RaisePropertyChanged(); }
    }

    /// <summary>The persisted per-item speed cap in bytes/sec (write-through to the model).</summary>
    public long CustomSpeedLimitBytesPerSecond
    {
        get => _item.CustomSpeedLimitBytesPerSecond;
        set { _item.CustomSpeedLimitBytesPerSecond = value; this.RaisePropertyChanged(); }
    }

    public bool IsChecked
    {
        get => _isChecked;
        set => this.RaiseAndSetIfChanged(ref _isChecked, value);
    }

    private bool _alreadyExisted;

    /// <summary>True when the download was skipped because the file was already on disk
    /// (FileExistPolicy=IgnoreDownload). The row is Completed, but the status text says so.</summary>
    public bool AlreadyExisted
    {
        get => _alreadyExisted;
        set
        {
            if (_alreadyExisted != value)
            {
                _alreadyExisted = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(StatusText));
            }
        }
    }

    private static string L(string key) => Localizer.Instance[key];

    private PlanRunState _planRun;

    /// <summary>Live per-segment progress board while a multi-part plan runs (null otherwise). The
    /// details dialog renders it as the "connections" list: waiting / downloading / done per segment.</summary>
    public PlanRunState PlanRun
    {
        get => _planRun;
        set => this.RaiseAndSetIfChanged(ref _planRun, value);
    }

    /// <summary>Live control surface for a running multi-part plan (null otherwise): the set of part engines
    /// currently in flight plus the paused gate the runner checks before starting another part. Pause/Resume/
    /// Cancel must act through this, NOT through <see cref="Download"/> — that is only the most recently
    /// started part, so pausing it left the other parallel segments transferring (issue #7 follow-up).</summary>
    public PlanController PlanControl { get; set; }

    /// <summary>Set by the multi-part plan runner to show "Part i/N" while downloading segments and
    /// "Assembling…" during post-processing. Null for a normal single-file download.</summary>
    public string PlanStage
    {
        get => _planStage;
        set
        {
            if (_planStage == value) return;
            _planStage = value;
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    public string StatusText => Status switch
    {
        // A queued row that is waiting on a freshly resolved link says so instead of a bare "Queued".
        DownloadStatus.None or DownloadStatus.Created when _isRefreshingLink => L("State_RefreshingLink"),
        DownloadStatus.None or DownloadStatus.Created => L("State_Queued"),
        DownloadStatus.Running when !string.IsNullOrEmpty(_planStage) => $"{_planStage} · {Progress:0}%",
        DownloadStatus.Running => $"{Progress:0}%",
        // Keep the percentage visible (and the bar filled) when paused/stopped, not just a state word.
        DownloadStatus.Paused => $"{Progress:0}% · {L("State_Paused")}",
        DownloadStatus.Stopped => $"{Progress:0}% · {L("State_Stopped")}",
        DownloadStatus.Completed => _alreadyExisted ? L("State_Exists") : L("State_Completed"),
        DownloadStatus.Failed => L("State_Failed"),
        _ => Status.ToString()
    };

    public string SizeText => Size is > 0 ? FormatBytes(Size.Value) : "—";

    public string SpeedText => Speed > 0 ? FormatBytes((long)Speed) + "/s" : "—";

    /// <summary>Estimated time remaining (remaining bytes ÷ current speed). "—" unless actively running.</summary>
    public string TimeLeftText
    {
        get
        {
            if (_status != DownloadStatus.Running || Speed <= 0 || Size is not > 0)
                return "—";
            var remaining = Size.Value - Downloaded;
            if (remaining <= 0)
                return "—";
            return FormatDuration(remaining / Speed);
        }
    }

    /// <summary>Formats a number of seconds as a compact duration (e.g. "45s", "1m 23s", "2h 5m").</summary>
    public static string FormatDuration(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            return "—";
        var t = TimeSpan.FromSeconds(seconds);
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1)
            return $"{t.Minutes}m {t.Seconds}s";
        return $"{t.Seconds}s";
    }

    /// <summary>The owning manager (lets the details dialog read the global speed limit / apply it back).</summary>
    public IDownloadManager Manager => _manager;

    public string LastTry => _item.LastTry?.ToString("dd MMM yyyy");

    /// <summary>Show a colored status badge for every state except active downloading (which shows %).</summary>
    public bool ShowStatusBadge => _status != DownloadStatus.Running;

    public bool CanPause => Status == DownloadStatus.Running;
    public bool CanResume => Status is DownloadStatus.Paused or DownloadStatus.Stopped
        or DownloadStatus.Created or DownloadStatus.None;
    public bool CanRetry => Status == DownloadStatus.Failed;
    public bool IsActive => Status is DownloadStatus.Running or DownloadStatus.Paused;
    public bool IsCompleted => Status == DownloadStatus.Completed;

    public DownloadItem GetItem() => _item;

    private void OpenContainingFolder()
    {
        // Open the folder AND highlight the file in the OS file manager (#8). For an in-progress download
        // the final file doesn't exist yet — the engine writes "<name>.download" — so reveal that temp
        // file instead, then fall back to just opening the folder.
        var final = _item.FilePath;
        if (!string.IsNullOrWhiteSpace(final) && File.Exists(final))
            RevealInFolder(final);
        else if (!string.IsNullOrWhiteSpace(final) && File.Exists(final + ".download"))
            RevealInFolder(final + ".download");
        else
            ShellOpen(_item.FolderPath);
    }

    private void OpenFile()
    {
        var path = _item.FilePath;
        ShellOpen(File.Exists(path) ? path : _item.FolderPath);
    }

    /// <summary>Opens the containing folder with the file selected/highlighted, cross-platform.</summary>
    private static void RevealInFolder(string path)
    {
        var revealed = OperatingSystem.IsWindows()
            ? Services.ShellLauncher.Run("explorer.exe", $"/select,\"{path}\"")
            : OperatingSystem.IsMacOS()
                ? Services.ShellLauncher.Run("open", "-R", path)
                // Linux: the FileManager1 D-Bus interface selects the item in Nautilus/Dolphin/etc.
                : Services.ShellLauncher.Run("dbus-send",
                    "--session",
                    "--dest=org.freedesktop.FileManager1",
                    "--type=method_call",
                    "/org/freedesktop/FileManager1",
                    "org.freedesktop.FileManager1.ShowItems",
                    "array:string:file://" + path,
                    "string:");

        if (!revealed)
            // Fall back to just opening the folder if the reveal mechanism isn't available.
            ShellOpen(Path.GetDirectoryName(path));
    }

    private static void ShellOpen(string target) => Services.ShellLauncher.Open(target);

    /// <summary>Human-readable byte size (e.g. "12.5 MB"). Public so other VMs (Queues) can reuse it.</summary>
    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }
}
