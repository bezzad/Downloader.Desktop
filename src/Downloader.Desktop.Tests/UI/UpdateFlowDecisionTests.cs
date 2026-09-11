using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// What the update check decides, once the GitHub lookup itself is out of the way.
///
/// Every branch here ends in something different happening to the user — a prompt, a browser tab, a
/// "you're up to date" note, or deliberate silence — and until now none of them ran in the suite
/// because the whole method was written off as "network". The lookup is the only network part; the
/// decisions after it are ordinary logic, and they are the ones that go wrong (a manual check that
/// reports nothing looks broken; a silent automatic check that pops a dialog is worse).
/// </summary>
public class UpdateFlowDecisionTests : IDisposable
{
    private readonly bool _notificationsWereEnabled = NotificationService.Enabled;
    // The suite's module initializer points this at a no-op so no test posts a real notification; the
    // tests below swap in a recorder, and it has to go back or a LATER test keeps writing into a dead list.
    private readonly Action<string, string, bool> _nativeWas = NotificationService.NativeOverride;
    private readonly List<string> _opened = new();

    public UpdateFlowDecisionTests()
    {
        NotificationService.Enabled = false;      // don't post real OS notifications
        ShellLauncher.OpenOverride = url => { _opened.Add(url); return true; };
        UpdateFlow.ResetForTests();
        Localizer.Instance.Load("en");
    }

    public void Dispose()
    {
        UpdateFlow.ResetForTests();
        ShellLauncher.OpenOverride = null;
        NotificationService.NativeOverride = _nativeWas;
        NotificationService.Enabled = _notificationsWereEnabled;
    }

    private static UpdateInfo Release(string tag = "v99.0.0", string asset = "https://host/Downloader.tar.gz") =>
        new()
        {
            Version = "99.0.0",
            Tag = tag,
            AssetUrl = asset,
            AssetName = "Downloader.tar.gz",
            ReleaseUrl = "https://github.com/bezzad/Downloader.Desktop/releases/tag/" + tag
        };

    /// <summary>The normal path: a newer release asks first and downloads nothing behind the user's back.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_newer_release_prompts_the_user_and_downloads_nothing_yet()
    {
        var prompted = new List<UpdateInfo>();
        UpdateFlow.PromptUpdate = info => prompted.Add(info);
        UpdateFlow.CheckOverride = () => Task.FromResult(Release());

        await UpdateFlow.CheckAsync(manual: true);
        Dispatcher.UIThread.RunJobs(); // the prompt is posted to the UI thread

        Assert.Equal(UpdateState.Available, UpdateFlow.State);
        Assert.Equal("v99.0.0", UpdateFlow.AvailableTag);
        Assert.Equal("99.0.0", UpdateFlow.AvailableVersion);
        Assert.Contains("v99.0.0", UpdateFlow.AvailableReleaseUrl);
        Assert.Equal("v99.0.0", Assert.Single(prompted).Tag);
        Assert.False(UpdateFlow.IsReady, "nothing may be staged until the user accepts");
        Assert.Empty(_opened);
    }

    /// <summary>
    /// With no in-app prompt wired up the user still has to learn about it, or a release goes
    /// unnoticed forever. The fallback is a plain system notification.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Without_a_prompt_handler_the_update_is_announced_instead()
    {
        UpdateFlow.PromptUpdate = null;
        UpdateFlow.CheckOverride = () => Task.FromResult(Release());

        await UpdateFlow.CheckAsync(manual: false);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(UpdateState.Available, UpdateFlow.State);
        Assert.Equal("v99.0.0", UpdateFlow.AvailableTag);
    }

    /// <summary>
    /// A release with no archive for this OS/arch cannot be installed in place, so the release page is
    /// opened instead of leaving the user with a dead "Download" button.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_release_with_no_asset_for_this_platform_opens_the_release_page()
    {
        var prompted = 0;
        UpdateFlow.PromptUpdate = _ => prompted++;
        UpdateFlow.CheckOverride = () => Task.FromResult(Release(asset: null));

        await UpdateFlow.CheckAsync(manual: true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("https://github.com/bezzad/Downloader.Desktop/releases/tag/v99.0.0",
            Assert.Single(_opened));
        Assert.Equal(UpdateState.Idle, UpdateFlow.State);
        Assert.Equal(0, prompted);
    }

    /// <summary>Already on the latest build: back to Idle, and nothing is offered.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Being_up_to_date_leaves_the_flow_idle()
    {
        var prompted = 0;
        UpdateFlow.PromptUpdate = _ => prompted++;
        UpdateFlow.CheckOverride = () => Task.FromResult<UpdateInfo>(null);

        await UpdateFlow.CheckAsync(manual: true);   // manual: the user gets told
        await UpdateFlow.CheckAsync(manual: false);  // automatic: silence

        Assert.Equal(UpdateState.Idle, UpdateFlow.State);
        Assert.Null(UpdateFlow.AvailableTag);
        Assert.Equal(0, prompted);
        Assert.Empty(_opened);
    }

    /// <summary>A failed lookup (offline, rate-limited, malformed) must land back on Idle, not wedge
    /// the flow in Checking — which would make every later check no-op behind the busy guard.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_check_that_throws_leaves_the_flow_idle_and_checkable_again()
    {
        UpdateFlow.CheckOverride = () => throw new InvalidOperationException("offline");

        await UpdateFlow.CheckAsync(manual: true);

        Assert.Equal(UpdateState.Idle, UpdateFlow.State);

        // and the next check still runs rather than being swallowed by the busy guard
        UpdateFlow.CheckOverride = () => Task.FromResult(Release());
        await UpdateFlow.CheckAsync(manual: false);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(UpdateState.Available, UpdateFlow.State);
    }

    /// <summary>Once an update is offered or staged, a second check must not re-prompt over it.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_second_check_does_not_re_prompt_while_one_is_already_offered()
    {
        var lookups = 0;
        var prompted = 0;
        UpdateFlow.PromptUpdate = _ => prompted++;
        UpdateFlow.CheckOverride = () => { lookups++; return Task.FromResult(Release()); };

        await UpdateFlow.CheckAsync(manual: false);
        Dispatcher.UIThread.RunJobs();
        await UpdateFlow.CheckAsync(manual: false);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, lookups);
        Assert.Equal(1, prompted);
    }

    /// <summary>"Later" puts the flow back to Idle so a future check can offer it again.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Dismissing_an_offered_update_lets_a_later_check_offer_it_again()
    {
        UpdateFlow.PromptUpdate = _ => { };
        UpdateFlow.CheckOverride = () => Task.FromResult(Release());

        await UpdateFlow.CheckAsync(manual: false);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(UpdateState.Available, UpdateFlow.State);

        UpdateFlow.Dismiss();

        Assert.Equal(UpdateState.Idle, UpdateFlow.State);
    }

    /// <summary>
    /// State changes drive the nav button and the Settings page, so they are marshaled onto the UI
    /// thread. A check kicked off from a background thread (startup does exactly that) must still
    /// deliver its notification.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_check_started_off_the_UI_thread_still_notifies_the_UI()
    {
        var changes = 0;
        UpdateFlow.Changed += () => changes++;
        UpdateFlow.PromptUpdate = _ => { };
        UpdateFlow.CheckOverride = () => Task.FromResult(Release());

        await Task.Run(() => UpdateFlow.CheckAsync(manual: false));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(UpdateState.Available, UpdateFlow.State);
        Assert.True(changes > 0, "the UI must be told the state changed");
    }

    /// <summary>Restarting into an update is only allowed once one is actually staged.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Restarting_is_refused_while_the_update_is_merely_offered()
    {
        var quits = 0;
        UpdateFlow.RequestQuit = () => quits++;
        UpdateFlow.PromptUpdate = _ => { };
        UpdateFlow.CheckOverride = () => Task.FromResult(Release());

        await UpdateFlow.CheckAsync(manual: false);
        Dispatcher.UIThread.RunJobs();

        UpdateFlow.ApplyAndRestart();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(UpdateState.Available, UpdateFlow.State);
        Assert.Equal(0, quits);
    }

    // ---- "Check for updates does nothing" (reported on 2.12.0) -------------------------------------
    // Every outcome was reported ONLY as an OS notification, which Notify() drops when the notifications
    // switch is off, and a FAILED check reported nothing anywhere — it only wrote to the log, which is
    // off by default. The button was indistinguishable from a dead one.

    /// <summary>A check that cannot reach GitHub must say so, not fail silently.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_check_that_fails_says_why_instead_of_looking_like_nothing_happened()
    {
        var shown = new List<string>();
        NotificationService.NativeOverride = (title, message, _) => shown.Add($"{title}: {message}");
        // The reported machine's situation: passive notifications are switched off. Feedback for
        // something the user just clicked must not be silenced by that.
        NotificationService.Enabled = false;
        UpdateFlow.CheckOverride = () => throw new System.Net.Http.HttpRequestException("no such host is known");

        await UpdateFlow.CheckAsync(manual: true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(UpdateState.Idle, UpdateFlow.State);
        Assert.Contains("no such host is known", UpdateFlow.LastCheckMessage);
        Assert.Contains(shown, s => s.Contains("Couldn't check for updates"));
    }

    /// <summary>Already current is an ANSWER, and has to reach the user as one.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_up_to_date_check_reports_it_where_the_notifications_switch_cannot_hide_it()
    {
        var shown = new List<string>();
        NotificationService.NativeOverride = (title, message, _) => shown.Add($"{title}: {message}");
        NotificationService.Enabled = false;
        UpdateFlow.CheckOverride = () => Task.FromResult<UpdateInfo>(null);

        await UpdateFlow.CheckAsync(manual: true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(UpdateState.Idle, UpdateFlow.State);
        Assert.Contains(UpdateService.CurrentVersion.ToString(), UpdateFlow.LastCheckMessage);
        Assert.Contains(shown, s => s.Contains("up to date"));
    }

    /// <summary>An automatic check stays quiet, but still records what it found for the Settings row.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_automatic_check_records_the_outcome_without_interrupting_anyone()
    {
        var shown = new List<string>();
        NotificationService.NativeOverride = (title, message, _) => shown.Add($"{title}: {message}");
        UpdateFlow.CheckOverride = () => Task.FromResult<UpdateInfo>(null);

        await UpdateFlow.CheckAsync(manual: false);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("latest version", UpdateFlow.LastCheckMessage);
        Assert.Empty(shown);
    }

    /// <summary>A newer version names itself on the row too, so Settings explains the changed button.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_available_release_is_named_on_the_settings_row()
    {
        UpdateFlow.PromptUpdate = _ => { };
        UpdateFlow.CheckOverride = () => Task.FromResult(Release("v99.1.0"));

        await UpdateFlow.CheckAsync(manual: true);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("v99.1.0", UpdateFlow.LastCheckMessage);
    }
}
