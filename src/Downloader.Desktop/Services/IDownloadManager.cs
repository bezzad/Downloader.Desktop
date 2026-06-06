using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Downloader.Desktop.Models;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Services;

/// <summary>
/// Owns the lifetime of every download: builds the engine instances, relays their events to the
/// row view models, and drives per-item and bulk actions. ViewModels stay thin and delegate here.
/// </summary>
public interface IDownloadManager
{
    /// <summary>The master, UI-bound list of downloads.</summary>
    ObservableCollection<DownloadItemViewModel> Items { get; }

    /// <summary>Raised (on the UI thread) when aggregate stats change, for the status bar.</summary>
    event Action StatsChanged;

    /// <summary>Combined speed of all running downloads, bytes/second.</summary>
    double TotalSpeed { get; }

    int ActiveCount { get; }
    int QueuedCount { get; }
    int CompletedCount { get; }

    /// <summary>Loads persisted items from the given config into <see cref="Items"/>.</summary>
    void Initialize(Config config);

    /// <summary>Adds a new download descriptor and (optionally) starts it immediately.</summary>
    DownloadItemViewModel Add(DownloadItem item, bool autoStart);

    void Start(DownloadItemViewModel vm);
    void Pause(DownloadItemViewModel vm);
    void Resume(DownloadItemViewModel vm);
    void Cancel(DownloadItemViewModel vm);
    void Retry(DownloadItemViewModel vm);
    Task Remove(DownloadItemViewModel vm);

    /// <summary>Resumes every paused/stopped/ready item.</summary>
    void StartAll();

    /// <summary>Pauses every active item.</summary>
    void StopAll();

    /// <summary>Removes every completed item from the list.</summary>
    void ClearCompleted();
}
