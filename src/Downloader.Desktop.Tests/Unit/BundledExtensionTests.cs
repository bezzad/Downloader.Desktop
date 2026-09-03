using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// The copy of the extension that ships inside the app.
///
/// <para><b>Why it exists:</b> the installer reads its build from <c>extension-catalog.json</c> on the
/// latest GitHub release — an asset that only appears in the release which SHIPS this feature. Every
/// release published before it carries no catalog at all, so without a bundled floor the installer finds
/// nothing, installs nothing, and the folder button has nothing to open. That is exactly what the author
/// hit. The catalog still wins when reachable, so the extension keeps updating independently of the app.</para>
/// </summary>
public class BundledExtensionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dl-bundled-" + Guid.NewGuid().ToString("N")[..8]);

    public BundledExtensionTests() => ExtensionInstallService.InstallRootOverride = _root;

    public void Dispose()
    {
        ExtensionInstallService.InstallRootOverride = null;   // process-wide
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>
    /// The two sides of the "which version is installed in which browser?" answer have to speak the same
    /// vocabulary: the extension reports a browser label, and the app looks that label up among its own
    /// browser ids. When they drift, a browser's row silently stays empty forever — which is what a Brave
    /// or Vivaldi user saw while the extension could only ever say "chrome". Read from the BUNDLED copy,
    /// so this is the code the app actually ships.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Every_browser_the_extension_can_report_is_a_browser_the_app_lists()
    {
        var result = ExtensionInstallService.InstallBundled("chrome", gecko: false);
        Assert.True(result.Success, result.Error);

        var common = File.ReadAllText(Path.Combine(result.Path, "common.js"));
        var body = Regex.Match(common, @"function labelFromUserAgent\([^)]*\)\s*\{(.*?)\n\}", RegexOptions.Singleline);
        Assert.True(body.Success, "labelFromUserAgent is gone from the bundled extension");

        var labels = Regex.Matches(body.Groups[1].Value, @"return\s+(?:/[^/]+/i\.test\([^)]*\)\s*\?\s*)?""([a-z]+)""(?:\s*:\s*""([a-z]+)"")?")
            .SelectMany(m => new[] { m.Groups[1].Value, m.Groups[2].Value })
            .Where(v => !string.IsNullOrEmpty(v))
            .Distinct()
            .ToList();

        Assert.NotEmpty(labels);
        var ids = BrowserDetector.Supported.Select(c => c.Id).ToList();
        foreach (var label in labels)
            Assert.Contains(label, ids);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_bundled_copy_installs_without_a_catalog_or_a_network()
    {
        var result = ExtensionInstallService.InstallBundled("chrome", gecko: false);

        Assert.True(result.Success, result.Error);
        // A browser refuses the whole extension if a file a manifest names is missing, so every packaged
        // file has to be there — this is the check that would have caught a half-bundled install.
        Assert.True(File.Exists(Path.Combine(result.Path, "manifest.json")));
        foreach (var file in ExtensionInstallService.BundledFiles)
            Assert.True(File.Exists(Path.Combine(result.Path, file.Replace('/', Path.DirectorySeparatorChar))),
                $"bundled install is missing {file}");
        Assert.False(string.IsNullOrWhiteSpace(result.Version));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_gecko_browser_gets_its_own_manifest_under_the_expected_name()
    {
        var result = ExtensionInstallService.InstallBundled("firefox", gecko: true);

        Assert.True(result.Success, result.Error);
        var manifest = File.ReadAllText(Path.Combine(result.Path, "manifest.json"));
        // The Firefox build is the one with the gecko id; shipping the Chrome manifest here would produce
        // an add-on Firefox will not load.
        Assert.Contains("gecko", manifest, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(result.Path, "manifest.firefox.json")));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Reinstalling_replaces_the_previous_copy_in_place()
    {
        var first = ExtensionInstallService.InstallBundled("chrome", gecko: false);
        File.WriteAllText(Path.Combine(first.Path, "stale.js"), "left over");

        var second = ExtensionInstallService.InstallBundled("chrome", gecko: false);

        Assert.Equal(first.Path, second.Path);                                  // the path must not move
        Assert.False(File.Exists(Path.Combine(second.Path, "stale.js")));       // and must not accumulate
        Assert.False(Directory.Exists(second.Path + ".new"));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_bundled_version_matches_what_gets_installed()
    {
        var installed = ExtensionInstallService.InstallBundled("chrome", gecko: false);

        Assert.Equal(ExtensionInstallService.BundledVersion(), installed.Version);
        Assert.Matches(@"^\d+\.\d+\.\d+$", installed.Version);
    }

    /// <summary>
    /// The bundled file list and the release zip's file list must stay identical. A file in one and not
    /// the other means the two ways of installing produce different extensions — and the missing-file
    /// case is one a browser rejects outright.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_bundled_file_list_matches_the_release_packaging_script()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "scripts", "build-extension.sh"));
        var common = Regex.Match(script, @"^COMMON=\((?<list>[^)]*)\)", RegexOptions.Multiline);
        Assert.True(common.Success, "COMMON=(...) not found in scripts/build-extension.sh");

        var packaged = common.Groups["list"].Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToList();

        foreach (var entry in packaged)
        {
            if (entry == "icons")
            {
                // The script ships the whole folder; the bundle lists each icon explicitly.
                Assert.Contains(ExtensionInstallService.BundledFiles, f => f.StartsWith("icons/"));
                continue;
            }
            Assert.Contains(entry, ExtensionInstallService.BundledFiles);
        }

        foreach (var bundled in ExtensionInstallService.BundledFiles.Where(f => !f.StartsWith("icons/")))
            Assert.Contains(bundled, packaged);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "scripts")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root not found");
    }
}
