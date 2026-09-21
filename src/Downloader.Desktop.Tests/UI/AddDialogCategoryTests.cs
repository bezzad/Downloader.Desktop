using System.Linq;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// Choosing a download's category before it starts. The automatic entry names what the app would
/// pick, so leaving it alone is an informed decision rather than a shrug.
/// </summary>
public class AddDialogCategoryTests
{
    private static (Config config, DownloadManager manager) Build()
    {
        Localizer.Instance.Load("en");
        var config = Config.New();
        config.Categories = CategoryService.CreateDefaults(key => key);
        var manager = new DownloadManager();
        manager.Initialize(config);
        return (config, manager);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_automatic_entry_comes_first_and_says_what_it_would_pick()
    {
        var (config, manager) = Build();
        var vm = new AddDownloadItemViewModel(config, "https://10.255.255.1/a.mp4", manager: manager)
        {
            Filename = "a.mp4"
        };

        var automatic = vm.CategoryChoices.First();

        Assert.Null(automatic.Id);
        Assert.Contains("Cat_Video", automatic.Label);   // the defaults are named by key in these tests
        Assert.Equal(automatic, vm.SelectedCategory);    // and it is what a plain add uses
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_automatic_entry_follows_the_name_being_typed()
    {
        var (config, manager) = Build();
        var vm = new AddDownloadItemViewModel(config, "https://10.255.255.1/x", manager: manager);

        vm.Filename = "a.mp4";
        Assert.Contains("Cat_Video", vm.CategoryChoices.First().Label);

        vm.Filename = "a.mp3";
        Assert.Contains("Cat_Audio", vm.CategoryChoices.First().Label);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_plain_add_leaves_the_category_to_be_worked_out()
    {
        var (config, manager) = Build();
        var vm = new AddDownloadItemViewModel(config, "https://10.255.255.1/a.mp4", manager: manager);

        var item = vm.BuildItems().Single();

        // Null, not "video": stored as a choice it would freeze, and would not follow a category the
        // user creates later.
        Assert.Null(item.CategoryId);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_chosen_category_lands_on_the_download()
    {
        var (config, manager) = Build();
        var vm = new AddDownloadItemViewModel(config, "https://10.255.255.1/a.mp4", manager: manager);
        vm.SelectedCategory = vm.CategoryChoices.First(c => c.Id == "image");

        var item = vm.BuildItems().Single();

        Assert.Equal("image", item.CategoryId);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Every_download_of_a_batch_gets_the_chosen_category()
    {
        var (config, manager) = Build();
        var vm = new AddDownloadItemViewModel(config,
            "https://10.255.255.1/a.mp4\nhttps://10.255.255.1/b.mp4", manager: manager);
        vm.SelectedCategory = vm.CategoryChoices.First(c => c.Id == "archive");

        var items = vm.BuildItems();

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal("archive", i.CategoryId));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_confirmed_programmatic_add_opens_with_the_category_and_type_the_caller_asked_for()
    {
        var (config, manager) = Build();
        var request = ApiAddRequest.FromJson("""
        { "url": "https://10.255.255.1/d/8f3a2b", "category": "image", "mime": "video/mp4" }
        """);

        var vm = new AddDownloadItemViewModel(config, request.Url, manager: manager, apiRequest: request);

        // Pre-selected, so the user reviews the real download rather than a bare URL.
        Assert.Equal("image", vm.SelectedCategory?.Id);

        var item = vm.BuildItems().Single();
        Assert.Equal("image", item.CategoryId);
        // The observed media type is not the user's to edit, so it rides along either way.
        Assert.Equal("video/mp4", item.ContentType);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_category_the_caller_names_but_that_does_not_exist_leaves_the_pick_automatic()
    {
        var (config, manager) = Build();
        var request = ApiAddRequest.FromJson("""
        { "url": "https://10.255.255.1/a.mp4", "category": "cat_nope" }
        """);

        var vm = new AddDownloadItemViewModel(config, request.Url, manager: manager, apiRequest: request);

        Assert.Null(vm.SelectedCategory?.Id);
        Assert.Null(vm.BuildItems().Single().CategoryId);
    }
}
