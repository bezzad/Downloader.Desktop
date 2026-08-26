using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// Queue-level operations on <see cref="DownloadManager"/>: start/pause/stop a whole queue, move
/// items between queues, reorder priority, and add/remove queues.
///
/// Two distinctions here have each been a reported bug and are easy to break again:
/// <list type="bullet">
/// <item>Pause and Stop are NOT the same. Pause suspends only the running items (the Queues page
/// toggle); Stop cancels everything waiting as well. Stopping only the running rows lets the pump
/// immediately refill the freed slots from the still-queued rows — the "3 stop, 3 start" bug.</item>
/// <item>A queue's <c>IsRunning</c> is persisted and gates the pump, so a queue saved as stopped
/// silently swallows every later start until something flips it back.</item>
/// </list>
///
/// No item is ever started for real: URLs point at an unreachable IP (never a hostname — DNS
/// resolution hangs the suite) and the queue is left not-running unless a test is specifically about
/// starting.
/// </summary>
public class QueueOperationTests
{
    private static (DownloadManager manager, Config config) Build()
    {
        Localizer.Instance.Load("en");
        var config = Config.New();
        var manager = new DownloadManager();
        manager.Initialize(config);
        config.DefaultQueue.IsRunning = false; // keep the pump from starting real network work
        return (manager, config);
    }

    private static DownloadItemViewModel Add(DownloadManager manager, string name, DownloadStatus status,
        string queueId = null)
    {
        var vm = manager.Add(new DownloadItem { Url = "https://10.255.255.1/" + name, FileName = name },
            autoStart: false);
        if (queueId != null)
            vm.GetItem().QueueId = queueId;
        vm.Status = status;
        return vm;
    }

    // ---- stop vs pause -----------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Stopping_a_queue_stops_the_waiting_items_too()
    {
        var (manager, config) = Build();
        var running = Add(manager, "running.bin", DownloadStatus.Running);
        var paused = Add(manager, "paused.bin", DownloadStatus.Paused);
        var queued = Add(manager, "queued.bin", DownloadStatus.Created);

        manager.StopQueue(config.DefaultQueue);

        // Everything waiting must stop as well, or the pump refills the slots the cancel just freed.
        Assert.Equal(DownloadStatus.Stopped, running.Status);
        Assert.Equal(DownloadStatus.Stopped, paused.Status);
        Assert.Equal(DownloadStatus.Stopped, queued.Status);
        Assert.False(config.DefaultQueue.IsRunning);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Stopping_a_queue_leaves_finished_and_failed_items_alone()
    {
        var (manager, config) = Build();
        var done = Add(manager, "done.bin", DownloadStatus.Completed);
        var failed = Add(manager, "failed.bin", DownloadStatus.Failed);

        manager.StopQueue(config.DefaultQueue);

        Assert.Equal(DownloadStatus.Completed, done.Status);
        Assert.Equal(DownloadStatus.Failed, failed.Status);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Pausing_a_queue_only_suspends_what_is_running()
    {
        var (manager, config) = Build();
        var running = Add(manager, "running.bin", DownloadStatus.Running);
        var queued = Add(manager, "queued.bin", DownloadStatus.Created);
        var done = Add(manager, "done.bin", DownloadStatus.Completed);

        manager.PauseQueue(config.DefaultQueue);

        Assert.Equal(DownloadStatus.Paused, running.Status);
        Assert.Equal(DownloadStatus.Created, queued.Status); // still waiting, not stopped
        Assert.Equal(DownloadStatus.Completed, done.Status);
        Assert.False(config.DefaultQueue.IsRunning);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Starting_a_queue_re_queues_stopped_and_failed_items()
    {
        var (manager, config) = Build();
        var stopped = Add(manager, "stopped.bin", DownloadStatus.Stopped);
        var failed = Add(manager, "failed.bin", DownloadStatus.Failed);
        var done = Add(manager, "done.bin", DownloadStatus.Completed);

        manager.StartQueue(config.DefaultQueue);

        // The pump only picks up Paused/Created/None, so Stopped/Failed have to be re-queued first —
        // otherwise "Start queue" after a Stop appears to do nothing at all. Whether a re-queued row
        // is still Created or has already been picked up (Running) depends on the cap, so the
        // invariant is that neither is left stranded in its terminal state.
        Assert.True(config.DefaultQueue.IsRunning);
        Assert.Contains(stopped.Status, new[] { DownloadStatus.Created, DownloadStatus.Running });
        Assert.Contains(failed.Status, new[] { DownloadStatus.Created, DownloadStatus.Running });
        Assert.Equal(DownloadStatus.Completed, done.Status); // never re-run a finished download

        manager.StopQueue(config.DefaultQueue); // don't leave real connection attempts running
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Queue_operations_ignore_a_null_queue()
    {
        var (manager, _) = Build();

        manager.StartQueue(null);
        manager.PauseQueue(null);
        manager.StopQueue(null);
    }

    // ---- multiple queues ---------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Adding_a_queue_announces_it_so_the_ui_can_show_it()
    {
        var (manager, config) = Build();
        var queuesChanged = 0;
        var listChanged = 0;
        manager.QueuesChanged += () => queuesChanged++;
        manager.ListChanged += () => listChanged++;

        var queue = manager.AddQueue("videos");

        Assert.NotNull(queue);
        Assert.Contains(config.Queues, q => q.Id == queue.Id && q.Name == "videos");
        Assert.True(queuesChanged > 0, "the Queues page reconciles its cards from this event");
        // The main grid's per-row Queue column only appears once a second queue exists, and it is
        // ListChanged that re-raises ShowQueue.
        Assert.True(listChanged > 0, "AddQueue must notify the list or the Queue column stays hidden");
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Removing_a_queue_deactivates_the_schedules_pointing_at_it()
    {
        var (manager, config) = Build();
        var queue = manager.AddQueue("nightly");
        config.Schedules.Add(new DownloadSchedule { Enabled = true, TargetQueueId = queue.Id });

        manager.RemoveQueue(queue);

        // A schedule aimed at a deleted queue would act on nothing (or worse, on a recycled id).
        var schedule = config.Schedules.Single();
        Assert.False(schedule.Enabled);
        Assert.Null(schedule.TargetQueueId);
        Assert.DoesNotContain(config.Queues, q => q.Id == queue.Id);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Moving_an_item_to_another_queue_updates_its_queue_id()
    {
        var (manager, config) = Build();
        var target = manager.AddQueue("videos");
        target.IsRunning = false;
        var item = Add(manager, "a.bin", DownloadStatus.Created);

        manager.MoveToQueue(item, target.Id);

        Assert.Equal(target.Id, item.GetItem().QueueId);
        Assert.Equal("videos", item.QueueName);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Moving_to_the_same_queue_or_a_missing_one_changes_nothing()
    {
        var (manager, config) = Build();
        var item = Add(manager, "a.bin", DownloadStatus.Created);
        var original = item.GetItem().QueueId;

        manager.MoveToQueue(item, original);
        manager.MoveToQueue(item, null);
        manager.MoveToQueue(item, string.Empty);
        manager.MoveToQueue(null, original);

        Assert.Equal(original, item.GetItem().QueueId);
    }

    // ---- ordering = pump priority -----------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Raising_an_items_priority_moves_it_up_the_master_list()
    {
        var (manager, _) = Build();
        var first = Add(manager, "first.bin", DownloadStatus.Created);
        var second = Add(manager, "second.bin", DownloadStatus.Created);

        manager.MovePriority(second, -1);

        // The master list order IS the pump order, so this is what changes which one starts next.
        Assert.Equal(new[] { "second.bin", "first.bin" },
            manager.Items.Select(i => i.FileName).ToArray());
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Priority_moves_stop_at_the_ends_of_the_queue()
    {
        var (manager, _) = Build();
        var first = Add(manager, "first.bin", DownloadStatus.Created);
        var second = Add(manager, "second.bin", DownloadStatus.Created);

        manager.MovePriority(first, -1);  // already top
        manager.MovePriority(second, 1);  // already bottom
        manager.MovePriority(first, 0);   // no direction
        manager.MovePriority(null, -1);

        Assert.Equal(new[] { "first.bin", "second.bin" },
            manager.Items.Select(i => i.FileName).ToArray());
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Dropping_a_row_above_another_reorders_the_list()
    {
        var (manager, _) = Build();
        var a = Add(manager, "a.bin", DownloadStatus.Created);
        var b = Add(manager, "b.bin", DownloadStatus.Created);
        var c = Add(manager, "c.bin", DownloadStatus.Created);

        manager.ReorderTo(c, a, placeAfter: false); // drag "c" above "a"

        Assert.Equal(new[] { "c.bin", "a.bin", "b.bin" },
            manager.Items.Select(i => i.FileName).ToArray());
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Dropping_a_row_below_another_reorders_the_list()
    {
        var (manager, _) = Build();
        var a = Add(manager, "a.bin", DownloadStatus.Created);
        var b = Add(manager, "b.bin", DownloadStatus.Created);
        var c = Add(manager, "c.bin", DownloadStatus.Created);

        manager.ReorderTo(a, c, placeAfter: true); // drag "a" below "c"

        Assert.Equal(new[] { "b.bin", "c.bin", "a.bin" },
            manager.Items.Select(i => i.FileName).ToArray());
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Dropping_a_row_onto_a_row_in_another_queue_adopts_that_queue()
    {
        var (manager, config) = Build();
        var other = manager.AddQueue("videos");
        other.IsRunning = false;

        var mine = Add(manager, "mine.bin", DownloadStatus.Created);
        var theirs = Add(manager, "theirs.bin", DownloadStatus.Created, other.Id);

        manager.ReorderTo(mine, theirs, placeAfter: true);

        Assert.Equal(other.Id, mine.GetItem().QueueId);
        Assert.Equal("videos", mine.QueueName);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Reorder_ignores_nonsense_arguments()
    {
        var (manager, _) = Build();
        var a = Add(manager, "a.bin", DownloadStatus.Created);
        var b = Add(manager, "b.bin", DownloadStatus.Created);
        var orphan = new DownloadItemViewModel(new DownloadItem { Url = "https://10.255.255.1/x" }, manager);

        manager.ReorderTo(a, a, placeAfter: true);   // onto itself
        manager.ReorderTo(null, b, placeAfter: true);
        manager.ReorderTo(a, null, placeAfter: true);
        manager.ReorderTo(orphan, a, placeAfter: true); // not in the list

        Assert.Equal(new[] { "a.bin", "b.bin" }, manager.Items.Select(i => i.FileName).ToArray());
    }

    // ---- "already downloaded" ---------------------------------------------

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(FileExistPolicy.Delete, false)]
    public void Only_the_ignore_policy_treats_an_existing_file_as_already_downloaded(
        FileExistPolicy policy, bool expected)
    {
        var path = Path.Combine(Path.GetTempPath(), "downloader-exists-" + System.Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, "hello");
        try
        {
            Assert.Equal(expected, DownloadManager.LooksAlreadyDownloaded(policy, path));

            // With the app's default policy the engine SKIPS the download, which arrives as a
            // "cancelled" completion — it must read as "already downloaded", not as a failure.
            Assert.True(DownloadManager.LooksAlreadyDownloaded(FileExistPolicy.IgnoreDownload, path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_missing_file_is_never_already_downloaded()
    {
        var missing = Path.Combine(Path.GetTempPath(), "downloader-missing-" + System.Guid.NewGuid().ToString("N"));

        Assert.False(DownloadManager.LooksAlreadyDownloaded(FileExistPolicy.IgnoreDownload, missing));
        Assert.False(DownloadManager.LooksAlreadyDownloaded(FileExistPolicy.IgnoreDownload, null));
        Assert.False(DownloadManager.LooksAlreadyDownloaded(FileExistPolicy.IgnoreDownload, string.Empty));
    }
}
