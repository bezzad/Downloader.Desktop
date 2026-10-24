using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// While the extension dialog is still loading, the main button shows a spinner — there is no progress
/// bar in the footer any more (the author found the bar beside an empty footer ugly).
/// </summary>
public class ExtensionInstallBusyTests
{
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task The_main_button_spins_while_loading_and_stops_after()
    {
        Localizer.Instance.Load("en");
        var catalog = new TaskCompletionSource<IReadOnlyList<ExtensionCatalogEntry>>();
        var vm = new ExtensionInstallViewModel(
            detect: () => new[] { new DetectedBrowser { Id = "chrome", Name = "Chrome", Family = BrowserFamily.Chromium, ExecutablePath = "/usr/bin/google-chrome" } },
            fetchCatalog: _ => catalog.Task,
            lastSeenVersion: _ => null,
            readInstalled: _ => null,
            bundledVersion: () => "1.0.0");

        var view = new ExtensionInstallView { DataContext = vm };
        view.Show();
        var load = vm.LoadAsync();
        DesktopLifetimeScope.Pump(8);

        Assert.Empty(view.GetVisualDescendants().OfType<ProgressBar>());
        var spinner = view.GetVisualDescendants().OfType<Arc>().Single(a => a.Name == "BusySpinner");
        var button = spinner.FindAncestorOfType<Button>();
        Assert.Same(vm.InstallCommand, button.Command);
        Assert.True(spinner.IsVisible);
        Assert.False(button.IsEffectivelyEnabled);

        catalog.SetResult(System.Array.Empty<ExtensionCatalogEntry>());
        await load;
        DesktopLifetimeScope.Pump(8);

        Assert.False(spinner.IsVisible);
        Assert.True(button.IsEffectivelyEnabled);

        view.Close();
    }
}
