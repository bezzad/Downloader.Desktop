using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The sidebar's Queues section and the single selection it shares with Categories, the section
/// headers, the accent bar on the selected row, and dragging a category to reorder it.
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

    private static (MainViewModel main, DownloadManager manager) Build(Config config = null)
    {
        Localizer.Instance.Load("en");
        if (config is null)
        {
            config = Config.New();
            config.Categories = CategoryService.CreateDefaults(key => key);
        }

        var manager = new DownloadManager();
        var main = new MainViewModel(new StubFileService(config), manager);
        manager.Initialize(config);
        Dispatcher.UIThread.RunJobs();
        return (main, manager);
    }

    private static DownloadItemViewModel Add(DownloadManager manager, string name, DownloadQueue queue,
        DownloadStatus status = DownloadStatus.Stopped)
    {
        var vm = manager.Add(new DownloadItem
        {
            Url = "https://10.255.255.1/" + name, FileName = name, QueueId = queue.Id
        }, autoStart: false);
        vm.Status = status;
        return vm;
    }

    private static string[] Listed(MainViewModel main) =>
        main.Downloads.ItemsView.Cast<DownloadItemViewModel>().Select(i => i.FileName).OrderBy(n => n).ToArray();

    private static void Run(System.Windows.Input.ICommand command) => command.Execute(null);

    // ---- queue filter + single selection ------------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Selecting_a_queue_lists_only_its_downloads_whatever_their_state()
    {
        var (main, manager) = Build();
        var main1 = manager.Queues[0];
        var night = manager.AddQueue("Night");
        Dispatcher.UIThread.RunJobs();
        Add(manager, "a.mp4", night, DownloadStatus.Running);
        Add(manager, "b.zip", night, DownloadStatus.Completed);
        Add(manager, "c.mp3", main1, DownloadStatus.Stopped);

        Run(main.QueueRows.Single(r => r.Name == "Night").SelectCommand);

        Assert.Equal(new[] { "a.mp4", "b.zip" }, Listed(main));
        Assert.Equal(night.Id, main.SelectedQueueId);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_queue_filter_combines_with_status_and_search()
    {
        var (main, manager) = Build();
        var night = manager.AddQueue("Night");
        Dispatcher.UIThread.RunJobs();
        Add(manager, "movie.mp4", night, DownloadStatus.Completed);
        Add(manager, "music.mp3", night, DownloadStatus.Completed);
        Add(manager, "movie2.mp4", night, DownloadStatus.Failed);
        Add(manager, "movie3.mp4", manager.Queues[0], DownloadStatus.Completed);

        Run(main.QueueRows.Single(r => r.Name == "Night").SelectCommand);
        Run(main.ShowCompletedCommand);
        main.SearchText = "movie";

        Assert.Equal(new[] { "movie.mp4" }, Listed(main));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Only_one_sidebar_row_is_selected_across_both_sections()
    {
        var (main, manager) = Build();
        var night = manager.AddQueue("Night");
        Dispatcher.UIThread.RunJobs();
        Add(manager, "a.mp4", night);
        Add(manager, "b.mp4", manager.Queues[0]);
        var video = main.CategoryRows.Single(r => r.Id == "video");
        var nightRow = main.QueueRows.Single(r => r.Id == night.Id);

        Run(video.SelectCommand);
        Run(nightRow.SelectCommand);

        Assert.Null(main.SelectedCategoryId);
        Assert.Equal(new[] { "a.mp4" }, Listed(main));
        Assert.Single(main.CategoryRows.Where(r => r.IsSelected).Cast<object>()
            .Concat(main.QueueRows.Where(r => r.IsSelected)), r => ReferenceEquals(r, nightRow));

        // A category clears the queue again.
        Run(video.SelectCommand);
        Assert.Null(main.SelectedQueueId);
        Assert.False(nightRow.IsSelected);
        Assert.True(video.IsSelected);
        Assert.Equal(new[] { "a.mp4", "b.mp4" }, Listed(main));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void All_clears_the_queue_but_keeps_status_and_search()
    {
        var (main, manager) = Build();
        var night = manager.AddQueue("Night");
        Dispatcher.UIThread.RunJobs();
        Add(manager, "movie.mp4", night, DownloadStatus.Completed);
        Add(manager, "movie2.mp4", manager.Queues[0], DownloadStatus.Completed);
        Add(manager, "movie3.mp4", manager.Queues[0], DownloadStatus.Failed);

        Run(main.QueueRows.Single(r => r.Id == night.Id).SelectCommand);
        Run(main.ShowCompletedCommand);
        main.SearchText = "movie";
        Run(main.CategoryRows[0].SelectCommand); // "All"

        Assert.Null(main.SelectedQueueId);
        Assert.True(main.CategoryRows[0].IsSelected);
        Assert.Equal(new[] { "movie.mp4", "movie2.mp4" }, Listed(main));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Queue_counts_respect_the_status_filter()
    {
        var (main, manager) = Build();
        var night = manager.AddQueue("Night");
        Dispatcher.UIThread.RunJobs();
        Add(manager, "a.mp4", night, DownloadStatus.Failed);
        Add(manager, "b.mp4", night, DownloadStatus.Completed);
        Add(manager, "c.mp4", night, DownloadStatus.Failed);
        var nightRow = main.QueueRows.Single(r => r.Id == night.Id);
        var mainRow = main.QueueRows.Single(r => r.Id == manager.Queues[0].Id);

        Assert.Equal(3, nightRow.Count);
        Run(main.ShowFailedCommand);
        Assert.Equal(2, nightRow.Count);
        Assert.Equal(0, mainRow.Count);
        Assert.True(mainRow.IsEmpty);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Queue_rows_follow_added_renamed_and_removed_queues()
    {
        var (main, manager) = Build();
        Assert.Single(main.QueueRows);

        var night = manager.AddQueue("Night");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { manager.Queues[0].Name, "Night" }, main.QueueRows.Select(r => r.Name));

        manager.RenameQueue(night, "Late");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Late", main.QueueRows[1].Name);

        manager.RemoveQueue(night);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(main.QueueRows);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Removing_the_selected_queue_falls_back_to_All()
    {
        var (main, manager) = Build();
        var night = manager.AddQueue("Night");
        Dispatcher.UIThread.RunJobs();
        Add(manager, "a.mp4", night);
        Add(manager, "b.mp4", manager.Queues[0]);
        Run(main.QueueRows.Single(r => r.Id == night.Id).SelectCommand);

        manager.RemoveQueue(night);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(main.SelectedQueueId);
        Assert.True(main.CategoryRows[0].IsSelected);
        Assert.Equal(2, Listed(main).Length);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Clear_filters_resets_the_queue_selection()
    {
        var (main, manager) = Build();
        var night = manager.AddQueue("Night");
        Dispatcher.UIThread.RunJobs();
        Add(manager, "a.mp4", night);
        Add(manager, "b.mp4", manager.Queues[0]);
        var nightRow = main.QueueRows.Single(r => r.Id == night.Id);
        Run(nightRow.SelectCommand);

        // Drive the COMMAND the empty state uses, not the method.
        Run(main.ClearFiltersCommand);

        Assert.Null(main.SelectedQueueId);
        Assert.False(nightRow.IsSelected);
        Assert.True(main.CategoryRows[0].IsSelected);
        Assert.Equal(2, Listed(main).Length);
    }

    // ---- the view: sections, accent bar, drag ---------------------------------------------------

    private static (Window window, MainViewModel main, DownloadManager manager) Show()
    {
        var (main, manager) = Build();
        manager.AddQueue("Night");
        Dispatcher.UIThread.RunJobs();
        var window = new MainWindow { DataContext = main, Width = 1100, Height = 750 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, main, manager);
    }

    private static Button[] Rows(Window window, string listName) =>
        window.FindControl<ItemsControl>(listName)!.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("cat")).ToArray();

    private static Border Bar(Button row) =>
        row.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("accentbar"));

    private static PathIcon Grip(Button row) =>
        row.GetVisualDescendants().OfType<PathIcon>().SingleOrDefault(p => p.Classes.Contains("grip"));

    private static Point Center(Visual v, Visual relativeTo) =>
        v.TranslatePoint(new Point(v.Bounds.Width / 2, v.Bounds.Height / 2), relativeTo)!.Value;

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Both_sections_start_expanded_and_collapsing_one_hides_its_rows()
    {
        var (window, main, _) = Show();
        Assert.True(Rows(window, "QueueList").All(r => r.IsEffectivelyVisible));
        Assert.True(Rows(window, "CategoryList").All(r => r.IsEffectivelyVisible));

        Run(main.ToggleQueuesSectionCommand);
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.FindControl<ItemsControl>("QueueList")!.IsVisible);
        Assert.True(Rows(window, "CategoryList").All(r => r.IsEffectivelyVisible));

        Run(main.ToggleCategoriesSectionCommand);
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.FindControl<StackPanel>("CategoriesSection")!.IsVisible);
        window.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Only_the_selected_row_shows_the_accent_bar()
    {
        var (window, main, _) = Show();
        // "All" starts selected.
        var all = Rows(window, "CategoryList").Concat(Rows(window, "QueueList")).ToArray();
        Assert.Single(all, r => Bar(r).IsVisible);
        Assert.True(Bar(all[0]).IsVisible);

        Run(main.QueueRows[1].SelectCommand);
        Dispatcher.UIThread.RunJobs();
        var queueRows = Rows(window, "QueueList");
        Assert.True(Bar(queueRows[1]).IsVisible);
        Assert.Single(Rows(window, "CategoryList").Concat(queueRows), r => Bar(r).IsVisible);
        window.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_accent_bar_follows_the_accent_picker()
    {
        var (window, _, _) = Show();
        var bar = Bar(Rows(window, "CategoryList")[0]);
        try
        {
            ThemeService.ApplyAccent("Purple");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ThemeService.Find("Purple").Color, Assert.IsAssignableFrom<ISolidColorBrush>(bar.Background).Color);

            ThemeService.ApplyAccent("Blue");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ThemeService.Find("Blue").Color, Assert.IsAssignableFrom<ISolidColorBrush>(bar.Background).Color);
        }
        finally
        {
            ThemeService.ApplyAccent("Teal"); // the default, for other tests
            window.Close();
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_accent_bar_sits_on_the_start_edge_in_both_directions()
    {
        var (window, _, _) = Show();
        var row = Rows(window, "CategoryList")[0];
        Assert.True(Center(Bar(row), row).X < row.Bounds.Width / 4);

        window.FlowDirection = FlowDirection.RightToLeft;
        Dispatcher.UIThread.RunJobs();
        row = Rows(window, "CategoryList")[0];
        // Position as drawn on screen: the RTL mirror is a render transform, so read the screen point.
        var barScreen = Bar(row).PointToScreen(new Point(Bar(row).Bounds.Width / 2, 0)).X;
        var rowLeft = row.PointToScreen(new Point(0, 0)).X;
        var rowRight = row.PointToScreen(new Point(row.Bounds.Width, 0)).X;
        var (lo, hi) = (System.Math.Min(rowLeft, rowRight), System.Math.Max(rowLeft, rowRight));
        Assert.True(barScreen > lo + (hi - lo) * 3 / 4, $"bar at {barScreen}, row {lo}..{hi}");
        window.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_grip_shows_on_a_hovered_category_but_not_on_All_or_queues()
    {
        var (window, _, _) = Show();
        var categories = Rows(window, "CategoryList");
        var video = categories[1];
        Assert.False(Grip(video).IsVisible);

        window.MouseMove(Center(video, window));
        Dispatcher.UIThread.RunJobs();
        Assert.True(Grip(video).IsVisible);

        window.MouseMove(Center(categories[0], window));
        Dispatcher.UIThread.RunJobs();
        Assert.False(Grip(categories[0]).IsVisible);
        Assert.False(Grip(video).IsVisible);

        // Queue rows have no grip at all: they are not draggable.
        Assert.All(Rows(window, "QueueList"), r => Assert.Null(Grip(r)));
        window.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Dragging_a_category_by_its_grip_reorders_the_categories()
    {
        var (window, main, manager) = Show();
        var before = manager.Categories.Categories.Select(c => c.Id).ToList();
        var rows = Rows(window, "CategoryList");
        var third = rows[3]; // category index 2
        var first = rows[1]; // category index 0

        window.MouseMove(Center(third, window));
        Dispatcher.UIThread.RunJobs();
        var grip = Center(Grip(third), window);
        window.MouseDown(grip, MouseButton.Left);
        window.MouseMove(Center(first, window));
        window.MouseUp(Center(first, window), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var after = manager.Categories.Categories.Select(c => c.Id).ToList();
        Assert.Equal(before[2], after[0]);
        Assert.Equal(before[0], after[1]);
        Assert.Equal(before[2], main.CategoryRows[1].Id);
        // Grabbing the grip must not also select the row.
        Assert.True(main.CategoryRows[0].IsSelected);
        window.Close();
    }
}
