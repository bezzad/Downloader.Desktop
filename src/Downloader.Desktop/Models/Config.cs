using Avalonia.Styling;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Downloader.Desktop.Models;

/// <summary>
/// Persisted application state: engine settings, the download list, queues, schedules and theme.
/// Saved as JSON by <see cref="Services.FileService"/>.
/// </summary>
public class Config
{
    /// <summary>Bumped when a load-time migration is added; see <see cref="EnsureValid"/>.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Config format version for one-time migrations. 0 = written before the field existed.</summary>
    public int SchemaVersion { get; set; }

    public DownloadSettings Settings { get; set; }
    public List<DownloadItem> Downloads { get; set; }
    public List<DownloadQueue> Queues { get; set; }
    public List<DownloadSchedule> Schedules { get; set; }
    /// <summary>Ids of plugins the user turned OFF (so they stay disabled across restarts).</summary>
    public List<string> DisabledPlugins { get; set; }
    public bool IsThemeDarkMode { get; set; }

    /// <summary>Last user-resized dimensions of each modal window type, keyed by a constant name (e.g. "AddDownload").</summary>
    public Dictionary<string, WindowSize> WindowSizes { get; set; }

    [JsonIgnore]
    public ThemeVariant ThemeMode
    {
        get => IsThemeDarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
        set => IsThemeDarkMode = value == ThemeVariant.Dark;
    }

    /// <summary>The default/primary queue items land in when none is specified.</summary>
    [JsonIgnore]
    public DownloadQueue DefaultQueue
    {
        get
        {
            Queues ??= new List<DownloadQueue>();
            if (Queues.Count == 0)
                Queues.Add(new DownloadQueue { Name = DownloadQueue.DefaultName });
            return Queues[0];
        }
    }

    public static Config New()
    {
        var settings = DownloadSettings.New();
        return new Config
        {
            SchemaVersion = CurrentSchemaVersion,
            Settings = settings,
            Downloads = new List<DownloadItem>(),
            Queues = new List<DownloadQueue>
            {
                new() { Name = DownloadQueue.DefaultName, MaxConcurrent = settings.MaxConcurrentDownloads }
            },
            Schedules = new List<DownloadSchedule>(),
            IsThemeDarkMode = false,
            WindowSizes = new Dictionary<string, WindowSize>()
        };
    }

    /// <summary>Fills in any missing pieces on a config loaded from disk (forward/backward compat).</summary>
    public Config EnsureValid()
    {
        Settings ??= DownloadSettings.New();
        if (string.IsNullOrWhiteSpace(Settings.DefaultSavePath))
            Settings.DefaultSavePath = DownloadSettings.New().DefaultSavePath;

        // Migrate the old, too-aggressive per-block read deadline. Configs written before the fix
        // persisted BlockTimeout=1000ms (1 s per 8 KB block), which falsely fails healthy but bursty
        // downloads with "connection timed out". Bump any value at/below that old default up to the
        // new safe default so existing users get the fix without touching Settings manually.
        if (Settings.BlockTimeout <= 1000)
            Settings.BlockTimeout = DownloadSettings.New().BlockTimeout;
        Downloads ??= new List<DownloadItem>();
        Queues ??= new List<DownloadQueue>();
        if (Queues.Count == 0)
            Queues.Add(new DownloadQueue { Name = DownloadQueue.DefaultName, MaxConcurrent = Settings.MaxConcurrentDownloads });
        Schedules ??= new List<DownloadSchedule>();
        WindowSizes ??= new Dictionary<string, WindowSize>();

        // v0 → v1: integration became on-by-default when the local API shipped. Configs written
        // before then persisted false without ever asking the user, so flip it ONCE; any value the
        // user sets afterwards is versioned as v1 and never touched again.
        if (SchemaVersion < 1)
            Settings.EnableBrowserIntegration = true;

        SchemaVersion = CurrentSchemaVersion;
        return this;
    }
}

/// <summary>Persisted width/height of a remembered modal window type.</summary>
public class WindowSize
{
    public double Width { get; set; }
    public double Height { get; set; }
}
