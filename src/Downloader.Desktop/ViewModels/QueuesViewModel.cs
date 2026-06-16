using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Avalonia.Collections;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// The Queues management page: list of queues with concurrency caps + run/pause, and the items in each.
/// </summary>
public class QueuesViewModel : ViewModelBase
{
    private readonly Config _config;
    private readonly IDownloadManager _manager;

    public ObservableCollection<QueueRowViewModel> Queues { get; } = new();
    public ICommand NewQueueCommand { get; }

    public QueuesViewModel()
    {
    }

    public QueuesViewModel(Config config, IDownloadManager manager)
    {
        _config = config;
        _manager = manager;
        NewQueueCommand = ReactiveCommand.Create(AddQueue);
        Reload();
    }

    private void Reload()
    {
        Queues.Clear();
        if (_config?.Queues == null)
            return;
        foreach (var q in _config.Queues)
            Queues.Add(new QueueRowViewModel(q, _manager, this, _config));
    }

    private void AddQueue()
    {
        var queue = _manager.AddQueue("New queue");
        Queues.Add(new QueueRowViewModel(queue, _manager, this, _config));
    }

    public void Remove(QueueRowViewModel row)
    {
        if (Queues.Count <= 1)
            return;
        _manager.RemoveQueue(row.Queue);
        Queues.Remove(row);
    }
}

/// <summary>A single queue card.</summary>
public class QueueRowViewModel : ViewModelBase
{
    private readonly IDownloadManager _manager;
    private readonly QueuesViewModel _parent;
    private readonly Config _config;

    public DownloadQueue Queue { get; }
    public DataGridCollectionView Items { get; }
    public ICommand RemoveCommand { get; }

    public QueueRowViewModel(DownloadQueue queue, IDownloadManager manager, QueuesViewModel parent, Config config = null)
    {
        Queue = queue;
        _manager = manager;
        _parent = parent;
        _config = config;
        Items = new DataGridCollectionView(manager.Items)
        {
            Filter = o => o is DownloadItemViewModel vm && vm.GetItem().QueueId == queue.Id
        };
        RemoveCommand = ReactiveCommand.Create(() => _parent.Remove(this));

        if (_manager != null)
            _manager.ListChanged += RefreshCounts;
    }

    private void RefreshCounts()
    {
        Items.Refresh();
        this.RaisePropertyChanged(nameof(TotalCount));
        this.RaisePropertyChanged(nameof(IsEmpty));
        this.RaisePropertyChanged(nameof(Summary));
        this.RaisePropertyChanged(nameof(IsRunning));
    }

    private int CountWhere(Func<DownloadItemViewModel, bool> predicate) =>
        _manager?.Items.Count(i => i.GetItem().QueueId == Queue.Id && predicate(i)) ?? 0;

    public int TotalCount => CountWhere(_ => true);
    public bool IsEmpty => TotalCount == 0;

    /// <summary>Plain-language summary of what's in the queue.</summary>
    public string Summary
    {
        get
        {
            var running = CountWhere(i => i.Status == DownloadStatus.Running);
            var waiting = CountWhere(i => i.Status is DownloadStatus.Created or DownloadStatus.None or DownloadStatus.Paused or DownloadStatus.Stopped);
            var done = CountWhere(i => i.Status == DownloadStatus.Completed);
            var failed = CountWhere(i => i.Status == DownloadStatus.Failed);

            var parts = new System.Collections.Generic.List<string>
            {
                $"{running} downloading",
                $"{waiting} waiting",
                $"{done} done"
            };
            if (failed > 0)
                parts.Add($"{failed} failed");
            return string.Join("  ·  ", parts) + $"  ·  up to {Math.Max(1, Queue.MaxConcurrent)} at once";
        }
    }

    public string Name
    {
        get => Queue.Name;
        set { Queue.Name = value; this.RaisePropertyChanged(); }
    }

    public int MaxConcurrent
    {
        get => Queue.MaxConcurrent;
        set
        {
            Queue.MaxConcurrent = Math.Max(1, value);
            // The primary queue mirrors the Settings "Max concurrent downloads" value (single source
            // of truth), so editing it here keeps the setting in sync — otherwise the setting would
            // overwrite this cap on the next launch.
            if (_config != null && ReferenceEquals(Queue, _config.DefaultQueue) && _config.Settings != null)
                _config.Settings.MaxConcurrentDownloads = Queue.MaxConcurrent;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(Summary));
            _manager.PumpQueue(Queue.Id);
        }
    }

    public bool IsRunning
    {
        get => Queue.IsRunning;
        set
        {
            if (value)
                _manager.StartQueue(Queue);
            else
                _manager.PauseQueue(Queue);
            this.RaisePropertyChanged();
        }
    }
}
