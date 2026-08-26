using System;
using System.IO;
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
public class CliRunnerTests
{
    /// <summary>
    /// Answers every request on as many ports of the API range as can be bound, so whichever
    /// candidate CliRunner picks is this stub. Ports already in use (a real app instance, another
    /// test) are skipped.
    /// </summary>
    private sealed class RangeStub : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly int _status;
        private readonly string _body;
        public int BoundCount { get; }

        public RangeStub(int status, string body)
        {
            _status = status;
            _body = body;

            foreach (var port in LocalApiService.PortRange)
            {
                try
                {
                    _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                    BoundCount++;
                }
                catch
                {
                    // can't reserve this prefix — fine, try the rest
                }
            }

            if (BoundCount == 0)
                return;

            try
            {
                _listener.Start();
            }
            catch
            {
                BoundCount = 0;
                return;
            }

            new Thread(Loop) { IsBackground = true }.Start();
        }

        private void Loop()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
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
            try { _listener.Stop(); } catch { }
            try { _listener.Close(); } catch { }
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

        var (exit, _, error) = Capture(() =>
            CliRunner.Run(Parse("cancel", Guid.NewGuid().ToString())));

        // Reaching the app and being told "no" is a different outcome from not finding the app at
        // all — it must surface the status, not the generic "start the app" advice.
        Assert.Equal(1, exit);
        Assert.Contains("404", error);
    }
}
