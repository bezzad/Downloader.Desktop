using System.Linq;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Converters;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The category editor's rules: a name is required and must be distinguishable, a built-in category
/// can be changed but not deleted, and cancelling leaves the live list exactly as it was.
/// </summary>
public class CategoryEditorTests
{
    private static CategoryService Service()
    {
        Localizer.Instance.Load("en");
        var service = new CategoryService();
        var config = Config.New();
        config.Categories = CategoryService.CreateDefaults(key => key);
        service.Initialize(config);
        return service;
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_new_category_needs_a_name()
    {
        var vm = new CategoryEditorViewModel(Service(), null) { Name = "   " };

        Assert.Null(vm.Build());
        Assert.True(vm.HasError);
        Assert.Equal(Localizer.Instance["Cat_NameRequired"], vm.Error);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_name_another_category_already_has_is_refused_with_the_reason()
    {
        var service = Service();
        var taken = service.Categories.First().Name;

        var vm = new CategoryEditorViewModel(service, null) { Name = taken };

        Assert.Null(vm.Build());
        Assert.Equal(Localizer.Instance["Cat_NameDuplicate"], vm.Error);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_category_may_keep_its_own_name_while_being_edited()
    {
        var service = Service();
        var video = service.ById("video");

        var vm = new CategoryEditorViewModel(service, video) { Color = "#111111" };

        var built = vm.Build();
        Assert.NotNull(built);
        Assert.Equal(video.Name, built.Name);
        Assert.Equal("#111111", built.Color);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Editing_works_on_a_copy_so_an_abandoned_edit_changes_nothing()
    {
        var service = Service();
        var before = service.ById("video").Name;

        var vm = new CategoryEditorViewModel(service, service.ById("video")) { Name = "changed" };
        vm.Build();   // built, but never applied — the dialog was cancelled

        Assert.Equal(before, service.ById("video").Name);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_built_in_category_offers_no_delete_but_a_user_one_does()
    {
        var service = Service();
        service.Add(new DownloadCategory { Name = "Mine", Icon = "file" });

        Assert.False(new CategoryEditorViewModel(service, service.ById("video")).CanDelete);
        Assert.False(new CategoryEditorViewModel(service, null).CanDelete);
        Assert.True(new CategoryEditorViewModel(service, service.Categories.Last()).CanDelete);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Extensions_are_taken_as_typed_and_cleaned_up()
    {
        var vm = new CategoryEditorViewModel(Service(), null)
        {
            Name = "Ebooks",
            Extensions = ".EPUB, mobi;  azw3  , epub"
        };

        var built = vm.Build();

        Assert.Equal(new[] { "epub", "mobi", "azw3" }, built.Extensions);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Every_offered_icon_really_renders()
    {
        // A picker entry that draws nothing would be an invisible choice.
        foreach (var key in CategoryEditorViewModel.IconChoices)
            Assert.NotNull(FileKindToIconConverter.GetIcon(key));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_icon_key_this_build_does_not_know_still_draws_something()
    {
        // From a list exported by a newer app. It must render, and the key itself is kept elsewhere
        // so the round trip is not destructive.
        Assert.NotNull(FileKindToIconConverter.GetIcon("an-icon-from-the-future"));
        Assert.Equal(FileKindToIconConverter.GetIcon("file"),
            FileKindToIconConverter.GetIcon("an-icon-from-the-future"));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Every_offered_color_parses_and_an_unusable_one_falls_back()
    {
        foreach (var hex in CategoryEditorViewModel.ColorChoices)
            Assert.NotNull(HexToBrushConverter.BrushFor(hex));

        // A hand-edited or imported value must never leave a cell with no brush at all.
        Assert.NotNull(HexToBrushConverter.BrushFor("not a color"));
        Assert.NotNull(HexToBrushConverter.BrushFor(null));
    }
}
