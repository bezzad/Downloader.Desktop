using Avalonia.Controls;
using Downloader.Desktop.Services;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Downloader.Desktop.ViewModels;

public class DownloadsViewModel : ViewModelBase
{
    private readonly IDownloadManager _manager;

    /// <summary>Design-time constructor with a couple of sample rows.</summary>
    public DownloadsViewModel()
    {
        RemoveItemCommand = ReactiveCommand.CreateFromTask<DownloadItemViewModel>(RemoveDownloadItem);

        if (Design.IsDesignMode)
        {
            DownloadItems = new ObservableCollection<DownloadItemViewModel>(new[]
            {
                new DownloadItemViewModel { FileName = "Hello" },
                new DownloadItemViewModel { FileName = "Downloader" }
            });
        }
        else
        {
            DownloadItems = new ObservableCollection<DownloadItemViewModel>();
        }
    }

    public DownloadsViewModel(IDownloadManager manager)
    {
        _manager = manager;
        DownloadItems = manager.Items;
        RemoveItemCommand = ReactiveCommand.CreateFromTask<DownloadItemViewModel>(RemoveDownloadItem);
    }

    /// <summary>
    /// The collection of downloads shown in the grid (the manager's master list at runtime).
    /// </summary>
    public ObservableCollection<DownloadItemViewModel> DownloadItems { get; }

    public ICommand RemoveItemCommand { get; }

    private async Task RemoveDownloadItem(DownloadItemViewModel item)
    {
        if (_manager != null)
            await _manager.Remove(item);
        else
            DownloadItems.Remove(item);
    }
}
