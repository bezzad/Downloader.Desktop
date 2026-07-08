using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Plugins;

namespace Downloader.Desktop.Services;

/// <summary>Outcome of installing/updating an optional plugin from a downloaded package.</summary>
public sealed class PluginInstallResult
{
    public bool Success { get; init; }
    /// <summary>Friendly, user-facing message when <see cref="Success"/> is false.</summary>
    public string Error { get; init; }
    /// <summary>The newly loaded plugin on success.</summary>
    public PluginDescriptor Plugin { get; init; }

    public static PluginInstallResult Ok(PluginDescriptor p) => new() { Success = true, Plugin = p };
    public static PluginInstallResult Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>Read-only info about a loaded plugin (bound by the Plugins UI; returned to callers).</summary>
public sealed class PluginDescriptor
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string Version { get; init; }
    public string Author { get; init; }
    public string Description { get; init; }
    public bool IsEnabled { get; internal set; } = true;

    /// <summary>True for plugins shipped with the app (loaded from the app dir's plugins/ folder).
    /// Built-ins can be disabled but not removed.</summary>
    public bool IsBuiltIn { get; init; }
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

    /// <summary>Where the plugins bundled with the app live (copied next to the executable at build/publish).</summary>
    public static string BuiltInPluginsRoot => Path.Combine(AppContext.BaseDirectory, "plugins");

    private readonly List<LoadedPlugin> _plugins = new();
    private readonly object _gate = new();

    public IReadOnlyList<PluginDescriptor> Plugins
    {
        get { lock (_gate) return _plugins.Select(p => p.Descriptor).ToList(); }
    }

    /// <summary>Register an already-instantiated plugin (used by the loader and by tests). Idempotent by Id.</summary>
    public void RegisterPlugin(IDownloaderPlugin plugin, AssemblyLoadContext context = null, string sourcePath = null,
        bool isBuiltIn = false)
    {
        if (plugin == null || string.IsNullOrWhiteSpace(plugin.Id))
            return;
        lock (_gate)
        {
            if (_plugins.Any(p => p.Descriptor.Id == plugin.Id))
                return; // already loaded
            var loaded = new LoadedPlugin(plugin, context, sourcePath, isBuiltIn);
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

    /// <summary>Scan a folder for plugin DLLs and load them. Safe on a missing/empty/garbage folder.
    /// Pass <paramref name="isBuiltIn"/> for the app-dir plugins folder (disable-only, never removable).</summary>
    public void LoadFromDirectory(string directory, bool isBuiltIn = false)
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
                        RegisterPlugin(plugin, alc, dll, isBuiltIn);
            }
            catch (Exception ex)
            {
                AppLog.Error($"Failed to load plugin '{dll}'", ex);
            }
        }
    }

    /// <summary>Loads the plugins bundled with the app (app dir /plugins) as built-ins.</summary>
    public void LoadBuiltIns() => LoadFromDirectory(BuiltInPluginsRoot, isBuiltIn: true);

    public ILinkResolver FindResolver(string url) =>
        Enabled().SelectMany(p => p.Resolvers).FirstOrDefault(r => Safe(() => r.CanResolve(url)));

    public IPostProcessor FindPostProcessor(PostProcess plan) =>
        Enabled().SelectMany(p => p.PostProcessors).FirstOrDefault(pp => Safe(() => pp.CanProcess(plan)));

    public ITransferProvider FindTransferProvider(string url) =>
        Enabled().SelectMany(p => p.TransferProviders).FirstOrDefault(tp => Safe(() => tp.CanHandle(url)));

    /// <summary>The id of the enabled plugin whose resolver claims <paramref name="url"/>, or null.
    /// Recorded on the download so post-download actions can be offered by the SAME plugin later.</summary>
    public string FindResolverPluginId(string url) =>
        Enabled().FirstOrDefault(p => p.Resolvers.Any(r => Safe(() => r.CanResolve(url))))?.Descriptor.Id;

    /// <summary>The post-download action the given plugin offers for this completed download, or null.
    /// Only the RESOLVING plugin's actions are consulted (never another plugin's).</summary>
    public IPostDownloadAction FindPostDownloadAction(string pluginId, string sourceUrl, string filePath)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
            return null;
        List<IPostDownloadAction> actions;
        lock (_gate)
        {
            var p = _plugins.FirstOrDefault(x => x.Descriptor.Id == pluginId && x.Descriptor.IsEnabled);
            if (p == null)
                return null;
            actions = p.PostDownloadActions.ToList();
        }
        return actions.FirstOrDefault(a => Safe(() => a.CanOffer(sourceUrl, filePath)));
    }

    /// <summary>Run the input through the first matching resolver, or null if no plugin claims it.</summary>
    public Task<DownloadPlan> ResolveAsync(string url, CancellationToken cancellationToken)
        => ResolveAsync(url, options: null, cancellationToken);

    /// <summary>As above, forwarding per-request resolve options (e.g. a supplied cookie file) to the resolver.</summary>
    public async Task<DownloadPlan> ResolveAsync(string url, ResolveOptions options, CancellationToken cancellationToken)
    {
        var resolver = FindResolver(url);
        if (resolver == null)
            return null;
        return options == null
            ? await resolver.ResolveAsync(url, cancellationToken).ConfigureAwait(false)
            : await resolver.ResolveAsync(url, options, cancellationToken).ConfigureAwait(false);
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
            if (p.Descriptor.IsBuiltIn)
                return false; // built-ins ship with the app — disable them instead of removing
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

    /// <summary>True if a plugin with this id is currently loaded.</summary>
    public bool IsInstalled(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
            return false;
        lock (_gate) return _plugins.Any(p => p.Descriptor.Id == pluginId);
    }

    /// <summary>The loaded version of a plugin (from its descriptor), or null if not installed.</summary>
    public string InstalledVersion(string pluginId)
    {
        lock (_gate) return _plugins.FirstOrDefault(p => p.Descriptor.Id == pluginId)?.Descriptor.Version;
    }

    /// <summary>The external runtime binaries (e.g. ffmpeg, yt-dlp) a loaded plugin declares it needs, or
    /// empty if it doesn't implement <see cref="IHasRuntimeDependencies"/> or isn't loaded.</summary>
    public IReadOnlyList<PluginBinaryDependency> GetRuntimeDependencies(string pluginId)
    {
        LoadedPlugin p;
        lock (_gate) p = _plugins.FirstOrDefault(x => x.Descriptor.Id == pluginId);
        if (p?.Plugin is not IHasRuntimeDependencies hasDeps)
            return Array.Empty<PluginBinaryDependency>();
        try { return hasDeps.GetRequiredDependencies(DataDirectoryFor(pluginId)) ?? Array.Empty<PluginBinaryDependency>(); }
        catch (Exception ex)
        {
            AppLog.Error($"Plugin '{pluginId}' failed to report its runtime dependencies", ex);
            return Array.Empty<PluginBinaryDependency>();
        }
    }

    private static string DataDirectoryFor(string pluginId)
    {
        var dir = Path.Combine(PluginsRoot, "data", pluginId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Install an optional plugin from a downloaded package zip. The package's SHA-256 is verified against
    /// <paramref name="expectedSha256"/> <b>before</b> anything is extracted or loaded — a mismatch aborts
    /// with a friendly error and leaves the plugins folder untouched (never loads unverified code). On a
    /// match the zip is extracted into its own folder under <see cref="PluginsRoot"/> and loaded like any
    /// user-installed (non-built-in, removable) plugin. Pure of network I/O (caller downloads the zip), so
    /// it is unit-testable with a local file.
    /// </summary>
    public Task<PluginInstallResult> InstallFromZipAsync(string zipPath, string expectedSha256,
        string folderName, CancellationToken ct = default)
        => InstallFromZipAsync(zipPath, expectedSha256, folderName, PluginsRoot, ct);

    /// <summary>Install variant with an explicit plugins root (tests pass a temp dir so the real user
    /// plugins folder is never touched). The public overload uses <see cref="PluginsRoot"/>.</summary>
    internal async Task<PluginInstallResult> InstallFromZipAsync(string zipPath, string expectedSha256,
        string folderName, string pluginsRoot, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            return PluginInstallResult.Fail("The plugin download is missing.");
        if (string.IsNullOrWhiteSpace(expectedSha256))
            return PluginInstallResult.Fail("The plugin catalog entry has no checksum to verify against.");

        // --- Security gate: verify BEFORE touching the plugins folder ---
        string actual;
        try { actual = await ComputeSha256Async(zipPath, ct).ConfigureAwait(false); }
        catch (Exception ex) { return PluginInstallResult.Fail($"Could not read the download: {ex.Message}"); }
        if (!string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            return PluginInstallResult.Fail("The plugin download could not be verified (checksum mismatch). Please try again.");

        // --- Extract into a dedicated per-plugin folder under the plugins root ---
        var target = Path.Combine(pluginsRoot, SafeFolderName(folderName));
        try
        {
            Directory.CreateDirectory(pluginsRoot);
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
            Directory.CreateDirectory(target);
            ZipFile.ExtractToDirectory(zipPath, target, overwriteFiles: true);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Extracting plugin package to '{target}' failed", ex);
            return PluginInstallResult.Fail($"Could not unpack the plugin: {ex.Message}");
        }

        // --- Load it (a normal user-installed plugin: non-built-in, removable) ---
        var before = new HashSet<string>();
        lock (_gate) foreach (var p in _plugins) before.Add(p.Descriptor.Id);
        LoadFromDirectory(target);
        var added = Plugins.FirstOrDefault(p => !before.Contains(p.Id));
        if (added == null)
        {
            try { Directory.Delete(target, recursive: true); } catch { /* best-effort cleanup */ }
            return PluginInstallResult.Fail("The downloaded package contained no plugin.");
        }
        return PluginInstallResult.Ok(added);
    }

    /// <summary>Lowercase hex SHA-256 of a file.</summary>
    public static async Task<string> ComputeSha256Async(string path, CancellationToken ct = default)
    {
        await using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(fs, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string SafeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "plugin";
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
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
        public IDownloaderPlugin Plugin { get; }
        public List<ILinkResolver> Resolvers { get; } = new();
        public List<ITransferProvider> TransferProviders { get; } = new();
        public List<IPostProcessor> PostProcessors { get; } = new();
        public List<IPostDownloadAction> PostDownloadActions { get; } = new();
        public IPluginContext Context { get; set; }
        public AssemblyLoadContext Alc { get; }
        public string SourcePath { get; }

        public LoadedPlugin(IDownloaderPlugin plugin, AssemblyLoadContext alc, string sourcePath = null,
            bool isBuiltIn = false)
        {
            Plugin = plugin;
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
                IsBuiltIn = isBuiltIn,
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
        public void RegisterPostDownloadAction(IPostDownloadAction action) { if (action != null) _owner.PostDownloadActions.Add(action); }

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
