using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Downloader.Desktop.Plugins;

/// <summary>
/// The entry point every plugin implements. The host discovers implementations in a plugin assembly,
/// instantiates them (parameterless ctor), and calls <see cref="Initialize"/> once to let the plugin
/// register its contributions (resolvers / transfer providers / post-processors).
/// </summary>
public interface IDownloaderPlugin
{
    /// <summary>Stable unique id, e.g. "com.bezzad.hls". Used to persist enabled/disabled state.</summary>
    string Id { get; }

    string Name { get; }
    string Version { get; }
    string Author { get; }
    string Description { get; }

    /// <summary>Register this plugin's contributions with the host.</summary>
    void Initialize(IPluginContext context);
}

/// <summary>What a plugin is given on <see cref="IDownloaderPlugin.Initialize"/> to register its parts.</summary>
public interface IPluginContext
{
    void RegisterResolver(ILinkResolver resolver);
    void RegisterTransferProvider(ITransferProvider provider);
    void RegisterPostProcessor(IPostProcessor processor);

    /// <summary>Registers a user-initiated action offered on completed downloads this plugin resolved
    /// (e.g. "Add to Ollama"). Default no-op so existing hosts/fakes keep compiling (additive API).</summary>
    void RegisterPostDownloadAction(IPostDownloadAction action) { }

    /// <summary>A per-plugin writable directory (e.g. for a plugin to download ffmpeg into).</summary>
    string DataDirectory { get; }

    /// <summary>The standard .NET logger for this plugin — writes into the app's log. Use
    /// <c>Logger.LogInformation/LogWarning/LogError(...)</c> (Microsoft.Extensions.Logging), the same
    /// logging contract the Downloader engine and the app use.</summary>
    ILogger Logger { get; }

    /// <summary>
    /// An <see cref="HttpClient"/> that honours the proxy the user set in the app's Settings — use it
    /// instead of <c>new HttpClient()</c> and a plugin needs no proxy code, no proxy setting and no
    /// proxy knowledge of its own. The proxy is read PER REQUEST, so changing it in Settings applies
    /// to clients that already exist: build one in <c>Initialize</c> and keep it for the plugin's life.
    /// <para>The default is a plain client, so a plugin built against an older host still works and a
    /// host that predates this member still compiles.</para>
    /// </summary>
    HttpClient CreateHttpClient() => new();

    /// <summary>
    /// The proxy address configured in the app, e.g. <c>socks5://127.0.0.1:1080</c> or
    /// <c>http://host:port</c>; null or empty when the user set none. Only needed for a TOOL the
    /// plugin spawns (yt-dlp's <c>--proxy</c>); for the plugin's own HTTP use
    /// <see cref="CreateHttpClient"/>, which applies it already.
    /// </summary>
    string? ProxyAddress => null;
}
