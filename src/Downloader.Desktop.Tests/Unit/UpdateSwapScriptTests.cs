using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// Guards the Windows update-swap script (#9 — "downloaded the update but the app couldn't restart or
/// replace itself"). The script runs in a windowless cmd, so it must never use `timeout /t` (dies with
/// "input redirection is not supported" without console stdin), and the extraction must RETRY until the
/// exe is replaceable — a stale tray-held instance or an AV scan keeping Downloader.exe locked used to
/// fail the single Expand-Archive silently and relaunch the OLD build.
/// </summary>
public class UpdateSwapScriptTests
{
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Windows_swap_script_waits_retries_and_relaunches()
    {
        var script = UpdateService.BuildWindowsScript(
            archive: @"C:\Temp\update.zip", appDir: @"C:\Apps\Downloader", exe: @"C:\Apps\Downloader\Downloader.exe", pid: 4242);

        // Waits for the launching process to exit before touching files.
        Assert.Contains("PID eq 4242", script);

        // Sleeps must be redirect-safe (`ping`), never `timeout /t` (fails without console stdin).
        Assert.Contains("ping -n 2 127.0.0.1", script);
        Assert.DoesNotContain("timeout /t", script);

        // The extraction retries while the exe is locked (stale tray instance / AV), instead of a
        // single silent attempt.
        Assert.Contains(":extract", script);
        Assert.Contains("Expand-Archive -Force", script);
        Assert.Contains("goto extract", script);

        // Cleans up the archive and relaunches the app.
        Assert.Contains(@"del ""C:\Temp\update.zip""", script);
        Assert.Contains(@"start """" ""C:\Apps\Downloader\Downloader.exe""", script);
    }
}
