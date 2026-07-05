using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using Downloader;
using Downloader.Desktop.Services;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// Backs the notch overlay ("dynamic island"): a live clock, the running downloads (top few) and their
/// aggregate speed. Pure view over <see cref="IDownloadManager"/> — no new download plumbing.
/// </summary>
public class NotchViewModel : ViewModelBase, IDisposable
{
    /// <summary>How many running rows the expanded island lists before "and N more…".</summary>
    public const int MaxRows = 3;

    private readonly IDownloadManager _manager;
    private readonly DispatcherTimer _clock;
    private bool _isExpanded;
    private string _timeText = DateTime.Now.ToString("HH:mm");
    private string _totalSpeedText = "";
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

    public ObservableCollection<DownloadItemViewModel> RunningRows { get; } = new();

    public string TimeText
    {
        get => _timeText;
        private set => this.RaiseAndSetIfChanged(ref _timeText, value);
    }

    /// <summary>Aggregate ↓speed shown in the pill while anything is running ("" when idle).</summary>
    public string TotalSpeedText
    {
        get => _totalSpeedText;
        private set
        {
            this.RaiseAndSetIfChanged(ref _totalSpeedText, value);
            this.RaisePropertyChanged(nameof(HasActivity));
        }
    }

    public bool HasActivity => !string.IsNullOrEmpty(_totalSpeedText);

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
        var running = _manager?.Items?.Where(i => i.Status == DownloadStatus.Running).ToList()
                      ?? new System.Collections.Generic.List<DownloadItemViewModel>();

        // Rebuild in place only when membership changed (rows self-update their progress/speed).
        if (!running.Take(MaxRows).SequenceEqual(RunningRows))
        {
            RunningRows.Clear();
            foreach (var vm in running.Take(MaxRows))
                RunningRows.Add(vm);
            this.RaisePropertyChanged(nameof(HasRows));
        }

        OverflowText = running.Count > MaxRows
            ? string.Format(Localizer.Instance["Notch_More"], running.Count - MaxRows)
            : null;

        var speed = _manager?.TotalSpeed ?? 0;
        TotalSpeedText = running.Count > 0 && speed > 0
            ? "↓ " + DownloadItemViewModel.FormatBytes((long)speed) + "/s"
            : running.Count > 0 ? "↓" : "";
    }

    public void Dispose()
    {
        _clock.Stop();
        if (_manager != null)
            _manager.StatsChanged -= OnStatsChanged;
    }
}
