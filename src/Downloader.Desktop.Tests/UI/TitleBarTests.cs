using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The main window's title bar: centered title, the search box and the app buttons live there, and
/// the search box grows while it is in use. Dialog title bars stay as they were.
/// </summary>
public class TitleBarTests
{
    private sealed class StubFileService : IFileService
    {
        public Task<Config> LoadFromFileAsync() => Task.FromResult(Config.New());
        public Task SaveToFileAsync(Config itemToSave) => Task.CompletedTask;
    }

    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
            Dispatcher.UIThread.RunJobs();
    }

    private static (MainWindow window, MainViewModel main, DownloadManager manager) Show()
    {
        Localizer.Instance.Load("en");
        var config = Config.New();
        var manager = new DownloadManager();
        var main = new MainViewModel(new StubFileService(), manager);
        manager.Initialize(config);
        config.DefaultQueue.IsRunning = false;
        var window = new MainWindow { DataContext = main };
        window.Show();
        Pump();
        return (window, main, manager);
    }

    private static T Named<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    private static TextBox Search(MainWindow window)
    {
        var box = Named<TextBox>(window, "SearchBox");
        box.Transitions = null; // assert the target width, not a frame of the animation
        return box;
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_search_box_is_in_the_title_bar_and_the_app_buttons_stay_in_the_top_bar()
    {
        var (window, main, _) = Show();
        var bar = window.GetVisualDescendants().OfType<TitleBar>().Single();
        var inBar = bar.GetVisualDescendants().ToList();

        Assert.True(bar.CenterTitle);
        Assert.Contains(Named<TextBox>(window, "SearchBox"), inBar);
        // Donate + About stay in the top bar, not the title bar.
        var appButtons = window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Command == main.DonateCommand || b.Command == main.ShowAboutCommand
                        || b.Command == main.ApplyUpdateCommand).ToList();
        Assert.Equal(3, appButtons.Count);
        Assert.All(appButtons, b => Assert.DoesNotContain(b, inBar));
        window.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_title_stays_visible_while_the_search_is_focused_at_the_default_size()
    {
        var (window, _, _) = Show();
        Search(window).Focus();
        Pump();
        Assert.True(window.GetVisualDescendants().OfType<TitleBar>().Single().IsCenteredTitleShown);
        window.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_title_is_light_in_the_dark_theme()
    {
        var previous = Application.Current!.RequestedThemeVariant;
        try
        {
            var (window, _, _) = Show();
            Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            Pump();
            var title = Named<TextBlock>(window, "CenteredTitle");
            var brush = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(title.Foreground);
            var c = brush.Color;
            Assert.True(c.R + c.G + c.B > 3 * 180, $"title color {c} is too dark for a dark background");
            window.Close();
        }
        finally
        {
            Application.Current.RequestedThemeVariant = previous;
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Typing_in_the_title_bar_search_filters_the_list()
    {
        var (window, main, manager) = Show();
        manager.Add(new DownloadItem { Url = "https://10.255.255.1/a", FileName = "invoice.pdf" }, autoStart: false);
        manager.Add(new DownloadItem { Url = "https://10.255.255.1/b", FileName = "photos.zip" }, autoStart: false);

        var box = Search(window);
        box.Focus();
        window.KeyTextInput("invoice");
        Pump();

        Assert.Equal("invoice", main.SearchText);
        Assert.Equal("invoice.pdf", main.Downloads.ItemsView.Cast<DownloadItemViewModel>().Single().FileName);
        window.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_title_is_centered_across_the_window()
    {
        var (window, _, _) = Show();
        var bar = window.GetVisualDescendants().OfType<TitleBar>().Single();
        Assert.True(bar.IsCenteredTitleShown);

        var title = Named<TextBlock>(bar, "CenteredTitle");
        var left = title.TranslatePoint(default, bar)!.Value.X;
        var center = left + title.Bounds.Width / 2;
        Assert.InRange(center, bar.Bounds.Width / 2 - 2, bar.Bounds.Width / 2 + 2);
        window.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_search_box_grows_while_in_use()
    {
        var (window, main, _) = Show();
        var box = Search(window);
        var other = Named<TextBox>(window, "TopUrlBox");

        Assert.Equal(200, box.Width);

        box.Focus();
        Pump();
        Assert.Equal(280, box.Width);

        main.SearchText = "zip";
        other.Focus();
        Pump();
        Assert.Equal(280, box.Width); // has text: stays wide

        main.SearchText = "";
        Pump();
        Assert.Equal(200, box.Width); // empty and unfocused: back to normal
        window.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_search_width_is_animated()
    {
        var (window, _, _) = Show();
        var box = Named<TextBox>(window, "SearchBox");
        Assert.Contains(box.Transitions!, t => t is DoubleTransition d && d.Property == Layoutable.WidthProperty);
        window.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Pressing_the_search_box_does_not_drag_but_empty_bar_space_does()
    {
        var (window, _, _) = Show();
        var bar = window.GetVisualDescendants().OfType<TitleBar>().Single();
        var drags = 0;
        bar.DragStarted += () => drags++;

        var box = Search(window);
        var inBox = box.TranslatePoint(new Point(box.Bounds.Width / 2, box.Bounds.Height / 2), window)!.Value;
        window.MouseDown(inBox, MouseButton.Left);
        window.MouseUp(inBox, MouseButton.Left);
        Pump();
        Assert.Equal(0, drags);
        Assert.True(box.IsFocused || box.IsKeyboardFocusWithin);

        // Empty space between the icon and the centered title.
        var empty = bar.TranslatePoint(new Point(80, bar.Bounds.Height / 2), window)!.Value;
        window.MouseDown(empty, MouseButton.Left);
        window.MouseUp(empty, MouseButton.Left);
        Assert.Equal(1, drags);
        window.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void At_minimum_width_the_title_never_overlaps_the_right_side()
    {
        var (window, _, _) = Show();
        window.Width = window.MinWidth;
        var box = Search(window);
        box.Focus();
        Pump();

        var bar = window.GetVisualDescendants().OfType<TitleBar>().Single();
        if (bar.IsCenteredTitleShown)
        {
            var title = Named<TextBlock>(bar, "CenteredTitle");
            var right = Named<ContentPresenter>(bar, "Right");
            var titleEnd = title.TranslatePoint(new Point(title.Bounds.Width, 0), bar)!.Value.X;
            Assert.True(titleEnd <= right.Bounds.X, $"title ends at {titleEnd}, right side starts at {right.Bounds.X}");
        }
        window.Close();
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_dialog_title_bar_is_unchanged()
    {
        var bar = new TitleBar { Title = "Add download", ShowMinMax = false };
        var window = new Window { Width = 500, Height = 200, Content = bar };
        window.Show();
        Pump();

        Assert.False(bar.CenterTitle);
        Assert.Null(bar.RightContent);
        Assert.False(bar.IsCenteredTitleShown);
        var leftTitle = bar.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Name != "CenteredTitle" && t.Text == "Add download");
        Assert.True(leftTitle.IsVisible);
        Assert.Empty(bar.GetVisualDescendants().OfType<TextBox>());
        window.Close();
    }
}
