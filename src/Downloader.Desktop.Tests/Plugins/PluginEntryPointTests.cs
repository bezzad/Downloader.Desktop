using System;
using System.Linq;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Plugins.Hls;
using Downloader.Desktop.Plugins.Ollama;
using Downloader.Desktop.Plugins.Website;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins;

/// <summary>
/// The three shipped plugins' entry points: identity, and what <c>Initialize</c> actually registers.
///
/// A plugin that throws from <c>Initialize</c> is swallowed by <see cref="PluginManager"/> (logged, then
/// dropped) so one bad plugin cannot take the app down — which means a broken entry point shows up only
/// as a feature that silently does not exist. Registering each real plugin through the real manager is
/// the cheapest way to catch that, and it also pins the contributions each one claims to make.
///
/// The ids matter beyond the code: they are the keys the release catalog, the update check and the
/// user's "disabled plugins" list are all written against, so changing one silently orphans installs.
/// </summary>
public class PluginEntryPointTests
{
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_streaming_plugin_registers_both_manifest_resolvers_and_a_post_processor()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new HlsPlugin());

        var descriptor = Assert.Single(pm.Plugins);
        Assert.Equal("com.bezzad.hls", descriptor.Id);
        Assert.False(string.IsNullOrWhiteSpace(descriptor.Name));
        Assert.Equal("bezzad", descriptor.Author);
        Assert.False(string.IsNullOrWhiteSpace(descriptor.Description));

        // Version derives from the assembly so the runtime value and the catalog's csproj <Version>
        // share one source — a hardcoded string here would make the update check prompt forever.
        Assert.Matches(@"^\d+\.\d+\.\d+$", descriptor.Version);

        // HLS and DASH claim disjoint extensions, so neither can shadow the other.
        Assert.NotNull(pm.FindResolver("https://host/stream.m3u8"));
        Assert.NotNull(pm.FindResolver("https://host/manifest.mpd"));
        Assert.Null(pm.FindResolver("https://host/file.zip"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_streaming_plugin_declares_ffmpeg_as_a_runtime_dependency()
    {
        var plugin = new HlsPlugin();
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "dldesktop-deps-" + Guid.NewGuid().ToString("N"));

        var deps = plugin.GetRequiredDependencies(dir);

        // ffmpeg is fetched by the HOST at Add-time rather than bundled, so if it stops being declared
        // the plugin installs fine and then fails at assembly time with no explanation.
        var dep = Assert.Single(deps);
        Assert.Equal("ffmpeg", dep.Id);
        Assert.False(string.IsNullOrWhiteSpace(dep.DisplayName));
        Assert.NotNull(dep.DownloadUrl);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_website_plugin_registers_a_fallback_resolver_and_its_transfer_provider()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new WebsitePlugin());

        var descriptor = Assert.Single(pm.Plugins);
        Assert.Equal("com.bezzad.website-zip", descriptor.Id);
        Assert.Equal("bezzad", descriptor.Author);
        Assert.Matches(@"^\d+\.\d+\.\d+$", descriptor.Version);

        // It claims page-like links only as a FALLBACK, and owns the websitezip: scheme end to end.
        Assert.NotNull(pm.FindResolver("https://example.com/docs"));
        Assert.NotNull(pm.FindTransferProvider("websitezip:https://example.com/docs"));
        Assert.Null(pm.FindTransferProvider("https://example.com/file.zip"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_ollama_plugin_claims_model_references()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new OllamaPlugin());

        var descriptor = Assert.Single(pm.Plugins);
        Assert.Equal("com.bezzad.ollama-models", descriptor.Id);
        Assert.Equal("bezzad", descriptor.Author);
        Assert.False(string.IsNullOrWhiteSpace(descriptor.Description));

        Assert.NotNull(pm.FindResolver("gemma3:12b"));
        Assert.NotNull(pm.FindResolver("https://ollama.com/library/gemma3"));
        Assert.Null(pm.FindResolver("https://example.com/file.zip"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_specific_plugin_is_never_shadowed_by_the_website_fallback()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new WebsitePlugin());
        pm.RegisterPlugin(new HlsPlugin());
        pm.RegisterPlugin(new OllamaPlugin());

        // The two-pass lookup exists for exactly this: an extensionless streaming or model link must
        // reach its own plugin, not the "any web page" fallback.
        Assert.Equal("com.bezzad.hls", pm.FindResolverPluginId("https://host/stream.m3u8"));
        Assert.Equal("com.bezzad.ollama-models", pm.FindResolverPluginId("gemma3:12b"));

        // …while an ordinary page still lands on the fallback.
        Assert.Equal("com.bezzad.website-zip", pm.FindResolverPluginId("https://example.com/docs"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void All_three_plugins_can_be_loaded_together_without_id_collisions()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new HlsPlugin());
        pm.RegisterPlugin(new WebsitePlugin());
        pm.RegisterPlugin(new OllamaPlugin());

        Assert.Equal(3, pm.Plugins.Count);
        Assert.Equal(3, pm.Plugins.Select(p => p.Id).Distinct().Count());
        Assert.All(pm.Plugins, p => Assert.True(p.IsEnabled));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Registering_the_same_plugin_twice_is_ignored()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new HlsPlugin());
        pm.RegisterPlugin(new HlsPlugin());

        Assert.Single(pm.Plugins);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_disabled_plugin_stops_contributing_immediately()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new HlsPlugin());
        Assert.NotNull(pm.FindResolver("https://host/stream.m3u8"));

        pm.SetEnabled("com.bezzad.hls", false);

        // Not "at the next restart" — the lookup must stop seeing it now.
        Assert.Null(pm.FindResolver("https://host/stream.m3u8"));
    }
}
