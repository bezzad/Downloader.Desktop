using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// <c>/api/add</c> in confirm mode and its companion <c>/api/add-status</c> (issue #13).
///
/// The shape matters as much as the behaviour: the request must NOT block on the user. Holding the
/// response open until someone answers the dialog would be simpler code and the wrong thing — the
/// extension's add timeout fires long before a user who stepped away comes back, and it would read
/// that as a failed hand-off and leave the file to the browser. So the app answers 202 with a ticket
/// at once and the caller follows the ticket.
/// </summary>
public class ConfirmAddEndpointTests : IDisposable
{
    private readonly List<ApiAddRequest> _asked = new();
    private readonly List<string> _tickets = new();

    public ConfirmAddEndpointTests()
    {
        LocalApiService.Stop();          // a leak from another test would answer on the wrong port
        LocalApiService.ClearPendingAdds();
    }

    public void Dispose()
    {
        LocalApiService.Stop();
        LocalApiService.ClearPendingAdds();
        LocalApiService.OnAddConfirmationRequested = null;
        LocalApiService.Manager = null;
        LocalApiService.Config = null;
        LocalApiService.PendingClock = () => DateTimeOffset.UtcNow;
        LocalApiService.PendingAddLifetime = TimeSpan.FromMinutes(10);
    }

    /// <summary>Stands in for the app shell: records what the UI was asked to confirm, and answers
    /// nothing until a test decides to (that IS the user thinking about it).</summary>
    private (DownloadManager Manager, Config Config) StartApp(bool settingOn = false)
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false;   // nothing may hit the network here
        config.Settings.ConfirmProgrammaticAdds = settingOn;
        config.Settings.DefaultSavePath = System.IO.Path.GetTempPath();
        manager.Initialize(config);

        LocalApiService.Manager = manager;
        LocalApiService.Config = config;
        LocalApiService.OnAddConfirmationRequested = (req, ticket) =>
        {
            _asked.Add(req);
            _tickets.Add(ticket);
        };
        LocalApiService.Start();
        Assert.True(LocalApiService.IsRunning);
        return (manager, config);
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void A_confirm_mode_add_answers_202_with_a_ticket_and_adds_nothing_yet()
    {
        var (manager, _) = StartApp();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var response = Post(client, "/api/add", """
            {"url":"https://10.255.255.1/a.zip","confirm":true,"filename":"a.zip","referer":"https://10.255.255.1/p"}
            """);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var ticket = JsonOf(response).GetProperty("ticket").GetString();
        Assert.False(string.IsNullOrWhiteSpace(ticket));

        // Nothing exists yet — that is the whole point.
        Assert.Empty(manager.Items);

        // The UI was handed the WHOLE request, not just a URL: a hand-off that lost its context would
        // turn a working download into a broken one (issues #7 and #9).
        Pump();
        Assert.Single(_asked);
        Assert.Equal("https://10.255.255.1/a.zip", _asked[0].Url);
        Assert.Equal("a.zip", _asked[0].Filename);
        Assert.Equal("https://10.255.255.1/p", _asked[0].Referer);
        Assert.Equal(ticket, _tickets[0]);

        // Until someone answers, the ticket is honestly pending.
        Assert.Equal("pending", StateOf(client, ticket));
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void The_app_setting_alone_turns_an_ordinary_add_into_a_confirmation()
    {
        // This is what covers a third-party client (Cat Catch) that will never send `confirm`.
        var (manager, _) = StartApp(settingOn: true);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var response = Post(client, "/api/add", """{"url":"https://10.255.255.1/a.zip"}""");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(manager.Items);
        Pump();
        Assert.Single(_asked);
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void An_explicit_confirm_false_stays_silent_even_with_the_setting_on()
    {
        var (manager, _) = StartApp(settingOn: true);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var response = Post(client, "/api/add", """{"url":"https://10.255.255.1/a.zip","confirm":false,"start":false}""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);   // today's shape, byte for byte
        Assert.Single(manager.Items);
        Assert.Empty(_asked);
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void A_ticket_reports_added_with_the_new_item_id_once_the_user_confirms()
    {
        var (manager, config) = StartApp();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var ticket = JsonOf(Post(client, "/api/add", """{"url":"https://10.255.255.1/a.zip","confirm":true,"start":false}"""))
            .GetProperty("ticket").GetString();
        Pump();

        // The user confirms: the shell adds the item and resolves the ticket with its id.
        var item = LocalApiService.BuildItem(_asked[0], config);
        manager.Add(item, autoStart: false);
        LocalApiService.ResolvePendingAdd(ticket, item.Id.ToString());

        var status = JsonOf(Send(client, $"/api/add-status?ticket={ticket}"));
        Assert.Equal("added", status.GetProperty("state").GetString());
        Assert.Equal(item.Id.ToString(), status.GetProperty("id").GetString());
        Assert.Single(manager.Items);
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void A_cancelled_confirmation_reports_cancelled_and_adds_nothing()
    {
        var (manager, _) = StartApp();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var ticket = JsonOf(Post(client, "/api/add", """{"url":"https://10.255.255.1/a.zip","confirm":true}"""))
            .GetProperty("ticket").GetString();
        Pump();
        LocalApiService.ResolvePendingAdd(ticket, null);

        var status = JsonOf(Send(client, $"/api/add-status?ticket={ticket}"));
        Assert.Equal("cancelled", status.GetProperty("state").GetString());
        Assert.False(status.TryGetProperty("id", out _), "a cancelled add has no item to point at");
        Assert.Empty(manager.Items);
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void An_unknown_or_expired_ticket_is_404_and_a_missing_one_is_400()
    {
        StartApp();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        Assert.Equal(HttpStatusCode.NotFound, Send(client, "/api/add-status?ticket=nosuch").StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, Send(client, "/api/add-status").StatusCode);

        // An unanswered dialog must not pin a ticket for the life of the process — and once forgotten
        // it reads as unknown, never as added (which would tell the extension to cancel the browser's
        // own copy of a download that does not exist).
        var now = DateTimeOffset.UtcNow;
        LocalApiService.PendingClock = () => now;
        var ticket = JsonOf(Post(client, "/api/add", """{"url":"https://10.255.255.1/a.zip","confirm":true}"""))
            .GetProperty("ticket").GetString();
        Assert.Equal("pending", StateOf(client, ticket));

        now += LocalApiService.PendingAddLifetime + TimeSpan.FromMinutes(1);
        Assert.Equal(HttpStatusCode.NotFound, Send(client, $"/api/add-status?ticket={ticket}").StatusCode);
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void A_second_confirmation_while_one_is_open_is_answered_rather_than_stacked()
    {
        StartApp();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var first = JsonOf(Post(client, "/api/add", """{"url":"https://10.255.255.1/a.zip","confirm":true}"""))
            .GetProperty("ticket").GetString();
        var second = JsonOf(Post(client, "/api/add", """{"url":"https://10.255.255.1/b.zip","confirm":true}"""))
            .GetProperty("ticket").GetString();
        Pump();

        Assert.Equal("pending", StateOf(client, first));
        Assert.Equal("cancelled", StateOf(client, second));
        Assert.Single(_asked);   // only ONE dialog was ever asked for
    }

    // ---- plumbing ----------------------------------------------------------

    private static void Pump()
    {
        for (var i = 0; i < 8; i++)
            Dispatcher.UIThread.RunJobs();
    }

    private static string StateOf(HttpClient client, string ticket) =>
        JsonOf(Send(client, $"/api/add-status?ticket={ticket}")).GetProperty("state").GetString();

    // The /api handlers marshal onto the UI thread, which IS this thread under the headless runtime —
    // so the request runs off-thread while the dispatcher is pumped (see LocalApiEndToEndTests.Pump).
    private static HttpResponseMessage Await(Task<HttpResponseMessage> task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(40);
        while (!task.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
            if (DateTime.UtcNow > deadline) throw new TimeoutException("local API request did not finish");
        }
        return task.GetAwaiter().GetResult();
    }

    private static HttpResponseMessage Send(HttpClient client, string pathAndQuery) =>
        Await(Task.Run(() => client.GetAsync($"http://127.0.0.1:{LocalApiService.EffectivePort}{pathAndQuery}")));

    private static HttpResponseMessage Post(HttpClient client, string path, string json) =>
        Await(Task.Run(() => client.PostAsync($"http://127.0.0.1:{LocalApiService.EffectivePort}{path}",
            new StringContent(json, Encoding.UTF8, "application/json"))));

    private static JsonElement JsonOf(HttpResponseMessage response)
    {
        var body = Task.Run(() => response.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
        return JsonDocument.Parse(body).RootElement.Clone();
    }
}
