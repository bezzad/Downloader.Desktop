using System;
using System.IO;
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
    public string Url { get; set; }

    /// <summary>Directory the file is saved into.</summary>
    public string SaveFolder { get; set; }

    /// <summary>File name; may be null/empty until the engine resolves it from the URL/headers.</summary>
    public string FileName { get; set; }

    public long? Size { get; set; }
    public long Downloaded { get; set; }
    public DateTime? LastTry { get; set; }
    public DownloadStatus Status { get; set; }

    [JsonIgnore]
    public string FolderPath => SaveFolder;

    [JsonIgnore]
    public string FilePath =>
        string.IsNullOrWhiteSpace(FileName)
            ? SaveFolder
            : Path.Combine(SaveFolder ?? string.Empty, FileName);
}
