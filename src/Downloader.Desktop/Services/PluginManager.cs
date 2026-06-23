using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Plugins;

namespace Downloader.Desktop.Services;

/// <summary>Read-only info about a loaded plugin (bound by the Plugins UI; returned to callers).</summary>
public sealed class PluginDescriptor
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string Version { get; init; }
    public string Author { get; init; }
    public string Description { get; init; }
    public bool IsEnabled { get; internal set; } = true;
}

/// <summary>
/// Loads external plugins and exposes their contributions to the download pipeline. A plugin is a DLL
/// (referencing <c>Downloader.Desktop.Plugins.Abstractions</c>) loaded into its own collectible
/// <see cref="AssemblyLoadContext"/>; the Abstractions SDK is forced to resolve from the host so plugin
/// types unify with the host's (shared type identity). Only ENABLED plugins' contributions are used.
/// Pure of UI/Avalonia so it's unit-testable with in-process fake plugins.
/// </summary>
public sealed class PluginManager
{
    /// <summary>Where external plugins live (Linux: ~/.config/Downloader/plugins).</summary>
    public static string PluginsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Downloader", "plugins");

    private readonly List<LoadedPlugin> _plugins = new();
    private readonly object _gate = new();

    public IReadOnlyList<PluginDescriptor> Plugins
    {
        get { lock (_gate) return _plugins.Select(p => p.Descriptor).ToList(); }
    }

    /// <summary>Register an already-instantiated plugin (used by the loader and by tests). Idempotent by Id.</summary>
    public void RegisterPlugin(IDownloaderPlugin plugin, AssemblyLoadContext context = null, string sourcePath = null)
    {
        if (plugin == null || string.IsNullOrWhiteSpace(plugin.Id))
            return;
        lock (_gate)
        {
            if (_plugins.Any(p => p.Descriptor.Id == plugin.Id))
                return; // already loaded
            var loaded = new LoadedPlugin(plugin, context, sourcePath);
            try
            {
                plugin.Initialize(loaded.Context);
            }
            catch (Exception ex)
            {
                AppLog.Error($"Plugin '{plugin.Id}' failed to initialize", ex);
                return;
            }
            _plugins.Add(loaded);
        }
    }

    /// <summary>Scan a folder for plugin DLLs and load them. Safe on a missing/empty/garbage folder.</summary>
    public void LoadFromDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;

        foreach (var dll in Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories))
        {
            try
            {
                var alc = new PluginLoadContext(dll);
                var asm = alc.LoadFromAssemblyPath(dll);
                var pluginTypes = SafeGetTypes(asm)
                    .Where(t => t is { IsAbstract: false, IsInterface: false } &&
                                typeof(IDownloaderPlugin).IsAssignableFrom(t))
                    .ToList();
                if (pluginTypes.Count == 0)
                    continue; // just a dependency DLL, not an entry plugin
                foreach (var type in pluginTypes)
                    if (Activator.CreateInstance(type) is IDownloaderPlugin plugin)
                        RegisterPlugin(plugin, alc, dll);
            }
            catch (Exception ex)
            {
                AppLog.Error($"Failed to load plugin '{dll}'", ex);
            }
        }
    }

    public ILinkResolver FindResolver(string url) =>
        Enabled().SelectMany(p => p.Resolvers).FirstOrDefault(r => Safe(() => r.CanResolve(url)));

    public IPostProcessor FindPostProcessor(PostProcess plan) =>
        Enabled().SelectMany(p => p.PostProcessors).FirstOrDefault(pp => Safe(() => pp.CanProcess(plan)));

    public ITransferProvider FindTransferProvider(string url) =>
        Enabled().SelectMany(p => p.TransferProviders).FirstOrDefault(tp => Safe(() => tp.CanHandle(url)));

    /// <summary>Run the input through the first matching resolver, or null if no plugin claims it.</summary>
    public async Task<DownloadPlan> ResolveAsync(string url, CancellationToken cancellationToken)
    {
        var resolver = FindResolver(url);
        if (resolver == null)
            return null;
        return await resolver.ResolveAsync(url, cancellationToken).ConfigureAwait(false);
    }

    public void SetEnabled(string pluginId, bool enabled)
    {
        lock (_gate)
        {
            var p = _plugins.FirstOrDefault(x => x.Descriptor.Id == pluginId);
            if (p != null)
                p.Descriptor.IsEnabled = enabled;
        }
    }

    /// <summary>
    /// Uninstall a plugin: drop it from the registry (so it stops contributing immediately), unload its
    /// collectible load context, and delete its DLL (+ sidecar deps.json) from disk so it doesn't reload on
    /// next launch. Returns true if the plugin was found. File deletion is best-effort: a still-mapped file
    /// (mostly Windows) is retried after a GC; if it still can't be deleted it's left for the next launch.
    /// </summary>
    public bool RemovePlugin(string pluginId)
    {
        LoadedPlugin p;
        lock (_gate)
        {
            p = _plugins.FirstOrDefault(x => x.Descriptor.Id == pluginId);
            if (p == null)
                return false;
            _plugins.Remove(p);
        }

        try { p.Context = null; p.Alc?.Unload(); }
        catch (Exception ex) { AppLog.Error($"Unloading plugin '{pluginId}' failed", ex); }

        if (!string.IsNullOrWhiteSpace(p.SourcePath))
        {
            TryDeleteFile(p.SourcePath);
            TryDeleteFile(Path.ChangeExtension(p.SourcePath, ".deps.json"));
        }
        return true;
    }

    private static void TryDeleteFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try { File.Delete(path); return; }
            catch (Exception ex)
            {
                if (attempt == 0) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
                else AppLog.Error($"Could not delete plugin file '{path}' (will retry next launch)", ex);
            }
        }
    }

    private List<LoadedPlugin> Enabled()
    {
        lock (_gate) return _plugins.Where(p => p.Descriptor.IsEnabled).ToList();
    }

    private static bool Safe(Func<bool> predicate)
    {
        try { return predicate(); } catch { return false; }
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
    }

    // ---- internals --------------------------------------------------------
    private sealed class LoadedPlugin
    {
        public PluginDescriptor Descriptor { get; }
        public List<ILinkResolver> Resolvers { get; } = new();
        public List<ITransferProvider> TransferProviders { get; } = new();
        public List<IPostProcessor> PostProcessors { get; } = new();
        public IPluginContext Context { get; set; }
        public AssemblyLoadContext Alc { get; }
        public string SourcePath { get; }

        public LoadedPlugin(IDownloaderPlugin plugin, AssemblyLoadContext alc, string sourcePath = null)
        {
            Alc = alc;
            SourcePath = sourcePath;
            Descriptor = new PluginDescriptor
            {
                Id = plugin.Id,
                Name = plugin.Name ?? plugin.Id,
                Version = plugin.Version ?? "",
                Author = plugin.Author ?? "",
                Description = plugin.Description ?? "",
                IsEnabled = true,
            };
            Context = new PluginContext(this, plugin);
        }
    }

    private sealed class PluginContext : IPluginContext
    {
        private readonly LoadedPlugin _owner;
        private readonly IDownloaderPlugin _plugin;
        public PluginContext(LoadedPlugin owner, IDownloaderPlugin plugin) { _owner = owner; _plugin = plugin; }

        public void RegisterResolver(ILinkResolver resolver) { if (resolver != null) _owner.Resolvers.Add(resolver); }
        public void RegisterTransferProvider(ITransferProvider provider) { if (provider != null) _owner.TransferProviders.Add(provider); }
        public void RegisterPostProcessor(IPostProcessor processor) { if (processor != null) _owner.PostProcessors.Add(processor); }

        public string DataDirectory
        {
            get
            {
                var dir = Path.Combine(PluginsRoot, "data", _plugin.Id);
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private Microsoft.Extensions.Logging.ILogger _logger;
        public Microsoft.Extensions.Logging.ILogger Logger =>
            _logger ??= AppLog.Factory.CreateLogger($"plugin:{_plugin.Id}");
    }

    /// <summary>
    /// Per-plugin load context. The Abstractions SDK is ALWAYS resolved from the host (return null →
    /// Default context) so a plugin's `IDownloaderPlugin`/`ILinkResolver` types are the SAME types as the
    /// host's — otherwise `IsAssignableFrom` would fail. Private plugin deps resolve via the ADR.
    /// </summary>
    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private const string SharedSdk = "Downloader.Desktop.Plugins.Abstractions";
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath) : base(isCollectible: true)
            => _resolver = new AssemblyDependencyResolver(pluginPath);

        protected override Assembly Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == SharedSdk)
                return null; // share the host's SDK copy → unified type identity
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path != null ? LoadFromAssemblyPath(path) : null;
        }
    }
}
