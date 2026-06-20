using System;
using System.Diagnostics;
using Avalonia.Threading;

namespace Downloader.Desktop.Services;

/// <summary>
/// Powers the computer off a short, cancelable delay after all downloads finish (opt-in via
/// <c>DownloadSettings.ShutdownOnCompletion</c>). Shows an actionable in-app notice the user can
/// click to cancel — so someone still at the keyboard isn't shut down on them. The OS power-off
/// call itself is best-effort and platform-specific; it is intentionally not unit-tested (like the
/// updater's self-swap), the testable part is the manager deciding when to arm this.
/// </summary>
public static class ShutdownService
{
    private const int CountdownSeconds = 30;

    private static DispatcherTimer _timer;

    /// <summary>True while a shutdown countdown is running.</summary>
    public static bool IsScheduled => _timer is { IsEnabled: true };

    /// <summary>
    /// Test/seam hook: when set, called instead of the real OS power-off. Returns nothing.
    /// Lets tests assert the countdown elapses without actually shutting the machine down.
    /// </summary>
    public static Action PowerOffOverride { get; set; }

    /// <summary>
    /// Starts a single <see cref="CountdownSeconds"/>-second countdown to shut down. No-op if one is
    /// already running. <paramref name="notify"/> shows the cancelable notice (mirrors the user's
    /// "notify on shutdown" setting).
    /// </summary>
    public static void Schedule(bool notify)
    {
        if (IsScheduled)
            return;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(CountdownSeconds) };
        _timer.Tick += (_, _) =>
        {
            Stop();
            PowerOff();
        };
        _timer.Start();

        if (notify)
            NotificationService.ShowAction(
                "Shutting down in 30s",
                "All downloads finished. The computer will shut down in 30 seconds — click here to cancel.",
                Cancel);
    }

    /// <summary>Cancels a pending shutdown (called when the user clicks the notice).</summary>
    public static void Cancel()
    {
        if (!IsScheduled)
            return;
        Stop();
        NotificationService.Notify("Shutdown canceled", "The computer will stay on.", isError: false);
    }

    private static void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    private static void PowerOff()
    {
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

    private static void Run(string file, params string[] args)
    {
        var psi = new ProcessStartInfo { FileName = file, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        Process.Start(psi);
    }
}
