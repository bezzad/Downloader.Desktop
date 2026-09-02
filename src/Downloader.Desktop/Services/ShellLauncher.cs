using System;
using System.Diagnostics;
using System.IO;

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
        catch (Exception ex)
        {
            // Best-effort for the CALLER, but not silent for us: a user can only ever report "nothing
            // happened", so without this line there is nothing to diagnose from.
            AppLog.Warn($"Could not open '{target}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Opens a folder in the file manager, trying each mechanism until one actually works.
    ///
    /// <para>The default handler (<see cref="TryOpen"/>) covers an ordinary desktop; <c>gio open</c> is the
    /// explicit fallback for environments where no handler is registered for a directory. Both are allowed
    /// under snap confinement, unlike the D-Bus reveal below.</para>
    /// </summary>
    public static bool OpenFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return false;

        if (TryOpen(folder))
            return true;

        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()
            && RunChecked(TimeSpan.FromSeconds(5), "gio", "open", folder))
            return true;

        AppLog.Warn($"No way to open the folder '{folder}' on this system");
        return false;
    }

    /// <summary>
    /// Opens the containing folder with <paramref name="path"/> selected, falling back to simply opening
    /// the folder when the platform's reveal mechanism is unavailable.
    ///
    /// <para><b>The fallback is the whole point.</b> On Linux the reveal is a D-Bus call to
    /// <c>org.freedesktop.FileManager1</c>, which AppArmor denies to a snap-confined app — and
    /// <c>dbus-send</c> reports success regardless unless it is asked to wait for the reply. That is why
    /// <c>--print-reply</c> and <see cref="RunChecked"/> are both required here: without them the app
    /// believes it revealed the file, skips the fallback, and does nothing at all.</para>
    /// </summary>
    /// <returns>
    /// False when neither the reveal nor the folder could be opened. The caller is expected to TELL the
    /// user: a click that silently does nothing is undiagnosable — for the user, who can only report
    /// "nothing happens", and for us.
    /// </returns>
    public static bool RevealInFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var revealed = OperatingSystem.IsWindows()
            // explorer.exe is documented to return a non-zero exit code even on success, so its result
            // cannot be checked — fire and forget, and never fall back on it.
            ? Run("explorer.exe", "/select,\"" + path + "\"")
            : OperatingSystem.IsMacOS()
                ? RunChecked(TimeSpan.FromSeconds(5), "open", "-R", path)
                : RunChecked(TimeSpan.FromSeconds(5), "dbus-send",
                    "--session",
                    // Without this, dbus-send does not wait for a reply and exits 0 even when the call
                    // was denied. This single flag is what makes the failure detectable.
                    "--print-reply",
                    "--dest=org.freedesktop.FileManager1",
                    "--type=method_call",
                    "/org/freedesktop/FileManager1",
                    "org.freedesktop.FileManager1.ShowItems",
                    "array:string:file://" + path,
                    "string:");

        if (revealed)
            return true;

        AppLog.Warn($"Could not reveal '{path}' — opening its folder instead");
        return OpenFolder(Path.GetDirectoryName(path));
    }

    /// <summary>
    /// Starts a command and waits (briefly) for it to finish, reporting whether it actually SUCCEEDED.
    ///
    /// <para><b>Why this exists:</b> <see cref="Run"/> reports whether the process STARTED, which is not
    /// the same thing and silently broke "open containing folder" for every snap user. Under snap
    /// confinement AppArmor denies the D-Bus call the reveal uses, but <c>dbus-send</c> without
    /// <c>--print-reply</c> does not wait for a reply and so exits 0 anyway — the app concluded it had
    /// revealed the file, never fell back to simply opening the folder, and nothing happened at all.
    /// A caller that has a fallback must use this, not <see cref="Run"/>.</para>
    ///
    /// <para>A command that has not finished within <paramref name="timeout"/> is treated as failed and
    /// killed: everything routed through here is a short-lived helper, so "still running" means stuck.</para>
    /// </summary>
    public static bool RunChecked(TimeSpan timeout, string file, params string[] args)
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

            using var process = Process.Start(psi);
            if (process == null)
                return false;

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* it exited in the meantime */ }
                AppLog.Warn($"'{file}' did not finish within {timeout.TotalSeconds:0.#}s — treating as failed");
                return false;
            }

            if (process.ExitCode != 0)
                AppLog.Warn($"'{file}' exited with code {process.ExitCode}");
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not run '{file}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Starts a specific command with explicit arguments, WITHOUT waiting — for a command that is not
    /// expected to exit (launching a browser). When the caller needs to know whether it worked, and has
    /// a fallback for when it did not, use <see cref="RunChecked"/> instead.
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
