using Downloader.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reactive.Concurrency;
using ReactiveUI;
using System.Threading;
using System.Windows.Input;

namespace Downloader.Desktop.ViewModels;

public class MainViewModel : ViewModelBase
{
    public DownloadsViewModel Downloads { get; private set; }
    // public HeadViewModel Head { get; } = new HeadViewModel();
    public ICommand AddUrlCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand StopAllCommand { get; }
    public ICommand StartAllCommand { get; }
    public ICommand ShowOptionsViewCommand { get; }
    public string DownloadUrl { get; set; }
    
    /// <summary>
    /// Gets or sets a list of Files
    /// </summary>
    private IEnumerable<string>? _selectedFiles;
    private IEnumerable<string>? SelectedFiles
    {
        get => _selectedFiles;
        set => this.RaiseAndSetIfChanged(ref _selectedFiles, value);
    }
    
    public MainViewModel()
    {
        RxApp.MainThreadScheduler.ScheduleAsync(InitMainViewModelAsync);
        AddUrlCommand = ReactiveCommand.CreateFromTask(SelectFilesAsync);
        ClearAllCommand = ReactiveCommand.CreateFromTask(SelectFilesAsync);
        StopAllCommand = ReactiveCommand.CreateFromTask(SelectFilesAsync);
        StartAllCommand = ReactiveCommand.CreateFromTask(SelectFilesAsync);
        ShowOptionsViewCommand = ReactiveCommand.CreateFromTask(SelectFilesAsync);
    }

    private async Task InitMainViewModelAsync(IScheduler scheduler, CancellationToken ct)
    {
        var filesService = App.Current?.Services?.GetService<IFileService>();
        if (filesService is null) throw new NullReferenceException("Missing File Service instance.");

        Downloads = new DownloadsViewModel(filesService);

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

    /// <summary>
    /// A command used to select some files
    /// </summary>
    private async Task SelectFilesAsync()
    {
        SelectedFiles = await this.OpenFileDialogAsync("Hello Avalonia");
    }
}