using System;
using System.IO;
using System.Threading.Tasks;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// "Ask before adding programmatic downloads" (issue #13): a download handed to the app over the local
/// API used to be added and started with no way to review it, whatever the extension's own
/// silent-vs-dialog toggle said.
///
/// Two levers ask for the same thing and they must not fight: the REQUEST's <c>confirm</c> parameter
/// (which the extension sends) and the APP's setting (the only lever over a third-party client that
/// will never learn to send one). The rule is one sentence — an explicit request value wins in BOTH
/// directions — and it is the matrix below, because getting it wrong either adds a download the user
/// wanted to see or blocks a script behind a modal it cannot answer.
/// </summary>
public class ConfirmBeforeAddTests : IDisposable
{
    public ConfirmBeforeAddTests() => LocalApiService.ClearPendingAdds();

    public void Dispose()
    {
        // The ticket store and its clock are process-wide statics.
        LocalApiService.ClearPendingAdds();
        LocalApiService.PendingClock = () => DateTimeOffset.UtcNow;
        LocalApiService.PendingAddLifetime = TimeSpan.FromMinutes(10);
    }

    private static Config ConfigWith(bool setting)
    {
        var config = Config.New();
        config.Settings.ConfirmProgrammaticAdds = setting;
        return config;
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    // setting off — today's behaviour for everyone who has not opted in
    [InlineData(false, null, false)]
    [InlineData(false, true, true)]   // the extension's dialog mode asks even with the setting off
    [InlineData(false, false, false)]
    // setting on — the blunt instrument for clients that send nothing
    [InlineData(true, null, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]  // …and an explicit opt-out still wins
    public void The_request_wins_over_the_setting_in_both_directions(bool setting, bool? confirm, bool expected)
    {
        var req = new ApiAddRequest { Url = "https://example.com/a.zip", Confirm = confirm };

        Assert.Equal(expected, LocalApiService.ShouldConfirm(req, ConfigWith(setting)));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_absent_confirm_stays_unset_so_the_setting_can_decide()
    {
        // The distinction that makes the matrix above possible: absent must NOT deserialize to false,
        // or the app-wide setting could never take effect on an ordinary client's request.
        Assert.Null(ApiAddRequest.FromJson("""{"url":"https://example.com/a.zip"}""").Confirm);
        Assert.Null(ApiAddRequest.FromQuery(new Uri("http://127.0.0.1:15151/api/add?url=https%3A%2F%2Fe.com%2Fa.zip")).Confirm);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("1", true)]
    public void An_explicit_confirm_is_read_from_the_query_form(string raw, bool expected)
    {
        var req = ApiAddRequest.FromQuery(
            new Uri($"http://127.0.0.1:15151/api/add?url=https%3A%2F%2Fe.com%2Fa.zip&confirm={raw}"));

        Assert.Equal(expected, req.Confirm);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(true)]
    [InlineData(false)]
    public void An_explicit_confirm_is_read_from_the_json_body(bool value)
    {
        var json = $$"""{"url":"https://example.com/a.zip","confirm":{{value.ToString().ToLowerInvariant()}}}""";

        Assert.Equal(value, ApiAddRequest.FromJson(json).Confirm);
    }

    // ---------------- The pending-confirmation store ----------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_ticket_starts_pending_and_carries_the_id_once_it_is_confirmed()
    {
        var (ticket, opened) = LocalApiService.RegisterPendingAdd();
        Assert.True(opened);
        Assert.Equal(LocalApiService.PendingAddState.Pending, LocalApiService.LookupPendingAdd(ticket).State);

        LocalApiService.ResolvePendingAdd(ticket, "item-1");

        var resolved = LocalApiService.LookupPendingAdd(ticket);
        Assert.Equal(LocalApiService.PendingAddState.Added, resolved.State);
        Assert.Equal("item-1", resolved.Id);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_cancelled_ticket_carries_no_id()
    {
        var (ticket, _) = LocalApiService.RegisterPendingAdd();

        LocalApiService.ResolvePendingAdd(ticket, null);

        var resolved = LocalApiService.LookupPendingAdd(ticket);
        Assert.Equal(LocalApiService.PendingAddState.Cancelled, resolved.State);
        Assert.Null(resolved.Id);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_second_confirmation_while_one_is_open_is_cancelled_rather_than_stacked()
    {
        // A page that fires several downloads at once must not bury the user under modals. The second
        // request still gets a ticket — it is answered, just answered "no".
        var (first, firstOpened) = LocalApiService.RegisterPendingAdd();
        var (second, secondOpened) = LocalApiService.RegisterPendingAdd();

        Assert.True(firstOpened);
        Assert.False(secondOpened);
        Assert.Equal(LocalApiService.PendingAddState.Pending, LocalApiService.LookupPendingAdd(first).State);
        Assert.Equal(LocalApiService.PendingAddState.Cancelled, LocalApiService.LookupPendingAdd(second).State);

        // Once the first is answered, the next add may open a dialog again.
        LocalApiService.ResolvePendingAdd(first, "item-1");
        Assert.True(LocalApiService.RegisterPendingAdd().Opened);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_expired_ticket_is_forgotten_and_never_reads_as_added()
    {
        var now = DateTimeOffset.UtcNow;
        LocalApiService.PendingClock = () => now;
        LocalApiService.PendingAddLifetime = TimeSpan.FromMinutes(10);

        var (ticket, _) = LocalApiService.RegisterPendingAdd();
        now += TimeSpan.FromMinutes(11);

        Assert.Null(LocalApiService.LookupPendingAdd(ticket));

        // And a late answer to a forgotten ticket cannot resurrect it as "added" — that would tell the
        // caller to cancel the browser's own download for a download that does not exist.
        LocalApiService.ResolvePendingAdd(ticket, "item-1");
        Assert.Null(LocalApiService.LookupPendingAdd(ticket));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_unknown_ticket_is_unknown()
    {
        Assert.Null(LocalApiService.LookupPendingAdd("nosuchticket"));
        Assert.Null(LocalApiService.LookupPendingAdd(null));
        Assert.Null(LocalApiService.LookupPendingAdd(""));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_first_answer_to_a_ticket_stands()
    {
        var (ticket, _) = LocalApiService.RegisterPendingAdd();

        LocalApiService.ResolvePendingAdd(ticket, "item-1");
        LocalApiService.ResolvePendingAdd(ticket, null); // a stray second close must not undo it

        Assert.Equal(LocalApiService.PendingAddState.Added, LocalApiService.LookupPendingAdd(ticket).State);
    }

    // ---------------- The setting itself ----------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_setting_is_off_by_default_so_existing_integrations_are_unaffected()
    {
        Assert.False(DownloadSettings.New().ConfirmProgrammaticAdds);
        Assert.False(LocalApiService.ShouldConfirm(new ApiAddRequest { Url = "https://e.com/a.zip" }, Config.New()));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task The_setting_survives_a_save_and_load()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dldesktop-confirm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        FileService.ConfigFileOverride = Path.Combine(dir, "config.json");
        try
        {
            var service = new FileService();
            var config = Config.New();
            config.Settings.ConfirmProgrammaticAdds = true;
            await service.SaveToFileAsync(config);

            var loaded = await service.LoadFromFileAsync();

            Assert.True(loaded.Settings.ConfirmProgrammaticAdds);
        }
        finally
        {
            FileService.ConfigFileOverride = null;
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
