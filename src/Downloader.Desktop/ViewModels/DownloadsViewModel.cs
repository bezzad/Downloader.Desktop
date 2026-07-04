using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls;
using Downloader.Desktop.Models;
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

    /// <summary>The shared config instance, for callers that need it (e.g. the Details dialog's persisted size).</summary>
    public Models.Config Config => _manager?.Config;

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

        // The "Start/Stop queue" toolbar menus are bound to ObservableCollections; rebuild them in place when
        // a queue is added/removed so a new queue shows up without restarting the app. (A MenuFlyout caches
        // its ItemsSource and does NOT re-read a computed property on PropertyChanged, so in-place
        // CollectionChanged is what actually refreshes the realized menu items.)
        RebuildQueueTargets();
        manager.QueuesChanged += OnQueuesChanged;
    }

    private void OnQueuesChanged()
    {
        void Apply()
        {
            RebuildQueueTargets();
            this.RaisePropertyChanged(nameof(ShowQueue));
        }
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(Apply);
    }

    private void RebuildQueueTargets()
    {
        StartQueueTargets.Clear();
        StopQueueTargets.Clear();
        foreach (var q in (IEnumerable<DownloadQueue>)_manager?.Queues ?? Enumerable.Empty<DownloadQueue>())
        {
            var queue = q;
            StartQueueTargets.Add(new QueueActionTarget
            {
                Name = queue.Name,
                Command = ReactiveCommand.Create(() => _manager.StartQueue(queue))
            });
            StopQueueTargets.Add(new QueueActionTarget
            {
                Name = queue.Name,
                Command = ReactiveCommand.Create(() => _manager.StopQueue(queue))
            });
        }
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

    /// <summary>Menu entries for "Start queue ▾" — one per queue, each starting that queue's items. Mutated in
    /// place by <see cref="RebuildQueueTargets"/> so the bound MenuFlyout refreshes live on queue add/remove.</summary>
    public ObservableCollection<QueueActionTarget> StartQueueTargets { get; } = new();

    /// <summary>Menu entries for "Stop queue ▾" — one per queue, each stopping all its items.</summary>
    public ObservableCollection<QueueActionTarget> StopQueueTargets { get; } = new();

    // Rows highlighted in the DataGrid (independent of the checkboxes). Pushed in from the view's
    // SelectionChanged so that simply selecting a row also counts as "selected" for the toolbar.
    private readonly System.Collections.Generic.List<DownloadItemViewModel> _gridSelection = new();

    /// <summary>Called by the view when the DataGrid's highlighted rows change.</summary>
    public void SetGridSelection(System.Collections.IList items)
    {
        _gridSelection.Clear();
        if (items != null)
            foreach (var it in items)
                if (it is DownloadItemViewModel vm)
                    _gridSelection.Add(vm);
        RaiseSelectionChanged();
    }

    /// <summary>The rows the toolbar acts on: checked rows plus any DataGrid-highlighted rows.</summary>
    private System.Collections.Generic.List<DownloadItemViewModel> SelectedTargets() =>
        _manager == null
            ? new System.Collections.Generic.List<DownloadItemViewModel>()
            : _manager.Items.Where(i => i.IsChecked || _gridSelection.Contains(i)).ToList();

    /// <summary>True while at least one row is checked OR highlighted — drives the bulk buttons' enabled state.</summary>
    public bool HasSelection => SelectedTargets().Count > 0;

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

    /// <summary>Show the per-row Queue name column only when more than one queue exists.</summary>
    public bool ShowQueue => _manager?.Queues != null && _manager.Queues.Count > 1;

    /// <summary>Drag-reorder forwarder used by the grid's drag handle (code-behind).</summary>
    public void Reorder(DownloadItemViewModel vm, DownloadItemViewModel target, bool placeAfter)
        => _manager?.ReorderTo(vm, target, placeAfter);

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
        this.RaisePropertyChanged(nameof(ShowQueue));
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
            StatusFilter.Active => vm.Status is DownloadStatus.Running or DownloadStatus.Paused,
            StatusFilter.Queued => vm.Status is DownloadStatus.Created or DownloadStatus.None,
            StatusFilter.Completed => vm.Status == DownloadStatus.Completed,
            // Failed = real failures only. User-Stopped items are NOT failures — they show under All.
            StatusFilter.Failed => vm.Status is DownloadStatus.Failed,
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
            foreach (var item in SelectedTargets())
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
            foreach (var item in SelectedTargets())
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
