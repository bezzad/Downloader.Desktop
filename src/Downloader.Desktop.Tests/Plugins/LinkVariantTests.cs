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
/// The generic resolver-variant mechanism (link-variants): a resolver can list the selectable variants
/// behind one link (qualities, model tags…), the host exposes them via
/// <see cref="PluginManager.GetVariantsAsync"/>, and the chosen id flows back through
/// <see cref="ResolveOptions.VariantId"/> on resolve.
/// </summary>
public class LinkVariantTests
{
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Host_returns_the_claiming_resolvers_variants()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new VariantPlugin());

        var variants = await pm.GetVariantsAsync("variant://video", CancellationToken.None);

        Assert.NotNull(variants);
        Assert.Equal(new[] { "1080", "audio" }, variants.Select(v => v.Id));
        Assert.True(variants[0].IsDefault);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Host_returns_null_when_no_resolver_claims_the_link()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new VariantPlugin());

        Assert.Null(await pm.GetVariantsAsync("https://unclaimed.example/x", CancellationToken.None));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Host_returns_null_for_a_disabled_plugin()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new VariantPlugin());
        pm.SetEnabled(VariantPlugin.PluginId, false);

        Assert.Null(await pm.GetVariantsAsync("variant://video", CancellationToken.None));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Host_swallows_a_variant_lookup_failure()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new VariantPlugin { ThrowOnVariants = true });

        Assert.Null(await pm.GetVariantsAsync("variant://video", CancellationToken.None));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Resolver_without_the_override_reports_no_variants()
    {
        ILinkResolver plain = new PlainResolver();
        Assert.Null(await plain.GetVariantsAsync("anything", null, CancellationToken.None));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Chosen_variant_id_reaches_the_resolver_through_the_manager()
    {
        var plugin = new VariantPlugin();
        var pm = new PluginManager();
        pm.RegisterPlugin(plugin);
        var dm = new DownloadManager(pm);

        var plan = await dm.ResolvePlanAsync("variant://video", CancellationToken.None, variantId: "720");

        Assert.Equal("720", plugin.Resolver.LastVariantId);
        Assert.Equal("https://cdn/720.mp4", plan.Parts[0].Url);
    }

    private sealed class VariantPlugin : IDownloaderPlugin
    {
        public const string PluginId = "com.test.variants";
        public string Id => PluginId;
        public string Name => "Variant plugin";
        public string Version => "1.0.0";
        public string Author => "test";
        public string Description => "test";
        public bool ThrowOnVariants { get; init; }
        public VariantResolver Resolver { get; private set; }

        public void Initialize(IPluginContext context) =>
            context.RegisterResolver(Resolver = new VariantResolver(ThrowOnVariants));
    }

    private sealed class VariantResolver : ILinkResolver
    {
        private readonly bool _throw;
        public string LastVariantId { get; private set; }

        public VariantResolver(bool @throw) => _throw = @throw;

        public bool CanResolve(string url) => url.StartsWith("variant://", StringComparison.Ordinal);

        public Task<IReadOnlyList<LinkVariant>> GetVariantsAsync(
            string url, ResolveOptions options, CancellationToken cancellationToken)
        {
            if (_throw)
                throw new InvalidOperationException("boom");
            return Task.FromResult<IReadOnlyList<LinkVariant>>(new[]
            {
                new LinkVariant { Id = "1080", Label = "1080p", IsDefault = true },
                new LinkVariant { Id = "audio", Label = "Audio only" },
            });
        }

        public Task<DownloadPlan> ResolveAsync(string url, CancellationToken cancellationToken)
            => ResolveAsync(url, null, cancellationToken);

        public Task<DownloadPlan> ResolveAsync(string url, ResolveOptions options, CancellationToken cancellationToken)
        {
            LastVariantId = options?.VariantId;
            return Task.FromResult(new DownloadPlan
            {
                SuggestedFileName = "clip.mp4",
                Parts = new[] { new DownloadPart { Url = $"https://cdn/{options?.VariantId ?? "best"}.mp4" } },
            });
        }
    }

    private sealed class PlainResolver : ILinkResolver
    {
        public bool CanResolve(string url) => true;
        public Task<DownloadPlan> ResolveAsync(string url, CancellationToken cancellationToken)
            => Task.FromResult(new DownloadPlan());
    }
}
