using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests;

/// <summary>i18n tests — run under the Avalonia headless runtime because translations load via AssetLoader.</summary>
public class LocalizationTests
{
    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("fa")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("ar")]
    [InlineData("eo")]
    [InlineData("tr")]
    [InlineData("az")]
    [InlineData("de")]
    [InlineData("it")]
    [InlineData("pt")]
    [InlineData("ru")]
    [InlineData("hi")]
    [InlineData("zh")]
    [InlineData("ja")]
    [InlineData("ko")]
    public void Each_language_resolves_core_keys(string lang)
    {
        Localizer.Instance.Load(lang);
        try
        {
            // A resolved key returns its translation, never the raw key.
            Assert.NotEqual("Top_Add", Localizer.Instance["Top_Add"]);
            Assert.NotEqual("Nav_Settings", Localizer.Instance["Nav_Settings"]);
            Assert.False(string.IsNullOrWhiteSpace(Localizer.Instance["Settings_Title"]));
            // Round-15 keys must resolve in every shipped language (new ones are full packs).
            Assert.NotEqual("Action_StopAll", Localizer.Instance["Action_StopAll"]);
            Assert.NotEqual("Set_ShutdownOnComplete", Localizer.Instance["Set_ShutdownOnComplete"]);
        }
        finally
        {
            Localizer.Instance.Load("en");
        }
    }

    [AvaloniaFact]
    public void Rtl_languages_flip_flow_direction()
    {
        Localizer.Instance.Load("fa");
        Assert.Equal(FlowDirection.RightToLeft, Localizer.Instance.FlowDirection);
        Localizer.Instance.Load("ar");
        Assert.Equal(FlowDirection.RightToLeft, Localizer.Instance.FlowDirection);
        Localizer.Instance.Load("en");
        Assert.Equal(FlowDirection.LeftToRight, Localizer.Instance.FlowDirection);
    }

    [AvaloniaFact]
    public void Unknown_key_falls_back_to_itself()
    {
        Localizer.Instance.Load("en");
        Assert.Equal("__nope__", Localizer.Instance["__nope__"]);
    }
}
