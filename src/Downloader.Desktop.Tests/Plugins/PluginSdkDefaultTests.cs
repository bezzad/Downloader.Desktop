using Downloader.Desktop.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins;

/// <summary>
/// The SDK's default interface implementations. They exist so an older host — or a plugin's own test
/// double — keeps compiling when the surface grows, which means the default body is the code that runs
/// for every context that predates the addition. The app's own context overrides it, so nothing in the
/// suite had ever executed the fallback.
/// </summary>
public class PluginSdkDefaultTests
{
    /// <summary>A context written before post-download actions existed must accept the call and ignore it.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_context_that_predates_post_download_actions_ignores_the_registration()
    {
        IPluginContext context = new LegacyContext();

        var ex = Record.Exception(() => context.RegisterPostDownloadAction(null));

        Assert.Null(ex);
    }

    /// <summary>Implements only what the interface required before <c>RegisterPostDownloadAction</c> was added.</summary>
    private sealed class LegacyContext : IPluginContext
    {
        public void RegisterResolver(ILinkResolver resolver) { }
        public void RegisterTransferProvider(ITransferProvider provider) { }
        public void RegisterPostProcessor(IPostProcessor processor) { }
        public string DataDirectory => ".";
        public ILogger Logger => NullLogger.Instance;
    }
}
