using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.Views;
using System;
using System.Collections.ObjectModel;
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
    private bool _isCategorySidebarOpen;

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
        ShowArchivedCommand = ReactiveCommand.Create(() => SelectFilter(StatusFilter.Archived));
        // Management pages open in-window: the central ContentControl swaps between the downloads
        // list and Queues/Scheduler/Settings; the toolbar's Downloads button returns to the list.
        ShowDownloadsCommand = ReactiveCommand.Create(() => Navigate(NavSection.Downloads));
        ShowQueuesCommand = ReactiveCommand.Create(() => Navigate(NavSection.Queues));
        ShowSchedulerCommand = ReactiveCommand.Create(() => Navigate(NavSection.Scheduler));
        ShowSettingViewCommand = ReactiveCommand.Create(() => Navigate(NavSection.Settings));
        ToggleCategorySidebarCommand = ReactiveCommand.Create(() => IsCategorySidebarOpen = !IsCategorySidebarOpen);
        AddCategoryCommand = ReactiveCommand.CreateFromTask(AddCategoryAsync);
        ClearFiltersCommand = ReactiveCommand.Create(ClearFilters);
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
    public ICommand ShowArchivedCommand { get; }
    public ICommand ShowDownloadsCommand { get; }
    public ICommand ShowQueuesCommand { get; }
    public ICommand ShowSchedulerCommand { get; }
    public ICommand ShowSettingViewCommand { get; }
    /// <summary>Shows or hides the category sidebar. Two states only — the deleted nav rail's third,
    /// icons-only state was part of what made it confusing.</summary>
    public ICommand ToggleCategorySidebarCommand { get; }

    /// <summary>Opens the editor for a brand-new category.</summary>
    public ICommand AddCategoryCommand { get; }

    /// <summary>Drops every filter at once — the empty state's way out.</summary>
    public ICommand ClearFiltersCommand { get; }
    public ICommand ShowAboutCommand { get; }
    public ICommand DonateCommand { get; }
    public ICommand ApplyUpdateCommand { get; }

    /// <summary>True once a new version is downloaded and ready — shows the nav "Update Downloader" button.</summary>
    public bool IsUpdateReady => UpdateFlow.IsReady;

    private void OnUpdateStateChanged() => this.RaisePropertyChanged(nameof(IsUpdateReady));

    /// <summary>
    /// Whether the category sidebar is showing. Off on first run — it is an extra, not the way the
    /// app works — and remembered across restarts once the user opens it.
    /// </summary>
    public bool IsCategorySidebarOpen
    {
        get => _isCategorySidebarOpen;
        set
        {
            if (_isCategorySidebarOpen == value)
                return;

            this.RaiseAndSetIfChanged(ref _isCategorySidebarOpen, value);
            if (_config != null)
            {
                _config.IsCategorySidebarOpen = value;
                SaveSoon();
            }

            if (value)
                RefreshCategoryCounts();
        }
    }

    /// <summary>The sidebar's rows: "All", then every category in the user's order.</summary>
    public ObservableCollection<CategoryRowViewModel> CategoryRows { get; } = new();

    /// <summary>Id of the category the list is narrowed to, or null for all of them.</summary>
    public string SelectedCategoryId
    {
        get => Downloads?.CategoryFilter;
        set
        {
            if (Downloads is null || Downloads.CategoryFilter == value)
                return;

            Downloads.CategoryFilter = value;
            foreach (var row in CategoryRows)
                row.IsSelected = row.Id == value;
            this.RaisePropertyChanged();
            RefreshCategoryCounts();
        }
    }

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
    public bool IsArchivedSelected => _section == NavSection.Downloads && _filter == StatusFilter.Archived;
    public bool IsDownloadsSelected => _section == NavSection.Downloads;

    /// <summary>The downloads page showing the working list — i.e. not the archived view. The toolbar's
    /// Start/Pause/Stop/Archive cluster is bound to this and the Restore/Remove cluster to
    /// <see cref="IsArchivedSelected"/>, so opening a management page hides both.</summary>
    public bool IsWorkingListSelected => IsDownloadsSelected && !IsArchivedSelected;
    public bool IsQueuesSelected => _section == NavSection.Queues;
    public bool IsSchedulerSelected => _section == NavSection.Scheduler;
    public bool IsSettingsSelected => _section == NavSection.Settings;

    // ---- Status bar ----
    public string TotalSpeedText => FormatSpeed(_downloadManager.TotalSpeed);

    /// <summary>Cumulative bytes downloaded across all rows, human-readable (#18). Recomputed on the
    /// stats pump — a single O(n) sum per 250 ms tick, negligible next to the per-row flush.</summary>
    public string TotalDownloadedText =>
        DownloadItemViewModel.FormatBytes(_downloadManager.Items.Where(i => !i.IsArchived).Sum(i => i.Downloaded));
    public int ActiveCount => _downloadManager.ActiveCount;
    public int QueuedCount => _downloadManager.QueuedCount;
    public int CompletedCount => _downloadManager.CompletedCount;

    // ---- Footer filter counts (each matches its StatusFilter bucket exactly, so the buttons are disjoint) ----
    // Archived items are excluded from EVERY status count, All included — a pill's number has to be the
    // number of rows clicking it shows, and the archived ones are only ever shown by the Archived pill.
    public int AllCount => _downloadManager.Items.Count(i => !i.IsArchived);
    public int ActiveFilterCount => _downloadManager.Items.Count(i =>
        !i.IsArchived && i.Status is DownloadStatus.Running);
    public int QueuedFilterCount => _downloadManager.Items.Count(i =>
        !i.IsArchived && i.Status is DownloadStatus.Created or DownloadStatus.None);
    public int StoppedFilterCount => _downloadManager.Items.Count(i =>
        !i.IsArchived && i.Status is DownloadStatus.Paused or DownloadStatus.Stopped);
    public int CompletedFilterCount => _downloadManager.Items.Count(i => !i.IsArchived && i.Status == DownloadStatus.Completed);
    public int FailedFilterCount => _downloadManager.Items.Count(i =>
        !i.IsArchived && i.Status is DownloadStatus.Failed);
    public int ArchivedFilterCount => _downloadManager.Items.Count(i => i.IsArchived);

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

        // The empty state's "Clear filters" must reset the SHELL's filters too — the category
        // sidebar's selection, the footer status pills and the search box all live up here. Without
        // this the page clears its own state and the list refills while every control still shows
        // the filter as applied.
        Downloads = new DownloadsViewModel(_downloadManager) { ClearFiltersRequested = ClearFilters };
        Queues = new QueuesViewModel(_config, _downloadManager);
        Scheduler = new SchedulerViewModel(_config, _downloadManager);
        Settings = new SettingViewModel(_config, _downloadManager, _pluginManager); // Plugins live in Settings now

        // Persist settings to disk as soon as the user changes one (#24), debounced so spinning a
        // NumericUpDown doesn't hammer the file.
        ((System.ComponentModel.INotifyPropertyChanged)Settings).PropertyChanged += (_, _) => SaveSoon();

        this.RaisePropertyChanged(nameof(Downloads));

        // The sidebar's own state is persisted; the rows are rebuilt whenever the category list
        // changes, from anywhere (the editor, a reorder, an import).
        _isCategorySidebarOpen = _config.IsCategorySidebarOpen;
        this.RaisePropertyChanged(nameof(IsCategorySidebarOpen));
        RebuildCategoryRows();
        _downloadManager.Categories.Changed += () => Dispatcher.UIThread.Post(RebuildCategoryRows);

        Navigate(NavSection.Downloads);
        OnStatsChanged();

        // Periodic autosave so an unclean exit doesn't lose the list/settings.
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _autoSaveTimer.Tick += (_, _) => RequestSave();
        _autoSaveTimer.Start();

        SetupAppShell();
    }

    private bool _quitting;

    /// <summary>True while the remembered layout is being applied, so the window's own resize/move
    /// events cannot be mistaken for the user re-arranging it.</summary>
    private bool _applyingLayout;

    /// <summary>The size the window reported just BEFORE the remembered layout was applied, and when
    /// that happened. The platform confirms a resize/move ASYNCHRONOUSLY, so the restore's own echo
    /// arrives once <see cref="_applyingLayout"/> is already back to false — and the first echo
    /// carries the NEW position with this OLD size (measured on a real Ubuntu/Wayland session).
    /// Recording that pair persists a size the user never chose.</summary>
    private Size? _preRestoreSize;
    private DateTime _layoutAppliedAt = DateTime.MinValue;

    /// <summary>How long a stale echo is still expected after a restore. A backstop only — the echo
    /// is identified by its SIZE, so a genuine resize in the first moments is still recorded, and a
    /// window manager that refuses our size cannot leave the app deaf to the user for ever.</summary>
    internal static TimeSpan LayoutEchoGrace { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Applies the remembered layout (see <see cref="WindowLayoutPolicy"/>) to the main window.
    /// Everything that could be wrong with it — a screen that is gone, a size bigger than this desktop,
    /// a corrupt record — is already handled by the policy; this just assigns the result.</summary>
    internal void RestoreWindowLayout(Window window)
    {
        if (window == null)
            return;

        try
        {
            _applyingLayout = true;
            _preRestoreSize = window.ClientSize;

            // The window's own declared size is the fallback: it is what a first run gets.
            var fallbackWidth = double.IsFinite(window.Width) ? window.Width : window.ClientSize.Width;
            var fallbackHeight = double.IsFinite(window.Height) ? window.Height : window.ClientSize.Height;

            var layout = WindowLayoutPolicy.Resolve(
                _config?.MainWindow, ScreenAreas(window),
                window.MinWidth, window.MinHeight,
                fallbackWidth, fallbackHeight,
                window.RenderScaling);

            window.Width = layout.Width;
            window.Height = layout.Height;

            if (layout.Position is { } position)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Position = position;
            }

            // Maximize LAST, so the size/position set above is what the window restores down to.
            if (layout.IsMaximized)
                window.WindowState = WindowState.Maximized;
        }
        catch (Exception ex)
        {
            // A window that opens at its default size is a far better outcome than one that fails to open.
            AppLog.Error("Could not restore the window layout", ex);
        }
        finally
        {
            _applyingLayout = false;
            _layoutAppliedAt = DateTime.UtcNow;
        }
    }

    /// <summary>Every connected screen's working area, primary first (the policy re-centres on it).
    /// Empty when the platform reports no screens (headless, and some Linux sessions).</summary>
    private static IReadOnlyList<PixelRect> ScreenAreas(Window window)
    {
        var areas = new List<PixelRect>();
        try
        {
            var screens = window.Screens;
            if (screens == null)
                return areas;

            var primary = screens.Primary;
            if (primary != null)
                areas.Add(primary.WorkingArea);
            foreach (var screen in screens.All)
            {
                if (!ReferenceEquals(screen, primary))
                    areas.Add(screen.WorkingArea);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Could not read the connected screens: " + ex.Message);
        }
        return areas;
    }

    /// <summary>Records the layout on every resize, move and maximize/restore, through the existing
    /// debounced save — so an exit the app never sees still leaves the last layout on disk.</summary>
    private void TrackWindowLayout(Window window)
    {
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty || e.Property == TopLevel.ClientSizeProperty)
                CaptureWindowLayout(window);
        };
        window.PositionChanged += (_, _) => CaptureWindowLayout(window);

        // Record a baseline straight away when nothing is remembered yet: the window is shown before
        // this runs, so the first maximize would otherwise have no normal bounds to keep and would
        // store the maximized frame as the size to restore down to.
        if (_config?.MainWindow == null)
            CaptureWindowLayout(window);
    }

    internal void CaptureWindowLayout(Window window)
    {
        if (_applyingLayout || _config == null || window == null)
            return;

        // Drop the restore's own echo: shortly after applying the layout the window reports the new
        // position while still carrying its PRE-restore size, and storing that pair would quietly
        // replace the remembered size with the old one. Matched on the size itself, so a real resize
        // — even one made immediately — is still recorded.
        if (WindowLayoutPolicy.IsRestoreEcho(
                window.ClientSize, _preRestoreSize, DateTime.UtcNow - _layoutAppliedAt, LayoutEchoGrace))
            return;

        try
        {
            // A window that has not laid out yet reports no size; recording that would persist a
            // zero-sized layout over a perfectly good one.
            if (window.WindowState == WindowState.Normal &&
                (window.ClientSize.Width <= 0 || window.ClientSize.Height <= 0))
                return;

            var captured = WindowLayoutPolicy.Capture(
                window.WindowState, window.Position,
                window.ClientSize.Width, window.ClientSize.Height,
                _config.MainWindow);

            // Minimized/full-screen hand back the same record — nothing to save.
            if (captured == null || ReferenceEquals(captured, _config.MainWindow))
                return;

            _config.MainWindow = captured;
            SaveSoon();   // already debounced; a resize drag costs two doubles per frame, no I/O
        }
        catch (Exception ex)
        {
            // These handlers run on the UI thread: an escaping exception would take the dispatcher —
            // and with it the whole window — down.
            AppLog.Error("Could not record the window layout", ex);
        }
    }

    /// <summary>
    /// Wires the system tray (#3), close-to-tray, run-at-startup (#4) and the update check (#6) once the
    /// config is loaded and the window exists.
    /// </summary>
    private void SetupAppShell()
    {
        if (View is not Window window)
            return;

        // Reopen the window the way the user left it (#15), then keep the record up to date as they
        // resize/move/maximize it — waiting for a clean exit would miss a tray quit or an OS restart.
        RestoreWindowLayout(window);
        TrackWindowLayout(window);

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

        // winget/portable installs create no Start-menu entry — self-register one (Windows, first run).
        StartMenuShortcut.EnsureOnWindows();

        // Local API + browser integration: extension links open the Add dialog pre-filled; the
        // /api routes act on the manager directly (silent adds from scripts and the CLI).
        LocalApiService.OnUrlCaptured = CaptureUrl;
        // A confirm-mode /api/add opens the Add dialog instead of adding straight away. Fire-and-forget:
        // the request was already answered with its ticket and must not wait for the user.
        LocalApiService.OnAddConfirmationRequested = (req, ticket) => _ = CaptureAddRequest(req, ticket);
        LocalApiService.Manager = _downloadManager;
        LocalApiService.Config = _config;
        LocalApiService.Plugins = _pluginManager;
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
            _ = CheckExtensionUpdateAsync();
        }
    }

    /// <summary>
    /// Background check for an out-of-date browser extension: compare what each browser's extension last
    /// reported (see <see cref="LocalApiService.LastSeenExtensions"/>) against the published catalog and,
    /// if any is older, point the user at Settings once.
    ///
    /// <para>Only browsers that have ACTUALLY contacted this app are considered — an extension that has
    /// never called is not "out of date", it is not installed, and nagging about it would be noise. The
    /// app never replaces an extension already loaded in a browser; only the browser can do that.</para>
    /// </summary>
    internal async Task CheckExtensionUpdateAsync()
    {
        try
        {
            var seen = LocalApiService.LastSeenExtensions;
            if (seen.Count == 0)
                return;   // nothing has called us — nothing to be out of date

            // An empty catalog is NOT the end of the check: the app carries its own copy of the extension,
            // and that copy is what a browser running an older one is behind. Returning here meant the
            // check could never fire on any machine today, since no published release carries a catalog.
            var catalog = await ExtensionCatalogService.FetchAsync().ConfigureAwait(true);

            // One pointer per run, however many browsers are behind: the action lives in the window
            // (Settings → Install browser extension), because a native notification cannot carry a click.
            if (ExtensionCatalogService.ShouldWarnAboutExtension(
                    seen.Values.Select(id => id.Version), catalog, ExtensionInstallService.BundledVersion(),
                    out var reported, out var available))
            {
                NotificationService.Notify(
                    Localizer.Instance["Ext_Install_Button"],
                    string.Format(Localizer.Instance["Ext_UpdateAvailable"], reported, available),
                    false);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Browser extension update check failed", ex);
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

    /// <summary>A CLI "add" payload arrived (forwarded or via --cli-add) — add it with no UI.
    /// UNCONDITIONALLY silent: it ignores both the request's `confirm` and the "ask before adding
    /// programmatic downloads" setting, because a script cannot answer a modal and must not be left
    /// waiting on one. Internal so a test can pin that.</summary>
    internal void SilentAdd(string json)
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

    /// <summary>A confirm-mode programmatic add arrived (issue #13) — surface the window and open the Add
    /// dialog carrying the WHOLE request, then resolve its ticket from what the user chose. The API has
    /// already answered `202`, so nothing is waiting on this: the caller follows the ticket instead.
    /// A multi-item confirm (the user ticked several variants) reports the first item's id — the ticket
    /// contract names one download, and every item is on the list either way.</summary>
    internal async Task CaptureAddRequest(ApiAddRequest req, string ticket)
    {
        if (req == null || req.Error != null || string.IsNullOrWhiteSpace(req.Url))
        {
            LocalApiService.ResolvePendingAdd(ticket, null);
            return;
        }

        BringToFront();
        string addedId = null;
        try
        {
            var result = await DialogHelper.ShowDialog<AddDownloadItemView, AddDownloadItemViewModel, List<DownloadItem>>(
                new AddDownloadItemView(),
                new AddDownloadItemViewModel(_config, req.Url, manager: _downloadManager,
                    getVariants: (u, ct) => _pluginManager.GetVariantsAsync(u, ct),
                    getResolverName: u => _pluginManager.FindResolverPluginName(u),
                    apiRequest: req),
                _config);

            if (result is { Count: > 0 })
            {
                SelectFilter(StatusFilter.All);
                await _downloadManager.AddRangeAsync(result, autoStart: req.Start);
                addedId = result[0].Id.ToString();
            }
        }
        catch (Exception ex)
        {
            // A dialog that failed to open added nothing, which is exactly what a cancel means to the
            // caller — resolving as cancelled below keeps the browser's own download untouched.
            AppLog.Error("Confirming a programmatic add failed", ex);
        }
        finally
        {
            LocalApiService.ResolvePendingAdd(ticket, addedId);
        }
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
        this.RaisePropertyChanged(nameof(ArchivedFilterCount));
        RefreshCategoryCounts();
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
        // The per-category counts state how many rows clicking that category would show, so they
        // move with the status filter.
        RefreshCategoryCounts();
    }

    // ---- Category sidebar --------------------------------------------------------------------

    /// <summary>Rebuilds the sidebar rows from the category list (after an add, edit, delete,
    /// reorder or import). Selection is kept when the chosen category is still there.</summary>
    private void RebuildCategoryRows()
    {
        var chosen = Downloads?.CategoryFilter;
        CategoryRows.Clear();
        CategoryRows.Add(new CategoryRowViewModel(null, SelectCategory, EditCategory, MoveCategory));
        foreach (var category in _downloadManager.Categories.Categories)
            CategoryRows.Add(new CategoryRowViewModel(category, SelectCategory, EditCategory, MoveCategory));

        // A category the user was filtering by can vanish (deleted, or absent from an import); fall
        // back to showing everything rather than to a filter that matches nothing.
        if (!string.IsNullOrWhiteSpace(chosen) && _downloadManager.Categories.ById(chosen) is null)
            chosen = null;

        foreach (var row in CategoryRows)
            row.IsSelected = row.Id == chosen;
        if (Downloads != null)
            Downloads.CategoryFilter = chosen;

        this.RaisePropertyChanged(nameof(SelectedCategoryId));
        RefreshCategoryCounts();
    }

    /// <summary>Recomputes each row's count under everything EXCEPT the category filter, so each
    /// number is exactly what clicking that row would show.</summary>
    private void RefreshCategoryCounts()
    {
        if (Downloads is null || CategoryRows.Count == 0)
            return;

        var visible = _downloadManager.Items.Where(Downloads.MatchesExceptCategory).ToList();
        foreach (var row in CategoryRows)
            row.Count = row.IsAll ? visible.Count : visible.Count(i => i.Category?.Id == row.Id);
    }

    private void SelectCategory(string categoryId) => SelectedCategoryId = categoryId;

    private void MoveCategory(CategoryRowViewModel row, int delta)
    {
        if (row?.Id is null)
            return;

        // The service renumbers and announces the change; the rebuild rides on that.
        _downloadManager.Categories.Move(row.Id, delta);
        RequestSave();
    }

    private async Task AddCategoryAsync() => await EditCategoryAsync(null);

    private void EditCategory(CategoryRowViewModel row) => _ = EditCategoryAsync(row?.Category);

    private async Task EditCategoryAsync(DownloadCategory category)
    {
        var edited = await DialogHelper.ShowCategoryEditor(_downloadManager.Categories, category);
        if (edited is null)
            return;

        if (category is null)
            _downloadManager.Categories.Add(edited);
        else
            _downloadManager.Categories.Update(edited);

        RequestSave();
    }

    private void ClearFilters()
    {
        Downloads?.ClearFilters();
        _filter = StatusFilter.All;
        // Assigned to the field, not through the property: the setter would push it back into
        // Downloads.Search, which has just been cleared.
        _searchText = null;
        foreach (var row in CategoryRows)
            row.IsSelected = row.IsAll;
        this.RaisePropertyChanged(nameof(SelectedCategoryId));
        this.RaisePropertyChanged(nameof(SearchText));
        RaiseNavFlags();
        RefreshCategoryCounts();
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
        this.RaisePropertyChanged(nameof(IsArchivedSelected));
        this.RaisePropertyChanged(nameof(IsWorkingListSelected));
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
