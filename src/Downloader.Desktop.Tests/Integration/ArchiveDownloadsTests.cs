using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// Archiving a download (issue #17). The rules that matter are the pair that keeps an archived download
/// INERT — archiving stops it, starting restores it — because everything else (the queue pump skipping it,
/// the totals ignoring it, the filters hiding it) is only safe while that holds. A download that were both
/// archived and queued would keep downloading where nobody can see it.
/// <para>
/// These drive the manager with the repo's unreachable address, so nothing is downloaded: <c>Start</c> sets
/// Running synchronously before its first await, which is what makes the transitions observable.
/// </para>
/// </summary>
public class ArchiveDownloadsTests
{
    // ── the invariant ────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Archiving_a_running_download_stops_it_first()
    {
        var (manager, _) = NewManager();
        var vm = Add(manager, autoStart: true);
        Assert.Equal(DownloadStatus.Running, vm.Status);

        manager.Archive(vm);

        Assert.True(vm.IsArchived);
        Assert.Equal(DownloadStatus.Stopped, vm.Status);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Archiving_a_queued_download_takes_it_out_of_the_queue()
    {
        var (manager, config) = NewManager();
        config.DefaultQueue.MaxConcurrent = 1;
        var running = Add(manager, autoStart: true);
        var waiting = Add(manager, autoStart: true); // capped out, so it sits queued

        // Both Created and None mean "waiting for a slot" to the manager; which one a capped-out item
        // carries is an engine detail, so assert what the app treats as equivalent.
        Assert.True(waiting.Status is DownloadStatus.Created or DownloadStatus.None, $"was {waiting.Status}");
        manager.Archive(waiting);

        Assert.True(waiting.IsArchived);
        Assert.Equal(DownloadStatus.Stopped, waiting.Status);

        // Freeing the slot must not resurrect it — here because Archive stopped it, which is the
        // cancel-first half of the invariant. The pump's own guard is pinned below.
        manager.Cancel(running);
        manager.PumpQueue(config.DefaultQueue.Id);
        Assert.Equal(DownloadStatus.Stopped, waiting.Status);
        Assert.True(waiting.IsArchived);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_pump_will_not_resume_an_archived_row()
    {
        // Defence in depth for the pump itself: a row that is archived AND paused is exactly what the
        // cancel-first rule is meant to prevent, so if it ever happens the pump must not run it anyway.
        var (manager, config) = NewManager();
        var vm = Add(manager, autoStart: false);
        manager.Archive(vm);
        vm.Status = DownloadStatus.Paused; // the state the pump picks up first

        manager.PumpQueue(config.DefaultQueue.Id);

        Assert.Equal(DownloadStatus.Paused, vm.Status);
        Assert.True(vm.IsArchived);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_completed_download_is_archived_without_changing_its_state()
    {
        var (manager, _) = NewManager();
        var vm = Add(manager, autoStart: false);
        vm.Status = DownloadStatus.Completed;

        manager.Archive(vm);

        Assert.True(vm.IsArchived);
        Assert.Equal(DownloadStatus.Completed, vm.Status); // archiving is not a stop for a finished row
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Restoring_puts_the_row_back_in_the_state_it_was_filed_away_in()
    {
        var (manager, _) = NewManager();
        var vm = Add(manager, autoStart: false);
        vm.Status = DownloadStatus.Failed;
        manager.Archive(vm);

        manager.Unarchive(vm);

        Assert.False(vm.IsArchived);
        Assert.Equal(DownloadStatus.Failed, vm.Status); // restoring is not a start
    }

    [AvaloniaTheory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("resume")]
    [InlineData("retry")]
    [InlineData("start")]
    public void Starting_an_archived_download_takes_it_out_of_the_archive(string how)
    {
        var (manager, _) = NewManager();
        var vm = Add(manager, autoStart: false);
        vm.Status = DownloadStatus.Stopped;
        manager.Archive(vm);
        Assert.True(vm.IsArchived);

        switch (how)
        {
            case "resume": manager.Resume(vm); break;
            case "retry": manager.Retry(vm); break;
            default: manager.Start(vm); break;
        }

        Assert.False(vm.IsArchived);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void No_path_leaves_a_download_both_archived_and_live()
    {
        var (manager, config) = NewManager();
        var archived = Add(manager, autoStart: true);
        manager.Archive(archived);
        var ordinary = Add(manager, autoStart: false);
        ordinary.Status = DownloadStatus.Stopped;

        // Every bulk start path in the app, one after another.
        manager.StartAll();
        manager.StartQueue(config.DefaultQueue);
        manager.PumpQueue(config.DefaultQueue.Id);

        Assert.True(archived.IsArchived);
        Assert.Equal(DownloadStatus.Stopped, archived.Status);
        // …and the ordinary one really was started, so the test is not passing because nothing ran.
        Assert.NotEqual(DownloadStatus.Stopped, ordinary.Status);
    }

    // ── taking no part in anything ───────────────────────────────────────────────────────────────────

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Stopping_everything_leaves_the_archived_rows_alone()
    {
        var (manager, _) = NewManager();
        var archived = Add(manager, autoStart: false);
        archived.Status = DownloadStatus.Paused;
        manager.Archive(archived);
        archived.Status = DownloadStatus.Paused; // filed away mid-download, kept as it was

        manager.StopAll();

        Assert.Equal(DownloadStatus.Paused, archived.Status);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_archived_row_is_absent_from_the_counts_and_the_totals()
    {
        var (manager, _) = NewManager();
        // A COMPLETED row, deliberately: archiving a running one stops it, so its disappearance from
        // ActiveCount would prove nothing about the counts themselves. Nothing about completion changes
        // when it is archived, so only the exclusion can make this number move.
        var done = Add(manager, autoStart: false);
        done.Status = DownloadStatus.Completed;
        var running = Add(manager, autoStart: true);
        running.Speed = 1024;

        Assert.Equal(1, manager.CompletedCount);
        Assert.Equal(1024, manager.TotalSpeed);

        manager.Archive(done);
        manager.Archive(running);

        Assert.Equal(0, manager.CompletedCount);
        Assert.Equal(0, manager.TotalSpeed);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_archived_row_is_absent_from_its_queue_card()
    {
        var (manager, config) = NewManager();
        var vm = Add(manager, autoStart: false);
        var page = new QueuesViewModel(config, manager);
        var card = page.Queues.First(q => q.Queue.Id == config.DefaultQueue.Id);
        card.IsExpanded = true; // a collapsed card holds no wrappers at all
        card.RebuildItems();
        Assert.Single(card.Items);

        manager.Archive(vm); // NotifyList → the card rebuilds itself

        Assert.Empty(card.Items);
        Assert.Equal(0, card.RunningCount);
        Assert.Equal(0, card.WaitingCount);
    }

    // ── the record ───────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_archived_flag_survives_a_save_and_load()
    {
        var config = Config.New();
        config.Downloads = new List<DownloadItem>
        {
            new() { Urls = { "https://10.255.255.1/a.zip" }, IsArchived = true, Status = DownloadStatus.Completed },
            new() { Urls = { "https://10.255.255.1/b.zip" } },
        };

        var loaded = JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(config));

        Assert.True(loaded.Downloads[0].IsArchived);
        Assert.False(loaded.Downloads[1].IsArchived);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────

    private static (DownloadManager Manager, Config Config) NewManager()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        var config = Config.New();
        config.Settings.MaxTryAgainOnFailure = 1;
        config.Settings.DefaultSavePath = TempDir();
        manager.Initialize(config);
        return (manager, config);
    }

    private static DownloadItemViewModel Add(DownloadManager manager, bool autoStart) =>
        manager.Add(new DownloadItem
        {
            Urls = new List<string> { "https://10.255.255.1/archive-me.bin" },
            SaveFolder = TempDir(),
            FileName = "archive-me.bin",
        }, autoStart);

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dldesktop-archive-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
