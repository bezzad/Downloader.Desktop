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
    private readonly IDownload _download;
    private readonly Dictionary<string, ChunkProgressViewModel> _parts = new();
    private DateTime _lastTickUtc;

    public DownloadItemViewModel Item { get; }
    public ObservableCollection<ChunkProgressViewModel> Parts { get; } = new();

    public bool HasParts => Parts.Count > 0;
    public string PartsSummary => Parts.Count > 0 ? $"{Parts.Count} connections" : string.Empty;
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

    private void OnItemPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadItemViewModel.Status) or nameof(DownloadItemViewModel.HasError))
            Dispatcher.UIThread.Post(() =>
            {
                this.RaisePropertyChanged(nameof(CanEdit));
                this.RaisePropertyChanged(nameof(HasError));
                this.RaisePropertyChanged(nameof(ErrorMessage));
            });
    }

    /// <summary>The source URL can be edited only while the download is not active.</summary>
    public bool CanEdit => Item != null && Item.Status is DownloadStatus.Stopped or DownloadStatus.Failed
        or DownloadStatus.Paused or DownloadStatus.Created or DownloadStatus.None;

    public string EditableUrl
    {
        get => Item?.Url;
        set { if (Item != null) Item.Url = value; this.RaisePropertyChanged(); }
    }

    /// <summary>Mirror URLs, one per line. Editable while stopped.</summary>
    public string MirrorsText
    {
        get => Item?.GetItem().Mirrors is { Count: > 0 } m ? string.Join(Environment.NewLine, m) : string.Empty;
        set
        {
            if (Item != null)
                Item.GetItem().Mirrors = (value ?? string.Empty)
                    .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            this.RaisePropertyChanged();
        }
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

    public string StatusText => _progress >= 99.99 ? "Completed" : _progress > 0 ? "Downloading" : "Pending";
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
