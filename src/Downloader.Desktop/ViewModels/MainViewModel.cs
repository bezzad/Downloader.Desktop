using Downloader.Desktop.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Reactive.Concurrency;
using ReactiveUI;
using System.Threading;
using System.Windows.Input;
using Avalonia;
using Downloader.Desktop.Models;
using Downloader.Desktop.Views;

namespace Downloader.Desktop.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly IFileService _fileService;
    private readonly IDownloadManager _downloadManager;
    private string _downloadUrl;
    private Config _config;

    public MainViewModel(IFileService fileService, IDownloadManager downloadManager)
    {
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _downloadManager = downloadManager ?? throw new ArgumentNullException(nameof(downloadManager));
        RxApp.MainThreadScheduler.ScheduleAsync(InitMainViewModelAsync);
        ShowSettingViewCommand = ReactiveCommand.CreateFromTask(ShowSettingView);
        AddDownloadItemCommand = ReactiveCommand.CreateFromTask(AddDownloadItem);
        ClearAllCommand = ReactiveCommand.Create(ClearAllStoppedItems);
        StopAllCommand = ReactiveCommand.Create(StopAll);
        StartAllCommand = ReactiveCommand.Create(StartAll);
    }
    
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

    private async Task InitMainViewModelAsync(IScheduler scheduler, CancellationToken ct)
    {
        // get the items to load
        _config = await _fileService.LoadFromFileAsync();
        Application.Current!.RequestedThemeVariant = _config.ThemeMode;

        _downloadManager.Initialize(_config);
        Downloads = new DownloadsViewModel(_downloadManager);
        this.RaisePropertyChanged(nameof(Downloads));
    }

    private async Task AddDownloadItem()
    {
        var result = await DialogHelper.ShowDialog<AddDownloadItemView, AddDownloadItemViewModel, DownloadItem>(
            new AddDownloadItemView(), new AddDownloadItemViewModel(_config, _downloadUrl));

        if (result != null)
        {
            _downloadManager.Add(result, autoStart: true);
            DownloadUrl = string.Empty;
        }
    }

    private void StopAll() => _downloadManager.StopAll();

    private void StartAll() => _downloadManager.StartAll();

    private void ClearAllStoppedItems() => _downloadManager.ClearCompleted();

    private async Task ShowSettingView()
    {
        await DialogHelper.ShowDialog<SettingView, SettingViewModel, bool>(
            new SettingView(), new SettingViewModel(_config));
        
        await SaveConfigFile().ConfigureAwait(false);
    }

    public async Task SaveConfigFile()
    {
        var downloadItems = Downloads.DownloadItems?.Select(item => item.GetItem())?.ToList();
        if (downloadItems is not null)
            _config.Downloads = downloadItems;
        await _fileService.SaveToFileAsync(_config);
    }
}