using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Views;

public partial class DownloadsView : UserControl
{
    private DataGridColumn _queueColumn;
    private DownloadItemViewModel _dragRow;

    public DownloadsView()
    {
        InitializeComponent();
        _queueColumn = Root.Columns.FirstOrDefault(c => c.SortMemberPath == "QueueName");
        Root.AddHandler(DragDrop.DragOverEvent, OnRowDragOver);
        Root.AddHandler(DragDrop.DropEvent, OnRowDrop);
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
    // The dragged row is held in a field (in-process); the DataTransfer only carries a marker so the
    // platform drag session is valid. Drop reads the field and reorders the master list.

    private async void OnGripPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (sender is not Control c || c.DataContext is not DownloadItemViewModel vm)
            return;
        if (!e.GetCurrentPoint(c).Properties.IsLeftButtonPressed)
            return;

        _dragRow = vm;
        e.Handled = true;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText("downloader-row"));
        try
        {
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
        finally
        {
            _dragRow = null;
        }
    }

    private void OnRowDragOver(object sender, DragEventArgs e)
    {
        e.DragEffects = _dragRow != null ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnRowDrop(object sender, DragEventArgs e)
    {
        if (_dragRow is null || DataContext is not DownloadsViewModel pageVm)
            return;
        if (e.Source is not Visual v)
            return;

        var row = v.FindAncestorOfType<DataGridRow>(includeSelf: true);
        if (row?.DataContext is not DownloadItemViewModel target)
            return;

        // Dropped on the lower half of the target row → place after it, else before it.
        var placeAfter = e.GetPosition(row).Y > row.Bounds.Height / 2;
        pageVm.Reorder(_dragRow, target, placeAfter);
        e.Handled = true;
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
            await DialogHelper.ShowDetails(item);
    }
}
