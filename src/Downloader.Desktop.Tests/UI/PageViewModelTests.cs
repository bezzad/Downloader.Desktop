using System.Linq;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The Queues, Scheduler and Downloads pages.
///
/// The Queues card's two editable controls both write somewhere else as well, and both have bitten
/// before: the concurrency cap must mirror back into the Settings value for the DEFAULT queue (or the
/// setting overwrites the cap on next launch), and the Run/Pause switch must go through the manager's
/// StartQueue/PauseQueue rather than flipping the flag directly.
///
/// The Downloads page's bulk actions act on checked rows PLUS DataGrid-highlighted rows, which is why
/// the grid-selection seam is exercised rather than only the checkboxes.
/// </summary>
public class PageViewModelTests
{
    private static (DownloadManager manager, Config config) Build()
    {
        Localizer.Instance.Load("en");
        var config = Config.New();
        var manager = new DownloadManager();
        manager.Initialize(config);
        config.DefaultQueue.IsRunning = false;
        return (manager, config);
    }

    private static DownloadItemViewModel Add(DownloadManager manager, string name, DownloadStatus status)
    {
        var vm = manager.Add(new DownloadItem { Url = "https://10.255.255.1/" + name, FileName = name },
            autoStart: false);
        vm.Status = status;
        return vm;
    }

    // ---- Queues page -------------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_queues_page_lists_the_configured_queues()
    {
        var (manager, config) = Build();
        var page = new QueuesViewModel(config, manager);

        Assert.NotEmpty(page.Queues);
        Assert.Contains(page.Queues, q => q.Queue.Id == config.DefaultQueue.Id);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Editing_the_default_queues_cap_mirrors_into_the_settings_value()
    {
        var (manager, config) = Build();
        var page = new QueuesViewModel(config, manager);
        var row = page.Queues.First(q => q.Queue.Id == config.DefaultQueue.Id);

        row.MaxConcurrent = 5;

        Assert.Equal(5, config.DefaultQueue.MaxConcurrent);
        // Without the mirror, the Settings value would overwrite this cap on the next launch.
        Assert.Equal(5, config.Settings.MaxConcurrentDownloads);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_queue_cap_is_never_less_than_one()
    {
        var (manager, config) = Build();
        var page = new QueuesViewModel(config, manager);
        var row = page.Queues.First();

        row.MaxConcurrent = 0;

        Assert.Equal(1, row.MaxConcurrent); // a zero cap would stall the queue forever
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_non_default_queues_cap_is_its_own()
    {
        var (manager, config) = Build();
        var extra = manager.AddQueue("videos");
        extra.IsRunning = false;
        var page = new QueuesViewModel(config, manager);
        var settingBefore = config.Settings.MaxConcurrentDownloads;

        page.Queues.First(q => q.Queue.Id == extra.Id).MaxConcurrent = 7;

        Assert.Equal(7, extra.MaxConcurrent);
        Assert.Equal(settingBefore, config.Settings.MaxConcurrentDownloads); // untouched
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_run_switch_stops_and_starts_the_queue_through_the_manager()
    {
        var (manager, config) = Build();
        var running = Add(manager, "running.bin", DownloadStatus.Running);
        var page = new QueuesViewModel(config, manager);
        var row = page.Queues.First(q => q.Queue.Id == config.DefaultQueue.Id);

        row.IsRunning = false;

        Assert.False(config.DefaultQueue.IsRunning);
        Assert.Equal(DownloadStatus.Paused, running.Status); // pause, not stop
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_queue_card_summarises_its_items()
    {
        var (manager, config) = Build();
        Add(manager, "a.bin", DownloadStatus.Running);
        Add(manager, "b.bin", DownloadStatus.Created);
        Add(manager, "c.bin", DownloadStatus.Completed);
        Add(manager, "d.bin", DownloadStatus.Failed);

        var page = new QueuesViewModel(config, manager);
        var row = page.Queues.First(q => q.Queue.Id == config.DefaultQueue.Id);

        Assert.Equal(4, row.Items.Count);
        Assert.Equal(1, row.RunningCount);
        Assert.Equal(1, row.WaitingCount);
        Assert.Equal(1, row.DoneCount);
        Assert.Equal(1, row.FailedCount);
        Assert.False(string.IsNullOrWhiteSpace(row.SummaryText));
        Assert.InRange(row.OverallProgress, 0, 100);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Move_targets_offer_every_other_queue()
    {
        var (manager, config) = Build();
        var extra = manager.AddQueue("videos");
        extra.IsRunning = false;
        Add(manager, "a.bin", DownloadStatus.Created);

        var page = new QueuesViewModel(config, manager);
        var row = page.Queues.First(q => q.Queue.Id == config.DefaultQueue.Id);
        var item = row.Items.First();

        // A row can be moved to any queue except the one it is already in.
        Assert.Contains(item.MoveTargets, t => t.Name == "videos");
        Assert.DoesNotContain(item.MoveTargets, t => t.Name == row.Name);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_queue_added_elsewhere_shows_up_on_the_page()
    {
        var (manager, config) = Build();
        var page = new QueuesViewModel(config, manager);
        var before = page.Queues.Count;

        // Queues can be created from the Add-download dialog, not just this page — the page
        // reconciles from the manager's QueuesChanged event.
        manager.AddQueue("from-the-add-dialog");

        Assert.Equal(before + 1, page.Queues.Count);
        Assert.Contains(page.Queues, q => q.Name == "from-the-add-dialog");
    }

    // ---- Scheduler page ----------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_new_schedule_can_be_created_and_removed()
    {
        var (manager, config) = Build();
        var page = new SchedulerViewModel(config, manager);

        page.NewScheduleCommand.Execute(null);

        Assert.Single(page.Schedules);
        var row = page.Schedules.First();
        Assert.NotEmpty(row.QueueOptions);

        row.Name = "nightly";
        row.Enabled = true;
        row.StartTime = new System.TimeSpan(1, 30, 0);
        row.StopTime = new System.TimeSpan(5, 0, 0);

        Assert.Equal("nightly", row.Name);
        Assert.True(row.Enabled);
        Assert.Equal(new System.TimeSpan(1, 30, 0), row.StartTime);
        Assert.Equal(new System.TimeSpan(5, 0, 0), row.StopTime);

        row.RemoveCommand.Execute(null);
        Assert.Empty(page.Schedules);
        Assert.Empty(config.Schedules);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_schedule_can_target_a_queue()
    {
        var (manager, config) = Build();
        var extra = manager.AddQueue("nightly");
        extra.IsRunning = false;
        var page = new SchedulerViewModel(config, manager);
        page.NewScheduleCommand.Execute(null);
        var row = page.Schedules.First();

        row.SelectedQueue = row.QueueOptions.First(q => q.Name == "nightly");

        Assert.Equal("nightly", row.SelectedQueue.Name);
        Assert.Equal(extra.Id, config.Schedules.First().TargetQueueId);
    }

    // ---- Downloads page ----------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Nothing_is_selected_until_a_row_is_checked_or_highlighted()
    {
        var (manager, config) = Build();
        Add(manager, "a.bin", DownloadStatus.Running);
        var page = new DownloadsViewModel(manager);

        Assert.False(page.HasSelection);
        Assert.Equal(false, page.SelectAllState);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Highlighting_a_row_in_the_grid_counts_as_selecting_it()
    {
        var (manager, config) = Build();
        var a = Add(manager, "a.bin", DownloadStatus.Running);
        var page = new DownloadsViewModel(manager);

        // A plain click highlights without checking the box; the toolbar must still act on it.
        page.SetGridSelection(new[] { a });

        Assert.True(page.HasSelection);

        page.SetGridSelection(null);
        Assert.False(page.HasSelection);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Checking_rows_drives_the_tri_state_select_all()
    {
        var (manager, config) = Build();
        var a = Add(manager, "a.bin", DownloadStatus.Running);
        var b = Add(manager, "b.bin", DownloadStatus.Running);
        var page = new DownloadsViewModel(manager);

        a.IsChecked = true;
        Assert.Null(page.SelectAllState); // some, but not all

        b.IsChecked = true;
        Assert.True(page.SelectAllState);

        a.IsChecked = false;
        b.IsChecked = false;
        Assert.Equal(false, page.SelectAllState);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Bulk_stop_acts_on_the_selected_rows_only()
    {
        var (manager, config) = Build();
        var selected = Add(manager, "selected.bin", DownloadStatus.Running);
        var other = Add(manager, "other.bin", DownloadStatus.Running);
        var page = new DownloadsViewModel(manager);

        selected.IsChecked = true;
        page.StopSelectedCommand.Execute(null);

        Assert.Equal(DownloadStatus.Stopped, selected.Status);
        Assert.Equal(DownloadStatus.Running, other.Status);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Bulk_remove_takes_the_selected_rows_out_of_the_list()
    {
        var (manager, config) = Build();
        var doomed = Add(manager, "doomed.bin", DownloadStatus.Stopped);
        var kept = Add(manager, "kept.bin", DownloadStatus.Stopped);
        var page = new DownloadsViewModel(manager);

        doomed.IsChecked = true;
        page.RemoveSelectedCommand.Execute(null);

        Assert.DoesNotContain(manager.Items, i => i.FileName == "doomed.bin");
        Assert.Contains(manager.Items, i => i.FileName == "kept.bin");
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Bulk_pause_only_affects_what_is_running()
    {
        var (manager, config) = Build();
        var running = Add(manager, "running.bin", DownloadStatus.Running);
        var done = Add(manager, "done.bin", DownloadStatus.Completed);
        var page = new DownloadsViewModel(manager);

        running.IsChecked = true;
        done.IsChecked = true;
        page.PauseSelectedCommand.Execute(null);

        Assert.Equal(DownloadStatus.Paused, running.Status);
        // Bulk actions apply to every selected row regardless of state, so the guard has to be in the
        // manager — a completed download must not become "paused".
        Assert.Equal(DownloadStatus.Completed, done.Status);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_queue_menus_offer_every_queue_and_follow_additions()
    {
        var (manager, config) = Build();
        var page = new DownloadsViewModel(manager);
        var before = page.StartQueueTargets.Count;

        manager.AddQueue("videos");

        // These feed MenuFlyouts, which cache their ItemsSource — they must be mutated in place or a
        // new queue only appears after an app restart.
        Assert.Equal(before + 1, page.StartQueueTargets.Count);
        Assert.Equal(before + 1, page.StopQueueTargets.Count);
        Assert.Contains(page.StartQueueTargets, t => t.Name == "videos");
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_queue_column_appears_only_once_a_second_queue_exists()
    {
        var (manager, config) = Build();
        var page = new DownloadsViewModel(manager);

        Assert.False(page.ShowQueue);

        manager.AddQueue("videos");
        page.Refresh();

        Assert.True(page.ShowQueue);
    }
}
