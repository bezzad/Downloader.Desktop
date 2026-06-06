using Downloader.Desktop.Models;
using Downloader.Desktop.Services;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// The Queues management page. Full CRUD + concurrency controls are added with the Queues feature.
/// </summary>
public class QueuesViewModel : ViewModelBase
{
    private readonly Config _config;
    private readonly IDownloadManager _manager;

    public QueuesViewModel()
    {
    }

    public QueuesViewModel(Config config, IDownloadManager manager)
    {
        _config = config;
        _manager = manager;
    }
}
