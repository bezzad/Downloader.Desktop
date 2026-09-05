using System.Collections.Generic;
using Downloader.Desktop.Plugins.GitHub;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins;

/// <summary>
/// What a github.com link means, decided offline. Every case here was probed against the live plugin and
/// the real API first: the shipped resolver claimed ANY github.com/&lt;owner&gt;/&lt;repo&gt;/… path and
/// always asked for `releases/latest`, so a link to v2.9.0's asset, an issue and a README all downloaded
/// v2.10.0's application tarball.
/// </summary>
public class GitHubLinkTests
{
    private const string Repo = "https://github.com/bezzad/Downloader.Desktop";

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(Repo)]                     // the repository itself
    [InlineData(Repo + "/")]               // …with a trailing slash
    [InlineData(Repo + "/releases")]       // its releases list
    [InlineData(Repo + "/releases/latest")]
    public void A_repository_or_its_releases_list_means_the_newest_release(string url)
    {
        var link = GitHubLink.Parse(url);

        Assert.Equal(GitHubLinkKind.LatestRelease, link.Kind);
        Assert.Equal("bezzad", link.Owner);
        Assert.Equal("Downloader.Desktop", link.Repo);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    // The link the report was filed with: GitHub's own releases page anchors each entry like this, and the
    // anchor is the ONLY thing naming the release the user was looking at.
    [InlineData(Repo + "/releases#release-v2.10.0", "v2.10.0")]
    [InlineData(Repo + "/releases/tag/v2.9.0", "v2.9.0")]
    [InlineData(Repo + "/releases/tag/v1.0.0-beta.1", "v1.0.0-beta.1")]
    public void A_link_that_names_a_release_resolves_that_release(string url, string tag)
    {
        var link = GitHubLink.Parse(url);

        Assert.Equal(GitHubLinkKind.TaggedRelease, link.Kind);
        Assert.Equal(tag, link.Tag);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_anchor_that_names_no_release_still_means_the_newest()
    {
        // Guessing at an unrecognised fragment would be worse than ignoring it.
        Assert.Equal(GitHubLinkKind.LatestRelease, GitHubLink.Parse(Repo + "/releases#user-content-notes").Kind);
        Assert.Equal(GitHubLinkKind.LatestRelease, GitHubLink.Parse(Repo + "/releases#release-").Kind);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    // Already the file the user asked for: claiming it substituted a DIFFERENT release's asset.
    [InlineData(Repo + "/releases/download/v2.9.0/Downloader-linux-x64.tar.gz")]
    [InlineData(Repo + "/issues/14")]
    [InlineData(Repo + "/pull/12")]
    [InlineData(Repo + "/discussions/3")]
    [InlineData(Repo + "/wiki")]
    [InlineData(Repo + "/tree/main/src")]
    [InlineData(Repo + "/commit/abc1234")]
    [InlineData("https://github.com/bezzad")]              // an owner owns no downloadable thing
    [InlineData("https://gitlab.com/bezzad/Downloader.Desktop")]
    [InlineData("not a url")]
    [InlineData("ftp://github.com/bezzad/repo")]
    public void Anything_that_is_not_a_release_or_a_file_is_left_alone(string url)
        => Assert.Equal(GitHubLinkKind.NotClaimed, GitHubLink.Parse(url).Kind);

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(Repo + "/blob/main/README.md", "main", "README.md")]
    [InlineData(Repo + "/raw/develop/src/app.config", "develop", "src/app.config")]
    [InlineData(Repo + "/blob/main/docs/my%20file.txt", "main", "docs/my file.txt")]
    public void A_file_page_means_the_file(string url, string reference, string path)
    {
        var link = GitHubLink.Parse(url);

        Assert.Equal(GitHubLinkKind.RawFile, link.Kind);
        Assert.Equal(reference, link.Ref);
        Assert.Equal(path, link.Path);
    }

    // ── Which asset belongs to this machine ──────────────────────────────────────────────────────────

    private static readonly List<GitHubAsset> Assets = new()
    {
        Asset("downloader-extension-chrome.zip"),
        Asset("Downloader-linux-x64.tar.gz"),
        Asset("Downloader-osx-arm64.tar.gz"),
        Asset("Downloader-osx-x64.tar.gz"),
        Asset("Downloader-win-x64.zip"),
    };

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("linux", "x64", "Downloader-linux-x64.tar.gz")]
    [InlineData("windows", "x64", "Downloader-win-x64.zip")]
    // Both macOS builds fit the OS; the processor is what tells them apart.
    [InlineData("macos", "arm64", "Downloader-osx-arm64.tar.gz")]
    [InlineData("macos", "x64", "Downloader-osx-x64.tar.gz")]
    public void The_asset_for_this_machine_is_picked(string os, string arch, string expected)
        => Assert.Equal(expected, GitHubReleasesResolver.PickAsset(Assets, os, arch).Name);

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_darwin_build_is_not_mistaken_for_a_windows_one()
    {
        // "darwin" contains "win". A substring check handed macOS builds to Windows users.
        var assets = new List<GitHubAsset> { Asset("tool-darwin-arm64.tar.gz"), Asset("tool-win-x64.zip") };

        Assert.Equal("tool-win-x64.zip", GitHubReleasesResolver.PickAsset(assets, "windows", "x64").Name);
        Assert.Equal("tool-darwin-arm64.tar.gz", GitHubReleasesResolver.PickAsset(assets, "macos", "arm64").Name);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_release_that_names_no_platform_falls_back_to_its_first_asset()
    {
        // Exactly what the plugin has always done when nothing matches — unchanged on purpose.
        var assets = new List<GitHubAsset> { Asset("program.jar"), Asset("notes.txt") };

        Assert.Equal("program.jar", GitHubReleasesResolver.PickAsset(assets, "linux", "x64").Name);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void This_machine_is_described_in_terms_the_picker_understands()
    {
        // The running OS/arch must be values PickAsset actually matches on, or the default pick silently
        // becomes "the first asset" on some platform no test runs.
        Assert.Contains(GitHubReleasesResolver.CurrentOs(), new[] { "windows", "macos", "linux" });
        Assert.Contains(GitHubReleasesResolver.CurrentArchitecture(), new[] { "x64", "arm64" });
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_assets_size_is_shown_in_human_terms()
    {
        Assert.Equal("512 B", GitHubReleasesResolver.DescribeSize(512));
        Assert.Equal("1 KB", GitHubReleasesResolver.DescribeSize(1024));
        Assert.Equal("49.2 MB", GitHubReleasesResolver.DescribeSize(51_589_120));
    }

    private static GitHubAsset Asset(string name) =>
        new("1", name, "https://example.invalid/" + name, 1024);
}
