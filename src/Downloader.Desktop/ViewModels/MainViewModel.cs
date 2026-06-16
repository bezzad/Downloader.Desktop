using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Threading;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly IFileService _fileService;
    private readonly IDownloadManager _downloadManager;
    private Config _config;

    private string _downloadUrl;
    private string _searchText;
    private object _currentPage;
    private NavSection _section = NavSection.Downloads;
    private StatusFilter _filter = StatusFilter.All;
    private DispatcherTimer _autoSaveTimer;
    private DateTime _lastSaveUtc;
    private bool _isSidebarExpanded = true;

    public MainViewModel(IFileService fileService, IDownloadManager downloadManager)
    {
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _downloadManager = downloadManager ?? throw new ArgumentNullException(nameof(downloadManager));

        AddDownloadItemCommand = ReactiveCommand.CreateFromTask(AddDownloadItem);
        StartAllCommand = ReactiveCommand.Create(() => _downloadManager.StartAll());
        StopAllCommand = ReactiveCommand.Create(() => _downloadManager.StopAll());
        ClearAllCommand = ReactiveCommand.Create(() => _downloadManager.ClearCompleted());

        ShowAllCommand = ReactiveCommand.Create(() => SelectFilter(StatusFilter.All));
        ShowActiveCommand = ReactiveCommand.Create(() => SelectFilter(StatusFilter.Active));
        ShowCompletedCommand = ReactiveCommand.Create(() => SelectFilter(StatusFilter.Completed));
        ShowFailedCommand = ReactiveCommand.Create(() => SelectFilter(StatusFilter.Failed));
        ShowQueuesCommand = ReactiveCommand.Create(() => Navigate(NavSection.Queues));
        ShowSchedulerCommand = ReactiveCommand.Create(() => Navigate(NavSection.Scheduler));
        ShowSettingViewCommand = ReactiveCommand.Create(() => Navigate(NavSection.Settings));
        ToggleSidebarCommand = ReactiveCommand.Create(() => IsSidebarExpanded = !IsSidebarExpanded);

        _downloadManager.StatsChanged += OnStatsChanged;
        _downloadManager.ListChanged += OnListChanged;
        RxApp.MainThreadScheduler.ScheduleAsync(InitMainViewModelAsync);
    }

    // ---- Pages ----
    public DownloadsViewModel Downloads { get; private set; }
    public QueuesViewModel Queues { get; private set; }
    public SchedulerViewModel Scheduler { get; private set; }
    public SettingViewModel Settings { get; private set; }

    public object CurrentPage
    {
        get => _currentPage;
        private set => this.RaiseAndSetIfChanged(ref _currentPage, value);
    }

    // ---- Commands ----
    public ICommand AddDownloadItemCommand { get; }
    public ICommand StartAllCommand { get; }
    public ICommand StopAllCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand ShowAllCommand { get; }
    public ICommand ShowActiveCommand { get; }
    public ICommand ShowCompletedCommand { get; }
    public ICommand ShowFailedCommand { get; }
    public ICommand ShowQueuesCommand { get; }
    public ICommand ShowSchedulerCommand { get; }
    public ICommand ShowSettingViewCommand { get; }
    public ICommand ToggleSidebarCommand { get; }

    /// <summary>When false the left rail collapses to an icons-only strip.</summary>
    public bool IsSidebarExpanded
    {
        get => _isSidebarExpanded;
        set
        {
            this.RaiseAndSetIfChanged(ref _isSidebarExpanded, value);
            this.RaisePropertyChanged(nameof(SidebarWidth));
        }
    }

    public double SidebarWidth => _isSidebarExpanded ? 208 : 56;

    public string DownloadUrl
    {
        get => _downloadUrl;
        set => this.RaiseAndSetIfChanged(ref _downloadUrl, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchText, value);
            if (Downloads != null)
                Downloads.Search = value;
        }
    }

    // ---- Nav selection flags (for highlighting) ----
    public bool IsAllSelected => _section == NavSection.Downloads && _filter == StatusFilter.All;
    public bool IsActiveSelected => _section == NavSection.Downloads && _filter == StatusFilter.Active;
    public bool IsCompletedSelected => _section == NavSection.Downloads && _filter == StatusFilter.Completed;
    public bool IsFailedSelected => _section == NavSection.Downloads && _filter == StatusFilter.Failed;
    public bool IsQueuesSelected => _section == NavSection.Queues;
    public bool IsSchedulerSelected => _section == NavSection.Scheduler;
    public bool IsSettingsSelected => _section == NavSection.Settings;

    // ---- Status bar ----
    public string TotalSpeedText => FormatSpeed(_downloadManager.TotalSpeed);
    public int ActiveCount => _downloadManager.ActiveCount;
    public int QueuedCount => _downloadManager.QueuedCount;
    public int CompletedCount => _downloadManager.CompletedCount;

    // ---- Nav count pills (match each filter) ----
    public int AllCount => _downloadManager.Items.Count;
    public int ActiveFilterCount => _downloadManager.Items.Count(i =>
        i.Status is DownloadStatus.Running or DownloadStatus.Paused or DownloadStatus.Created);
    public int CompletedFilterCount => _downloadManager.Items.Count(i => i.Status == DownloadStatus.Completed);
    public int FailedFilterCount => _downloadManager.Items.Count(i =>
        i.Status is DownloadStatus.Failed or DownloadStatus.Stopped);

    private async Task InitMainViewModelAsync(IScheduler scheduler, CancellationToken ct)
    {
        _config = (await _fileService.LoadFromFileAsync()).EnsureValid();
        AppLog.SetEnabled(_config.Settings.EnableLogging);
        NotificationService.Enabled = _config.Settings.EnableNotifications;
        Localizer.Instance.Load(_config.Settings.Language);
        Application.Current!.RequestedThemeVariant = _config.ThemeMode;

        _downloadManager.Initialize(_config);
        Downloads = new DownloadsViewModel(_downloadManager);
        Queues = new QueuesViewModel(_config, _downloadManager);
        Scheduler = new SchedulerViewModel(_config, _downloadManager);
        Settings = new SettingViewModel(_config, _downloadManager);

        // Persist settings to disk as soon as the user changes one (#24), debounced so spinning a
        // NumericUpDown doesn't hammer the file.
        ((System.ComponentModel.INotifyPropertyChanged)Settings).PropertyChanged += (_, _) => SaveSoon();

        this.RaisePropertyChanged(nameof(Downloads));
        Navigate(NavSection.Downloads);
        OnStatsChanged();

        // Periodic autosave so an unclean exit doesn't lose the list/settings.
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _autoSaveTimer.Tick += (_, _) => RequestSave();
        _autoSaveTimer.Start();
    }

    private void RequestSave()
    {
        if ((DateTime.UtcNow - _lastSaveUtc).TotalSeconds < 3)
            return;
        _lastSaveUtc = DateTime.UtcNow;
        _ = SaveConfigFile();
    }

    private DispatcherTimer _saveSoonTimer;

    /// <summary>Debounced near-immediate save, used when the user changes a setting (#24).</summary>
    private void SaveSoon()
    {
        if (_saveSoonTimer == null)
        {
            _saveSoonTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveSoonTimer.Tick += (_, _) =>
            {
                _saveSoonTimer.Stop();
                _lastSaveUtc = DateTime.UtcNow;
                _ = SaveConfigFile();
            };
        }

        // Pick up live setting changes that affect runtime services right away.
        if (_config?.Settings != null)
        {
            AppLog.SetEnabled(_config.Settings.EnableLogging);
            NotificationService.Enabled = _config.Settings.EnableNotifications;
        }

        _saveSoonTimer.Stop();
        _saveSoonTimer.Start(); // restart the debounce window
    }

    private void OnStatsChanged()
    {
        this.RaisePropertyChanged(nameof(TotalSpeedText));
        this.RaisePropertyChanged(nameof(ActiveCount));
        this.RaisePropertyChanged(nameof(QueuedCount));
        this.RaisePropertyChanged(nameof(CompletedCount));
        this.RaisePropertyChanged(nameof(AllCount));
        this.RaisePropertyChanged(nameof(ActiveFilterCount));
        this.RaisePropertyChanged(nameof(CompletedFilterCount));
        this.RaisePropertyChanged(nameof(FailedFilterCount));
    }

    // Only refresh the (expensive) filtered grid when items actually move buckets,
    // never on every progress tick.
    private void OnListChanged()
    {
        OnStatsChanged();
        Downloads?.Refresh();
        RequestSave();
    }

    private void Navigate(NavSection section)
    {
        _section = section;
        CurrentPage = section switch
        {
            NavSection.Queues => Queues,
            NavSection.Scheduler => Scheduler,
            NavSection.Settings => Settings,
            _ => (object)Downloads
        };
        RaiseNavFlags();
    }

    private void SelectFilter(StatusFilter filter)
    {
        _filter = filter;
        if (Downloads != null)
            Downloads.Filter = filter;
        Navigate(NavSection.Downloads);
    }

    private void RaiseNavFlags()
    {
        this.RaisePropertyChanged(nameof(IsAllSelected));
        this.RaisePropertyChanged(nameof(IsActiveSelected));
        this.RaisePropertyChanged(nameof(IsCompletedSelected));
        this.RaisePropertyChanged(nameof(IsFailedSelected));
        this.RaisePropertyChanged(nameof(IsQueuesSelected));
        this.RaisePropertyChanged(nameof(IsSchedulerSelected));
        this.RaisePropertyChanged(nameof(IsSettingsSelected));
    }

    private async Task AddDownloadItem()
    {
        // Always open the dialog; URLs can be typed there if the top box was empty.
        var result = await DialogHelper.ShowDialog<AddDownloadItemView, AddDownloadItemViewModel, List<DownloadItem>>(
            new AddDownloadItemView(), new AddDownloadItemViewModel(_config, _downloadUrl));

        if (result is { Count: > 0 })
        {
            foreach (var item in result)
                _downloadManager.Add(item, autoStart: true);

            DownloadUrl = string.Empty;
            SelectFilter(StatusFilter.All);
        }
    }

    public async Task SaveConfigFile()
    {
        if (_config == null)
            return;

        _config.Downloads = _downloadManager.Items.Select(i => i.GetItem()).ToList();
        await _fileService.SaveToFileAsync(_config).ConfigureAwait(false);
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        var mbps = bytesPerSecond / (1024.0 * 1024.0);
        return $"{mbps:0.00} MB/s";
    }
}
