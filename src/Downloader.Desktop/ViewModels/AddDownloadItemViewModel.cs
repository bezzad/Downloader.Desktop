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
        _filename = url;
        SelectFileStoragePathCommand = ReactiveCommand.CreateFromTask(SelectFileStoragePathAsync);
        StartDownloadCommand = ReactiveCommand.CreateFromTask(StartDownloadAsync);
    }

    private async Task SelectFileStoragePathAsync()
    {
        var path = await DialogHelper.OpenFolderPicker("Select a folder to save the files in");
        StorageFolderPath = path.LocalPath;
    }

    private async Task StartDownloadAsync()
    {
        var download = DownloadBuilder.New()
            .WithUrl(_url)
            .WithFolder(new DirectoryInfo(StorageFolderPath))
            .WithFileName(_filename)
            .Configure(config =>
            {
                config.ParallelDownload = true;
                config.ChunkCount = DownloadChunks;
            }).Build();

        View.Close(download);
        
        await download.StartAsync();
    }
}