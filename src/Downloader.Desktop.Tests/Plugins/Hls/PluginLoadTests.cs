using System.Reflection;
using System.Runtime.Loader;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Plugins.Hls;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins.Hls;

/// <summary>
/// Mirrors the host loader: load the built plugin DLL in a collectible AssemblyLoadContext that resolves the
/// SDK (<c>Downloader.Desktop.Plugins.Abstractions</c>) from the default context, so the loaded plugin type
/// shares type identity with the host and satisfies <c>is IDownloaderPlugin</c>.
/// </summary>
public class PluginLoadTests
{
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Plugin_loads_in_host_mirroring_context_and_exposes_metadata()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "Downloader.Desktop.Plugins.Hls.dll");
        Assert.True(File.Exists(dll), $"plugin DLL not found at {dll}");

        var alc = new HostMirroringLoadContext(dll);
        try
        {
            var asm = alc.LoadFromAssemblyPath(dll);

            var pluginType = asm.GetTypes().SingleOrDefault(t =>
                typeof(IDownloaderPlugin).IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false });
            Assert.NotNull(pluginType);

            var plugin = (IDownloaderPlugin)Activator.CreateInstance(pluginType!)!;
            Assert.Equal("com.bezzad.hls", plugin.Id);
            Assert.False(string.IsNullOrWhiteSpace(plugin.Name));
            Assert.False(string.IsNullOrWhiteSpace(plugin.Version));
            Assert.Equal("bezzad", plugin.Author);
            Assert.False(string.IsNullOrWhiteSpace(plugin.Description));
        }
        finally
        {
            alc.Unload();
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Initialize_registers_a_resolver_and_a_post_processor()
    {
        var ctx = new FakeContext();
        new HlsPlugin().Initialize(ctx);

        // Two resolvers: HLS playlists and DASH manifests.
        Assert.Equal(2, ctx.Resolvers.Count);
        Assert.Single(ctx.PostProcessors);
        Assert.Empty(ctx.TransferProviders);

        var hls = "https://cdn.example.com/v/index.m3u8";
        var dash = "https://cdn.example.com/v/manifest.mpd";
        Assert.Single(ctx.Resolvers, r => r.CanResolve(hls));
        Assert.Single(ctx.Resolvers, r => r.CanResolve(dash));

        // Neither claims a page URL, and neither is a fallback that could shadow another plugin.
        foreach (var resolver in ctx.Resolvers)
        {
            Assert.False(resolver.IsFallback);
            Assert.False(resolver.CanResolve("https://youtube.com/watch?v=abc"));
            Assert.False(resolver.CanResolve("https://x.com/u/status/1"));
        }
    }

    private sealed class HostMirroringLoadContext(string mainPath) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(mainPath);

        protected override Assembly? Load(AssemblyName name)
        {
            // The SDK must resolve from the default context for shared type identity (exactly what the host does).
            if (name.Name == "Downloader.Desktop.Plugins.Abstractions") return null;
            var path = _resolver.ResolveAssemblyToPath(name);
            if (path is null) return null;
            // Same guard as the host's PluginLoadContext: once this context is unloading it cannot load
            // anything, and must say so rather than throw on the unload thread. See PluginLoadContextTests.
            try { return LoadFromAssemblyPath(path); }
            catch (InvalidOperationException) { return null; }
        }
    }

    private sealed class FakeContext : IPluginContext
    {
        public List<ILinkResolver> Resolvers { get; } = new();
        public List<IPostProcessor> PostProcessors { get; } = new();
        public List<ITransferProvider> TransferProviders { get; } = new();

        public void RegisterResolver(ILinkResolver resolver) => Resolvers.Add(resolver);
        public void RegisterTransferProvider(ITransferProvider provider) => TransferProviders.Add(provider);
        public void RegisterPostProcessor(IPostProcessor processor) => PostProcessors.Add(processor);

        public string DataDirectory { get; } = Directory.CreateTempSubdirectory("hls-plugin-").FullName;
        public Microsoft.Extensions.Logging.ILogger Logger { get; } =
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }
}
