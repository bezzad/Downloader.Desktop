using System.Linq;
using System.Runtime.Versioning;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// The macOS system pickers (and their "New Folder" box) take Cmd+X/C/V/A only through Edit menu
/// items with these exact Cocoa selectors and keys. A wrong selector or key would leave paste broken.
/// Reading the table does not call the Objective-C runtime, so this runs on every OS.
/// </summary>
[SupportedOSPlatform("macos")]
public class MacEditMenuTests
{
    [Theory]
    [InlineData("cut:", "x")]
    [InlineData("copy:", "c")]
    [InlineData("paste:", "v")]
    [InlineData("selectAll:", "a")]
    public void Edit_menu_maps_the_standard_keys_to_the_cocoa_actions(string action, string key)
    {
        Assert.Contains(MacEditMenu.Items, i => i.Action == action && i.Key == key);
    }

    [Fact]
    public void Edit_menu_has_no_duplicate_keys()
    {
        Assert.Equal(MacEditMenu.Items.Length, MacEditMenu.Items.Select(i => i.Key).Distinct().Count());
    }
}
