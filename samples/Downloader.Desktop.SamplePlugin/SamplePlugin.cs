using Downloader.Desktop.Plugins;

namespace Downloader.Desktop.SamplePlugin;

/// <summary>
/// Minimal example plugin. Registers a resolver that claims "sample://" inputs and turns them into a
/// single direct download (the core engine then downloads it). Real plugins (HLS, torrent) follow the
/// same shape — implement <see cref="IDownloaderPlugin"/>, register contributions in Initialize.
/// </summary>
public sealed class SamplePlugin : IDownloaderPlugin
{
    public string Id => "com.bezzad.sample";
    public string Name => "Sample Plugin";
    public string Version => "1.0.0";
    public string Author => "bezzad";
    public string Description => "Example plugin that resolves sample:// links to a direct download.";

    public void Initialize(IPluginContext context)
    {
        context.Log("Sample plugin initialized");
        context.RegisterResolver(new SampleResolver());
    }

    private sealed class SampleResolver : IMediaResolver
    {
        public bool CanResolve(string url) =>
            url.StartsWith("sample://", System.StringComparison.OrdinalIgnoreCase);

        public Task<DownloadPlan> ResolveAsync(string url, CancellationToken cancellationToken) =>
            Task.FromResult(new DownloadPlan
            {
                SuggestedFileName = "sample.bin",
                Parts = new[]
                {
                    new MediaPart
                    {
                        Url = url.Replace("sample://", "https://", System.StringComparison.OrdinalIgnoreCase),
                        Kind = PartKind.Combined,
                    },
                },
                PostProcess = PostProcess.None,
            });
    }
}
