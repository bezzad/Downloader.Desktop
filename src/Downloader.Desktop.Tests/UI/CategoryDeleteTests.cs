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
/// Deleting a category from its right-click menu. The two rules that matter: only a category the
/// user made can go (a built-in one is refused by the service, so the menu must not offer it), and
/// deleting one never costs the user a download — the downloads it held go back to being sorted
/// automatically, which is exactly what the confirmation promises.
/// </summary>
public class CategoryDeleteTests
{
    private sealed class StubFileService : IFileService
    {
        private readonly Config _config;

        public StubFileService(Config config) => _config = config;

        public Config Saved { get; private set; }

        public Task<Config> LoadFromFileAsync() => Task.FromResult(_config);

        public Task SaveToFileAsync(Config itemToSave)
        {
            Saved = itemToSave;
            return Task.CompletedTask;
        }
    }

    private static Config NewConfig()
    {
        var config = Config.New();
        config.Categories = CategoryService.CreateDefaults(key => key);
        return config;
    }

    private static (MainViewModel main, DownloadManager manager) Build(Config config)
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        var main = new MainViewModel(new StubFileService(config), manager);
        manager.Initialize(config);
        // MainViewModel's init is scheduled, not inline; pump it so the sidebar rows exist.
        Dispatcher.UIThread.RunJobs();
        return (main, manager);
    }

    /// <summary>Adds a category of the user's own and returns its sidebar row.</summary>
    private static CategoryRowViewModel AddOwn(MainViewModel main, DownloadManager manager, string name,
        params string[] extensions)
    {
        manager.Categories.Add(new DownloadCategory
        {
            Name = name,
            Icon = "other",
            Extensions = extensions.ToList()
        });
        Dispatcher.UIThread.RunJobs();
        return main.CategoryRows.First(r => r.Name == name);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Only_a_category_the_user_made_offers_delete()
    {
        var (main, manager) = Build(NewConfig());

        // "All" is not a category at all, and every shipped category is built in.
        Assert.False(main.CategoryRows.First(r => r.IsAll).CanDelete);
        Assert.All(main.CategoryRows.Where(r => !r.IsAll), r => Assert.False(r.CanDelete));

        Assert.True(AddOwn(main, manager, "Fonts", "ttf").CanDelete);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Deleting_a_category_takes_it_off_the_sidebar_and_out_of_the_saved_config()
    {
        var config = NewConfig();
        var (main, manager) = Build(config);
        var row = AddOwn(main, manager, "Fonts", "ttf");

        await main.DeleteCategoryAsync(row);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(manager.Categories.Categories, c => c.Name == "Fonts");
        Assert.DoesNotContain(main.CategoryRows, r => r.Name == "Fonts");
        Assert.DoesNotContain(config.Categories, c => c.Name == "Fonts");
    }

    /// <summary>The menu already hides the item for a built-in (covered above); this is the backstop
    /// behind it — driving the delete path directly still cannot remove one, because the service
    /// refuses. Both halves matter: bulk/keyboard paths bypass a view-level guard.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_built_in_category_survives_the_delete_path()
    {
        var (main, manager) = Build(NewConfig());
        var builtIn = main.CategoryRows.First(r => !r.IsAll);
        var before = manager.Categories.Categories.Count;

        await main.DeleteCategoryAsync(builtIn);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(before, manager.Categories.Categories.Count);
        Assert.Contains(manager.Categories.Categories, c => c.Id == builtIn.Id);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task The_downloads_in_a_deleted_category_are_kept_and_sorted_automatically_again()
    {
        var (main, manager) = Build(NewConfig());
        var row = AddOwn(main, manager, "Fonts", "ttf");

        // One download explicitly put in it, one that only lands there by its extension.
        var chosen = manager.Add(new DownloadItem
        {
            Url = "https://10.255.255.1/a.zip",
            FileName = "a.zip",
            CategoryId = row.Id
        }, autoStart: false);
        var byExtension = manager.Add(new DownloadItem
        {
            Url = "https://10.255.255.1/b.ttf",
            FileName = "b.ttf"
        }, autoStart: false);

        Assert.Equal(row.Id, chosen.Category?.Id);
        Assert.Equal(row.Id, byExtension.Category?.Id);

        await main.DeleteCategoryAsync(row);
        Dispatcher.UIThread.RunJobs();

        // Both downloads are still listed; neither is left pointing at a category that is gone.
        Assert.Equal(2, manager.Items.Count);
        Assert.NotNull(chosen.Category);
        Assert.NotNull(byExtension.Category);
        Assert.NotEqual(row.Id, chosen.Category.Id);
        Assert.NotEqual(row.Id, byExtension.Category.Id);
        // The .zip falls back to the built-in archives category, so this is a re-sort, not a dump
        // of everything into "Other".
        Assert.Equal("Cat_Archive", chosen.Category.Name);
    }
}
