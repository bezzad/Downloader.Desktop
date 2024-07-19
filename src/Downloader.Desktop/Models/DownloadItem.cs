using System;
using System.IO;
using System.Text.Json.Serialization;

namespace Downloader.Desktop.Models;

public class DownloadItem
{
    private string? _folderPath;
    private string? _fileName;

    public string? FilePath { get; set; }
    public string? Url { get; set; }
    public long? Size { get; set; }
    public long Downloaded { get; set; }
    public DateTime? LastTry { get; set; }
    public DownloadStatus Status { get; set; }

    [JsonIgnore]
    public string? FileName { get => _fileName ?? (_fileName = Path.GetFileName(FilePath)); set => _fileName = value; }
    [JsonIgnore]
    public string? FolderPath => _folderPath ?? (_folderPath = Path.GetDirectoryName(FilePath));
}