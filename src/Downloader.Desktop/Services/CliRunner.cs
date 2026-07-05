using System;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace Downloader.Desktop.Services;

/// <summary>
/// Executes a parsed <see cref="CliCommand"/> and returns the process exit code. Runs before
/// Avalonia starts — plain console I/O only. <c>add</c> hands the download to the running instance
/// over the single-instance channel (or starts the app detached); the other verbs talk HTTP to the
/// local API and need the app running with integration enabled.
/// </summary>
public static class CliRunner
{
    private static readonly string NotReachable =
        "Downloader is not reachable — start the app and make sure integration is enabled in Settings.";

    public static int Run(CliCommand cmd)
    {
        AttachWindowsConsole();

        if (cmd.Error != null)
        {
            Console.Error.WriteLine($"Error: {cmd.Error}");
            Console.Error.WriteLine(CliParser.Usage);
            return 2;
        }

        return cmd.Verb switch
        {
            "add" => RunAdd(cmd),
            "list" => RunHttp("list", null, printBody: true),
            _ => RunHttp(cmd.Verb, cmd.Id, printBody: false)
        };
    }

    private static int RunAdd(CliCommand cmd)
    {
        var json = cmd.Add.ToJson();

        // A running instance owns the single-instance lock port — hand the payload to it.
        if (SingleInstanceService.TryForwardAdd(json))
        {
            Console.WriteLine("Added to the running Downloader instance.");
            return 0;
        }

        // No instance running: start the app detached with the payload; it adds at startup. The
        // CLI process must never block on the GUI's lifetime.
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath,
                ArgumentList = { CliParser.CliAddSwitch, json },
                UseShellExecute = false
            });
            Console.WriteLine("Downloader started — download added.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not start Downloader: {ex.Message}");
            return 1;
        }
    }

    private static int RunHttp(string verb, string id, bool printBody)
    {
        var suffix = $"/api/{verb}" + (id != null ? $"?id={Uri.EscapeDataString(id)}" : "");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        // The running app may have fallen back within the declared port range, so try the last-known-good
        // port first (from the persisted config), then the rest of the range until one answers.
        foreach (var port in ResolveCandidatePorts())
        {
            var url = $"http://127.0.0.1:{port}{suffix}";
            try
            {
                using var response = client.GetAsync(url).GetAwaiter().GetResult();
                var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine(printBody ? body : "OK");
                    return 0;
                }

                // Reached the app but it rejected the request — report that, don't keep probing.
                Console.Error.WriteLine($"Error ({(int)response.StatusCode}): {body}");
                return 1;
            }
            catch (Exception)
            {
                // Couldn't reach this port — try the next candidate in the declared range.
            }
        }

        Console.Error.WriteLine(NotReachable);
        return 1;
    }

    /// <summary>Ordered ports to try: the last-known-good persisted port first (if valid), then the whole
    /// declared range. Reads the same config file the app writes its effective port to.</summary>
    private static System.Collections.Generic.IEnumerable<int> ResolveCandidatePorts()
    {
        int persisted = 0;
        try { persisted = new FileService().LoadFromFileAsync().GetAwaiter().GetResult()?.Settings?.LocalApiPort ?? 0; }
        catch { /* unreadable config — just probe the range */ }

        if (Array.IndexOf(LocalApiService.PortRange, persisted) >= 0)
            yield return persisted;
        foreach (var p in LocalApiService.PortRange)
            if (p != persisted)
                yield return p;
    }

    // The app is a GUI-subsystem executable on Windows, so stdout is detached from the invoking
    // terminal; attach to the parent console (harmless no-op when double-clicked or elsewhere).
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    private static void AttachWindowsConsole()
    {
        if (OperatingSystem.IsWindows())
        {
            try { AttachConsole(AttachParentProcess); } catch { /* best-effort */ }
        }
    }
}
