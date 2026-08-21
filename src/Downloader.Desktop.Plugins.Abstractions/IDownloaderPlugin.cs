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
}
