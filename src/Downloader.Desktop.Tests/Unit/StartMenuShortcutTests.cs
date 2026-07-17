using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>Guards the Windows Start-menu shortcut script (winget/portable installs create no entry —
/// "installed successfully but I can't find it anywhere").</summary>
public class StartMenuShortcutTests
{
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Shortcut_script_targets_the_exe_and_saves_the_lnk()
    {
        var script = StartMenuShortcut.BuildShortcutScript(
            lnkPath: @"C:\Users\u\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Downloader.lnk",
            exePath: @"C:\Apps\Downloader\Downloader.exe");

        Assert.Contains(@"CreateShortcut('C:\Users\u\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Downloader.lnk')", script);
        Assert.Contains(@"$s.TargetPath='C:\Apps\Downloader\Downloader.exe'", script);
        Assert.Contains(@"$s.WorkingDirectory='C:\Apps\Downloader'", script);
        Assert.Contains("$s.Save()", script);
    }
}
