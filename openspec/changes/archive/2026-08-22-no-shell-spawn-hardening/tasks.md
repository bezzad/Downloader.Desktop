# Tasks — no-shell-spawn-hardening

- [x] 1. `WindowsNotifier`: replace the spawned PowerShell toast with in-process `Shell_NotifyIconW`
      (`NIF_INFO`) on a cached hidden message-only window; own exe icon via `ExtractIconExW`; clamp text
      to the shell buffers; thread `isError` → `NIIF_ERROR` from `NotificationService`
- [x] 2. `StartMenuShortcut`: write the `.lnk` via in-process `IShellLink`/`IPersistFile` COM; drop
      `BuildShortcutScript`, keep `ResolveWorkingDirectory` pure + unit-tested
- [x] 3. `StartupService`: write/read/delete the HKCU Run value via `Microsoft.Win32.Registry` instead of
      spawning `reg.exe` (also makes `IsEnabled()` a real value read, not stdout scraping)
- [x] 4. `UpdateService.BuildWindowsScript`: extract with the in-box `%SystemRoot%\System32\tar.exe` by
      absolute path instead of `powershell Expand-Archive`; retry loop and `ping` sleeps unchanged
- [x] 5. Guardrail `NoShellSpawnTests`: scan shipping source (app + plugins) for `powershell`, `pwsh`,
      `Expand-Archive`, `WScript.Shell`, `-EncodedCommand`, `cmd /c`, `--cookies-from-browser`, spawned
      `reg.exe`; comment-stripping is string-literal-aware and line-number preserving; empty allow-list;
      the scanner and stripper are themselves tested
- [x] 6. Update `StartMenuShortcutTests` + `UpdateSwapScriptTests` to the new behavior (and assert the
      swap script contains no PowerShell)
- [x] 7. Standing rule in `CLAUDE.md` / `AGENTS.md` + the `downloader-desktop` skill: never spawn a
      shell, never read browser data, never download-then-execute unverified binaries, absolute paths
      only; points at the guardrail test
- [x] 8. `dotnet build` clean and `dotnet test` green (420 tests)

## Not done — deliberately out of scope

- [ ] 9. **Authenticode-sign the Windows builds** (Azure Trusted Signing + a `release.yml` step). This is
      the remaining root cause — Bitdefender's timeline flags `Downloader.exe (unsigned)` — and it also
      fixes SmartScreen. Needs a certificate the author must obtain, so it cannot be done from here.
- [ ] 10. **Manual Windows smoke test** of the three unverifiable paths (no Windows runner in CI): post a
      notification; delete `%APPDATA%\…\Start Menu\Programs\Downloader.lnk` and relaunch; toggle
      run-at-startup on/off and check `HKCU\…\Run`; take an update end to end. All three are fail-soft, so
      a mistake degrades silently rather than crashing — which is exactly why it needs eyes on Windows.
- [ ] 11. **Report the false positive to Bitdefender** (and re-test once signing lands).
