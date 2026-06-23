namespace Downloader.Desktop.ViewModels;

/// <summary>Top-level destinations shown in the central content area.</summary>
public enum NavSection
{
    Downloads,
    Queues,
    Scheduler,
    Plugins,
    Settings
}

/// <summary>Status filter applied to the downloads table.</summary>
public enum StatusFilter
{
    All,
    Active,
    Queued,
    Completed,
    Failed
}
