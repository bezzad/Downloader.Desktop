using System;
using System.Diagnostics;
using System.IO;

namespace Downloader.Desktop.Services;

/// <summary>
/// Windows: self-register a Start-menu shortcut on first run. winget's zip/portable install puts the
/// exe on PATH but creates NO Start-menu entry — users reported "installed successfully but I can't
/// find it anywhere". Idempotent (skips when the shortcut exists), best-effort, per-user (no admin).
/// Removed by deleting %APPDATA%\Microsoft\Windows\Start Menu\Programs\Downloader.lnk.
/// </summary>
public static class StartMenuShortcut
{
    public static void EnsureOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                return;
            var programs = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Start Menu", "Programs");
            var lnk = Path.Combine(programs, "Downloader.lnk");
            if (File.Exists(lnk))
                return;
            Directory.CreateDirectory(programs);

            var script = BuildShortcutScript(lnk, exe);
            Process.Start(new ProcessStartInfo("powershell",
                $"-NoProfile -Command \"{script}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            // a missing shortcut must never break startup
        }
    }

    /// <summary>The PowerShell that creates the .lnk (WScript.Shell COM). Pure — unit-tested.
    /// The working dir is split on '\' explicitly so the helper behaves the same when the tests run
    /// it on Linux (Path.GetDirectoryName doesn't parse Windows paths there).</summary>
    internal static string BuildShortcutScript(string lnkPath, string exePath)
    {
        var cut = exePath.LastIndexOf('\\');
        var workDir = cut > 0 ? exePath[..cut] : exePath;
        return "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('" + lnkPath + "'); " +
               "$s.TargetPath='" + exePath + "'; " +
               "$s.WorkingDirectory='" + workDir + "'; " +
               "$s.Description='Downloader — fast multi-connection download manager'; " +
               "$s.Save()";
    }
}
