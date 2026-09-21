using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// Carrying settings and categories to another machine: what goes in the file, what must never, and
/// the rule that a file which cannot be used leaves the current setup exactly as it was.
/// </summary>
public class SettingsPortabilityTests
{
    private static Config Config(string savePath = "/home/someone/Downloads")
    {
        var config = Models.Config.New();
        config.Categories = CategoryService.CreateDefaults(key => key);
        config.Settings.DefaultSavePath = savePath;
        return config;
    }

    // ---- what the file contains ----------------------------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_export_carries_the_settings_and_every_category()
    {
        var config = Config();
        config.Settings.ChunkCount = 5;
        config.Settings.Language = "fa";

        var document = JsonDocument.Parse(SettingsPortability.Export(config)).RootElement;

        Assert.Equal(SettingsPortability.CurrentFormat, document.GetProperty("format").GetInt32());
        Assert.Equal(5, document.GetProperty("settings").GetProperty(nameof(DownloadSettings.ChunkCount)).GetInt32());
        Assert.Equal("fa", document.GetProperty("settings").GetProperty(nameof(DownloadSettings.Language)).GetString());

        var categories = document.GetProperty("categories").EnumerateArray().ToList();
        Assert.Equal(config.Categories.Count, categories.Count);
        var video = categories.First(c => c.GetProperty("Id").GetString() == "video");
        Assert.Equal("video", video.GetProperty("Icon").GetString());
        Assert.Equal("#4F8DF5", video.GetProperty("Color").GetString());
        Assert.Contains("mp4", video.GetProperty("Extensions").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(0, video.GetProperty("Position").GetInt32());
        // Reserved for a future uploaded icon, so a file written today stays valid once they exist.
        Assert.Equal(JsonValueKind.Null, video.GetProperty("IconData").ValueKind);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_export_carries_no_download_data_and_nothing_machine_specific()
    {
        var config = Config("/home/someone/Downloads");
        config.Downloads.Add(new DownloadItem { Url = "https://10.255.255.1/a.mp4", FileName = "a.mp4" });
        config.Schedules.Add(new DownloadSchedule());
        config.Settings.LocalApiPort = 15153;
        config.WindowSizes["AddDownload"] = new WindowSize { Width = 1, Height = 2 };

        var json = SettingsPortability.Export(config);
        var document = JsonDocument.Parse(json).RootElement;

        // Work in progress on one machine is not portable.
        Assert.False(document.TryGetProperty("downloads", out _));
        Assert.False(document.TryGetProperty("queues", out _));
        Assert.False(document.TryGetProperty("schedules", out _));
        Assert.False(document.TryGetProperty("windowSizes", out _));
        // Nor is anything that only means something where it was written. Checked on the raw text as
        // well, so a value smuggled in under another name still fails this.
        var settings = document.GetProperty("settings");
        Assert.False(settings.TryGetProperty(nameof(DownloadSettings.DefaultSavePath), out _));
        Assert.False(settings.TryGetProperty(nameof(DownloadSettings.LocalApiPort), out _));
        Assert.DoesNotContain("/home/someone/Downloads", json);
        Assert.DoesNotContain("a.mp4", json);
    }

    // ---- crossing machines ---------------------------------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_file_written_on_one_machine_applies_whole_on_another()
    {
        // The export side: Linux paths in the settings, a renamed category and one of the user's own.
        var source = Config("/home/someone/Downloads");
        source.Settings.ChunkCount = 6;
        source.Settings.AccentColor = "Purple";
        source.Categories.First(c => c.Id == "video").Name = "映画";
        source.Categories.Add(new DownloadCategory
        {
            Id = "cat_mine",
            Name = "Ebooks",
            Icon = "document",
            Color = "#123456",
            Extensions = new List<string> { "epub", "mobi" },
            Position = source.Categories.Count
        });

        var json = SettingsPortability.Export(source);

        // The import side: a different machine, with a Windows save folder of its own.
        var target = Config(@"C:\Users\Someone\Downloads");
        Assert.Equal(ImportProblem.None, SettingsPortability.TryParse(json, target, out var imported));
        SettingsPortability.Apply(target, imported);

        Assert.Equal(6, target.Settings.ChunkCount);
        Assert.Equal("Purple", target.Settings.AccentColor);
        // The machine's own save folder survives the import — it was never in the file.
        Assert.Equal(@"C:\Users\Someone\Downloads", target.Settings.DefaultSavePath);

        Assert.Equal("映画", target.Categories.First(c => c.Id == "video").Name);
        var mine = target.Categories.First(c => c.Id == "cat_mine");
        Assert.Equal("Ebooks", mine.Name);
        Assert.Equal("document", mine.Icon);
        Assert.Equal("#123456", mine.Color);
        Assert.Equal(new[] { "epub", "mobi" }, mine.Extensions);
        // Order survives, which matters: it is also the grid's sort key and the extension precedence.
        Assert.Equal(Enumerable.Range(0, target.Categories.Count), target.Categories.Select(c => c.Position));
        Assert.Equal(source.Categories.Select(c => c.Id), target.Categories.Select(c => c.Id));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Imported_categories_re_categorize_downloads_that_made_no_choice()
    {
        var target = Config();
        var auto = new DownloadItem { FileName = "book.epub" };
        var chosen = new DownloadItem { FileName = "clip.mp4", CategoryId = "image" };
        target.Downloads.Add(auto);
        target.Downloads.Add(chosen);

        var source = Config();
        source.Categories.Insert(0, new DownloadCategory
        {
            Id = "cat_books",
            Name = "Ebooks",
            Icon = "document",
            Color = "#123456",
            Extensions = new List<string> { "epub" }
        });

        Assert.Equal(ImportProblem.None,
            SettingsPortability.TryParse(SettingsPortability.Export(source), target, out var imported));
        SettingsPortability.Apply(target, imported);

        // No choice of its own, so it follows the incoming rules at once.
        Assert.Equal("cat_books", CategoryService.Detect(target.Categories, auto.FileName, null).Id);
        // An explicit choice is the user's and survives, because that category is still there.
        Assert.Equal("image", chosen.CategoryId);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_override_naming_a_category_the_import_does_not_have_is_released()
    {
        var target = Config();
        var item = new DownloadItem { FileName = "clip.mp4", CategoryId = "cat_gone" };
        target.Downloads.Add(item);

        Assert.Equal(ImportProblem.None,
            SettingsPortability.TryParse(SettingsPortability.Export(Config()), target, out var imported));
        SettingsPortability.Apply(target, imported);

        // Left in place it would claim a choice while silently falling back — and would resurrect if
        // some later category happened to reuse the id.
        Assert.Null(item.CategoryId);
    }

    // ---- refusing a file -----------------------------------------------------------------------

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"format\":1}")]
    [InlineData("[1,2,3]")]
    public void A_file_that_is_not_one_of_ours_is_refused_and_changes_nothing(string json)
    {
        var target = Config();
        var before = target.Settings.ChunkCount;
        var categoriesBefore = target.Categories.Count;

        Assert.Equal(ImportProblem.Unreadable, SettingsPortability.TryParse(json, target, out var imported));

        Assert.Null(imported);
        Assert.Equal(before, target.Settings.ChunkCount);
        Assert.Equal(categoriesBefore, target.Categories.Count);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_file_listing_no_categories_is_refused()
    {
        // Applying it would leave the app with no categories at all, and every download pointing at
        // one that does not exist.
        var target = Config();

        Assert.Equal(ImportProblem.NoCategories,
            SettingsPortability.TryParse("{\"format\":1,\"settings\":{},\"categories\":[]}", target, out _));
        Assert.Equal(ImportProblem.NoCategories,
            SettingsPortability.TryParse("{\"format\":1,\"settings\":{}}", target, out _));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_category_entry_missing_an_id_or_a_name_is_skipped_not_fatal()
    {
        var target = Config();
        const string json = """
        {
          "format": 1,
          "settings": {},
          "categories": [
            { "Id": "", "Name": "no id" },
            { "Id": "nameless" },
            { "Id": "good", "Name": "Good", "Icon": "file", "Color": "#111111", "Extensions": [".BIN"] }
          ]
        }
        """;

        Assert.Equal(ImportProblem.None, SettingsPortability.TryParse(json, target, out var imported));

        Assert.Single(imported.Categories);
        Assert.Equal("good", imported.Categories[0].Id);
        // Extensions are normalized on the way in, so a hand-written file behaves like a real export.
        Assert.Equal(new[] { "bin" }, imported.Categories[0].Extensions);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_file_from_another_version_keeps_what_it_knows_and_ignores_the_rest()
    {
        var target = Config();
        target.Settings.ChunkCount = 3;
        target.Settings.MaxTryAgainOnFailure = 7;

        // Names a setting this build does not have, and omits one it does.
        const string json = """
        {
          "format": 99,
          "settings": { "ChunkCount": 4, "SomethingFromTheFuture": true },
          "categories": [ { "Id": "other", "Name": "Other", "Icon": "file" } ]
        }
        """;

        Assert.Equal(ImportProblem.None, SettingsPortability.TryParse(json, target, out var imported));

        // Recognized: applied.
        Assert.Equal(4, imported.Settings.ChunkCount);
        // Omitted: keeps the value in force, rather than snapping to a type default.
        Assert.Equal(7, imported.Settings.MaxTryAgainOnFailure);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_machine_specific_value_in_a_hand_edited_file_is_ignored()
    {
        var target = Config("/home/someone/Downloads");
        var json = """
        {
          "format": 1,
          "settings": { "DefaultSavePath": "/somewhere/else", "LocalApiPort": 15155 },
          "categories": [ { "Id": "other", "Name": "Other", "Icon": "file" } ]
        }
        """;

        Assert.Equal(ImportProblem.None, SettingsPortability.TryParse(json, target, out var imported));
        SettingsPortability.Apply(target, imported);

        Assert.Equal("/home/someone/Downloads", target.Settings.DefaultSavePath);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_unrecognized_icon_key_survives_the_round_trip()
    {
        var source = Config();
        source.Categories.First(c => c.Id == "video").Icon = "an-icon-from-the-future";
        var target = Config();

        Assert.Equal(ImportProblem.None,
            SettingsPortability.TryParse(SettingsPortability.Export(source), target, out var imported));
        SettingsPortability.Apply(target, imported);

        // It renders as the default icon; dropping the key would make the round trip destructive.
        Assert.Equal("an-icon-from-the-future", target.Categories.First(c => c.Id == "video").Icon);
    }
}
