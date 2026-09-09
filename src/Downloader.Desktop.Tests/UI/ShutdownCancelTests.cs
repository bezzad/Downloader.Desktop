using System;
using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// A cancelled shutdown must really be cancelled.
///
/// <para>It was not. <c>ShutdownService.Cancel()</c> closed the countdown window and cleared its own
/// field, but the countdown is a <c>DispatcherTimer</c> that lives on the dispatcher, not on the window —
/// so it kept ticking, reached zero, and powered the machine off ~30 seconds after the user cancelled.
/// The dialog's own Cancel button was safe (it goes through the view model, which stops the timer), so
/// the broken path was the one with no one watching: the tray's "cancel shutdown", and the test
/// suite.</para>
///
/// <para>This is what shut the author's machine down mid-test-run on 2026-09-09, and it is the leading
/// suspect for the Windows/macOS CI legs that "hang" in the test step with no failure and no dump: those
/// runners accept a power-off, a GitHub-hosted ubuntu runner does not — which is exactly the pattern the
/// hang has always had.</para>
/// </summary>
public class ShutdownCancelTests : IDisposable
{
    private readonly bool _notificationsWereEnabled = NotificationService.Enabled;

    public ShutdownCancelTests() => NotificationService.Enabled = false;

    public void Dispose()
    {
        NotificationService.Enabled = _notificationsWereEnabled;
        ShutdownService.Cancel();
        ShutdownService.PowerOffOverride = null;
    }

    /// <summary>
    /// The regression itself: arm, cancel, then let more than the countdown's worth of ticks run. Nothing
    /// may power off. One-second countdown so the test does not have to wait 30.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async System.Threading.Tasks.Task A_cancelled_countdown_never_reaches_the_power_off()
    {
        Localizer.Instance.Load("en");
        var poweredOff = 0;
        var vm = new ShutdownViewModel(1, onElapsed: () => poweredOff++, onCancel: () => { });

        // Dismissing the dialog is NOT the same as stopping the countdown — that was the bug.
        vm.StopCountdown();

        var deadline = Environment.TickCount64 + 3000;
        while (Environment.TickCount64 < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await System.Threading.Tasks.Task.Delay(50);
        }

        Assert.Equal(0, poweredOff);
    }

    /// <summary>
    /// The same thing through the service, which is the path the tray and the suite use: after
    /// <see cref="ShutdownService.Cancel"/> the countdown must be dead, not merely hidden.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async System.Threading.Tasks.Task Cancelling_through_the_service_stops_the_countdown_for_good()
    {
        Localizer.Instance.Load("en");
        var commands = new List<string>();
        // NO PowerOffOverride, deliberately: that override short-circuits the platform dispatch, so a
        // leaked countdown firing through it would be invisible. Watching one layer lower — at the
        // launcher — is what makes a real power-off attempt observable, and it is also the exact command
        // that reached the author's machine.
        ShutdownService.PowerOffOverride = null;
        ShellLauncher.RunOverride = (file, _) => { commands.Add(file); return true; };
        // The countdown has to be allowed to REACH ZERO for this to prove anything: a cancel that only
        // hides the dialog looks identical to a real one until the timer fires.
        ShutdownService.CountdownSeconds = 1;
        try
        {
            ShutdownService.Schedule(notify: false);
            Dispatcher.UIThread.RunJobs();
            Assert.True(ShutdownService.IsScheduled);

            ShutdownService.Cancel();
            Dispatcher.UIThread.RunJobs();
            Assert.False(ShutdownService.IsScheduled);

            var deadline = Environment.TickCount64 + 3000;
            while (Environment.TickCount64 < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                await System.Threading.Tasks.Task.Delay(50);
            }

            Assert.Empty(commands);
        }
        finally
        {
            ShellLauncher.RunOverride = null;
            ShutdownService.CountdownSeconds = 30;
        }
    }

    /// <summary>
    /// The backstop: with no override installed, a test run must not be able to start a real process.
    /// This is what keeps a future leak from taking a machine down instead of failing a test.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_suite_cannot_start_a_real_process()
    {
        Assert.True(ShellLauncher.RealProcessStartBlocked,
            "the test assembly's module initializer must block real process starts");

        ShutdownService.PowerOffOverride = null;
        ShellLauncher.RunOverride = null;

        // Reaching the real platform dispatch with nothing stubbed is refused, not executed.
        Assert.False(ShellLauncher.Run("systemctl", "poweroff"));
        Assert.False(ShellLauncher.RunChecked(TimeSpan.FromSeconds(1), "shutdown", "/s", "/t", "0"));
    }
}
