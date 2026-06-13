using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Threading;
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
    public bool HasConfig => Item?.Configuration != null;
    public int Connections => Item?.Configuration?.ChunkCount ?? 0;

    public DownloadDetailsViewModel()
    {
    }

    public DownloadDetailsViewModel(DownloadItemViewModel item)
    {
        Item = item;
        _download = item?.Download;

        if (_download != null)
        {
            var chunks = _download.Package?.Chunks;
            if (chunks != null)
                foreach (var c in chunks)
                    GetOrAddPart(c.Id);

            _download.ChunkDownloadProgressChanged += OnChunkProgress;
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
            part.Progress = e.ProgressPercentage;
            part.Speed = e.BytesPerSecondSpeed;
            part.Received = e.ReceivedBytesSize;
            part.Total = e.TotalBytesToReceive;
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
        }

        return part;
    }

    /// <summary>Detach engine handlers; call when the dialog closes.</summary>
    public void Cleanup()
    {
        if (_download != null)
            _download.ChunkDownloadProgressChanged -= OnChunkProgress;
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

    public double Progress
    {
        get => _progress;
        set => this.RaiseAndSetIfChanged(ref _progress, value);
    }

    public double Speed
    {
        get => _speed;
        set
        {
            this.RaiseAndSetIfChanged(ref _speed, value);
            this.RaisePropertyChanged(nameof(SpeedText));
        }
    }

    public long Received
    {
        get => _received;
        set
        {
            this.RaiseAndSetIfChanged(ref _received, value);
            this.RaisePropertyChanged(nameof(RangeText));
        }
    }

    public long Total
    {
        get => _total;
        set
        {
            this.RaiseAndSetIfChanged(ref _total, value);
            this.RaisePropertyChanged(nameof(RangeText));
        }
    }

    public string RangeText => _total > 0 ? $"{FormatBytes(_received)} / {FormatBytes(_total)}" : FormatBytes(_received);

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
