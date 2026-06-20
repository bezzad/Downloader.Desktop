using System;
using System.Windows.Input;
using Avalonia.Threading;
using Downloader.Desktop.Services;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// Backs the shutdown countdown dialog: a live "shutting down in N s" counter with a clear Cancel
/// (and "Shut down now") button. The dialog is the reliable, always-visible way for a user at the
/// keyboard to stop an automatic shutdown — including when the app was minimized to the tray.
/// </summary>
public class ShutdownViewModel : ViewModelBase
{
    private readonly Action _onElapsed;
    private readonly Action _onCancel;
    private DispatcherTimer _timer;
    private int _remaining;

    public ShutdownViewModel(int seconds, Action onElapsed, Action onCancel)
    {
        _remaining = seconds;
        _onElapsed = onElapsed;
        _onCancel = onCancel;

        CancelCommand = ReactiveCommand.Create(Cancel);
        ShutdownNowCommand = ReactiveCommand.Create(Now);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    /// <summary>Design-time ctor.</summary>
    public ShutdownViewModel() : this(30, null, null) { }

    public ICommand CancelCommand { get; }
    public ICommand ShutdownNowCommand { get; }

    private static string L(string key) => Localizer.Instance[key];

    public string Title => L("Shutdown_Title");
    public string Message => L("Shutdown_Message");
    public string CountdownText => string.Format(L("Shutdown_Countdown"), _remaining);
    public string CancelText => L("Shutdown_Cancel");
    public string ShutdownNowText => L("Shutdown_Now");

    /// <summary>Raised when the dialog should close (cancel, shut-down-now, or countdown elapsed).</summary>
    public event Action CloseRequested;

    private void OnTick(object sender, EventArgs e)
    {
        _remaining--;
        this.RaisePropertyChanged(nameof(CountdownText));
        if (_remaining <= 0)
        {
            Stop();
            CloseRequested?.Invoke();
            _onElapsed?.Invoke();
        }
    }

    private void Cancel()
    {
        Stop();
        CloseRequested?.Invoke();
        _onCancel?.Invoke();
    }

    private void Now()
    {
        Stop();
        CloseRequested?.Invoke();
        _onElapsed?.Invoke();
    }

    private void Stop()
    {
        if (_timer == null)
            return;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;
    }
}
