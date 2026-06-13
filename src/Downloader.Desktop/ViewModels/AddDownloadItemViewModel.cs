using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// Collects a URL + destination folder (and an optional file name) for a new download, then returns a
/// <see cref="DownloadItem"/> descriptor. When the name is left blank the engine resolves the real name
/// from the URL / Content-Disposition headers.
/// </summary>
public class AddDownloadItemViewModel : ViewModelBase
{
    private readonly Config _config;
    private readonly string _url;
    private string _fileName;
    private string _storageFolderPath;
    private DownloadQueue _selectedQueue;

    public AddDownloadItemViewModel(Config config, string url)
    {
        _config = config;
        _url = url;
        _storageFolderPath = config?.Settings?.DefaultSavePath;
        _fileName = string.Empty;
        _selectedQueue = config?.DefaultQueue;

        SelectFileStoragePathCommand = ReactiveCommand.CreateFromTask(SelectFileStoragePathAsync);
        StartDownloadCommand = ReactiveCommand.Create(StartDownload);
    }

    public List<DownloadQueue> Queues => _config?.Queues;

    public DownloadQueue SelectedQueue
    {
        get => _selectedQueue;
        set => this.RaiseAndSetIfChanged(ref _selectedQueue, value);
    }

    /// <summary>Hide the queue picker when there is only the default queue.</summary>
    public bool ShowQueuePicker => (_config?.Queues?.Count ?? 0) > 1;

    public ICommand SelectFileStoragePathCommand { get; }
    public ICommand StartDownloadCommand { get; }

    public string Url => _url;

    public string StorageFolderPath
    {
        get => _storageFolderPath;
        set => this.RaiseAndSetIfChanged(ref _storageFolderPath, value);
    }

    /// <summary>Optional. Blank means "auto-detect from the link".</summary>
    public string Filename
    {
        get => _fileName;
        set => this.RaiseAndSetIfChanged(ref _fileName, value);
    }

    private async Task SelectFileStoragePathAsync()
    {
        var path = await DialogHelper.OpenFolderPicker("Select a folder to save the file in");
        if (path != null)
            StorageFolderPath = path.LocalPath;
    }

    private void StartDownload()
    {
        var item = new DownloadItem
        {
            Url = _url,
            SaveFolder = string.IsNullOrWhiteSpace(StorageFolderPath)
                ? _config?.Settings?.DefaultSavePath
                : StorageFolderPath,
            FileName = string.IsNullOrWhiteSpace(Filename) ? null : Filename.Trim(),
            QueueId = SelectedQueue?.Id ?? _config?.DefaultQueue?.Id,
            Status = DownloadStatus.Created,
            LastTry = DateTime.Now
        };

        View.Close(item);
    }
}
