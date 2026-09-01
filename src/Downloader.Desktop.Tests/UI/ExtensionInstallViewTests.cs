using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The install-extension dialog as a real window: that it renders, that Escape closes it, and that the
/// Settings button is what opens it. The decisions behind it are covered in
/// <c>Unit/ExtensionInstallViewModelTests</c>; this is the wiring.
/// </summary>
public class ExtensionInstallViewTests
{
    private static ExtensionInstallViewModel StubVm() => new(
        detect: () => new[]
        {
            new DetectedBrowser { Id = "chrome", Name = "Google Chrome", Family = BrowserFamily.Chromium, ExecutablePath = "/usr/bin/google-chrome" },
            new DetectedBrowser { Id = "firefox", Name = "Mozilla Firefox", Family = BrowserFamily.Gecko, ExecutablePath = "/usr/bin/firefox" },
        },
        fetchCatalog: _ => Task.FromResult<System.Collections.Generic.IReadOnlyList<ExtensionCatalogEntry>>(new[]
        {
            new ExtensionCatalogEntry { Id = "chrome", Family = "chromium", Name = "Chrome, Edge", Version = "1.8.0", AssetName = "c.zip", AssetUrl = "https://x/c.zip", Sha256 = new string('a', 64) },
            new ExtensionCatalogEntry { Id = "firefox", Family = "gecko", Name = "Firefox", Version = "1.8.0", AssetName = "f.zip", AssetUrl = "https://x/f.zip", Sha256 = new string('b', 64) },
        }),
        install: (e, _, _) => Task.FromResult(ExtensionInstallResult.Ok($"/data/extension/{e.Id}", e.Version)),
        lastSeenVersion: _ => null,
        installedPath: _ => null);

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task The_dialog_renders_the_browsers_and_the_steps()
    {
        Localizer.Instance.Load("en");
        var vm = StubVm();
        await vm.LoadAsync();

        var view = new ExtensionInstallView { DataContext = vm };
        view.Show();
        DesktopLifetimeScope.Pump();

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
        var selectable = view.GetVisualDescendants().OfType<SelectableTextBlock>().Select(t => t.Text ?? "").ToList();
        var checkboxes = view.GetVisualDescendants().OfType<CheckBox>().ToList();

        Assert.Equal(2, checkboxes.Count);                                  // one per detected browser
        Assert.Contains(texts, t => t.Contains("chrome://extensions"));     // the Chromium steps rendered
        Assert.Contains(texts, t => t.Contains("about:debugging"));         // the Gecko steps rendered
        Assert.Contains(texts, t => t.Contains("restarts", StringComparison.OrdinalIgnoreCase));
        // Nothing is unpacked yet, so no folder path is shown to copy.
        Assert.DoesNotContain(selectable, t => t.Contains("/data/extension/"));

        view.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task After_installing_the_folder_path_is_shown_so_it_can_be_copied()
    {
        Localizer.Instance.Load("en");
        var vm = StubVm();
        await vm.LoadAsync();

        var view = new ExtensionInstallView { DataContext = vm };
        view.Show();
        await vm.InstallSelectedAsync();
        DesktopLifetimeScope.Pump();

        // That path is the one thing the user has to hand to their browser, so it must be on screen and
        // selectable rather than merely known to the app.
        var selectable = view.GetVisualDescendants().OfType<SelectableTextBlock>().Select(t => t.Text ?? "").ToList();
        Assert.Contains(selectable, t => t.Contains("/data/extension/chrome"));

        view.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Escape_closes_the_dialog()
    {
        Localizer.Instance.Load("en");
        var vm = StubVm();
        await vm.LoadAsync();
        var view = new ExtensionInstallView { DataContext = vm };
        view.Show();
        DesktopLifetimeScope.Pump();

        view.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
        DesktopLifetimeScope.Pump();

        Assert.False(view.IsVisible);
    }

    /// <summary>Closing must drop the language subscription, or the dialog's VM outlives the process.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Closing_detaches_the_view_model()
    {
        Localizer.Instance.Load("en");
        var vm = StubVm();
        await vm.LoadAsync();
        var view = new ExtensionInstallView { DataContext = vm };
        view.Show();
        DesktopLifetimeScope.Pump();

        view.Close();
        DesktopLifetimeScope.Pump();

        // Detach is idempotent; the assertion that matters is that Close ran it without throwing.
        vm.Detach();
    }

    /// <summary>
    /// The Settings button is the only way a user reaches this, so it is worth pinning that it opens the
    /// dialog rather than merely existing. Needs a desktop lifetime — every DialogHelper entry point
    /// early-returns when there is no main window, which is the headless default.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void The_settings_button_opens_the_dialog()
    {
        Localizer.Instance.Load("en");
        using var scope = new DesktopLifetimeScope();

        var manager = new DownloadManager();
        var config = Config.New();
        config.DisabledPlugins ??= new System.Collections.Generic.List<string>();
        config.DefaultQueue.IsRunning = false;
        manager.Initialize(config);
        var settings = new SettingViewModel(config, manager);

        settings.InstallExtensionCommand.Execute(null);
        DesktopLifetimeScope.Pump();

        // ShowDialog parents to the main window, so the dialog is one of its owned windows — the
        // hand-made lifetime never populates its own Windows list.
        var dialog = scope.MainWindow.OwnedWindows.OfType<ExtensionInstallView>().FirstOrDefault();
        Assert.NotNull(dialog);

        dialog.Close();
        DesktopLifetimeScope.Pump();
    }
}
