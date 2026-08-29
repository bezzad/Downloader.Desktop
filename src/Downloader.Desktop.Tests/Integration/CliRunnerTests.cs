using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// The CLI verbs. <c>list</c>/<c>pause</c>/<c>resume</c>/… talk HTTP to the running app's local API,
/// probing the declared port range until something answers, so they are driven here against a stub
/// listener bound to that range.
///
/// The <c>add</c> verb is deliberately NOT exercised: when no instance holds the single-instance lock
/// it falls through to <c>Process.Start(Environment.ProcessPath)</c>, i.e. it would launch a real GUI
/// application from the test run. Its payload construction is already covered through
/// <c>ApiAddRequest</c>/<c>CliParser</c>.
/// </summary>
public class CliRunnerTests : IDisposable
{
    private readonly string _configPath =
        Path.Combine(Path.GetTempPath(), "dldesktop-cli-" + Guid.NewGuid().ToString("N") + ".json");

    public CliRunnerTests() => FileService.ConfigFileOverride = _configPath;

    public void Dispose()
    {
        FileService.ConfigFileOverride = null;   // never leave the real config redirected
        try { File.Delete(_configPath); } catch { /* best-effort */ }
    }

    /// <summary>
    /// Writes the config the CLI reads to find the app's last-known-good port. Without this the CLI
    /// probes the range in order and hits whatever else happens to hold the first port — which is not
    /// something a test can control, and is precisely the situation the persisted port exists for.
    /// </summary>
    private void PersistPort(int port)
    {
        var config = Models.Config.New();
        config.Settings.LocalApiPort = port;
        new FileService().SaveToFileAsync(config).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Answers every request on as many ports of the API range as can be bound, so whichever candidate
    /// CliRunner picks is this stub. Ports already in use (a real app instance, another test, another
    /// job on the machine) are skipped.
    ///
    /// One listener PER PORT, deliberately: a single listener holding all five prefixes fails to start
    /// wholesale when any one port is taken, and the "nothing to assert against" guard below then made
    /// every test in this class silently pass without running a line of CliRunner. That is exactly what
    /// happened on a box where something else held 15151.
    /// </summary>
    private sealed class RangeStub : IDisposable
    {
        private readonly List<HttpListener> _listeners = new();
        private readonly int _status;
        private readonly string _body;
        public int BoundCount => _listeners.Count;

        /// <summary>A port this stub really owns, so a test can steer the CLI onto it.</summary>
        public int FirstBoundPort { get; private set; }

        public RangeStub(int status, string body)
        {
            _status = status;
            _body = body;

            foreach (var port in LocalApiService.PortRange)
            {
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                try
                {
                    listener.Start();
                }
                catch
                {
                    listener.Close();
                    continue; // that port belongs to someone else — the rest still stand in
                }

                _listeners.Add(listener);
                if (FirstBoundPort == 0)
                    FirstBoundPort = port;
                var captured = listener;
                new Thread(() => Loop(captured)) { IsBackground = true }.Start();
            }
        }

        private void Loop(HttpListener listener)
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); }
                catch { return; }

                try
                {
                    var bytes = Encoding.UTF8.GetBytes(_body);
                    ctx.Response.StatusCode = _status;
                    ctx.Response.ContentLength64 = bytes.Length;
                    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                    ctx.Response.Close();
                }
                catch
                {
                    // client went away
                }
            }
        }

        public void Dispose()
        {
            foreach (var listener in _listeners)
            {
                try { listener.Stop(); } catch { }
                try { listener.Close(); } catch { }
            }
        }
    }

    private static (int exit, string output, string error) Capture(Func<int> run)
    {
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        var prevOut = Console.Out;
        var prevErr = Console.Error;
        try
        {
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            var code = run();
            return (code, outWriter.ToString(), errWriter.ToString());
        }
        finally
        {
            Console.SetOut(prevOut);
            Console.SetError(prevErr);
        }
    }

    private static CliCommand Parse(params string[] args)
    {
        CliParser.TryParse(args, out var cmd);
        return cmd;
    }

    [Fact(Timeout = TestTimeouts.SlowMs)]
    public void A_parse_error_prints_the_usage_and_exits_two()
    {
        var cmd = Parse("pause"); // control verbs need exactly one download id

        var (exit, _, error) = Capture(() => CliRunner.Run(cmd));

        Assert.Equal(2, exit);
        Assert.Contains("Error:", error);
        Assert.Contains("Usage", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = TestTimeouts.SlowMs)]
    public void List_prints_the_body_the_app_returned()
    {
        using var stub = new RangeStub(200, "[{\"id\":\"abc\"}]");
        if (stub.BoundCount == 0)
            return; // every API port is taken on this machine; nothing to assert against

        PersistPort(stub.FirstBoundPort);

        var (exit, output, _) = Capture(() => CliRunner.Run(Parse("list")));

        Assert.Equal(0, exit);
        Assert.False(string.IsNullOrWhiteSpace(output));
    }

    [Fact(Timeout = TestTimeouts.SlowMs)]
    public void A_control_verb_reports_ok_rather_than_the_raw_body()
    {
        using var stub = new RangeStub(200, "{\"ok\":true}");
        if (stub.BoundCount == 0)
            return;

        PersistPort(stub.FirstBoundPort);

        var (exit, output, _) = Capture(() =>
            CliRunner.Run(Parse("pause", Guid.NewGuid().ToString())));

        Assert.Equal(0, exit);
        Assert.Contains("OK", output);
    }

    [Fact(Timeout = TestTimeouts.SlowMs)]
    public void An_app_that_rejects_the_request_is_reported_instead_of_probing_on()
    {
        using var stub = new RangeStub(404, "no such download");
        if (stub.BoundCount == 0)
            return;

        PersistPort(stub.FirstBoundPort);

        var (exit, _, error) = Capture(() =>
            CliRunner.Run(Parse("cancel", Guid.NewGuid().ToString())));

        // Reaching the app and being told "no" is a different outcome from not finding the app at
        // all — it must surface the status, not the generic "start the app" advice.
        Assert.Equal(1, exit);
        Assert.Contains("404", error);
    }

    /// <summary>
    /// With no app listening anywhere in the range, the CLI must say so in words the user can act on —
    /// "start the app and enable integration" — rather than failing silently or hanging on each probe.
    /// </summary>
    [Fact(Timeout = TestTimeouts.SlowMs)]
    public void With_no_app_running_the_cli_says_how_to_fix_it()
    {
        // Only meaningful when the whole range really is free — something else on the machine holding
        // one of these ports would answer the probe and make "not reachable" untrue.
        using (var probe = new RangeStub(200, "[]"))
            if (probe.BoundCount != LocalApiService.PortRange.Length)
                return;

        // Point the persisted port at one outside the range so it is ignored, and bind nothing.
        PersistPort(1);

        var (exit, _, error) = Capture(() => CliRunner.Run(Parse("list")));

        Assert.Equal(1, exit);
        Assert.Contains("not reachable", error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An unreadable config just means "probe the whole range" — not a crash before the app is found.</summary>
    [Fact(Timeout = TestTimeouts.SlowMs)]
    public void An_unreadable_config_still_lets_the_cli_probe_the_range()
    {
        using var stub = new RangeStub(200, "[]");
        if (stub.BoundCount == 0)
            return;

        File.WriteAllText(_configPath, "{ this is not json");

        var (exit, _, _) = Capture(() => CliRunner.Run(Parse("list")));

        // Whatever answers first, the run completed rather than throwing out of the config read.
        Assert.InRange(exit, 0, 1);
    }
}
