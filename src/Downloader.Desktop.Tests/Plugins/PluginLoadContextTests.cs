using System;
using System.IO;
using System.Reflection;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins;

/// <summary>
/// A plugin's load context is collectible, so it can be unloaded while something still holds code from
/// it — a background task the plugin started, a finalizer, or a coverage tracker's exit hook. Those late
/// callers still ask the context to resolve a dependency, and an unloading context cannot load one.
///
/// Answering "not mine" (null) is the only safe reply: the runtime then serves the assembly from the
/// default context, which is where every framework assembly already lives. Throwing instead escapes as an
/// unhandled exception on whichever thread is running the unload — there is no user code above it to
/// catch it — and takes the whole process down with it.
///
/// This is not hypothetical. It is what has been failing CI: the coverage collector instruments the HLS
/// plugin, its tracker's module-unload hook resolves <c>System.Threading.Thread</c> through a collectible
/// context, and the throw killed the test host mid-run — which vstest reports as "Test host process
/// crashed", blaming whichever test happened to be in flight. Hence a different innocent test named
/// every time.
/// </summary>
public class PluginLoadContextTests
{
    /// <summary>A real plugin DLL, so the context has a genuine dependency graph to resolve against.</summary>
    private static string PluginDll =>
        Path.Combine(AppContext.BaseDirectory, "Downloader.Desktop.Plugins.Hls.dll");

    /// <summary>A name this context's resolver maps to a file — so resolution reaches the load call,
    /// which is the only place the throw can come from. A name it cannot map returns null long before
    /// that and would prove nothing.</summary>
    private static AssemblyName Resolvable => new("Downloader.Desktop.Plugins.Hls");

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_unloading_context_defers_resolution_instead_of_killing_the_process()
    {
        Assert.True(File.Exists(PluginDll), $"plugin DLL not found at {PluginDll}");

        var context = new PluginManager.PluginLoadContext(PluginDll);
        context.LoadPluginAssembly(PluginDll);
        context.Unload();

        Assert.Null(context.ResolveDependency(Resolvable));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_live_context_still_resolves_its_own_dependencies()
    {
        // The guard must not become "never resolve anything" — that would silently break plugin loading
        // instead of crashing it, which is worse.
        var context = new PluginManager.PluginLoadContext(PluginDll);
        try
        {
            Assert.NotNull(context.ResolveDependency(Resolvable));
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_shared_sdk_always_comes_from_the_host()
    {
        // Unchanged rule, pinned here because the guard sits on the same path: the SDK must resolve from
        // the default context so plugin types satisfy `is IDownloaderPlugin` against the host's copy.
        var context = new PluginManager.PluginLoadContext(PluginDll);
        try
        {
            Assert.Null(context.ResolveDependency(
                new AssemblyName("Downloader.Desktop.Plugins.Abstractions")));
        }
        finally
        {
            context.Unload();
        }
    }
}
