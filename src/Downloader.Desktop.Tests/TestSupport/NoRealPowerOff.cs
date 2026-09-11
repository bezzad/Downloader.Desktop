using System.Runtime.CompilerServices;
using Downloader.Desktop.Services;

namespace Downloader.Desktop.Tests;

/// <summary>
/// Stops the suite from POWERING THE MACHINE OFF. It really did, repeatedly, on the author's box on
/// 2026-09-09: the run started and the computer shut down about a minute later.
///
/// How: the shutdown countdown is a <c>DispatcherTimer</c>, and <c>ShutdownService.Cancel()</c> used to
/// close the dialog without stopping it. Under <c>AvaloniaTestIsolationLevel.PerAssembly</c> the
/// dispatcher lives for the whole run (before that, each test got a fresh one and the leaked timer died
/// with it), so a countdown armed by one test kept ticking, reached zero minutes later — by which time
/// the per-test <c>PowerOffOverride</c>/<c>RunOverride</c> stubs had been reset in their <c>finally</c>
/// blocks — and ran the real <c>systemctl poweroff</c>. The <c>Close()</c> fix removes the leak; this
/// removes the ability to do damage if anything else ever reaches that code unstubbed.
///
/// It blocks only the REAL process start: a test that installs <see cref="ShellLauncher.RunOverride"/>
/// to assert which command a platform would issue still sees exactly what it asserted.
///
/// A module initializer runs once before any test, which is what makes this cover the whole assembly
/// rather than the few classes that name the service. Same pattern as <see cref="NoRealNotifications"/>.
/// </summary>
internal static class NoRealPowerOff
{
    [ModuleInitializer]
    internal static void Install() => ShellLauncher.RealProcessStartBlocked = true;
}
