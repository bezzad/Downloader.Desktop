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
    private DownloadItemViewModel _dragRow;
    private DataGridRow _sourceRow;
    private DataGridRow _dropRow;
    private Border _ghost;
    private double _ghostLeft;
    private double _ghostGrabY;
    private bool _dragging;

    public DownloadsView()
    {
        InitializeComponent();
        _queueColumn = Root.Columns.FirstOrDefault(c => c.SortMemberPath == "QueueName");
        DataContextChanged += (_, _) => HookQueueColumn();
    }

    private void HookQueueColumn()
    {
        if (DataContext is not DownloadsViewModel vm || _queueColumn is null)
            return;
        _queueColumn.IsVisible = vm.ShowQueue;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DownloadsViewModel.ShowQueue))
                _queueColumn.IsVisible = vm.ShowQueue;
        };
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // A highlighted row counts as "selected" for the toolbar, even when its checkbox is unchecked.
        if (sender is DataGrid grid && DataContext is DownloadsViewModel vm)
            vm.SetGridSelection(grid.SelectedItems);
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
