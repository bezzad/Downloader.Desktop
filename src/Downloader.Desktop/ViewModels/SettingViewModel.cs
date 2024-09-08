using ReactiveUI;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;

namespace Downloader.Desktop.ViewModels;

public class SettingViewModel : ViewModelBase
{
    private Config _config;
    
    public SettingViewModel(Config config)
    {
        // Initialize default values 
        _config = config;
            
        // Command to open the file dialog to select the save path
        SelectSavePathCommand = ReactiveCommand.CreateFromTask(SelectSavePath);
        SwitchThemeCommand = ReactiveCommand.Create(SwitchTheme);
    }
    
    public ICommand SelectSavePathCommand { get; }
    public ICommand SwitchThemeCommand { get; }

    public bool IsDarkTheme
    {
        get => _config.ThemeMode == ThemeVariant.Dark;
        set
        {
            this.RaisePropertyChanged();
            _config.ThemeMode = value ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }
    
    public string DefaultSavePath
    {
        get => _config.DefaultSavePath;
        set
        {
            this.RaisePropertyChanged();
            _config.DefaultSavePath = value;
        }
    }

    public int DefaultDownloadSegments
    {
        get => _config.DefaultDownloadChunks;
        set
        {
            this.RaisePropertyChanged();
            _config.DefaultDownloadChunks = value;
        }
    }
    
    // Method to select the save path
    private async Task SelectSavePath()
    {
        var path = await DialogHelper.OpenFolderPicker("Select Default Save Path");
        DefaultSavePath = path.LocalPath;
    }
    
    private void SwitchTheme()
    {
        // Check if there is a valid FluentTheme applied
        if (Application.Current?.Styles[0] is FluentTheme)
        {
            // Set the new theme mode
            Application.Current.RequestedThemeVariant = _config.ThemeMode;
        }
    }
}