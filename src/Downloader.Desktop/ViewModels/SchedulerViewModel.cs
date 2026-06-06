using Downloader.Desktop.Models;
using Downloader.Desktop.Services;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// The Scheduler management page. Full CRUD + timer engine are added with the Scheduler feature.
/// </summary>
public class SchedulerViewModel : ViewModelBase
{
    private readonly Config _config;
    private readonly IDownloadManager _manager;

    public SchedulerViewModel()
    {
    }

    public SchedulerViewModel(Config config, IDownloadManager manager)
    {
        _config = config;
        _manager = manager;
    }
}
