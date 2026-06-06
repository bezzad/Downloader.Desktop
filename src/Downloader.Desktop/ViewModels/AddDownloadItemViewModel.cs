using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

public class AddDownloadItemViewModel : ViewModelBase
{
    private string _filename;
    private readonly Config _config;
    private readonly string _url;

    public ICommand SelectFileStoragePathCommand { get; }
    public ICommand StartDownloadCommand { get; }

    public string StorageFolderPath
    {
        get => _config.DefaultSavePath;
        set
        {
            this.RaisePropertyChanged();
            _config.DefaultSavePath = value;
        }
    }

    public int DownloadChunks
    {
        get => _config.DefaultDownloadChunks;
        set
        {
            this.RaisePropertyChanged();
            _config.DefaultDownloadChunks = value;
        }
    }

    public string Filename
    {
        get => _filename;
        set => this.RaiseAndSetIfChanged(ref _filename, value);
    }

    public AddDownloadItemViewModel(Config config, string url)
    {
        _config = config;
        _url = url;
        _filename = DeriveFileName(url);
        SelectFileStoragePathCommand = ReactiveCommand.CreateFromTask(SelectFileStoragePathAsync);
        StartDownloadCommand = ReactiveCommand.Create(StartDownload);
    }

    private async Task SelectFileStoragePathAsync()
    {
        var path = await DialogHelper.OpenFolderPicker("Select a folder to save the files in");
        StorageFolderPath = path.LocalPath;
    }

    private void StartDownload()
    {
        // Hand a descriptor back to the caller; the DownloadManager builds and starts the engine.
        var item = new DownloadItem
        {
            Url = _url,
            FilePath = Path.Combine(StorageFolderPath ?? string.Empty, Filename ?? string.Empty),
            FileName = Filename,
            Status = DownloadStatus.Created,
            LastTry = DateTime.Now
        };

        View.Close(item);
    }

    /// <summary>Best-effort file name from the URL path, falling back to a timestamped name.</summary>
    private static string DeriveFileName(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var name = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        return $"download_{DateTime.Now:yyyyMMdd_HHmmss}";
    }
}