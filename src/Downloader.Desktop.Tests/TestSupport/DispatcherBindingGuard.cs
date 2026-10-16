using System;
using System.Linq;
using System.Reflection;
using Avalonia.Threading;
using Xunit;
using Xunit.v3;

[assembly: Downloader.Desktop.Tests.DispatcherBindingGuard]

namespace Downloader.Desktop.Tests;

/// <summary>
/// Fails any plain <c>[Fact]</c>/<c>[Theory]</c> that brings Avalonia's UI dispatcher into existence.
/// <para><c>Dispatcher.UIThread</c> is a process-wide singleton bound to whichever thread touches it first.
/// When a plain test ran before the first <c>[AvaloniaFact]</c> and touched it (a <c>DownloadManager</c>, a
/// row view model), it bound to the xunit thread; the headless session then failed its own thread check
/// while starting, and the whole run hung or aborted with "THREAD-AFFINITY VIOLATION" — on whichever OS
/// happened to order that test first. This turns that into a named failure, and resets the singleton so
/// the session can still start and the rest of the run is unaffected.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class DispatcherBindingGuard : BeforeAfterTestAttribute
{
    private static readonly FieldInfo UiThread =
        typeof(Dispatcher).GetField("s_uiThread", BindingFlags.NonPublic | BindingFlags.Static);

    private static bool _existedBefore;

    /// <summary>Whether the singleton has been created (read without creating it).</summary>
    internal static bool DispatcherExists => UiThread?.GetValue(null) != null;

    private static bool IsAvaloniaTest(MethodInfo method) =>
        method.GetCustomAttributes().Any(a => a.GetType().Name.StartsWith("Avalonia", StringComparison.Ordinal));

    public override void Before(MethodInfo methodUnderTest, IXunitTest test) =>
        _existedBefore = DispatcherExists;

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (UiThread == null || _existedBefore || IsAvaloniaTest(methodUnderTest) || !DispatcherExists)
            return;

        UiThread.SetValue(null, null);
        Assert.Fail(
            $"{methodUnderTest.Name} is a plain test but created Avalonia's UI dispatcher on its own thread. " +
            "Make it [AvaloniaFact]/[AvaloniaTheory] — otherwise, when it runs first, the headless session " +
            "cannot start and the whole run hangs.");
    }
}
