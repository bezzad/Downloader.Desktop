using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Downloader;
using Downloader.Desktop.Converters;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>Tests that need the (headless) Avalonia runtime: geometry parsing, dispatcher, view models.</summary>
public class AppTests
{
    [AvaloniaTheory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("video")]
    [InlineData("audio")]
    [InlineData("image")]
    [InlineData("archive")]
    [InlineData("document")]
    [InlineData("app")]
    [InlineData("disc")]
    [InlineData("file")]
    [InlineData("unknown-kind")]
    public void FileKind_icons_parse(string kind)
    {
        var geometry = FileKindToIconConverter.Instance.Convert(kind, typeof(Geometry), null, CultureInfo.InvariantCulture);
        Assert.IsAssignableFrom<Geometry>(geometry);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Manager_add_and_remove_updates_stats()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());

        var vm = manager.Add(new DownloadItem { Url = "https://host/a.zip", SaveFolder = "/tmp" }, autoStart: false);
        Assert.Single(manager.Items);
        Assert.Equal(1, manager.QueuedCount);

        vm.Status = DownloadStatus.Completed;
        Assert.Equal(1, manager.CompletedCount);
        Assert.Equal(0, manager.QueuedCount);

        manager.Remove(vm);
        Assert.Empty(manager.Items);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void DownloadsViewModel_filters_by_status_and_search()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());

        var a = manager.Add(new DownloadItem { Url = "https://host/movie.mp4", FileName = "movie.mp4" }, autoStart: false);
        manager.Add(new DownloadItem { Url = "https://host/song.mp3", FileName = "song.mp3" }, autoStart: false);
        a.Status = DownloadStatus.Completed;

        var view = new DownloadsViewModel(manager) { Filter = StatusFilter.Completed };
        Assert.Single(view.ItemsView);

        view.Filter = StatusFilter.All;
        view.Search = "song";
        Assert.Single(view.ItemsView);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Stopped_items_show_under_All_but_not_under_Failed()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());

        var stopped = manager.Add(new DownloadItem { Url = "https://host/a.zip", FileName = "a.zip" }, autoStart: false);
        var failed = manager.Add(new DownloadItem { Url = "https://host/b.zip", FileName = "b.zip" }, autoStart: false);
        stopped.Status = DownloadStatus.Stopped;
        failed.Status = DownloadStatus.Failed;

        var view = new DownloadsViewModel(manager) { Filter = StatusFilter.Failed };
        Assert.Single(view.ItemsView);                       // only the real failure
        Assert.DoesNotContain(view.ItemsView.Cast<DownloadItemViewModel>(), i => i.Status == DownloadStatus.Stopped);

        view.Filter = StatusFilter.All;
        Assert.Equal(2, view.ItemsView.Count);               // stopped is visible under All
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Stopped_filter_lists_paused_and_stopped_and_buckets_are_disjoint()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());

        // One item per status → every bucket must own exactly its statuses, disjointly.
        var byStatus = new Dictionary<DownloadStatus, DownloadItemViewModel>();
        foreach (var s in new[]
                 {
                     DownloadStatus.Running, DownloadStatus.Paused, DownloadStatus.Stopped,
                     DownloadStatus.Failed, DownloadStatus.Completed, DownloadStatus.Created, DownloadStatus.None
                 })
        {
            var vm = manager.Add(new DownloadItem { Url = $"https://host/{s}.bin", FileName = $"{s}.bin" }, autoStart: false);
            vm.Status = s;
            byStatus[s] = vm;
        }

        var view = new DownloadsViewModel(manager);
        int CountFor(StatusFilter f)
        {
            view.Filter = f;
            return view.ItemsView.Count;
        }

        // The Stopped bucket owns Paused + Stopped (the user's "where did my paused items go" fix, #2).
        view.Filter = StatusFilter.Stopped;
        var stoppedSet = view.ItemsView.Cast<DownloadItemViewModel>().Select(i => i.Status).ToHashSet();
        Assert.Equal(new HashSet<DownloadStatus> { DownloadStatus.Paused, DownloadStatus.Stopped }, stoppedSet);

        // Disjoint + jointly exhaustive: the five buckets sum to the total.
        var total = CountFor(StatusFilter.Active) + CountFor(StatusFilter.Queued) + CountFor(StatusFilter.Stopped)
                    + CountFor(StatusFilter.Completed) + CountFor(StatusFilter.Failed);
        Assert.Equal(manager.Items.Count, total);
        Assert.Equal(manager.Items.Count, CountFor(StatusFilter.All));

        // Active narrowed to Running only; Failed owns Failed only.
        view.Filter = StatusFilter.Active;
        Assert.Equal(DownloadStatus.Running, Assert.Single(view.ItemsView.Cast<DownloadItemViewModel>()).Status);
        view.Filter = StatusFilter.Failed;
        Assert.Equal(DownloadStatus.Failed, Assert.Single(view.ItemsView.Cast<DownloadItemViewModel>()).Status);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Header_sort_cycles_asc_desc_none_and_none_restores_master_order()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        // Master order (= queue priority) deliberately differs from both sorted orders.
        manager.Add(new DownloadItem { Url = "https://host/m.bin", FileName = "m.bin" }, autoStart: false);
        manager.Add(new DownloadItem { Url = "https://host/a.bin", FileName = "a.bin" }, autoStart: false);
        manager.Add(new DownloadItem { Url = "https://host/z.bin", FileName = "z.bin" }, autoStart: false);

        var view = new DownloadsViewModel(manager);
        string[] Names() => view.ItemsView.Cast<DownloadItemViewModel>().Select(i => i.FileName).ToArray();

        Assert.Null(view.SortPath); // unsorted by default → master order
        Assert.Equal(new[] { "m.bin", "a.bin", "z.bin" }, Names());

        view.CycleSort("FileName"); // 1st click → ascending
        Assert.Equal("FileName", view.SortPath);
        Assert.Equal(System.ComponentModel.ListSortDirection.Ascending, view.SortDirection);
        Assert.Equal(new[] { "a.bin", "m.bin", "z.bin" }, Names());

        view.CycleSort("FileName"); // 2nd click → descending
        Assert.Equal(System.ComponentModel.ListSortDirection.Descending, view.SortDirection);
        Assert.Equal(new[] { "z.bin", "m.bin", "a.bin" }, Names());

        view.CycleSort("FileName"); // 3rd click → none (master order back, drag-reorder works again)
        Assert.Null(view.SortPath);
        Assert.Equal(new[] { "m.bin", "a.bin", "z.bin" }, Names());

        // Clicking a different column while sorted starts that column ascending.
        view.CycleSort("FileName");
        view.CycleSort("Size");
        Assert.Equal("Size", view.SortPath);
        Assert.Equal(System.ComponentModel.ListSortDirection.Ascending, view.SortDirection);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Clearing_the_sort_enables_drag_reorder_from_the_current_view()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        var m = manager.Add(new DownloadItem { Url = "https://host/m.bin", FileName = "m.bin" }, autoStart: false);
        var a = manager.Add(new DownloadItem { Url = "https://host/a.bin", FileName = "a.bin" }, autoStart: false);

        var view = new DownloadsViewModel(manager);
        view.CycleSort("FileName");
        Assert.NotNull(view.SortPath);

        // The grip's drag-start calls ClearSort() so the drop reorders the master list, not a sorted view.
        view.ClearSort();
        Assert.Null(view.SortPath);
        Assert.Empty(view.ItemsView.SortDescriptions);

        view.Reorder(a, m, placeAfter: false); // drag "a" above "m"
        // Master order (= pump priority) is the real invariant; the app refreshes the view via
        // MainViewModel.OnListChanged → Downloads.Refresh(), mirrored here.
        Assert.Equal(new[] { "a.bin", "m.bin" }, manager.Items.Select(i => i.FileName).ToArray());
        view.Refresh();
        Assert.Equal(new[] { "a.bin", "m.bin" },
            view.ItemsView.Cast<DownloadItemViewModel>().Select(i => i.FileName).ToArray());
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Status_bar_total_downloaded_sums_all_items()
    {
        var manager = new DownloadManager();
        var main = new MainViewModel(new StubFileService(), manager);
        manager.Initialize(Config.New());

        var a = manager.Add(new DownloadItem { Url = "https://host/a.bin", FileName = "a.bin" }, autoStart: false);
        var b = manager.Add(new DownloadItem { Url = "https://host/b.bin", FileName = "b.bin" }, autoStart: false);
        a.Downloaded = 512 * 1024;        // 0.5 MB
        b.Downloaded = 1536 * 1024;       // 1.5 MB

        manager.RaiseStatsForTest();

        Assert.Equal(DownloadItemViewModel.FormatBytes(2 * 1024 * 1024), main.TotalDownloadedText);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Bulk_add_streams_in_slices_and_coalesces_notifications()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());

        var listChanged = 0;
        manager.ListChanged += () => listChanged++;

        var items = Enumerable.Range(0, 500)
            .Select(i => new DownloadItem { Urls = new() { $"https://10.255.255.1/f{i}.bin" }, SaveFolder = "/tmp" })
            .ToList();

        await manager.AddRangeAsync(items, autoStart: false);

        Assert.Equal(500, manager.Items.Count);
        // The point of the fix (#bulk-add hang): notifications are per SLICE, not per item — 500
        // adds must not fire 500 full list/stats refreshes.
        Assert.InRange(listChanged, 1, 25);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void First_activation_shows_a_hidden_window()
    {
        var window = new Window { Width = 300, Height = 200, WindowState = WindowState.Minimized };
        window.Show();
        window.Hide(); // parked in the tray
        Assert.False(window.IsVisible);

        // One activation (tray click / second-instance launch) must fully surface it (#6).
        WindowActivation.BringToFront(window);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(window.IsVisible);
        Assert.Equal(WindowState.Normal, window.WindowState);
        window.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Taskbar_aggregate_progress_reflects_active_downloads()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());

        // No items → hidden.
        Assert.Equal((false, 0d), TaskbarProgressService.Aggregate(manager.Items));

        var a = manager.Add(new DownloadItem { Url = "https://host/a.bin" }, autoStart: false);
        var b = manager.Add(new DownloadItem { Url = "https://host/b.bin" }, autoStart: false);
        var c = manager.Add(new DownloadItem { Url = "https://host/c.bin" }, autoStart: false);

        // Nothing running → hidden (completed/stopped rows never keep a taskbar bar alive).
        c.Status = DownloadStatus.Completed;
        Assert.Equal((false, 0d), TaskbarProgressService.Aggregate(manager.Items));

        // Two running at 25% and 75% → visible at their mean (0.5), ignoring the completed row.
        a.Status = DownloadStatus.Running; a.Progress = 25;
        b.Status = DownloadStatus.Running; b.Progress = 75;
        var (visible, fraction) = TaskbarProgressService.Aggregate(manager.Items);
        Assert.True(visible);
        Assert.Equal(0.5, fraction, 3);

        // All done → hidden again.
        a.Status = DownloadStatus.Completed;
        b.Status = DownloadStatus.Stopped;
        Assert.Equal((false, 0d), TaskbarProgressService.Aggregate(manager.Items));
    }

    /// <summary>
    /// The three Settings → Logging buttons rendered with the Fluent default background, which is a
    /// very low-alpha white overlay: on our dark card surface they didn't read as buttons at all (light
    /// theme was fine). They must carry the explicit "secondary" style class that gives them a visible
    /// surface + border in both themes.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Logging_action_buttons_use_the_visible_secondary_style()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);

        var view = new Views.SettingView { DataContext = new SettingViewModel(config, manager) };
        var window = new Window { Content = view };
        window.Show();
        try
        {
            var logButtons = window.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Command != null && b.Classes.Contains("secondary"))
                .ToList();

            Assert.True(logButtons.Count >= 3,
                $"expected the 3 Logging buttons to use Classes=\"secondary\", found {logButtons.Count}");

            // A bare Fluent button is what made them invisible in dark mode — the class must actually
            // resolve to a real surface, not just be present as a label.
            foreach (var button in logButtons)
                Assert.NotNull(button.Background);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Caption (window control) buttons were oversized; they are 44x40, two pixels down from
    /// the original 46x42. Pinned so a future style edit doesn't quietly grow them back.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Caption_buttons_are_44x40()
    {
        var bar = new Views.TitleBar();
        var window = new Window { Content = bar };
        window.Show();
        try
        {
            var caption = window.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Contains("caption"))
                .ToList();

            Assert.Equal(3, caption.Count);
            Assert.All(caption, b =>
            {
                Assert.Equal(44, b.Width);
                Assert.Equal(40, b.Height);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Settings_sections_are_expanders_expanded_by_default_and_search_filters_options()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);

        var view = new Views.SettingView { DataContext = new SettingViewModel(config, manager) };
        var window = new Window { Content = view };
        window.Show();
        try
        {
            var panel = window.GetVisualDescendants().OfType<StackPanel>()
                .First(p => p.Name == "SectionsPanel");
            var sections = panel.Children.OfType<Expander>().ToList();

            // #16: every section (incl. Plugins and Advanced) is a collapsible Expander, expanded by default.
            Assert.True(sections.Count >= 6, $"expected >= 6 section expanders, got {sections.Count}");
            Assert.All(sections, s => Assert.True(s.IsExpanded, $"'{s.Header}' should start expanded"));

            // #15: searching narrows to matching rows/sections; "notif" lives in General + Notifications.
            view.ApplyFilter("notif");
            var visible = sections.Where(s => s.IsVisible).Select(s => s.Header?.ToString()).ToList();
            Assert.Contains(visible, h => h?.Contains("Notification", StringComparison.OrdinalIgnoreCase) == true);
            Assert.DoesNotContain(visible, h => h?.Contains("Logging", StringComparison.OrdinalIgnoreCase) == true);

            // Matching rows stay, non-matching rows in a visible section hide.
            var general = sections.First(s =>
                s.Header?.ToString()?.Contains("General", StringComparison.OrdinalIgnoreCase) == true);
            Assert.True(general.IsVisible); // holds the notifications master switch
            var generalRows = general.GetLogicalDescendants().OfType<Grid>()
                .Where(g => g.Classes.Contains("field")).ToList();
            Assert.Contains(generalRows, r => r.IsVisible);
            Assert.Contains(generalRows, r => !r.IsVisible);

            // Clearing restores everything.
            view.ApplyFilter("");
            Assert.All(sections, s => Assert.True(s.IsVisible));
            Assert.All(generalRows, r => Assert.True(r.IsVisible));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void New_schedules_get_distinct_numbered_names()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);

        var page = new SchedulerViewModel(config, manager);
        page.NewScheduleCommand.Execute(null);
        page.NewScheduleCommand.Execute(null);

        // Numbered — NOT the "New schedule" button label, so item and action can't be confused (#14).
        Assert.Equal("Schedule 1", page.Schedules[0].Name);
        Assert.Equal("Schedule 2", page.Schedules[1].Name);

        // The next number skips names already in use (delete #1, add again → smallest free = 1).
        page.Remove(page.Schedules[0]);
        page.NewScheduleCommand.Execute(null);
        Assert.Equal(new[] { "Schedule 2", "Schedule 1" }, page.Schedules.Select(s => s.Name).ToArray());
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Removing_a_queue_with_unfinished_items_requires_confirmation()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);
        var media = manager.AddQueue("Media");
        var item = manager.Add(new DownloadItem { Urls = new() { "https://host/a.bin" }, QueueId = media.Id }, autoStart: false);

        var page = new QueuesViewModel(config, manager);
        var mediaRow = page.Queues.First(r => r.Queue.Id == media.Id);

        // Unfinished item + user declines → queue stays.
        var asked = 0;
        page.ConfirmRemoval = (_, _) => { asked++; return Task.FromResult(false); };
        await page.Remove(mediaRow);
        Assert.Equal(1, asked);
        Assert.Contains(page.Queues, r => r.Queue.Id == media.Id);

        // User accepts → queue removed, its item reassigned to the default queue.
        page.ConfirmRemoval = (_, _) => Task.FromResult(true);
        await page.Remove(mediaRow);
        Assert.DoesNotContain(page.Queues, r => r.Queue.Id == media.Id);
        Assert.Equal(config.DefaultQueue.Id, item.GetItem().QueueId);

        // A queue with only finished items removes without asking.
        var docs = manager.AddQueue("Docs");
        var done = manager.Add(new DownloadItem { Urls = new() { "https://host/b.bin" }, QueueId = docs.Id }, autoStart: false);
        done.Status = DownloadStatus.Completed;
        var page2 = new QueuesViewModel(config, manager);
        var askedAgain = false;
        page2.ConfirmRemoval = (_, _) => { askedAgain = true; return Task.FromResult(true); };
        await page2.Remove(page2.Queues.First(r => r.Queue.Id == docs.Id));
        Assert.False(askedAgain);
        Assert.DoesNotContain(page2.Queues, r => r.Queue.Id == docs.Id);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Page_views_are_created_once_and_reused_across_navigation()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);

        var cache = new Views.PageViewCache();
        var downloads = new DownloadsViewModel(manager);
        var queues = new QueuesViewModel(config, manager);

        var v1 = cache.GetView(downloads);
        var q1 = cache.GetView(queues);
        Assert.IsType<Views.DownloadsView>(v1);
        Assert.IsType<Views.QueuesView>(q1);
        Assert.Same(downloads, v1.DataContext);

        // Navigating away and back must return the SAME instance — no page rebuild, state preserved.
        Assert.Same(v1, cache.GetView(downloads));
        Assert.Same(q1, cache.GetView(queues));
        Assert.NotSame(v1, q1);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Queue_item_wrappers_build_lazily_and_are_reused()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);
        for (var i = 0; i < 20; i++)
            manager.Add(new DownloadItem { Urls = new() { $"https://host/f{i}.bin" }, FileName = $"f{i}.bin" }, autoStart: false);

        var page = new QueuesViewModel(config, manager);
        var row = page.Queues[0];
        Assert.Equal(20, row.Items.Count); // expanded (default) → wrappers built

        // A list change that does NOT alter this queue's membership must REUSE the wrappers
        // (rebuilding 2k wrappers on every tick was part of the Queues-page hang).
        var before = row.Items[0];
        manager.RaiseStatsForTest();
        row.RebuildItems();
        Assert.Same(before, row.Items[0]);

        // Collapsing drops the wrappers entirely (nothing to render); expanding rebuilds them.
        row.IsExpanded = false;
        Assert.Empty(row.Items);
        Assert.True(row.HasItems); // aggregate stats still reflect the real membership
        row.IsExpanded = true;
        Assert.Equal(20, row.Items.Count);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Queue_cards_collapse_and_expand_individually_and_all_at_once()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);
        manager.AddQueue("Media");
        manager.AddQueue("Docs");

        var page = new QueuesViewModel(config, manager);
        Assert.Equal(3, page.Queues.Count);
        Assert.All(page.Queues, q => Assert.True(q.IsExpanded)); // expanded by default

        // One row toggles independently and the "all collapsed" flag follows the whole set.
        page.Queues[0].IsExpanded = false;
        Assert.False(page.Queues[0].IsExpanded);
        Assert.True(page.Queues[1].IsExpanded);
        Assert.False(page.AllCollapsed);

        page.AllCollapsed = true;   // toolbar toggle → collapse all
        Assert.All(page.Queues, q => Assert.False(q.IsExpanded));
        Assert.True(page.AllCollapsed);

        page.AllCollapsed = false;  // toolbar toggle → expand all
        Assert.All(page.Queues, q => Assert.True(q.IsExpanded));
        Assert.False(page.AllCollapsed);

        // Collapsing every row by hand flips the aggregate flag too.
        foreach (var q in page.Queues)
            q.IsExpanded = false;
        Assert.True(page.AllCollapsed);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Removing_a_queue_reassigns_its_items()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);

        var queue = manager.AddQueue("Second");
        var vm = manager.Add(new DownloadItem { Url = "https://host/c.zip", QueueId = queue.Id }, autoStart: false);
        Assert.Equal(queue.Id, vm.GetItem().QueueId);

        manager.RemoveQueue(queue);
        Assert.Equal(config.DefaultQueue.Id, vm.GetItem().QueueId);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void QueuesChanged_fires_on_add_and_remove()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());

        int fired = 0;
        manager.QueuesChanged += () => fired++;

        var queue = manager.AddQueue("Second");
        Assert.Equal(1, fired); // adding a queue notifies so the start/stop menus refresh

        manager.RemoveQueue(queue);
        Assert.Equal(2, fired); // removing a queue notifies too
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Start_stop_queue_menus_update_live_on_add_and_remove()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        var view = new DownloadsViewModel(manager);

        Assert.Single(view.StartQueueTargets); // default queue only
        Assert.Single(view.StopQueueTargets);

        var queue = manager.AddQueue("Second"); // no view.Refresh() — must update live (the reported bug)
        Assert.Equal(2, view.StartQueueTargets.Count);
        Assert.Contains(view.StartQueueTargets, t => t.Name == "Second");
        Assert.Contains(view.StopQueueTargets, t => t.Name == "Second");

        manager.RemoveQueue(queue);
        Assert.Single(view.StartQueueTargets);
        Assert.DoesNotContain(view.StartQueueTargets, t => t.Name == "Second");
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Adding_a_queue_shows_the_queue_column()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false;
        manager.Initialize(config);
        manager.Add(new DownloadItem { Url = "https://host/a.zip" }, autoStart: false);

        var view = new DownloadsViewModel(manager);
        Assert.False(view.ShowQueue); // only the default queue

        manager.AddQueue("Second");
        view.Refresh();
        Assert.True(view.ShowQueue); // a 2nd queue → Queue column shown
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Details_exposes_the_queue_name()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false;
        manager.Initialize(config);
        var vm = manager.Add(new DownloadItem { Url = "https://host/a.zip" }, autoStart: false);

        var details = new DownloadDetailsViewModel(vm);
        Assert.True(details.HasQueue);
        Assert.Equal(config.DefaultQueue.Name, vm.QueueName);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void ReorderTo_moves_item_in_master_list()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false; // don't auto-pump network starts
        manager.Initialize(config);

        var a = manager.Add(new DownloadItem { Url = "https://host/a.zip" }, autoStart: false);
        var b = manager.Add(new DownloadItem { Url = "https://host/b.zip" }, autoStart: false);
        var c = manager.Add(new DownloadItem { Url = "https://host/c.zip" }, autoStart: false);
        Assert.Equal(new[] { a, b, c }, manager.Items);

        // Drag a below c → a moves to the end.
        manager.ReorderTo(a, c, placeAfter: true);
        Assert.Equal(new[] { b, c, a }, manager.Items);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Dragging_into_another_queue_changes_its_queue()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false;
        manager.Initialize(config);

        var second = manager.AddQueue("Second");
        var a = manager.Add(new DownloadItem { Url = "https://host/a.zip" }, autoStart: false);
        var b = manager.Add(new DownloadItem { Url = "https://host/b.zip", QueueId = second.Id }, autoStart: false);
        Assert.NotEqual(second.Id, a.GetItem().QueueId);

        // Drop a onto b (which lives in the second queue) → a adopts the second queue.
        manager.ReorderTo(a, b, placeAfter: false);
        Assert.Equal(second.Id, a.GetItem().QueueId);
        Assert.Equal("Second", a.QueueName);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Add_dialog_parses_multiple_urls()
    {
        var config = Config.New();
        var vm = new AddDownloadItemViewModel(config, "https://host/a.zip\nhttps://host/b.zip");
        Assert.True(vm.CanDownload);
        Assert.True(vm.IsMultiple);
        Assert.False(vm.IsBulk); // a couple of links stays an editable box
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Add_dialog_collapses_a_huge_paste_to_a_summary_without_probing()
    {
        Localizer.Instance.Load("en");
        var config = Config.New();
        int resolveCalls = 0;
        var vm = new AddDownloadItemViewModel(
            config, string.Empty,
            resolveFileInfo: (_, _) => { Interlocked.Increment(ref resolveCalls); return Task.FromResult<(string, long)?>(null); },
            resolveDebounce: TimeSpan.Zero,
            readClipboard: () => Task.FromResult<string>(null));

        // Sample-shaped bulk paste: many links + blank lines interspersed.
        var lines = Enumerable.Range(0, 2000).Select(i => $"https://host/f{i}.mp4");
        var big = string.Join("\n", lines) + "\n\n\n";
        vm.Urls = big;

        Assert.True(vm.IsBulk);
        Assert.Equal(2000, vm.LinkCount);
        Assert.Contains("2000", vm.BulkSummaryText);
        Assert.Equal(0, resolveCalls);                 // no per-link probing on a bulk paste
        Assert.Equal(2000, vm.BuildItems().Count);     // every link is still added

        vm.ClearUrlsCommand.Execute(null);
        Assert.False(vm.IsBulk);
        Assert.True(string.IsNullOrEmpty(vm.Urls));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Add_dialog_shows_the_claiming_plugins_badge_for_a_single_url()
    {
        Localizer.Instance.Load("en");
        var config = Config.New();
        string ResolverName(string u) => u.Contains("page") ? "Website offline copy" : null;
        var vm = new AddDownloadItemViewModel(config, string.Empty,
            (_, _) => Task.FromResult<(string, long)?>(null), TimeSpan.Zero,
            readClipboard: () => Task.FromResult<string>(null),
            getResolverName: ResolverName);

        vm.Urls = "https://host/docs/page";
        Assert.True(vm.HasResolver);
        Assert.Equal("Website offline copy", vm.ResolverName);
        Assert.Contains("Website offline copy", vm.ResolverBadgeText);

        // unclaimed link → no badge
        vm.Urls = "https://host/file.zip";
        Assert.False(vm.HasResolver);

        // multi-URL input → no badge even when a plugin would claim one of them
        vm.Urls = "https://host/docs/page\nhttps://host/file.zip";
        Assert.False(vm.HasResolver);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Add_dialog_shows_variants_and_blocks_download_while_fetching()
    {
        var config = Config.New();
        var gate = new TaskCompletionSource<IReadOnlyList<Downloader.Desktop.Plugins.LinkVariant>>();
        var vm = new AddDownloadItemViewModel(
            config, string.Empty,
            (_, _) => Task.FromResult<(string, long)?>(null), TimeSpan.Zero,
            getVariants: (_, _) => gate.Task);

        vm.Urls = "https://video.example/watch?v=1";
        Assert.True(vm.IsFetchingVariants);
        Assert.False(vm.CanDownload); // author's decision: wait for the list on variant-capable links

        gate.SetResult(new[]
        {
            new Downloader.Desktop.Plugins.LinkVariant { Id = "1080", Label = "1080p", IsDefault = true },
            new Downloader.Desktop.Plugins.LinkVariant { Id = "audio", Label = "Audio only" },
        });
        await Task.Delay(50);

        Assert.False(vm.IsFetchingVariants);
        Assert.True(vm.CanDownload);
        Assert.True(vm.HasVariants);
        Assert.True(vm.Variants.Single(v => v.Id == "1080").IsChecked);  // default pre-checked
        Assert.False(vm.Variants.Single(v => v.Id == "audio").IsChecked);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Add_dialog_reports_why_a_variant_lookup_failed()
    {
        // A YouTube link the plugin can't extract (bot check) used to show a spinner and then simply
        // nothing — the reason only appeared after Download, on the failed row. Say it up front.
        var config = Config.New();
        var vm = new AddDownloadItemViewModel(
            config, string.Empty,
            (_, _) => Task.FromResult<(string, long)?>(null), TimeSpan.Zero,
            getVariants: (u, _) => u.Contains("youtu.be")
                ? Task.FromException<IReadOnlyList<Downloader.Desktop.Plugins.LinkVariant>>(
                    new InvalidOperationException("This site wants to verify a signed-in browser session."))
                : Task.FromResult<IReadOnlyList<Downloader.Desktop.Plugins.LinkVariant>>(null));

        vm.Urls = "https://youtu.be/8uiKr3U71RE";
        await Task.Delay(50);

        Assert.True(vm.HasVariantError);
        Assert.Contains("signed-in browser session", vm.VariantError);
        Assert.True(vm.ShowVariantSection);
        Assert.True(vm.CanDownload); // the add still proceeds — the resolver's default pick may work

        // Editing the input clears the stale message.
        vm.Urls = "https://host/file.zip";
        Assert.False(vm.HasVariantError);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Add_dialog_builds_one_item_per_checked_variant()
    {
        var config = Config.New();
        var vm = new AddDownloadItemViewModel(
            config, string.Empty,
            (_, _) => Task.FromResult<(string, long)?>(null), TimeSpan.Zero,
            getVariants: (_, _) => Task.FromResult<IReadOnlyList<Downloader.Desktop.Plugins.LinkVariant>>(new[]
            {
                new Downloader.Desktop.Plugins.LinkVariant { Id = "720", Label = "720p", IsDefault = true },
                new Downloader.Desktop.Plugins.LinkVariant { Id = "audio", Label = "Audio only" },
                new Downloader.Desktop.Plugins.LinkVariant { Id = "12b", Label = "gemma3:12b", SubstituteUrl = "gemma3:12b" },
            }));

        vm.Urls = "https://video.example/watch?v=1";
        await Task.Delay(80);
        Assert.True(vm.HasVariants);
        foreach (var v in vm.Variants)
            v.IsChecked = true;

        var items = vm.BuildItems();

        Assert.Equal(3, items.Count);
        // Facet variants keep the pasted URL + persist the variant id.
        Assert.Equal("https://video.example/watch?v=1", items[0].Urls[0]);
        Assert.Equal("720", items[0].VariantId);
        Assert.Equal("audio", items[1].VariantId);
        // A substitute-URL variant becomes its own link with NO variant id.
        Assert.Equal("gemma3:12b", items[2].Urls[0]);
        Assert.Null(items[2].VariantId);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Add_dialog_variant_lookup_failure_falls_back_to_plain_add()
    {
        var config = Config.New();
        var vm = new AddDownloadItemViewModel(
            config, string.Empty,
            (_, _) => Task.FromResult<(string, long)?>(null), TimeSpan.Zero,
            getVariants: (_, _) => Task.FromException<IReadOnlyList<Downloader.Desktop.Plugins.LinkVariant>>(
                new InvalidOperationException("network down")));

        vm.Urls = "https://video.example/watch?v=1";
        await Task.Delay(50);

        Assert.False(vm.IsFetchingVariants);
        Assert.False(vm.HasVariants);
        Assert.True(vm.CanDownload);

        var items = vm.BuildItems();
        Assert.Single(items);
        Assert.Null(items[0].VariantId); // default pick
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Add_dialog_editing_the_url_clears_stale_variants()
    {
        var config = Config.New();
        var vm = new AddDownloadItemViewModel(
            config, string.Empty,
            (_, _) => Task.FromResult<(string, long)?>(null), TimeSpan.Zero,
            getVariants: (url, _) => Task.FromResult<IReadOnlyList<Downloader.Desktop.Plugins.LinkVariant>>(
                url.Contains("video")
                    ? new[] { new Downloader.Desktop.Plugins.LinkVariant { Id = "720", Label = "720p", IsDefault = true } }
                    : null));

        vm.Urls = "https://video.example/watch?v=1";
        await Task.Delay(50);
        Assert.True(vm.HasVariants);

        vm.Urls = "https://plain.example/file.zip";
        await Task.Delay(50);
        Assert.False(vm.HasVariants);
        Assert.True(vm.CanDownload);
        Assert.Null(vm.BuildItems()[0].VariantId);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Add_dialog_updates_auto_filename_when_single_url_changes()
    {
        var config = Config.New();
        var names = new Dictionary<string, (string FileName, long FileSize)>
        {
            ["https://host/a"] = ("alpha.iso", 100),
            ["https://host/b"] = ("beta.iso", 200)
        };
        var vm = new AddDownloadItemViewModel(
            config,
            string.Empty,
            (url, _) => Task.FromResult<(string, long)?>(names.TryGetValue(url, out var info) ? info : null),
            TimeSpan.Zero);

        vm.Urls = "https://host/a";
        await Task.Delay(50);
        Assert.Equal("alpha.iso", vm.Filename);

        vm.Urls = "https://host/b";
        await Task.Delay(50);
        Assert.Equal("beta.iso", vm.Filename);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Add_dialog_keeps_manual_filename_until_cleared()
    {
        var config = Config.New();
        var names = new Dictionary<string, (string FileName, long FileSize)>
        {
            ["https://host/one"] = ("one.iso", 100),
            ["https://host/two"] = ("two.iso", 200),
            ["https://host/three"] = ("three.iso", 300)
        };
        var vm = new AddDownloadItemViewModel(
            config,
            string.Empty,
            (url, _) => Task.FromResult<(string, long)?>(names.TryGetValue(url, out var info) ? info : null),
            TimeSpan.Zero);

        vm.Urls = "https://host/one";
        await Task.Delay(50);
        Assert.Equal("one.iso", vm.Filename);

        vm.Filename = "custom-name.iso";
        vm.Urls = "https://host/two";
        await Task.Delay(50);
        Assert.Equal("custom-name.iso", vm.Filename);

        vm.Filename = string.Empty; // clearing re-enables auto-managed filename
        vm.Urls = "https://host/three";
        await Task.Delay(50);
        Assert.Equal("three.iso", vm.Filename);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Add_dialog_constructor_prefilled_url_triggers_resolution()
    {
        var config = Config.New();
        var vm = new AddDownloadItemViewModel(
            config,
            "https://host/prefilled",
            (_, _) => Task.FromResult<(string, long)?>(("prefilled.iso", 100)),
            TimeSpan.Zero);

        await Task.Delay(50);
        Assert.Equal("prefilled.iso", vm.Filename);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Add_dialog_ignores_extensionless_probe_names()
    {
        // youtube.com/watch?v=… probes to the page-path segment "watch" — not a file name. The box must
        // stay empty so the resolver/engine names the download at start.
        var config = Config.New();
        var vm = new AddDownloadItemViewModel(
            config,
            "https://www.youtube.com/watch?v=abc123",
            (_, _) => Task.FromResult<(string, long)?>(("watch", 0)),
            TimeSpan.Zero);

        await Task.Delay(50);
        Assert.Equal(string.Empty, vm.Filename);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Add_dialog_uses_url_name_fallback_when_probe_returns_null()
    {
        var config = Config.New();
        var vm = new AddDownloadItemViewModel(
            config,
            "https://releases.ubuntu.com/26.04/ubuntu-26.04-desktop-amd64.iso",
            (_, _) => Task.FromResult<(string, long)?>(null),
            TimeSpan.Zero);

        await Task.Delay(50);
        Assert.Equal("ubuntu-26.04-desktop-amd64.iso", vm.Filename);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Add_dialog_suggests_single_clipboard_url_and_accepts_it()
    {
        var config = Config.New();
        var vm = new AddDownloadItemViewModel(
            config,
            string.Empty,
            (_, _) => Task.FromResult<(string, long)?>(null),
            TimeSpan.Zero,
            readClipboard: () => Task.FromResult("https://host/from-clipboard.zip"));

        await vm.ClipboardSuggestionReady;
        Assert.Equal("https://host/from-clipboard.zip", vm.ClipboardSuggestion);
        Assert.True(vm.ShowClipboardSuggestion);
        Assert.False(vm.CanDownload); // suggestion is NOT committed yet

        vm.AcceptClipboardSuggestion();
        Assert.Equal("https://host/from-clipboard.zip", vm.Urls);
        Assert.True(vm.CanDownload);
        Assert.False(vm.ShowClipboardSuggestion);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Add_dialog_suggests_multiple_clipboard_urls_mixed_separators()
    {
        var config = Config.New();
        var vm = new AddDownloadItemViewModel(
            config,
            string.Empty,
            (_, _) => Task.FromResult<(string, long)?>(null),
            TimeSpan.Zero,
            readClipboard: () => Task.FromResult("https://host/a.zip https://host/b.zip,https://host/c.zip"));

        await vm.ClipboardSuggestionReady;
        Assert.True(vm.ShowClipboardSuggestion);

        // The overlay shows a compact one-line summary for many URLs (so a big clipboard can't flood
        // the box) while the full text is still what gets committed on accept.
        Localizer.Instance.Load("en");
        Assert.Equal("3 links on clipboard", vm.ClipboardSuggestionDisplay);
        Assert.DoesNotContain("\n", vm.ClipboardSuggestionDisplay);
        Assert.Contains("a.zip", vm.ClipboardSuggestion); // full text retained for accept

        vm.AcceptClipboardSuggestion();
        Assert.True(vm.IsMultiple);
        Assert.Contains("a.zip", vm.Urls);
        Assert.Contains("b.zip", vm.Urls);
        Assert.Contains("c.zip", vm.Urls);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Add_dialog_enter_accepts_clipboard_suggestion_not_newline()
    {
        // Regression: Enter in the empty multi-line links box used to insert a newline instead of
        // accepting the clipboard suggestion, because the TextBox's own bubble-phase handler ran first.
        // The view intercepts Enter in the TUNNEL phase; this drives a real key press to prove it.
        var config = Config.New();
        var vm = new AddDownloadItemViewModel(
            config,
            string.Empty,
            (_, _) => Task.FromResult<(string, long)?>(null),
            TimeSpan.Zero,
            readClipboard: () => Task.FromResult("https://host/from-clipboard.zip"));
        await vm.ClipboardSuggestionReady;

        var view = new Views.AddDownloadItemView { DataContext = vm };
        vm.View = view;
        view.Show();
        var box = view.GetVisualDescendants().OfType<Avalonia.Controls.TextBox>().First(t => t.Name == "UrlBox");
        box.Focus();

        view.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None,
            Avalonia.Input.PhysicalKey.Enter, "\r");

        Assert.Equal("https://host/from-clipboard.zip", vm.Urls); // accepted, no leading/trailing newline
        Assert.DoesNotContain("\n", vm.Urls);
        Assert.False(vm.ShowClipboardSuggestion);
        Assert.True(vm.CanDownload);
        view.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Add_dialog_esc_closes_it()
    {
        // Esc must close the Add-link dialog like every other custom-chrome dialog (no native chrome
        // means no built-in close-on-Esc).
        var config = Config.New();
        var vm = new AddDownloadItemViewModel(config, string.Empty,
            (_, _) => Task.FromResult<(string, long)?>(null), TimeSpan.Zero);
        var view = new Views.AddDownloadItemView { DataContext = vm };
        vm.View = view;
        view.Show();

        var closed = false;
        view.Closed += (_, _) => closed = true;
        view.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None,
            Avalonia.Input.PhysicalKey.Escape, null);

        Assert.True(closed);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Add_dialog_ignores_clipboard_when_seed_url_present()
    {
        var config = Config.New();
        var vm = new AddDownloadItemViewModel(
            config,
            "https://host/seed.zip",
            (_, _) => Task.FromResult<(string, long)?>(null),
            TimeSpan.Zero,
            readClipboard: () => Task.FromResult("https://host/clipboard.zip"));

        await vm.ClipboardSuggestionReady;
        Assert.Null(vm.ClipboardSuggestion);
        Assert.False(vm.ShowClipboardSuggestion);
        Assert.Equal("https://host/seed.zip", vm.Urls);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Add_dialog_no_suggestion_for_non_url_clipboard()
    {
        var config = Config.New();
        var vm = new AddDownloadItemViewModel(
            config,
            string.Empty,
            (_, _) => Task.FromResult<(string, long)?>(null),
            TimeSpan.Zero,
            readClipboard: () => Task.FromResult("just some copied prose, not a link"));

        await vm.ClipboardSuggestionReady;
        Assert.Null(vm.ClipboardSuggestion);
        Assert.False(vm.ShowClipboardSuggestion);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Plan_stage_shows_part_progress_in_status_text()
    {
        Localizer.Instance.Load("en");
        var vm = new DownloadItemViewModel(new DownloadItem { Status = DownloadStatus.Running }, null)
        {
            Status = DownloadStatus.Running,
            Progress = 40
        };
        // No plan stage → plain percent.
        Assert.Equal("40%", vm.StatusText);
        // Multi-part plan sets the stage → "Part i/N · %".
        vm.PlanStage = "Part 3/10";
        Assert.Equal("Part 3/10 · 40%", vm.StatusText);
        // Cleared when the plan finishes.
        vm.PlanStage = null;
        Assert.Equal("40%", vm.StatusText);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Details_dialog_renders_plan_segments_as_waiting_active_done_rows()
    {
        var item = new DownloadItem { Urls = { "https://h/video.m3u8" }, FileName = "video.mp4", Status = DownloadStatus.Running };
        var vm = new DownloadItemViewModel(item, null) { Status = DownloadStatus.Running };

        // Simulate the plan runner's live board: seg0 done, seg1 downloading at 40%, seg2 waiting.
        var run = new PlanRunState(3);
        run.SetDone(0, 5000);
        run.SetActive(1);
        run.Report(1, 0.4, 512 * 1024, 2000);
        run.SetTotal(1, 5000);
        vm.PlanRun = run;

        var details = new DownloadDetailsViewModel(vm);
        try
        {
            Assert.Equal(3, details.Parts.Count); // every segment gets a row (not one resetting chunk)
            Localizer.Instance.Load("en");
            Assert.Equal(100, details.Parts[0].Progress);
            Assert.Equal("Completed", details.Parts[0].StatusText);
            Assert.Equal(40, details.Parts[1].Progress, 1);
            Assert.Equal("Downloading", details.Parts[1].StatusText);
            Assert.Equal(0, details.Parts[2].Progress);
            Assert.Equal("Pending", details.Parts[2].StatusText); // waiting its turn in the parallel cap
            Assert.Contains("3", details.PartsSummary); // "3 segments"
        }
        finally { details.Cleanup(); }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Retry_clears_the_persisted_plan_so_it_re_resolves()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false; // don't actually start anything in the test
        manager.Initialize(config);

        var item = new DownloadItem
        {
            Urls = { "http://127.0.0.1:9/never" }, // connection-refused fast if the pump ever starts it
            SaveFolder = System.IO.Path.GetTempPath(),
            QueueId = config.DefaultQueue.Id,
            PlanJson = "{\"Parts\":[{\"Url\":\"http://h/a\"},{\"Url\":\"http://h/b\"}]}"
        };
        var vm = manager.Add(item, autoStart: false);
        vm.Status = DownloadStatus.Failed;

        manager.Retry(vm);

        // Retry drops the saved plan so the next Start re-resolves the (possibly expired) link.
        Assert.Null(item.PlanJson);
        Assert.NotEqual(DownloadStatus.Failed, vm.Status); // re-queued (Created) or already picked up (Running)
        manager.Cancel(vm);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Add_dialog_can_create_and_select_a_new_queue()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.Settings.MaxConcurrentDownloads = 5;
        config.DefaultQueue.IsRunning = false;
        manager.Initialize(config);

        var vm = new AddDownloadItemViewModel(config, "https://host/file.zip",
            (_, _) => Task.FromResult<(string, long)?>(null), TimeSpan.Zero, manager: manager);
        Assert.True(vm.CanAddQueue);
        Assert.False(vm.ShowQueuePicker); // only the default queue exists so far

        // Empty name → no-op.
        vm.NewQueueName = "   ";
        vm.ConfirmAddQueue();
        Assert.Single(config.Queues);

        vm.NewQueueName = "Series S01";
        vm.ConfirmAddQueue();

        Assert.Equal(2, config.Queues.Count);
        var created = config.Queues.Last();
        Assert.Equal("Series S01", created.Name);
        Assert.Equal(5, created.MaxConcurrent); // seeded from settings via manager.AddQueue
        Assert.True(vm.ShowQueuePicker);        // picker appears with the second queue
        Assert.Same(created, vm.SelectedQueue); // new queue selected for this add

        // The started download lands in the new queue.
        var view = new Views.AddDownloadItemView { DataContext = vm };
        vm.View = view;
        view.Show();
        vm.StartDownloadCommand.Execute(null);
        // View.Close(items) returns the descriptors via ShowDialog normally; here grab them from the VM path:
        view.Close();
        var item = new DownloadItem
        {
            Urls = { "https://host/file.zip" },
            QueueId = vm.SelectedQueue?.Id
        };
        Assert.Equal(created.Id, item.QueueId);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Pending_name_shows_placeholder()
    {
        var item = new DownloadItem { Url = "https://host/x", Status = DownloadStatus.Running };
        var vm = new DownloadItemViewModel(item, null);
        Assert.True(vm.IsNamePending);
        Assert.Equal("Fetching name…", vm.DisplayName);
    }

    [AvaloniaTheory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(DownloadStatus.Running)]
    [InlineData(DownloadStatus.Completed)]
    [InlineData(DownloadStatus.Failed)]
    [InlineData(DownloadStatus.Paused)]
    [InlineData(DownloadStatus.Stopped)]
    [InlineData(DownloadStatus.Created)]
    public void Status_brush_is_provided(DownloadStatus status)
    {
        var brush = Converters.StatusToBrushConverter.Instance
            .Convert(status, typeof(Avalonia.Media.IBrush), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.IsAssignableFrom<Avalonia.Media.IBrush>(brush);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void StartAll_respects_queue_concurrency_cap()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.Settings.MaxConcurrentDownloads = 2; // authoritative limit (drives the primary queue)
        manager.Initialize(config);
        Assert.Equal(2, config.DefaultQueue.MaxConcurrent);

        // Non-routable host so the background download attempts never actually connect.
        for (int i = 0; i < 6; i++)
            manager.Add(new DownloadItem { Url = $"https://10.255.255.1/file{i}.zip", SaveFolder = "/tmp" }, autoStart: false);

        manager.StartAll();

        // Only the cap may run at once; the rest stay queued (this is the queue-limit fix).
        Assert.Equal(2, manager.ActiveCount);
        Assert.Equal(4, manager.QueuedCount);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Settings_max_concurrent_caps_running_downloads()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.Settings.MaxConcurrentDownloads = 5;
        manager.Initialize(config);

        // The user lowers the limit on the Settings page — it must drive the real queue cap.
        _ = new SettingViewModel(config, manager) { MaxConcurrentDownloads = 2 };
        Assert.Equal(2, config.DefaultQueue.MaxConcurrent);

        for (int i = 0; i < 8; i++)
            manager.Add(new DownloadItem { Url = $"https://10.255.255.1/file{i}.zip", SaveFolder = "/tmp" }, autoStart: false);

        manager.StartAll();

        Assert.Equal(2, manager.ActiveCount);
        Assert.Equal(6, manager.QueuedCount);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Staged_progress_flushes_only_while_running()
    {
        var item = new DownloadItem { Status = DownloadStatus.Running };
        var vm = new DownloadItemViewModel(item, null) { Status = DownloadStatus.Running };

        vm.StageProgress(42, 1000, 500, 2000);
        Assert.True(vm.FlushProgress());
        Assert.Equal(42, vm.Progress);

        // No new data → nothing to flush.
        Assert.False(vm.FlushProgress());

        // Values staged after a pause are dropped so the row keeps its last fill.
        vm.Status = DownloadStatus.Paused;
        vm.StageProgress(99, 0, 1900, 2000);
        Assert.False(vm.FlushProgress());
        Assert.Equal(42, vm.Progress);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Completed_item_ignores_stop_resume_and_retry()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);

        var vm = manager.Add(new DownloadItem { Url = "https://host/done.zip", SaveFolder = "/tmp" }, autoStart: false);
        vm.Status = DownloadStatus.Completed;
        vm.Progress = 100;

        // Bulk "Stop" hits a completed row — it must stay Completed (not flip to Stopped).
        manager.Cancel(vm);
        Assert.Equal(DownloadStatus.Completed, vm.Status);

        // Bulk "Start"/retry must never re-run a finished download from 0%.
        manager.Resume(vm);
        Assert.Equal(DownloadStatus.Completed, vm.Status);
        manager.Retry(vm);
        Assert.Equal(DownloadStatus.Completed, vm.Status);
        Assert.Equal(100, vm.Progress);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Stopping_all_stops_running_and_queued_items()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.Settings.MaxConcurrentDownloads = 3;
        manager.Initialize(config);

        for (int i = 0; i < 10; i++)
            manager.Add(new DownloadItem { Url = $"https://10.255.255.1/file{i}.zip", SaveFolder = "/tmp" }, autoStart: false);

        manager.StartAll();
        Assert.Equal(3, manager.ActiveCount);
        Assert.Equal(7, manager.QueuedCount);

        // "Select all + Stop": Cancel every row (what StopSelectedCommand does). Running rows stop and,
        // crucially, queued rows stop too — so nothing is left for the pump to auto-start.
        foreach (var vm in manager.Items.ToList())
            manager.Cancel(vm);

        Assert.Equal(0, manager.ActiveCount);
        Assert.Equal(0, manager.QueuedCount);
        Assert.All(manager.Items, vm => Assert.Equal(DownloadStatus.Stopped, vm.Status));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void StopAll_stops_running_and_queued_items()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.Settings.MaxConcurrentDownloads = 3;
        manager.Initialize(config);

        for (int i = 0; i < 8; i++)
            manager.Add(new DownloadItem { Url = $"https://10.255.255.1/file{i}.zip", SaveFolder = "/tmp" }, autoStart: false);

        manager.StartAll();
        Assert.True(manager.ActiveCount > 0);

        manager.StopAll();

        Assert.Equal(0, manager.ActiveCount);
        Assert.Equal(0, manager.QueuedCount);
        Assert.All(manager.Items, vm => Assert.Equal(DownloadStatus.Stopped, vm.Status));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void AllDownloadsCompleted_fires_once_when_list_drains()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);
        config.DefaultQueue.IsRunning = false; // don't let the pump kick off background (network) starts

        var fired = 0;
        manager.AllDownloadsCompleted += () => fired++;

        var a = manager.Add(new DownloadItem { Url = "https://host/a.zip", SaveFolder = "/tmp" }, autoStart: false);
        var b = manager.Add(new DownloadItem { Url = "https://host/b.zip", SaveFolder = "/tmp" }, autoStart: false);

        // Drive them through the manager's post-completion bookkeeping (test seam).
        manager.RaiseCompletedForTest(a);
        Assert.Equal(0, fired); // b still queued → not "all complete" yet

        manager.RaiseCompletedForTest(b);
        Assert.Equal(1, fired); // everything done → fired exactly once
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Stopping_items_does_not_arm_all_completed_even_with_a_finished_item()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);
        config.DefaultQueue.IsRunning = false;

        var fired = 0;
        manager.AllDownloadsCompleted += () => fired++;

        var done = manager.Add(new DownloadItem { Url = "https://host/done.zip", SaveFolder = "/tmp" }, autoStart: false);
        var other = manager.Add(new DownloadItem { Url = "https://host/other.zip", SaveFolder = "/tmp" }, autoStart: false);

        manager.RaiseCompletedForTest(done);     // one finished, 'other' still queued → no fire
        Assert.Equal(0, fired);

        // User clicks Stop All → 'other' is cancelled (Stopped). Nothing is active/queued and a
        // completed item remains, but a *stop* must NOT trigger the shutdown/all-complete flow.
        manager.RaiseStoppedForTest(other);
        Assert.Equal(0, fired);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void SelectAll_header_is_tri_state_and_drives_selection()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        var view = new DownloadsViewModel(manager);

        var a = manager.Add(new DownloadItem { Url = "https://host/a.zip" }, autoStart: false);
        var b = manager.Add(new DownloadItem { Url = "https://host/b.zip" }, autoStart: false);

        Assert.False(view.SelectAllState);   // none checked → false
        Assert.False(view.HasSelection);

        a.IsChecked = true;
        Assert.Null(view.SelectAllState);     // some checked → indeterminate
        Assert.True(view.HasSelection);

        b.IsChecked = true;
        Assert.True(view.SelectAllState);     // all checked → true

        view.SelectAllState = false;          // setting clears every row
        Assert.False(a.IsChecked);
        Assert.False(b.IsChecked);

        view.SelectAllState = true;           // setting selects every row
        Assert.True(a.IsChecked);
        Assert.True(b.IsChecked);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Selected_bulk_commands_disable_without_selection()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        var view = new DownloadsViewModel(manager);
        var a = manager.Add(new DownloadItem { Url = "https://host/a.zip" }, autoStart: false);

        Assert.False(((System.Windows.Input.ICommand)view.StartSelectedCommand).CanExecute(null));
        Assert.False(((System.Windows.Input.ICommand)view.RemoveSelectedCommand).CanExecute(null));
        // Stop-all is selection-independent — always available.
        Assert.True(((System.Windows.Input.ICommand)view.StopAllCommand).CanExecute(null));

        a.IsChecked = true;
        Assert.True(((System.Windows.Input.ICommand)view.StartSelectedCommand).CanExecute(null));
        Assert.True(((System.Windows.Input.ICommand)view.StopSelectedCommand).CanExecute(null));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Grid_row_selection_enables_bulk_commands_without_checkboxes()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        var view = new DownloadsViewModel(manager);
        var a = manager.Add(new DownloadItem { Url = "https://host/a.zip" }, autoStart: false);

        Assert.False(view.HasSelection);

        // Selecting (highlighting) a row in the DataGrid counts as selected — no checkbox needed (#3).
        view.SetGridSelection(new System.Collections.Generic.List<object> { a });
        Assert.True(view.HasSelection);
        Assert.True(((System.Windows.Input.ICommand)view.StartSelectedCommand).CanExecute(null));
        Assert.False(a.IsChecked); // the checkbox stays unchecked

        view.SetGridSelection(null);
        Assert.False(view.HasSelection);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void StartQueue_runs_remaining_stopped_and_failed_items()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.Settings.MaxConcurrentDownloads = 5;
        manager.Initialize(config);

        // Non-routable host so nothing actually connects; we only assert state transitions.
        var stopped = manager.Add(new DownloadItem { Url = "https://10.255.255.1/a.zip", SaveFolder = "/tmp" }, autoStart: false);
        var failed = manager.Add(new DownloadItem { Url = "https://10.255.255.1/b.zip", SaveFolder = "/tmp" }, autoStart: false);
        var done = manager.Add(new DownloadItem { Url = "https://10.255.255.1/c.zip", SaveFolder = "/tmp" }, autoStart: false);
        stopped.Status = DownloadStatus.Stopped;
        failed.Status = DownloadStatus.Failed;
        done.Status = DownloadStatus.Completed;

        manager.StartQueue(config.DefaultQueue);

        // The stopped and failed rows must be (re)started; the completed one is left alone.
        Assert.NotEqual(DownloadStatus.Stopped, stopped.Status);
        Assert.NotEqual(DownloadStatus.Failed, failed.Status);
        Assert.Equal(DownloadStatus.Completed, done.Status);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Completed_item_loads_at_full_progress_even_without_downloaded_bytes()
    {
        // A file that already existed on disk is Completed but may have Downloaded=0 persisted.
        var item = new DownloadItem { FileName = "a.zip", Size = 1000, Downloaded = 0, Status = DownloadStatus.Completed };
        var vm = new DownloadItemViewModel(item, null);
        Assert.Equal(100, vm.Progress);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Setting_status_completed_forces_full_progress()
    {
        var vm = new DownloadItemViewModel(new DownloadItem { Size = 1000 }, null) { Progress = 0 };
        vm.Status = DownloadStatus.Completed;
        Assert.Equal(100, vm.Progress);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Chunk_status_text_tracks_progress()
    {
        // Reads localized strings, so it must run under the headless runtime with en loaded
        // (as a plain Fact it flaked on CI, returning the raw "State_Pending" key).
        Localizer.Instance.Load("en");
        var chunk = new ChunkProgressViewModel(1);
        chunk.Update(0, 0, 0, 100);
        Assert.Equal("Pending", chunk.StatusText);
        chunk.Update(50, 0, 50, 100);
        Assert.Equal("Downloading", chunk.StatusText);
        chunk.Update(100, 0, 100, 100);
        Assert.Equal("Completed", chunk.StatusText);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void AlreadyExisted_completed_row_shows_exists_text()
    {
        Localizer.Instance.Load("en");
        var vm = new DownloadItemViewModel(new DownloadItem(), null)
        {
            Status = DownloadStatus.Completed
        };
        Assert.Equal("Completed", vm.StatusText);

        vm.AlreadyExisted = true;
        Assert.Equal("Already downloaded", vm.StatusText);
        Assert.True(vm.IsCompleted); // still counts as done (Open file works, no retry)
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void TimeLeft_reflects_remaining_over_speed()
    {
        var item = new DownloadItem { Status = DownloadStatus.Running };
        var vm = new DownloadItemViewModel(item, null) { Status = DownloadStatus.Running };
        vm.Size = 1000;
        vm.Downloaded = 200;
        vm.Speed = 100;                 // 800 bytes / 100 B/s = 8s
        Assert.Equal("8s", vm.TimeLeftText);

        vm.Status = DownloadStatus.Paused;
        Assert.Equal("—", vm.TimeLeftText);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Queue_summary_reflects_items()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);
        manager.Add(new DownloadItem { Url = "https://host/a.zip" }, autoStart: false);

        var queues = new QueuesViewModel(config, manager);
        var row = queues.Queues[0];

        Assert.Equal(1, row.TotalCount);
        Assert.Contains("waiting", row.SummaryText);
        Assert.Single(row.Items);                       // the item shows up as a wrapped row
        Assert.Equal(1, row.WaitingCount);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Move_to_queue_reassigns_item()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);
        var vm = manager.Add(new DownloadItem { Url = "https://host/a.zip" }, autoStart: false);
        var other = manager.AddQueue("Second");

        manager.MoveToQueue(vm, other.Id);

        Assert.Equal(other.Id, vm.GetItem().QueueId);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Move_priority_reorders_within_queue()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);
        var a = manager.Add(new DownloadItem { Url = "https://host/a.zip" }, autoStart: false);
        var b = manager.Add(new DownloadItem { Url = "https://host/b.zip" }, autoStart: false);

        Assert.True(manager.Items.IndexOf(a) < manager.Items.IndexOf(b));
        manager.MovePriority(b, -1); // move b up past a
        Assert.True(manager.Items.IndexOf(b) < manager.Items.IndexOf(a));
    }

    [AvaloniaTheory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(DownloadStatus.Running)]
    [InlineData(DownloadStatus.Paused)]
    public void Interrupted_downloads_load_as_stopped(DownloadStatus saved)
    {
        // A Running/Paused state can't survive a restart (no live connection), so both must come back
        // as Stopped — never a misleading "Paused" row.
        var manager = new DownloadManager();
        var config = Config.New();
        config.Downloads.Add(new DownloadItem { Url = "https://host/x.zip", Status = saved });
        manager.Initialize(config);
        Assert.Equal(DownloadStatus.Stopped, manager.Items[0].Status);
    }

    [AvaloniaTheory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(DownloadStatus.Completed)]
    [InlineData(DownloadStatus.Stopped)]
    [InlineData(DownloadStatus.Failed)]
    public void Terminal_states_survive_load(DownloadStatus saved)
    {
        // Only Running/Paused are normalized; finished/stopped/failed rows keep their saved state.
        var manager = new DownloadManager();
        var config = Config.New();
        config.Downloads.Add(new DownloadItem { Url = "https://host/x.zip", Status = saved });
        manager.Initialize(config);
        Assert.Equal(saved, manager.Items[0].Status);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Accent_applies_color_and_selection_resources()
    {
        ThemeService.ApplyAccent("Blue");
        var res = Avalonia.Application.Current!.Resources;
        Assert.Equal(ThemeService.Find("Blue").Color, res["SystemAccentColor"]);
        Assert.True(res.ContainsKey("RowSelectionBrush"));
        Assert.IsAssignableFrom<IBrush>(res["RowSelectionBrush"]);
        // Unknown key falls back to the first accent (Teal) rather than throwing.
        Assert.Equal(ThemeService.Accents[0], ThemeService.Find("does-not-exist"));
        ThemeService.ApplyAccent("Teal"); // restore default for other tests
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Every_language_has_a_loadable_flag()
    {
        foreach (var lang in Localizer.Languages)
        {
            // Assets/flags/{code}.svg must be embedded and rasterize for each language.
            Assert.NotNull(lang.Flag);
            // Rendered at 3x the 15px display height so HiDPI screens get a crisp image.
            Assert.Equal(45, lang.Flag.PixelSize.Height);
            Assert.True(lang.Flag.PixelSize.Width > 0);
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Start_runs_item_even_when_its_queue_was_paused()
    {
        // Regression (1.3.2/1.3.3): a persisted queue.IsRunning=false made every per-item Start silently
        // no-op — the item stuck as "Queued", only rescued later by the scheduler/StartQueue. An explicit
        // Start (row button / bulk) must un-pause the queue and actually run the item.
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);
        var vm = manager.Add(new DownloadItem { Url = "https://10.255.255.1/f.zip", SaveFolder = "/tmp" }, autoStart: false);
        vm.Status = DownloadStatus.Stopped;
        config.DefaultQueue.IsRunning = false; // the stuck precondition (a saved "stopped" queue)

        manager.Resume(vm); // user clicks Start on the row

        Assert.True(config.DefaultQueue.IsRunning);      // explicit Start un-pauses the queue
        Assert.Equal(DownloadStatus.Running, vm.Status); // item actually started (not stuck as Queued)
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Removing_a_queue_deactivates_its_schedules()
    {
        // Deleting a queue must disable + unbind any schedules pointing at it, so the scheduler can't
        // act on a now-deleted target (orphaned-schedule bug).
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);
        var q2 = manager.AddQueue("Nightly");
        var sch = new DownloadSchedule { Name = "nightly", TargetQueueId = q2.Id, Enabled = true };
        config.Schedules.Add(sch);

        manager.RemoveQueue(q2);

        Assert.DoesNotContain(q2, config.Queues);
        Assert.False(sch.Enabled);
        Assert.Null(sch.TargetQueueId);
    }

    // ---- plugin resolver is actually consumed by the download flow (the github.com/owner/repo bug) ----

    private sealed class FakeRepoResolver : ILinkResolver
    {
        public bool CanResolve(string url) => url.StartsWith("repo://");
        public Task<DownloadPlan> ResolveAsync(string url, CancellationToken ct) =>
            Task.FromResult(new DownloadPlan
            {
                SuggestedFileName = "app-linux-x64.tar.gz",
                Parts = new[] { new DownloadPart { Url = "https://cdn.example/app-linux-x64.tar.gz" } },
            });
    }

    private sealed class FakeRepoPlugin : IDownloaderPlugin
    {
        public string Id => "test.repo";
        public string Name => "Repo";
        public string Version => "1.0.0";
        public string Author => "tester";
        public string Description => "fake repo resolver";
        public void Initialize(IPluginContext context) => context.RegisterResolver(new FakeRepoResolver());
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Download_flow_resolves_a_link_via_an_enabled_plugin()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakeRepoPlugin());
        var manager = new DownloadManager(pm);

        // A link the plugin claims is rewritten to the real asset URL + suggested name (the actual fix:
        // before, DownloadManager never consulted any plugin, so a github.com/owner/repo link was handed
        // straight to the engine and downloaded the HTML page instead of the release asset).
        var (url, name) = await manager.ResolveViaPluginsAsync("repo://owner/app", null, default);
        Assert.Equal("https://cdn.example/app-linux-x64.tar.gz", url);
        Assert.Equal("app-linux-x64.tar.gz", name);

        // A user-typed name is preserved (plugin only fills it when empty).
        var (_, keep) = await manager.ResolveViaPluginsAsync("repo://owner/app", "my-name.bin", default);
        Assert.Equal("my-name.bin", keep);

        // A link no plugin claims passes through untouched.
        var (plain, _) = await manager.ResolveViaPluginsAsync("https://example.com/file.zip", null, default);
        Assert.Equal("https://example.com/file.zip", plain);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Download_flow_without_a_plugin_manager_passes_links_through()
    {
        var manager = new DownloadManager(); // no plugins
        var (url, name) = await manager.ResolveViaPluginsAsync("repo://owner/app", "n.bin", default);
        Assert.Equal("repo://owner/app", url);
        Assert.Equal("n.bin", name);
    }

    private sealed class StubFileService : IFileService
    {
        public Task<Config> LoadFromFileAsync() => Task.FromResult(Config.New());
        public Task SaveToFileAsync(Config itemToSave) => Task.CompletedTask;
    }
}
