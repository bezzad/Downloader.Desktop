using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Downloader.Desktop.Tests;

/// <summary>
/// Guards the built-in vs. optional plugin boundary (consolidate-official-plugins change): the optional
/// HLS plugin lives in the same solution but must NEVER be referenced by, or staged into, the main app —
/// it ships only as a downloadable release asset. If a future edit adds a ProjectReference or extends the
/// StageBundledPlugins allow-list to include it, these tests fail loudly instead of the plugin silently
/// bloating every install.
/// </summary>
public class PluginIsolationTests
{
    private const string OptionalPluginAssembly = "Downloader.Desktop.Plugins.Hls";

    [Fact]
    public void App_csproj_never_references_the_optional_Hls_plugin()
    {
        var csproj = FindRepoFile(Path.Combine("Downloader.Desktop", "Downloader.Desktop.csproj"));
        Assert.True(File.Exists(csproj), $"app csproj not found (looked at {csproj})");

        // Strip XML comments first: the csproj deliberately NAMES the optional plugin in explanatory
        // comments (to warn future editors); only live markup — a <ProjectReference> or a
        // StageBundledPlugins glob — counts as a reference.
        var markup = System.Text.RegularExpressions.Regex.Replace(
            File.ReadAllText(csproj), "<!--.*?-->", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.False(markup.Contains(OptionalPluginAssembly, StringComparison.OrdinalIgnoreCase),
            $"{OptionalPluginAssembly} must not be referenced or staged by the app csproj — optional plugins ship as release assets, never bundled.");
    }

    [Fact]
    public void Built_in_plugins_are_staged_but_the_optional_plugin_is_not()
    {
        // Best-effort: only assert when the app's staged plugins/ folder is present (i.e. the app was
        // built into a discoverable output). Skips cleanly on a tests-only build.
        var pluginsDir = FindAppStagedPluginsDir();
        if (pluginsDir is null)
            return;

        var dlls = Directory.GetFiles(pluginsDir, "*.dll").Select(Path.GetFileName).ToList();
        Assert.Contains("Downloader.Desktop.Plugins.GitHub.dll", dlls);
        Assert.Contains("Downloader.Desktop.Plugins.Ollama.dll", dlls);
        Assert.DoesNotContain(dlls, f => f!.Contains(OptionalPluginAssembly, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Walk up from the test output to the repo's <c>src</c> dir and resolve a file under it.</summary>
    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return relative; // not found; caller asserts existence
    }

    /// <summary>Locate the app's staged <c>plugins/</c> folder (RID/config-agnostic), or null if absent.</summary>
    private static string FindAppStagedPluginsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var appBin = Path.Combine(dir.FullName, "Downloader.Desktop", "bin");
            if (Directory.Exists(appBin))
            {
                return Directory.EnumerateDirectories(appBin, "plugins", SearchOption.AllDirectories)
                    .FirstOrDefault();
            }
            dir = dir.Parent;
        }
        return null;
    }
}
