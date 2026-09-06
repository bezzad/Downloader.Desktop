using System;

namespace Downloader.Desktop.Models;

/// <summary>
/// What one host was measured to accept: the number of simultaneous connections it served a download over,
/// and when that was learned. Persisted with <see cref="Config"/> so the refusal is not rediscovered — and
/// a partial file thrown away — on every download from that host (issue #14).
/// <para>
/// A hint, never a verdict: the entry expires (see <see cref="Services.ServerLimits.RetestAfter"/>) so a
/// host that was strict once is not downloaded at a reduced count for ever, and the user's configured
/// maximum always wins over a remembered number that is higher.
/// </para>
/// </summary>
public class ServerConnectionLimit
{
    /// <summary>How many simultaneous connections the host actually served.</summary>
    public int Connections { get; set; }

    /// <summary>When that was measured (UTC), so the entry can be re-tested rather than trusted forever.</summary>
    public DateTime LearnedUtc { get; set; }
}
