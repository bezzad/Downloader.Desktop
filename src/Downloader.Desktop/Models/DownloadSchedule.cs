using System;

namespace Downloader.Desktop.Models;

/// <summary>
/// A time rule that starts (and optionally stops) a target queue or a single download within a
/// daily window, on selected days. Evaluated periodically by the download manager.
/// </summary>
public class DownloadSchedule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; }

    /// <summary>Queue this schedule controls (mutually exclusive with <see cref="TargetItemId"/>).</summary>
    public string TargetQueueId { get; set; }

    /// <summary>Single download this schedule controls.</summary>
    public Guid? TargetItemId { get; set; }

    public TimeSpan StartTime { get; set; }

    /// <summary>Optional time to stop/pause the target. Null = run until finished.</summary>
    public TimeSpan? StopTime { get; set; }

    /// <summary>Days the schedule is active. Empty/null = every day.</summary>
    public DayOfWeek[] Days { get; set; }

    /// <summary>When true, the schedule disables itself after firing once.</summary>
    public bool Once { get; set; }

    public bool Enabled { get; set; } = true;
}
