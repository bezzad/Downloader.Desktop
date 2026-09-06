using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Plugins.GitHub;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins;

/// <summary>
/// What the Add window is offered for a GitHub link, and what pressing Download then fetches.
/// <para>
/// The reported bug was invisible from the resolver's own point of view: it DID find
/// <c>Downloader-linux-x64.tar.gz</c>, but offered no choices, so the dialog showed no name, no size, and
/// — because variants come only from the claiming resolver — filled the "Choose what to download" slot
/// from the Website fallback plugin instead ("Offline copy (.zip)"). These run against a loopback stand-in
/// for the releases API, so the listing, the tag lookup and the failure wording cost nothing.
/// </para>
/// </summary>
public class GitHubVariantTests : IDisposable
{
    private readonly ReleaseApiStub _api = new();
    private readonly ILinkResolver _resolver = new GitHubReleasesResolver();
    private const string Repo = "https://github.com/bezzad/Downloader.Desktop";

    public GitHubVariantTests()
    {
        GitHubReleasesResolver.ClearCache();
        GitHubReleasesResolver.ApiBase = _api.Url;
    }

    public void Dispose()
    {
        GitHubReleasesResolver.ApiBase = "https://api.github.com"; // static: always restore
        GitHubReleasesResolver.ClearCache();
        _api.Dispose();
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Every_asset_of_the_release_is_offered_with_this_machines_pre_selected()
    {
        _api.Latest = Release("v2.10.0", "Downloader-win-x64.zip", "Downloader-linux-x64.tar.gz",
            "Downloader-osx-arm64.tar.gz");

        var variants = await _resolver.GetVariantsAsync(Repo + "/releases", null, CancellationToken.None);

        Assert.NotNull(variants);
        Assert.Equal(3, variants!.Count);
        Assert.Equal(
            new[] { "Downloader-win-x64.zip", "Downloader-linux-x64.tar.gz", "Downloader-osx-arm64.tar.gz" },
            variants.Select(v => v.Label));
        // Exactly one default, and it is the asset this machine would have downloaded anyway.
        var expected = GitHubReleasesResolver.PickAsset(
            _api.Latest!.Assets, GitHubReleasesResolver.CurrentOs(), GitHubReleasesResolver.CurrentArchitecture());
        Assert.Equal(expected.Name, Assert.Single(variants, v => v.IsDefault).Label);
        // Each choice carries its own address, so choosing one downloads that asset directly.
        Assert.All(variants, v => Assert.StartsWith("https://downloads.example/", v.SubstituteUrl));
        Assert.All(variants, v => Assert.NotNull(v.ExpectedSize));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task The_release_named_by_the_link_is_the_one_listed()
    {
        _api.Latest = Release("v2.10.0", "Downloader-linux-x64.tar.gz");
        _api.Tagged["v2.9.0"] = Release("v2.9.0", "older-linux-x64.tar.gz");

        var variants = await _resolver.GetVariantsAsync(Repo + "/releases/tag/v2.9.0", null, CancellationToken.None);

        Assert.Equal("older-linux-x64.tar.gz", Assert.Single(variants!).Label);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task The_anchor_on_the_releases_page_names_the_release_too()
    {
        // The exact shape of the reported link.
        _api.Latest = Release("v2.10.0", "newest-linux-x64.tar.gz");
        _api.Tagged["v2.9.0"] = Release("v2.9.0", "older-linux-x64.tar.gz");

        var plan = await _resolver.ResolveAsync(Repo + "/releases#release-v2.9.0", CancellationToken.None);

        Assert.Equal("older-linux-x64.tar.gz", plan.SuggestedFileName);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Downloading_without_choosing_gets_this_machines_asset()
    {
        _api.Latest = Release("v2.10.0", "Downloader-win-x64.zip", "Downloader-linux-x64.tar.gz",
            "Downloader-osx-x64.tar.gz", "Downloader-osx-arm64.tar.gz");

        var plan = await _resolver.ResolveAsync(Repo, CancellationToken.None);

        var expected = GitHubReleasesResolver.PickAsset(
            _api.Latest!.Assets, GitHubReleasesResolver.CurrentOs(), GitHubReleasesResolver.CurrentArchitecture());
        Assert.Equal(expected.Name, plan.SuggestedFileName);
        Assert.Equal(expected.Url, Assert.Single(plan.Parts).Url);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_chosen_asset_wins_and_an_unknown_choice_falls_back()
    {
        _api.Latest = Release("v2.10.0", "Downloader-linux-x64.tar.gz", "Downloader-win-x64.zip");
        var wanted = _api.Latest!.Assets.First(a => a.Name == "Downloader-win-x64.zip");

        var chosen = await _resolver.ResolveAsync(Repo, new ResolveOptions { VariantId = wanted.Id }, CancellationToken.None);
        Assert.Equal("Downloader-win-x64.zip", chosen.SuggestedFileName);

        // An id from an older release, or a hand-edited one, must not fail the download.
        var stale = await _resolver.ResolveAsync(Repo, new ResolveOptions { VariantId = "999999" }, CancellationToken.None);
        var expected = GitHubReleasesResolver.PickAsset(
            _api.Latest!.Assets, GitHubReleasesResolver.CurrentOs(), GitHubReleasesResolver.CurrentArchitecture());
        Assert.Equal(expected.Name, stale.SuggestedFileName);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_link_with_nothing_to_choose_between_offers_no_picker()
    {
        _api.Latest = Release("v2.10.0", "Downloader-linux-x64.tar.gz");

        // A file link is one file, and an issue is not ours at all — neither should populate the picker
        // (and an empty offer is what lets a fallback plugin's variant appear instead).
        Assert.Null(await _resolver.GetVariantsAsync(Repo + "/blob/main/README.md", null, CancellationToken.None));
        Assert.Null(await _resolver.GetVariantsAsync(Repo + "/issues/14", null, CancellationToken.None));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_file_link_downloads_the_file_itself()
    {
        var plan = await _resolver.ResolveAsync(Repo + "/blob/main/docs/readme.md", CancellationToken.None);

        Assert.Equal("readme.md", plan.SuggestedFileName);
        Assert.Equal("https://raw.githubusercontent.com/bezzad/Downloader.Desktop/main/docs/readme.md",
            Assert.Single(plan.Parts).Url);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_release_with_no_files_says_so()
    {
        _api.Latest = new StubRelease("v2.10.0", new List<GitHubAsset>());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _resolver.ResolveAsync(Repo, CancellationToken.None));

        Assert.Contains("no downloadable files", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_release_that_does_not_exist_names_the_tag()
    {
        _api.Latest = Release("v2.10.0", "Downloader-linux-x64.tar.gz");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _resolver.ResolveAsync(Repo + "/releases/tag/v9.9.9", CancellationToken.None));

        // A claiming resolver's message reaches the failed row verbatim, so it has to be a sentence.
        Assert.Contains("v9.9.9", error.Message);
        Assert.DoesNotContain("status code", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Listing_then_downloading_asks_the_api_once()
    {
        // The Add window lists, then resolves. Against an anonymous limit of 60 requests an hour, that
        // should not be two calls for one user action.
        _api.Latest = Release("v2.10.0", "Downloader-linux-x64.tar.gz");

        await _resolver.GetVariantsAsync(Repo, null, CancellationToken.None);
        await _resolver.ResolveAsync(Repo, CancellationToken.None);

        Assert.Equal(1, _api.Requests);
    }

    private static StubRelease Release(string tag, params string[] assetNames) =>
        new(tag, assetNames.Select((n, i) => new GitHubAsset(
            Id: (100 + i).ToString(), Name: n, Url: "https://downloads.example/" + n, Size: 1024 * (i + 1))).ToList());

    internal sealed record StubRelease(string Tag, IReadOnlyList<GitHubAsset> Assets);

    /// <summary>A loopback stand-in for the releases API: serves the two endpoints the resolver uses and
    /// counts what was asked of it.</summary>
    private sealed class ReleaseApiStub : IDisposable
    {
        private readonly HttpListener _listener = new();
        private int _requests;

        public string Url { get; }
        public StubRelease? Latest { get; set; }
        public Dictionary<string, StubRelease> Tagged { get; } = new();
        public int Requests => Volatile.Read(ref _requests);

        public ReleaseApiStub()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(Url + "/");
            _listener.Start();
            _ = Task.Run(LoopAsync);
        }

        private async Task LoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { break; }
                _ = Task.Run(() => Handle(ctx));
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            try
            {
                Interlocked.Increment(ref _requests);
                var path = ctx.Request.Url?.AbsolutePath ?? "";
                StubRelease? release = null;
                if (path.EndsWith("/releases/latest", StringComparison.Ordinal))
                    release = Latest;
                else if (path.Contains("/releases/tags/", StringComparison.Ordinal))
                    Tagged.TryGetValue(Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]), out release);

                if (release is null)
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }

                var assets = string.Join(",", release.Assets.Select(a =>
                    $$"""{"id":{{a.Id}},"name":"{{a.Name}}","browser_download_url":"{{a.Url}}","size":{{a.Size}}}"""));
                var body = System.Text.Encoding.UTF8.GetBytes(
                    $$"""{"tag_name":"{{release.Tag}}","assets":[{{assets}}]}""");
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = body.Length;
                ctx.Response.OutputStream.Write(body, 0, body.Length);
                ctx.Response.Close();
            }
            catch (HttpListenerException) { }
            catch (System.IO.IOException) { }
        }

        public void Dispose()
        {
            try { _listener.Stop(); _listener.Close(); } catch (ObjectDisposedException) { }
        }
    }
}
