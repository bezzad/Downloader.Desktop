using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// The two pure decisions behind trying a download's other addresses: which address leads an attempt, and
/// whether a given failure is one that a different address could fix. The behaviour they serve is proven
/// end-to-end in <c>Integration/UrlFailoverTests</c>; these pin the edges that are awkward to provoke
/// through a real download.
/// </summary>
public class UrlAttemptTests
{
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_attempt_uses_exactly_one_address()
    {
        var urls = new[] { "a", "b", "c" };

        // One address per attempt. Handing the engine the whole list makes it spread the download's chunks
        // across addresses that are not equivalent — "the link the user clicked" and "where the browser
        // ended up" — so a dead one kept receiving chunks and downloads finished empty.
        Assert.Equal(new[] { "a" }, DownloadManager.OrderUrlsForAttempt(urls, 0));
        Assert.Equal(new[] { "b" }, DownloadManager.OrderUrlsForAttempt(urls, 1));
        Assert.Equal(new[] { "c" }, DownloadManager.OrderUrlsForAttempt(urls, 2));
    }

    /// <summary>A stale or nonsensical index must never leave a download with no address to request —
    /// it is session state, and the item it indexes can change underneath it.</summary>
    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    public void An_out_of_range_attempt_falls_back_to_the_first_address(int attempt)
    {
        var urls = new[] { "a", "b", "c" };
        Assert.Equal(new[] { "a" }, DownloadManager.OrderUrlsForAttempt(urls, attempt));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_single_address_download_always_uses_that_address()
    {
        var urls = new[] { "only" };
        Assert.Equal(urls, DownloadManager.OrderUrlsForAttempt(urls, 0));
        Assert.Equal(urls, DownloadManager.OrderUrlsForAttempt(urls, 1));
        Assert.Empty(DownloadManager.OrderUrlsForAttempt(Array.Empty<string>(), 0));
    }

    // ── Which failures another address could fix ─────────────────────────────────────────────────────

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public void A_server_that_refused_or_lost_the_address_is_worth_another_one(HttpStatusCode status)
    {
        Assert.True(DownloadManager.CanRetryWithAnotherUrl(Http(status)));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_request_that_never_completed_is_worth_another_address()
    {
        // No status at all: connection refused, DNS failure, reset. Another host may well answer.
        Assert.True(DownloadManager.CanRetryWithAnotherUrl(new HttpRequestException("connection refused")));
        Assert.True(DownloadManager.CanRetryWithAnotherUrl(new SocketException(111)));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_failure_that_says_nothing_about_the_address_is_not_worth_another_one()
    {
        // The user stopped it; the disk is full or unwritable; the transfer stalled. Repeating any of
        // these against a second address only makes the same failure take longer.
        Assert.False(DownloadManager.CanRetryWithAnotherUrl(new OperationCanceledException()));
        Assert.False(DownloadManager.CanRetryWithAnotherUrl(new TaskCanceledException()));
        Assert.False(DownloadManager.CanRetryWithAnotherUrl(new IOException("disk full")));
        Assert.False(DownloadManager.CanRetryWithAnotherUrl(new UnauthorizedAccessException()));
        Assert.False(DownloadManager.CanRetryWithAnotherUrl(null));
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void A_server_side_error_is_left_to_the_engines_own_retries(HttpStatusCode status)
    {
        // 5xx is the same server having a bad moment on the same file — the engine already retries it,
        // and switching address would abandon a resumable download for no reason.
        Assert.False(DownloadManager.CanRetryWithAnotherUrl(Http(status)));
    }

    /// <summary>The engine wraps a chunk's failure before it reaches the completion event, so the status
    /// is routinely buried a few levels down.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_status_is_found_through_the_wrapping_the_engine_adds()
    {
        var buried = new AggregateException("chunk failed",
            new Exception("outer", Http(HttpStatusCode.Forbidden)));

        Assert.True(DownloadManager.CanRetryWithAnotherUrl(buried));
    }

    // ── Telling "too many connections" apart from "this address is gone" ─────────────────────────────

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_forbidden_response_while_several_connections_were_open_looks_like_concurrency()
    {
        // The reporter's measurement: one mirror serves the file over 1-3 connections and answers 403
        // from the 4th on. Ours applied the configured maximum to every download and then called the
        // result an expired link.
        Assert.True(DownloadManager.LooksLikeConcurrencyRefusal(Http(HttpStatusCode.Forbidden), 4));
        Assert.True(DownloadManager.LooksLikeConcurrencyRefusal(Http(HttpStatusCode.Forbidden), 2));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_forbidden_response_to_a_lone_request_is_the_servers_real_answer()
    {
        // Nothing to back off from — retrying with "fewer" connections would repeat the same request.
        Assert.False(DownloadManager.LooksLikeConcurrencyRefusal(Http(HttpStatusCode.Forbidden), 1));
        Assert.False(DownloadManager.LooksLikeConcurrencyRefusal(Http(HttpStatusCode.Forbidden), 0));
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public void Only_a_forbidden_response_is_read_as_a_concurrency_refusal(HttpStatusCode status)
    {
        // These are about the ADDRESS (or the server's own trouble); no number of connections changes them.
        Assert.False(DownloadManager.LooksLikeConcurrencyRefusal(Http(status), 8));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_missing_error_is_never_a_concurrency_refusal()
    {
        Assert.False(DownloadManager.LooksLikeConcurrencyRefusal(null, 8));
        Assert.False(DownloadManager.LooksLikeConcurrencyRefusal(new IOException("disk full"), 8));
    }

    // ── A "finished" download that produced nothing ──────────────────────────────────────────────────

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_missing_or_empty_file_means_the_download_produced_nothing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dldesktop-empty-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var missing = Path.Combine(dir, "never-written.bin");
            Assert.True(DownloadManager.LooksEmptyAfterCompletion(missing));

            var empty = Path.Combine(dir, "empty.bin");
            File.WriteAllBytes(empty, Array.Empty<byte>());
            Assert.True(DownloadManager.LooksEmptyAfterCompletion(empty));

            var real = Path.Combine(dir, "real.bin");
            File.WriteAllBytes(real, new byte[] { 1 });
            Assert.False(DownloadManager.LooksEmptyAfterCompletion(real));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_unknown_path_is_never_judged_empty()
    {
        // The engine did not say where it wrote. Guessing "empty" there would fail healthy downloads.
        Assert.False(DownloadManager.LooksEmptyAfterCompletion(null));
        Assert.False(DownloadManager.LooksEmptyAfterCompletion(""));
        Assert.False(DownloadManager.LooksEmptyAfterCompletion("   "));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_download_that_produced_nothing_is_worth_another_address()
    {
        // It travels the failure path so the next address is tried, but it is NOT an expired link and must
        // not be described as one.
        var empty = new EmptyDownloadException("finished with no file");
        Assert.True(DownloadManager.CanRetryWithAnotherUrl(empty));
        Assert.False(DownloadManager.LooksLikeExpiredLinkError(empty));
        // Over SEVERAL connections, finishing with nothing is also what a server refusing ranged requests
        // looks like — every chunk is refused and no single status survives to the completion.
        Assert.True(DownloadManager.LooksLikeConcurrencyRefusal(empty, 8));
        // Over one connection it is not about concurrency, and backing off further would be meaningless.
        Assert.False(DownloadManager.LooksLikeConcurrencyRefusal(empty, 1));
    }

    private static HttpRequestException Http(HttpStatusCode status) =>
        new("server said no", inner: null, statusCode: status);
}
