using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// The accent as a plain hex string. The browser extension paints itself with this over
/// <c>/api/settings</c>, so a key that silently resolved to the wrong colour would leave the popup
/// wearing a colour the app is not — the exact drift this replaced.
/// </summary>
public class AccentHexTests
{
    [Theory]
    [InlineData("Teal", "#16A4C2")]
    [InlineData("Blue", "#2F7DE1")]
    [InlineData("Purple", "#8A60E6")]
    [InlineData("Green", "#2BA86B")]
    [InlineData("Amber", "#E2922E")]
    public void Every_offered_accent_has_its_own_hex(string key, string expected) =>
        Assert.Equal(expected, ThemeService.HexOf(key));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Chartreuse")]
    public void An_unknown_accent_falls_back_to_the_default_one(string? key) =>
        Assert.Equal(ThemeService.HexOf("Teal"), ThemeService.HexOf(key));
}
