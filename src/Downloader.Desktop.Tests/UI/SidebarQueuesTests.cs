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
/// The sidebar's Queues section: one row per live queue, the queue filter, and the single selection
/// shared with the Categories section.
/// </summary>
public class SidebarQueuesTests
{
    private sealed class StubFileService : IFileService
    {
        private readonly Config _config;

        public StubFileService(Config config) => _config = config;

        public Task<Config> LoadFromFileAsync() => Task.FromResult(_config);

        public Task SaveToFileAsync(Config itemToSave) => Task.CompletedTask;
    }

    private static (MainViewModel main, DownloadManager manager) Build()
    {
        Localizer.Instance.Load("en");
        var config = Config.New();
        config.Categories = CategoryService.CreateDefaults(key => key);
        config.DefaultQueue.IsRunning = false;   // nothing may start a real download
        var manager = new DownloadManager();
        var main = new MainViewModel(new StubFileService(config), manager);
        manager.Initialize(config);
        Dispatcher.UIThread.RunJobs();
        return (main, manager);
    }

    private static DownloadItemViewModel Add(DownloadManager manager, string name, DownloadQueue queue,
        DownloadStatus status = DownloadStatus.Stopped)
    {
        var vm = manager.Add(new DownloadItem { Url = "https://10.255.255.1/" + name, FileName = name },
            autoStart: false);
        manager.MoveToQueue(vm, queue.Id);
        vm.Status = status;
        return vm;
    }

    private static string[] Listed(MainViewModel main) =>
        main.Downloads.ItemsView.Cast<DownloadItemViewModel>().Select(i => i.FileName).OrderBy(n => n).ToArray();

    private static SidebarQueueRowViewModel Row(MainViewModel main, string name) =>
        main.QueueRows.Single(r => r.Name == name);

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Queues_are_listed_with_their_counts()
    {
        var (main, manager) = Build();
        var first = manager.Queues[0];
        var night = manager.AddQueue("Night");
        Add(manager, "a.mkv", first);
        Add(manager, "b.mkv", first);
        Add(manager, "c.mkv", night);
        Add(manager, "d.mkv", night);
        Add(manager, "e.mkv", night);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { first.Name, "Night" }, main.QueueRows.Select(r => r.Name));
        Assert.Equal(2, Row(main, first.Name).Count);
        Assert.Equal(3, Row(main, "Night").Count);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Selecting_a_queue_lists_only_its_downloads_whatever_their_state()
    {
        var (main, manager) = Build();
        var night = manager.AddQueue("Night");
        Add(manager, "main.mkv", manager.Queues[0]);
        Add(manager, "done.mkv", night, DownloadStatus.Completed);
        Add(manager, "failed.zip", night, DownloadStatus.Failed);
        Dispatcher.UIThread.RunJobs();

        Row(main, "Night").SelectCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(night.Id, main.SelectedQueueId);
        Assert.Equal(new[] { "done.mkv", "failed.zip" }, Listed(main));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_queue_filter_combines_with_the_status_filter_and_the_search()
    {
        var (main, manager) = Build();
        var night = manager.AddQueue("Night");
        Add(manager, "alpha.mkv", night, DownloadStatus.Failed);
        Add(manager, "beta.mkv", night, DownloadStatus.Failed);
        Add(manager, "alpha.zip", night, DownloadStatus.Completed);
        Add(manager, "alpha.iso", manager.Queues[0], DownloadStatus.Failed);
        Dispatcher.UIThread.RunJobs();

        main.SelectedQueueId = night.Id;
        main.ShowFailedCommand.Execute(null);
        main.SearchText = "alpha";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { "alpha.mkv" }, Listed(main));
        // Counts follow the status filter and the search too.
        Assert.Equal(1, Row(main, "Night").Count);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void One_selection_across_both_sections()
    {
        var (main, manager) = Build();
        var night = manager.AddQueue("Night");
        Add(manager, "a.mkv", night);
        Add(manager, "b.mp3", night);
        Dispatcher.UIThread.RunJobs();

        main.SelectedCategoryId = "video";
        Row(main, "Night").SelectCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // Picking a queue clears the category…
        Assert.Null(main.SelectedCategoryId);
        Assert.Equal(new[] { "a.mkv", "b.mp3" }, Listed(main));
        Assert.Single(main.QueueRows, r => r.IsSelected);
        Assert.DoesNotContain(main.CategoryRows, r => r.IsSelected);

        // …and picking a category clears the queue.
        main.CategoryRows.Single(r => r.Id == "audio").SelectCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(main.SelectedQueueId);
        Assert.DoesNotContain(main.QueueRows, r => r.IsSelected);
        Assert.Single(main.CategoryRows, r => r.IsSelected);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void All_clears_the_queue_but_keeps_the_status_filter_and_the_search()
    {
        var (main, manager) = Build();
        var night = manager.AddQueue("Night");
        Add(manager, "alpha.mkv", night, DownloadStatus.Failed);
        Add(manager, "alpha.zip", manager.Queues[0], DownloadStatus.Failed);
        Add(manager, "beta.zip", manager.Queues[0], DownloadStatus.Failed);
        Dispatcher.UIThread.RunJobs();

        main.SelectedQueueId = night.Id;
        main.ShowFailedCommand.Execute(null);
        main.SearchText = "alpha";
        main.CategoryRows[0].SelectCommand.Execute(null);   // "All"
        Dispatcher.UIThread.RunJobs();

        Assert.Null(main.SelectedQueueId);
        Assert.True(main.CategoryRows[0].IsSelected);
        Assert.True(main.IsFailedSelected);
        Assert.Equal("alpha", main.SearchText);
        Assert.Equal(new[] { "alpha.mkv", "alpha.zip" }, Listed(main));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void New_renamed_and_removed_queues_show_at_once()
    {
        var (main, manager) = Build();

        var night = manager.AddQueue("Night");
        Assert.Contains(main.QueueRows, r => r.Name == "Night");

        manager.RenameQueue(night, "Overnight");
        Assert.Contains(main.QueueRows, r => r.Name == "Overnight");
        Assert.DoesNotContain(main.QueueRows, r => r.Name == "Night");

        manager.RemoveQueue(night);
        Assert.DoesNotContain(main.QueueRows, r => r.Id == night.Id);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Removing_the_selected_queue_falls_back_to_All()
    {
        var (main, manager) = Build();
        var night = manager.AddQueue("Night");
        Add(manager, "a.mkv", manager.Queues[0]);
        Dispatcher.UIThread.RunJobs();

        main.SelectedQueueId = night.Id;
        Assert.Empty(Listed(main));

        manager.RemoveQueue(night);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(main.SelectedQueueId);
        Assert.True(main.CategoryRows[0].IsSelected);
        Assert.Equal(new[] { "a.mkv" }, Listed(main));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Clear_filters_resets_a_selected_queue()
    {
        var (main, manager) = Build();
        var night = manager.AddQueue("Night");
        Add(manager, "a.mkv", manager.Queues[0]);
        Dispatcher.UIThread.RunJobs();

        main.SelectedQueueId = night.Id;
        Dispatcher.UIThread.RunJobs();
        Assert.True(main.Downloads.IsEmpty);

        // Through the BUTTON the empty state binds to.
        main.Downloads.ClearFiltersCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(main.SelectedQueueId);
        Assert.False(main.Downloads.HasFilter);
        Assert.DoesNotContain(main.QueueRows, r => r.IsSelected);
        Assert.True(main.CategoryRows[0].IsSelected);
        Assert.Equal(new[] { "a.mkv" }, Listed(main));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Collapsing_a_section_hides_its_rows_and_expanding_shows_them()
    {
        var (main, _) = Build();
        Assert.True(main.IsCategoriesExpanded);
        Assert.True(main.IsQueuesExpanded);

        main.ToggleQueuesSectionCommand.Execute(null);
        Assert.False(main.IsQueuesExpanded);
        Assert.True(main.IsCategoriesExpanded);
        main.ToggleQueuesSectionCommand.Execute(null);
        Assert.True(main.IsQueuesExpanded);

        main.ToggleCategoriesSectionCommand.Execute(null);
        Assert.False(main.IsCategoriesExpanded);
    }
}
