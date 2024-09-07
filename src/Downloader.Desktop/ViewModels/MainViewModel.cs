using Downloader.Desktop.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Reactive.Concurrency;
using ReactiveUI;
using System.Threading;
using System.Windows.Input;
using Downloader.Desktop.Models;
using Downloader.Desktop.Views;

namespace Downloader.Desktop.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly IFileService _fileService;
    private string _downloadUrl;
    private Config _config;

    public DownloadsViewModel Downloads { get; private set; }
    public ICommand AddDownloadItemCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand StopAllCommand { get; }
    public ICommand StartAllCommand { get; }
    public ICommand ShowSettingViewCommand { get; }

    public string DownloadUrl
    {
        get => _downloadUrl;
        set => this.RaiseAndSetIfChanged(ref _downloadUrl, value);
    }

    public MainViewModel(IFileService fileService)
    {
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        RxApp.MainThreadScheduler.ScheduleAsync(InitMainViewModelAsync);
        ShowSettingViewCommand = ReactiveCommand.CreateFromTask(ShowSettingView);
        AddDownloadItemCommand = ReactiveCommand.CreateFromTask(AddDownloadItem);
    }

    private async Task InitMainViewModelAsync(IScheduler scheduler, CancellationToken ct)
    {
        Downloads = new DownloadsViewModel();

        // get the items to load
        _config = await _fileService.LoadFromFileAsync();
        foreach (var item in _config.Downloads)
        {
            Downloads.DownloadItems.Add(new(item));
        }
    }

    private async Task AddDownloadItem()
    {
        var result = await DialogHelper.ShowDialog<AddDownloadItemView, AddDownloadItemViewModel, IDownload>(
            new AddDownloadItemView(), new AddDownloadItemViewModel(_config, _downloadUrl));

        if (result != null)
        {
            Downloads.DownloadItems.Add(new DownloadItemViewModel(new DownloadItem()
            {
                FileName = result.Filename,
                Size = result.TotalFileSize,
                Downloaded = result.DownloadedFileSize,
                Url = result.Url,
                FilePath = result.Filename,
                Status = result.Status,
                LastTry = DateTime.Now
            }));
        }
    }

    private async Task ShowSettingView()
    {
        await DialogHelper.ShowDialog<SettingView, SettingViewModel, bool>(new SettingView(),
            new SettingViewModel(_config));
        await SaveConfigFile();
    }

    public async Task SaveConfigFile()
    {
        var downloadItems = Downloads.DownloadItems?.Select(item => item.GetItem())?.ToList();
        if (downloadItems is not null)
            _config.Downloads = downloadItems;
        await _fileService.SaveToFileAsync(_config);
    }
}