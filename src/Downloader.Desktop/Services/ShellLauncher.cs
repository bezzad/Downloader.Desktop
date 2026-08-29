using System;
using System.Diagnostics;

namespace Downloader.Desktop.Services;

/// <summary>
/// The one place the app hands something to the operating system: a URL to the default browser, a
/// folder to the file manager, or a specific command (the platform-specific "reveal this file"
/// invocations). Every call site used to build its own <see cref="ProcessStartInfo"/> with the same
/// try/catch — six near-identical copies across the view models and Settings.
///
/// Behaviour is unchanged and deliberately best-effort: if nothing is registered to handle the target
/// there is nothing useful to tell the user, so a failure is swallowed. What this adds is a single
/// seam (<see cref="OpenOverride"/> / <see cref="RunOverride"/>) so tests can assert WHICH url or
/// command a button would launch without actually opening a browser or a file manager on the
/// developer's machine. Same pattern as <see cref="ShutdownService.PowerOffOverride"/>.
///
/// Note this is not a general "run a program" helper and must not become one — see the standing rule
/// in CLAUDE.md about never spawning a shell. It starts what the caller names, by absolute path where
/// the caller has one.
/// </summary>
public static class ShellLauncher
{
    /// <summary>Test seam: when set, called instead of really handing the target to the OS. Returning
    /// false simulates a machine with no handler registered, which is what drives fallbacks like
    /// Settings' "browser compose, else mailto".</summary>
    internal static Func<string, bool> OpenOverride { get; set; }

    /// <summary>Test seam: when set, called instead of really starting the command. Returning false
    /// simulates a platform where the command is missing (no FileManager1 D-Bus service, no
    /// explorer/open), which is what drives the "just open the folder" fallback.</summary>
    internal static Func<string, string[], bool> RunOverride { get; set; }

    /// <summary>
    /// Opens a URL, file or folder with whatever the OS has registered for it. Best-effort: a machine
    /// with no handler (a headless box, a stripped container) simply does nothing.
    /// </summary>
    public static void Open(string target) => TryOpen(target);

    /// <summary>
    /// Same as <see cref="Open"/>, but reports whether the OS accepted it, so a caller can fall back
    /// to a second target (Settings tries a browser compose URL, then a mailto: link).
    /// </summary>
    public static bool TryOpen(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return false;

        if (OpenOverride is { } handler)
            return handler(target);

        try
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            return true;
        }
        catch
        {
            // best-effort: nothing actionable if no handler is registered
            return false;
        }
    }

    /// <summary>
    /// Starts a specific command with explicit arguments (the platform "reveal this file in its
    /// folder" invocations). Returns false if it could not be started, so a caller can fall back.
    /// </summary>
    public static bool Run(string file, params string[] args)
    {
        if (string.IsNullOrWhiteSpace(file))
            return false;

        if (RunOverride is { } handler)
            return handler(file, args ?? Array.Empty<string>());

        try
        {
            var psi = new ProcessStartInfo { FileName = file, UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in args ?? Array.Empty<string>())
                psi.ArgumentList.Add(a);
            return Process.Start(psi) != null;
        }
        catch
        {
            return false;
        }
    }
}
