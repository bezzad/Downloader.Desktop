using System.Runtime.CompilerServices;
using Downloader.Desktop.Services;

namespace Downloader.Desktop.Tests;

/// <summary>
/// Stops the whole suite from posting REAL operating-system notifications.
///
/// The app announces downloads through the OS, and plenty of tests drive paths that do so (a download
/// completing, a plugin installing). On Linux that call goes through <c>ShellLauncher</c>, which tests
/// already stub — but the macOS and Windows branches call the shell directly, so those runners were
/// actually being asked to show notifications from a machine with no interactive session, and
/// <c>Shell_NotifyIconW</c> waits on a shell that is not there to answer.
///
/// A module initializer runs once, before any test, which is what makes this cover the whole assembly
/// rather than the handful of test classes that call the service by name.
/// </summary>
internal static class NoRealNotifications
{
    [ModuleInitializer]
    internal static void Install() => NotificationService.NativeOverride = (_, _, _) => { };
}
