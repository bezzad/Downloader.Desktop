using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using Downloader;
using Downloader.Desktop.Services;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// Backs the notch overlay ("dynamic island"): a live clock, the active (running + paused) downloads
/// and their aggregate speed/percent. Pure view over <see cref="IDownloadManager"/>.
/// </summary>
public class NotchViewModel : ViewModelBase, IDisposable
{
    /// <summary>How many active rows the expanded island lists before "and N more…".</summary>
    public const int MaxRows = 3;

    private readonly IDownloadManager _manager;
    private readonly DispatcherTimer _clock;
    private bool _isExpanded;
    private string _timeText = DateTime.Now.ToString("HH:mm");
    private string _totalSpeedText = "";
    private string _totalPercentText = "";
    private string _overflowText;

    public NotchViewModel(IDownloadManager manager)
    {
        _manager = manager;
        if (_manager != null)
            _manager.StatsChanged += OnStatsChanged;

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => TimeText = DateTime.Now.ToString("HH:mm");
        _clock.Start();
        OnStatsChanged();
    }

    /// <summary>On macOS the pill sits AT the physical notch, so the collapsed content must live on the
    /// WINGS beside the webcam housing — the center column is a hardware-sized gap. Elsewhere there is
    /// no cutout and the gap collapses.</summary>
    public static bool IsMac => OperatingSystem.IsMacOS();
    public static double NotchGapWidth => IsMac ? 185 : 6;

    /// <summary>The collapsed clock shows only when idle (a downloading pill shows the speed INSTEAD of
    /// the time — one stat at a time, author's spec) and never on macOS (the menu bar already has a
    /// clock, and the wing is too narrow for speed + time).</summary>
    public bool ShowCollapsedClock => !IsMac && !HasActivity;

    /// <summary>Running first, then paused (top <see cref="MaxRows"/>).</summary>
    public ObservableCollection<DownloadItemViewModel> RunningRows { get; } = new();

    public string TimeText
    {
        get => _timeText;
        private set => this.RaiseAndSetIfChanged(ref _timeText, value);
    }

    /// <summary>Aggregate ↓speed shown while anything is running ("" when idle).</summary>
    public string TotalSpeedText
    {
        get => _totalSpeedText;
        private set
        {
            this.RaiseAndSetIfChanged(ref _totalSpeedText, value);
            this.RaisePropertyChanged(nameof(HasActivity));
            this.RaisePropertyChanged(nameof(ShowCollapsedClock));
        }
    }

    public bool HasActivity => !string.IsNullOrEmpty(_totalSpeedText);

    /// <summary>Average completion of the active downloads, e.g. "62%" ("" when idle).</summary>
    public string TotalPercentText
    {
        get => _totalPercentText;
        private set
        {
            this.RaiseAndSetIfChanged(ref _totalPercentText, value);
            this.RaisePropertyChanged(nameof(HasPercent));
        }
    }

    public bool HasPercent => !string.IsNullOrEmpty(_totalPercentText);

    /// <summary>"and N more…" under the listed rows, or null when everything fits.</summary>
    public string OverflowText
    {
        get => _overflowText;
        private set
        {
            this.RaiseAndSetIfChanged(ref _overflowText, value);
            this.RaisePropertyChanged(nameof(HasOverflow));
        }
    }

    public bool HasOverflow => _overflowText != null;

    public bool HasRows => RunningRows.Count > 0;

    /// <summary>True while the island is hover-expanded (drives the content swap).</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
    }

    private void OnStatsChanged()
    {
        // Active = running + paused (the author wants paused items visible in the island too).
        var items = _manager?.Items;
        var active = items == null
            ? new System.Collections.Generic.List<DownloadItemViewModel>()
            : items.Where(i => i.Status is DownloadStatus.Running or DownloadStatus.Paused)
                   .OrderBy(i => i.Status == DownloadStatus.Running ? 0 : 1)
                   .ToList();

        // Rebuild in place only when membership changed (rows self-update their progress/speed).
        if (!active.Take(MaxRows).SequenceEqual(RunningRows))
        {
            RunningRows.Clear();
            foreach (var vm in active.Take(MaxRows))
                RunningRows.Add(vm);
            this.RaisePropertyChanged(nameof(HasRows));
        }

        OverflowText = active.Count > MaxRows
            ? string.Format(Localizer.Instance["Notch_More"], active.Count - MaxRows)
            : null;

        var running = active.Count(i => i.Status == DownloadStatus.Running);
        var speed = _manager?.TotalSpeed ?? 0;
        TotalSpeedText = running > 0 && speed > 0
            ? "↓ " + FormatSpeedCompact((long)speed)
            : running > 0 ? "↓" : "";

        TotalPercentText = active.Count > 0 ? $"{active.Average(i => i.Progress):0}%" : "";
    }

    /// <summary>Whole-number speed ("776 KB/s") — the notch wing is too narrow for decimals.</summary>
    internal static string FormatSpeedCompact(long bytesPerSec)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytesPerSec;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0} {units[unit]}/s";
    }

    public void Dispose()
    {
        _clock.Stop();
        if (_manager != null)
            _manager.StatsChanged -= OnStatsChanged;
    }
}
