using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// Settings page exposing the full set of engine options (Basic / Advanced / Request) bound to
/// <see cref="DownloadSettings"/>, plus the app theme. Changes mutate the live config and are
/// persisted by the app on shutdown.
/// </summary>
public class SettingViewModel : ViewModelBase
{
    private readonly Config _config;
    private readonly IDownloadManager _manager;
    private DownloadSettings S => _config.Settings;

    /// <summary>Plugin management, embedded as a collapsible section in Settings (advanced — most users
    /// never need it, so it's tucked away here rather than a top-level menu).</summary>
    public PluginsViewModel Plugins { get; }

    public SettingViewModel(Config config, IDownloadManager manager = null, PluginManager pluginManager = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _manager = manager;
        // Keep the "Local API" row live: a LATE bind (the startup retry finally succeeding) or a stop
        // flips the dot/text without the user having to toggle the feature (the reported bug).
        LocalApiService.StatusChanged += RaiseLocalApiStatus;
        Plugins = new PluginsViewModel(pluginManager ?? new PluginManager(), _config);
        SelectSavePathCommand = ReactiveCommand.CreateFromTask(SelectSavePath);
        SwitchThemeCommand = ReactiveCommand.Create(SwitchTheme);
        OpenLogsFolderCommand = ReactiveCommand.Create(OpenLogsFolder);
        ExportLogsCommand = ReactiveCommand.CreateFromTask(ExportLogs);
        EmailLogsCommand = ReactiveCommand.Create(EmailLogs);
        ResetDefaultsCommand = ReactiveCommand.Create(ResetDefaults);
        // "Check for updates" while Idle → "Download update" once a version is available → "Restart to
        // update" once it's downloaded. The same button drives the whole flow from Settings.
        CheckUpdateCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (UpdateFlow.IsReady) UpdateFlow.ApplyAndRestart();
            else if (UpdateFlow.State == UpdateState.Available) await UpdateFlow.StartDownloadAsync();
            else await UpdateFlow.CheckAsync(manual: true);
        });
        CancelUpdateDownloadCommand = ReactiveCommand.Create(UpdateFlow.CancelDownload);
        UpdateFlow.Changed += OnUpdateStateChanged;
    }

    private void OnUpdateStateChanged()
    {
        this.RaisePropertyChanged(nameof(UpdateButtonText));
        this.RaisePropertyChanged(nameof(IsUpdateDownloading));
        this.RaisePropertyChanged(nameof(UpdateProgress));
        this.RaisePropertyChanged(nameof(UpdateProgressText));
        this.RaisePropertyChanged(nameof(AvailableVersionText));
        this.RaisePropertyChanged(nameof(HasAvailableVersion));
    }

    public ICommand CancelUpdateDownloadCommand { get; }

    /// <summary>"v1.4.0 available" once a newer version is detected (so the user sees WHICH version).</summary>
    public string AvailableVersionText =>
        string.IsNullOrWhiteSpace(UpdateFlow.AvailableTag)
            ? string.Empty
            : string.Format(Localizer.Instance["Update_VersionAvailable"], UpdateFlow.AvailableTag);

    public bool HasAvailableVersion =>
        UpdateFlow.State is UpdateState.Available or UpdateState.Downloading or UpdateState.Ready
        && !string.IsNullOrWhiteSpace(UpdateFlow.AvailableTag);

    /// <summary>Label for the update button: Check / Checking / Downloading% / Restart to update.</summary>
    public string UpdateButtonText => UpdateFlow.State switch
    {
        UpdateState.Checking => Localizer.Instance["Update_Checking"],
        UpdateState.Available => Localizer.Instance["Update_DownloadBtn"],
        UpdateState.Downloading => Localizer.Instance["Update_Downloading"],
        UpdateState.Ready => Localizer.Instance["Update_Restart"],
        _ => Localizer.Instance["Btn_CheckUpdate"]
    };

    public bool IsUpdateDownloading => UpdateFlow.State == UpdateState.Downloading;
    public double UpdateProgress => UpdateFlow.Progress;
    public string UpdateProgressText => $"{UpdateFlow.Progress:0}%";

    public ICommand SelectSavePathCommand { get; }
    public ICommand SwitchThemeCommand { get; }
    public ICommand OpenLogsFolderCommand { get; }
    public ICommand ExportLogsCommand { get; }
    public ICommand EmailLogsCommand { get; }
    public ICommand ResetDefaultsCommand { get; }
    public ICommand CheckUpdateCommand { get; }

    // ---- App behavior (tray / startup / updates) ----

    /// <summary>Keep running in the tray when the window is closed. Turning it off also turns off startup.</summary>
    public bool EnableSystemTray
    {
        get => S.EnableSystemTray;
        set
        {
            if (S.EnableSystemTray == value) return;
            S.EnableSystemTray = value;
            if (value) TrayService.Enable();
            else TrayService.Disable();

            // Run-at-startup needs the tray; disabling the tray must disable startup too.
            if (!value && S.RunAtStartup)
            {
                S.RunAtStartup = false;
                StartupService.Apply(false);
                this.RaisePropertyChanged(nameof(RunAtStartup));
            }
            this.RaisePropertyChanged();
        }
    }

    /// <summary>Show the top-center "dynamic island" overlay; starts/stops live.</summary>
    public bool EnableNotch
    {
        get => S.EnableNotch;
        set
        {
            if (S.EnableNotch == value) return;
            S.EnableNotch = value;
            if (value) NotchService.Start(_manager);
            else NotchService.Stop();
            this.RaisePropertyChanged();
        }
    }

    /// <summary>Launch hidden to the tray when the OS starts. Enabling it also enables the tray.</summary>
    public bool RunAtStartup
    {
        get => S.RunAtStartup;
        set
        {
            if (S.RunAtStartup == value) return;

            // Startup implies the tray; enabling startup with the tray off turns the tray on.
            if (value && !S.EnableSystemTray)
            {
                S.EnableSystemTray = true;
                TrayService.Enable();
                this.RaisePropertyChanged(nameof(EnableSystemTray));
            }

            S.RunAtStartup = value;
            StartupService.Apply(value);
            this.RaisePropertyChanged();
        }
    }

    public bool AutoUpdate
    {
        get => S.AutoUpdate;
        set { S.AutoUpdate = value; this.RaisePropertyChanged(); }
    }

    /// <summary>Restores every setting (and the theme) to its default value.</summary>
    private void ResetDefaults()
    {
        _config.Settings = DownloadSettings.New();
        _config.ThemeMode = ThemeVariant.Light;

        // Re-apply settings that affect runtime services immediately.
        AppLog.SetEnabled(S.EnableLogging);
        NotificationService.Enabled = S.EnableNotifications;
        if (S.EnableSystemTray) TrayService.Enable(); else TrayService.Disable();
        StartupService.Apply(S.RunAtStartup);
        if (S.EnableBrowserIntegration) LocalApiService.Start(); else LocalApiService.Stop();
        ThemeService.Apply(_config); // reset theme variant + accent together

        // Empty name tells the bindings every property changed, refreshing the whole page
        // (and triggering the debounced save wired to SettingViewModel.PropertyChanged).
        this.RaisePropertyChanged(string.Empty);
    }

    /// <summary>
    /// App version shown in the About card — the clean 3-part major.minor.patch (e.g. "1.1.2"), the
    /// SAME value the update check compares against the latest release tag, so About and "up to
    /// date / update available" always agree and match the published version.
    /// </summary>
    public string AppVersion => UpdateService.CurrentVersion.ToString();

    public bool EnableLogging
    {
        get => S.EnableLogging;
        set
        {
            S.EnableLogging = value;
            AppLog.SetEnabled(value);
            this.RaisePropertyChanged();
        }
    }

    public bool EnableNotifications
    {
        get => S.EnableNotifications;
        set
        {
            var wasEnabled = S.EnableNotifications;
            S.EnableNotifications = value;
            NotificationService.Enabled = value;
            this.RaisePropertyChanged();

            // When the user turns it on, send a sample so they can see how it looks and confirm
            // the feature is now active.
            if (value && !wasEnabled)
                NotificationService.Notify(
                    "Notifications enabled",
                    "You'll get a desktop alert here when a download completes or fails.",
                    isError: false);

            // The per-event toggles live under the master, so refresh their enabled state in the UI.
            this.RaisePropertyChanged(nameof(NotifyOnComplete));
            this.RaisePropertyChanged(nameof(NotifyOnFailed));
            this.RaisePropertyChanged(nameof(NotifyOnAllComplete));
            this.RaisePropertyChanged(nameof(NotifyOnShutdown));
        }
    }

    public bool NotifyOnComplete
    {
        get => S.NotifyOnComplete;
        set { S.NotifyOnComplete = value; this.RaisePropertyChanged(); }
    }

    public bool NotifyOnFailed
    {
        get => S.NotifyOnFailed;
        set { S.NotifyOnFailed = value; this.RaisePropertyChanged(); }
    }

    public bool NotifyOnAllComplete
    {
        get => S.NotifyOnAllComplete;
        set { S.NotifyOnAllComplete = value; this.RaisePropertyChanged(); }
    }

    public bool NotifyOnShutdown
    {
        get => S.NotifyOnShutdown;
        set { S.NotifyOnShutdown = value; this.RaisePropertyChanged(); }
    }

    /// <summary>Power off the computer once every download finishes (30 s cancelable notice first).</summary>
    public bool ShutdownOnCompletion
    {
        get => S.ShutdownOnCompletion;
        set { S.ShutdownOnCompletion = value; this.RaisePropertyChanged(); }
    }

    /// <summary>Let a browser extension hand links to the app over a local loopback port.</summary>
    public bool EnableBrowserIntegration
    {
        get => S.EnableBrowserIntegration;
        set
        {
            if (S.EnableBrowserIntegration == value) return;
            S.EnableBrowserIntegration = value;
            if (value) LocalApiService.Start();
            else LocalApiService.Stop();
            this.RaisePropertyChanged();
            RaiseLocalApiStatus();
        }
    }

    /// <summary>The effective loopback address the local API bound to, e.g. "127.0.0.1:15151".</summary>
    public string LocalApiAddress
    {
        get
        {
            var port = LocalApiService.EffectivePort != 0 ? LocalApiService.EffectivePort : LocalApiService.PreferredPort;
            return $"127.0.0.1:{port}";
        }
    }

    /// <summary>Live "connected" / "not running" indicator for the local API row.</summary>
    public string LocalApiStatusText =>
        LocalApiService.IsRunning ? Localizer.Instance["Set_LocalApiConnected"] : Localizer.Instance["Set_LocalApiOffline"];

    public bool IsLocalApiRunning => LocalApiService.IsRunning;

    /// <summary>Green when reachable, gray when not — same palette as the download status dots.</summary>
    public Avalonia.Media.IBrush LocalApiStatusBrush =>
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(LocalApiService.IsRunning ? "#1FA971" : "#6B7A83"));

    private void RaiseLocalApiStatus()
    {
        this.RaisePropertyChanged(nameof(LocalApiAddress));
        this.RaisePropertyChanged(nameof(LocalApiStatusText));
        this.RaisePropertyChanged(nameof(IsLocalApiRunning));
        this.RaisePropertyChanged(nameof(LocalApiStatusBrush));
    }


    // ---- Language ----
    public System.Collections.Generic.IReadOnlyList<LanguageOption> Languages => Localizer.Languages;

    public LanguageOption SelectedLanguage
    {
        get => Languages.FirstOrDefault(l => string.Equals(l.Code, S.Language, StringComparison.OrdinalIgnoreCase))
               ?? Languages[0];
        set
        {
            if (value == null) return;
            S.Language = value.Code;
            Localizer.Instance.Load(value.Code); // switches every bound string + RTL live
            this.RaisePropertyChanged();
        }
    }

    // ---- Theme ----
    public bool IsDarkTheme
    {
        get => _config.ThemeMode == ThemeVariant.Dark;
        set
        {
            _config.ThemeMode = value ? ThemeVariant.Dark : ThemeVariant.Light;
            this.RaisePropertyChanged();
        }
    }

    // ---- Accent ----
    public System.Collections.Generic.IReadOnlyList<AccentOption> Accents => ThemeService.Accents;

    public AccentOption SelectedAccent
    {
        get => ThemeService.Find(S.AccentColor);
        set
        {
            if (value == null) return;
            S.AccentColor = value.Key;
            ThemeService.ApplyAccent(value.Key); // live recolor across the app
            this.RaisePropertyChanged();
        }
    }

    // ---- Basic ----
    public string DefaultSavePath
    {
        get => S.DefaultSavePath;
        set { S.DefaultSavePath = value; this.RaisePropertyChanged(); }
    }

    /// <summary>When on, the folder picked for a new download becomes the default save path.</summary>
    public bool RememberLastSavePath
    {
        get => S.RememberLastSavePath;
        set { S.RememberLastSavePath = value; this.RaisePropertyChanged(); }
    }

    public int ChunkCount
    {
        get => S.ChunkCount;
        set { S.ChunkCount = value; this.RaisePropertyChanged(); }
    }

    public bool ParallelDownload
    {
        get => S.ParallelDownload;
        set { S.ParallelDownload = value; this.RaisePropertyChanged(); }
    }

    public int ParallelCount
    {
        get => S.ParallelCount;
        set { S.ParallelCount = value; this.RaisePropertyChanged(); }
    }

    /// <summary>Speed cap in KB/s (0 = unlimited), mapped to bytes for the engine.</summary>
    public long MaxSpeedKbPerSecond
    {
        get => S.MaximumBytesPerSecond <= 0 ? 0 : S.MaximumBytesPerSecond / 1024;
        set
        {
            S.MaximumBytesPerSecond = value <= 0 ? 0 : value * 1024;
            // Make the change bite immediately on running downloads that follow the global limit (items
            // with a per-item override are left alone) — same live-apply pattern as MaxConcurrentDownloads.
            _manager?.ApplyGlobalSpeedLimit(S.MaximumBytesPerSecond);
            this.RaisePropertyChanged();
        }
    }

    public int MaxConcurrentDownloads
    {
        get => S.MaxConcurrentDownloads;
        set
        {
            S.MaxConcurrentDownloads = Math.Max(1, value);
            // This is the limit users actually reach for, so make it bite: keep the primary queue's
            // concurrency cap in lockstep and re-pump so the change takes effect immediately (start
            // more if the cap grew; no new starts beyond it if it shrank). Without this the value only
            // seeded brand-new queues and never limited running downloads.
            if (_config.DefaultQueue is { } q)
            {
                q.MaxConcurrent = S.MaxConcurrentDownloads;
                _manager?.PumpQueue(q.Id);
            }

            this.RaisePropertyChanged();
        }
    }

    // ---- Advanced ----
    public int BufferBlockSize
    {
        get => S.BufferBlockSize;
        set { S.BufferBlockSize = value; this.RaisePropertyChanged(); }
    }

    public int MaxTryAgainOnFailure
    {
        get => S.MaxTryAgainOnFailure;
        set { S.MaxTryAgainOnFailure = value; this.RaisePropertyChanged(); }
    }

    public int BlockTimeout
    {
        get => S.BlockTimeout;
        set { S.BlockTimeout = value; this.RaisePropertyChanged(); }
    }

    public int HttpClientTimeout
    {
        get => S.HttpClientTimeout;
        set { S.HttpClientTimeout = value; this.RaisePropertyChanged(); }
    }

    public long MinimumSizeOfChunking
    {
        get => S.MinimumSizeOfChunking;
        set { S.MinimumSizeOfChunking = value; this.RaisePropertyChanged(); }
    }

    public long MinimumChunkSize
    {
        get => S.MinimumChunkSize;
        set { S.MinimumChunkSize = value; this.RaisePropertyChanged(); }
    }

    /// <summary>Max memory buffer in MB (0 = unlimited), mapped to bytes for the engine.</summary>
    public long MaxMemoryBufferMb
    {
        get => S.MaximumMemoryBufferBytes <= 0 ? 0 : S.MaximumMemoryBufferBytes / (1024 * 1024);
        set { S.MaximumMemoryBufferBytes = value <= 0 ? 0 : value * 1024 * 1024; this.RaisePropertyChanged(); }
    }

    public bool CheckDiskSizeBeforeDownload
    {
        get => S.CheckDiskSizeBeforeDownload;
        set { S.CheckDiskSizeBeforeDownload = value; this.RaisePropertyChanged(); }
    }

    public bool EnableAutoResumeDownload
    {
        get => S.EnableAutoResumeDownload;
        set { S.EnableAutoResumeDownload = value; this.RaisePropertyChanged(); }
    }

    public bool ClearPackageOnCompletionWithFailure
    {
        get => S.ClearPackageOnCompletionWithFailure;
        set { S.ClearPackageOnCompletionWithFailure = value; this.RaisePropertyChanged(); }
    }

    public Array FileExistPolicies { get; } = Enum.GetValues(typeof(FileExistPolicy));

    public FileExistPolicy SelectedFileExistPolicy
    {
        get => S.FileExistPolicy;
        set { S.FileExistPolicy = value; this.RaisePropertyChanged(); }
    }

    public string DownloadFileExtension
    {
        get => S.DownloadFileExtension;
        set { S.DownloadFileExtension = value; this.RaisePropertyChanged(); }
    }

    // ---- Request ----
    public string UserAgent
    {
        get => S.UserAgent;
        set { S.UserAgent = value; this.RaisePropertyChanged(); }
    }

    public string Referer
    {
        get => S.Referer;
        set { S.Referer = value; this.RaisePropertyChanged(); }
    }

    public string Accept
    {
        get => S.Accept;
        set { S.Accept = value; this.RaisePropertyChanged(); }
    }

    public bool AllowAutoRedirect
    {
        get => S.AllowAutoRedirect;
        set { S.AllowAutoRedirect = value; this.RaisePropertyChanged(); }
    }

    public int MaximumAutomaticRedirections
    {
        get => S.MaximumAutomaticRedirections;
        set { S.MaximumAutomaticRedirections = value; this.RaisePropertyChanged(); }
    }

    public int ConnectTimeout
    {
        get => S.ConnectTimeout;
        set { S.ConnectTimeout = value; this.RaisePropertyChanged(); }
    }

    public bool KeepAlive
    {
        get => S.KeepAlive;
        set { S.KeepAlive = value; this.RaisePropertyChanged(); }
    }

    public string ProxyAddress
    {
        get => S.ProxyAddress;
        set { S.ProxyAddress = value; this.RaisePropertyChanged(); }
    }

    private async Task SelectSavePath()
    {
        var path = await DialogHelper.OpenFolderPicker("Select default save folder");
        if (path != null)
            DefaultSavePath = path.LocalPath;
    }

    private void SwitchTheme()
    {
        if (Application.Current?.Styles[0] is FluentTheme)
            Application.Current.RequestedThemeVariant = _config.ThemeMode;
    }

    private static void OpenLogsFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(AppLog.LogFolder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppLog.LogFolder,
                UseShellExecute = true
            });
        }
        catch
        {
            // best-effort
        }
    }

    /// <summary>
    /// Opens a Gmail compose page (in the default web browser) pre-addressed to the developer, and
    /// reveals the logs folder so the user can attach the file. A web compose URL is used instead of a
    /// <c>mailto:</c> link because many systems have no mail client registered for mailto. (#25)
    /// </summary>
    private void EmailLogs()
    {
        const string to = "behzad.khosravifar@gmail.com";
        var subject = Uri.EscapeDataString($"Downloader.Desktop logs (v{AppVersion})");
        var body = Uri.EscapeDataString(
            "Hi,\n\nI'm sending my Downloader logs. Please attach the log file from:\n" +
            AppLog.CurrentLogFile +
            "\n\nIssue description:\n\n\n" +
            "--- Please keep the details below to help diagnose the problem ---\n" +
            BuildDiagnostics());

        // Gmail "compose" deep link — opens in whatever the user's default browser is.
        var gmail = $"https://mail.google.com/mail/?view=cm&fs=1&to={to}&su={subject}&body={body}";
        // mailto as a fallback for users who do have a desktop mail client.
        var mailto = $"mailto:{to}?subject={subject}&body={body}";

        try
        {
            System.IO.Directory.CreateDirectory(AppLog.LogFolder);
            var hasLog = System.IO.File.Exists(AppLog.CurrentLogFile);

            // Reveal the folder and copy the log path to the clipboard so the user can paste it
            // straight into the browser's "attach file" dialog — attachments can't be pre-filled
            // from a compose URL, so this is the smoothest manual path.
            if (hasLog)
            {
                OpenInShell(AppLog.LogFolder);
                _ = DialogHelper.CopyTextAsync(AppLog.CurrentLogFile);
            }

            // Prefer the browser-based compose; fall back to mailto if that fails.
            if (!OpenInShell(gmail))
                OpenInShell(mailto);

            NotificationService.Notify(
                "Attach your log file",
                hasLog
                    ? $"Logs folder opened. Attach \"{System.IO.Path.GetFileName(AppLog.CurrentLogFile)}\" to the email — its path is copied to your clipboard."
                    : "Turn on \"Write a log file\" first, reproduce the issue, then email the logs.",
                isError: false);
        }
        catch
        {
            // best-effort
        }
    }

    /// <summary>Builds a short, non-sensitive environment + settings report for the support email.</summary>
    private string BuildDiagnostics()
    {
        var s = _config.Settings;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"App version : {AppVersion}");
        sb.AppendLine($"OS          : {System.Runtime.InteropServices.RuntimeInformation.OSDescription} {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
        sb.AppendLine($"Process arch: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"Runtime     : {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Local time  : {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Downloads   : {_config.Downloads?.Count ?? 0}  |  Queues: {_config.Queues?.Count ?? 0}");
        sb.AppendLine("--- Settings ---");
        sb.AppendLine($"Connections={s.ChunkCount}, parallel={s.ParallelDownload}, parallelCount={s.ParallelCount}, maxConcurrent={s.MaxConcurrentDownloads}");
        sb.AppendLine($"maxSpeed={(s.MaximumBytesPerSecond <= 0 ? "unlimited" : s.MaximumBytesPerSecond / 1024 + " KB/s")}, memBuffer={(s.MaximumMemoryBufferBytes <= 0 ? "unlimited" : s.MaximumMemoryBufferBytes / (1024 * 1024) + " MB")}");
        sb.AppendLine($"httpTimeout={s.HttpClientTimeout}ms, connectTimeout={s.ConnectTimeout}ms, blockTimeout={s.BlockTimeout}ms, maxRetries={s.MaxTryAgainOnFailure}");
        sb.AppendLine($"autoResume={s.EnableAutoResumeDownload}, checkDisk={s.CheckDiskSizeBeforeDownload}, fileExists={s.FileExistPolicy}, redirects={s.AllowAutoRedirect}/{s.MaximumAutomaticRedirections}");
        return sb.ToString();
    }

    /// <summary>Opens a URL/path with the OS default handler. Returns false if it couldn't start.</summary>
    private static bool OpenInShell(string target)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task ExportLogs()
    {
        var source = AppLog.CurrentLogFile;
        if (!System.IO.File.Exists(source))
            return;

        var target = await DialogHelper.SaveFilePicker("Export log file", System.IO.Path.GetFileName(source));
        if (target == null)
            return;

        try
        {
            System.IO.File.Copy(source, target.LocalPath, overwrite: true);
        }
        catch
        {
            // best-effort
        }
    }
}
