using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// What the archived downloads look like from the list's side (issue #17): which filter shows them, which
/// pill counts them, and which toolbar cluster is on screen. The rule being pinned is that archiving is a
/// SEPARATE axis from a download's state — an archived Failed download is still Failed, so it must not be
/// found under Failed, nor under All, and must read Failed again once restored.
/// </summary>
public class ArchiveViewTests
{
    // ── the filter ───────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void No_status_filter_shows_an_archived_download()
    {
        var (manager, page) = NewPage();
        var vm = Add(manager, "kept.zip", DownloadStatus.Failed);
        manager.Archive(vm);

        foreach (var filter in new[]
                 {
                     StatusFilter.All, StatusFilter.Active, StatusFilter.Queued,
                     StatusFilter.Stopped, StatusFilter.Completed, StatusFilter.Failed
                 })
        {
            page.Filter = filter;
            Assert.DoesNotContain(vm, Visible(page));
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_archived_filter_shows_them_whatever_their_state()
    {
        var (manager, page) = NewPage();
        var failed = Add(manager, "failed.zip", DownloadStatus.Failed);
        var done = Add(manager, "done.zip", DownloadStatus.Completed);
        var ordinary = Add(manager, "live.zip", DownloadStatus.Completed);
        manager.Archive(failed);
        manager.Archive(done);

        page.Filter = StatusFilter.Archived;
        var shown = Visible(page);

        Assert.Equal(2, shown.Count);
        Assert.Contains(failed, shown);
        Assert.Contains(done, shown);
        Assert.DoesNotContain(ordinary, shown);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Restoring_puts_the_row_back_under_its_own_status()
    {
        var (manager, page) = NewPage();
        var vm = Add(manager, "back.zip", DownloadStatus.Failed);
        manager.Archive(vm);

        manager.Unarchive(vm);
        page.Filter = StatusFilter.Failed;

        Assert.Contains(vm, Visible(page));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Search_narrows_the_archived_view()
    {
        var (manager, page) = NewPage();
        var wanted = Add(manager, "holiday-photos.zip", DownloadStatus.Completed);
        var other = Add(manager, "kernel.iso", DownloadStatus.Completed);
        manager.Archive(wanted);
        manager.Archive(other);

        page.Filter = StatusFilter.Archived;
        page.Search = "holiday";

        Assert.Equal(new[] { wanted }, Visible(page));
    }

    // ── the pills ────────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Every_pill_counts_exactly_the_rows_clicking_it_shows()
    {
        var (manager, page, main) = NewShell();
        Add(manager, "a.zip", DownloadStatus.Completed);
        Add(manager, "b.zip", DownloadStatus.Failed);
        var archivedDone = Add(manager, "c.zip", DownloadStatus.Completed);
        var archivedFailed = Add(manager, "d.zip", DownloadStatus.Failed);
        manager.Archive(archivedDone);
        manager.Archive(archivedFailed);

        var expected = new (StatusFilter Filter, int Count)[]
        {
            (StatusFilter.All, main.AllCount),
            (StatusFilter.Completed, main.CompletedFilterCount),
            (StatusFilter.Failed, main.FailedFilterCount),
            (StatusFilter.Archived, main.ArchivedFilterCount),
        };

        foreach (var (filter, count) in expected)
        {
            page.Filter = filter;
            Assert.Equal(count, Visible(page).Count);
        }

        Assert.Equal(2, main.AllCount);          // the archived two are outside every status bucket
        Assert.Equal(2, main.ArchivedFilterCount);
    }

    /// <summary>Archiving must not shrink the status bar's total. The pills count what their filter
    /// shows; this total says how much the app has fetched, and an archived download kept its file.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_archived_download_still_counts_towards_the_status_bar_total()
    {
        var (manager, _, main) = NewShell();
        var vm = Add(manager, "big.iso", DownloadStatus.Completed);
        vm.Downloaded = 5 * 1024 * 1024;
        var withIt = main.TotalDownloadedText;

        manager.Archive(vm);

        Assert.Equal(withIt, main.TotalDownloadedText);
        Assert.Equal(DownloadItemViewModel.FormatBytes(5 * 1024 * 1024), main.TotalDownloadedText);
    }

    /// <summary>The live rows and the archived ones are summed together — not one or the other.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_status_bar_total_adds_the_archived_bytes_to_the_live_ones()
    {
        var (manager, _, main) = NewShell();
        var kept = Add(manager, "live.iso", DownloadStatus.Completed);
        kept.Downloaded = 3 * 1024 * 1024;
        var filed = Add(manager, "old.iso", DownloadStatus.Completed);
        filed.Downloaded = 2 * 1024 * 1024;

        manager.Archive(filed);

        Assert.Equal(DownloadItemViewModel.FormatBytes(5 * 1024 * 1024), main.TotalDownloadedText);
        Assert.Equal(1, main.AllCount);          // the pill still shows only the live row
        Assert.Equal(1, main.ArchivedFilterCount);
    }

    // ── the toolbar ──────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_toolbar_cluster_follows_the_active_filter_and_the_open_page()
    {
        var (_, _, main) = NewShell();

        main.ShowAllCommand.Execute(null);
        Assert.True(main.IsWorkingListSelected);
        Assert.False(main.IsArchivedSelected);

        main.ShowArchivedCommand.Execute(null);
        Assert.False(main.IsWorkingListSelected, "Start/Pause/Stop/Archive make no sense over archived rows");
        Assert.True(main.IsArchivedSelected);

        // A management page hides BOTH clusters, exactly as it did before archiving existed.
        main.ShowSettingViewCommand.Execute(null);
        Assert.False(main.IsWorkingListSelected);
        Assert.False(main.IsArchivedSelected);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Archive_is_enabled_by_the_selection_like_the_other_bulk_actions()
    {
        var (manager, page) = NewPage();
        var vm = Add(manager, "pick-me.zip", DownloadStatus.Completed);

        Assert.False(page.ArchiveSelectedCommand.CanExecute(null));
        Assert.False(page.UnarchiveSelectedCommand.CanExecute(null));

        vm.IsChecked = true;

        Assert.True(page.ArchiveSelectedCommand.CanExecute(null));
        Assert.True(page.StartSelectedCommand.CanExecute(null)); // same rule as the neighbours
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Archiving_the_selection_files_every_checked_row_away()
    {
        var (manager, page) = NewPage();
        var one = Add(manager, "one.zip", DownloadStatus.Completed);
        var two = Add(manager, "two.zip", DownloadStatus.Failed);
        var untouched = Add(manager, "three.zip", DownloadStatus.Completed);
        one.IsChecked = two.IsChecked = true;

        page.ArchiveSelectedCommand.Execute(null);

        Assert.True(one.IsArchived);
        Assert.True(two.IsArchived);
        Assert.False(untouched.IsArchived);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────

    private static (DownloadManager Manager, DownloadsViewModel Page) NewPage()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        var config = Config.New();
        config.Settings.DefaultSavePath = TempDir();
        config.DefaultQueue.IsRunning = false; // nothing may start: these are list tests
        manager.Initialize(config);
        return (manager, new DownloadsViewModel(manager));
    }

    private static (DownloadManager Manager, DownloadsViewModel Page, MainViewModel Main) NewShell()
    {
        var (manager, page) = NewPage();
        // The counts and the toolbar flags live on MainViewModel; the page here is a standalone view over
        // the SAME manager, so setting its filter and reading those counts are two views of one list.
        var main = new MainViewModel(new FileService(), manager);
        return (manager, page, main);
    }

    private static DownloadItemViewModel Add(DownloadManager manager, string name, DownloadStatus status)
    {
        var vm = manager.Add(new DownloadItem
        {
            Urls = new List<string> { "https://10.255.255.1/" + name },
            SaveFolder = TempDir(),
            FileName = name,
        }, autoStart: false);
        vm.Status = status;
        return vm;
    }

    private static List<DownloadItemViewModel> Visible(DownloadsViewModel page)
    {
        page.Refresh();
        return page.ItemsView.Cast<DownloadItemViewModel>().ToList();
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dldesktop-archview-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
