using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// "Open containing folder" — the regression a snap user hit, where clicking it did nothing at all.
///
/// <para>Two defects compounded. On Linux the reveal is a D-Bus call to <c>org.freedesktop.FileManager1</c>,
/// which AppArmor DENIES to a snap-confined app; but <c>dbus-send</c> without <c>--print-reply</c> never
/// waits for the reply and exits 0 anyway (verified on the reporter's own machine: exit 0 denied vs exit 1
/// with the flag). And <see cref="ShellLauncher.Run"/> reported whether the process STARTED, not whether it
/// worked. So the app concluded it had revealed the file, skipped the fallback, and did nothing — silently,
/// with no log line to diagnose from.</para>
///
/// <para>The tests below are about the FALLBACK, because that is the part that was missing.</para>
/// </summary>
public class RevealInFolderTests : IDisposable
{
    private readonly List<(string File, string[] Args)> _ran = new();
    private readonly List<string> _opened = new();

    public void Dispose()
    {
        // Process-wide seams: leaking one silently changes a LATER test, not this one.
        ShellLauncher.RunOverride = null;
        ShellLauncher.OpenOverride = null;
    }

    private void Arrange(bool revealSucceeds, bool openSucceeds = true)
    {
        ShellLauncher.RunOverride = (file, args) => { _ran.Add((file, args)); return revealSucceeds; };
        ShellLauncher.OpenOverride = target => { _opened.Add(target); return openSucceeds; };
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_reveal_that_fails_falls_back_to_opening_the_folder()
    {
        Arrange(revealSucceeds: false);

        ShellLauncher.RevealInFolder(Path.Combine("/home", "u", "Downloads", "file.zip"));

        // THE regression: before the fix this list was empty, and the user saw nothing happen.
        Assert.Contains(Path.Combine("/home", "u", "Downloads"), _opened);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_reveal_that_works_does_not_also_open_the_folder()
    {
        Arrange(revealSucceeds: true);

        ShellLauncher.RevealInFolder("/home/u/Downloads/file.zip");

        // Two windows for one click would be its own bug.
        Assert.Empty(_opened);
        Assert.Single(_ran);
    }

    /// <summary>
    /// The flag that makes the failure detectable at all. Without it dbus-send exits 0 even when the call
    /// was denied, so no amount of exit-code checking downstream can help.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_linux_reveal_asks_dbus_send_to_wait_for_a_reply()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            return; // the D-Bus reveal is the Linux path

        Arrange(revealSucceeds: true);

        ShellLauncher.RevealInFolder("/home/u/Downloads/file.zip");

        var (file, args) = Assert.Single(_ran);
        Assert.Equal("dbus-send", file);
        Assert.Contains("--print-reply", args);
        Assert.Contains("array:string:file:///home/u/Downloads/file.zip", args);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Nothing_is_launched_for_an_empty_path()
    {
        Arrange(revealSucceeds: false);

        ShellLauncher.RevealInFolder(null);
        ShellLauncher.RevealInFolder("   ");

        Assert.Empty(_ran);
        Assert.Empty(_opened);
    }

    // ---- OpenFolder's own fallback chain ----

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Opening_a_folder_uses_the_default_handler_when_it_works()
    {
        Arrange(revealSucceeds: false, openSucceeds: true);

        Assert.True(ShellLauncher.OpenFolder("/home/u/Downloads"));

        Assert.Equal(new[] { "/home/u/Downloads" }, _opened);
        Assert.Empty(_ran);   // no need for the explicit fallback
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Opening_a_folder_falls_back_to_gio_when_no_handler_is_registered()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            return; // gio is the Linux/BSD fallback

        Arrange(revealSucceeds: true, openSucceeds: false);

        Assert.True(ShellLauncher.OpenFolder("/home/u/Downloads"));

        var (file, args) = Assert.Single(_ran);
        Assert.Equal("gio", file);
        Assert.Equal(new[] { "open", "/home/u/Downloads" }, args);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Opening_a_folder_reports_failure_when_nothing_works()
    {
        Arrange(revealSucceeds: false, openSucceeds: false);

        Assert.False(ShellLauncher.OpenFolder("/home/u/Downloads"));
        Assert.False(ShellLauncher.OpenFolder(null));
    }

    /// <summary>
    /// A command that starts and then fails must read as failure. This is the distinction
    /// <see cref="ShellLauncher.Run"/> could not make, and the reason the fallback never ran.
    /// </summary>
    [Fact(Timeout = TestTimeouts.SlowMs)]
    public void RunChecked_reports_the_exit_code_where_Run_reports_only_that_it_started()
    {
        if (OperatingSystem.IsWindows())
            return; // /bin/false is the portable "starts fine, fails" command on unix

        Assert.True(ShellLauncher.Run("/bin/false"));                                  // it started
        Assert.False(ShellLauncher.RunChecked(TimeSpan.FromSeconds(5), "/bin/false")); // …and failed
        Assert.True(ShellLauncher.RunChecked(TimeSpan.FromSeconds(5), "/bin/true"));
    }

    [Fact(Timeout = TestTimeouts.SlowMs)]
    public void RunChecked_treats_a_command_that_hangs_as_failed()
    {
        if (OperatingSystem.IsWindows())
            return;

        // Everything routed through RunChecked is a short-lived helper, so "still running" means stuck —
        // and a reveal that hangs must not block the fallback forever.
        Assert.False(ShellLauncher.RunChecked(TimeSpan.FromMilliseconds(300), "/bin/sleep", "30"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void RunChecked_reports_failure_for_a_command_that_does_not_exist()
        => Assert.False(ShellLauncher.RunChecked(TimeSpan.FromSeconds(5), "definitely-not-a-real-command-xyz"));
}
