using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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
    private readonly DownloadService _download;
    private readonly Dictionary<string, ChunkProgressViewModel> _parts = new();
    private DateTime _lastTickUtc;

    public DownloadItemViewModel Item { get; }
    public ObservableCollection<ChunkProgressViewModel> Parts { get; } = new();

    private static string L(string key) => Localizer.Instance[key];

    public bool HasParts => Parts.Count > 0;
    public string PartsSummary => Parts.Count > 0 ? string.Format(L("Det_ConnCount"), Parts.Count) : string.Empty;
    public bool HasConfig => Item?.Configuration != null;
    public int Connections => Item?.Configuration?.ChunkCount ?? 0;

    public DownloadDetailsViewModel()
    {
    }

    public DownloadDetailsViewModel(DownloadItemViewModel item)
    {
        Item = item;
        _download = item?.Download;

        CopyUrlCommand = ReactiveCommand.CreateFromTask(() => DialogHelper.CopyTextAsync(Item?.Url));
        AddMirrorCommand = ReactiveCommand.Create(() => AddMirror(string.Empty));

        // Seed the mirror editor from the stored mirrors (everything after the primary URL).
        if (Item?.GetItem().Mirrors is { } existing)
            foreach (var m in existing)
                AddMirror(m, sync: false);

        if (item != null)
            ((INotifyPropertyChanged)item).PropertyChanged += OnItemPropertyChanged;

        if (_download != null)
        {
            var chunks = _download.Package?.Chunks;
            if (chunks != null)
                foreach (var c in chunks)
                    GetOrAddPart(c.Id);

            _download.ChunkDownloadProgressChanged += OnChunkProgress;
        }
    }

    public ICommand CopyUrlCommand { get; }
    public ICommand AddMirrorCommand { get; }

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
        if (e.PropertyName is nameof(DownloadItemViewModel.Status) or nameof(DownloadItemViewModel.HasError)
            or nameof(DownloadItemViewModel.FileName))
            Dispatcher.UIThread.Post(() =>
            {
                this.RaisePropertyChanged(nameof(CanEdit));
                this.RaisePropertyChanged(nameof(HasError));
                this.RaisePropertyChanged(nameof(ErrorMessage));
                this.RaisePropertyChanged(nameof(FilePath));
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

    public bool HasError => Item?.HasError == true;
    public string ErrorMessage => Item?.ErrorMessage;

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
            if (Item?.Configuration != null)
                Item.Configuration.MaximumBytesPerSecond = value <= 0 ? 0 : value * 1024;
            this.RaisePropertyChanged();
        }
    }

    private void OnChunkProgress(object sender, DownloadProgressChangedEventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastTickUtc).TotalMilliseconds < 150)
            return;
        _lastTickUtc = now;

        Dispatcher.UIThread.Post(() =>
        {
            var part = GetOrAddPart(e.ProgressId);
            part.Update(e.ProgressPercentage, e.BytesPerSecondSpeed, e.ReceivedBytesSize, e.TotalBytesToReceive);
        });
    }

    private ChunkProgressViewModel GetOrAddPart(string id)
    {
        id ??= "main";
        if (!_parts.TryGetValue(id, out var part))
        {
            part = new ChunkProgressViewModel(Parts.Count + 1);
            _parts[id] = part;
            Parts.Add(part);
            this.RaisePropertyChanged(nameof(HasParts));
            this.RaisePropertyChanged(nameof(PartsSummary));
        }

        return part;
    }

    /// <summary>Detach engine handlers; call when the dialog closes.</summary>
    public void Cleanup()
    {
        if (_download != null)
            _download.ChunkDownloadProgressChanged -= OnChunkProgress;
        if (Item != null)
            ((INotifyPropertyChanged)Item).PropertyChanged -= OnItemPropertyChanged;
    }
}

/// <summary>One editable mirror URL row in the details dialog.</summary>
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

    public ChunkProgressViewModel(int index)
    {
        Index = index;
    }

    public int Index { get; }
    public string Title => $"Part {Index}";

    public void Update(double progress, double speed, long received, long total)
    {
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

    public double Progress => _progress;

    public string StatusText => _progress >= 99.99
        ? Localizer.Instance["State_Completed"]
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
