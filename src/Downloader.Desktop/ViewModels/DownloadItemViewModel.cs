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

    private DownloadService _download;

    /// <summary>The live engine handle while this item is downloading/paused; null otherwise.
    /// Notifies so a details dialog opened before the download started can attach once it's set.</summary>
    public DownloadService Download
    {
        get => _download;
        set => this.RaiseAndSetIfChanged(ref _download, value);
    }

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
        }
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

    public string StatusText => Status switch
    {
        DownloadStatus.None or DownloadStatus.Created => L("State_Queued"),
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
        try
        {
            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else if (OperatingSystem.IsMacOS())
            {
                var psi = new System.Diagnostics.ProcessStartInfo("open") { UseShellExecute = false };
                psi.ArgumentList.Add("-R");
                psi.ArgumentList.Add(path);
                System.Diagnostics.Process.Start(psi);
            }
            else
            {
                // Linux: the FileManager1 D-Bus interface selects the item in Nautilus/Dolphin/etc.
                var psi = new System.Diagnostics.ProcessStartInfo("dbus-send") { UseShellExecute = false };
                psi.ArgumentList.Add("--session");
                psi.ArgumentList.Add("--dest=org.freedesktop.FileManager1");
                psi.ArgumentList.Add("--type=method_call");
                psi.ArgumentList.Add("/org/freedesktop/FileManager1");
                psi.ArgumentList.Add("org.freedesktop.FileManager1.ShowItems");
                psi.ArgumentList.Add("array:string:file://" + path);
                psi.ArgumentList.Add("string:");
                System.Diagnostics.Process.Start(psi);
            }
        }
        catch
        {
            // Fall back to just opening the folder if the reveal mechanism isn't available.
            ShellOpen(Path.GetDirectoryName(path));
        }
    }

    private static void ShellOpen(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch
        {
            // Opening is best-effort; ignore platform/shell failures.
        }
    }

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
