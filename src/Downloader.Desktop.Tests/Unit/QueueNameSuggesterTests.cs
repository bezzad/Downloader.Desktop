using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// The queue name offered for a batch of links. It comes from the part the links' file names share,
/// with the trailing numbering dropped (S01/E02 say nothing about what the group is). Links with no
/// file name (a site/page URL) give nothing, and the caller falls back to a plain "New queue".
/// </summary>
public class QueueNameSuggesterTests
{
    [Fact]
    public void A_series_is_named_after_what_its_episodes_share()
    {
        var name = QueueNameSuggester.Suggest(new[]
        {
            "https://host/files/The.X.Movie.S01.E02.mkv",
            "https://host/files/The.X.Movie.S01.E03.mkv",
            "https://host/files/The.X.Movie.S01.E04.mkv"
        });

        Assert.Equal("The.X.Movie", name);
    }

    [Fact]
    public void Other_separators_and_a_query_string_are_handled()
    {
        var name = QueueNameSuggester.Suggest(new[]
        {
            "https://host/a/Big%20Buck-01.mp4?token=abc",
            "https://host/b/Big%20Buck-02.mp4?token=def"
        });

        Assert.Equal("Big.Buck", name);
    }

    [Fact]
    public void Links_that_share_nothing_get_no_name()
    {
        Assert.Null(QueueNameSuggester.Suggest(new[]
        {
            "https://host/alpha.zip",
            "https://host/beta.zip"
        }));
    }

    [Fact]
    public void A_link_without_a_file_name_gives_nothing()
    {
        Assert.Null(QueueNameSuggester.Suggest(new[]
        {
            "https://www.youtube.com/watch?v=abc",
            "https://x.com/user/status/123"
        }));
    }

    [Fact]
    public void One_link_is_not_a_batch()
    {
        Assert.Null(QueueNameSuggester.Suggest(new[] { "https://host/The.X.Movie.S01.E02.mkv" }));
        Assert.Null(QueueNameSuggester.Suggest(null));
    }
}
