using System;
using System.Collections.Generic;
using Avalonia.Styling;

namespace Downloader.Desktop.Models;

public class Config
{
    public int DefaultDownloadChunks { get; set; }
    public string DefaultSavePath { get; set; }
    public List<DownloadItem> Downloads { get; set; }
    public ThemeVariant ThemeMode { get; set; }
    
    public static Config New()
    {
        return new Config()
        {
            Downloads = [],
            DefaultSavePath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            DefaultDownloadChunks = 4,
            ThemeMode = ThemeVariant.Light
        };
    }
}