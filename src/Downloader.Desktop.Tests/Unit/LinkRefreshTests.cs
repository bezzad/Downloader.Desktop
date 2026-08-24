using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>Pure logic behind refreshing an expired download link (issue #6).</summary>
public class LinkRefreshTests
{
    private static HttpRequestException Http(HttpStatusCode status) =>
        new("response status code does not indicate success", null, status);

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public void Expired_link_statuses_are_recognized(HttpStatusCode status) =>
        Assert.True(DownloadManager.LooksLikeExpiredLinkError(Http(status)));

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Expired_link_status_is_found_through_wrappers()
    {
        // The engine wraps a chunk's failure before it reaches the completion event.
        Assert.True(DownloadManager.LooksLikeExpiredLinkError(
            new AggregateException(Http(HttpStatusCode.Forbidden))));
        Assert.True(DownloadManager.LooksLikeExpiredLinkError(
            new InvalidOperationException("chunk 2 failed", Http(HttpStatusCode.Gone))));
        Assert.True(DownloadManager.LooksLikeExpiredLinkError(
            new AggregateException(new IOException("disk"), Http(HttpStatusCode.Unauthorized))));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Transient_failures_are_not_expired_links()
    {
        // These mean "try again with the same link", so they must never trigger a link refresh.
        Assert.False(DownloadManager.LooksLikeExpiredLinkError(Http(HttpStatusCode.InternalServerError)));
        Assert.False(DownloadManager.LooksLikeExpiredLinkError(Http(HttpStatusCode.ServiceUnavailable)));
        Assert.False(DownloadManager.LooksLikeExpiredLinkError(Http(HttpStatusCode.RequestTimeout)));
        Assert.False(DownloadManager.LooksLikeExpiredLinkError(new TaskCanceledException("timeout")));
        Assert.False(DownloadManager.LooksLikeExpiredLinkError(new SocketException(110)));
        Assert.False(DownloadManager.LooksLikeExpiredLinkError(new HttpRequestException("no status")));
        Assert.False(DownloadManager.LooksLikeExpiredLinkError(new IOException("disk full")));
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(1_000L, 1_000L, LinkRefreshCheck.Match)]      // same file → resume the partial
    [InlineData(1_000L, 999L, LinkRefreshCheck.Mismatch)]     // different file → partial would be lost
    [InlineData(1_000L, 0L, LinkRefreshCheck.Unknown)]        // server hides the size → nothing to compare
    [InlineData(0L, 1_000L, LinkRefreshCheck.Unknown)]        // we never knew the size → nothing to compare
    public void New_link_size_decides_whether_the_partial_survives(long known, long fresh, LinkRefreshCheck expected)
    {
        long? knownSize = known == 0 ? null : known;
        Assert.Equal(expected, DownloadDetailsViewModel.EvaluateNewLink(knownSize, fresh));
    }
}
