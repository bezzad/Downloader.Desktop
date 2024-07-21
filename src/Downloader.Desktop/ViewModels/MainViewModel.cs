using Downloader.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using System.Reactive.Concurrency;
using ReactiveUI;
using System.Threading;

namespace Downloader.Desktop.ViewModels;

public class MainViewModel : ViewModelBase
{
    public DownloadsViewModel Downloads { get; } = new DownloadsViewModel();
    public HeadViewModel Head { get; } = new HeadViewModel();

    public MainViewModel()
    {
        RxApp.MainThreadScheduler.ScheduleAsync(InitMainViewModelAsync);
    }

    private async Task InitMainViewModelAsync(IScheduler scheduler, CancellationToken ct)
    {
        var filesService = App.Current?.Services?.GetService<IFileService>();
        if (filesService is null) throw new NullReferenceException("Missing File Service instance.");

        // get the items to load
        var itemsLoaded = await filesService.LoadFromFileAsync();

        if (itemsLoaded is not null)
        {
            foreach (var item in itemsLoaded)
            {
                Downloads.DownloadItems.Add(new(item));
            }
        }
    }
}
