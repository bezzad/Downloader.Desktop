# Design — no-shell-spawn-hardening

## Decision

Replace every shell spawn with the direct API the shell script was reaching for, and enforce it with a
source scan. The principle: **an unsigned desktop app must not produce a process tree that looks like
an attack chain**, even when every individual step is benign.

## The four replacements

### 1. Notifications — `WindowsNotifier`

`powershell.exe -EncodedCommand …` → `Shell_NotifyIconW(NIM_ADD | NIM_MODIFY)` with `NIF_INFO`.

- Windows 10/11 render a `NIF_INFO` balloon as a real toast and keep it in the Action Center, so the
  "works while hidden in the tray" contract is preserved. That was the reason for going native in the
  first place, and an in-app-only toast would have regressed it.
- The shell needs an HWND + icon id to hang a balloon on, so a hidden **message-only** window
  (`HWND_MESSAGE`) is created lazily and cached. No message pump is required because we never request
  click callbacks; the `WNDPROC` delegate is held in a static field so the shell can never call into
  freed memory.
- The icon comes from `ExtractIconExW(Environment.ProcessPath)` so notifications are attributed to this
  app, falling back to `IDI_APPLICATION`. Same motive as `MacNotifier` posting in-process.
- `dwInfoFlags` now carries `NIIF_ERROR` vs `NIIF_INFO`, so `NotificationService` threads its existing
  `isError` flag through — a small behavior gain that the old script path ignored.
- Title/message are clamped to the fixed shell buffers (`szInfoTitle` 64, `szInfo` 256) so marshalling
  can never overrun.

Rejected: adding a `net10.0-windows10.0.x` TFM for real WinRT toasts. It would fork the build matrix for
every platform and pull in CsWinRT, against a one-TFM design that CI, publishing and the macOS bundle all
depend on. Rejected: dropping to an in-app Avalonia toast — a regression, as above.

### 2. Start-menu shortcut — `StartMenuShortcut`

`powershell` + `WScript.Shell` → `IShellLink` + `IPersistFile` COM, in-process.
`BuiltInComInteropSupport` is already `true` in the app csproj and the app is not trimmed/AOT, so
`ComImport` interop works as-is. Interface members are declared in full vtable order (including unused
ones) because the order *is* the ABI. The pure part — deriving the shortcut's working directory — stays
`internal` and unit-tested; `BuildShortcutScript` is gone.

### 3. Run-at-startup — `StartupService`

Spawned `reg.exe` (both write and read) → `Microsoft.Win32.Registry`. The API is in the shared framework
on the platform-neutral `net10.0` TFM and annotated `[SupportedOSPlatform("windows")]`, so the Windows
branches are annotated to match. Writing an autostart key is inherently persistence-shaped and will
always carry some weight; doing it through a spawned command-line tool added a parent→child chain on top
for no benefit. `IsEnabled()` also gets cheaper and more accurate: a real value read instead of scraping
`reg.exe` stdout for a substring.

### 4. Update swap — `UpdateService.BuildWindowsScript`

`powershell -Command Expand-Archive` → `"%SystemRoot%\System32\tar.exe" -x -f … -C …`.

- `tar.exe` is in-box from Windows 10 1803 (bsdtar, which reads zip). Same one-line role in the script.
- Invoked by **absolute path**, which also closes a PATH-hijacking hole the bare `powershell` had.
- The `ping`-based sleep and the ~60-attempt retry loop are untouched — they exist because a tray-held
  instance or an AV scan can keep the exe locked, and that hazard is unchanged.
- The detached `cmd` script itself stays: a running process cannot replace its own executable. `cmd`
  without PowerShell is a far weaker signal, and there is no alternative that doesn't ship a second
  updater binary.

## The guardrail — `NoShellSpawnTests`

A text scan over the **shipping** source (`Downloader.Desktop`, `Downloader.Desktop.Plugins`,
`Downloader.Desktop.Plugins.Abstractions`); the test project is excluded because it necessarily names the
patterns it bans.

Design choices worth recording:

- **Comments are stripped, string literals are not.** The ban is on *doing* the thing, not on explaining
  why we don't — and those explanations are exactly what stops the next session reintroducing it. But a
  banned pattern inside a string literal is real code: that is precisely how the old
  `Expand-Archive` reached users. The stripper is string-aware (verbatim strings, escapes) so a `//` in a
  URL literal can't swallow the rest of a line, and it blanks comments to spaces so reported line numbers
  stay accurate.
- **Scanning text, not syntax.** A pattern can hide in a helper, a constant, or a generated script body;
  a text scan catches all of them, and this file's own first run caught two leftovers.
- **The allow-list is empty and keyed exactly** (`relative/path.cs::pattern`). Every previous use had an
  in-process alternative, so an addition should require a written reason and a review.
- **The scanner is itself tested** (`Scanner_actually_matches_the_patterns_it_bans`,
  `Comment_stripper_keeps_literals_and_line_numbers`). A guardrail that silently stops matching is worse
  than none.

Banned: `powershell`, `pwsh`, `Expand-Archive`, `WScript.Shell`, `-EncodedCommand`, `cmd /c`,
`--cookies-from-browser`, and spawning `reg.exe`.

## What this does not fix

The binaries are still unsigned, which is the root aggravator Bitdefender's own timeline points at
(`Downloader.exe (unsigned)`). Behavioral engines grade the same actions far more leniently for a signed,
reputable publisher. Authenticode signing needs a certificate and a `release.yml` step and is out of
scope here; until it lands, keeping the process tree flat is what we control.
