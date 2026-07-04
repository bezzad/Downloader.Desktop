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

    /// <summary>Raised when items are added/removed or change status (refresh the filtered list).</summary>
    event Action ListChanged;

    /// <summary>Raised once (on the UI thread) when a download finishes and nothing is left running or
    /// queued — used for the "all downloads complete" notification and shutdown-on-completion.</summary>
    event Action AllDownloadsCompleted;

    /// <summary>Raised when the queue set changes (a queue is added or removed) so per-queue menus refresh
    /// without an app restart.</summary>
    event Action QueuesChanged;

    /// <summary>The configured queues (read-only view for building per-queue menus).</summary>
    System.Collections.Generic.IReadOnlyList<DownloadQueue> Queues { get; }

    /// <summary>The shared config instance loaded at startup (null until <see cref="Initialize"/> runs).</summary>
    Config Config { get; }

    /// <summary>Combined speed of all running downloads, bytes/second.</summary>
    double TotalSpeed { get; }

    int ActiveCount { get; }
    int QueuedCount { get; }
    int CompletedCount { get; }

    /// <summary>Loads persisted items from the given config into <see cref="Items"/>.</summary>
    void Initialize(Config config);

    /// <summary>Adds a new download descriptor and (optionally) starts it immediately.</summary>
    DownloadItemViewModel Add(DownloadItem item, bool autoStart);

    /// <summary>Runs a bulk action, coalescing its many list-change notifications into one refresh.</summary>
    void Batch(Action action);

    void Start(DownloadItemViewModel vm);
    void Pause(DownloadItemViewModel vm);
    void Resume(DownloadItemViewModel vm);
    void Cancel(DownloadItemViewModel vm);
    void Retry(DownloadItemViewModel vm);
    Task Remove(DownloadItemViewModel vm);

    /// <summary>Resumes every paused/stopped/ready item.</summary>
    void StartAll();

    /// <summary>Stops every item (running, paused or queued) — cancels their engines and marks them Stopped.</summary>
    void StopAll();

    /// <summary>Removes every completed item from the list.</summary>
    void ClearCompleted();

    // ---- Queues ----

    /// <summary>Starts (up to the cap) the queued items in a queue and marks it running.</summary>
    void StartQueue(DownloadQueue queue);

    /// <summary>Pauses every running item in a queue and marks it paused.</summary>
    void PauseQueue(DownloadQueue queue);

    /// <summary>Stops every item in a queue (running/paused/queued → Stopped) and marks it not running.</summary>
    void StopQueue(DownloadQueue queue);

    /// <summary>Starts the next queued item(s) while a concurrency slot is free.</summary>
    void PumpQueue(string queueId);

    DownloadQueue AddQueue(string name);

    /// <summary>Removes a queue, reassigning its items to another queue (keeps at least one).</summary>
    void RemoveQueue(DownloadQueue queue);

    /// <summary>Moves a download into another queue and re-pumps both (capped) queues.</summary>
    void MoveToQueue(DownloadItemViewModel vm, string queueId);

    /// <summary>Changes a download's priority within its queue by shifting it earlier/later in the
    /// list (<paramref name="direction"/> -1 = up/sooner, +1 = down/later); pump order follows list order.</summary>
    void MovePriority(DownloadItemViewModel vm, int direction);

    /// <summary>Drag-reorders <paramref name="vm"/> to sit before/after <paramref name="target"/> in the
    /// master list (= pump priority). If the target is in a different queue, the dragged item adopts that
    /// queue and both queues are re-pumped.</summary>
    void ReorderTo(DownloadItemViewModel vm, DownloadItemViewModel target, bool placeAfter);
}
