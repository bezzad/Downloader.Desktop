using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace Downloader.Desktop;

public partial class App : Application
{
    private bool _canClose = false; // This flag is used to check if window is allowed to close
    private IServiceProvider _services;
    private MainViewModel _mainViewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        InstallDispatcherSafetyNet();
    }

    /// <summary>
    /// Keep the UI thread alive when something posted to it fails.
    /// <para>
    /// Every engine event, timer tick and <c>async void</c> continuation lands on the dispatcher, and an
    /// exception escaping any of them ends the thread that runs it — the window stops responding, with the
    /// download list frozen mid-progress and no message. The same failure in the headless test suite takes
    /// the shared dispatcher down, after which no test can run OR time out (the timeout needs that thread),
    /// which is what a CI hang blamed on an arbitrary unrelated test turned out to be.
    /// </para>
    /// <para>
    /// A failed UI update is never worth the whole thread, so it is logged and marked handled. Individual
    /// paths still guard themselves where they can say something better; this is the floor, not the plan.
    /// </para>
    /// </summary>
    private static void InstallDispatcherSafetyNet()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            AppLog.Error("Unhandled exception on the UI thread", e.Exception);
            e.Handled = true;
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Register all the services needed for the application to run
        ConfigureServices();

        // Check if running in design mode
        if (Design.IsDesignMode)
        {
            // Skip platform checks or other logic not needed in design mode
            return;
        }

        // Platform-specific check (e.g., for desktop platforms)
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("This application is designed just for Desktop platforms!");
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Resolve the MainWindow and set its DataContext via DI
            var vm = _services?.GetRequiredService<MainViewModel>();
            _mainViewModel = vm;
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };
            vm!.View = desktop.MainWindow;

            // Listen to the ShutdownRequested-event
            desktop.ShutdownRequested += DesktopOnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<IDownloadManager, DownloadManager>();
        services.AddSingleton<PluginManager>();
        services.AddTransient<MainViewModel>();
        _services = services.BuildServiceProvider();
    }

    // We want to save our downloads before we actually shutdown the App.
    // As File I/O is async, we need to wait until file is closed
    // before we can actually close this window
    private void DesktopOnShutdownRequested(object sender, ShutdownRequestedEventArgs e)
    {
        e.Cancel = !_canClose; // cancel closing event first time

        if (!_canClose)
        {
            // Hide every window immediately so the close FEELS instant — the previous code blocked the
            // UI thread with a synchronous .Wait(5s) on the save, which is what made the close/red button
            // hang for a few seconds. The process now lingers invisibly just long enough to flush the
            // save, then really exits below.
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime hideDesktop)
                foreach (var w in hideDesktop.Windows)
                    w.Hide();

            _ = FinishShutdownAsync();
        }
    }

    private async Task FinishShutdownAsync()
    {
        // Persist the download list + settings before actually shutting down — bounded so a stuck save
        // can never leave the process lingering forever, but no longer blocks the UI thread while waiting.
        try
        {
            if (_mainViewModel is not null)
                await _mainViewModel.SaveConfigFile().ConfigureAwait(false);
        }
        catch
        {
            // Saving is best-effort; never block shutdown on it.
        }

        // If an update was downloaded, stage the swap now: it waits for this process to exit, then
        // extracts over the app folder and relaunches. Runs whether the user clicked "Update
        // Downloader" or just closed the app.
        UpdateFlow.ApplyPendingOnExit();

        // Set _canClose to true and Close this Window again
        _canClose = true;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        });
    }
}