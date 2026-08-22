using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>Guards the Windows Start-menu shortcut (winget/portable installs create no entry —
/// "installed successfully but I can't find it anywhere"). The .lnk itself is written in-process via
/// IShellLink (issue #4 — no shell spawn), so the only pure, platform-independent part left to test is
/// the "Start in" directory derivation; see <see cref="NoShellSpawnTests"/> for the no-shell guardrail.</summary>
public class StartMenuShortcutTests
{
    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(@"C:\Apps\Downloader\Downloader.exe", @"C:\Apps\Downloader")]
    [InlineData(@"C:\Downloader.exe", @"C:")]
    [InlineData("Downloader.exe", "Downloader.exe")] // no separator: fall back to the path itself
    public void Working_directory_is_the_exe_folder(string exe, string expected)
        => Assert.Equal(expected, StartMenuShortcut.ResolveWorkingDirectory(exe));
}
