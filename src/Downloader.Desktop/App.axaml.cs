using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Downloader.Desktop;

public partial class App : Application
{
    private bool _canClose; // This flag is used to check if window is allowed to close

    // This is a reference to our MainViewModel which we use to save the list on shutdown.
    // TODO: Use Dependency Injection in this App to inject ViewModels
    private MainViewModel? _mainViewModel;
    private MainWindow? _mainView;
    public new static App? Current => Application.Current as App;

    /// <summary>
    /// Gets the <see cref="IServiceProvider"/> instance to resolve application services.
    /// </summary>
    public IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainView = new MainWindow();
            // IoC services added to DI
            BuildServices();
            _mainViewModel = new MainViewModel();
            _mainView.DataContext = _mainViewModel;
            desktop.MainWindow = _mainView;

            // Listen to the ShutdownRequested-event
            desktop.ShutdownRequested += DesktopOnShutdownRequested;
        }
        else
        {
            throw new PlatformNotSupportedException("This application designed just for Desktop platforms!");
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void BuildServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IFileService>(x => new FileService(_mainView));

        Services = services.BuildServiceProvider();
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

            var fileService = App.Current?.Services?.GetService<IFileService>();
            if (fileService is null) throw new NullReferenceException("Missing File Service instance.");

            await fileService.SaveToFileAsync(itemsToSave);

            // Set _canClose to true and Close this Window again
            _canClose = true;
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
    }
}
