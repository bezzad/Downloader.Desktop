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

        AddDownloadItemCommand = ReactiveCommand.CreateFromTask(() => AddDownloadItem());
        StartAllCommand = ReactiveCommand.Create(() => _downloadManager.StartAll());
        StopAllCommand = ReactiveCommand.Create(() => _downloadManager.StopAll());
        ClearAllCommand = ReactiveCommand.Create(() => _downloadManager.ClearCompleted());

        ShowAllCommand = ReactiveCommand.Create(() => SelectFilter(StatusFilter.All));
        ShowActiveCommand = ReactiveCommand.Create(() => SelectFilter(StatusFilter.Active));
        ShowQueuedCommand = ReactiveCommand.Create(() => SelectFilter(StatusFilter.Queued));
        ShowStoppedCommand = ReactiveCommand.Create(() => SelectFilter(StatusFilter.Stopped));
        ShowCompletedCommand = ReactiveCommand.Create(() => SelectFilter(StatusFilter.Completed));
        ShowFailedCommand = ReactiveCommand.Create(() => SelectFilter(StatusFilter.Failed));
        // Management pages open in-window: the central ContentControl swaps between the downloads
        // list and Queues/Scheduler/Settings; the toolbar's Downloads button returns to the list.
        ShowDownloadsCommand = ReactiveCommand.Create(() => Navigate(NavSection.Downloads));
        ShowQueuesCommand = ReactiveCommand.Create(() => Navigate(NavSection.Queues));
        ShowSchedulerCommand = ReactiveCommand.Create(() => Navigate(NavSection.Scheduler));
        ShowSettingViewCommand = ReactiveCommand.Create(() => Navigate(NavSection.Settings));
        ToggleSidebarCommand = ReactiveCommand.Create(() => IsSidebarExpanded = !IsSidebarExpanded);
        ShowAboutCommand = ReactiveCommand.CreateFromTask(DialogHelper.ShowAbout);
        // In-app Donate modal — opening a browser page gave no visible feedback ("it sound like
        // do nothing"); the modal shows the channels right in the app (USDT copies in-app).
        DonateCommand = ReactiveCommand.CreateFromTask(DialogHelper.ShowDonate);
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
    public ICommand ShowStoppedCommand { get; }
    public ICommand ShowCompletedCommand { get; }
    public ICommand ShowFailedCommand { get; }
    public ICommand ShowDownloadsCommand { get; }
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
    public bool IsStoppedSelected => _section == NavSection.Downloads && _filter == StatusFilter.Stopped;
    public bool IsCompletedSelected => _section == NavSection.Downloads && _filter == StatusFilter.Completed;
    public bool IsFailedSelected => _section == NavSection.Downloads && _filter == StatusFilter.Failed;
    public bool IsDownloadsSelected => _section == NavSection.Downloads;
    public bool IsQueuesSelected => _section == NavSection.Queues;
    public bool IsSchedulerSelected => _section == NavSection.Scheduler;
    public bool IsSettingsSelected => _section == NavSection.Settings;

    // ---- Status bar ----
    public string TotalSpeedText => FormatSpeed(_downloadManager.TotalSpeed);

    /// <summary>Cumulative bytes downloaded across all rows, human-readable (#18). Recomputed on the
    /// stats pump — a single O(n) sum per 250 ms tick, negligible next to the per-row flush.</summary>
    public string TotalDownloadedText =>
        DownloadItemViewModel.FormatBytes(_downloadManager.Items.Sum(i => i.Downloaded));
    public int ActiveCount => _downloadManager.ActiveCount;
    public int QueuedCount => _downloadManager.QueuedCount;
    public int CompletedCount => _downloadManager.CompletedCount;

    // ---- Footer filter counts (each matches its StatusFilter bucket exactly, so the buttons are disjoint) ----
    public int AllCount => _downloadManager.Items.Count;
    public int ActiveFilterCount => _downloadManager.Items.Count(i =>
        i.Status is DownloadStatus.Running);
    public int QueuedFilterCount => _downloadManager.Items.Count(i =>
        i.Status is DownloadStatus.Created or DownloadStatus.None);
    public int StoppedFilterCount => _downloadManager.Items.Count(i =>
        i.Status is DownloadStatus.Paused or DownloadStatus.Stopped);
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

        // Self-heal plugin binary dependencies (yt-dlp/deno/ffmpeg…): an install-time fetch that was
        // interrupted (app closed, network drop) otherwise never retries and the plugin half-works.
        _ = Task.Run(EnsurePluginDependenciesAsync);

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

        // The notch overlay ("dynamic island") is opt-in; it runs independently of the main window so
        // it stays visible while the app sits in the tray.
        if (_config.Settings.EnableNotch)
            NotchService.Start(_downloadManager);

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
            // If the preferred port was taken and we fell back within the declared range, tell the user
            // once so the extension's "not connected" makes sense. Subscribed BEFORE Start so it also
            // covers a LATE bind from the background retry (a transient startup port conflict used to
            // leave the API silently dead until the user toggled the feature — the reported bug).
            var portNotified = false;
            LocalApiService.StatusChanged += () =>
            {
                if (portNotified || !LocalApiService.IsRunning ||
                    LocalApiService.EffectivePort == LocalApiService.PreferredPort)
                    return;
                portNotified = true;
                NotificationService.Notify(
                    Localizer.Instance["LocalApi_PortChangedTitle"],
                    string.Format(Localizer.Instance["LocalApi_PortChangedMsg"], LocalApiService.EffectivePort),
                    false);
            };
            LocalApiService.Start();
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
        }

        if (_config.Settings.AutoUpdate)
        {
            _ = UpdateFlow.CheckAsync(manual: false);
            _ = CheckPluginUpdatesAsync();
        }
    }

    /// <summary>
    /// Background check for updates to INSTALLED optional plugins: compare each loaded optional plugin's
    /// version against the release catalog and, for any that's newer, show a single actionable notification
    /// the user can accept (download → verify sha256 → swap). Never auto-updates; failure-tolerant (an
    /// empty/unreachable catalog is a no-op). Built-ins are excluded (they update with the app).
    /// </summary>
    private async Task CheckPluginUpdatesAsync()
    {
        try
        {
            var catalog = await PluginCatalogService.FetchAsync().ConfigureAwait(true);
            if (catalog.Count == 0)
                return;

            foreach (var descriptor in _pluginManager.Plugins.Where(p => !p.IsBuiltIn).ToList())
            {
                var info = catalog.FirstOrDefault(c => c.Id == descriptor.Id);
                if (info == null || !PluginCatalogService.MeetsMinAppVersion(info.MinAppVersion) ||
                    !PluginCatalogService.IsNewer(info.Version, descriptor.Version))
                    continue;

                // The in-window action lives on the Settings → Plugins row (its "Update" button, shown via
                // PluginRowViewModel.UpdateAvailable) — the notification just points the user there.
                var title = Localizer.Instance["Plugins_UpdateAvailable"];
                var message = $"{descriptor.Name} → v{info.Version}";
                NotificationService.Notify(title, message, false);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Plugin update check failed", ex);
        }
    }

    /// <summary>Background retry of any enabled plugin's missing binary dependencies (resumable — a
    /// half-downloaded archive from an interrupted install is picked up, a corrupt one replaced).
    /// Failure-tolerant: offline just logs and the next launch tries again.</summary>
    private async Task EnsurePluginDependenciesAsync()
    {
        foreach (var descriptor in _pluginManager.Plugins.Where(p => p.IsEnabled).ToList())
        {
            try
            {
                var deps = _pluginManager.GetRuntimeDependencies(descriptor.Id);
                if (deps.Count == 0)
                    continue;
                await PluginDependencyInstaller.EnsureAllAsync(deps, null, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Error($"Dependency check for plugin {descriptor.Id} failed", ex);
            }
        }
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
    private void BringToFront() => Services.WindowActivation.BringToFront(View as Window);

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
        // OS taskbar/dock progress (#4) — cheap: Update() no-ops when the value hasn't changed.
        var (visible, fraction) = TaskbarProgressService.Aggregate(_downloadManager.Items);
        TaskbarProgressService.Update(View as Avalonia.Controls.Window, visible, fraction);

        this.RaisePropertyChanged(nameof(TotalSpeedText));
        this.RaisePropertyChanged(nameof(TotalDownloadedText));
        this.RaisePropertyChanged(nameof(ActiveCount));
        this.RaisePropertyChanged(nameof(QueuedCount));
        this.RaisePropertyChanged(nameof(CompletedCount));
        this.RaisePropertyChanged(nameof(AllCount));
        this.RaisePropertyChanged(nameof(ActiveFilterCount));
        this.RaisePropertyChanged(nameof(QueuedFilterCount));
        this.RaisePropertyChanged(nameof(StoppedFilterCount));
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
        this.RaisePropertyChanged(nameof(IsDownloadsSelected));
        this.RaisePropertyChanged(nameof(IsAllSelected));
        this.RaisePropertyChanged(nameof(IsActiveSelected));
        this.RaisePropertyChanged(nameof(IsQueuedSelected));
        this.RaisePropertyChanged(nameof(IsStoppedSelected));
        this.RaisePropertyChanged(nameof(IsCompletedSelected));
        this.RaisePropertyChanged(nameof(IsFailedSelected));
        this.RaisePropertyChanged(nameof(IsQueuesSelected));
        this.RaisePropertyChanged(nameof(IsSchedulerSelected));
        this.RaisePropertyChanged(nameof(IsSettingsSelected));
    }

    /// <summary>Open the Add dialog seeded with the given text WITHOUT routing it through the top-bar box —
    /// used for a large paste, so the top box never lays out thousands of lines (the freeze). The top box
    /// stays empty.</summary>
    public Task OpenAddWithText(string text) => AddDownloadItem(text);

    private async Task AddDownloadItem(string seed = null)
    {
        var url = seed ?? _downloadUrl;
        // Always open the dialog; URLs can be typed there if the top box was empty.
        var result = await DialogHelper.ShowDialog<AddDownloadItemView, AddDownloadItemViewModel, List<DownloadItem>>(
            new AddDownloadItemView(),
            new AddDownloadItemViewModel(_config, url, manager: _downloadManager,
                getVariants: (u, ct) => _pluginManager.GetVariantsAsync(u, ct),
                getResolverName: u => _pluginManager.FindResolverPluginName(u)),
            _config);

        if (result is { Count: > 0 })
        {
            DownloadUrl = string.Empty;
            SelectFilter(StatusFilter.All);
            // The dialog is already closed — stream the rows in UI-yielding slices so a 2k-link add
            // never freezes the window (the user watches them appear; order/timing doesn't matter).
            await _downloadManager.AddRangeAsync(result, autoStart: true);
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
