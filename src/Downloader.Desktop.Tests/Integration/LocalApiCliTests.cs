using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
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

/// <summary>Pure tests for the local API request models, the CLI parser and the config migration.</summary>
public class LocalApiCliLogicTests
{
    // ---------------- ApiAddRequest ----------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void AddRequest_parses_full_json_body()
    {
        var req = ApiAddRequest.FromJson(
            """{"url":"https://host/file.zip","filename":"a.zip","path":"/tmp","queue":"Main queue","mirrors":["https://m1/f.zip"],"start":false}""");

        Assert.Null(req.Error);
        Assert.Equal("https://host/file.zip", req.Url);
        Assert.Equal("a.zip", req.Filename);
        Assert.Equal("/tmp", req.Path);
        Assert.Equal("Main queue", req.Queue);
        Assert.Single(req.Mirrors);
        Assert.False(req.Start);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("""{"filename":"a.zip"}""")]                 // no url
    [InlineData("""{"url":"ftp://host/file.zip"}""")]        // non-http scheme
    [InlineData("""{"url":"not a url"}""")]
    [InlineData("not json at all")]
    public void AddRequest_rejects_invalid_input(string json)
    {
        Assert.NotNull(ApiAddRequest.FromJson(json).Error);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void AddRequest_rejects_relative_path()
    {
        var req = ApiAddRequest.FromJson("""{"url":"https://host/f.zip","path":"downloads/sub"}""");
        Assert.Contains("path", req.Error);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void AddRequest_parses_query_form_and_start_flag()
    {
        var req = ApiAddRequest.FromQuery(new Uri(
            "http://127.0.0.1:15151/api/add?url=https%3A%2F%2Fhost%2Ff.zip&filename=f.zip&start=false"));

        Assert.Null(req.Error);
        Assert.Equal("https://host/f.zip", req.Url);
        Assert.Equal("f.zip", req.Filename);
        Assert.False(req.Start);

        Assert.True(ApiAddRequest.FromQuery(new Uri(
            "http://127.0.0.1:15151/api/add?url=https%3A%2F%2Fhost%2Ff.zip")).Start); // default
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void AddRequest_json_round_trips()
    {
        var src = new ApiAddRequest { Url = "https://host/f.zip", Filename = "f.zip", Start = false };
        var round = ApiAddRequest.FromJson(src.ToJson());
        Assert.Null(round.Error);
        Assert.Equal(src.Url, round.Url);
        Assert.Equal(src.Filename, round.Filename);
        Assert.False(round.Start);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void ExtractIdFromJson_reads_id_and_tolerates_garbage()
    {
        Assert.Equal("abc", LocalApiService.ExtractIdFromJson("""{"id":"abc"}"""));
        Assert.Null(LocalApiService.ExtractIdFromJson("""{"other":1}"""));
        Assert.Null(LocalApiService.ExtractIdFromJson("not json"));
    }

    // ---------------- BuildItem ----------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void BuildItem_fills_defaults_and_resolves_queue_by_name()
    {
        var config = Config.New();
        config.Queues.Add(new DownloadQueue { Name = "Night" });

        var req = ApiAddRequest.FromJson("""{"url":"https://host/f.zip","queue":"night","mirrors":["https://m/f.zip"]}""");
        var item = LocalApiService.BuildItem(req, config);

        Assert.Equal(config.Settings.DefaultSavePath, item.SaveFolder); // no path given
        Assert.Equal(config.Queues[1].Id, item.QueueId);                // matched case-insensitively
        Assert.Equal(2, item.Urls.Count);                               // url + mirror
        Assert.Null(item.FileName);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void BuildItem_unknown_queue_falls_back_to_default()
    {
        var config = Config.New();
        var req = ApiAddRequest.FromJson("""{"url":"https://host/f.zip","queue":"nope"}""");
        Assert.Equal(config.DefaultQueue.Id, LocalApiService.BuildItem(req, config).QueueId);
    }

    // ---------------- CliParser ----------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Cli_add_parses_all_options()
    {
        Assert.True(CliParser.TryParse(
            new[] { "add", "--url", "https://host/f.zip", "--filename", "f.zip", "--path", "/tmp", "--queue", "Main", "--no-start" },
            out var cmd));
        Assert.Null(cmd.Error);
        Assert.Equal("add", cmd.Verb);
        Assert.Equal("https://host/f.zip", cmd.Add.Url);
        Assert.Equal("/tmp", cmd.Add.Path);
        Assert.False(cmd.Add.Start);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Cli_usage_errors_are_reported()
    {
        var badInvocations = new[]
        {
            new[] { "add" },                          // missing --url
            new[] { "add", "--bogus", "x" },          // unknown option
            new[] { "add", "--url", "not-a-url" },    // invalid url
            new[] { "list", "extra" },                // list takes no args
            new[] { "pause" },                        // missing id
            new[] { "pause", "not-a-guid" }           // bad id
        };
        foreach (var args in badInvocations)
        {
            Assert.True(CliParser.TryParse(args, out var cmd));
            Assert.NotNull(cmd.Error);
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Cli_control_verbs_take_a_guid()
    {
        var id = Guid.NewGuid().ToString();
        foreach (var verb in new[] { "pause", "resume", "cancel", "retry", "remove" })
        {
            Assert.True(CliParser.TryParse(new[] { verb, id }, out var cmd));
            Assert.Null(cmd.Error);
            Assert.Equal(id, cmd.Id);
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Cli_non_verbs_fall_through_to_gui()
    {
        var guiLaunches = new[]
        {
            Array.Empty<string>(),
            new[] { "https://host/file.zip" },   // bare URL launch
            new[] { "--minimized" },             // OS autostart launch
            new[] { "--cli-add", "{}" }          // spawned add payload launch
        };
        foreach (var args in guiLaunches)
            Assert.False(CliParser.TryParse(args, out _));
    }

    // ---------------- Config migration ----------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Integration_toggle_defaults_on_for_new_configs()
    {
        Assert.True(Config.New().Settings.EnableBrowserIntegration);
        Assert.Equal(Config.CurrentSchemaVersion, Config.New().SchemaVersion);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Old_config_is_migrated_to_enabled_once()
    {
        var old = new Config { Settings = DownloadSettings.New() };
        old.Settings.EnableBrowserIntegration = false; // persisted before the field's default flipped
        old.SchemaVersion = 0;

        old.EnsureValid();
        Assert.True(old.Settings.EnableBrowserIntegration);
        Assert.Equal(Config.CurrentSchemaVersion, old.SchemaVersion);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void User_choice_after_migration_is_respected()
    {
        var cfg = Config.New();
        cfg.Settings.EnableBrowserIntegration = false; // the user turned it off post-migration

        cfg.EnsureValid();
        Assert.False(cfg.Settings.EnableBrowserIntegration);
    }
}

/// <summary>End-to-end local API test: real listener, real manager, loopback HTTP.</summary>
public class LocalApiEndToEndTests
{
    /// <summary>Runs an HTTP task off-thread while pumping the dispatcher so /api handlers (which
    /// marshal onto the UI thread) can complete without deadlocking the test thread.</summary>
    private static T Pump<T>(Task<T> task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("local API request did not finish");
        }
        return task.GetAwaiter().GetResult();
    }

    private static Task<HttpResponseMessage> Get(HttpClient client, string pathAndQuery) =>
        Task.Run(() => client.GetAsync($"http://127.0.0.1:{LocalApiService.EffectivePort}{pathAndQuery}"));

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Api_add_list_control_and_legacy_endpoints_work()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false; // nothing must hit the network in this test
        manager.Initialize(config);

        LocalApiService.Manager = manager;
        LocalApiService.Config = config;
        LocalApiService.Start();
        Assert.True(LocalApiService.IsRunning); // port free (no app running on this box)

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            // Legacy endpoints unchanged: /ping is 200 + CORS, /add without url is 400.
            var ping = Pump(Get(client, "/ping"));
            Assert.Equal(HttpStatusCode.OK, ping.StatusCode);
            Assert.Equal("*", ping.Headers.GetValues("Access-Control-Allow-Origin").Single());
            Assert.Equal(HttpStatusCode.BadRequest, Pump(Get(client, "/add")).StatusCode);

            // Silent add (start=false so no engine/network work) → 201 + id, item lands in the manager.
            var add = Pump(Get(client, "/api/add?url=https%3A%2F%2Fhost%2Ffile.zip&filename=file.zip&start=false"));
            Assert.Equal((HttpStatusCode)201, add.StatusCode);
            Assert.False(add.Headers.Contains("Access-Control-Allow-Origin")); // no CORS on /api/*
            var id = JsonDocument.Parse(Pump(Task.Run(() => add.Content.ReadAsStringAsync())))
                .RootElement.GetProperty("id").GetString();
            Assert.True(Guid.TryParse(id, out _));
            Assert.Single(manager.Items);
            Assert.Equal("file.zip", manager.Items[0].GetItem().FileName);

            // Bad add input → 400.
            Assert.Equal(HttpStatusCode.BadRequest, Pump(Get(client, "/api/add?url=nope")).StatusCode);

            // List reflects the item.
            var list = Pump(Get(client, "/api/list"));
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var rows = JsonDocument.Parse(Pump(Task.Run(() => list.Content.ReadAsStringAsync()))).RootElement;
            Assert.Equal(1, rows.GetArrayLength());
            Assert.Equal(id, rows[0].GetProperty("id").GetString());

            // Control: unknown id → 404; cancel by real id → 200 and the row is Stopped.
            Assert.Equal(HttpStatusCode.NotFound, Pump(Get(client, $"/api/pause?id={Guid.NewGuid()}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, Pump(Get(client, $"/api/cancel?id={id}")).StatusCode);
            Assert.Equal(global::Downloader.DownloadStatus.Stopped, manager.Items[0].Status);

            // POST body form works too (control by JSON id) and is idempotent on a stopped row.
            var post = Task.Run(() => client.PostAsync(
                $"http://127.0.0.1:{LocalApiService.EffectivePort}/api/pause",
                new StringContent($$"""{"id":"{{id}}"}""", Encoding.UTF8, "application/json")));
            Assert.Equal(HttpStatusCode.OK, Pump(post).StatusCode);
        }
        finally
        {
            LocalApiService.Stop();
            LocalApiService.Manager = null;
            LocalApiService.Config = null;
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Api_add_reports_how_much_request_context_it_accepted()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false; // nothing must hit the network in this test
        manager.Initialize(config);

        LocalApiService.Manager = manager;
        LocalApiService.Config = config;
        LocalApiService.Start();
        Assert.True(LocalApiService.IsRunning);

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            // A GET add carrying a context in the wire shapes: Cookie-header string + `Name: value` block.
            var add = Pump(Get(client,
                "/api/add?start=false&url=" + Uri.EscapeDataString("https://cdn.example.com/v/index.m3u8") +
                "&referer=" + Uri.EscapeDataString("https://site.example/watch/42") +
                "&cookies=" + Uri.EscapeDataString("SID=s3cret; pref=1") +
                "&headers=" + Uri.EscapeDataString("Origin: https://site.example\nX-Token: t0ken")));
            Assert.Equal((HttpStatusCode)201, add.StatusCode);

            var body = Pump(Task.Run(() => add.Content.ReadAsStringAsync()));
            var json = JsonDocument.Parse(body).RootElement;
            Assert.Equal(2, json.GetProperty("cookies").GetInt32());
            Assert.Equal(2, json.GetProperty("headers").GetInt32());
            Assert.True(json.GetProperty("referer").GetBoolean());

            // Counts only — a value must never come back out. (The referer is a bool, not the URL.)
            Assert.DoesNotContain("s3cret", body);
            Assert.DoesNotContain("t0ken", body);
            Assert.DoesNotContain("site.example", body);

            // It really reached the item, not just the count.
            var item = manager.Items[0].GetItem();
            Assert.Equal(2, item.Request.Cookies.Count);
            Assert.Equal("t0ken", item.Request.Headers["X-Token"]);
            Assert.Equal("https://site.example/watch/42", item.Referer);

            // The zero case is reported as zero, not omitted — that's what makes a dropped hand-off visible.
            var plain = Pump(Get(client, "/api/add?start=false&url=https%3A%2F%2Fhost%2Ff.zip"));
            var plainJson = JsonDocument.Parse(Pump(Task.Run(() => plain.Content.ReadAsStringAsync()))).RootElement;
            Assert.Equal(0, plainJson.GetProperty("cookies").GetInt32());
            Assert.Equal(0, plainJson.GetProperty("headers").GetInt32());
            Assert.False(plainJson.GetProperty("referer").GetBoolean());
        }
        finally
        {
            foreach (var vm in manager.Items)
            {
                var path = vm.GetItem().CookieFilePath;
                if (path is { Length: > 0 } && File.Exists(path))
                    File.Delete(path);
            }
            LocalApiService.Stop();
            LocalApiService.Manager = null;
            LocalApiService.Config = null;
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Start_falls_back_to_next_port_when_preferred_is_taken()
    {
        // Occupy the preferred port so the service must fall back. If 15151 is ALREADY taken (e.g. a real
        // Downloader instance is running on this machine), that satisfies the precondition just as well —
        // don't fail the test trying to double-bind it.
        HttpListener blocker = null;
        try
        {
            blocker = new HttpListener();
            blocker.Prefixes.Add($"http://127.0.0.1:{LocalApiService.PreferredPort}/");
            blocker.Start();
        }
        catch (HttpListenerException)
        {
            blocker = null; // preferred port is already held externally — precondition met without us
        }

        try
        {
            var config = Config.New();
            LocalApiService.Config = config;
            LocalApiService.Start();

            Assert.True(LocalApiService.IsRunning);
            Assert.NotEqual(LocalApiService.PreferredPort, LocalApiService.EffectivePort);
            Assert.Contains(LocalApiService.EffectivePort, LocalApiService.PortRange);
            // The effective port is persisted so the next start / the CLI prefers it.
            Assert.Equal(LocalApiService.EffectivePort, config.Settings.LocalApiPort);
        }
        finally
        {
            LocalApiService.Stop();
            LocalApiService.Config = null;
            blocker?.Stop();
            blocker?.Close();
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)] // binds every port via HttpListener — slow on macOS CI (~1m17s)
    public void Start_retries_in_background_until_a_port_frees_up()
    {
        // The reported bug: a transient startup condition (all ports momentarily busy) left the API
        // silently dead until the user toggled the feature. Start must keep retrying and bind late.
        var blockers = LocalApiService.PortRange.Select(p =>
        {
            var l = new HttpListener();
            l.Prefixes.Add($"http://127.0.0.1:{p}/");
            try { l.Start(); return l; } catch { return null; } // a port already busy externally blocks too
        }).ToList();

        var config = Config.New();
        LocalApiService.Config = config;
        LocalApiService.RetryInterval = TimeSpan.FromMilliseconds(50);
        try
        {
            LocalApiService.Start();
            Assert.False(LocalApiService.IsRunning); // everything blocked right now

            // A retry attempt while everything is still blocked stays down (and doesn't throw).
            LocalApiService.RetryTickForTest();
            Assert.False(LocalApiService.IsRunning);

            // Free one port; the next background retry tick should grab it.
            var freed = blockers.First(b => b != null);
            freed.Stop(); freed.Close();
            blockers[blockers.IndexOf(freed)] = null;
            LocalApiService.RetryTickForTest();

            Assert.True(LocalApiService.IsRunning, "the retry should bind once a port frees up");
            Assert.Contains(LocalApiService.EffectivePort, LocalApiService.PortRange);
            Assert.Equal(LocalApiService.EffectivePort, config.Settings.LocalApiPort); // persisted late too
        }
        finally
        {
            LocalApiService.Stop();
            LocalApiService.Config = null;
            LocalApiService.RetryInterval = TimeSpan.FromSeconds(5);
            foreach (var b in blockers.Where(b => b != null))
            {
                try { b.Stop(); b.Close(); } catch { /* cleanup */ }
            }
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void SingleInstance_lock_port_is_outside_the_api_range()
    {
        // The single-instance/CLI lock binds one of LockPorts at startup; if any were inside the API's
        // fallback range the API could never use that port (verified live: the API skipped 15152 → 15153
        // when the lock still sat on 15152). Guard the invariant for EVERY candidate.
        Assert.DoesNotContain(SingleInstanceService.LockPort, LocalApiService.PortRange);
        foreach (var port in SingleInstanceService.LockPorts)
            Assert.DoesNotContain(port, LocalApiService.PortRange);
    }

    /// <summary>Two currently-free loopback ports (asked of the OS, then released) so these tests never
    /// fight over the real lock ports — 15150 can legitimately be busy on a dev machine, which is the
    /// very scenario under test.</summary>
    private static int[] FreePorts(int count)
    {
        var ports = new int[count];
        var probes = new TcpListener[count];
        for (var i = 0; i < count; i++)
        {
            probes[i] = new TcpListener(IPAddress.Loopback, 0);
            probes[i].Start();
            ports[i] = ((IPEndPoint)probes[i].LocalEndpoint).Port;
        }
        foreach (var p in probes) p.Stop();
        return ports;
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_foreign_listener_on_the_lock_port_does_not_make_the_app_exit()
    {
        // The real-world failure: an unrelated process (the Cursor editor) held 15150, so TryClaim read
        // "AddressAlreadyInUse" as "another Downloader is running", forwarded its args into that socket
        // and exited 0 — the app simply never opened, with no window and no error. A foreign squatter
        // must NOT be mistaken for our own instance.
        var ports = FreePorts(2);
        var squatter = new TcpListener(IPAddress.Loopback, ports[0]);
        squatter.Start();
        // Accept and stay silent, exactly like a foreign protocol that never sends our greeting.
        _ = Task.Run(async () =>
        {
            try { using var c = await squatter.AcceptTcpClientAsync(); await Task.Delay(3000); }
            catch { /* listener stopped */ }
        }, TestContext.Current.CancellationToken);
        try
        {
            Assert.True(SingleInstanceService.TryClaim(Array.Empty<string>(), ports),
                "the app must keep running when the lock port is held by a foreign process");
            // It must have fallen through to the next candidate rather than exiting or giving up the lock.
            Assert.Equal(ports[1], SingleInstanceService.EffectiveLockPort);
        }
        finally
        {
            SingleInstanceService.Stop();
            squatter.Stop();
        }
    }

    // AvaloniaFact, not Fact: the primary delivers forwarded messages via Dispatcher.UIThread.Post,
    // which needs the headless dispatcher (and the Pump below) to actually run the handler.
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_real_second_instance_is_still_detected_and_bows_out()
    {
        // The other half of the guard: a genuine primary (which answers the handshake) must still make a
        // later launch forward its args and exit, or the squatter fix would let two instances run.
        var ports = FreePorts(2);
        Assert.True(SingleInstanceService.TryClaim(Array.Empty<string>(), ports), "first claim is primary");
        Assert.Equal(ports[0], SingleInstanceService.EffectiveLockPort);
        try
        {
            var forwarded = new TaskCompletionSource<string>();
            SingleInstanceService.SetMessageHandler(m => forwarded.TrySetResult(m));

            // A "second launch" over the SAME candidates must bow out (false), not grab ports[1].
            Assert.False(SingleInstanceService.TryClaim(new[] { "https://example.com/f.zip" }, ports),
                "a second launch must defer to the running primary");
            Assert.Equal(ports[0], SingleInstanceService.EffectiveLockPort);

            // ...and the primary must actually receive the forwarded URL (pump: it arrives via UIThread.Post).
            Assert.Equal("https://example.com/f.zip", Pump(forwarded.Task));
        }
        finally
        {
            SingleInstanceService.Stop();
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Start_prefers_the_persisted_effective_port()
    {
        // A config that remembers a non-default port from a previous run should bind that one first.
        // Pick a remembered port that is actually FREE right now — a live Downloader instance on this
        // machine may hold any port in the range (it held 15153 during the author's session).
        var remembered = LocalApiService.PortRange.Skip(1).First(p =>
        {
            try
            {
                var probe = new HttpListener();
                probe.Prefixes.Add($"http://127.0.0.1:{p}/");
                probe.Start(); probe.Stop(); probe.Close();
                return true;
            }
            catch { return false; }
        });
        var config = Config.New();
        config.Settings.LocalApiPort = remembered;
        LocalApiService.Config = config;
        try
        {
            LocalApiService.Start();
            Assert.True(LocalApiService.IsRunning);
            Assert.Equal(remembered, LocalApiService.EffectivePort);
            Assert.Equal(remembered, config.Settings.LocalApiPort); // round-trips unchanged
        }
        finally
        {
            LocalApiService.Stop();
            LocalApiService.Config = null;
        }
    }
}
