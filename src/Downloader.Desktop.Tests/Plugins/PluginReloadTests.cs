using System;
using System.IO;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins;

/// <summary>
/// Regression tests for the plugin update swap: the .NET runtime caches loaded assembly images by FILE
/// PATH, so after an update replaces a plugin DLL in place, loading "the same path" again used to return
/// the OLD assembly (bit the v2.0.0 HLS 1.1.2→1.3.0 update — the new DLL was downloaded, verified and
/// extracted, yet the app kept reporting the old version). PluginLoadContext now loads via LoadFromStream,
/// which bypasses that cache. These tests replace the file content at a fixed path and assert the NEW
/// content is what actually loads.
/// </summary>
public class PluginReloadTests
{
    // Two real plugin DLLs with DIFFERENT ids, both built into the test output.
    private static string OllamaDll => Path.Combine(AppContext.BaseDirectory, "Downloader.Desktop.Plugins.Ollama.dll");
    private static string HlsDll => Path.Combine(AppContext.BaseDirectory, "Downloader.Desktop.Plugins.Hls.dll");
    private const string OllamaId = "com.bezzad.ollama-models";
    private const string HlsId = "com.bezzad.hls";

    [Fact]
    public void Replacing_a_plugin_file_in_place_loads_the_new_content()
    {
        Assert.True(File.Exists(OllamaDll), $"missing test asset {OllamaDll}");
        Assert.True(File.Exists(HlsDll), $"missing test asset {HlsDll}");

        var root = Directory.CreateTempSubdirectory("plugswap").FullName;
        try
        {
            // "Install" plugin A at a fixed path and load it.
            var dir = Path.Combine(root, "swap-target");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "Plugin.dll");
            File.Copy(OllamaDll, path);

            var mgr = new PluginManager();
            mgr.LoadFromDirectory(root);
            Assert.True(mgr.IsInstalled(OllamaId), "initial plugin did not load");

            // Update swap: remove it, then put a DIFFERENT assembly at the SAME path (what
            // PluginCatalogService.InstallOrUpdateAsync + InstallFromZipAsync do).
            Assert.True(mgr.RemovePlugin(OllamaId));
            File.Copy(HlsDll, path, overwrite: true);
            mgr.LoadFromDirectory(root);

            // With the by-path image cache this loaded the OLD assembly again (Ollama); the stream
            // loader must see the file's current bytes.
            Assert.True(mgr.IsInstalled(HlsId), "replaced file did not load its new content (stale by-path assembly cache)");
            Assert.False(mgr.IsInstalled(OllamaId), "old plugin came back after its file was replaced");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* best-effort */ }
        }
    }
}
