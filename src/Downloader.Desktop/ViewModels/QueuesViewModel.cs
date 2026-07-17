using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// The Queues management page: a card per queue with live aggregate stats, a combined progress bar,
/// run/pause + concurrency cap, and the queue's downloads with per-item progress, actions, reorder
/// (priority) and move-between-queues.
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
        foreach (var row in Queues)
            row.Detach();
        Queues.Clear();
        if (_config?.Queues == null)
            return;
        foreach (var q in _config.Queues)
            Queues.Add(new QueueRowViewModel(q, _manager, this, _config));
        RaiseQueuesChanged();
    }

    private void AddQueue()
    {
        var queue = _manager.AddQueue("New queue");
        Queues.Add(new QueueRowViewModel(queue, _manager, this, _config));
        RaiseQueuesChanged();
    }

    public void Remove(QueueRowViewModel row)
    {
        if (Queues.Count <= 1)
            return;
        _manager.RemoveQueue(row.Queue);
        row.Detach();
        Queues.Remove(row);
        RaiseQueuesChanged();
    }

    /// <summary>The other queues a download can be moved into (everything except <paramref name="self"/>).</summary>
    public IEnumerable<DownloadQueue> QueuesOtherThan(QueueRowViewModel self) =>
        Queues.Where(r => !ReferenceEquals(r, self)).Select(r => r.Queue).ToList();

    /// <summary>Toolbar collapse/expand-all (#10): true when every queue card is collapsed. Setting it
    /// folds/unfolds all cards, so a user with many long queues can jump to the one they want.
    /// In-session only (not persisted), like the Settings sections.</summary>
    public bool AllCollapsed
    {
        get => Queues.Count > 0 && Queues.All(q => !q.IsExpanded);
        set
        {
            foreach (var row in Queues)
                row.IsExpanded = !value;
            this.RaisePropertyChanged();
        }
    }

    /// <summary>Called by rows when their own expander toggles, so the toolbar toggle tracks the set.</summary>
    internal void RaiseAllCollapsedChanged() => this.RaisePropertyChanged(nameof(AllCollapsed));

    /// <summary>When the set of queues changes, rebuild each card's item list so move-targets stay current.</summary>
    private void RaiseQueuesChanged()
    {
        foreach (var row in Queues)
            row.RebuildItems();
    }
}

/// <summary>A single queue card: header, aggregate stats, combined progress and its downloads.</summary>
public class QueueRowViewModel : ViewModelBase
{
    private readonly IDownloadManager _manager;
    private readonly QueuesViewModel _parent;
    private readonly Config _config;

    public DownloadQueue Queue { get; }
    public ObservableCollection<QueueItemViewModel> Items { get; } = new();
    public ICommand RemoveCommand { get; }

    private bool _isExpanded = true;

    /// <summary>Whether this card's item list is unfolded (#10). The header (name, stats, toggle,
    /// cap) stays visible either way; collapsing hides only the list. Default expanded.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            this.RaiseAndSetIfChanged(ref _isExpanded, value);
            _parent?.RaiseAllCollapsedChanged();
        }
    }

    public QueueRowViewModel(DownloadQueue queue, IDownloadManager manager, QueuesViewModel parent, Config config = null)
    {
        Queue = queue;
        _manager = manager;
        _parent = parent;
        _config = config;
        RemoveCommand = ReactiveCommand.Create(() => _parent.Remove(this));

        if (_manager != null)
        {
            _manager.ListChanged += OnListChanged;
            _manager.StatsChanged += RefreshStats; // live speed/progress while downloading
        }
        RebuildItems();
    }

    public void Detach()
    {
        if (_manager == null)
            return;
        _manager.ListChanged -= OnListChanged;
        _manager.StatsChanged -= RefreshStats;
    }

    private void OnListChanged()
    {
        RebuildItems();
        RefreshStats();
    }

    /// <summary>Rebuilds the queue's item wrappers from the manager (order = pump priority).</summary>
    public void RebuildItems()
    {
        var mine = _manager?.Items.Where(i => i.GetItem().QueueId == Queue.Id).ToList()
                   ?? new List<DownloadItemViewModel>();

        Items.Clear();
        foreach (var vm in mine)
            Items.Add(new QueueItemViewModel(vm, _manager, this));

        this.RaisePropertyChanged(nameof(IsEmpty));
        this.RaisePropertyChanged(nameof(HasItems));
    }

    private void RefreshStats()
    {
        this.RaisePropertyChanged(nameof(RunningCount));
        this.RaisePropertyChanged(nameof(WaitingCount));
        this.RaisePropertyChanged(nameof(DoneCount));
        this.RaisePropertyChanged(nameof(FailedCount));
        this.RaisePropertyChanged(nameof(HasFailed));
        this.RaisePropertyChanged(nameof(SummaryText));
        this.RaisePropertyChanged(nameof(TotalSpeedText));
        this.RaisePropertyChanged(nameof(OverallProgress));
        this.RaisePropertyChanged(nameof(IsRunning));
    }

    private IEnumerable<DownloadItemViewModel> Mine =>
        _manager?.Items.Where(i => i.GetItem().QueueId == Queue.Id) ?? Enumerable.Empty<DownloadItemViewModel>();

    public IEnumerable<DownloadQueue> OtherQueues => _parent?.QueuesOtherThan(this) ?? Enumerable.Empty<DownloadQueue>();

    public int RunningCount => Mine.Count(i => i.Status == DownloadStatus.Running);
    public int WaitingCount => Mine.Count(i => i.Status is DownloadStatus.Created or DownloadStatus.None
                                                or DownloadStatus.Paused or DownloadStatus.Stopped);
    public int DoneCount => Mine.Count(i => i.Status == DownloadStatus.Completed);
    public int FailedCount => Mine.Count(i => i.Status == DownloadStatus.Failed);
    public bool HasFailed => FailedCount > 0;

    public int TotalCount => Mine.Count();
    public bool IsEmpty => TotalCount == 0;
    public bool HasItems => TotalCount > 0;

    /// <summary>e.g. "2 downloading · 1 waiting · 5 done · 12.4 MB/s".</summary>
    public string SummaryText
    {
        get
        {
            var parts = new List<string>
            {
                $"{RunningCount} downloading",
                $"{WaitingCount} waiting",
                $"{DoneCount} done"
            };
            if (HasFailed)
                parts.Add($"{FailedCount} failed");
            var speed = TotalSpeedText;
            if (speed != null)
                parts.Add(speed);
            return string.Join("  ·  ", parts);
        }
    }

    public string TotalSpeedText
    {
        get
        {
            var bytes = Mine.Where(i => i.Status == DownloadStatus.Running).Sum(i => i.Speed);
            return bytes > 0 ? DownloadItemViewModel.FormatBytes((long)bytes) + "/s" : null;
        }
    }

    /// <summary>Combined progress 0–100 (average across the queue's items; empty queue = 0).</summary>
    public double OverallProgress
    {
        get
        {
            var items = Mine.ToList();
            return items.Count == 0 ? 0 : items.Average(i => i.Progress);
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
            this.RaisePropertyChanged(nameof(SummaryText));
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
            RefreshStats();
        }
    }
}

/// <summary>One download inside a queue card: the live row VM plus reorder / move-to-queue actions.</summary>
public class QueueItemViewModel : ViewModelBase
{
    private readonly IDownloadManager _manager;
    private readonly QueueRowViewModel _row;

    public DownloadItemViewModel Item { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    public QueueItemViewModel(DownloadItemViewModel item, IDownloadManager manager, QueueRowViewModel row)
    {
        Item = item;
        _manager = manager;
        _row = row;
        MoveUpCommand = ReactiveCommand.Create(() => _manager.MovePriority(Item, -1));
        MoveDownCommand = ReactiveCommand.Create(() => _manager.MovePriority(Item, +1));
    }

    /// <summary>The queues this download can be moved into, each with a ready-to-bind command.</summary>
    public IEnumerable<QueueMoveTarget> MoveTargets =>
        _row.OtherQueues.Select(q => new QueueMoveTarget
        {
            Name = q.Name,
            Command = ReactiveCommand.Create(() => _manager.MoveToQueue(Item, q.Id))
        }).ToList();

    public bool CanMoveToOtherQueue => MoveTargets.Any();
}

/// <summary>A "move this download to queue X" menu entry.</summary>
public sealed class QueueMoveTarget
{
    public string Name { get; init; }
    public ICommand Command { get; init; }
}
