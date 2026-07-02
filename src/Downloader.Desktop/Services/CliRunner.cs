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
        var url = $"http://127.0.0.1:{LocalApiService.Port}/api/{verb}" +
                  (id != null ? $"?id={Uri.EscapeDataString(id)}" : "");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = client.GetAsync(url).GetAwaiter().GetResult();
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine(printBody ? body : "OK");
                return 0;
            }

            Console.Error.WriteLine($"Error ({(int)response.StatusCode}): {body}");
            return 1;
        }
        catch (Exception)
        {
            Console.Error.WriteLine(NotReachable);
            return 1;
        }
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
