using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;
using System.Linq;
using System.Threading.Tasks;

namespace Downloader.Desktop;

public partial class App : Application
{
    private bool _canClose; // This flag is used to check if window is allowed to close
    // This is a reference to our MainViewModel which we use to save the list on shutdown.
    // TODO: Use Dependency Injection in this App to inject ViewModels
    private readonly MainViewModel _mainViewModel = new MainViewModel();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = _mainViewModel
            };

            // Listen to the ShutdownRequested-event
            desktop.ShutdownRequested += DesktopOnShutdownRequested;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new DownloadsView
            {
                DataContext = new DownloadsViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();

        // Init the MainViewModel 
        await InitMainViewModelAsync();
    }

    // Optional: Load data from disc
    private async Task InitMainViewModelAsync()
    {
        // get the items to load
        var itemsLoaded = await FileService.LoadFromFileAsync();

        if (itemsLoaded is not null)
        {
            foreach (var item in itemsLoaded)
            {
                _mainViewModel.Downloads.DownloadItems.Add(new(item));
            }
        }
    }

    // We want to save our downloads before we actually shutdown the App.
    // As File I/O is async, we need to wait until file is closed 
    // before we can actually close this window
    private async void DesktopOnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        e.Cancel = !_canClose; // cancel closing event first time

        if (!_canClose)
        {
            // To save the items, we map them to the ToDoItem-Model which is better suited for I/O operations
            var itemsToSave = _mainViewModel.Downloads.DownloadItems.Select(item => item.GetItem());

            await FileService.SaveToFileAsync(itemsToSave);

            // Set _canClose to true and Close this Window again
            _canClose = true;
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
    }
}
