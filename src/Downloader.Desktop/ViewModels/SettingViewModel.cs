using System;
using ReactiveUI;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Shapes;
using Avalonia.Platform.Storage;
using Downloader.Desktop.Services;

namespace Downloader.Desktop.ViewModels;

public class SettingViewModel : ViewModelBase
{
    private string _defaultSavePath;
    private int _defaultDownloadSegments;

    // Properties for the save path and number of segments
    public string DefaultSavePath
    {
        get => _defaultSavePath;
        set => this.RaiseAndSetIfChanged(ref _defaultSavePath, value);
    }

    public int DefaultDownloadSegments
    {
        get => _defaultDownloadSegments;
        set => this.RaiseAndSetIfChanged(ref _defaultDownloadSegments, value);
    }

    // Command for selecting the save path
    public ICommand SelectSavePathCommand { get; }

    public SettingViewModel()
    {
        // Initialize default values (can be loaded from configuration or user preferences)
        DefaultSavePath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        DefaultDownloadSegments = 4; // Example default number of segments

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