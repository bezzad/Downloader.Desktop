using Avalonia.Controls;
using Avalonia.Input;
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
        if (sender is DataGrid { SelectedItem: DownloadItemViewModel item })
            await DialogHelper.ShowDetails(item);
    }
}
