using System;
using System.Collections.ObjectModel;
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
            Queues.Add(new QueueRowViewModel(q, _manager, this));
    }

    private void AddQueue()
    {
        var queue = _manager.AddQueue("New queue");
        Queues.Add(new QueueRowViewModel(queue, _manager, this));
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

    public DownloadQueue Queue { get; }
    public DataGridCollectionView Items { get; }
    public ICommand RemoveCommand { get; }

    public QueueRowViewModel(DownloadQueue queue, IDownloadManager manager, QueuesViewModel parent)
    {
        Queue = queue;
        _manager = manager;
        _parent = parent;
        Items = new DataGridCollectionView(manager.Items)
        {
            Filter = o => o is DownloadItemViewModel vm && vm.GetItem().QueueId == queue.Id
        };
        RemoveCommand = ReactiveCommand.Create(() => _parent.Remove(this));
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
            this.RaisePropertyChanged();
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
