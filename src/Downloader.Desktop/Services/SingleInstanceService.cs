using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Downloader.Desktop.Services;

/// <summary>
/// Enforces a single running instance and provides a tiny IPC channel between launches. A fixed
/// loopback port doubles as the lock: the first instance binds it (and becomes the primary), later
/// launches fail to bind, forward their command-line args (e.g. a URL) to the primary, then exit.
/// The primary surfaces its window and treats a forwarded URL as a new "add" link.
///
/// This is the cross-platform equivalent of the Windows named-mutex + WM_COPYDATA trick.
/// </summary>
public static class SingleInstanceService
{
    /// <summary>The preferred single-instance / CLI-forward lock port. Must sit OUTSIDE the local API's
    /// fallback range (<see cref="LocalApiService.PortRange"/> = 15151–15155): if it were inside (it used
    /// to be 15152) the API could never bind that port — this lock holds it — so the API's fallback would
    /// silently skip it. A regression test asserts every lock port stays outside the range.</summary>
    public const int LockPort = 15150;

    /// <summary>Lock-port candidates, tried in order. A port can be held by a COMPLETELY UNRELATED
    /// process (a real case: the Cursor editor listens on 15150), so one fixed port is not enough —
    /// see <see cref="TryClaim"/>. All of these sit outside <see cref="LocalApiService.PortRange"/>.</summary>
    public static readonly int[] LockPorts = { LockPort, 15156, 15157, 15158 };

    /// <summary>Message prefix for a structured CLI add payload: <c>add:{json}</c>.</summary>
    public const string AddPrefix = "add:";

    /// <summary>Sent by the primary the moment a peer connects, so a caller can tell OUR listener apart
    /// from a foreign process that happens to hold the port. Bump the suffix on a protocol change.</summary>
    private const string Greeting = "downloader-ipc/1";

    /// <summary>How long to wait for the greeting before deciding a listener is not ours.</summary>
    private const int HandshakeTimeoutMs = 500;

    /// <summary>The lock port this instance actually bound, or 0 when it holds no lock.</summary>
    public static int EffectiveLockPort { get; private set; }

    private static TcpListener _listener;
    private static readonly List<string> _pending = new();
    private static Action<string> _onMessage;

    /// <summary>
    /// Tries to become the primary instance. Returns true if this process should keep running. Returns
    /// false if another instance is already running (this call forwarded its args to it and the caller
    /// should exit immediately).
    /// </summary>
    public static bool TryClaim(string[] args) => TryClaim(args, LockPorts);

    /// <summary>Testable core of <see cref="TryClaim(string[])"/>: same logic over a caller-supplied
    /// candidate list, so tests can use ephemeral ports instead of the real (possibly busy) ones.</summary>
    internal static bool TryClaim(string[] args, int[] ports)
    {
        foreach (var port in ports)
        {
            // NOTE: assign the static field only on SUCCESS. Failing paths must not touch it, or a
            // secondary claim in the same process would null out a live primary's listener.
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                _listener = listener;
                EffectiveLockPort = port;
                _ = Task.Run(AcceptLoopAsync);
                return true; // we are the primary instance
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                // The port is taken — but by WHOM? Assuming "another Downloader" here is what made the
                // app exit silently (code 0, no window, no error) whenever an unrelated process held the
                // port: it forwarded its args into the void and quit. Only bow out if the listener
                // actually speaks our protocol; otherwise it's a foreign squatter, so try the next port.
                if (TrySendTo(port, ForwardMessage(args)))
                    return false;
                AppLog.Warn($"Lock port {port} is held by a foreign process — trying the next candidate");
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Could not bind lock port {port}: {ex.Message}");
            }
        }

        // Every candidate is unusable and none of them is a Downloader. Run normally WITHOUT a lock:
        // losing single-instance is far better than refusing to start (the old code's silent exit).
        EffectiveLockPort = 0;
        AppLog.Warn("No lock port available — running without single-instance enforcement");
        return true;
    }

    /// <summary>Registers the handler that receives forwarded messages (URLs). Flushes any buffered ones.</summary>
    public static void SetMessageHandler(Action<string> onMessage)
    {
        _onMessage = onMessage;
        List<string> buffered;
        lock (_pending)
        {
            buffered = new List<string>(_pending);
            _pending.Clear();
        }
        foreach (var m in buffered)
            _onMessage?.Invoke(m);
    }

    public static void Stop()
    {
        try { _listener?.Stop(); } catch { /* ignore */ }
        _listener = null;
        EffectiveLockPort = 0;
    }

    /// <summary>The message a secondary launch hands to the primary.</summary>
    private static string ForwardMessage(string[] args)
    {
        // A spawned CLI add can race a just-started instance: hand over the payload instead of
        // losing it (a bare "add:{json}" arrives at the primary's handler like a forwarded add).
        var cliAdd = Array.IndexOf(args ?? Array.Empty<string>(), CliParser.CliAddSwitch);
        return cliAdd >= 0 && cliAdd + 1 < args.Length
            ? AddPrefix + args[cliAdd + 1]
            // Otherwise send the first URL-looking arg (or empty = "just focus the window").
            : FirstUrl(args) ?? string.Empty;
    }

    /// <summary>Forwards a CLI add payload to a running instance. False when none is running.</summary>
    public static bool TryForwardAdd(string json) => TrySend(AddPrefix + json);

    /// <summary>Sends to whichever lock port a real Downloader answers on. False when none does.</summary>
    private static bool TrySend(string message)
    {
        foreach (var port in LockPorts)
            if (TrySendTo(port, message))
                return true;
        return false;
    }

    /// <summary>Handshakes with the listener on <paramref name="port"/> and, only if it is really ours,
    /// sends <paramref name="message"/>. False when nothing is listening or it is a foreign process.</summary>
    private static bool TrySendTo(int port, string message)
    {
        try
        {
            using var client = new TcpClient { ReceiveTimeout = HandshakeTimeoutMs, SendTimeout = HandshakeTimeoutMs };
            client.Connect(IPAddress.Loopback, port);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);

            // A foreign listener either says nothing (read times out) or says something else; both mean
            // "not a Downloader". NOTE: an OLDER Downloader sends no greeting either, so during an
            // upgrade a new launch may briefly fail to detect a running old one — acceptable and transient.
            if (reader.ReadLine()?.Trim() != Greeting)
                return false;

            var bytes = Encoding.UTF8.GetBytes(message + "\n");
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
            return true;
        }
        catch
        {
            // best-effort — nothing listening, a foreign socket, or it went away mid-handshake
            return false;
        }
    }

    private static async Task AcceptLoopAsync()
    {
        while (_listener != null)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false); }
            catch { break; }

            try
            {
                using (client)
                await using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    // Announce ourselves FIRST so the peer can tell a real Downloader from a foreign
                    // process squatting the port (see TrySendTo).
                    var hello = Encoding.UTF8.GetBytes(Greeting + "\n");
                    await stream.WriteAsync(hello).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);

                    var msg = (await reader.ReadToEndAsync().ConfigureAwait(false))?.Trim();
                    Dispatch(msg ?? string.Empty);
                }
            }
            catch
            {
                // ignore a malformed connection and keep listening
            }
        }
    }

    private static void Dispatch(string msg)
    {
        if (_onMessage != null)
            // AcceptLoop runs on a background thread; the handler touches the window (Show/Activate),
            // which MUST happen on the UI thread or it silently no-ops (the "relaunch does nothing" bug).
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _onMessage?.Invoke(msg));
        else
            lock (_pending) _pending.Add(msg); // buffer until the app wires its handler
    }

    /// <summary>Returns the first http(s) URL among the args (skips switches like --minimized).</summary>
    public static string FirstUrl(string[] args)
    {
        if (args == null)
            return null;
        foreach (var a in args)
            if (!string.IsNullOrWhiteSpace(a) && (a.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                                                  || a.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                return a.Trim();
        return null;
    }
}
