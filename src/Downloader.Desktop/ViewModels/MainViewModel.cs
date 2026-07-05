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
using Avalonia.Controls;
using Avalonia.Threading;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly IFileService _fileService;
    private readonly IDownloadManager _downloadManager;
    private readonly PluginManager _pluginManager;
    private Config _config;

    private string _downloadUrl;
    private string _searchText;
    private object _currentPage;
    private NavSection _section = NavSection.Downloads;
    private StatusFilter _filter = StatusFilter.All;
    private DispatcherTimer _autoSaveTimer;
    private DateTime _lastSaveUtc;
    private bool _isSidebarExpanded = true;

    public MainViewModel(IFileService fileService, IDownloadManager downloadManager, PluginManager pluginManager = null)
    {
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _downloadManager = downloadManager ?? throw new ArgumentNullException(nameof(downloadManager));
        _pluginManager = pluginManager ?? new PluginManager();

        AddDownloadItemCommand = ReactiveCommand.CreateFromTask(AddDownloadItem);
        StartAllCommand = ReactiveCommand.Create(() => _downloadManager.StartAll());
        StopAllCommand = ReactiveCommand.Create(() => _downloadManager.StopAll());
        ClearAllCommand = ReactiveCommand.Create(() => _downloadManager.ClearCompleted());

        ShowAllCommand = ReactiveCommand.Create(() => SelectFilter(StatusFilter.All));
        ShowActiveCommand = ReactiveCommand.Create(() => SelectFilter(StatusFilter.Active));
        ShowQueuedCommand = ReactiveCommand.Create(() => SelectFilter(StatusFilter.Queued));
        ShowCompletedCommand = ReactiveCommand.Create(() => SelectFilter(StatusFilter.Completed));
        ShowFailedCommand = ReactiveCommand.Create(() => SelectFilter(StatusFilter.Failed));
        // Management pages open as dialogs over the always-downloads main view (the left rail was removed).
        ShowQueuesCommand = ReactiveCommand.CreateFromTask(() => DialogHelper.ShowPage(Queues, Localizer.Instance["Nav_Queues"], _config));
        ShowSchedulerCommand = ReactiveCommand.CreateFromTask(() => DialogHelper.ShowPage(Scheduler, Localizer.Instance["Nav_Scheduler"], _config));
        ShowSettingViewCommand = ReactiveCommand.CreateFromTask(() => DialogHelper.ShowPage(Settings, Localizer.Instance["Nav_Settings"], _config));
        ToggleSidebarCommand = ReactiveCommand.Create(() => IsSidebarExpanded = !IsSidebarExpanded);
        ShowAboutCommand = ReactiveCommand.CreateFromTask(DialogHelper.ShowAbout);
        DonateCommand = ReactiveCommand.Create(() =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = AboutViewModel.DonateUrl, UseShellExecute = true }); }
            catch { /* best-effort */ }
        });
        ApplyUpdateCommand = ReactiveCommand.Create(UpdateFlow.ApplyAndRestart);
        UpdateFlow.Changed += OnUpdateStateChanged;

        _downloadManager.StatsChanged += OnStatsChanged;
        _downloadManager.ListChanged += OnListChanged;
        _downloadManager.AllDownloadsCompleted += OnAllDownloadsCompleted;
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
    public ICommand ShowQueuedCommand { get; }
    public ICommand ShowCompletedCommand { get; }
    public ICommand ShowFailedCommand { get; }
    public ICommand ShowQueuesCommand { get; }
    public ICommand ShowSchedulerCommand { get; }
    public ICommand ShowSettingViewCommand { get; }
    public ICommand ToggleSidebarCommand { get; }
    public ICommand ShowAboutCommand { get; }
    public ICommand DonateCommand { get; }
    public ICommand ApplyUpdateCommand { get; }

    /// <summary>True once a new version is downloaded and ready — shows the nav "Update Downloader" button.</summary>
    public bool IsUpdateReady => UpdateFlow.IsReady;

    private void OnUpdateStateChanged() => this.RaisePropertyChanged(nameof(IsUpdateReady));

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
    public bool IsQueuedSelected => _section == NavSection.Downloads && _filter == StatusFilter.Queued;
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

    // ---- Footer filter counts (each matches its StatusFilter bucket exactly, so the buttons are disjoint) ----
    public int AllCount => _downloadManager.Items.Count;
    public int ActiveFilterCount => _downloadManager.Items.Count(i =>
        i.Status is DownloadStatus.Running or DownloadStatus.Paused);
    public int QueuedFilterCount => _downloadManager.Items.Count(i =>
        i.Status is DownloadStatus.Created or DownloadStatus.None);
    public int CompletedFilterCount => _downloadManager.Items.Count(i => i.Status == DownloadStatus.Completed);
    public int FailedFilterCount => _downloadManager.Items.Count(i =>
        i.Status is DownloadStatus.Failed);

    private async Task InitMainViewModelAsync(IScheduler scheduler, CancellationToken ct)
    {
        _config = (await _fileService.LoadFromFileAsync()).EnsureValid();
        AppLog.SetEnabled(_config.Settings.EnableLogging);
        NotificationService.Enabled = _config.Settings.EnableNotifications;
        Localizer.Instance.Load(_config.Settings.Language);
        ThemeService.Apply(_config); // theme variant + chosen accent

        _downloadManager.Initialize(_config);

        // Load the bundled built-in plugins (app dir /plugins — disable-only) plus the user's external
        // plugins (~/.config/Downloader/plugins), then apply the persisted disabled list to both.
        _pluginManager.LoadBuiltIns();
        _pluginManager.LoadFromDirectory(Services.PluginManager.PluginsRoot);
        foreach (var id in _config.DisabledPlugins ?? new System.Collections.Generic.List<string>())
            _pluginManager.SetEnabled(id, false);

        // Plugins loaded AFTER the download list: re-raise post-download-action offers on completed
        // items so their row buttons (e.g. "Add to Ollama") appear without needing a status change.
        foreach (var vm in _downloadManager.Items)
            vm.RaisePostActionChanged();

        Downloads = new DownloadsViewModel(_downloadManager);
        Queues = new QueuesViewModel(_config, _downloadManager);
        Scheduler = new SchedulerViewModel(_config, _downloadManager);
        Settings = new SettingViewModel(_config, _downloadManager, _pluginManager); // Plugins live in Settings now

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

        SetupAppShell();
    }

    private bool _quitting;

    /// <summary>
    /// Wires the system tray (#3), close-to-tray, run-at-startup (#4) and the update check (#6) once the
    /// config is loaded and the window exists.
    /// </summary>
    private void SetupAppShell()
    {
        if (View is not Window window)
            return;

        // Focus-aware notification routing: any window active ⇒ in-app toasts; unfocused/tray ⇒ OS.
        NotificationService.SetFocused(window.IsActive);
        window.Activated += (_, _) => NotificationService.SetFocused(true);
        window.Deactivated += (_, _) => NotificationService.SetFocused(false);

        TrayService.Init(window, Quit);
        TrayService.NotificationsToggled = enabled =>
        {
            _config.Settings.EnableNotifications = enabled;
            SaveSoon();
        };
        UpdateFlow.RequestQuit = Quit;
        UpdateFlow.PromptUpdate = info => DialogHelper.ShowUpdatePrompt(info); // in-app Download/Later dialog

        if (_config.Settings.EnableSystemTray)
            TrayService.Enable();

        // Closing the window keeps the app alive in the tray (downloads keep running) unless the user
        // really quit from the tray menu, the tray is turned off, or an update is staged (closing should
        // then actually exit so the update applies).
        window.Closing += (_, e) =>
        {
            // Only intercept the close if a tray icon is actually present — otherwise there'd be no way
            // to bring the window back. When an update is ready, let the close go through so it installs.
            if (TrayService.IsActive && !_quitting && !UpdateFlow.IsReady)
            {
                e.Cancel = true;
                window.Hide();
                // Hidden to tray: route notifications to the OS. macOS doesn't fire Deactivated on Hide,
                // so set this explicitly (the visibility check in NotificationService backs it up).
                NotificationService.SetFocused(false);
            }
        };

        // Keep the OS autostart entry in sync with the setting on every launch.
        StartupService.Apply(_config.Settings.RunAtStartup);

        // Local API + browser integration: extension links open the Add dialog pre-filled; the
        // /api routes act on the manager directly (silent adds from scripts and the CLI).
        LocalApiService.OnUrlCaptured = CaptureUrl;
        LocalApiService.Manager = _downloadManager;
        LocalApiService.Config = _config;
        if (_config.Settings.EnableBrowserIntegration)
        {
            LocalApiService.Start();
            // If the preferred port was taken and we fell back within the declared range, tell the user
            // once (this setup runs once per session) so the extension's "not connected" makes sense.
            if (LocalApiService.IsRunning && LocalApiService.EffectivePort != LocalApiService.PreferredPort)
                NotificationService.Notify(
                    Localizer.Instance["LocalApi_PortChangedTitle"],
                    string.Format(Localizer.Instance["LocalApi_PortChangedMsg"], LocalApiService.EffectivePort),
                    false);
        }

        // Single instance: a second launch forwards its message here. A structured "add:{json}"
        // (from the CLI) is added silently — no dialog, no focus steal; a plain URL keeps today's
        // behavior (surface the window and open Add pre-filled).
        SingleInstanceService.SetMessageHandler(msg =>
        {
            if (msg != null && msg.StartsWith(SingleInstanceService.AddPrefix, StringComparison.Ordinal))
            {
                SilentAdd(msg[SingleInstanceService.AddPrefix.Length..]);
                return;
            }
            BringToFront();
            if (!string.IsNullOrWhiteSpace(msg))
                CaptureUrl(msg);
        });
        // Handle args passed to this (the first) instance too: a CLI add payload or a bare URL.
        var startupArgs = Environment.GetCommandLineArgs();
        var cliAdd = Array.IndexOf(startupArgs, CliParser.CliAddSwitch);
        if (cliAdd >= 0 && cliAdd + 1 < startupArgs.Length)
            SilentAdd(startupArgs[cliAdd + 1]);
        else if (SingleInstanceService.FirstUrl(startupArgs) is { } startupUrl)
            CaptureUrl(startupUrl);

        // Launched at OS startup with --minimized → start hidden in the tray.
        if (_config.Settings.EnableSystemTray &&
            Environment.GetCommandLineArgs().Contains("--minimized"))
        {
            window.Hide();
            NotificationService.SetFocused(false); // started hidden in tray ⇒ OS notifications
        }

        if (_config.Settings.AutoUpdate)
            _ = UpdateFlow.CheckAsync(manual: false);
    }

    /// <summary>A CLI "add" payload arrived (forwarded or via --cli-add) — add it with no UI.</summary>
    private void SilentAdd(string json)
    {
        var req = ApiAddRequest.FromJson(json ?? string.Empty);
        if (req.Error != null)
        {
            AppLog.Info($"Ignored invalid CLI add payload: {req.Error}");
            return;
        }
        _downloadManager.Add(LocalApiService.BuildItem(req, _config), autoStart: req.Start);
    }

    /// <summary>A link arrived from the browser extension — surface the window and open Add pre-filled.</summary>
    private void CaptureUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;
        BringToFront();
        DownloadUrl = url;
        _ = AddDownloadItem();
    }

    /// <summary>Restores + activates the main window (used by single-instance and captured links).</summary>
    private void BringToFront()
    {
        if (View is not Window window)
            return;
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
        window.Topmost = true;
        window.Topmost = false; // brief topmost flip nudges it to the foreground across WMs
    }

    /// <summary>Really exit the app (from the tray menu / updater), bypassing close-to-tray.</summary>
    private void Quit()
    {
        _quitting = true;
        if (View is not Window window)
            return;
        // Close any open owned dialogs FIRST. Quitting is reachable from inside a modal (Settings →
        // "Restart to update"), and on macOS closing the owner while its modal's nested native session
        // is still running swallows the shutdown — the app never exits, so a staged update is never
        // applied ("clicked restart and nothing happened", v1.5.0 on macOS).
        foreach (var child in window.OwnedWindows)
            child.Close();
        window.Close();
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

    /// <summary>
    /// Fired when every download has finished. Shows the "all complete" notification and, if the user
    /// opted in, starts the cancelable shutdown countdown.
    /// </summary>
    private void OnAllDownloadsCompleted()
    {
        var s = _config?.Settings;
        if (s == null)
            return;

        if (s.EnableNotifications && s.NotifyOnAllComplete)
            NotificationService.NotifyAllCompleted(_downloadManager.CompletedCount);

        if (s.ShutdownOnCompletion)
            ShutdownService.Schedule(notify: s.EnableNotifications && s.NotifyOnShutdown);
    }

    private void OnStatsChanged()
    {
        this.RaisePropertyChanged(nameof(TotalSpeedText));
        this.RaisePropertyChanged(nameof(ActiveCount));
        this.RaisePropertyChanged(nameof(QueuedCount));
        this.RaisePropertyChanged(nameof(CompletedCount));
        this.RaisePropertyChanged(nameof(AllCount));
        this.RaisePropertyChanged(nameof(ActiveFilterCount));
        this.RaisePropertyChanged(nameof(QueuedFilterCount));
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
        this.RaisePropertyChanged(nameof(IsQueuedSelected));
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
            new AddDownloadItemView(), new AddDownloadItemViewModel(_config, _downloadUrl, manager: _downloadManager), _config);

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
