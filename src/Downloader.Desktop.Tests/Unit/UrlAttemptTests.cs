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
    public void The_chosen_address_leads_and_the_others_keep_their_order()
    {
        var urls = new[] { "a", "b", "c" };

        Assert.Equal(new[] { "a", "b", "c" }, DownloadManager.OrderUrlsForAttempt(urls, 0));
        Assert.Equal(new[] { "b", "a", "c" }, DownloadManager.OrderUrlsForAttempt(urls, 1));
        Assert.Equal(new[] { "c", "a", "b" }, DownloadManager.OrderUrlsForAttempt(urls, 2));
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
        Assert.Equal(urls, DownloadManager.OrderUrlsForAttempt(urls, attempt));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_single_address_is_returned_untouched()
    {
        var urls = new[] { "only" };
        Assert.Same(urls, DownloadManager.OrderUrlsForAttempt(urls, 0));
        Assert.Same(urls, DownloadManager.OrderUrlsForAttempt(urls, 1));
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

    private static HttpRequestException Http(HttpStatusCode status) =>
        new("server said no", inner: null, statusCode: status);
}
