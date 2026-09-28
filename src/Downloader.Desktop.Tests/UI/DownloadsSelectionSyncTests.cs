using System.Linq;
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
/// A row's checkbox IS its selection: the grid's selection (click / Ctrl+click / Shift+click) sets the
/// checkboxes, and a checkbox or select-all change updates the grid. Headless cannot produce a real
/// row click, so the grid's SelectedItem/SelectedItems stand in for the mouse.
/// </summary>
public class DownloadsSelectionSyncTests
{
    private static (DownloadManager manager, DownloadsViewModel page, DataGrid grid, DownloadItemViewModel[] rows) Host()
    {
        Localizer.Instance.Load("en");
        var config = Config.New();
        var manager = new DownloadManager();
        manager.Initialize(config);
        config.DefaultQueue.IsRunning = false;
        var rows = new[] { "a.bin", "b.bin", "c.bin" }.Select(n =>
        {
            var vm = manager.Add(new DownloadItem { Url = "https://10.255.255.1/" + n, FileName = n }, autoStart: false);
            vm.Status = DownloadStatus.Running;
            return vm;
        }).ToArray();
        var page = new DownloadsViewModel(manager);
        var view = new DownloadsView { DataContext = page };
        new Window { Content = view, Width = 1100, Height = 600 }.Show();
        DesktopLifetimeScope.Pump(8);
        return (manager, page, view.GetVisualDescendants().OfType<DataGrid>().First(), rows);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Selecting_another_row_unchecks_the_previous_one()
    {
        var (_, _, grid, r) = Host();
        r[0].IsChecked = true;

        grid.SelectedItem = r[1]; // a plain click on B

        Assert.False(r[0].IsChecked);
        Assert.True(r[1].IsChecked);
        Assert.Equal(new object[] { r[1] }, grid.SelectedItems.Cast<object>());
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Adding_a_row_to_the_selection_keeps_both_checked()
    {
        var (_, _, grid, r) = Host();
        grid.SelectedItem = r[0];

        grid.SelectedItems.Add(r[1]); // Ctrl+click on B

        Assert.True(r[0].IsChecked);
        Assert.True(r[1].IsChecked);
        Assert.False(r[2].IsChecked);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Checking_a_checkbox_adds_the_row_without_removing_others()
    {
        var (_, _, grid, r) = Host();
        grid.SelectedItem = r[0];

        r[1].IsChecked = true;

        Assert.Contains(r[0], grid.SelectedItems.Cast<object>());
        Assert.Contains(r[1], grid.SelectedItems.Cast<object>());
        Assert.True(r[0].IsChecked);

        r[1].IsChecked = false;
        Assert.DoesNotContain(r[1], grid.SelectedItems.Cast<object>());
        Assert.Contains(r[0], grid.SelectedItems.Cast<object>());
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Select_all_selects_every_visible_row_and_clearing_empties_it()
    {
        var (_, page, grid, r) = Host();

        page.SelectAllState = true;
        Assert.Equal(3, grid.SelectedItems.Count);

        page.SelectAllState = false;
        Assert.Empty(grid.SelectedItems);
        Assert.All(r, i => Assert.False(i.IsChecked));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_filter_that_hides_a_selected_row_unselects_it_and_pause_touches_only_checked_rows()
    {
        var (_, page, grid, r) = Host();
        grid.SelectedItem = r[0];
        grid.SelectedItems.Add(r[1]);

        r[1].Status = DownloadStatus.Failed;
        page.Filter = StatusFilter.Active; // hides B
        DesktopLifetimeScope.Pump();
        page.Filter = StatusFilter.All;
        DesktopLifetimeScope.Pump();

        Assert.True(r[0].IsChecked);
        Assert.False(r[1].IsChecked);

        page.PauseSelectedCommand.Execute(null);
        Assert.Equal(DownloadStatus.Paused, r[0].Status);
        Assert.Equal(DownloadStatus.Running, r[2].Status);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Sorting_and_reordering_keep_the_selection_and_checkboxes_equal()
    {
        var (_, page, grid, r) = Host();
        grid.SelectedItem = r[0];
        grid.SelectedItems.Add(r[2]);

        page.CycleSort("FileName");
        DesktopLifetimeScope.Pump();
        page.CycleSort("FileName");
        DesktopLifetimeScope.Pump();
        Assert.True(r[0].IsChecked);
        Assert.True(r[2].IsChecked);

        page.Reorder(r[0], r[2], placeAfter: true);
        DesktopLifetimeScope.Pump();
        var selected = grid.SelectedItems.Cast<DownloadItemViewModel>().ToHashSet();
        Assert.All(r, i => Assert.Equal(i.IsChecked, selected.Contains(i)));
    }
}
