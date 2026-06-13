using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// Details dialog for a single download: top-level info (via <see cref="Item"/>) plus live
/// per-part (chunk) progress sourced from the engine's ChunkDownloadProgressChanged event.
/// </summary>
public class DownloadDetailsViewModel : ViewModelBase
{
    private readonly IDownload _download;
    private readonly Dictionary<string, ChunkProgressViewModel> _parts = new();
    private DateTime _lastTickUtc;

    public DownloadItemViewModel Item { get; }
    public ObservableCollection<ChunkProgressViewModel> Parts { get; } = new();

    public bool HasParts => Parts.Count > 0;

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
