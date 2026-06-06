using System;

namespace Downloader.Desktop.Models;

/// <summary>
/// A named group of downloads with a concurrency cap and an optional schedule.
/// Downloads reference their queue via <see cref="DownloadItem.QueueId"/>.
/// </summary>
public class DownloadQueue
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; }

    /// <summary>How many items in this queue may download at the same time.</summary>
    public int MaxConcurrent { get; set; } = 3;

    /// <summary>When false, the queue is paused and starts nothing automatically.</summary>
    public bool IsRunning { get; set; } = true;

    /// <summary>Optional schedule controlling when this queue runs.</summary>
    public string ScheduleId { get; set; }

    public const string DefaultName = "Main queue";
}
