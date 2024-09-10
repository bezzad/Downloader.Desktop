using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Downloader.Desktop.Models;

public class Config
{
    public int DefaultDownloadChunks { get; set; }
    public string DefaultSavePath { get; set; }
    public List<DownloadItem> Downloads { get; set; }
    public bool IsThemeDarkMode { get; set; }

    [JsonIgnore]
    public ThemeVariant ThemeMode
    {
        get => IsThemeDarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
        set => IsThemeDarkMode = value == ThemeVariant.Dark;
    }

    public static Config New()
    {
        return new Config()
        {
            Downloads = [],
            DefaultSavePath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            DefaultDownloadChunks = 4,
            IsThemeDarkMode = false
        };
    }
}