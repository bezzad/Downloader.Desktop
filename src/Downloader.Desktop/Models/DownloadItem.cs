using System.IO;

namespace Downloader.Desktop.Models;

public class DownloadItem
{
    private string? _folderPath;

    public string? FolderPath { get => _folderPath ?? Path.GetDirectoryName(FileName); set => _folderPath = value; }
    public string? FileName { get; set; }
    public string? Url { get; set; }
    public bool ValidateData { get; set; }
    public DownloadStatus Status { get; set; }
}
