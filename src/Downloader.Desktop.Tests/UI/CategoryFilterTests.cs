using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The category as a dimension of the downloads list: filtering by it, sorting the grid on it,
/// overriding it per download, and what a bulk action reaches while a filter is hiding rows.
/// </summary>
public class CategoryFilterTests
{
    private static (DownloadManager manager, DownloadsViewModel page) Build()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        var config = Config.New();
        // Named by key so the assertions do not depend on the localizer's wording.
        config.Categories = CategoryService.CreateDefaults(key => key);
        manager.Initialize(config);
        return (manager, new DownloadsViewModel(manager));
    }

    private static DownloadItemViewModel Add(DownloadManager manager, string name,
        DownloadStatus status = DownloadStatus.Stopped)
    {
        // An unreachable IP, never a hostname (a .invalid host stalls in DNS and hangs the suite).
        var vm = manager.Add(new DownloadItem { Url = "https://10.255.255.1/" + name, FileName = name },
            autoStart: false);
        vm.Status = status;
        return vm;
    }

    private static List<DownloadItemViewModel> Visible(DownloadsViewModel page) =>
        page.ItemsView.Cast<DownloadItemViewModel>().ToList();

    // ---- filtering -----------------------------------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Selecting_a_category_narrows_the_list_to_it()
    {
        var (manager, page) = Build();
        Add(manager, "movie.mkv");
        Add(manager, "song.mp3");
        Add(manager, "book.pdf");

        page.CategoryFilter = "video";

        Assert.Equal(new[] { "movie.mkv" }, Visible(page).Select(i => i.FileName));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_filter_applies_to_every_state_not_just_finished_downloads()
    {
        // The reported ask was explicitly that this work on the WHOLE list. A running video and a
        // running archive must be told apart exactly like two completed ones.
        var (manager, page) = Build();
        Add(manager, "movie.mkv", DownloadStatus.Running);
        Add(manager, "pack.zip", DownloadStatus.Running);
        Add(manager, "clip.mp4", DownloadStatus.Created);
        Add(manager, "old.mp4", DownloadStatus.Completed);
        Add(manager, "bad.mp4", DownloadStatus.Failed);

        page.CategoryFilter = "video";

        Assert.Equal(new[] { "movie.mkv", "clip.mp4", "old.mp4", "bad.mp4" },
            Visible(page).Select(i => i.FileName));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_three_filters_are_all_applied_together()
    {
        var (manager, page) = Build();
        Add(manager, "holiday.mp4", DownloadStatus.Completed);
        Add(manager, "holiday.mp3", DownloadStatus.Completed);   // wrong category
        Add(manager, "holiday.mkv", DownloadStatus.Failed);      // wrong status
        Add(manager, "work.mp4", DownloadStatus.Completed);      // wrong search term

        page.CategoryFilter = "video";
        page.Filter = StatusFilter.Completed;
        page.Search = "holiday";

        Assert.Equal(new[] { "holiday.mp4" }, Visible(page).Select(i => i.FileName));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Clearing_the_category_leaves_the_status_filter_and_the_search_in_force()
    {
        // "All" is not "no filters at all" — it drops one dimension and leaves the other two alone.
        var (manager, page) = Build();
        Add(manager, "holiday.mp4", DownloadStatus.Completed);
        Add(manager, "holiday.mp3", DownloadStatus.Completed);
        Add(manager, "holiday.zip", DownloadStatus.Failed);
        Add(manager, "work.mp3", DownloadStatus.Completed);

        page.CategoryFilter = "video";
        page.Filter = StatusFilter.Completed;
        page.Search = "holiday";
        page.CategoryFilter = null;

        Assert.Equal(new[] { "holiday.mp4", "holiday.mp3" }, Visible(page).Select(i => i.FileName));
        Assert.Equal(StatusFilter.Completed, page.Filter);
        Assert.Equal("holiday", page.Search);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_empty_result_is_told_apart_from_an_empty_download_list()
    {
        var (manager, page) = Build();

        // Nothing added at all: the list is empty and no filter is why.
        Assert.True(page.IsEmpty);
        Assert.False(page.HasFilter);

        Add(manager, "song.mp3");
        page.CategoryFilter = "video";

        // Now there ARE downloads and the filter is hiding them — a different message, with a way out.
        Assert.True(page.IsEmpty);
        Assert.True(page.HasFilter);

        page.ClearFilters();

        Assert.False(page.IsEmpty);
        Assert.False(page.HasFilter);
    }

    // ---- select-all and bulk actions -----------------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Select_all_covers_only_the_rows_the_filter_is_showing()
    {
        var (manager, page) = Build();
        var video = Add(manager, "movie.mkv");
        var audio = Add(manager, "song.mp3");

        page.CategoryFilter = "video";
        page.SelectAllState = true;

        Assert.True(video.IsChecked);
        Assert.False(audio.IsChecked);
        // With every visible row checked the header reads as fully checked, not indeterminate — the
        // hidden row must not make it look like a partial selection.
        Assert.True(page.SelectAllState);
        Assert.Equal(1, page.SelectedCount);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async System.Threading.Tasks.Task A_bulk_remove_never_reaches_a_download_the_filter_is_hiding()
    {
        // The defect this exists for: check everything with no filter, narrow to one category, press
        // Remove — and watch downloads you cannot see disappear.
        var (manager, page) = Build();
        var video = Add(manager, "movie.mkv");
        var audio = Add(manager, "song.mp3");
        var doc = Add(manager, "book.pdf");

        page.SelectAllState = true;             // all three checked, nothing hidden
        Assert.True(audio.IsChecked);

        page.CategoryFilter = "video";          // now only the video is on screen
        Assert.Equal(1, page.SelectedCount);

        page.RemoveSelectedCommand.Execute(null);
        await System.Threading.Tasks.Task.Yield();

        Assert.DoesNotContain(video, manager.Items);
        Assert.Contains(audio, manager.Items);
        Assert.Contains(doc, manager.Items);
    }

    // ---- sorting -------------------------------------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_type_column_sorts_by_the_users_order_not_by_a_name()
    {
        // Sorting on a name would order on the alphabet of whichever language is loaded, so the
        // grouping would rearrange itself when the user switches language. It sorts on position.
        var (manager, page) = Build();
        var doc = Add(manager, "book.pdf");
        var video = Add(manager, "movie.mkv");
        var audio = Add(manager, "song.mp3");

        Assert.True(video.CategoryOrder < audio.CategoryOrder);
        Assert.True(audio.CategoryOrder < doc.CategoryOrder);

        // Move Documents to the top and the order the grid would show changes with it.
        while (manager.Categories.ById("document").Position > 0)
            manager.Categories.Move("document", -1);

        Assert.True(doc.CategoryOrder < video.CategoryOrder);
    }

    // ---- per-download override -----------------------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_download_can_be_filed_somewhere_its_extension_does_not_suggest()
    {
        var (manager, page) = Build();
        var row = Add(manager, "clip.mp4");

        Assert.Equal("video", row.Category.Id);

        row.CategoryId = "image";

        Assert.Equal("image", row.Category.Id);
        page.CategoryFilter = "image";
        Assert.Contains(row, Visible(page));
        page.CategoryFilter = "video";
        Assert.DoesNotContain(row, Visible(page));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_override_survives_a_restart_and_choosing_automatic_undoes_it()
    {
        var (manager, _) = Build();
        var row = Add(manager, "clip.mp4");
        row.CategoryId = "image";

        // Round-trip the way a real restart does: the saved list, re-loaded into a fresh manager.
        var saved = Config.New();
        saved.Categories = CategoryService.CreateDefaults(key => key);
        saved.Downloads = manager.Items.Select(i => i.GetItem()).ToList();

        var reopened = new DownloadManager();
        reopened.Initialize(saved);

        var restored = reopened.Items.Single();
        Assert.Equal("image", restored.Category.Id);

        // "Automatic" is simply clearing the choice, which is only expressible because the field is
        // nullable rather than resolved once and stored.
        restored.CategoryId = null;
        Assert.Equal("video", restored.Category.Id);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Deleting_a_category_releases_the_downloads_that_were_filed_under_it()
    {
        var (manager, _) = Build();
        manager.Categories.Add(new DownloadCategory { Name = "Mine", Icon = "file" });
        var mine = manager.Categories.Categories.Last().Id;
        var row = Add(manager, "clip.mp4");
        row.CategoryId = mine;
        Assert.Equal(mine, row.Category.Id);

        manager.Categories.Remove(mine);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // The choice is cleared, not merely ignored — left in place it would resurrect if some later
        // category reused the id.
        Assert.Null(row.CategoryId);
        Assert.Equal("video", row.Category.Id);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Changing_the_category_of_a_running_download_does_not_disturb_it()
    {
        var (manager, _) = Build();
        var row = Add(manager, "movie.mkv", DownloadStatus.Running);
        row.StageProgress(24.7, 5000, 1234, 5000);
        row.FlushProgress();
        var progressBefore = row.Progress;

        row.CategoryId = "image";

        Assert.Equal("image", row.Category.Id);
        Assert.Equal(DownloadStatus.Running, row.Status);
        Assert.Equal(progressBefore, row.Progress);
        Assert.Contains(row, manager.Items);
    }
}
