using System.Collections.Generic;
using System.Linq;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// The category model and how a download's category is worked out: the precedence (the user's
/// choice, then the extension, then the content type, then Other), the ordering that doubles as
/// extension precedence, and the editing rules.
/// </summary>
public class CategoryServiceTests
{
    /// <summary>The built-ins, named by their key so no localizer (and so no Avalonia runtime) is
    /// needed for tests that are about behavior rather than wording.</summary>
    private static List<DownloadCategory> Defaults() => CategoryService.CreateDefaults(key => key);

    private static CategoryService Service(Config config = null)
    {
        var service = new CategoryService();
        service.Initialize(config ?? Config.New());
        return service;
    }

    // ---- precedence --------------------------------------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_explicit_choice_beats_the_extension()
    {
        var service = Service();

        var resolved = service.Resolve("image", "clip.mp4", null);

        Assert.Equal("image", resolved.Id);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_choice_naming_a_category_that_is_gone_falls_back_to_detection()
    {
        var service = Service();

        var resolved = service.Resolve("cat_deleted", "clip.mp4", null);

        Assert.Equal("video", resolved.Id);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_extension_beats_the_content_type()
    {
        // A server that says "I don't know" (or says something wrong) must not overrule a name that
        // does know.
        Assert.Equal("audio", CategoryService.Detect(Defaults(), "song.mp3", "application/octet-stream").Id);
        Assert.Equal("audio", CategoryService.Detect(Defaults(), "song.mp3", "video/mp4").Id);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("audio/mpeg", "audio")]
    [InlineData("video/mp4", "video")]
    [InlineData("video/x-matroska", "video")]
    [InlineData("image/png", "image")]
    [InlineData("application/pdf", "document")]
    [InlineData("application/zip", "archive")]
    [InlineData("application/x-zip-compressed", "archive")]
    [InlineData("application/vnd.android.package-archive", "app")]
    [InlineData("text/plain; charset=utf-8", "document")]
    public void The_content_type_is_used_when_the_name_has_no_extension(string contentType, string expected)
    {
        Assert.Equal(expected, CategoryService.Detect(Defaults(), "download", contentType).Id);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("application/octet-stream")]
    [InlineData("binary/octet-stream")]
    [InlineData("")]
    [InlineData(null)]
    public void A_content_type_that_says_nothing_lands_in_other(string? contentType)
    {
        // octet-stream is the header's way of saying it does not know; treating it as an answer
        // would put half the web in one category.
        Assert.Equal(DownloadCategory.OtherId, CategoryService.Detect(Defaults(), "download", contentType).Id);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_download_with_no_name_yet_resolves_to_other_and_corrects_itself_later()
    {
        var service = Service();
        var item = new DownloadItem { Url = "https://10.255.255.1/x" };

        Assert.Equal(DownloadCategory.OtherId, service.Resolve(item).Id);

        item.FileName = "holiday.mp4";

        Assert.Equal("video", service.Resolve(item).Id);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_unrecognized_extension_lands_in_other()
    {
        Assert.Equal(DownloadCategory.OtherId, CategoryService.Detect(Defaults(), "payload.qqq", null).Id);
    }

    // ---- order is precedence ------------------------------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void When_two_categories_claim_an_extension_the_earlier_one_wins()
    {
        var service = Service();
        service.Add(new DownloadCategory { Name = "Clips", Icon = "video", Extensions = new List<string> { "mp4" } });
        var clips = service.Categories.Last();

        // Added at the end, so the built-in Video still wins.
        Assert.Equal("video", service.Resolve(null, "a.mp4", null).Id);

        // Move it to the top and the same file resolves differently — one number, visible in the
        // sidebar, doing the job a hidden precedence rule would otherwise do.
        while (clips.Position > 0)
            service.Move(clips.Id, -1);

        Assert.Equal(clips.Id, service.Resolve(null, "a.mp4", null).Id);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Moving_a_category_up_and_down_returns_it_to_where_it_was()
    {
        var service = Service();
        var before = service.Categories.Select(c => c.Id).ToList();
        var second = before[1];

        Assert.True(service.Move(second, -1));
        Assert.Equal(0, service.ById(second).Position);
        Assert.True(service.Move(second, 1));

        Assert.Equal(before, service.Categories.Select(c => c.Id).ToList());
        // Positions stay a dense 0..n-1 run, which is what the grid's sort depends on.
        Assert.Equal(Enumerable.Range(0, before.Count), service.Categories.Select(c => c.Position));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_category_cannot_be_moved_off_either_end()
    {
        var service = Service();
        var first = service.Categories.First().Id;
        var last = service.Categories.Last().Id;

        Assert.False(service.Move(first, -1));
        Assert.False(service.Move(last, 1));
    }

    // ---- editing -----------------------------------------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_new_category_claims_its_extensions_at_once()
    {
        var config = Config.New();
        var service = Service(config);

        // Before: .epub is a document, because that is where the built-ins put it.
        Assert.Equal("document", service.Resolve(null, "book.epub", null).Id);

        service.Add(new DownloadCategory
        {
            Name = "Ebooks",
            Icon = "document",
            Color = "#123456",
            Extensions = new List<string> { "epub" }
        });
        var ebooks = service.Categories.Last();
        while (ebooks.Position > 0)
            service.Move(ebooks.Id, -1);

        // After: no restart, no re-scan of the download list — the answer is derived on every read.
        Assert.Equal(ebooks.Id, service.Resolve(null, "book.epub", null).Id);
        // …and it is in the config that gets saved.
        Assert.Contains(config.Categories, c => c.Id == ebooks.Id);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_built_in_category_cannot_be_deleted_but_a_user_one_can()
    {
        var service = Service();
        service.Add(new DownloadCategory { Name = "Mine", Icon = "file" });
        var mine = service.Categories.Last().Id;

        Assert.False(service.Remove("video"));
        Assert.True(service.Remove(mine));
        Assert.Null(service.ById(mine));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_name_is_required_and_must_not_repeat_an_existing_one()
    {
        var service = Service();

        Assert.False(service.IsNameAvailable(""));
        Assert.False(service.IsNameAvailable("   "));
        Assert.False(service.IsNameAvailable(service.Categories.First().Name));
        // Case and surrounding space do not make two names distinguishable in a sidebar.
        Assert.False(service.IsNameAvailable("  " + service.Categories.First().Name.ToUpperInvariant() + " "));
        Assert.True(service.IsNameAvailable("Something else"));
        // A category keeps its own name while being edited.
        var first = service.Categories.First();
        Assert.True(service.IsNameAvailable(first.Name, first.Id));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Editing_applies_over_the_live_category()
    {
        var service = Service();

        var edited = service.ById("video").Clone();
        edited.Name = "映画";
        edited.Color = "#ABCDEF";
        edited.Extensions = new List<string> { ".MP4", "mp4", " mkv " };
        service.Update(edited);

        var live = service.ById("video");
        Assert.Equal("映画", live.Name);
        Assert.Equal("#ABCDEF", live.Color);
        // Extensions are normalized: no dots, lowercase, no repeats.
        Assert.Equal(new[] { "mp4", "mkv" }, live.Extensions);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_clone_can_be_thrown_away_without_touching_the_live_list()
    {
        var service = Service();

        var draft = service.ById("video").Clone();
        draft.Name = "changed";
        draft.Extensions.Add("qqq");

        Assert.NotEqual("changed", service.ById("video").Name);
        Assert.DoesNotContain("qqq", service.ById("video").Extensions);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_unrecognized_icon_key_is_preserved_rather_than_dropped()
    {
        // A list exported by a newer app may name an icon this build does not know. It renders as
        // the default icon (the converter's job), but throwing the key away would make the round
        // trip destructive.
        var service = Service();
        var edited = service.ById("video").Clone();
        edited.Icon = "an-icon-from-the-future";
        service.Update(edited);

        Assert.Equal("an-icon-from-the-future", service.ById("video").Icon);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Changing_the_list_announces_itself()
    {
        var service = Service();
        var changes = 0;
        service.Changed += () => changes++;

        service.Add(new DownloadCategory { Name = "A", Icon = "file" });
        var id = service.Categories.Last().Id;
        service.Update(service.ById(id).Clone());
        service.Move(id, -1);
        service.Remove(id);

        Assert.Equal(4, changes);
    }

    // ---- parsing -----------------------------------------------------------------------------

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("mp4, mkv avi", new[] { "mp4", "mkv", "avi" })]
    [InlineData(".MP4;.mkv", new[] { "mp4", "mkv" })]
    [InlineData("mp4,mp4 , mp4", new[] { "mp4" })]
    [InlineData("", new string[0])]
    [InlineData(null, new string[0])]
    public void Extension_text_is_parsed_into_a_clean_list(string? text, string[] expected)
    {
        Assert.Equal(expected, CategoryService.ParseExtensions(text));
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("movie.MP4", "mp4")]
    [InlineData("archive.tar.gz", "gz")]
    [InlineData("noextension", "")]
    // .NET reads a leading-dot name as being all extension; nothing claims "bashrc", so it
    // lands in Other, which is the right answer anyway.
    [InlineData(".bashrc", "bashrc")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void The_extension_is_read_lowercase_and_without_its_dot(string? name, string expected)
    {
        Assert.Equal(expected, CategoryService.ExtensionOf(name));
    }
}
