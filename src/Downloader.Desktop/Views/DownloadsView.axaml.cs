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
    public DownloadsView()
    {
        InitializeComponent();
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
