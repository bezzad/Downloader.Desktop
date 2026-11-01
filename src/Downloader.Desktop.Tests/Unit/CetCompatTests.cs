using System;
using System.IO;
using System.Xml.Linq;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// Issue #20: on Windows 11 21H2 the app did not start at all. The runtime stopped with
/// "Your Windows doesn't fully support CET" (exit code 0x80131506), because .NET marks the exe as
/// CET compatible by default. The app project must turn that mark off.
/// </summary>
public class CetCompatTests
{
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void App_exe_is_not_marked_cet_compatible()
    {
        var csproj = XDocument.Load(FindRepoFile(Path.Combine("src", "Downloader.Desktop", "Downloader.Desktop.csproj")));

        var cet = csproj.Root!.Element("PropertyGroup")?.Element("CETCompat");

        Assert.NotNull(cet);
        Assert.Null(cet.Attribute("Condition")); // must apply to every build, not only some
        Assert.Equal("false", cet.Value.Trim(), ignoreCase: true);
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
