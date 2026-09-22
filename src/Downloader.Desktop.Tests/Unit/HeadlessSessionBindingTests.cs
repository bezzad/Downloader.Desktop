using System;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Threading;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// Guards <see cref="HeadlessSessionFirst"/> — the fix for the recurring CI "test host hang".
///
/// <para>This is deliberately a plain <c>[Fact]</c>, not an <c>[AvaloniaFact]</c>: the whole point is to
/// stand where the damage was done, on the xunit thread, outside the session.</para>
/// </summary>
public class HeadlessSessionBindingTests
{
    /// <summary>
    /// Touching <c>Dispatcher.UIThread</c> from the xunit thread must not be able to break the shared
    /// headless session.
    ///
    /// <para>Before the fix this did not fail — it HUNG, for ever, and so did every test after it: the
    /// session's dispatch loop faulted on <c>Dispatcher.VerifyAccess()</c> and each later test awaited a
    /// completion source nothing would set. Measured at the time: this test killed at 240 s, versus
    /// 173 ms for the identical control that did not touch the dispatcher first.</para>
    ///
    /// <para>Hence the bounded wait. A regression here must be a red test with a readable reason, not
    /// another silent stall that takes the whole run with it and blames some innocent test.</para>
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Touching_the_dispatcher_outside_the_session_cannot_break_it()
    {
        // The touch that used to poison the binding: Dispatcher.UIThread is a process-global singleton
        // that binds to whichever thread reaches it first, and plenty of ordinary production code
        // (DownloadManager.OnUi, the UI pump, the tray and notch services) reaches it from plain tests.
        _ = Dispatcher.UIThread;

        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(TestAppBuilder).Assembly);

        var dispatched = session.Dispatch(() => Task.FromResult(true),
            TestContext.Current.CancellationToken);
        var finished = await Task.WhenAny(dispatched,
            Task.Delay(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));

        // The only invariant worth asserting is that it ANSWERS. Where the work runs is Avalonia's
        // business and asserting a thread identity here just pins an implementation detail.
        Assert.True(finished == dispatched,
            "The headless session did not answer within 15s — its dispatch loop is faulted, which is the " +
            "CI hang. Has HeadlessSessionFirst stopped running, or stopped dispatching once at startup?");
        Assert.True(await dispatched);
    }
}
