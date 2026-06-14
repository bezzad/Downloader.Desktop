using System;
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
    private DownloadSettings S => _config.Settings;

    public SettingViewModel(Config config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        SelectSavePathCommand = ReactiveCommand.CreateFromTask(SelectSavePath);
        SwitchThemeCommand = ReactiveCommand.Create(SwitchTheme);
        OpenLogsFolderCommand = ReactiveCommand.Create(OpenLogsFolder);
        ExportLogsCommand = ReactiveCommand.CreateFromTask(ExportLogs);
    }

    public ICommand SelectSavePathCommand { get; }
    public ICommand SwitchThemeCommand { get; }
    public ICommand OpenLogsFolderCommand { get; }
    public ICommand ExportLogsCommand { get; }

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

    // ---- Basic ----
    public string DefaultSavePath
    {
        get => S.DefaultSavePath;
        set { S.DefaultSavePath = value; this.RaisePropertyChanged(); }
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
        set { S.MaximumBytesPerSecond = value <= 0 ? 0 : value * 1024; this.RaisePropertyChanged(); }
    }

    public int MaxConcurrentDownloads
    {
        get => S.MaxConcurrentDownloads;
        set { S.MaxConcurrentDownloads = value; this.RaisePropertyChanged(); }
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
