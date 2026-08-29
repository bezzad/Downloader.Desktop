using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The post-download offer has to be VISIBLE, not merely computable: "Add to Ollama" is useless if the
/// row's button never renders. This drives the real window with a completed row whose plugin offers an
/// action and looks for the button in the visual tree.
/// </summary>
public class PostActionButtonTests
{
    private sealed class StubFileService : IFileService
    {
        public Task<Config> LoadFromFileAsync() => Task.FromResult(Config.New());
        public Task SaveToFileAsync(Config itemToSave) => Task.CompletedTask;
    }

    private static void Pump()
    {
        for (var i = 0; i < 8; i++)
            Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_completed_model_row_shows_its_install_button()
    {
        Localizer.Instance.Load("en");

        var folder = Path.Combine(Path.GetTempPath(), "dldesktop-postaction-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "model.gguf");
        File.WriteAllBytes(file, new byte[16]);

        var plugins = new PluginManager();
        plugins.RegisterPlugin(new OfferingPlugin());
        var manager = new DownloadManager(plugins);
        var config = Config.New();
        var main = new MainViewModel(new StubFileService(), manager);
        manager.Initialize(config);
        config.DefaultQueue.IsRunning = false; // nothing may start; the row is already finished
        Pump();

        // A finished model download, as it looks after a completed run (or after a restart).
        var vm = manager.Add(new DownloadItem
        {
            Url = "fakemodel:demo",
            SaveFolder = folder,
            FileName = "model.gguf",
            ResolverPluginId = "test.offering-plugin",
        }, autoStart: false);
        vm.Status = global::Downloader.DownloadStatus.Completed;
        vm.RaisePostActionChanged();

        var window = new MainWindow { DataContext = main };
        window.Show();
        Pump();

        Assert.True(vm.HasPostAction);
        Assert.Equal("Add to Test Store", vm.PostActionLabel);

        // The button carries the action's label as its tooltip, which is what distinguishes it from the
        // other icon buttons on the row.
        var button = window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => ToolTip.GetTip(b) as string == "Add to Test Store");
        Assert.NotNull(button);
        Assert.True(button!.IsVisible);
        Assert.True(button.IsEffectivelyVisible, "the install button exists but is not actually on screen");

        window.Close();
        try { Directory.Delete(folder, recursive: true); } catch (IOException) { /* best effort */ }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_ordinary_completed_download_shows_no_install_button()
    {
        Localizer.Instance.Load("en");

        var plugins = new PluginManager();
        plugins.RegisterPlugin(new OfferingPlugin());
        var manager = new DownloadManager(plugins);
        var config = Config.New();
        var main = new MainViewModel(new StubFileService(), manager);
        manager.Initialize(config);
        config.DefaultQueue.IsRunning = false;
        Pump();

        var vm = manager.Add(new DownloadItem { Url = "https://10.255.255.1/a.zip", FileName = "a.zip" },
            autoStart: false);
        vm.Status = global::Downloader.DownloadStatus.Completed;
        vm.RaisePostActionChanged();

        var window = new MainWindow { DataContext = main };
        window.Show();
        Pump();

        Assert.False(vm.HasPostAction);
        var button = window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => ToolTip.GetTip(b) as string == "Add to Test Store");
        Assert.True(button is null || !button.IsEffectivelyVisible);

        window.Close();
    }

    private sealed class AddToStoreAction : IPostDownloadAction
    {
        public string Label => "Add to Test Store";
        public bool CanOffer(string sourceUrl, string filePath) =>
            sourceUrl != null && sourceUrl.StartsWith("fakemodel:", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath);
        public Task ExecuteAsync(string sourceUrl, string filePath, IProgress<double> progress, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class OfferingPlugin : IDownloaderPlugin
    {
        public string Id => "test.offering-plugin";
        public string Name => "Offering Plugin";
        public string Version => "1.0.0";
        public string Author => "tester";
        public string Description => "offers an action on a completed model download";
        public void Initialize(IPluginContext context) => context.RegisterPostDownloadAction(new AddToStoreAction());
    }
}
