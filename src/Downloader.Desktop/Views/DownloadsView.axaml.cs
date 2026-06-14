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
        // Only open details for a double-click on an actual row. Double-clicking a column header
        // (to auto-size/sort) must not open the dialog. (#12)
        if (e.Source is Visual v && v.FindAncestorOfType<DataGridColumnHeader>(includeSelf: true) != null)
            return;

        if (sender is DataGrid { SelectedItem: DownloadItemViewModel item })
            await DialogHelper.ShowDetails(item);
    }
}
