using System;
using Avalonia.Threading;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;

namespace Downloader.Desktop.Services;

/// <summary>
/// Powers the computer off a short, cancelable delay after all downloads finish (opt-in via
/// <c>DownloadSettings.ShutdownOnCompletion</c>).
///
/// The user MUST be able to stop it, even if the app was minimized to the system tray — so this shows
/// a Topmost countdown dialog with a clear Cancel button (a standalone window appears regardless of
/// whether the main window is hidden) AND fires a native OS notification as a heads-up. The OS
/// power-off call is best-effort/platform-specific and not unit-tested (like the updater's self-swap);
/// the testable part is the manager deciding WHEN to arm this.
/// </summary>
public static class ShutdownService
{
    private const int DefaultCountdownSeconds = 30;

    /// <summary>
    /// Test seam: how long the countdown runs. A test that has to prove the countdown was really STOPPED
    /// (and not merely hidden) has to let it run to zero, and 30 seconds of pumping per test is not
    /// something a suite can carry.
    /// </summary>
    internal static int CountdownSeconds { get; set; } = DefaultCountdownSeconds;

    private static ShutdownView _dialog;

    /// <summary>True while a shutdown countdown dialog is showing.</summary>
    public static bool IsScheduled => _dialog != null;

    /// <summary>Test/seam hook: when set, called instead of the real OS power-off.</summary>
    public static Action PowerOffOverride { get; set; }

    /// <summary>
    /// Starts the cancelable shutdown countdown. No-op if one is already running. <paramref name="notify"/>
    /// also fires a native OS notification (mirrors the "notify on shutdown" setting); the dialog is shown
    /// regardless, since it's the safety mechanism that lets the user cancel.
    /// </summary>
    public static void Schedule(bool notify)
    {
        void Show()
        {
            if (IsScheduled)
                return;

            // Native (system) heads-up so a user with the app in the tray still gets an OS alert.
            if (notify)
                NotificationService.Notify(
                    Localizer.Instance["Shutdown_Notify_Title"],
                    string.Format(Localizer.Instance["Shutdown_Notify_Msg"], CountdownSeconds),
                    isError: false);

            var vm = new ShutdownViewModel(CountdownSeconds, onElapsed: PowerOff, onCancel: OnCanceled);
            _dialog = new ShutdownView { DataContext = vm };
            vm.CloseRequested += Close;
            _dialog.Show();           // standalone Topmost window — visible even if the main window is hidden
            _dialog.Activate();
        }

        if (Dispatcher.UIThread.CheckAccess())
            Show();
        else
            Dispatcher.UIThread.Post(Show);
    }

    /// <summary>Cancels a pending shutdown (e.g. from a tray action). Safe from any thread.</summary>
    public static void Cancel()
    {
        void Do()
        {
            if (!IsScheduled)
                return;
            Close();
            OnCanceled();
        }

        if (Dispatcher.UIThread.CheckAccess())
            Do();
        else
            Dispatcher.UIThread.Post(Do);
    }

    private static void OnCanceled() =>
        NotificationService.Notify(
            Localizer.Instance["Shutdown_Canceled_Title"],
            Localizer.Instance["Shutdown_Canceled_Msg"],
            isError: false);

    private static void Close()
    {
        var dlg = _dialog;
        _dialog = null;

        // Stop the countdown BEFORE closing the window. The countdown is a DispatcherTimer, which lives
        // on the dispatcher and not on the window, so closing the dialog on its own leaves it ticking —
        // and it powers the machine off when it reaches zero. That made Cancel() (the tray's "cancel
        // shutdown", and the test suite between tests) close the dialog and shut the computer down
        // anyway 30 seconds later. The dialog's own Cancel button was safe: it goes through the view
        // model, which does stop the timer.
        (dlg?.DataContext as ShutdownViewModel)?.StopCountdown();

        try { dlg?.Close(); } catch { /* already closed */ }
    }

    /// <summary>
    /// Internal rather than private so a test can exercise the platform dispatch below WITHOUT
    /// setting <see cref="PowerOffOverride"/> — that override short-circuits the very code that
    /// decides which command each OS gets, and a wrong command there means the machine simply never
    /// shuts down, with nothing reported to anyone.
    /// </summary>
    internal static void PowerOff()
    {
        Close();

        if (PowerOffOverride != null)
        {
            PowerOffOverride();
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
                Run("shutdown", "/s", "/t", "0");
            else if (OperatingSystem.IsMacOS())
                Run("osascript", "-e", "tell application \"System Events\" to shut down");
            else // Linux / *nix
                Run("systemctl", "poweroff");
        }
        catch
        {
            // best-effort — nothing sensible to do if the OS refuses the power-off.
        }
    }

    private static void Run(string file, params string[] args) => ShellLauncher.Run(file, args);
}
