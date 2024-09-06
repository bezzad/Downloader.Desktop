using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Downloader.Desktop;

public partial class App : Application
{
    private bool _canClose; // This flag is used to check if window is allowed to close
    private IServiceProvider? _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Register all the services needed for the application to run
        ConfigureServices();
        var vm = _services?.GetRequiredService<MainViewModel>();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Resolve the MainWindow and set its DataContext via DI
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };

            // Listen to the ShutdownRequested-event
            desktop.ShutdownRequested += DesktopOnShutdownRequested;
        }
        else
        {
            throw new PlatformNotSupportedException("This application designed just for Desktop platforms!");
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IFileService, FileService>();
        services.AddTransient<MainViewModel>();
        _services = services.BuildServiceProvider();
    }

    // We want to save our downloads before we actually shutdown the App.
    // As File I/O is async, we need to wait until file is closed 
    // before we can actually close this window
    private async void DesktopOnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        e.Cancel = !_canClose; // cancel closing event first time

        if (!_canClose)
        {
            _services?.GetRequiredService<MainViewModel>().SaveConfigFile();

            // Set _canClose to true and Close this Window again
            _canClose = true;
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
    }
}