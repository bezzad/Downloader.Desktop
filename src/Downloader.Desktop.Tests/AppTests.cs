using System.Globalization;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Downloader;
using Downloader.Desktop.Converters;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests;

/// <summary>Tests that need the (headless) Avalonia runtime: geometry parsing, dispatcher, view models.</summary>
public class AppTests
{
    [AvaloniaTheory]
    [InlineData("video")]
    [InlineData("audio")]
    [InlineData("image")]
    [InlineData("archive")]
    [InlineData("document")]
    [InlineData("app")]
    [InlineData("disc")]
    [InlineData("file")]
    [InlineData("unknown-kind")]
    public void FileKind_icons_parse(string kind)
    {
        var geometry = FileKindToIconConverter.Instance.Convert(kind, typeof(Geometry), null, CultureInfo.InvariantCulture);
        Assert.IsAssignableFrom<Geometry>(geometry);
    }

    [AvaloniaFact]
    public void Manager_add_and_remove_updates_stats()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());

        var vm = manager.Add(new DownloadItem { Url = "https://host/a.zip", SaveFolder = "/tmp" }, autoStart: false);
        Assert.Single(manager.Items);
        Assert.Equal(1, manager.QueuedCount);

        vm.Status = DownloadStatus.Completed;
        Assert.Equal(1, manager.CompletedCount);
        Assert.Equal(0, manager.QueuedCount);

        manager.Remove(vm);
        Assert.Empty(manager.Items);
    }

    [AvaloniaFact]
    public void DownloadsViewModel_filters_by_status_and_search()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());

        var a = manager.Add(new DownloadItem { Url = "https://host/movie.mp4", FileName = "movie.mp4" }, autoStart: false);
        manager.Add(new DownloadItem { Url = "https://host/song.mp3", FileName = "song.mp3" }, autoStart: false);
        a.Status = DownloadStatus.Completed;

        var view = new DownloadsViewModel(manager) { Filter = StatusFilter.Completed };
        Assert.Single(view.ItemsView);

        view.Filter = StatusFilter.All;
        view.Search = "song";
        Assert.Single(view.ItemsView);
    }

    [AvaloniaFact]
    public void Stopped_items_show_under_All_but_not_under_Failed()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());

        var stopped = manager.Add(new DownloadItem { Url = "https://host/a.zip", FileName = "a.zip" }, autoStart: false);
        var failed = manager.Add(new DownloadItem { Url = "https://host/b.zip", FileName = "b.zip" }, autoStart: false);
        stopped.Status = DownloadStatus.Stopped;
        failed.Status = DownloadStatus.Failed;

        var view = new DownloadsViewModel(manager) { Filter = StatusFilter.Failed };
        Assert.Single(view.ItemsView);                       // only the real failure
        Assert.DoesNotContain(view.ItemsView.Cast<DownloadItemViewModel>(), i => i.Status == DownloadStatus.Stopped);

        view.Filter = StatusFilter.All;
        Assert.Equal(2, view.ItemsView.Count);               // stopped is visible under All
    }

    [AvaloniaFact]
    public void Removing_a_queue_reassigns_its_items()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);

        var queue = manager.AddQueue("Second");
        var vm = manager.Add(new DownloadItem { Url = "https://host/c.zip", QueueId = queue.Id }, autoStart: false);
        Assert.Equal(queue.Id, vm.GetItem().QueueId);

        manager.RemoveQueue(queue);
        Assert.Equal(config.DefaultQueue.Id, vm.GetItem().QueueId);
    }

    [AvaloniaFact]
    public void QueuesChanged_fires_on_add_and_remove()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());

        int fired = 0;
        manager.QueuesChanged += () => fired++;

        var queue = manager.AddQueue("Second");
        Assert.Equal(1, fired); // adding a queue notifies so the start/stop menus refresh

        manager.RemoveQueue(queue);
        Assert.Equal(2, fired); // removing a queue notifies too
    }

    [AvaloniaFact]
    public void Start_stop_queue_menus_update_live_on_add_and_remove()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        var view = new DownloadsViewModel(manager);

        Assert.Single(view.StartQueueTargets); // default queue only
        Assert.Single(view.StopQueueTargets);

        var queue = manager.AddQueue("Second"); // no view.Refresh() — must update live (the reported bug)
        Assert.Equal(2, view.StartQueueTargets.Count);
        Assert.Contains(view.StartQueueTargets, t => t.Name == "Second");
        Assert.Contains(view.StopQueueTargets, t => t.Name == "Second");

        manager.RemoveQueue(queue);
        Assert.Single(view.StartQueueTargets);
        Assert.DoesNotContain(view.StartQueueTargets, t => t.Name == "Second");
    }

    [AvaloniaFact]
    public void Adding_a_queue_shows_the_queue_column()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false;
        manager.Initialize(config);
        manager.Add(new DownloadItem { Url = "https://host/a.zip" }, autoStart: false);

        var view = new DownloadsViewModel(manager);
        Assert.False(view.ShowQueue); // only the default queue

        manager.AddQueue("Second");
        view.Refresh();
        Assert.True(view.ShowQueue); // a 2nd queue → Queue column shown
    }

    [AvaloniaFact]
    public void Details_exposes_the_queue_name()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false;
        manager.Initialize(config);
        var vm = manager.Add(new DownloadItem { Url = "https://host/a.zip" }, autoStart: false);

        var details = new DownloadDetailsViewModel(vm);
        Assert.True(details.HasQueue);
        Assert.Equal(config.DefaultQueue.Name, vm.QueueName);
    }

    [AvaloniaFact]
    public void ReorderTo_moves_item_in_master_list()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false; // don't auto-pump network starts
        manager.Initialize(config);

        var a = manager.Add(new DownloadItem { Url = "https://host/a.zip" }, autoStart: false);
        var b = manager.Add(new DownloadItem { Url = "https://host/b.zip" }, autoStart: false);
        var c = manager.Add(new DownloadItem { Url = "https://host/c.zip" }, autoStart: false);
        Assert.Equal(new[] { a, b, c }, manager.Items);

        // Drag a below c → a moves to the end.
        manager.ReorderTo(a, c, placeAfter: true);
        Assert.Equal(new[] { b, c, a }, manager.Items);
    }

    [AvaloniaFact]
    public void Dragging_into_another_queue_changes_its_queue()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false;
        manager.Initialize(config);

        var second = manager.AddQueue("Second");
        var a = manager.Add(new DownloadItem { Url = "https://host/a.zip" }, autoStart: false);
        var b = manager.Add(new DownloadItem { Url = "https://host/b.zip", QueueId = second.Id }, autoStart: false);
        Assert.NotEqual(second.Id, a.GetItem().QueueId);

        // Drop a onto b (which lives in the second queue) → a adopts the second queue.
        manager.ReorderTo(a, b, placeAfter: false);
        Assert.Equal(second.Id, a.GetItem().QueueId);
        Assert.Equal("Second", a.QueueName);
    }

    [AvaloniaFact]
    public void Add_dialog_parses_multiple_urls()
    {
        var config = Config.New();
        var vm = new AddDownloadItemViewModel(config, "https://host/a.zip\nhttps://host/b.zip");
        Assert.True(vm.CanDownload);
        Assert.True(vm.IsMultiple);
    }

    [AvaloniaFact]
    public void Pending_name_shows_placeholder()
    {
        var item = new DownloadItem { Url = "https://host/x", Status = DownloadStatus.Running };
        var vm = new DownloadItemViewModel(item, null);
        Assert.True(vm.IsNamePending);
        Assert.Equal("Fetching name…", vm.DisplayName);
    }

    [AvaloniaTheory]
    [InlineData(DownloadStatus.Running)]
    [InlineData(DownloadStatus.Completed)]
    [InlineData(DownloadStatus.Failed)]
    [InlineData(DownloadStatus.Paused)]
    [InlineData(DownloadStatus.Stopped)]
    [InlineData(DownloadStatus.Created)]
    public void Status_brush_is_provided(DownloadStatus status)
    {
        var brush = Converters.StatusToBrushConverter.Instance
            .Convert(status, typeof(Avalonia.Media.IBrush), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.IsAssignableFrom<Avalonia.Media.IBrush>(brush);
    }

    [AvaloniaFact]
    public void StartAll_respects_queue_concurrency_cap()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.Settings.MaxConcurrentDownloads = 2; // authoritative limit (drives the primary queue)
        manager.Initialize(config);
        Assert.Equal(2, config.DefaultQueue.MaxConcurrent);

        // Non-routable host so the background download attempts never actually connect.
        for (int i = 0; i < 6; i++)
            manager.Add(new DownloadItem { Url = $"https://10.255.255.1/file{i}.zip", SaveFolder = "/tmp" }, autoStart: false);

        manager.StartAll();

        // Only the cap may run at once; the rest stay queued (this is the queue-limit fix).
        Assert.Equal(2, manager.ActiveCount);
        Assert.Equal(4, manager.QueuedCount);
    }

    [AvaloniaFact]
    public void Settings_max_concurrent_caps_running_downloads()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.Settings.MaxConcurrentDownloads = 5;
        manager.Initialize(config);

        // The user lowers the limit on the Settings page — it must drive the real queue cap.
        _ = new SettingViewModel(config, manager) { MaxConcurrentDownloads = 2 };
        Assert.Equal(2, config.DefaultQueue.MaxConcurrent);

        for (int i = 0; i < 8; i++)
            manager.Add(new DownloadItem { Url = $"https://10.255.255.1/file{i}.zip", SaveFolder = "/tmp" }, autoStart: false);

        manager.StartAll();

        Assert.Equal(2, manager.ActiveCount);
        Assert.Equal(6, manager.QueuedCount);
    }

    [AvaloniaFact]
    public void Staged_progress_flushes_only_while_running()
    {
        var item = new DownloadItem { Status = DownloadStatus.Running };
        var vm = new DownloadItemViewModel(item, null) { Status = DownloadStatus.Running };

        vm.StageProgress(42, 1000, 500, 2000);
        Assert.True(vm.FlushProgress());
        Assert.Equal(42, vm.Progress);

        // No new data → nothing to flush.
        Assert.False(vm.FlushProgress());

        // Values staged after a pause are dropped so the row keeps its last fill.
        vm.Status = DownloadStatus.Paused;
        vm.StageProgress(99, 0, 1900, 2000);
        Assert.False(vm.FlushProgress());
        Assert.Equal(42, vm.Progress);
    }

    [AvaloniaFact]
    public void Completed_item_ignores_stop_resume_and_retry()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);

        var vm = manager.Add(new DownloadItem { Url = "https://host/done.zip", SaveFolder = "/tmp" }, autoStart: false);
        vm.Status = DownloadStatus.Completed;
        vm.Progress = 100;

        // Bulk "Stop" hits a completed row — it must stay Completed (not flip to Stopped).
        manager.Cancel(vm);
        Assert.Equal(DownloadStatus.Completed, vm.Status);

        // Bulk "Start"/retry must never re-run a finished download from 0%.
        manager.Resume(vm);
        Assert.Equal(DownloadStatus.Completed, vm.Status);
        manager.Retry(vm);
        Assert.Equal(DownloadStatus.Completed, vm.Status);
        Assert.Equal(100, vm.Progress);
    }

    [AvaloniaFact]
    public void Stopping_all_stops_running_and_queued_items()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.Settings.MaxConcurrentDownloads = 3;
        manager.Initialize(config);

        for (int i = 0; i < 10; i++)
            manager.Add(new DownloadItem { Url = $"https://10.255.255.1/file{i}.zip", SaveFolder = "/tmp" }, autoStart: false);

        manager.StartAll();
        Assert.Equal(3, manager.ActiveCount);
        Assert.Equal(7, manager.QueuedCount);

        // "Select all + Stop": Cancel every row (what StopSelectedCommand does). Running rows stop and,
        // crucially, queued rows stop too — so nothing is left for the pump to auto-start.
        foreach (var vm in manager.Items.ToList())
            manager.Cancel(vm);

        Assert.Equal(0, manager.ActiveCount);
        Assert.Equal(0, manager.QueuedCount);
        Assert.All(manager.Items, vm => Assert.Equal(DownloadStatus.Stopped, vm.Status));
    }

    [AvaloniaFact]
    public void StopAll_stops_running_and_queued_items()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.Settings.MaxConcurrentDownloads = 3;
        manager.Initialize(config);

        for (int i = 0; i < 8; i++)
            manager.Add(new DownloadItem { Url = $"https://10.255.255.1/file{i}.zip", SaveFolder = "/tmp" }, autoStart: false);

        manager.StartAll();
        Assert.True(manager.ActiveCount > 0);

        manager.StopAll();

        Assert.Equal(0, manager.ActiveCount);
        Assert.Equal(0, manager.QueuedCount);
        Assert.All(manager.Items, vm => Assert.Equal(DownloadStatus.Stopped, vm.Status));
    }

    [AvaloniaFact]
    public void AllDownloadsCompleted_fires_once_when_list_drains()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);
        config.DefaultQueue.IsRunning = false; // don't let the pump kick off background (network) starts

        var fired = 0;
        manager.AllDownloadsCompleted += () => fired++;

        var a = manager.Add(new DownloadItem { Url = "https://host/a.zip", SaveFolder = "/tmp" }, autoStart: false);
        var b = manager.Add(new DownloadItem { Url = "https://host/b.zip", SaveFolder = "/tmp" }, autoStart: false);

        // Drive them through the manager's post-completion bookkeeping (test seam).
        manager.RaiseCompletedForTest(a);
        Assert.Equal(0, fired); // b still queued → not "all complete" yet

        manager.RaiseCompletedForTest(b);
        Assert.Equal(1, fired); // everything done → fired exactly once
    }

    [AvaloniaFact]
    public void Stopping_items_does_not_arm_all_completed_even_with_a_finished_item()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);
        config.DefaultQueue.IsRunning = false;

        var fired = 0;
        manager.AllDownloadsCompleted += () => fired++;

        var done = manager.Add(new DownloadItem { Url = "https://host/done.zip", SaveFolder = "/tmp" }, autoStart: false);
        var other = manager.Add(new DownloadItem { Url = "https://host/other.zip", SaveFolder = "/tmp" }, autoStart: false);

        manager.RaiseCompletedForTest(done);     // one finished, 'other' still queued → no fire
        Assert.Equal(0, fired);

        // User clicks Stop All → 'other' is cancelled (Stopped). Nothing is active/queued and a
        // completed item remains, but a *stop* must NOT trigger the shutdown/all-complete flow.
        manager.RaiseStoppedForTest(other);
        Assert.Equal(0, fired);
    }

    [AvaloniaFact]
    public void SelectAll_header_is_tri_state_and_drives_selection()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        var view = new DownloadsViewModel(manager);

        var a = manager.Add(new DownloadItem { Url = "https://host/a.zip" }, autoStart: false);
        var b = manager.Add(new DownloadItem { Url = "https://host/b.zip" }, autoStart: false);

        Assert.False(view.SelectAllState);   // none checked → false
        Assert.False(view.HasSelection);

        a.IsChecked = true;
        Assert.Null(view.SelectAllState);     // some checked → indeterminate
        Assert.True(view.HasSelection);

        b.IsChecked = true;
        Assert.True(view.SelectAllState);     // all checked → true

        view.SelectAllState = false;          // setting clears every row
        Assert.False(a.IsChecked);
        Assert.False(b.IsChecked);

        view.SelectAllState = true;           // setting selects every row
        Assert.True(a.IsChecked);
        Assert.True(b.IsChecked);
    }

    [AvaloniaFact]
    public void Selected_bulk_commands_disable_without_selection()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        var view = new DownloadsViewModel(manager);
        var a = manager.Add(new DownloadItem { Url = "https://host/a.zip" }, autoStart: false);

        Assert.False(((System.Windows.Input.ICommand)view.StartSelectedCommand).CanExecute(null));
        Assert.False(((System.Windows.Input.ICommand)view.RemoveSelectedCommand).CanExecute(null));
        // Stop-all is selection-independent — always available.
        Assert.True(((System.Windows.Input.ICommand)view.StopAllCommand).CanExecute(null));

        a.IsChecked = true;
        Assert.True(((System.Windows.Input.ICommand)view.StartSelectedCommand).CanExecute(null));
        Assert.True(((System.Windows.Input.ICommand)view.StopSelectedCommand).CanExecute(null));
    }

    [AvaloniaFact]
    public void Grid_row_selection_enables_bulk_commands_without_checkboxes()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        var view = new DownloadsViewModel(manager);
        var a = manager.Add(new DownloadItem { Url = "https://host/a.zip" }, autoStart: false);

        Assert.False(view.HasSelection);

        // Selecting (highlighting) a row in the DataGrid counts as selected — no checkbox needed (#3).
        view.SetGridSelection(new System.Collections.Generic.List<object> { a });
        Assert.True(view.HasSelection);
        Assert.True(((System.Windows.Input.ICommand)view.StartSelectedCommand).CanExecute(null));
        Assert.False(a.IsChecked); // the checkbox stays unchecked

        view.SetGridSelection(null);
        Assert.False(view.HasSelection);
    }

    [AvaloniaFact]
    public void StartQueue_runs_remaining_stopped_and_failed_items()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.Settings.MaxConcurrentDownloads = 5;
        manager.Initialize(config);

        // Non-routable host so nothing actually connects; we only assert state transitions.
        var stopped = manager.Add(new DownloadItem { Url = "https://10.255.255.1/a.zip", SaveFolder = "/tmp" }, autoStart: false);
        var failed = manager.Add(new DownloadItem { Url = "https://10.255.255.1/b.zip", SaveFolder = "/tmp" }, autoStart: false);
        var done = manager.Add(new DownloadItem { Url = "https://10.255.255.1/c.zip", SaveFolder = "/tmp" }, autoStart: false);
        stopped.Status = DownloadStatus.Stopped;
        failed.Status = DownloadStatus.Failed;
        done.Status = DownloadStatus.Completed;

        manager.StartQueue(config.DefaultQueue);

        // The stopped and failed rows must be (re)started; the completed one is left alone.
        Assert.NotEqual(DownloadStatus.Stopped, stopped.Status);
        Assert.NotEqual(DownloadStatus.Failed, failed.Status);
        Assert.Equal(DownloadStatus.Completed, done.Status);
    }

    [AvaloniaFact]
    public void Completed_item_loads_at_full_progress_even_without_downloaded_bytes()
    {
        // A file that already existed on disk is Completed but may have Downloaded=0 persisted.
        var item = new DownloadItem { FileName = "a.zip", Size = 1000, Downloaded = 0, Status = DownloadStatus.Completed };
        var vm = new DownloadItemViewModel(item, null);
        Assert.Equal(100, vm.Progress);
    }

    [AvaloniaFact]
    public void Setting_status_completed_forces_full_progress()
    {
        var vm = new DownloadItemViewModel(new DownloadItem { Size = 1000 }, null) { Progress = 0 };
        vm.Status = DownloadStatus.Completed;
        Assert.Equal(100, vm.Progress);
    }

    [AvaloniaFact]
    public void Chunk_status_text_tracks_progress()
    {
        // Reads localized strings, so it must run under the headless runtime with en loaded
        // (as a plain Fact it flaked on CI, returning the raw "State_Pending" key).
        Localizer.Instance.Load("en");
        var chunk = new ChunkProgressViewModel(1);
        chunk.Update(0, 0, 0, 100);
        Assert.Equal("Pending", chunk.StatusText);
        chunk.Update(50, 0, 50, 100);
        Assert.Equal("Downloading", chunk.StatusText);
        chunk.Update(100, 0, 100, 100);
        Assert.Equal("Completed", chunk.StatusText);
    }

    [AvaloniaFact]
    public void AlreadyExisted_completed_row_shows_exists_text()
    {
        Localizer.Instance.Load("en");
        var vm = new DownloadItemViewModel(new DownloadItem(), null)
        {
            Status = DownloadStatus.Completed
        };
        Assert.Equal("Completed", vm.StatusText);

        vm.AlreadyExisted = true;
        Assert.Equal("Already downloaded", vm.StatusText);
        Assert.True(vm.IsCompleted); // still counts as done (Open file works, no retry)
    }

    [AvaloniaFact]
    public void TimeLeft_reflects_remaining_over_speed()
    {
        var item = new DownloadItem { Status = DownloadStatus.Running };
        var vm = new DownloadItemViewModel(item, null) { Status = DownloadStatus.Running };
        vm.Size = 1000;
        vm.Downloaded = 200;
        vm.Speed = 100;                 // 800 bytes / 100 B/s = 8s
        Assert.Equal("8s", vm.TimeLeftText);

        vm.Status = DownloadStatus.Paused;
        Assert.Equal("—", vm.TimeLeftText);
    }

    [AvaloniaFact]
    public void Queue_summary_reflects_items()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);
        manager.Add(new DownloadItem { Url = "https://host/a.zip" }, autoStart: false);

        var queues = new QueuesViewModel(config, manager);
        var row = queues.Queues[0];

        Assert.Equal(1, row.TotalCount);
        Assert.Contains("waiting", row.SummaryText);
        Assert.Single(row.Items);                       // the item shows up as a wrapped row
        Assert.Equal(1, row.WaitingCount);
    }

    [AvaloniaFact]
    public void Move_to_queue_reassigns_item()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);
        var vm = manager.Add(new DownloadItem { Url = "https://host/a.zip" }, autoStart: false);
        var other = manager.AddQueue("Second");

        manager.MoveToQueue(vm, other.Id);

        Assert.Equal(other.Id, vm.GetItem().QueueId);
    }

    [AvaloniaFact]
    public void Move_priority_reorders_within_queue()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);
        var a = manager.Add(new DownloadItem { Url = "https://host/a.zip" }, autoStart: false);
        var b = manager.Add(new DownloadItem { Url = "https://host/b.zip" }, autoStart: false);

        Assert.True(manager.Items.IndexOf(a) < manager.Items.IndexOf(b));
        manager.MovePriority(b, -1); // move b up past a
        Assert.True(manager.Items.IndexOf(b) < manager.Items.IndexOf(a));
    }

    [AvaloniaTheory]
    [InlineData(DownloadStatus.Running)]
    [InlineData(DownloadStatus.Paused)]
    public void Interrupted_downloads_load_as_stopped(DownloadStatus saved)
    {
        // A Running/Paused state can't survive a restart (no live connection), so both must come back
        // as Stopped — never a misleading "Paused" row.
        var manager = new DownloadManager();
        var config = Config.New();
        config.Downloads.Add(new DownloadItem { Url = "https://host/x.zip", Status = saved });
        manager.Initialize(config);
        Assert.Equal(DownloadStatus.Stopped, manager.Items[0].Status);
    }

    [AvaloniaTheory]
    [InlineData(DownloadStatus.Completed)]
    [InlineData(DownloadStatus.Stopped)]
    [InlineData(DownloadStatus.Failed)]
    public void Terminal_states_survive_load(DownloadStatus saved)
    {
        // Only Running/Paused are normalized; finished/stopped/failed rows keep their saved state.
        var manager = new DownloadManager();
        var config = Config.New();
        config.Downloads.Add(new DownloadItem { Url = "https://host/x.zip", Status = saved });
        manager.Initialize(config);
        Assert.Equal(saved, manager.Items[0].Status);
    }

    [AvaloniaFact]
    public void Accent_applies_color_and_selection_resources()
    {
        ThemeService.ApplyAccent("Blue");
        var res = Avalonia.Application.Current!.Resources;
        Assert.Equal(ThemeService.Find("Blue").Color, res["SystemAccentColor"]);
        Assert.True(res.ContainsKey("RowSelectionBrush"));
        Assert.IsAssignableFrom<IBrush>(res["RowSelectionBrush"]);
        // Unknown key falls back to the first accent (Teal) rather than throwing.
        Assert.Equal(ThemeService.Accents[0], ThemeService.Find("does-not-exist"));
        ThemeService.ApplyAccent("Teal"); // restore default for other tests
    }

    [AvaloniaFact]
    public void Every_language_has_a_loadable_flag()
    {
        foreach (var lang in Localizer.Languages)
            Assert.NotNull(lang.Flag); // Assets/flags/{code}.png must be embedded for each language
    }

    [AvaloniaFact]
    public void Start_runs_item_even_when_its_queue_was_paused()
    {
        // Regression (1.3.2/1.3.3): a persisted queue.IsRunning=false made every per-item Start silently
        // no-op — the item stuck as "Queued", only rescued later by the scheduler/StartQueue. An explicit
        // Start (row button / bulk) must un-pause the queue and actually run the item.
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);
        var vm = manager.Add(new DownloadItem { Url = "https://10.255.255.1/f.zip", SaveFolder = "/tmp" }, autoStart: false);
        vm.Status = DownloadStatus.Stopped;
        config.DefaultQueue.IsRunning = false; // the stuck precondition (a saved "stopped" queue)

        manager.Resume(vm); // user clicks Start on the row

        Assert.True(config.DefaultQueue.IsRunning);      // explicit Start un-pauses the queue
        Assert.Equal(DownloadStatus.Running, vm.Status); // item actually started (not stuck as Queued)
    }

    [AvaloniaFact]
    public void Removing_a_queue_deactivates_its_schedules()
    {
        // Deleting a queue must disable + unbind any schedules pointing at it, so the scheduler can't
        // act on a now-deleted target (orphaned-schedule bug).
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);
        var q2 = manager.AddQueue("Nightly");
        var sch = new DownloadSchedule { Name = "nightly", TargetQueueId = q2.Id, Enabled = true };
        config.Schedules.Add(sch);

        manager.RemoveQueue(q2);

        Assert.DoesNotContain(q2, config.Queues);
        Assert.False(sch.Enabled);
        Assert.Null(sch.TargetQueueId);
    }
}
