using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The optional category sidebar: its two states and their persistence, what it lists, and the rule
/// that each count says exactly how many rows clicking that row would show.
/// </summary>
public class CategorySidebarTests
{
    private sealed class StubFileService : IFileService
    {
        private readonly Config _config;

        public StubFileService(Config config = null) => _config = config;

        public Config Saved { get; private set; }

        public Task<Config> LoadFromFileAsync() => Task.FromResult(_config ?? NewConfig());

        public Task SaveToFileAsync(Config itemToSave)
        {
            Saved = itemToSave;
            return Task.CompletedTask;
        }
    }

    /// <summary>A config whose categories are named by key, so assertions do not depend on wording.</summary>
    private static Config NewConfig()
    {
        var config = Config.New();
        config.Categories = CategoryService.CreateDefaults(key => key);
        return config;
    }

    private static (MainViewModel main, DownloadManager manager) Build(Config config = null)
    {
        Localizer.Instance.Load("en");
        config ??= NewConfig();
        var manager = new DownloadManager();
        var main = new MainViewModel(new StubFileService(config), manager);
        manager.Initialize(config);
        // MainViewModel's init is scheduled, not inline; pump it so the sidebar rows exist.
        Dispatcher.UIThread.RunJobs();
        return (main, manager);
    }

    private static void Add(DownloadManager manager, string name, DownloadStatus status = DownloadStatus.Stopped)
    {
        var vm = manager.Add(new DownloadItem { Url = "https://10.255.255.1/" + name, FileName = name },
            autoStart: false);
        vm.Status = status;
    }

    // ---- the toggle and its persistence --------------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_open_sidebar_is_still_open_next_launch()
    {
        var config = NewConfig();
        var (main, _) = Build(config);
        Assert.False(main.IsCategorySidebarOpen);

        main.ToggleCategorySidebarCommand.Execute(null);

        // Written to the configuration that gets saved…
        Assert.True(config.IsCategorySidebarOpen);

        // …and read back on the next launch.
        var (reopened, _) = Build(config);
        Assert.True(reopened.IsCategorySidebarOpen);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_closed_sidebar_is_still_closed_next_launch()
    {
        var config = NewConfig();
        config.IsCategorySidebarOpen = true;
        var (main, _) = Build(config);
        Assert.True(main.IsCategorySidebarOpen);

        main.ToggleCategorySidebarCommand.Execute(null);
        Assert.False(config.IsCategorySidebarOpen);

        var (reopened, _) = Build(config);
        Assert.False(reopened.IsCategorySidebarOpen);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Starting_up_with_a_saved_download_list_keeps_every_download()
    {
        // Regression. Announcing the category list mid-Initialize — while Items was still empty —
        // reached the shell, which answers a list change by saving, and a save writes the live item
        // list back over Config.Downloads. Initialize then read that emptied list and the user's
        // downloads were gone, silently, on every launch.
        var config = NewConfig();
        config.Downloads.Add(new DownloadItem
        {
            Url = "https://10.255.255.1/movie.mkv", FileName = "movie.mkv", Status = DownloadStatus.Stopped
        });
        config.Downloads.Add(new DownloadItem
        {
            Url = "https://10.255.255.1/song.mp3", FileName = "song.mp3", Status = DownloadStatus.Completed
        });

        var manager = new DownloadManager();
        var main = new MainViewModel(new StubFileService(config), manager);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, manager.Items.Count);
        Assert.Equal(new[] { "movie.mkv", "song.mp3" }, manager.Items.Select(i => i.FileName));
        Assert.Equal(2, config.Downloads.Count);
        // And they are categorized, which is the thing the mis-ordered call was there to make happen.
        Assert.Equal("video", manager.Items[0].Category.Id);
        Assert.Equal("audio", manager.Items[1].Category.Id);
        Assert.Equal(2, main.CategoryRows.First(r => r.IsAll).Count);
    }

    // ---- what it lists -------------------------------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void It_lists_All_first_then_every_category_in_order()
    {
        var (main, manager) = Build();

        Assert.True(main.CategoryRows[0].IsAll);
        Assert.Equal(manager.Categories.Categories.Select(c => c.Id),
            main.CategoryRows.Skip(1).Select(r => r.Id));
        // Only the "All" row carries interface text; a category's name is the user's own.
        Assert.Equal("All", main.CategoryRows[0].Name);
        Assert.False(main.CategoryRows[0].CanEdit);
        Assert.True(main.CategoryRows[1].CanEdit);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_empty_category_is_dimmed_rather_than_removed_from_the_list()
    {
        var (main, manager) = Build();
        Add(manager, "movie.mkv");
        Dispatcher.UIThread.RunJobs();

        var video = main.CategoryRows.First(r => r.Id == "video");
        var audio = main.CategoryRows.First(r => r.Id == "audio");

        Assert.Equal(1, video.Count);
        Assert.False(video.IsEmpty);
        // Still listed. Hiding and re-showing rows as downloads arrive would make the sidebar twitch.
        Assert.Equal(0, audio.Count);
        Assert.True(audio.IsEmpty);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_new_category_appears_without_a_restart()
    {
        var (main, manager) = Build();
        var before = main.CategoryRows.Count;

        manager.Categories.Add(new DownloadCategory { Name = "Ebooks", Icon = "document" });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(before + 1, main.CategoryRows.Count);
        Assert.Equal("Ebooks", main.CategoryRows.Last().Name);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Only_the_All_row_follows_a_language_change()
    {
        var (main, _) = Build();
        var all = main.CategoryRows[0];
        var video = main.CategoryRows.First(r => r.Id == "video");
        var categoryNameBefore = video.Name;

        try
        {
            Localizer.Instance.Load("fa");

            // "All" is interface text and must translate…
            Assert.Equal(Localizer.Instance["Cat_All"], all.Name);
            Assert.NotEqual("All", all.Name);
            // …while a category's name is the user's data and must not, whatever script it is in.
            Assert.Equal(categoryNameBefore, video.Name);
        }
        finally
        {
            Localizer.Instance.Load("en");
        }
    }

    // ---- clearing the filters ------------------------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Clear_filters_resets_the_controls_that_show_the_filters_not_just_the_list()
    {
        // Regression (reported with screenshots): the empty state's button cleared the PAGE's filter
        // state, so the list refilled — while the sidebar row stayed highlighted, the footer pill
        // stayed active and the search box kept its text. The page and the shell hold the filters
        // between them, and the button only ever reached the page.
        var (main, manager) = Build();
        Add(manager, "movie.mkv");
        Add(manager, "song.mp3");
        Dispatcher.UIThread.RunJobs();

        main.SelectedCategoryId = "document";     // a category with nothing in it
        main.ShowFailedCommand.Execute(null);
        main.SearchText = "nothing-matches-this";
        Dispatcher.UIThread.RunJobs();

        Assert.True(main.Downloads.IsEmpty);
        Assert.True(main.Downloads.HasFilter);

        // Through the BUTTON the empty state actually binds to, not the method behind it.
        main.Downloads.ClearFiltersCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // The list comes back…
        Assert.False(main.Downloads.IsEmpty);
        Assert.False(main.Downloads.HasFilter);
        Assert.Equal(2, main.Downloads.ItemsView.Cast<DownloadItemViewModel>().Count());

        // …and so does every control that was showing a filter as applied.
        Assert.Null(main.SelectedCategoryId);
        Assert.True(main.CategoryRows[0].IsSelected);
        Assert.DoesNotContain(main.CategoryRows.Skip(1), r => r.IsSelected);
        Assert.True(main.IsAllSelected);
        Assert.False(main.IsFailedSelected);
        Assert.True(string.IsNullOrEmpty(main.SearchText));
    }

    // ---- counts --------------------------------------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Each_count_is_exactly_what_clicking_that_row_would_show()
    {
        var (main, manager) = Build();
        Add(manager, "a.mp4", DownloadStatus.Failed);
        Add(manager, "b.mp4", DownloadStatus.Failed);
        Add(manager, "c.mp4", DownloadStatus.Failed);
        Add(manager, "d.mp4", DownloadStatus.Completed);
        Add(manager, "e.mp4", DownloadStatus.Completed);
        Add(manager, "song.mp3", DownloadStatus.Failed);
        Dispatcher.UIThread.RunJobs();

        var video = main.CategoryRows.First(r => r.Id == "video");
        var all = main.CategoryRows[0];
        Assert.Equal(5, video.Count);
        Assert.Equal(6, all.Count);

        // Narrow to Failed: the counts move with it, because a number that did not would send the
        // user to an empty list.
        main.ShowFailedCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, video.Count);
        Assert.Equal(4, all.Count);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Selecting_a_row_marks_it_and_filters_the_list()
    {
        var (main, manager) = Build();
        Add(manager, "movie.mkv");
        Add(manager, "song.mp3");
        Dispatcher.UIThread.RunJobs();

        var video = main.CategoryRows.First(r => r.Id == "video");
        video.SelectCommand.Execute(null);

        Assert.True(video.IsSelected);
        Assert.False(main.CategoryRows[0].IsSelected);
        Assert.Equal("video", main.Downloads.CategoryFilter);
        Assert.Single(main.Downloads.ItemsView.Cast<DownloadItemViewModel>());

        main.CategoryRows[0].SelectCommand.Execute(null);

        Assert.True(main.CategoryRows[0].IsSelected);
        Assert.Null(main.Downloads.CategoryFilter);
        Assert.Equal(2, main.Downloads.ItemsView.Cast<DownloadItemViewModel>().Count());
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Deleting_the_category_being_filtered_by_falls_back_to_showing_everything()
    {
        var (main, manager) = Build();
        manager.Categories.Add(new DownloadCategory { Name = "Mine", Icon = "file" });
        Dispatcher.UIThread.RunJobs();
        var mine = manager.Categories.Categories.Last().Id;
        Add(manager, "movie.mkv");
        main.SelectedCategoryId = mine;
        Assert.Equal(mine, main.Downloads.CategoryFilter);

        manager.Categories.Remove(mine);
        Dispatcher.UIThread.RunJobs();

        // A filter matching a category that no longer exists would show an empty list with no clue why.
        Assert.Null(main.Downloads.CategoryFilter);
        Assert.True(main.CategoryRows[0].IsSelected);
    }
}
