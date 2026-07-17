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

/// <summary>Status filter applied to the downloads table. The non-All buckets are DISJOINT and jointly
/// exhaustive (each status belongs to exactly one): Active=Running, Queued=Created/None,
/// Stopped=Paused+Stopped, Completed, Failed. Stopped exists so paused items — which normalize to
/// Stopped after a restart — are never lost (#2).</summary>
public enum StatusFilter
{
    All,
    Active,
    Queued,
    Stopped,
    Completed,
    Failed
}
