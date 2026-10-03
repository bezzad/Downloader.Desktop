using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// Moving the selection to another row must take the highlight off the old row at once. Avalonia's
/// DataGrid refreshes a cell's own <c>:selected</c> pseudo-class only when the pointer enters that cell,
/// so a highlight keyed on the CELL stayed painted on the previous row until it was hovered.
/// </summary>
public class GridSelectionHighlightTests
{
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Selecting_another_row_moves_the_highlight_off_every_cell_of_the_old_row()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        var config = Config.New();
        config.Settings.DefaultSavePath = TempDir();
        config.DefaultQueue.IsRunning = false;
        manager.Initialize(config);
        var first = Add(manager, "a.zip");
        var second = Add(manager, "b.zip");

        var view = new DownloadsView { DataContext = new DownloadsViewModel(manager), Height = 400 };
        var window = new Window { Width = 1100, Height = 500, Content = view };
        window.Show();
        Pump();
        var grid = view.FindControl<DataGrid>("Root")!;

        grid.SelectedItem = first;
        Pump();
        grid.SelectedItem = second;
        Pump();

        var selectedBg = Cells(grid, second).Select(c => c.Background).Distinct().ToList();
        Assert.Single(selectedBg);
        Assert.NotNull(selectedBg[0]);
        Assert.All(Cells(grid, first), c => Assert.NotEqual(selectedBg[0], c.Background));
    }

    private static IEnumerable<DataGridCell> Cells(DataGrid grid, DownloadItemViewModel vm) =>
        grid.GetVisualDescendants().OfType<DataGridRow>().First(r => r.DataContext == vm)
            .GetVisualDescendants().OfType<DataGridCell>().ToList();

    private static void Pump()
    {
        for (int i = 0; i < 5; i++)
            Dispatcher.UIThread.RunJobs();
    }

    private static DownloadItemViewModel Add(DownloadManager manager, string name)
    {
        var vm = manager.Add(new DownloadItem
        {
            Urls = new List<string> { "https://10.255.255.1/" + name },
            SaveFolder = TempDir(),
            FileName = name,
        }, autoStart: false);
        vm.Status = DownloadStatus.Stopped;
        return vm;
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dldesktop-gridsel-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
