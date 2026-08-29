using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The application bootstrap: register the services, resolve the shell view model out of them, build
/// the main window and hand it the view model.
///
/// This runs exactly once per launch and every failure in it is a crash on startup with no UI to
/// report it — a service that cannot be resolved, a window that cannot be constructed. The headless
/// runtime has no desktop lifetime, so the bootstrap's whole body used to be skipped by its own
/// "if this is a desktop app" guard.
/// </summary>
public class AppBootstrapTests : IDisposable
{
    private readonly string _configPath =
        Path.Combine(Path.GetTempPath(), "dldesktop-boot-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly string _pluginsRoot =
        Path.Combine(Path.GetTempPath(), "dldesktop-boot-plugins-" + Guid.NewGuid().ToString("N"));

    public AppBootstrapTests()
    {
        // The bootstrap resolves the REAL FileService and PluginManager — point both away from the
        // developer's own config and plugins folder.
        Directory.CreateDirectory(_pluginsRoot);
        FileService.ConfigFileOverride = _configPath;
        PluginManager.PluginsRootOverride = _pluginsRoot;
    }

    public void Dispose()
    {
        FileService.ConfigFileOverride = null;
        PluginManager.PluginsRootOverride = null;
        try { File.Delete(_configPath); } catch { /* best-effort */ }
        try { Directory.Delete(_pluginsRoot, recursive: true); } catch { /* best-effort */ }
    }

    /// <summary>
    /// Runs the real bootstrap against a real desktop lifetime. The reflection is into OUR OWN
    /// override (Avalonia declares it protected and the headless session already called it once, with
    /// no lifetime to act on), so a rename fails loudly here rather than quietly skipping the check.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void The_bootstrap_resolves_the_shell_and_gives_it_a_window()
    {
        using var scope = new DesktopLifetimeScope();
        var app = Application.Current!;
        var lifetime = (IClassicDesktopStyleApplicationLifetime)app.ApplicationLifetime!;

        var bootstrap = app.GetType().GetMethod("OnFrameworkInitializationCompleted",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(bootstrap);

        bootstrap!.Invoke(app, null);

        // A main window, built from the real MainWindow view, with the shell view model behind it.
        Assert.IsType<MainWindow>(lifetime.MainWindow);
        var vm = Assert.IsType<MainViewModel>(lifetime.MainWindow!.DataContext);
        Assert.Same(lifetime.MainWindow, vm.View);

        lifetime.MainWindow.Close();
    }

    /// <summary>
    /// The container has to hold everything the shell asks for. A missing registration only shows up
    /// as a startup crash, so resolving each one here is the cheapest possible guard.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void Every_service_the_shell_depends_on_can_be_resolved()
    {
        using var scope = new DesktopLifetimeScope();
        var app = Application.Current!;

        app.GetType()
            .GetMethod("ConfigureServices", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, null);

        var services = (IServiceProvider)app.GetType()
            .GetField("_services", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(app)!;

        Assert.NotNull(services.GetService(typeof(IFileService)));
        Assert.NotNull(services.GetService(typeof(IDownloadManager)));
        Assert.NotNull(services.GetService(typeof(PluginManager)));

        // The download manager is a singleton (one master list) while the shell is transient.
        Assert.Same(services.GetService(typeof(IDownloadManager)), services.GetService(typeof(IDownloadManager)));
        Assert.NotSame(services.GetService(typeof(MainViewModel)), services.GetService(typeof(MainViewModel)));
    }
}
