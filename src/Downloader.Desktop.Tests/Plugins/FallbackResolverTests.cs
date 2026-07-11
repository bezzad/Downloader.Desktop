using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins;

/// <summary>
/// Fallback-resolver semantics (ILinkResolver.IsFallback): a generic resolver (e.g. the website plugin's
/// "any web page") must never shadow a specific one, and variant lookups merge across ALL claiming
/// resolvers instead of consulting only the first.
/// </summary>
public class FallbackResolverTests
{
    private sealed class StubResolver : ILinkResolver
    {
        public Func<string, bool> Claims = _ => true;
        public bool Fallback;
        public IReadOnlyList<LinkVariant> Variants;
        public bool ThrowOnVariants;

        public bool IsFallback => Fallback;
        public bool CanResolve(string url) => Claims(url);
        public Task<DownloadPlan> ResolveAsync(string url, CancellationToken ct) =>
            Task.FromResult(new DownloadPlan { Parts = new[] { new DownloadPart { Url = url } } });

        public Task<IReadOnlyList<LinkVariant>> GetVariantsAsync(string url, ResolveOptions options, CancellationToken ct)
            => ThrowOnVariants ? throw new InvalidOperationException("variant lookup broke") : Task.FromResult(Variants);
    }

    private sealed class StubPlugin(string id, params ILinkResolver[] resolvers) : IDownloaderPlugin
    {
        public string Id => id;
        public string Name => id;
        public string Version => "1.0.0";
        public string Author => "tester";
        public string Description => "stub";
        public void Initialize(IPluginContext context)
        {
            foreach (var r in resolvers)
                context.RegisterResolver(r);
        }
    }

    private static LinkVariant Variant(string vid, bool isDefault = false, string substitute = null) =>
        new() { Id = vid, Label = vid, IsDefault = isDefault, SubstituteUrl = substitute };

    [Fact]
    public void Specific_resolver_wins_over_a_fallback_that_also_claims()
    {
        var pm = new PluginManager();
        var fallback = new StubResolver { Fallback = true };
        var specific = new StubResolver();
        // the fallback plugin registers FIRST — load order must not matter
        pm.RegisterPlugin(new StubPlugin("test.fallback", fallback));
        pm.RegisterPlugin(new StubPlugin("test.specific", specific));

        Assert.Same(specific, pm.FindResolver("https://github.com/owner/repo"));
        Assert.Equal("test.specific", pm.FindResolverPluginId("https://github.com/owner/repo"));
    }

    [Fact]
    public void Fallback_handles_urls_no_specific_resolver_claims()
    {
        var pm = new PluginManager();
        var fallback = new StubResolver { Fallback = true };
        var specific = new StubResolver { Claims = u => u.Contains("github.com") };
        pm.RegisterPlugin(new StubPlugin("test.fallback", fallback));
        pm.RegisterPlugin(new StubPlugin("test.specific", specific));

        Assert.Same(fallback, pm.FindResolver("https://blog.example.com/post"));
        Assert.Equal("test.fallback", pm.FindResolverPluginId("https://blog.example.com/post"));
    }

    [Fact]
    public async Task Only_the_detected_resolvers_variants_are_shown_never_a_fallbacks_extras()
    {
        var pm = new PluginManager();
        var specific = new StubResolver { Variants = new[] { Variant("1080", isDefault: true), Variant("720") } };
        // the fallback also claims — its generic variant must NOT pollute the specific plugin's list
        var fallback = new StubResolver { Fallback = true, Variants = new[] { Variant("zip", isDefault: true, substitute: "websitezip:x") } };
        pm.RegisterPlugin(new StubPlugin("test.fallback", fallback));
        pm.RegisterPlugin(new StubPlugin("test.specific", specific));

        var shown = await pm.GetVariantsAsync("https://video.site/watch", CancellationToken.None);

        Assert.Equal(new[] { "1080", "720" }, shown.Select(v => v.Id));
        Assert.True(shown[0].IsDefault);
    }

    [Fact]
    public async Task Fallback_variants_appear_when_the_specific_resolver_offers_none()
    {
        var pm = new PluginManager();
        var specific = new StubResolver { Variants = null }; // claims, but has no choices to offer
        var fallback = new StubResolver { Fallback = true, Variants = new[] { Variant("zip", substitute: "websitezip:x") } };
        pm.RegisterPlugin(new StubPlugin("test.fallback", fallback));
        pm.RegisterPlugin(new StubPlugin("test.specific", specific));

        var shown = await pm.GetVariantsAsync("https://blog.example.com/post", CancellationToken.None);

        Assert.Equal("zip", Assert.Single(shown).Id);
    }

    [Fact]
    public async Task One_failing_variant_lookup_does_not_hide_the_others()
    {
        var pm = new PluginManager();
        var broken = new StubResolver { ThrowOnVariants = true };
        var working = new StubResolver { Fallback = true, Variants = new[] { Variant("zip") } };
        pm.RegisterPlugin(new StubPlugin("test.broken", broken));
        pm.RegisterPlugin(new StubPlugin("test.working", working));

        var merged = await pm.GetVariantsAsync("https://site/page", CancellationToken.None);

        Assert.Equal("zip", Assert.Single(merged).Id);
    }

    [Fact]
    public void Resolver_plugin_name_lookup_respects_fallback_ordering()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new StubPlugin("test.fallback", new StubResolver { Fallback = true }));
        pm.RegisterPlugin(new StubPlugin("test.specific", new StubResolver { Claims = u => u.Contains("github.com") }));

        Assert.Equal("test.specific", pm.FindResolverPluginName("https://github.com/o/r"));
        Assert.Equal("test.fallback", pm.FindResolverPluginName("https://blog.example.com/post"));
        Assert.Null(new PluginManager().FindResolverPluginName("https://x/"));
    }

    [Fact]
    public async Task No_claiming_resolver_or_no_variants_returns_null()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new StubPlugin("test.none", new StubResolver { Claims = _ => false }));

        Assert.Null(await pm.GetVariantsAsync("https://site/page", CancellationToken.None));

        pm.RegisterPlugin(new StubPlugin("test.quiet", new StubResolver())); // claims, offers nothing
        Assert.Null(await pm.GetVariantsAsync("https://site/page", CancellationToken.None));
    }
}
