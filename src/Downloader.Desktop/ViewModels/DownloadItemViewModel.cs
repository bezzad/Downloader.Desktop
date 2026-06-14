using System;
using System.IO;
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

    /// <summary>The live engine handle while this item is downloading/paused; null otherwise.</summary>
    public IDownload Download { get; set; }

    /// <summary>Timestamp of the last UI progress update (used to throttle high-frequency events).</summary>
    public DateTime LastUiUpdateUtc { get; set; }

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
        _progress = _item.Size is > 0 ? (double)_item.Downloaded / _item.Size.Value * 100 : 0;

        PauseCommand = ReactiveCommand.Create(() => _manager?.Pause(this));
        ResumeCommand = ReactiveCommand.Create(() => _manager?.Resume(this));
        CancelCommand = ReactiveCommand.Create(() => _manager?.Cancel(this));
        RetryCommand = ReactiveCommand.Create(() => _manager?.Retry(this));
        RemoveCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (_manager != null) await _manager.Remove(this);
        });
        OpenFolderCommand = ReactiveCommand.Create(OpenContainingFolder);
    }

    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand OpenFolderCommand { get; }

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
                this.RaisePropertyChanged(nameof(IsNamePending));
                this.RaisePropertyChanged(nameof(FileKind));
            }
        }
    }

    /// <summary>Coarse file category (video/audio/image/archive/document/app/disc/file) by extension.</summary>
    public string FileKind => GetFileKind(_item.FileName);

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

    /// <summary>Name shown in the list — a placeholder while the engine resolves the real name.</summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(_item.FileName) ? _item.FileName
        : IsNamePending ? "Fetching name…"
        : "(unnamed)";

    /// <summary>True while we are still waiting for the engine to report the file name.</summary>
    public bool IsNamePending =>
        string.IsNullOrWhiteSpace(_item.FileName) &&
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
        }
    }

    public DownloadStatus Status
    {
        get => _status;
        set
        {
            this.RaiseAndSetIfChanged(ref _status, value);
            _item.Status = value;
            this.RaisePropertyChanged(nameof(StatusText));
            this.RaisePropertyChanged(nameof(CanPause));
            this.RaisePropertyChanged(nameof(CanResume));
            this.RaisePropertyChanged(nameof(CanRetry));
            this.RaisePropertyChanged(nameof(IsActive));
            this.RaisePropertyChanged(nameof(IsCompleted));
            this.RaisePropertyChanged(nameof(HasError));
            this.RaisePropertyChanged(nameof(DisplayName));
            this.RaisePropertyChanged(nameof(IsNamePending));
        }
    }

    public bool IsChecked
    {
        get => _isChecked;
        set => this.RaiseAndSetIfChanged(ref _isChecked, value);
    }

    public string StatusText => Status switch
    {
        DownloadStatus.None or DownloadStatus.Created => "Queued",
        DownloadStatus.Running => $"{Progress:0}%",
        DownloadStatus.Paused => "Paused",
        DownloadStatus.Stopped => "Stopped",
        DownloadStatus.Completed => "Completed",
        DownloadStatus.Failed => "Failed",
        _ => Status.ToString()
    };

    public string SizeText => Size is > 0 ? FormatBytes(Size.Value) : "—";

    public string SpeedText => Speed > 0 ? FormatBytes((long)Speed) + "/s" : "—";

    public string LastTry => _item.LastTry?.ToString("dd MMM yyyy");

    public bool CanPause => Status == DownloadStatus.Running;
    public bool CanResume => Status is DownloadStatus.Paused or DownloadStatus.Stopped
        or DownloadStatus.Created or DownloadStatus.None;
    public bool CanRetry => Status == DownloadStatus.Failed;
    public bool IsActive => Status is DownloadStatus.Running or DownloadStatus.Paused;
    public bool IsCompleted => Status == DownloadStatus.Completed;

    public DownloadItem GetItem() => _item;

    private void OpenContainingFolder()
    {
        var folder = _item.FolderPath;
        if (string.IsNullOrWhiteSpace(folder))
            return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch
        {
            // Opening the folder is best-effort; ignore platform/shell failures.
        }
    }

    private static string FormatBytes(long bytes)
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
