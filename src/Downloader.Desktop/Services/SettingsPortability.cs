using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Downloader.Desktop.Models;

namespace Downloader.Desktop.Services;

/// <summary>What went wrong reading an export file, or <see cref="None"/> when nothing did.</summary>
public enum ImportProblem
{
    None,

    /// <summary>Not valid JSON, or not shaped like one of our exports.</summary>
    Unreadable,

    /// <summary>Valid, but lists no categories — applying it would leave the app with none.</summary>
    NoCategories
}

/// <summary>The settings and categories read out of an export file.</summary>
public sealed class ImportedSettings
{
    /// <summary>The settings to apply, already merged over the ones in force so that a value this
    /// version does not recognize is ignored and one the file omits keeps its current value.</summary>
    public DownloadSettings Settings { get; init; }

    /// <summary>The category list to install, never empty.</summary>
    public List<DownloadCategory> Categories { get; init; }
}

/// <summary>
/// Carries a user's settings and categories between machines.
/// </summary>
/// <remarks>
/// Deliberately narrow: the download list, the queues and the schedules are not portable (they
/// describe work in progress on one machine), and neither are values that only mean something where
/// they were written — the save folder, the port the local API happened to bind, the remembered
/// window sizes. What is left contains no file-system paths and nothing platform-specific, so a file
/// written on one operating system applies cleanly on another.
/// </remarks>
public static class SettingsPortability
{
    /// <summary>Bumped if the shape ever changes incompatibly. Readers ignore what they do not know,
    /// so adding a field does not need a bump.</summary>
    public const int CurrentFormat = 1;

    /// <summary>The suggested file name for an export.</summary>
    public const string DefaultFileName = "downloader-settings.json";

    /// <summary>
    /// Settings that mean something only on the machine that wrote them. Excluded from an export,
    /// and ignored on import even if a hand-edited file carries them.
    /// </summary>
    private static readonly string[] MachineSpecific =
    {
        nameof(DownloadSettings.DefaultSavePath),
        nameof(DownloadSettings.LocalApiPort)
    };

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Renders the portable part of a configuration as JSON.</summary>
    public static string Export(Config config)
    {
        var settings = JsonSerializer.SerializeToNode(config?.Settings ?? DownloadSettings.New())?.AsObject()
                       ?? new JsonObject();
        foreach (var key in MachineSpecific)
            settings.Remove(key);

        var categories = new JsonArray();
        foreach (var category in (config?.Categories ?? new List<DownloadCategory>()).OrderBy(c => c.Position))
            categories.Add(JsonSerializer.SerializeToNode(category));

        var document = new JsonObject
        {
            ["format"] = CurrentFormat,
            ["app"] = "Downloader",
            ["settings"] = settings,
            ["categories"] = categories
        };

        return document.ToJsonString(Options);
    }

    /// <summary>
    /// Reads an export, validating it fully <b>before</b> anything is applied, so a bad file leaves
    /// the current settings and categories untouched.
    /// </summary>
    public static ImportProblem TryParse(string json, Config current, out ImportedSettings imported)
    {
        imported = null;
        if (string.IsNullOrWhiteSpace(json))
            return ImportProblem.Unreadable;

        JsonObject document;
        try
        {
            document = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return ImportProblem.Unreadable;
        }

        if (document is null || document["settings"] is not JsonObject incoming)
            return ImportProblem.Unreadable;

        var categories = ReadCategories(document["categories"] as JsonArray);
        if (categories.Count == 0)
            return ImportProblem.NoCategories;

        DownloadSettings settings;
        try
        {
            settings = MergeSettings(current?.Settings ?? DownloadSettings.New(), incoming);
        }
        catch (JsonException)
        {
            return ImportProblem.Unreadable;
        }

        if (settings is null)
            return ImportProblem.Unreadable;

        imported = new ImportedSettings { Settings = settings, Categories = categories };
        return ImportProblem.None;
    }

    /// <summary>
    /// Overlays the file's settings onto the ones in force. A value this version does not recognize
    /// is dropped by the deserializer; one the file omits keeps whatever the user has now, which is
    /// what makes a file from an older version safe to import.
    /// </summary>
    private static DownloadSettings MergeSettings(DownloadSettings inForce, JsonObject incoming)
    {
        var merged = JsonSerializer.SerializeToNode(inForce)?.AsObject() ?? new JsonObject();
        foreach (var pair in incoming)
        {
            if (MachineSpecific.Contains(pair.Key))
                continue;

            merged[pair.Key] = pair.Value?.DeepClone();
        }

        return merged.Deserialize<DownloadSettings>(Options);
    }

    private static List<DownloadCategory> ReadCategories(JsonArray array)
    {
        var categories = new List<DownloadCategory>();
        if (array is null)
            return categories;

        foreach (var node in array)
        {
            DownloadCategory category;
            try
            {
                category = node.Deserialize<DownloadCategory>(Options);
            }
            catch (JsonException)
            {
                // One malformed entry does not condemn the file; the "at least one category"
                // check below is what decides whether there is enough here to use.
                continue;
            }

            if (category is null ||
                string.IsNullOrWhiteSpace(category.Id) ||
                string.IsNullOrWhiteSpace(category.Name))
                continue;

            category.Extensions = CategoryService.NormalizeExtensions(category.Extensions);
            category.MediaTypes ??= new List<string>();
            categories.Add(category);
        }

        for (var i = 0; i < categories.Count; i++)
            categories[i].Position = i;

        return categories;
    }

    /// <summary>
    /// Installs a parsed import over a configuration. Downloads whose explicit category is not in the
    /// incoming list lose that choice and go back to being worked out, so no download is left
    /// pointing at a category that does not exist.
    /// </summary>
    public static void Apply(Config config, ImportedSettings imported)
    {
        if (config is null || imported is null)
            return;

        // The save folder is not portable, so the one in force is kept whatever the file said.
        var savePath = config.Settings?.DefaultSavePath;
        var apiPort = config.Settings?.LocalApiPort ?? 0;

        config.Settings = imported.Settings;
        config.Settings.DefaultSavePath = savePath;
        config.Settings.LocalApiPort = apiPort;
        config.Categories = imported.Categories;

        var known = new HashSet<string>(imported.Categories.Select(c => c.Id), StringComparer.Ordinal);
        foreach (var download in config.Downloads ?? new List<DownloadItem>())
        {
            if (!string.IsNullOrWhiteSpace(download.CategoryId) && !known.Contains(download.CategoryId))
                download.CategoryId = null;
        }
    }
}
