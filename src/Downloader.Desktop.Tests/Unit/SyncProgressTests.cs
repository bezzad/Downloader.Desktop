using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// Why <see cref="SyncProgress{T}"/> exists, pinned.
///
/// `ExtensionInstallServiceTests.Reporting_progress_reaches_one_hundred_percent` failed on macOS CI with
/// `Assert.Contains() … Collection: []` — no progress at all — while passing on every other leg. The cause
/// is below: `System.Progress&lt;T&gt;` POSTS each report to the `SynchronizationContext` captured when it was
/// constructed, and in a plain `[Fact]` that context is whatever an earlier `[AvaloniaFact]` left on the
/// thread, which nothing pumps. Whether the test passed came down to test ORDERING.
///
/// The first assertion here deliberately pins .NET's behaviour rather than ours: if a future runtime ever
/// delivered those reports anyway, the helper's reason for existing would have changed and we should find
/// out from a red test, not by guessing.
/// </summary>
public class SyncProgressTests
{
    /// <summary>A context that queues and never runs anything — what an unpumped dispatcher looks like.</summary>
    private sealed class NeverPumps : SynchronizationContext
    {
        public int Posted;
        public override void Post(SendOrPostCallback d, object state) => Interlocked.Increment(ref Posted);
    }

    // NOT async: awaiting anything while the never-pumping context is current would queue the
    // continuation into it and hang the test — which is the same mechanism, seen from the other side.
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Old_form_loses_reports_new_form_does_not()
    {
        var previous = SynchronizationContext.Current;
        var ctx = new NeverPumps();
        SynchronizationContext.SetSynchronizationContext(ctx);
        try
        {
            var lost = new List<double>();
            IProgress<double> old = new Progress<double>(p => lost.Add(p)); // captures ctx here
            var kept = new SyncProgress<double>();

            // Report from this very thread: Progress<T> posts to the captured context either way, it
            // never invokes the handler inline. That is precisely the trap.
            old.Report(1.0);
            kept.Report(1.0);
            Thread.Sleep(200); // generous: the posted report is never coming

            Assert.Empty(lost);                      // <-- the CI failure, reproduced
            Assert.True(ctx.Posted > 0);             // it was posted, just never delivered
            Assert.Contains(1.0, kept.Reports);      // <-- the fix
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
