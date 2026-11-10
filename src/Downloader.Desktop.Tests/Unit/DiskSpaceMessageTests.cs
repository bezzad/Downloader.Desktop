using System.IO;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>The engine's "not enough disk space" message names the needed bytes, the free bytes and the
/// mount it checked. The row must show it whole, so the user can see which disk is full.</summary>
public class DiskSpaceMessageTests
{
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Not_enough_space_message_is_kept_intact()
    {
        const string message = "Not enough disk space for '/home/u/Downloads/ai models/qwen3.6': " +
                               "needs 21718480960 more bytes, 1000 bytes free on '/home'.";

        string shown = DownloadManager.DescribeFailure(new IOException(message));

        Assert.Equal("Disk error: " + message, shown);
    }
}
