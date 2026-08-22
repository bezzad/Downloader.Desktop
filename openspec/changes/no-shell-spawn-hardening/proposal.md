# Proposal — no-shell-spawn-hardening

## Problem

Bitdefender's Advanced Threat Defense blocked and quarantined Downloader on a clean Windows 11 machine
([issue #4](https://github.com/bezzad/Downloader.Desktop/issues/4)). It reported `ATC4.Detection` /
`SuspiciousBehavior.30C90CB86FF01125` with this timeline:

```
explorer.exe (Microsoft-signed)
    → Downloader.exe (unsigned)
        → powershell.exe
            → conhost.exe
```

Nothing the app did was malicious. But the *shape* of what it did is what behavioral engines score, and
we were producing an unusually bad shape for an unsigned binary:

| Action | How it was done | How it reads to an AV engine |
| --- | --- | --- |
| Post a toast notification | `powershell.exe -EncodedCommand <base64>` | Hidden, obfuscated script execution (T1059.001) |
| Create a Start-menu shortcut | `powershell` + `WScript.Shell` COM | Script-host abuse + persistence-shaped write |
| Enable run-at-startup | spawned `reg.exe` to write the HKCU Run key | Registry persistence via a command-line tool |
| Apply an update | detached `.cmd` → `powershell Expand-Archive` over the app's own exe | Self-replacing binary |

The reporter had just installed the HLS plugin, and `PluginsViewModel` posts a notification on install
success — which is exactly why the detection fired *then*. The quarantined files under
`C:\ProgramData\Microsoft\NetFramework\BreadcrumbStore\` and the AMD DXCache are not written by us at
all; they were swept up by Bitdefender's behavioral rollback, confirming a generic verdict rather than a
signature match.

Two aggravating factors were separately in play and are worth recording: the released Windows binaries
are **not Authenticode-signed**, and HLS plugin 1.4.0 (before 2.0.0) downloaded and executed
`yt-dlp.exe` + `deno.exe` and read Chrome/Edge/Brave/Firefox cookie stores via
`--cookies-from-browser` — literal infostealer behavior. The plugin rewrite already removed the latter.

## What this change does

Every one of those four actions had a direct, in-process API alternative. Use them, and add a guardrail
so the pattern cannot silently return.

- Toasts → `Shell_NotifyIconW` with `NIF_INFO` (in-process P/Invoke; Windows 10/11 surface it as a real
  toast and keep it in the Action Center), hung on a hidden message-only window.
- Start-menu shortcut → the shell's `IShellLink` + `IPersistFile` COM objects, in-process.
- Run-at-startup → `Microsoft.Win32.Registry`, in-process.
- Update extraction → the in-box `tar.exe` (bsdtar reads zip), by its absolute `%SystemRoot%` path so
  there is no PATH-hijacking window either.
- **Guardrail**: `NoShellSpawnTests` text-scans the shipping source (app + all plugins) and fails the
  build on `powershell`, `pwsh`, `Expand-Archive`, `WScript.Shell`, `-EncodedCommand`, `cmd /c`,
  `--cookies-from-browser`, or spawning `reg.exe`. Its allow-list is empty by design.
- **Standing rule** recorded in `CLAUDE.md` / `AGENTS.md` and the project skill, so no future session
  reintroduces it.

## Non-goals / still open

- **Authenticode signing of the Windows builds** is the remaining root cause and is not addressed here.
  It needs a certificate (Azure Trusted Signing) and a `release.yml` step; it also fixes SmartScreen.
  Tracked separately.
- The update swap still runs through a detached `cmd` script — unavoidable, because the app cannot
  replace its own running executable. It no longer involves PowerShell.
- Reporting the false positive to Bitdefender is a follow-up action for the author.

## Verification limits

The three Windows code paths (`Shell_NotifyIconW`, `IShellLink`, `Registry`) **cannot be executed on
this dev box or in CI** — there is no Windows runner. They are written fail-soft (any failure returns
false / is swallowed, exactly as before) and their pure parts are unit-tested, but the behavior itself
needs a manual smoke test on Windows: post a notification, delete `Downloader.lnk` and relaunch, toggle
run-at-startup, and take an update. This is called out in `tasks.md` rather than assumed done.
