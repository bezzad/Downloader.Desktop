using System;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls;
using Downloader.Desktop.Services;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// The downloads table page. Wraps the manager's master collection in a filterable
/// <see cref="DataGridCollectionView"/> driven by the selected status filter + search text.
/// </summary>
public class DownloadsViewModel : ViewModelBase
{
    private readonly IDownloadManager _manager;
    private StatusFilter _filter = StatusFilter.All;
    private string _search;

    /// <summary>Design-time constructor with sample rows.</summary>
    public DownloadsViewModel()
    {
        var sample = new ObservableCollection<DownloadItemViewModel>(new[]
        {
            new DownloadItemViewModel { FileName = "ubuntu-24.04.iso" },
            new DownloadItemViewModel { FileName = "podcast-ep12.mp3" }
        });
        ItemsView = new DataGridCollectionView(sample);
        RemoveItemCommand = ReactiveCommand.CreateFromTask<DownloadItemViewModel>(RemoveDownloadItem);
    }

    public DownloadsViewModel(IDownloadManager manager)
    {
        _manager = manager;
        // NOTE: no GroupDescriptions here on purpose. Avalonia's DataGrid does not row-virtualize
        // grouped data, which made scrolling/UI janky once there were more than ~10 rows (#3). Keeping
        // the view flat restores virtualization; the batch "Group" field is retained on the model.
        ItemsView = new DataGridCollectionView(manager.Items) { Filter = Matches };
        RemoveItemCommand = ReactiveCommand.CreateFromTask<DownloadItemViewModel>(RemoveDownloadItem);

        // Per-row Start/Pause/Stop/Remove act on the *selected* rows, so they're enabled only while at
        // least one row is checked. Stop-all / queue actions are selection-independent (see below).
        var hasSelection = this.WhenAnyValue(x => x.HasSelection);
        StartSelectedCommand = ReactiveCommand.Create(() => ForEachSelected(i => _manager.Resume(i)), hasSelection);
        PauseSelectedCommand = ReactiveCommand.Create(() => ForEachSelected(i => _manager.Pause(i)), hasSelection);
        StopSelectedCommand = ReactiveCommand.Create(() => ForEachSelected(i => _manager.Cancel(i)), hasSelection);
        RemoveSelectedCommand = ReactiveCommand.Create(RemoveSelected, hasSelection);
        StopAllCommand = ReactiveCommand.Create(() => _manager.StopAll());

        // Track row check-state so HasSelection / SelectAllState stay in sync with the row checkboxes.
        foreach (var item in manager.Items)
            item.PropertyChanged += OnItemPropertyChanged;
        manager.Items.CollectionChanged += OnItemsCollectionChanged;
    }

    private void OnItemsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (DownloadItemViewModel it in e.OldItems)
                it.PropertyChanged -= OnItemPropertyChanged;
        if (e.NewItems != null)
            foreach (DownloadItemViewModel it in e.NewItems)
                it.PropertyChanged += OnItemPropertyChanged;
        RaiseSelectionChanged();
    }

    private void OnItemPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DownloadItemViewModel.IsChecked))
            RaiseSelectionChanged();
    }

    private void RaiseSelectionChanged()
    {
        this.RaisePropertyChanged(nameof(HasSelection));
        this.RaisePropertyChanged(nameof(SelectAllState));
    }

    /// <summary>Filterable view bound to the DataGrid.</summary>
    public DataGridCollectionView ItemsView { get; }

    public ICommand RemoveItemCommand { get; }
    public ICommand StartSelectedCommand { get; }
    public ICommand PauseSelectedCommand { get; }
    public ICommand StopSelectedCommand { get; }
    public ICommand RemoveSelectedCommand { get; }
    public ICommand StopAllCommand { get; }

    /// <summary>Menu entries for "Start queue ▾" — one per queue, each starting that queue's items.</summary>
    public System.Collections.Generic.IEnumerable<QueueActionTarget> StartQueueTargets =>
        _manager?.Queues.Select(q => new QueueActionTarget
        {
            Name = q.Name,
            Command = ReactiveCommand.Create(() => _manager.StartQueue(q))
        }).ToList() ?? Enumerable.Empty<QueueActionTarget>();

    /// <summary>Menu entries for "Stop queue ▾" — one per queue, each pausing that queue.</summary>
    public System.Collections.Generic.IEnumerable<QueueActionTarget> StopQueueTargets =>
        _manager?.Queues.Select(q => new QueueActionTarget
        {
            Name = q.Name,
            Command = ReactiveCommand.Create(() => _manager.PauseQueue(q))
        }).ToList() ?? Enumerable.Empty<QueueActionTarget>();

    /// <summary>True while at least one row is checked — drives the per-row bulk buttons' enabled state.</summary>
    public bool HasSelection => _manager?.Items.Any(i => i.IsChecked) == true;

    /// <summary>
    /// Grid-header tri-state checkbox: true = all visible rows checked, false = none, null = some.
    /// Setting it checks/unchecks every visible (filtered) row.
    /// </summary>
    public bool? SelectAllState
    {
        get
        {
            if (_manager == null)
                return false;
            var visible = _manager.Items.Where(PassesView).ToList();
            if (visible.Count == 0)
                return false;
            var checkedCount = visible.Count(i => i.IsChecked);
            return checkedCount == 0 ? false : checkedCount == visible.Count ? true : (bool?)null;
        }
        set
        {
            if (_manager == null)
                return;
            var check = value == true; // null/false → clear, true → select all
            foreach (var item in _manager.Items)
                if (PassesView(item))
                    item.IsChecked = check;
            RaiseSelectionChanged();
        }
    }

    public bool IsEmpty => ItemsView is null || ItemsView.Count == 0;

    public StatusFilter Filter
    {
        get => _filter;
        set
        {
            _filter = value;
            Refresh();
        }
    }

    public string Search
    {
        get => _search;
        set
        {
            _search = value;
            Refresh();
        }
    }

    /// <summary>Re-evaluates the filter (call when items or their statuses change).</summary>
    public void Refresh()
    {
        ItemsView?.Refresh();
        this.RaisePropertyChanged(nameof(IsEmpty));
        RaiseSelectionChanged();
    }

    private bool Matches(object o)
    {
        if (o is not DownloadItemViewModel vm)
            return false;

        if (!string.IsNullOrWhiteSpace(_search))
        {
            var s = _search.Trim();
            var inName = vm.FileName?.Contains(s, StringComparison.OrdinalIgnoreCase) == true;
            var inUrl = vm.Url?.Contains(s, StringComparison.OrdinalIgnoreCase) == true;
            if (!inName && !inUrl)
                return false;
        }

        return _filter switch
        {
            StatusFilter.Active => vm.Status is DownloadStatus.Running or DownloadStatus.Paused or DownloadStatus.Created,
            StatusFilter.Completed => vm.Status == DownloadStatus.Completed,
            StatusFilter.Failed => vm.Status is DownloadStatus.Failed or DownloadStatus.Stopped,
            _ => true
        };
    }

    private async Task RemoveDownloadItem(DownloadItemViewModel item)
    {
        if (_manager != null)
            await _manager.Remove(item);
        Refresh();
    }

    private bool PassesView(DownloadItemViewModel item) => Matches(item);

    private void ForEachSelected(Action<DownloadItemViewModel> action)
    {
        if (_manager == null)
            return;
        // Batch so a large selection re-filters the grid once, not once per row (avoids UI freeze).
        _manager.Batch(() =>
        {
            foreach (var item in _manager.Items.Where(i => i.IsChecked).ToList())
                action(item);
        });
        Refresh();
    }

    private void RemoveSelected()
    {
        if (_manager == null)
            return;
        _manager.Batch(() =>
        {
            foreach (var item in _manager.Items.Where(i => i.IsChecked).ToList())
                _ = _manager.Remove(item);
        });
        Refresh();
    }
}

/// <summary>A "start/stop queue X" menu entry: a queue name plus the ready-to-bind action.</summary>
public sealed class QueueActionTarget
{
    public string Name { get; init; }
    public ICommand Command { get; init; }
}
