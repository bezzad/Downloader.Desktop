using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// Guards the AUR package files (packaging/aur, task #5): the PKGBUILD/.SRCINFO must stay
/// publish-ready — a real semver pkgver, a real tarball sha256 (never a placeholder), and the two
/// files in lockstep (release.sh rewrites both; a drift means a broken `yay -S downloader-bin`).
/// </summary>
public class AurPackagingTests
{
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Pkgbuild_and_srcinfo_are_publish_ready_and_in_lockstep()
    {
        var pkgbuild = File.ReadAllText(FindRepoFile(Path.Combine("packaging", "aur", "PKGBUILD")));
        var srcinfo = File.ReadAllText(FindRepoFile(Path.Combine("packaging", "aur", ".SRCINFO")));

        var pkgVer = Regex.Match(pkgbuild, @"^pkgver=([0-9]+\.[0-9]+\.[0-9]+)$", RegexOptions.Multiline);
        Assert.True(pkgVer.Success, "PKGBUILD must declare a semver pkgver");

        var sha = Regex.Match(pkgbuild, @"sha256sums=\('([a-f0-9]{64})'", RegexOptions.Multiline);
        Assert.True(sha.Success, "PKGBUILD's first sha256 must be a real 64-hex digest, not a placeholder");

        Assert.Contains($"pkgver = {pkgVer.Groups[1].Value}", srcinfo);
        Assert.Contains($"sha256sums = {sha.Groups[1].Value}", srcinfo);
        Assert.Contains($"/v{pkgVer.Groups[1].Value}/", srcinfo); // source URLs pinned to the same tag
        Assert.Contains("pkgname = downloader-bin", srcinfo);
    }

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
        throw new FileNotFoundException(relative);
    }
}
