using Downloader.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
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
    private string _storageFolderPath;
    private Config _config;

    public DownloadsViewModel Downloads { get; private set; }
    public ICommand AddUrlCommand { get; }
    public ICommand SelectFileStoragePathCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand StopAllCommand { get; }
    public ICommand StartAllCommand { get; }
    public ICommand ShowSettingViewCommand { get; }
    public string DownloadUrl { get; set; }

    public string StorageFolderPath
    {
        get => _storageFolderPath;
        set => this.RaiseAndSetIfChanged(ref _storageFolderPath, value);
    }

    /// <summary>
    /// Gets or sets a list of Files
    /// </summary>
    private readonly IFileService _fileService;
    private IEnumerable<string>? _selectedFiles;

    private IEnumerable<string>? SelectedFiles
    {
        get => _selectedFiles;
        set => this.RaiseAndSetIfChanged(ref _selectedFiles, value);
    }

    public MainViewModel(IFileService fileService)
    {
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        RxApp.MainThreadScheduler.ScheduleAsync(InitMainViewModelAsync);
        SelectFileStoragePathCommand = ReactiveCommand.CreateFromTask(SelectFileStoragePathAsync);
        ShowSettingViewCommand = ReactiveCommand.CreateFromTask(ShowSettingView);
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

    /// <summary>
    /// A command used to select some files
    /// </summary>
    private async Task ShowSettingView()
    {
        // Access the main window to open the modal dialog
        var mainWindow = DialogHelper.GetMainWindow();
        if (mainWindow != null)
        {
            var settingsWindow = new SettingView()
            {
                DataContext = new SettingViewModel()
            };

            // Show as a modal dialog and wait for it to close
            await settingsWindow.ShowDialog(mainWindow);
        }
    }

    private async Task SelectFileStoragePathAsync()
    {
        var path = await DialogHelper.OpenFolderPicker("Select a folder to save the files in");
        StorageFolderPath = path.LocalPath;
    }

    public async Task SaveConfigFile()
    {
        _config.Downloads = Downloads.DownloadItems.Select(item => item.GetItem()).ToList();
        await _fileService.SaveToFileAsync(_config);
    }
}