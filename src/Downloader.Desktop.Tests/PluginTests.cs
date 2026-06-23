using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests;

/// <summary>
/// TDD for the plugin foundation: the registry/pipeline behavior of <see cref="PluginManager"/>.
/// Pure logic (no Avalonia) — uses in-process fake plugins. (External-DLL loading via AssemblyLoadContext
/// is exercised by the safe-directory test + manual/sample-plugin verification.)
/// </summary>
public class PluginTests
{
    // ---- fakes ------------------------------------------------------------
    private sealed class FakeResolver(string scheme) : ILinkResolver
    {
        public bool CanResolve(string url) => url.StartsWith(scheme);
        public Task<DownloadPlan> ResolveAsync(string url, CancellationToken ct) =>
            Task.FromResult(new DownloadPlan
            {
                SuggestedFileName = "out.mp4",
                Parts = new[]
                {
                    new DownloadPart { Url = "https://cdn/v.mp4", Kind = PartKind.Video },
                    new DownloadPart { Url = "https://cdn/a.m4a", Kind = PartKind.Audio },
                },
                PostProcess = new PostProcess { Kind = PostProcessKind.Mux },
            });
    }

    private sealed class FakePostProcessor : IPostProcessor
    {
        public bool CanProcess(PostProcess plan) => plan.Kind == PostProcessKind.Mux;
        public Task<string> ProcessAsync(IReadOnlyList<string> inputs, PostProcess plan, string outputPath,
            System.IProgress<double> progress, CancellationToken ct) => Task.FromResult(outputPath);
    }

    private sealed class FakeTransferProvider : ITransferProvider
    {
        public bool CanHandle(string url) => url.StartsWith("magnet:");
        public ITransfer Create(string url, string targetFolder) => null!;
    }

    private sealed class FakePlugin : IDownloaderPlugin
    {
        public string Id => "test.plugin";
        public string Name => "Test Plugin";
        public string Version => "1.0.0";
        public string Author => "tester";
        public string Description => "fake plugin for tests";
        public void Initialize(IPluginContext context)
        {
            context.RegisterResolver(new FakeResolver("fake://"));
            context.RegisterPostProcessor(new FakePostProcessor());
            context.RegisterTransferProvider(new FakeTransferProvider());
        }
    }

    // ---- tests ------------------------------------------------------------
    [Fact]
    public void Registering_a_plugin_adds_it_and_its_contributions()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin());

        Assert.Single(pm.Plugins);
        Assert.Equal("test.plugin", pm.Plugins[0].Id);
        Assert.True(pm.Plugins[0].IsEnabled);
        Assert.NotNull(pm.FindResolver("fake://reel/123"));
        Assert.Null(pm.FindResolver("https://example.com/file.zip"));
    }

    [Fact]
    public async Task ResolveAsync_routes_to_the_matching_resolver()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin());

        var plan = await pm.ResolveAsync("fake://reel/123", CancellationToken.None);
        Assert.NotNull(plan);
        Assert.Equal("out.mp4", plan!.SuggestedFileName);
        Assert.Equal(2, plan.Parts.Count);

        Assert.Null(await pm.ResolveAsync("https://plain/file.zip", CancellationToken.None));
    }

    [Fact]
    public void Post_processor_is_selected_by_the_plan()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin());

        Assert.NotNull(pm.FindPostProcessor(new PostProcess { Kind = PostProcessKind.Mux }));
        Assert.Null(pm.FindPostProcessor(PostProcess.None));
    }

    [Fact]
    public void Transfer_provider_is_selected_by_the_url()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin());

        Assert.NotNull(pm.FindTransferProvider("magnet:?xt=urn:btih:abc"));
        Assert.Null(pm.FindTransferProvider("https://example.com/file.zip"));
    }

    [Fact]
    public void Disabling_a_plugin_removes_its_contributions_and_re_enabling_restores_them()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin());

        pm.SetEnabled("test.plugin", false);
        Assert.False(pm.Plugins[0].IsEnabled);
        Assert.Null(pm.FindResolver("fake://x"));
        Assert.Null(pm.FindTransferProvider("magnet:x"));

        pm.SetEnabled("test.plugin", true);
        Assert.True(pm.Plugins[0].IsEnabled);
        Assert.NotNull(pm.FindResolver("fake://x"));
    }

    [Fact]
    public void Loading_a_missing_or_empty_directory_is_safe()
    {
        var pm = new PluginManager();
        pm.LoadFromDirectory("/no/such/plugins/dir"); // must not throw
        Assert.Empty(pm.Plugins);

        var empty = System.IO.Directory.CreateTempSubdirectory().FullName;
        pm.LoadFromDirectory(empty);
        Assert.Empty(pm.Plugins);
    }

    [Fact]
    public void Registering_the_same_plugin_id_twice_is_idempotent()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin());
        pm.RegisterPlugin(new FakePlugin());
        Assert.Single(pm.Plugins);
    }

    [Fact]
    public void Loads_a_real_external_plugin_DLL_from_disk()
    {
        // Proves the AssemblyLoadContext loader + shared-SDK type identity works end-to-end with a real
        // external DLL (the sample plugin, built + staged by the test csproj).
        var dir = System.IO.Path.Combine(System.AppContext.BaseDirectory, "plugins-sample");
        Assert.True(System.IO.Directory.Exists(dir), "sample plugin was not staged — check the test csproj target");

        var pm = new PluginManager();
        pm.LoadFromDirectory(dir);

        Assert.Contains(pm.Plugins, p => p.Id == "com.bezzad.github-releases");
        Assert.NotNull(pm.FindResolver("https://github.com/bezzad/Downloader.Desktop")); // GitHub resolver
        Assert.NotNull(pm.FindTransferProvider("file:///tmp/x"));                        // file:// transfer
        Assert.Null(pm.FindResolver("https://example.com/file.zip"));
    }

    [Fact]
    public void GitHub_resolver_claims_only_owner_repo_links()
    {
        // CanResolve gating for the real sample resolver (pure, no network): owner/repo on github.com only.
        var dir = System.IO.Path.Combine(System.AppContext.BaseDirectory, "plugins-sample");
        Assert.True(System.IO.Directory.Exists(dir), "sample plugin was not staged — check the test csproj target");
        var pm = new PluginManager();
        pm.LoadFromDirectory(dir);

        Assert.NotNull(pm.FindResolver("https://github.com/bezzad/Downloader.Desktop"));
        Assert.NotNull(pm.FindResolver("https://github.com/bezzad/Downloader.Desktop/releases")); // extra path ok
        Assert.Null(pm.FindResolver("https://github.com/bezzad"));        // owner only -> not claimed
        Assert.Null(pm.FindResolver("https://gitlab.com/bezzad/repo"));   // wrong host -> not claimed
        Assert.Null(pm.FindResolver("not a url"));
    }

    [Fact]
    public async Task Resolves_a_real_github_repo_to_a_release_asset()
    {
        // LIVE network test (hits api.github.com) — gated so CI/offline runs skip it. Run locally with
        // DLDESKTOP_NET=1 to verify the exact reported scenario end-to-end: a github.com/owner/repo link
        // resolves to a real release asset URL + name for this OS.
        if (System.Environment.GetEnvironmentVariable("DLDESKTOP_NET") != "1")
            return;

        var dir = System.IO.Path.Combine(System.AppContext.BaseDirectory, "plugins-sample");
        var pm = new PluginManager();
        pm.LoadFromDirectory(dir);
        var resolver = pm.FindResolver("https://github.com/bezzad/Downloader.Desktop");
        Assert.NotNull(resolver);

        var plan = await resolver.ResolveAsync("https://github.com/bezzad/Downloader.Desktop", default);
        Assert.NotNull(plan);
        Assert.NotEmpty(plan.Parts);
        Assert.StartsWith("https://", plan.Parts[0].Url);
        Assert.False(string.IsNullOrWhiteSpace(plan.SuggestedFileName));
    }
}
