using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins;

/// <summary>
/// A catalog plugin must still start on an app older than the SDK it was built against.
/// <para>
/// v2.13.0 added <c>IPluginContext.CreateHttpClient()</c> and every catalog plugin began calling it. An app
/// older than that has no such member, so <c>Initialize</c> threw <c>MissingMethodException</c>: the app
/// reported "The downloaded package contained no plugin", and an Update removed the working copy first —
/// the plugin was simply gone. The default interface implementation does not help here: it lives in the
/// NEW SDK, and an old app loads its OWN.
/// </para>
/// These tests load each plugin against the real SDK assembly shipped in v2.12.0
/// (<c>Fixtures/OldSdk/v2.12.0</c>, taken from that release's macOS bundle) — the only honest stand-in
/// for "an app that predates this member".
/// </summary>
public class OldHostCompatibilityTests
{
    private const string SdkName = "Downloader.Desktop.Plugins.Abstractions";

    private static readonly string OldSdkPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "OldSdk", "v2.12.0", SdkName + ".dll");

    private static readonly string[] CatalogPluginNames =
    {
        "Downloader.Desktop.Plugins.Hls",
        "Downloader.Desktop.Plugins.Website",
        "Downloader.Desktop.Plugins.SiteMedia",
    };

    public static TheoryData<string> CatalogPlugins => new(CatalogPluginNames);

    /// <summary>ONE context for the whole class, so the old SDK is loaded exactly once: DispatchProxy reuses
    /// its generated proxy type across calls, and a second copy of the SDK makes that type implement the
    /// FIRST copy's IPluginContext — "generatedProxy cannot be converted to IPluginContext".</summary>
    private static readonly OldHostContext OldHost = new();

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_fixture_really_is_an_sdk_without_the_newer_members()
    {
        var sdk = OldHost.LoadSdk();
        var context = sdk.GetType("Downloader.Desktop.Plugins.IPluginContext")!;
        // If this ever holds the member, the tests below prove nothing.
        Assert.Null(context.GetMethod("CreateHttpClient"));
        Assert.Null(context.GetProperty("ProxyAddress"));
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [MemberData(nameof(CatalogPlugins))]
    public void A_catalog_plugin_initializes_on_an_app_that_predates_the_newest_sdk_members(string assembly)
    {
        var sdk = OldHost.LoadSdk();
        var pluginAssembly = OldHost.LoadFromAssemblyName(new AssemblyName(assembly));
        var pluginInterface = sdk.GetType("Downloader.Desktop.Plugins.IDownloaderPlugin")!;
        var contextInterface = sdk.GetType("Downloader.Desktop.Plugins.IPluginContext")!;

        var pluginType = pluginAssembly.GetTypes().Single(t => pluginInterface.IsAssignableFrom(t) && !t.IsAbstract);
        var plugin = Activator.CreateInstance(pluginType)!;
        var context = (OldContext)DispatchProxy.Create(contextInterface, typeof(OldContext));
        context.DataDirectory = Path.Combine(Path.GetTempPath(), "dldesktop-oldhost-" + Guid.NewGuid().ToString("N"));

        try
        {
            var error = Record.Exception(() => pluginInterface.GetMethod("Initialize")!.Invoke(plugin, new object[] { context }));
            var cause = (error as TargetInvocationException)?.InnerException ?? error;
            Assert.True(cause == null, $"{assembly} failed to initialize on the v2.12.0 SDK: {cause}");
            Assert.NotEmpty(context.Registered);
        }
        finally
        {
            try { Directory.Delete(context.DataDirectory, recursive: true); } catch { /* may never have been created */ }
        }
    }

    /// <summary>Resolves the SDK to the OLD assembly and the plugin to its current build; everything else
    /// (logging abstractions, the BCL) comes from the default context. Deliberately NOT collectible — an
    /// unloading context under coverage has crashed this test host before (see SKILL.md).</summary>
    private sealed class OldHostContext() : AssemblyLoadContext("old-host-v2.12.0", isCollectible: false)
    {
        public Assembly LoadSdk() => LoadFromAssemblyName(new AssemblyName(SdkName));

        protected override Assembly Load(AssemblyName name)
        {
            // Called once per name: the context remembers what it returned.
            if (name.Name == SdkName)
                return LoadFromStream(new MemoryStream(File.ReadAllBytes(OldSdkPath)));
            if (CatalogPluginNames.Contains(name.Name))
                return LoadFromStream(new MemoryStream(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name.Name + ".dll"))));
            return null;
        }
    }

    /// <summary>A stand-in host context implementing the OLD <c>IPluginContext</c>, whose type exists only
    /// inside <see cref="OldHostContext"/> — which is why it is a proxy rather than a class.</summary>
    public class OldContext : DispatchProxy
    {
        public string DataDirectory { get; set; }
        public List<object> Registered { get; } = new();

        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            switch (targetMethod!.Name)
            {
                case "get_Logger": return NullLogger.Instance;
                case "get_DataDirectory": return DataDirectory;
            }
            if (targetMethod.Name.StartsWith("Register", StringComparison.Ordinal))
            {
                Registered.Add(args![0]);
                return null;
            }
            var ret = targetMethod.ReturnType;
            return ret.IsValueType && ret != typeof(void) ? Activator.CreateInstance(ret) : null;
        }
    }
}
