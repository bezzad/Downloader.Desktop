using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Downloader.Desktop.Plugins;

namespace Downloader.Desktop.Models;

/// <summary>
/// A JSON-persistable copy of a resolver's <see cref="DownloadPlan"/> (the SDK types use init-only
/// members + read-only collections that don't round-trip cleanly through System.Text.Json). Stored on
/// <see cref="DownloadItem.PlanJson"/> so a multi-part / post-process download survives an app restart
/// and resumes from the first incomplete part.
/// </summary>
public sealed class PersistedPlan
{
    public string SuggestedFileName { get; set; }
    public List<PersistedPart> Parts { get; set; } = new();
    public PostProcessKind PostProcessKind { get; set; } = PostProcessKind.None;
    public string PostProcessRecipe { get; set; }

    /// <summary>A plan that needs the runner (more than one part, or any post-processing).</summary>
    public bool NeedsRunner => Parts.Count > 1 || PostProcessKind != PostProcessKind.None;

    public static PersistedPlan From(DownloadPlan plan) => new()
    {
        SuggestedFileName = plan.SuggestedFileName,
        Parts = plan.Parts.Select(p => new PersistedPart
        {
            Url = p.Url,
            Kind = p.Kind,
            ExpectedSize = p.ExpectedSize,
            Headers = p.Headers?.ToDictionary(kv => kv.Key, kv => kv.Value)
        }).ToList(),
        PostProcessKind = plan.PostProcess?.Kind ?? PostProcessKind.None,
        PostProcessRecipe = plan.PostProcess?.Recipe
    };

    public PostProcess ToPostProcess() => new() { Kind = PostProcessKind, Recipe = PostProcessRecipe };

    public string ToJson() => JsonSerializer.Serialize(this);

    public static PersistedPlan FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try { return JsonSerializer.Deserialize<PersistedPlan>(json); }
        catch { return null; }
    }
}

public sealed class PersistedPart
{
    public string Url { get; set; }
    public PartKind Kind { get; set; } = PartKind.Combined;
    public long? ExpectedSize { get; set; }
    public Dictionary<string, string> Headers { get; set; }
}
