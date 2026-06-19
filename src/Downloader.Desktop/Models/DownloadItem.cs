using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace Downloader.Desktop.Models;

/// <summary>
/// A persisted download record. The folder and file name are stored separately so a download can be
/// added with only a URL + folder and have the engine resolve the real file name later.
/// </summary>
public class DownloadItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string QueueId { get; set; }

    /// <summary>
    /// All source URLs for the same file: the first is the primary, the rest are mirrors/fallbacks.
    /// A single URL simply means "no mirrors". The engine consumes the whole array
    /// (<c>DownloadFileTaskAsync(string[] urls, …)</c>) so mirrors are real fallbacks, not just metadata.
    /// </summary>
    public List<string> Urls { get; set; } = new();

    /// <summary>Primary URL (first entry of <see cref="Urls"/>). Convenience accessor for the UI.</summary>
    [JsonIgnore]
    public string Url
    {
        get => Urls is { Count: > 0 } ? Urls[0] : null;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            if (Urls.Count == 0)
                Urls.Add(value);
            else
                Urls[0] = value;
        }
    }

    /// <summary>Mirror URLs only (everything after the primary).</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Mirrors => Urls.Count > 1 ? Urls.Skip(1).ToList() : Array.Empty<string>();

    /// <summary>Replaces the mirror list, keeping the primary URL intact.</summary>
    public void SetMirrors(IEnumerable<string> mirrors)
    {
        var primary = Url;
        Urls = new List<string>();
        if (!string.IsNullOrWhiteSpace(primary))
            Urls.Add(primary);
        if (mirrors != null)
            Urls.AddRange(mirrors.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()));
    }

    // ---- Legacy config migration (pre-Urls schema: single "Url" + "Mirrors" array) ----
    // Getters return null so they are not re-serialized (FileService ignores nulls when writing).
    [JsonPropertyName("Url")]
    public string LegacyUrl
    {
        get => null;
        set { if (!string.IsNullOrWhiteSpace(value) && !Urls.Contains(value)) Urls.Insert(0, value); }
    }

    [JsonPropertyName("Mirrors")]
    public List<string> LegacyMirrors
    {
        get => null;
        set
        {
            if (value == null) return;
            foreach (var m in value)
                if (!string.IsNullOrWhiteSpace(m) && !Urls.Contains(m))
                    Urls.Add(m);
        }
    }

    /// <summary>Directory the file is saved into.</summary>
    public string SaveFolder { get; set; }

    /// <summary>File name; may be null/empty until the engine resolves it from the URL/headers.</summary>
    public string FileName { get; set; }

    /// <summary>
    /// Display-only name resolved from the URL/Content-Disposition before the download actually starts
    /// (for items still waiting on a queue slot). Persisted so a queued item keeps showing its name
    /// after an app restart instead of reverting to "unnamed" (the engine still resolves the
    /// authoritative <see cref="FileName"/> when it starts).
    /// </summary>
    public string PreviewName { get; set; }

    /// <summary>Optional grouping label (e.g. the batch a multi-URL add created). Null = ungrouped.</summary>
    public string Group { get; set; }

    public long? Size { get; set; }
    public long Downloaded { get; set; }
    public DateTime? LastTry { get; set; }
    public DownloadStatus Status { get; set; }

    /// <summary>Human-readable reason for the last failure (shown in the UI).</summary>
    public string LastError { get; set; }

    [JsonIgnore]
    public string FolderPath => SaveFolder;

    [JsonIgnore]
    public string FilePath =>
        string.IsNullOrWhiteSpace(FileName)
            ? SaveFolder
            : Path.Combine(SaveFolder ?? string.Empty, FileName);
}
