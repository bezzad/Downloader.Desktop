using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Services;

/// <summary>
/// Default <see cref="IDownloadManager"/>. Builds <see cref="IDownload"/> instances via
/// <see cref="DownloadBuilder"/>, marshals engine events onto the UI thread, and updates the
/// matching <see cref="DownloadItemViewModel"/>. Queue concurrency / scheduling are layered on later.
/// </summary>
public class DownloadManager : IDownloadManager
{
    private Config _config;

    public ObservableCollection<DownloadItemViewModel> Items { get; } = new();

    public event Action StatsChanged;
    public event Action ListChanged;
    private DateTime _lastStatsUtc;

    public double TotalSpeed
    {
        get
        {
            double sum = 0;
            foreach (var i in Items)
                if (i.Status == DownloadStatus.Running)
                    sum += i.Speed;
            return sum;
        }
    }

    public int ActiveCount => Items.Count(i => i.Status == DownloadStatus.Running);
    public int QueuedCount => Items.Count(i => i.Status is DownloadStatus.Created or DownloadStatus.None);
    public int CompletedCount => Items.Count(i => i.Status == DownloadStatus.Completed);

    /// <summary>Throttled status-bar number updates for high-frequency progress events.</summary>
    private void NotifyStats()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastStatsUtc).TotalMilliseconds < 400)
            return;
        _lastStatsUtc = now;
        StatsChanged?.Invoke();
    }

    /// <summary>Items added/removed or changed status — refresh the filtered list and numbers.</summary>
    private void NotifyList()
    {
        StatsChanged?.Invoke();
        ListChanged?.Invoke();
    }

    public void Initialize(Config config)
    {
        _config = config ?? Config.New();
        Items.Clear();
        foreach (var item in _config.Downloads ?? new List<DownloadItem>())
        {
            // Nothing is actually running on a fresh launch — show in-progress items as resumable.
            if (item.Status == DownloadStatus.Running)
                item.Status = DownloadStatus.Paused;
            Items.Add(new DownloadItemViewModel(item, this));
        }

        StartScheduler();
    }

    // ---------------- Scheduler ----------------

    private DispatcherTimer _schedulerTimer;
    private readonly HashSet<string> _firedKeys = new();
    private DateTime _firedDay = DateTime.MinValue;

    private void StartScheduler()
    {
        _schedulerTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _schedulerTimer.Tick -= OnSchedulerTick;
        _schedulerTimer.Tick += OnSchedulerTick;
        _schedulerTimer.Start();
    }

    private void OnSchedulerTick(object sender, EventArgs e) => EvaluateSchedules();

    private void EvaluateSchedules()
    {
        if (_config?.Schedules == null)
            return;

        var now = DateTime.Now;
        if (now.Date != _firedDay)
        {
            _firedDay = now.Date;
            _firedKeys.Clear();
        }

        var tod = now.TimeOfDay;
        foreach (var sch in _config.Schedules.ToList())
        {
            if (!sch.Enabled)
                continue;
            if (sch.Days is { Length: > 0 } && !sch.Days.Contains(now.DayOfWeek))
                continue;

            var startKey = sch.Id + ":start";
            var stopKey = sch.Id + ":stop";

            var inWindow = tod >= sch.StartTime && (sch.StopTime == null || tod < sch.StopTime.Value);
            if (inWindow && _firedKeys.Add(startKey))
            {
                TriggerStart(sch);
                if (sch.Once)
                    sch.Enabled = false;
            }

            if (sch.StopTime is { } stop && tod >= stop && _firedKeys.Add(stopKey))
                TriggerStop(sch);
        }
    }

    private void TriggerStart(DownloadSchedule sch)
    {
        if (!string.IsNullOrEmpty(sch.TargetQueueId))
        {
            var queue = FindQueue(sch.TargetQueueId);
            if (queue != null)
                StartQueue(queue);
        }
        else if (sch.TargetItemId is { } id)
        {
            var vm = Items.FirstOrDefault(i => i.GetItem().Id == id);
            if (vm != null && vm.CanResume)
                Resume(vm);
        }
    }

    private void TriggerStop(DownloadSchedule sch)
    {
        if (!string.IsNullOrEmpty(sch.TargetQueueId))
        {
            var queue = FindQueue(sch.TargetQueueId);
            if (queue != null)
                PauseQueue(queue);
        }
        else if (sch.TargetItemId is { } id)
        {
            var vm = Items.FirstOrDefault(i => i.GetItem().Id == id);
            if (vm != null && vm.Status == DownloadStatus.Running)
                Pause(vm);
        }
    }

    public DownloadItemViewModel Add(DownloadItem item, bool autoStart)
    {
        if (string.IsNullOrWhiteSpace(item.QueueId) && _config != null)
            item.QueueId = _config.DefaultQueue.Id;

        var vm = new DownloadItemViewModel(item, this);
        Items.Add(vm);
        if (autoStart)
            PumpQueue(item.QueueId); // starts now if a slot is free, otherwise stays queued

        NotifyList();
        return vm;
    }

    public async void Start(DownloadItemViewModel vm)
    {
        var item = vm.GetItem();
        if (string.IsNullOrWhiteSpace(item.Url))
            return;

        try
        {
            var folder = string.IsNullOrWhiteSpace(item.SaveFolder)
                ? _config?.Settings?.DefaultSavePath
                : item.SaveFolder;
            item.SaveFolder = folder;

            var builder = DownloadBuilder.New()
                .WithUrl(item.Url)
                .WithDirectory(folder ?? string.Empty);

            // Only force a name when the user supplied one; otherwise let the engine
            // resolve the real file name from the URL / Content-Disposition headers.
            if (!string.IsNullOrWhiteSpace(item.FileName))
                builder = builder.WithFileName(item.FileName);

            var configuration = _config?.Settings?.ToConfiguration() ?? new DownloadConfiguration();
            vm.Configuration = configuration; // keep a handle so the details dialog can tweak it live
            var download = builder.WithConfiguration(configuration).Build();

            Attach(vm, download);
            item.LastTry = DateTime.Now;
            vm.ErrorMessage = null; // clear any previous failure on (re)start
            vm.Status = DownloadStatus.Running;
            // Run on a background thread: StartAsync does synchronous setup before its first await,
            // which would otherwise briefly block (and with many events, freeze) the UI thread.
            await Task.Run(() => download.StartAsync()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            OnUi(() =>
            {
                vm.ErrorMessage = Describe(ex);
                vm.Status = DownloadStatus.Failed;
                NotifyList();
            });
        }
    }

    /// <summary>Turns an exception into a short, user-friendly root cause.</summary>
    private static string Describe(Exception ex)
    {
        var e = ex;
        while (e.InnerException != null)
            e = e.InnerException;

        return e switch
        {
            System.Net.Http.HttpRequestException => $"Network error: {e.Message}",
            System.Net.WebException we => $"Network error: {we.Message}",
            UnauthorizedAccessException => "Permission denied writing the file. Try another folder.",
            IOException io => $"Disk error: {io.Message}",
            TaskCanceledException or OperationCanceledException => "The download timed out or was cancelled.",
            _ => e.Message
        };
    }

    public void Pause(DownloadItemViewModel vm)
    {
        vm.Download?.Pause();
        vm.Status = DownloadStatus.Paused;
        vm.Speed = 0;
        NotifyList();
    }

    public void Resume(DownloadItemViewModel vm)
    {
        if (vm.Download != null && vm.Status == DownloadStatus.Paused)
        {
            vm.Download.Resume();
            vm.Status = DownloadStatus.Running;
        }
        else
        {
            // No live handle (stopped or freshly loaded) — (re)build and start.
            Start(vm);
        }
        NotifyList();
    }

    public void Cancel(DownloadItemViewModel vm)
    {
        vm.Download?.Stop();
        vm.Status = DownloadStatus.Stopped;
        vm.Speed = 0;
        NotifyList();
    }

    public void Retry(DownloadItemViewModel vm) => Start(vm);

    public Task Remove(DownloadItemViewModel vm)
    {
        try
        {
            vm.Download?.Stop();
        }
        catch
        {
            // best-effort stop before removal
        }

        Items.Remove(vm);
        NotifyList();
        return Task.CompletedTask;
    }

    public void StartAll()
    {
        foreach (var vm in Items.Where(v => v.CanResume).ToList())
            Resume(vm);
        NotifyList();
    }

    public void StopAll()
    {
        foreach (var vm in Items.Where(v => v.Status == DownloadStatus.Running).ToList())
            Pause(vm);
        NotifyList();
    }

    public void ClearCompleted()
    {
        foreach (var vm in Items.Where(v => v.IsCompleted).ToList())
            Items.Remove(vm);
        NotifyList();
    }

    private void TryStartNextInQueue(string queueId) => PumpQueue(queueId);

    private DownloadQueue FindQueue(string id) =>
        _config?.Queues?.FirstOrDefault(q => q.Id == id);

    public void PumpQueue(string queueId)
    {
        var queue = FindQueue(queueId);
        if (queue == null || !queue.IsRunning)
            return;

        int running = Items.Count(i => i.GetItem().QueueId == queueId && i.Status == DownloadStatus.Running);
        var cap = Math.Max(1, queue.MaxConcurrent);

        foreach (var vm in Items.Where(i =>
                     i.GetItem().QueueId == queueId &&
                     i.Status is DownloadStatus.Created or DownloadStatus.None).ToList())
        {
            if (running >= cap)
                break;
            Start(vm);
            running++;
        }
    }

    public void StartQueue(DownloadQueue queue)
    {
        if (queue == null)
            return;
        queue.IsRunning = true;

        // Wake up paused items too, then fill remaining slots from the queued ones.
        foreach (var vm in Items.Where(i =>
                     i.GetItem().QueueId == queue.Id && i.Status == DownloadStatus.Paused).ToList())
        {
            if (Items.Count(i => i.GetItem().QueueId == queue.Id && i.Status == DownloadStatus.Running) >= Math.Max(1, queue.MaxConcurrent))
                break;
            Resume(vm);
        }

        PumpQueue(queue.Id);
        NotifyList();
    }

    public void PauseQueue(DownloadQueue queue)
    {
        if (queue == null)
            return;
        queue.IsRunning = false;
        foreach (var vm in Items.Where(i =>
                     i.GetItem().QueueId == queue.Id && i.Status == DownloadStatus.Running).ToList())
            Pause(vm);
        NotifyList();
    }

    public DownloadQueue AddQueue(string name)
    {
        var queue = new DownloadQueue
        {
            Name = string.IsNullOrWhiteSpace(name) ? "New queue" : name,
            MaxConcurrent = _config?.Settings?.MaxConcurrentDownloads ?? 3
        };
        _config?.Queues?.Add(queue);
        return queue;
    }

    public void RemoveQueue(DownloadQueue queue)
    {
        if (queue == null || _config?.Queues == null || _config.Queues.Count <= 1)
            return;

        var fallback = _config.Queues.FirstOrDefault(q => q.Id != queue.Id);
        foreach (var vm in Items.Where(i => i.GetItem().QueueId == queue.Id).ToList())
            vm.GetItem().QueueId = fallback?.Id;

        _config.Queues.Remove(queue);
        NotifyList();
    }

    private void Attach(DownloadItemViewModel vm, IDownload download)
    {
        vm.Download = download;

        download.DownloadStarted += (_, e) => OnUi(() =>
        {
            // The engine resolved the real file path (from URL / Content-Disposition) and reports
            // it as the full path in e.FileName. IDownload.Filename stays empty when no name was
            // supplied, so derive the name/folder from e.FileName instead.
            if (!string.IsNullOrWhiteSpace(e.FileName))
            {
                var name = Path.GetFileName(e.FileName);
                if (string.IsNullOrWhiteSpace(vm.FileName) && !string.IsNullOrWhiteSpace(name))
                    vm.FileName = name;

                var dir = Path.GetDirectoryName(e.FileName);
                if (!string.IsNullOrWhiteSpace(dir))
                    vm.GetItem().SaveFolder = dir;
            }

            if (e.TotalBytesToReceive > 0)
                vm.Size = e.TotalBytesToReceive;
            vm.Status = DownloadStatus.Running;
            NotifyList();
        });

        download.DownloadProgressChanged += (_, e) =>
        {
            // Throttle UI updates to ~5 fps per item; the engine raises this event very frequently
            // and posting every one to the UI thread is what froze the window.
            var now = DateTime.UtcNow;
            if ((now - vm.LastUiUpdateUtc).TotalMilliseconds < 200)
                return;
            vm.LastUiUpdateUtc = now;

            OnUi(() =>
            {
                vm.Progress = e.ProgressPercentage;
                vm.Speed = e.BytesPerSecondSpeed;
                vm.Downloaded = e.ReceivedBytesSize;
                if (vm.Size is null or 0 && e.TotalBytesToReceive > 0)
                    vm.Size = e.TotalBytesToReceive;
                NotifyStats();
            });
        };

        download.DownloadFileCompleted += (_, e) => OnUi(() =>
        {
            vm.Speed = 0;
            if (e.Cancelled)
            {
                // Distinguish a user pause (live handle kept) from a hard stop.
                if (vm.Status != DownloadStatus.Paused)
                    vm.Status = DownloadStatus.Stopped;
            }
            else if (e.Error != null)
            {
                vm.ErrorMessage = Describe(e.Error);
                vm.Status = DownloadStatus.Failed;
            }
            else
            {
                vm.Progress = 100;
                vm.Status = DownloadStatus.Completed;
            }

            // A finished/stopped item frees a slot — let the queue start the next one.
            TryStartNextInQueue(vm.GetItem().QueueId);
            NotifyList();
        });
    }

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
