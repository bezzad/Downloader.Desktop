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
    void RegisterResolver(IMediaResolver resolver);
    void RegisterTransferProvider(ITransferProvider provider);
    void RegisterPostProcessor(IPostProcessor processor);

    /// <summary>A per-plugin writable directory (e.g. for a plugin to download yt-dlp/ffmpeg into).</summary>
    string DataDirectory { get; }

    /// <summary>Write a line to the app log (prefixed with the plugin name).</summary>
    void Log(string message);
}
