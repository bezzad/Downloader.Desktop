using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// The Scheduler management page: rules that start/stop a queue within a daily time window.
/// </summary>
public class SchedulerViewModel : ViewModelBase
{
    private readonly Config _config;
    private readonly IDownloadManager _manager;

    public ObservableCollection<ScheduleRowViewModel> Schedules { get; } = new();
    public ICommand NewScheduleCommand { get; }

    public SchedulerViewModel()
    {
    }

    public SchedulerViewModel(Config config, IDownloadManager manager)
    {
        _config = config;
        _manager = manager;
        NewScheduleCommand = ReactiveCommand.Create(AddSchedule);
        Reload();
    }

    private void Reload()
    {
        Schedules.Clear();
        if (_config?.Schedules == null)
            return;
        foreach (var s in _config.Schedules)
            Schedules.Add(new ScheduleRowViewModel(s, _config, this));
    }

    private void AddSchedule()
    {
        var schedule = new DownloadSchedule
        {
            // Numbered ("Schedule 1", "Schedule 2", …), NOT the "New schedule" button label — a new
            // item named like the button confused users about which one is the action (#14).
            Name = NextScheduleName(),
            StartTime = DateTime.Now.TimeOfDay,
            TargetQueueId = _config.Queues.FirstOrDefault()?.Id,
            Enabled = true
        };
        _config.Schedules.Add(schedule);
        Schedules.Add(new ScheduleRowViewModel(schedule, _config, this));
    }

    /// <summary>Smallest "Schedule {n}" not already taken by an existing schedule name.</summary>
    private string NextScheduleName()
    {
        var format = Services.Localizer.Instance["Sched_DefaultName"]; // "Schedule {0}"
        var existing = new HashSet<string>(
            _config.Schedules.Select(s => s.Name).Where(n => !string.IsNullOrEmpty(n)));
        for (var n = 1; ; n++)
        {
            var name = string.Format(format, n);
            if (!existing.Contains(name))
                return name;
        }
    }

    public void Remove(ScheduleRowViewModel row)
    {
        _config.Schedules.Remove(row.Schedule);
        Schedules.Remove(row);
    }
}

/// <summary>A single schedule card.</summary>
public class ScheduleRowViewModel : ViewModelBase
{
    private readonly Config _config;
    private readonly SchedulerViewModel _parent;

    public DownloadSchedule Schedule { get; }
    public ICommand RemoveCommand { get; }

    public ScheduleRowViewModel(DownloadSchedule schedule, Config config, SchedulerViewModel parent)
    {
        Schedule = schedule;
        _config = config;
        _parent = parent;
        RemoveCommand = ReactiveCommand.Create(() => _parent.Remove(this));
    }

    public List<DownloadQueue> QueueOptions => _config.Queues;

    public string Name
    {
        get => Schedule.Name;
        set { Schedule.Name = value; this.RaisePropertyChanged(); }
    }

    public TimeSpan? StartTime
    {
        get => Schedule.StartTime;
        set { Schedule.StartTime = value ?? TimeSpan.Zero; this.RaisePropertyChanged(); }
    }

    public TimeSpan? StopTime
    {
        get => Schedule.StopTime;
        set { Schedule.StopTime = value; this.RaisePropertyChanged(); }
    }

    public DownloadQueue SelectedQueue
    {
        get => _config.Queues.FirstOrDefault(q => q.Id == Schedule.TargetQueueId);
        set { Schedule.TargetQueueId = value?.Id; this.RaisePropertyChanged(); }
    }

    public bool Enabled
    {
        get => Schedule.Enabled;
        set { Schedule.Enabled = value; this.RaisePropertyChanged(); }
    }

    public bool Once
    {
        get => Schedule.Once;
        set { Schedule.Once = value; this.RaisePropertyChanged(); }
    }
}
