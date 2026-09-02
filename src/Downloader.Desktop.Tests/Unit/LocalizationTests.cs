using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>i18n tests — run under the Avalonia headless runtime because translations load via AssetLoader.</summary>
public class LocalizationTests
{
    [AvaloniaTheory(Timeout = TestTimeouts.DefaultMs)]
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
            // The message shown when the app could not take over a download the browser is still
            // fetching (issue #9) — a pack missing it would tell the user to paste a fresh link in
            // English, for a file they have not lost.
            Assert.NotEqual("Error_BrowserHandoffRefused", Localizer.Instance["Error_BrowserHandoffRefused"]);
            // What a site that only serves a signed-in session is told to do. A pack missing it would fall
            // back to English for the one message whose old wording ("sign in") was actively misleading.
            Assert.NotEqual("Error_SiteNeedsBrowserSession", Localizer.Instance["Error_SiteNeedsBrowserSession"]);
            // What a server refusing several connections at once is told to the user — the failure that
            // used to be reported as an expired link, sending people after a link they had not lost.
            Assert.NotEqual("Error_ServerRefusedConnections", Localizer.Instance["Error_ServerRefusedConnections"]);
            // A download that finished without producing a file — a green row over an empty folder is the
            // one outcome worse than an honest failure, so its wording must exist everywhere.
            Assert.NotEqual("Error_NothingDownloaded", Localizer.Instance["Error_NothingDownloaded"]);
            // And a download the app had to end itself because nothing was ever going to end it.
            Assert.NotEqual("Error_DownloadStalled", Localizer.Instance["Error_DownloadStalled"]);
        }
        finally
        {
            Localizer.Instance.Load("en");
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Rtl_languages_flip_flow_direction()
    {
        Localizer.Instance.Load("fa");
        Assert.Equal(FlowDirection.RightToLeft, Localizer.Instance.FlowDirection);
        Localizer.Instance.Load("ar");
        Assert.Equal(FlowDirection.RightToLeft, Localizer.Instance.FlowDirection);
        Localizer.Instance.Load("en");
        Assert.Equal(FlowDirection.LeftToRight, Localizer.Instance.FlowDirection);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Unknown_key_falls_back_to_itself()
    {
        Localizer.Instance.Load("en");
        Assert.Equal("__nope__", Localizer.Instance["__nope__"]);
    }
}

/// <summary>
/// The install-extension dialog's strings, in every pack.
///
/// This dialog is the one place the app has to EXPLAIN something — the steps to add an extension to a
/// browser, and what that way of adding it costs. A missing translation there does not degrade
/// gracefully: it shows a user instructions they cannot read, or a raw key.
/// </summary>
public class ExtensionInstallLocalizationTests
{
    private static readonly string[] Languages =
    {
        "en", "fa", "es", "fr", "ar", "eo", "tr", "az", "de", "it", "pt", "ru", "hi", "zh", "ja", "ko",
    };

    private static readonly string[] Keys =
    {
        "Ext_Install_Button", "Ext_Install_Hint", "Ext_Install_Title", "Ext_Browsers",
        "Ext_NotConnected", "Ext_Connected", "Ext_UpdateAvailable",
        "Ext_Family_Chromium", "Ext_Family_Gecko",
        "Ext_Steps_Chromium_1", "Ext_Steps_Chromium_2", "Ext_Steps_Chromium_3",
        "Ext_Steps_Gecko_1", "Ext_Steps_Gecko_2", "Ext_Steps_Gecko_3",
        "Ext_Limits_Chromium", "Ext_Limits_Gecko",
        "Ext_NoBrowsers", "Ext_NoBuild", "Ext_PickABrowser", "Ext_UsingBundled",
        "Ext_Install", "Ext_OpenStore", "Ext_CopyPath", "Ext_OpenFolder", "Ext_FolderLabel",
        "Ext_StepsTitle", "Ext_Refresh", "Ext_Cancel",
    };

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Every_pack_carries_every_key()
    {
        var missing = new System.Collections.Generic.List<string>();
        foreach (var lang in Languages)
        {
            Localizer.Instance.Load(lang);
            foreach (var key in Keys)
            {
                var value = Localizer.Instance[key];
                // A missing key falls back to itself, so the key coming back IS the failure signal.
                if (string.IsNullOrWhiteSpace(value) || value == key)
                    missing.Add($"{lang}:{key}");
            }
        }
        Localizer.Instance.Load("en");

        Assert.True(missing.Count == 0, "Untranslated install-extension strings: " + string.Join(", ", missing));
    }

    /// <summary>The two format strings are what tell the user which version they have and which is
    /// available — a pack that drops a placeholder silently loses that.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_version_strings_keep_their_placeholders()
    {
        var broken = new System.Collections.Generic.List<string>();
        foreach (var lang in Languages)
        {
            Localizer.Instance.Load(lang);
            if (!Localizer.Instance["Ext_Connected"].Contains("{0}"))
                broken.Add($"{lang}:Ext_Connected");
            var update = Localizer.Instance["Ext_UpdateAvailable"];
            if (!update.Contains("{0}") || !update.Contains("{1}"))
                broken.Add($"{lang}:Ext_UpdateAvailable");
        }
        Localizer.Instance.Load("en");

        Assert.True(broken.Count == 0, "Version strings missing a placeholder: " + string.Join(", ", broken));
    }

    /// <summary>
    /// Every pack must actually name the browser page the user has to open — those are literal addresses,
    /// so a translation that paraphrases them away leaves the steps unfollowable.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_steps_keep_the_literal_browser_addresses()
    {
        var broken = new System.Collections.Generic.List<string>();
        foreach (var lang in Languages)
        {
            Localizer.Instance.Load(lang);
            if (!Localizer.Instance["Ext_Steps_Chromium_1"].Contains("chrome://extensions"))
                broken.Add($"{lang}:Ext_Steps_Chromium_1");
            if (!Localizer.Instance["Ext_Steps_Gecko_1"].Contains("about:debugging"))
                broken.Add($"{lang}:Ext_Steps_Gecko_1");
            if (!Localizer.Instance["Ext_Steps_Gecko_3"].Contains("manifest.json"))
                broken.Add($"{lang}:Ext_Steps_Gecko_3");
        }
        Localizer.Instance.Load("en");

        Assert.True(broken.Count == 0, "Steps missing a literal address: " + string.Join(", ", broken));
    }
}
