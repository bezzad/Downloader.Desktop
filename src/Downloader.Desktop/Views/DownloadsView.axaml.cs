using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Views;

public partial class DownloadsView : UserControl
{
    private DataGridColumn _queueColumn;
    private DataGridColumn _typeColumn;
    private DataGridColumn _timeLeftColumn;
    private DownloadItemViewModel _dragRow;
    private DataGridRow _sourceRow;
    private DataGridRow _dropRow;
    private Border _ghost;
    private double _ghostLeft;
    private double _ghostGrabY;
    private bool _dragging;
    private bool _syncing; // stops the checkbox <-> grid-selection sync from looping

    public DownloadsView()
    {
        InitializeComponent();
        _queueColumn = Root.Columns.FirstOrDefault(c => c.SortMemberPath == "QueueName");
        _typeColumn = Root.Columns.FirstOrDefault(c => c.SortMemberPath == "CategoryOrder");
        _timeLeftColumn = Root.Columns.FirstOrDefault(c => c.SortMemberPath == "TimeLeftText");
        DataContextChanged += (_, _) => HookQueueColumn();
        Root.SizeChanged += (_, _) => FitColumns();
        // Tri-state sorting (#12): cancel the DataGrid's built-in 2-state sort and let the VM cycle
        // Asc → Desc → None instead (None = master order, where drag-to-reorder works). The header
        // glyph stays right because the grid renders it from the view's SortDescriptions.
        Root.Sorting += OnColumnSorting;
    }

    private void OnColumnSorting(object sender, DataGridColumnEventArgs e)
    {
        e.Handled = true; // suppress the built-in Asc/Desc-only toggle
        if (DataContext is DownloadsViewModel vm)
            vm.CycleSort(e.Column.SortMemberPath);
    }

    private void HookQueueColumn()
    {
        if (DataContext is not DownloadsViewModel vm || _queueColumn is null)
            return;
        FitColumns();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DownloadsViewModel.ShowQueue))
                FitColumns();
            else if (e.PropertyName == nameof(DownloadsViewModel.SelectAllState))
                SyncGridFromChecks();
        };
    }

    /// <summary>
    /// Shows the Queue column only with 2+ queues, and drops Time left, then Queue, then Type when the
    /// grid is too narrow for every column (the sidebar open on a small window).
    /// </summary>
    private void FitColumns()
    {
        bool showQueue = DataContext is DownloadsViewModel { ShowQueue: true };
        if (_queueColumn is not null)
            _queueColumn.IsVisible = showQueue;

        var optional = new List<DataGridColumn> { _timeLeftColumn, showQueue ? _queueColumn : null, _typeColumn };
        optional.RemoveAll(c => c is null);
        if (Root.Bounds.Width <= 0)
            return;

        double required = Root.Columns.Where(c => c != _queueColumn || showQueue).Sum(WidthOf);
        int hide = ColumnFit.CountToHide(Root.Bounds.Width, required, optional.Select(WidthOf).ToList());
        for (int i = 0; i < optional.Count; i++)
            optional[i].IsVisible = i >= hide;
    }

    private static double WidthOf(DataGridColumn column) =>
        column.Width.IsAbsolute ? column.Width.Value : column.MinWidth;

    // The row checkbox IS the row's selection. The grid gives us click / Ctrl+click / Shift+click;
    // its changes set the checkboxes, and a checkbox (or select-all) change updates the grid.

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing)
            return;
        _syncing = true;
        foreach (var item in e.RemovedItems.OfType<DownloadItemViewModel>())
            item.IsChecked = false;
        foreach (var item in e.AddedItems.OfType<DownloadItemViewModel>())
            item.IsChecked = true;
        _syncing = false;
    }

    /// <summary>Makes the grid's selection equal the checked rows, without touching the others.</summary>
    private void SyncGridFromChecks()
    {
        if (_syncing || Root.ItemsSource is null)
            return;
        _syncing = true;
        var selected = Root.SelectedItems;
        foreach (var item in selected.OfType<DownloadItemViewModel>().Where(i => !i.IsChecked).ToList())
            selected.Remove(item);
        foreach (var item in Root.ItemsSource.OfType<DownloadItemViewModel>())
            if (item.IsChecked && !selected.Contains(item))
                selected.Add(item);
        _syncing = false;
    }

    // --- Drag-to-reorder (grip handle in the first column) ---
    // Manual pointer-driven drag: pressing the grip captures the pointer and lifts a floating "ghost"
    // (a VisualBrush snapshot of the row) onto the DragOverlay canvas. The ghost follows the pointer so
    // the row visibly sticks under the cursor; the row under the pointer is highlighted as the drop
    // target; releasing reorders the master list. (The OS DragDrop session showed no moving visual on X11.)

    private void OnGripPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (sender is not Control c || c.DataContext is not DownloadItemViewModel vm)
            return;
        if (!e.GetCurrentPoint(c).Properties.IsLeftButtonPressed)
            return;

        _sourceRow = c.FindAncestorOfType<DataGridRow>();
        if (_sourceRow is null)
            return;

        // Dragging reorders the MASTER list, which only makes sense unsorted — auto-clear an active
        // sort (visually stable: the rows keep the order they were just shown in) so the drop sticks.
        if (DataContext is DownloadsViewModel pageVm)
            pageVm.ClearSort();

        _dragRow = vm;
        _sourceRow.Classes.Add("dragging");

        var topLeft = _sourceRow.TranslatePoint(new Point(0, 0), DragOverlay) ?? default;
        _ghostLeft = topLeft.X;
        _ghostGrabY = e.GetPosition(_sourceRow).Y;

        _ghost = new Border
        {
            Width = _sourceRow.Bounds.Width,
            Height = _sourceRow.Bounds.Height,
            CornerRadius = new CornerRadius(6),
            Opacity = 0.92,
            Background = this.FindResource("SystemRegionColor") as IBrush ?? Brushes.White,
            BorderBrush = this.FindResource("SystemAccentColor") as IBrush ?? Brushes.SteelBlue,
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 6 18 0 #50000000"),
            IsHitTestVisible = false,
            Child = new Border
            {
                Background = new VisualBrush(_sourceRow)
                {
                    Stretch = Stretch.None,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top
                }
            }
        };
        Canvas.SetLeft(_ghost, _ghostLeft);
        Canvas.SetTop(_ghost, topLeft.Y);
        DragOverlay.Children.Add(_ghost);

        _dragging = true;
        e.Pointer.Capture(c);
        e.Handled = true;
    }

    private void OnGripPointerMoved(object sender, PointerEventArgs e)
    {
        if (!_dragging || _ghost is null)
            return;

        var p = e.GetPosition(DragOverlay);
        Canvas.SetLeft(_ghost, _ghostLeft);
        Canvas.SetTop(_ghost, p.Y - _ghostGrabY);

        UpdateDropTarget(e.GetPosition(Root));
        e.Handled = true;
    }

    private void OnGripPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
            return;
        _dragging = false;
        e.Pointer.Capture(null);

        var row = RowAt(e.GetPosition(Root));
        if (row?.DataContext is DownloadItemViewModel target && DataContext is DownloadsViewModel pageVm
            && _dragRow != null && !ReferenceEquals(target, _dragRow))
        {
            var placeAfter = e.GetPosition(row).Y > row.Bounds.Height / 2;
            pageVm.Reorder(_dragRow, target, placeAfter);
        }
        ClearDrag();
        e.Handled = true;
    }

    private void OnGripCaptureLost(object sender, PointerCaptureLostEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            ClearDrag();
        }
    }

    // Highlight the row currently under the pointer (skip the dragged row itself).
    private void UpdateDropTarget(Point posInRoot)
    {
        var row = RowAt(posInRoot);
        if (ReferenceEquals(row, _dropRow))
            return;
        _dropRow?.Classes.Remove("droptarget");
        _dropRow = null;
        if (row?.DataContext is DownloadItemViewModel t && !ReferenceEquals(t, _dragRow))
        {
            _dropRow = row;
            _dropRow.Classes.Add("droptarget");
        }
    }

    private DataGridRow RowAt(Point posInRoot)
        => (Root.InputHitTest(posInRoot) as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true);

    private void ClearDrag()
    {
        if (_ghost != null)
        {
            DragOverlay.Children.Remove(_ghost);
            _ghost = null;
        }
        _sourceRow?.Classes.Remove("dragging");
        _dropRow?.Classes.Remove("droptarget");
        _sourceRow = null;
        _dropRow = null;
        _dragRow = null;
    }

    private async void OnRowDoubleTapped(object sender, TappedEventArgs e)
    {
        if (e.Source is not Visual v)
            return;

        // Double-clicking a column header (to auto-size/sort) must not open the dialog. (#12)
        if (v.FindAncestorOfType<DataGridColumnHeader>(includeSelf: true) != null)
            return;

        // Resolve the row directly from the clicked element instead of relying on DataGrid.SelectedItem
        // (cells are non-focusable for clean row selection, so we don't depend on cell focus/selection).
        var row = v.FindAncestorOfType<DataGridRow>(includeSelf: true);
        if (row?.DataContext is DownloadItemViewModel item)
            await DialogHelper.ShowDetails(item, (DataContext as DownloadsViewModel)?.Config);
    }
}
