using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
/// Right-click menus are styled once, globally, in App.axaml: every ContextMenu and MenuFlyout in
/// the app wears the same rounded card and the same roomy rows, instead of Fluent's square default
/// (or a per-menu Padding somebody remembered to add). These pin the values a menu actually picks
/// up when it is opened, and that the category menu carries its icons.
/// </summary>
public class ContextMenuStyleTests
{
    private sealed class StubFileService : IFileService
    {
        private readonly Config _config;

        public StubFileService(Config config) => _config = config;

        public Task<Config> LoadFromFileAsync() => Task.FromResult(_config);

        public Task SaveToFileAsync(Config itemToSave) => Task.CompletedTask;
    }

    /// <summary>Opens a real menu on a shown window, which is when app styles reach it.</summary>
    private static (ContextMenu menu, MenuItem item) OpenMenu()
    {
        var item = new MenuItem { Header = "Something" };
        var menu = new ContextMenu { ItemsSource = new[] { item } };
        var target = new Button { Content = "target", ContextMenu = menu };
        var window = new Window { Content = target, Width = 300, Height = 200 };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        menu.Open(target);
        Dispatcher.UIThread.RunJobs();

        return (menu, item);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_opened_menu_wears_the_apps_rounded_card()
    {
        var (menu, _) = OpenMenu();

        Assert.Equal(new CornerRadius(10), menu.CornerRadius);
        Assert.Equal(new Thickness(5), menu.Padding);
        Assert.Equal(new Thickness(1), menu.BorderThickness);
        Assert.Equal(180, menu.MinWidth);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_menu_row_is_a_roomy_target_with_its_own_rounded_highlight()
    {
        var (_, item) = OpenMenu();

        Assert.Equal(new Thickness(11, 7), item.Padding);
        Assert.Equal(new CornerRadius(6), item.CornerRadius);
        Assert.Equal(32, item.MinHeight);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_category_menu_offers_edit_both_moves_and_delete_each_with_an_icon()
    {
        Localizer.Instance.Load("en");
        var config = Config.New();
        config.Categories = CategoryService.CreateDefaults(key => key);
        config.IsCategorySidebarOpen = true;

        var manager = new DownloadManager();
        var main = new MainViewModel(new StubFileService(config), manager);
        manager.Initialize(config);
        Dispatcher.UIThread.RunJobs();
        main.IsCategorySidebarOpen = true;

        var window = new MainWindow { DataContext = main };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The "All" row cannot be edited, so take the first row that carries a menu.
        var menu = window.GetVisualDescendants()
            .OfType<Button>()
            .Select(b => b.ContextMenu)
            .FirstOrDefault(m => m is not null);

        Assert.NotNull(menu);
        var items = menu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(4, items.Count);
        Assert.All(items, i => Assert.IsType<PathIcon>(i.Icon));
        // The look is the global style's job, and a local value would beat it — so the menu must
        // not set one. (Styles only reach a menu once it is opened, hence the check on the setter
        // rather than on the value; the two tests above cover what an opened menu ends up with.)
        Assert.False(menu.IsSet(TemplatedControl.PaddingProperty));

        window.Close();
    }
}
