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
/// The sidebar as drawn: section collapse, the accent bar on the one selected row, the hover drag
/// handle on category rows, and a real pointer drag that reorders categories.
/// </summary>
public class SidebarViewTests
{
    private sealed class StubFileService : IFileService
    {
        private readonly Config _config;

        public StubFileService(Config config) => _config = config;

        public Task<Config> LoadFromFileAsync() => Task.FromResult(_config);

        public Task SaveToFileAsync(Config itemToSave) => Task.CompletedTask;
    }

    private static (MainWindow window, MainViewModel main, DownloadManager manager, Config config) Show()
    {
        Localizer.Instance.Load("en");
        var config = Config.New();
        config.Categories = CategoryService.CreateDefaults(key => key);
        config.DefaultQueue.IsRunning = false;
        var manager = new DownloadManager();
        var main = new MainViewModel(new StubFileService(config), manager);
        manager.Initialize(config);
        Dispatcher.UIThread.RunJobs();
        manager.AddQueue("Night");

        var window = new MainWindow { DataContext = main };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, main, manager, config);
    }

    private static Button[] Rows(Window window) =>
        window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("cat") && b.IsEffectivelyVisible)
            .ToArray();

    private static Button CategoryRow(Window window, string id) =>
        Rows(window).Single(b => b.DataContext is CategoryRowViewModel r && r.Id == id);

    private static Button QueueRow(Window window, string name) =>
        Rows(window).Single(b => b.DataContext is SidebarQueueRowViewModel r && r.Name == name);

    private static Border Bar(Button row) =>
        row.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("selbar"));

    private static Border Grip(Button row) =>
        row.GetVisualDescendants().OfType<Border>().SingleOrDefault(b => b.Classes.Contains("grip"));

    private static Point Center(Control control, Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window).Value;

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Both_sections_are_listed_and_collapsing_one_hides_only_its_rows()
    {
        var (window, main, _, _) = Show();
        Assert.Contains(Rows(window), b => b.DataContext is SidebarQueueRowViewModel);
        Assert.Contains(Rows(window), b => b.DataContext is CategoryRowViewModel);

        main.ToggleQueuesSectionCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(Rows(window), b => b.DataContext is SidebarQueueRowViewModel);
        Assert.Contains(Rows(window), b => b.DataContext is CategoryRowViewModel);

        main.ToggleCategoriesSectionCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(Rows(window), b => b.DataContext is CategoryRowViewModel);

        main.ToggleQueuesSectionCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(Rows(window), b => b.DataContext is SidebarQueueRowViewModel);
        window.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Only_the_selected_row_shows_the_accent_bar()
    {
        var (window, main, _, _) = Show();

        // At start "All" is the selection.
        Assert.Single(Rows(window), r => Bar(r).IsVisible);
        Assert.True(Bar(CategoryRow(window, null)).IsVisible);

        main.SelectedCategoryId = "video";
        Dispatcher.UIThread.RunJobs();
        Assert.Single(Rows(window), r => Bar(r).IsVisible);
        Assert.True(Bar(CategoryRow(window, "video")).IsVisible);

        QueueRow(window, "Night").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(Rows(window), r => Bar(r).IsVisible);
        Assert.True(Bar(QueueRow(window, "Night")).IsVisible);
        window.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_bar_follows_the_accent_without_a_restart()
    {
        var (window, main, _, _) = Show();
        main.SelectedCategoryId = "video";
        Dispatcher.UIThread.RunJobs();
        try
        {
            ThemeService.ApplyAccent("Purple");
            Dispatcher.UIThread.RunJobs();
            var purple = ((ISolidColorBrush)Bar(CategoryRow(window, "video")).Background).Color;
            Assert.Equal(ThemeService.Find("Purple").Color, purple);

            ThemeService.ApplyAccent("Green");
            Dispatcher.UIThread.RunJobs();
            var green = ((ISolidColorBrush)Bar(CategoryRow(window, "video")).Background).Color;
            Assert.Equal(ThemeService.Find("Green").Color, green);
        }
        finally
        {
            ThemeService.ApplyAccent(null);
            window.Close();
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_bar_sits_on_the_start_edge_left_to_right_and_right_to_left()
    {
        var (window, main, _, _) = Show();
        main.SelectedCategoryId = "video";
        Dispatcher.UIThread.RunJobs();

        var row = CategoryRow(window, "video");
        Assert.True(Bar(row).TranslatePoint(default, row)!.Value.X < row.Bounds.Width / 2);

        window.FlowDirection = FlowDirection.RightToLeft;
        Dispatcher.UIThread.RunJobs();
        row = CategoryRow(window, "video");
        var bar = Bar(row);
        // Measured in the WINDOW's (unmirrored) space: the bar is now on the row's right edge.
        var barX = bar.TranslatePoint(default, window)!.Value.X;
        var rowX = row.TranslatePoint(default, window)!.Value.X;
        var rowLeft = System.Math.Min(rowX, rowX - row.Bounds.Width);
        Assert.True(System.Math.Abs(barX - rowLeft) > row.Bounds.Width / 2,
            $"bar at {barX}, row from {rowLeft} width {row.Bounds.Width}");
        window.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_drag_handle_shows_on_a_hovered_category_only()
    {
        var (window, _, _, _) = Show();

        // Never on "All", never on a queue.
        Assert.False(Grip(CategoryRow(window, null)).IsVisible);
        Assert.Null(Grip(QueueRow(window, "Night")));

        var video = CategoryRow(window, "video");
        Assert.Equal(0, Grip(video).Opacity);

        window.MouseMove(Center(video, window));
        Dispatcher.UIThread.RunJobs();
        Assert.True(Grip(video).Opacity > 0);
        // …and only on the hovered row.
        Assert.Equal(0, Grip(CategoryRow(window, "audio")).Opacity);
        window.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Dragging_a_category_by_its_handle_reorders_and_keeps_the_order()
    {
        var (window, main, _, config) = Show();
        var ids = config.Categories.Select(c => c.Id).ToList();
        Assert.True(ids.IndexOf("document") > ids.IndexOf("video"));

        var grip = Grip(CategoryRow(window, "document"));
        var start = Center(grip, window);
        window.MouseMove(start);
        window.MouseDown(start, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        var hit = window.InputHitTest(start) as Visual;
        Assert.True(CategoryRow(window, "document").Classes.Contains("dragging"),
            $"hit {hit?.GetType().Name} at {start}; grip bounds {grip.Bounds} visible {grip.IsEffectivelyVisible}; "
            + string.Join(">", hit?.GetVisualAncestors().Take(5).Select(a => a.GetType().Name) ?? []));
        var target = Center(CategoryRow(window, "video"), window);
        window.MouseMove(target);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("droptarget", CategoryRow(window, "video").Classes);
        window.MouseUp(target, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // The config list is what gets saved, so this is the order after a restart too.
        ids = config.Categories.Select(c => c.Id).ToList();
        Assert.Equal(ids.IndexOf("video") - 1, ids.IndexOf("document"));
        Assert.Equal(Enumerable.Range(0, ids.Count), config.Categories.Select(c => c.Position));
        // The press on the handle did not select the row.
        Assert.Null(main.SelectedCategoryId);
        Assert.DoesNotContain(Rows(window), r => r.Classes.Contains("droptarget"));
        window.Close();
    }
}
