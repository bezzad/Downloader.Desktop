using Downloader.Desktop.Models;
using System;
using System.Collections.Generic;

namespace Downloader.Desktop.Services;

/// <summary>
/// The app's memory of how many simultaneous connections each host accepts (issue #14).
/// <para>
/// A download refused for concurrency steps down until the server serves it, and each step costs a refused
/// request and the partial file it had gathered. Recording where it settled means the next download from
/// that host starts at a count it is known to accept and pays that cost once per host rather than once per
/// download. Everything here is pure over the dictionary, so every rule is testable without a download.
/// </para>
/// </summary>
public static class ServerLimits
{
    /// <summary>How long a learned limit is trusted before the ceiling is attempted again. A limit that
    /// never expired would punish a host for ever over one bad minute on a CDN. Settable so tests don't
    /// have to wait a week.</summary>
    public static TimeSpan RetestAfter { get; set; } = TimeSpan.FromDays(7);

    /// <summary>The key a limit is stored under: the address's host, lower-cased. Null for anything that
    /// isn't an absolute URL — an unkeyable address simply has no memory.</summary>
    public static string HostOf(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? uri.Host.ToLowerInvariant()
            : null;
    }

    /// <summary>How many connections a download from this host should START with.
    /// <para>
    /// The configured maximum is a ceiling and always wins: a remembered 8 must never override a user who
    /// has since chosen 2. An entry that is missing, nonsensical (a count of zero or less) or older than
    /// <see cref="RetestAfter"/> yields the ceiling, so a stale lesson is re-tested rather than obeyed.
    /// </para></summary>
    public static int ChooseStartingCount(
        IDictionary<string, ServerConnectionLimit> memory, string host, int ceiling, DateTime nowUtc)
    {
        ceiling = Math.Max(1, ceiling);
        if (memory == null || string.IsNullOrEmpty(host) || !memory.TryGetValue(host, out var entry) || entry == null)
            return ceiling;
        if (entry.Connections <= 0)
            return ceiling;
        if (nowUtc - entry.LearnedUtc >= RetestAfter)
            return ceiling;
        return Math.Min(ceiling, entry.Connections);
    }

    /// <summary>Record where a download from this host settled, or forget the host when it served the full
    /// ceiling — a host that no longer refuses must be released, or it would be held to one bad day.</summary>
    public static void Record(
        IDictionary<string, ServerConnectionLimit> memory, string host, int accepted, int ceiling, DateTime nowUtc)
    {
        if (memory == null || string.IsNullOrEmpty(host) || accepted <= 0)
            return;
        if (accepted >= ceiling)
        {
            memory.Remove(host);
            return;
        }
        memory[host] = new ServerConnectionLimit { Connections = accepted, LearnedUtc = nowUtc };
    }
}
