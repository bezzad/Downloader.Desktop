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
using System.Reactive.Linq;
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
    private string _categoryFilter;
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
        // Archive/Restore are selection-driven exactly like Start/Pause/Stop: always on the toolbar,
        // greyed out with nothing selected. A button that came and went with the selection count would be
        // the only one in the row behaving that way, and would shift the toolbar under the user's pointer.
        ArchiveSelectedCommand = ReactiveCommand.Create(() => ForEachSelected(i => _manager.Archive(i)), hasSelection);
        UnarchiveSelectedCommand = ReactiveCommand.Create(() => ForEachSelected(i => _manager.Unarchive(i)), hasSelection);
        StopAllCommand = ReactiveCommand.Create(() => _manager.StopAll());
        CreateMenuCommands();
        ClearFiltersCommand = ReactiveCommand.Create(() =>
        {
            if (ClearFiltersRequested is null)
                ClearFilters();
            else
                ClearFiltersRequested();
        });

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
        MoveToQueueTargets.Clear();
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
            MoveToQueueTargets.Add(new QueueActionTarget
            {
                Name = queue.Name,
                Command = ReactiveCommand.Create(() => ForEachSelected(i => _manager.MoveToQueue(i, queue.Id)))
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
        else if (e.PropertyName is nameof(DownloadItemViewModel.Status) or nameof(DownloadItemViewModel.IsArchived))
            MenuState++;
    }

    private void RaiseSelectionChanged()
    {
        this.RaisePropertyChanged(nameof(HasSelection));
        this.RaisePropertyChanged(nameof(SelectAllState));
        this.RaisePropertyChanged(nameof(SelectedCount));
        this.RaisePropertyChanged(nameof(SelectedCountText));
        MenuState++;
    }

    /// <summary>Filterable view bound to the DataGrid.</summary>
    public DataGridCollectionView ItemsView { get; }

    public ICommand RemoveItemCommand { get; }
    public ICommand StartSelectedCommand { get; }
    public ICommand PauseSelectedCommand { get; }
    public ICommand StopSelectedCommand { get; }
    public ICommand RemoveSelectedCommand { get; }

    /// <summary>Files every selected download away.</summary>
    public ICommand ArchiveSelectedCommand { get; }

    /// <summary>Puts every selected archived download back in the working list.</summary>
    public ICommand UnarchiveSelectedCommand { get; }
    public ICommand StopAllCommand { get; }

    /// <summary>The empty state's way out when the filters, not the download list, are why it is empty.</summary>
    public ICommand ClearFiltersCommand { get; }

    /// <summary>Menu entries for "Start queue ▾" — one per queue, each starting that queue's items. Mutated in
    /// place by <see cref="RebuildQueueTargets"/> so the bound MenuFlyout refreshes live on queue add/remove.</summary>
    public ObservableCollection<QueueActionTarget> StartQueueTargets { get; } = new();

    /// <summary>Menu entries for "Stop queue ▾" — one per queue, each stopping all its items.</summary>
    public ObservableCollection<QueueActionTarget> StopQueueTargets { get; } = new();

    /// <summary>
    /// The rows the toolbar acts on: checked rows (the view keeps checkboxes and grid selection equal) that are ALSO visible under
    /// the active filters. The visibility clause matters — a filter can hide a row the user checked
    /// earlier, and removing or stopping a download nobody can see is the kind of surprise a Remove
    /// button must never spring.
    /// </summary>
    private System.Collections.Generic.List<DownloadItemViewModel> SelectedTargets() =>
        _manager == null
            ? new System.Collections.Generic.List<DownloadItemViewModel>()
            : _manager.Items.Where(i => i.IsChecked && PassesView(i)).ToList();

    /// <summary>True while at least one visible row is checked — drives the bulk buttons' enabled state.</summary>
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
            this.RaisePropertyChanged(nameof(IsArchivedView));
            Refresh();
        }
    }

    /// <summary>
    /// Id of the category the list is narrowed to, or null for every category. Independent of the
    /// status filter and the search box: all three are ANDed, and clearing this one leaves the other
    /// two in force.
    /// </summary>
    public string CategoryFilter
    {
        get => _categoryFilter;
        set
        {
            if (_categoryFilter == value)
                return;

            _categoryFilter = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(HasFilter));
            Refresh();
        }
    }

    /// <summary>True while anything is hiding rows. The toolbar states how many downloads are
    /// selected only while this is true — with nothing hidden, the checkboxes say it already.</summary>
    public bool HasFilter =>
        !string.IsNullOrWhiteSpace(_categoryFilter) ||
        _filter != StatusFilter.All ||
        !string.IsNullOrWhiteSpace(_search);

    /// <summary>How many downloads the bulk buttons would act on.</summary>
    public int SelectedCount => SelectedTargets().Count;

    /// <summary>"N selected", for the toolbar.</summary>
    public string SelectedCountText => string.Format(Localizer.Instance["Toolbar_Selected"], SelectedCount);

    /// <summary>
    /// Set by the shell so the empty state's "Clear filters" also resets the footer status pills and
    /// the search box, which live up there. Unset (design time, tests) it clears this page's own.
    /// </summary>
    public Action ClearFiltersRequested { get; set; }

    /// <summary>Drops every filter at once — what the empty state's action does.</summary>
    public void ClearFilters()
    {
        _categoryFilter = null;
        _filter = StatusFilter.All;
        _search = null;
        this.RaisePropertyChanged(nameof(CategoryFilter));
        this.RaisePropertyChanged(nameof(Filter));
        this.RaisePropertyChanged(nameof(Search));
        this.RaisePropertyChanged(nameof(HasFilter));
        Refresh();
    }

    // ---- Tri-state column sorting (#12) ----
    // The DataGrid's built-in header sort only cycles Asc/Desc — there is no "off", so an applied sort
    // permanently fights drag-to-reorder (which rewrites the master order). The VM owns the sort instead:
    // header clicks cycle Asc → Desc → None; None clears SortDescriptions so the view shows master order
    // and dragging works. The header glyph stays correct because the DataGrid renders it from the view's
    // SortDescriptions.

    private string _sortPath;
    private ListSortDirection _sortDirection;

    /// <summary>The SortMemberPath currently sorted by, or null when unsorted (master order).</summary>
    public string SortPath => _sortPath;

    /// <summary>The active sort direction; only meaningful while <see cref="SortPath"/> is non-null.</summary>
    public ListSortDirection SortDirection => _sortDirection;

    /// <summary>Header click: cycle this column Asc → Desc → None; a different column starts at Asc.</summary>
    public void CycleSort(string sortMemberPath)
    {
        if (string.IsNullOrEmpty(sortMemberPath))
            return;

        if (_sortPath != sortMemberPath)
            ApplySort(sortMemberPath, ListSortDirection.Ascending);
        else if (_sortDirection == ListSortDirection.Ascending)
            ApplySort(sortMemberPath, ListSortDirection.Descending);
        else
            ClearSort();
    }

    /// <summary>Back to the unsorted master order (drag-to-reorder operates on this order).</summary>
    public void ClearSort()
    {
        if (_sortPath == null)
            return;
        _sortPath = null;
        ItemsView.SortDescriptions.Clear();
        this.RaisePropertyChanged(nameof(SortPath));
    }

    private void ApplySort(string path, ListSortDirection direction)
    {
        _sortPath = path;
        _sortDirection = direction;
        ItemsView.SortDescriptions.Clear();
        ItemsView.SortDescriptions.Add(DataGridSortDescription.FromPath(path, direction));
        this.RaisePropertyChanged(nameof(SortPath));
        this.RaisePropertyChanged(nameof(SortDirection));
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
        this.RaisePropertyChanged(nameof(HasFilter));
        RaiseSelectionChanged();
    }

    /// <summary>
    /// Everything the list is filtered by EXCEPT the category. The sidebar's per-category counts are
    /// taken over this, so each number states exactly how many rows clicking that category will show.
    /// </summary>
    public bool MatchesExceptCategory(DownloadItemViewModel vm) => vm != null && PassesSearchAndStatus(vm);

    private bool Matches(object o)
    {
        if (o is not DownloadItemViewModel vm)
            return false;

        // The category dimension. Judged on the download's RESOLVED category, so it applies to every
        // row whatever its state — running, queued, paused, failed or completed — not only finished
        // ones. Null means "every category" and leaves the other two filters alone.
        if (!string.IsNullOrWhiteSpace(_categoryFilter) && vm.Category?.Id != _categoryFilter)
            return false;

        return PassesSearchAndStatus(vm);
    }

    private bool PassesSearchAndStatus(DownloadItemViewModel vm)
    {
        // Archiving is a separate axis from a download's state: an archived download is still Failed or
        // Completed, and reads that way again once it is restored. So the archived view shows archived
        // rows WHATEVER their state, and every status filter — All included — rejects them. Search still
        // applies on both sides, which is what makes a large archive usable.
        if (_filter == StatusFilter.Archived)
        {
            if (!vm.IsArchived)
                return false;
        }
        else if (vm.IsArchived)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(_search))
        {
            var s = _search.Trim();
            var inName = vm.FileName?.Contains(s, StringComparison.OrdinalIgnoreCase) == true;
            var inUrl = vm.Url?.Contains(s, StringComparison.OrdinalIgnoreCase) == true;
            if (!inName && !inUrl)
                return false;
        }

        // Buckets are disjoint (see StatusFilter docs): a Paused item lives in Stopped, not Active,
        // so no row ever matches two pills and the pill counts sum to the total.
        return _filter switch
        {
            StatusFilter.Active => vm.Status is DownloadStatus.Running,
            StatusFilter.Queued => vm.Status is DownloadStatus.Created or DownloadStatus.None,
            StatusFilter.Stopped => vm.Status is DownloadStatus.Paused or DownloadStatus.Stopped,
            StatusFilter.Completed => vm.Status == DownloadStatus.Completed,
            // Failed = real failures only. User-Stopped items are NOT failures — they show under Stopped.
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

    // ---- Row right-click menu ----
    // The menu acts on the selection (SelectedTargets), like the toolbar. Each item is enabled when it
    // applies to at least one target; the manager's own guards skip the targets it does not apply to.

    private int _menuState;

    /// <summary>Bumped whenever the selection or a row's state changes, so the menu commands re-check.</summary>
    public int MenuState
    {
        get => _menuState;
        private set => this.RaiseAndSetIfChanged(ref _menuState, value);
    }

    private IObservable<bool> Applies(Func<List<DownloadItemViewModel>, bool> rule) =>
        this.WhenAnyValue(x => x.MenuState).Select(_ => rule(SelectedTargets()));

    private static bool Any(List<DownloadItemViewModel> targets, Func<DownloadStatus, bool> rule) =>
        targets.Any(t => rule(t.Status));

    private void CreateMenuCommands()
    {
        OpenCommand = ReactiveCommand.Create(
            () => ForEachSelected(i => { if (i.IsCompleted) i.OpenFileCommand.Execute(null); }),
            Applies(t => Any(t, s => s == DownloadStatus.Completed)));
        OpenFolderMenuCommand = ReactiveCommand.Create(() =>
        {
            foreach (var row in SelectedTargets().GroupBy(i => i.GetItem().FolderPath).Select(g => g.First()))
                row.OpenFolderCommand.Execute(null);
        }, Applies(t => t.Count > 0));
        ResumeMenuCommand = ReactiveCommand.Create(
            () => ForEachSelected(i =>
            {
                if (i.Status == DownloadStatus.Failed)
                    _manager.Retry(i);
                else
                    _manager.Resume(i);
            }),
            Applies(t => Any(t, s => s is DownloadStatus.Paused or DownloadStatus.Stopped or DownloadStatus.Failed
                or DownloadStatus.Created or DownloadStatus.None)));
        PauseMenuCommand = ReactiveCommand.Create(() => ForEachSelected(i => _manager.Pause(i)),
            Applies(t => Any(t, s => s == DownloadStatus.Running)));
        StopMenuCommand = ReactiveCommand.Create(() => ForEachSelected(i => _manager.Cancel(i)),
            Applies(t => Any(t, s => s is DownloadStatus.Running or DownloadStatus.Paused
                or DownloadStatus.Created or DownloadStatus.None)));
        RestartCommand = ReactiveCommand.Create(() => ForEachSelected(i => _manager.Restart(i)),
            Applies(t => Any(t, s => s is not (DownloadStatus.Running or DownloadStatus.Completed))));
        var hasTargets = Applies(t => t.Count > 0);
        CopyLinkCommand = ReactiveCommand.CreateFromTask(() => CopyText(DownloadCopyFormat.Links(SelectedItems())), hasTargets);
        CopyJsonCommand = ReactiveCommand.CreateFromTask(() => CopyText(DownloadCopyFormat.Json(SelectedItems())), hasTargets);
        CopyCurlCommand = ReactiveCommand.CreateFromTask(() => CopyText(DownloadCopyFormat.Curl(SelectedItems())), hasTargets);
        PostActionMenuCommand = ReactiveCommand.Create(() => MenuRow?.PostActionCommand.Execute(null));
        PropertiesCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (MenuRow is { } row)
                await DialogHelper.ShowDetails(row, Config);
        }, Applies(t => t.Count == 1));
    }

    private IEnumerable<DownloadItem> SelectedItems() => SelectedTargets().Select(i => i.GetItem());

    /// <summary>Clipboard write; a seam so tests can see what would be copied.</summary>
    public Func<string, Task> CopyText { get; set; } = DialogHelper.CopyTextAsync;

    public ICommand OpenCommand { get; private set; }
    public ICommand OpenFolderMenuCommand { get; private set; }

    /// <summary>Continues a paused/stopped/queued download and retries a failed one.</summary>
    public ICommand ResumeMenuCommand { get; private set; }
    public ICommand PauseMenuCommand { get; private set; }
    public ICommand StopMenuCommand { get; private set; }
    public ICommand RestartCommand { get; private set; }
    public ICommand CopyLinkCommand { get; private set; }
    public ICommand CopyJsonCommand { get; private set; }
    public ICommand CopyCurlCommand { get; private set; }
    public ICommand PostActionMenuCommand { get; private set; }

    /// <summary>Opens the Details window; enabled only with exactly one download selected.</summary>
    public ICommand PropertiesCommand { get; private set; }

    /// <summary>"Move to queue ▸" entries, rebuilt in place when queues change. Shown only with 2+ queues.</summary>
    public ObservableCollection<QueueActionTarget> MoveToQueueTargets { get; } = new();

    /// <summary>True while the Archived filter is on: the menu shows Restore instead of Archive.</summary>
    public bool IsArchivedView => _filter == StatusFilter.Archived;

    /// <summary>The single selected row, or null with none or several.</summary>
    private DownloadItemViewModel MenuRow => SelectedTargets() is { Count: 1 } t ? t[0] : null;

    /// <summary>Category entries for the menu: the first selected row's choices, applied to every selected row.</summary>
    public List<CategoryChoice> MenuCategoryChoices =>
        SelectedTargets().FirstOrDefault()?.CategoryChoices
            .Select(c => new CategoryChoice(c.Id, c.Label, id => ForEachSelected(i => i.CategoryId = id)))
            .ToList() ?? new List<CategoryChoice>();

    /// <summary>The plugin action offered for the single selected row (e.g. "Add to Ollama"), or null.</summary>
    public string MenuPostActionLabel => MenuRow?.PostActionLabel;

    public bool HasMenuPostAction => MenuPostActionLabel != null;

    /// <summary>
    /// Called when the menu opens on <paramref name="row"/>. A row outside the selection becomes the only
    /// selected row (other ticks are cleared); a row inside it keeps the selection. Returns true when the
    /// selection changed. The view moves the grid's highlight from the ticks.
    /// </summary>
    public bool PrepareMenuFor(DownloadItemViewModel row)
    {
        var changed = false;
        if (row != null && !SelectedTargets().Contains(row))
        {
            foreach (var item in _manager?.Items ?? Enumerable.Empty<DownloadItemViewModel>())
                item.IsChecked = item == row;
            changed = true;
        }
        this.RaisePropertyChanged(nameof(MenuCategoryChoices));
        this.RaisePropertyChanged(nameof(MenuPostActionLabel));
        this.RaisePropertyChanged(nameof(HasMenuPostAction));
        return changed;
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
