using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The whole "open containing folder" chain, from the rendered button to the launcher.
///
/// <para>The launcher itself is covered in <c>Unit/RevealInFolderTests</c>; what is NOT covered there is
/// whether the button in the UI actually reaches it. A command that never fires — an unbound button, a
/// binding that silently fails to resolve — looks EXACTLY like a launcher that does nothing, and that
/// ambiguity is what made this bug so hard to pin down.</para>
/// </summary>
public class OpenFolderButtonTests : IDisposable
{
    private readonly List<string> _opened = new();
    private readonly List<(string File, string[] Args)> _ran = new();

    public OpenFolderButtonTests()
    {
        Localizer.Instance.Load("en");
        ShellLauncher.OpenOverride = target => { _opened.Add(target); return true; };
        ShellLauncher.RunOverride = (file, args) => { _ran.Add((file, args)); return true; };
    }

    public void Dispose()
    {
        // Process-wide seams.
        ShellLauncher.OpenOverride = null;
        ShellLauncher.RunOverride = null;
    }

    /// <summary>A download row whose file is genuinely on disk, so the reveal branch is the one taken.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void The_row_command_reaches_the_launcher_with_the_real_path()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dl-openbtn-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "sample.zip");
        File.WriteAllText(file, "x");
        try
        {
            var manager = new DownloadManager();
            var config = Config.New();
            config.DefaultQueue.IsRunning = false;
            manager.Initialize(config);
            var row = manager.Add(new DownloadItem
            {
                Url = "https://10.255.255.1/sample.zip",
                FileName = "sample.zip",
                SaveFolder = dir,
            }, autoStart: false);

            row.OpenFolderCommand.Execute(null);

            // It reached the launcher, and with the file's own path — not an empty one.
            Assert.True(_ran.Count > 0 || _opened.Count > 0, "the command never reached ShellLauncher");
            if (_ran.Count > 0)
                Assert.Contains(_ran.SelectMany(r => r.Args).Concat(new[] { _ran[0].File }),
                    a => a.Contains("sample.zip") || a.Contains(dir));
            else
                Assert.Contains(_opened, o => o == dir);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>A row with no folder saved must still not fail silently.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void A_row_with_nothing_to_open_does_not_crash()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false;
        manager.Initialize(config);
        var row = manager.Add(new DownloadItem { Url = "https://10.255.255.1/x.zip", SaveFolder = "" }, autoStart: false);

        row.OpenFolderCommand.Execute(null);   // must not throw — an exception here would be swallowed
                                               // by the dispatcher hook and look like "nothing happens"
    }

    /// <summary>
    /// The grid's button is really bound to that command. An unbound button is indistinguishable from a
    /// launcher failure from the user's side, so this is worth pinning rather than eyeballing the XAML.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void The_grid_renders_a_bound_open_folder_button()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false;
        manager.Initialize(config);
        manager.Add(new DownloadItem
        {
            Url = "https://10.255.255.1/sample.zip",
            FileName = "sample.zip",
            SaveFolder = Path.GetTempPath(),
        }, autoStart: false);

        var view = new DownloadsView { DataContext = new DownloadsViewModel(manager) };
        var window = new Window { Content = view, Width = 1000, Height = 600 };
        window.Show();
        DesktopLifetimeScope.Pump(8);

        var bound = view.GetVisualDescendants().OfType<Button>()
            .Count(b => ReferenceEquals(b.Command, manager.Items[0].OpenFolderCommand));
        Assert.True(bound > 0, "no rendered button is bound to the row's OpenFolderCommand");

        window.Close();
    }

    /// <summary>The extension dialog's Open-folder button binds through a $parent lookup, which is exactly
    /// the shape that fails silently when it does not resolve.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task The_extension_dialog_open_folder_button_is_bound_and_carries_its_row()
    {
        var vm = new ExtensionInstallViewModel(
            detect: () => new[] { new DetectedBrowser { Id = "chrome", Name = "Chrome", Family = BrowserFamily.Chromium, ExecutablePath = "/usr/bin/google-chrome" } },
            fetchCatalog: _ => Task.FromResult<IReadOnlyList<ExtensionCatalogEntry>>(new[]
            {
                new ExtensionCatalogEntry { Id = "chrome", Family = "chromium", Name = "Chrome", Version = "1.8.0", AssetName = "c.zip", AssetUrl = "https://x/c.zip", Sha256 = new string('a', 64) },
            }),
            install: (e, _, _) => Task.FromResult(ExtensionInstallResult.Ok("/data/extension/chrome", e.Version)),
            lastSeenVersion: _ => null,
            readInstalled: _ => new InstalledCopy("/data/extension/chrome", "1.8.0"),
            installBundled: (id, _) => ExtensionInstallResult.Ok($"/data/extension/{id}", "1.8.0"),
            bundledVersion: () => "0.0.1");
        await vm.LoadAsync();

        var view = new ExtensionInstallView { DataContext = vm };
        view.Show();
        DesktopLifetimeScope.Pump(8);

        var button = view.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => ReferenceEquals(b.Command, vm.OpenFolderCommand));
        Assert.NotNull(button);
        Assert.IsType<ExtensionTargetRow>(button.CommandParameter);   // the row travels with the click

        button.Command.Execute(button.CommandParameter);
        Assert.Contains("/data/extension/chrome", _opened);

        view.Close();
    }
}
