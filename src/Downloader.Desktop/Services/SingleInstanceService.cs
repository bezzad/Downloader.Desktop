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
    // Distinct from the local API listener (15151).
    private const int LockPort = 15152;

    /// <summary>Message prefix for a structured CLI add payload: <c>add:{json}</c>.</summary>
    public const string AddPrefix = "add:";

    private static TcpListener _listener;
    private static readonly List<string> _pending = new();
    private static Action<string> _onMessage;

    /// <summary>
    /// Tries to become the primary instance. Returns true if this process should keep running. Returns
    /// false if another instance is already running (this call forwarded its args to it and the caller
    /// should exit immediately).
    /// </summary>
    public static bool TryClaim(string[] args)
    {
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, LockPort);
            _listener.Start();
            _ = Task.Run(AcceptLoopAsync);
            return true; // we are the primary instance
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            // Another instance owns the lock — hand it our args and bow out.
            TryForward(args);
            return false;
        }
        catch
        {
            // Any other failure (e.g. can't bind for an unrelated reason): fail open and run normally.
            return true;
        }
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
    }

    private static void TryForward(string[] args)
    {
        // A spawned CLI add can race a just-started instance: hand over the payload instead of
        // losing it (a bare "add:{json}" arrives at the primary's handler like a forwarded add).
        var cliAdd = Array.IndexOf(args ?? Array.Empty<string>(), CliParser.CliAddSwitch);
        var message = cliAdd >= 0 && cliAdd + 1 < args.Length
            ? AddPrefix + args[cliAdd + 1]
            // Otherwise send the first URL-looking arg (or empty = "just focus the window").
            : FirstUrl(args) ?? string.Empty;
        TrySend(message);
    }

    /// <summary>Forwards a CLI add payload to a running instance. False when none is running.</summary>
    public static bool TryForwardAdd(string json) => TrySend(AddPrefix + json);

    private static bool TrySend(string message)
    {
        try
        {
            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, LockPort);
            using var stream = client.GetStream();
            var bytes = Encoding.UTF8.GetBytes(message + "\n");
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
            return true;
        }
        catch
        {
            // best-effort — no instance is listening (or it went away mid-send)
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
