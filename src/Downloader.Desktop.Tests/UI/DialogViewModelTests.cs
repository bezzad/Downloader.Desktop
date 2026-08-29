using System;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The four small dialog view models (About / Donate / Shutdown / Update-prompt). They were at 0%
/// coverage: each is reachable only through a modal window, so nothing exercised them.
///
/// These are <see cref="AvaloniaFactAttribute"/>s, not plain Facts, because every one of them reads
/// <see cref="Localizer"/> — the i18n maps only load under the Avalonia headless runtime (AssetLoader),
/// and a plain Fact gets the raw key back and is order-dependent (the intermittent macOS CI failures
/// documented in SKILL.md). Each one loads "en" first for the same reason.
/// </summary>
public class DialogViewModelTests
{
    // ---- AboutViewModel ----------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void About_reports_the_running_version_and_the_brand_name()
    {
        Localizer.Instance.Load("en");
        var vm = new AboutViewModel();

        Assert.Equal("Downloader", vm.AppName);

        // The About card must agree with the update check — a mismatch here is what made every patch
        // release look "newer forever" (SKILL.md, #update-false-alarm).
        Assert.Contains(UpdateService.CurrentVersion.ToString(), vm.VersionText);
        Assert.DoesNotContain("About_Version", vm.VersionText); // i18n key actually resolved
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void About_exposes_every_command_and_canonical_link()
    {
        Localizer.Instance.Load("en");
        var vm = new AboutViewModel();

        Assert.NotNull(vm.OpenRepoCommand);
        Assert.NotNull(vm.OpenEngineCommand);
        Assert.NotNull(vm.OpenDonateCommand);
        Assert.NotNull(vm.OpenLicensesCommand);
        Assert.NotNull(vm.OpenTranslateCommand);
        Assert.NotNull(vm.OpenGithubCommand);
        Assert.NotNull(vm.OpenTelegramCommand);
        Assert.NotNull(vm.OpenEmailCommand);

        // Links point at this project, not a placeholder. (The repo rule: never name another
        // download manager anywhere — these must stay our own URLs.)
        Assert.StartsWith("https://github.com/bezzad/", AboutViewModel.RepoUrl);
        Assert.StartsWith("https://github.com/bezzad/", AboutViewModel.EngineRepoUrl);
        Assert.StartsWith("https://github.com/bezzad/", AboutViewModel.DonateUrl);
        Assert.StartsWith("https://github.com/bezzad/", AboutViewModel.LicenseUrl);
        Assert.StartsWith("https://t.me/", AboutViewModel.TelegramUrl);
        Assert.Contains("@", AboutViewModel.Email);
    }

    // ---- DonateViewModel ---------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Donate_exposes_the_addresses_and_starts_uncopied()
    {
        var vm = new DonateViewModel();

        Assert.False(vm.Copied);
        Assert.Equal(DonateViewModel.UsdtAddress, vm.Usdt);
        Assert.Equal(DonateViewModel.UsdtNetwork, vm.Network);

        // An EVM address: 0x + 40 hex chars. A truncated/garbled address would send money nowhere.
        Assert.StartsWith("0x", vm.Usdt);
        Assert.Equal(42, vm.Usdt.Length);

        Assert.NotNull(vm.OpenSponsorsCommand);
        Assert.NotNull(vm.OpenLiberapayCommand);
        Assert.NotNull(vm.OpenRepoCommand);
        Assert.NotNull(vm.CopyUsdtCommand);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Donate_links_include_github_sponsors_and_liberapay()
    {
        Assert.Equal("https://github.com/sponsors/bezzad", DonateViewModel.GitHubSponsorsUrl);
        Assert.StartsWith("https://liberapay.com/", DonateViewModel.LiberapayUrl);
    }

    // ---- ShutdownViewModel -------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Shutdown_cancel_closes_the_dialog_and_does_not_power_off()
    {
        Localizer.Instance.Load("en");
        var elapsed = 0;
        var cancelled = 0;
        var closed = 0;

        var vm = new ShutdownViewModel(30, onElapsed: () => elapsed++, onCancel: () => cancelled++);
        vm.CloseRequested += () => closed++;

        Assert.Contains("30", vm.CountdownText);

        vm.CancelCommand.Execute(null);

        Assert.Equal(1, cancelled);
        Assert.Equal(1, closed);
        Assert.Equal(0, elapsed); // cancelling must never trigger the power-off callback

        // The timer is stopped, so a second press cannot fire the callbacks again.
        vm.CancelCommand.Execute(null);
        Assert.Equal(2, cancelled); // command still works, but…
        Assert.Equal(0, elapsed);   // …still no shutdown
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Shutdown_now_closes_the_dialog_and_triggers_the_shutdown()
    {
        Localizer.Instance.Load("en");
        var elapsed = 0;
        var cancelled = 0;
        var closed = 0;

        var vm = new ShutdownViewModel(30, onElapsed: () => elapsed++, onCancel: () => cancelled++);
        vm.CloseRequested += () => closed++;

        vm.ShutdownNowCommand.Execute(null);

        Assert.Equal(1, elapsed);
        Assert.Equal(1, closed);
        Assert.Equal(0, cancelled);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Shutdown_labels_are_localized_not_raw_keys()
    {
        Localizer.Instance.Load("en");
        var vm = new ShutdownViewModel(30, null, null);

        foreach (var text in new[] { vm.Title, vm.Message, vm.CancelText, vm.ShutdownNowText })
        {
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain("Shutdown_", text);
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Shutdown_design_time_ctor_does_not_throw_with_null_callbacks()
    {
        Localizer.Instance.Load("en");
        var vm = new ShutdownViewModel();

        // Null callbacks must be tolerated — the XAML designer constructs this one.
        vm.ShutdownNowCommand.Execute(null);
        vm.CancelCommand.Execute(null);
    }

    // ---- UpdatePromptViewModel ---------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Update_prompt_shows_the_offered_version_and_knows_it_has_a_release_page()
    {
        Localizer.Instance.Load("en");
        var vm = new UpdatePromptViewModel("9.9.9", "https://github.com/bezzad/Downloader.Desktop/releases/tag/v9.9.9");

        Assert.Contains("9.9.9", vm.Message);
        Assert.True(vm.HasReleaseUrl);
        foreach (var text in new[] { vm.Title, vm.DownloadText, vm.LaterText, vm.ViewChangesText })
        {
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain("Update_", text);
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Update_prompt_without_a_release_url_hides_the_whats_new_link()
    {
        Localizer.Instance.Load("en");
        var vm = new UpdatePromptViewModel("9.9.9", null);

        Assert.False(vm.HasReleaseUrl);

        // "What's new" with no URL must be inert, not a crash.
        vm.ViewChangesCommand.Execute(null);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Update_prompt_later_closes_and_dismisses()
    {
        Localizer.Instance.Load("en");
        UpdateFlow.ResetForTests();

        var closed = 0;
        var vm = new UpdatePromptViewModel("9.9.9", null);
        vm.CloseRequested += () => closed++;

        vm.LaterCommand.Execute(null);

        Assert.Equal(1, closed);
        Assert.Equal(UpdateState.Idle, UpdateFlow.State);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Update_prompt_download_closes_and_no_ops_without_a_pending_update()
    {
        Localizer.Instance.Load("en");
        UpdateFlow.ResetForTests();

        var closed = 0;
        var vm = new UpdatePromptViewModel("9.9.9", null);
        vm.CloseRequested += () => closed++;

        vm.DownloadCommand.Execute(null);
        Assert.Equal(1, closed);

        // Nothing is pending, so StartDownloadAsync must return without touching the network.
        await UpdateFlow.StartDownloadAsync();
        Assert.Equal(UpdateState.Idle, UpdateFlow.State);
    }
}
