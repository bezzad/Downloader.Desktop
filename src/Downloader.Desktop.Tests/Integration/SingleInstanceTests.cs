using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// Claiming the single-instance lock, and what a second launch hands to the first.
///
/// The failure mode this guards is the one that cost a long session: a FOREIGN process holding the
/// lock port used to be read as "another Downloader is already running", so the app forwarded its
/// arguments into a stranger's socket and returned from Main — exit 0, no window, no error, entirely
/// indistinguishable from a broken install. The greeting handshake exists so that can never happen
/// again, and the last resort is to run WITHOUT the lock rather than to exit.
///
/// Tests always pass ephemeral ports: binding the real lock port would collide with the developer's
/// own running app (and with itself).
/// </summary>
public class SingleInstanceTests : IDisposable
{
    public void Dispose() => SingleInstanceService.Stop();

    private static int[] FreePorts(int count)
    {
        var ports = new List<int>();
        var listeners = new List<TcpListener>();
        for (var i = 0; i < count; i++)
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            listeners.Add(l);
            ports.Add(((IPEndPoint)l.LocalEndpoint).Port);
        }
        foreach (var l in listeners)
            l.Stop();
        return ports.ToArray();
    }

    // ---- claiming the lock -------------------------------------------------

    [Fact(Timeout = TestTimeouts.SlowMs)]
    public void The_first_instance_claims_a_port_and_records_it()
    {
        var ports = FreePorts(2);

        Assert.True(SingleInstanceService.TryClaim(Array.Empty<string>(), ports));

        Assert.Contains(SingleInstanceService.EffectiveLockPort, ports);
        SingleInstanceService.Stop();
    }

    [Fact(Timeout = TestTimeouts.SlowMs)]
    public void An_unusable_port_is_skipped_for_the_next_candidate()
    {
        var ports = FreePorts(2);

        // Hold the first candidate so the claim has to fall through to the second.
        var blocker = new TcpListener(IPAddress.Loopback, ports[0]);
        blocker.Start();
        try
        {
            Assert.True(SingleInstanceService.TryClaim(Array.Empty<string>(), ports));
            Assert.Equal(ports[1], SingleInstanceService.EffectiveLockPort);
        }
        finally
        {
            blocker.Stop();
            SingleInstanceService.Stop();
        }
    }

    [Fact(Timeout = TestTimeouts.SlowMs)]
    public void With_every_port_taken_the_app_runs_on_without_the_lock()
    {
        var ports = FreePorts(2);
        var blockers = ports.Select(p =>
        {
            var l = new TcpListener(IPAddress.Loopback, p);
            l.Start();
            return l;
        }).ToList();

        try
        {
            // The last resort must be "run anyway", never "exit": returning false here would make the
            // app close silently with no window and no error — the reported "installed but clicking
            // it does nothing".
            Assert.True(SingleInstanceService.TryClaim(Array.Empty<string>(), ports));
            Assert.Equal(0, SingleInstanceService.EffectiveLockPort);
        }
        finally
        {
            foreach (var l in blockers)
                l.Stop();
        }
    }

    [Fact(Timeout = TestTimeouts.SlowMs)]
    public void A_foreign_listener_on_the_lock_port_does_not_make_the_app_exit()
    {
        var ports = FreePorts(1);

        // Something else entirely (a code editor, a dev server) is on the port and will never speak
        // our greeting.
        var stranger = new TcpListener(IPAddress.Loopback, ports[0]);
        stranger.Start();
        try
        {
            Assert.True(SingleInstanceService.TryClaim(new[] { "https://example.invalid/f.zip" }, ports));
        }
        finally
        {
            stranger.Stop();
            SingleInstanceService.Stop();
        }
    }

    [Fact(Timeout = TestTimeouts.SlowMs)]
    public void Stopping_is_idempotent_and_safe_before_any_claim()
    {
        SingleInstanceService.Stop();
        SingleInstanceService.Stop();

        var ports = FreePorts(1);
        Assert.True(SingleInstanceService.TryClaim(Array.Empty<string>(), ports));
        SingleInstanceService.Stop();
        SingleInstanceService.Stop();
    }

    // ---- forwarding to a running instance ---------------------------------

    [Fact(Timeout = TestTimeouts.SlowMs)]
    public void Forwarding_to_nothing_reports_failure_rather_than_hanging()
    {
        // Nothing is listening, so the CLI must fall back to starting the app itself.
        Assert.False(SingleInstanceService.TryForwardAdd("{\"url\":\"https://example.invalid/f.zip\"}"));
    }

    // ---- picking the argument to hand over --------------------------------

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(new[] { "https://host/f.zip" }, "https://host/f.zip")]
    [InlineData(new[] { "http://host/f.zip" }, "http://host/f.zip")]
    [InlineData(new[] { "--minimized", "https://host/f.zip" }, "https://host/f.zip")]
    [InlineData(new[] { "HTTPS://host/f.zip" }, "HTTPS://host/f.zip")]      // scheme match is case-insensitive
    [InlineData(new[] { "https://host/f.zip  " }, "https://host/f.zip")]    // trailing space trimmed off
    public void The_first_url_among_the_arguments_is_the_one_handed_over(string[] args, string expected)
    {
        Assert.Equal(expected, SingleInstanceService.FirstUrl(args));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Arguments_with_no_url_hand_over_nothing()
    {
        // "Just focus the window" — a launch with only switches must not be mistaken for a download.
        Assert.Null(SingleInstanceService.FirstUrl(null));
        Assert.Null(SingleInstanceService.FirstUrl(Array.Empty<string>()));
        Assert.Null(SingleInstanceService.FirstUrl(new[] { "--minimized" }));
        Assert.Null(SingleInstanceService.FirstUrl(new[] { "", "   ", null }));
        Assert.Null(SingleInstanceService.FirstUrl(new[] { "ftp://host/f.zip", "/tmp/file.zip" }));
        // Leading whitespace is NOT tolerated: the scheme check runs before the trim, and argv does
        // not normally deliver padded arguments.
        Assert.Null(SingleInstanceService.FirstUrl(new[] { "  https://host/f.zip" }));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_lock_ports_never_overlap_the_local_api_range()
    {
        // The app's own lock would otherwise permanently occupy an API port, and the API's fallback
        // would silently skip it (verified live once — the reason LockPort moved off 15152).
        Assert.Empty(SingleInstanceService.LockPorts.Intersect(LocalApiService.PortRange));
    }
}
