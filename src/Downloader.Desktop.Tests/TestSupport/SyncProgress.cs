using System;
using System.Collections.Generic;

namespace Downloader.Desktop.Tests;

/// <summary>
/// Collects <see cref="IProgress{T}"/> reports SYNCHRONOUSLY, on whichever thread reported them.
///
/// Why this exists rather than <c>new Progress&lt;T&gt;(x =&gt; list.Add(x))</c>: `System.Progress&lt;T&gt;`
/// captures `SynchronizationContext.Current` when it is CONSTRUCTED and **posts** every report to it. In a
/// plain `[Fact]` that context is whatever an earlier `[AvaloniaFact]` happened to leave on the thread —
/// and nothing pumps it — so the reports are queued and never delivered. The assertion then sees an empty
/// list, which is a failure about test plumbing rather than about the code under test.
///
/// That is not hypothetical: `ExtensionInstallServiceTests.Reporting_progress_reaches_one_hundred_percent`
/// failed exactly this way on macOS CI (`Assert.Contains() … Collection: []`) while passing everywhere
/// else, because test ordering decided whether a context was installed.
///
/// The contract worth asserting is "the service calls Report, and the values are these" — which is what
/// this captures. Production code keeps using `Progress&lt;T&gt;`: there the posting is the point, because
/// it is what marshals a background report onto the UI thread.
///
/// Reports can arrive from several threads, so the list is guarded.
/// </summary>
public sealed class SyncProgress<T> : IProgress<T>
{
    private readonly List<T> _reports = new();
    private readonly object _gate = new();

    public void Report(T value)
    {
        lock (_gate)
            _reports.Add(value);
    }

    /// <summary>A snapshot of everything reported so far.</summary>
    public IReadOnlyList<T> Reports
    {
        get
        {
            lock (_gate)
                return _reports.ToArray();
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
                return _reports.Count;
        }
    }
}
