using ReactiveUI;
using System.Threading.Tasks;
using System.Windows.Input;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;

namespace Downloader.Desktop.ViewModels;

public class SettingViewModel : ViewModelBase
{
    private string _defaultSavePath;
    private int _defaultDownloadSegments;
    private Config _config;


    // Properties for the save path and number of segments
    public string DefaultSavePath
    {
        get => _defaultSavePath;
        set
        {
            this.RaiseAndSetIfChanged(ref _defaultSavePath, value);
            _config.DefaultSavePath = value;
        }
    }

    public int DefaultDownloadSegments
    {
        get => _defaultDownloadSegments;
        set
        {
            this.RaiseAndSetIfChanged(ref _defaultDownloadSegments, value);
            //_config.DefaultDownloadChunks = value;
        }
    }

    // Command for selecting the save path
    public ICommand SelectSavePathCommand { get; }

    public SettingViewModel(Config config)
    {
        // Initialize default values 
        _config = config;
        DefaultSavePath = config.DefaultSavePath;
        DefaultDownloadSegments = config.DefaultDownloadChunks; 
            
        // Command to open the file dialog to select the save path
        SelectSavePathCommand = ReactiveCommand.CreateFromTask(SelectSavePath);
    }

    // Method to select the save path
    private async Task SelectSavePath()
    {
        var path = await DialogHelper.OpenFolderPicker("Select Default Save Path");
        DefaultSavePath = path.LocalPath;
    }
}