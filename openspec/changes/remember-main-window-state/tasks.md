## 1. Persisted model

- [ ] 1.1 Add `Models/WindowLayout.cs` — `IsMaximized`, `Width`, `Height`, `X`, `Y` (all plain
      JSON-friendly properties, no Avalonia types).
- [ ] 1.2 Add `Config.MainWindow` (`WindowLayout`, nullable = "no memory"); leave it `null` in
      `Config.New()` and do NOT null-coalesce it in `EnsureValid()` — absent must stay absent so the
      XAML defaults apply. No `SchemaVersion` bump.
- [ ] 1.3 Test: a config saved and reloaded round-trips the layout, and a config written before this
      feature loads with `MainWindow == null` and no exception.

## 2. Pure layout policy

- [ ] 2.1 Add `Services/WindowLayoutPolicy.cs` with pure statics `Capture(isMaximized, x, y, width,
      height)` and `Resolve(saved, workingAreas, minWidth, minHeight, fallbackWidth,
      fallbackHeight)` returning the layout to apply. No `Window`/`Screens` reference.
- [ ] 2.2 Implement the clamp rules from design §3: size clamped to `[min, largest working area]`;
      title-strip visibility (96×24) against every working area; re-centre on the first working area
      when nothing qualifies; `IsMaximized` preserved alongside the normal geometry; a non-finite,
      zero or negative record treated as no memory.
- [ ] 2.3 Tests `Unit/WindowLayoutPolicyTests` — one per spec scenario: screen gone, screen
      smaller, below minimum, corrupt/non-finite record, partly-off-screen left alone, maximized
      preserved, multi-monitor with a negative-X screen (a monitor left of the primary, which is the
      case a naive `Math.Max(0, …)` clamp breaks).

## 3. Restore at startup

- [ ] 3.1 In `MainViewModel.SetupAppShell()`, before anything shows the window, read
      `window.Screens.All` working areas and apply `WindowLayoutPolicy.Resolve(_config.MainWindow,
      …)` — set `Width`/`Height`/`Position`, then `WindowState` (maximized last, so the normal
      geometry is registered as the restore bounds).
- [ ] 3.2 Guard against a null/empty `Screens` (headless, and some Linux sessions): fall back to the
      XAML defaults rather than throwing.
- [ ] 3.3 Confirm `MainWindow.axaml` still carries the default `Width`/`Height`/`MinWidth`/
      `MinHeight`/`WindowStartupLocation` — they are now the documented fallback, not dead markup.

## 4. Capture on change

- [ ] 4.1 Subscribe once in `SetupAppShell()` to the window's `ClientSizeProperty` and
      `WindowStateProperty` changes and to `PositionChanged`; each handler re-captures into
      `_config.MainWindow` and calls the existing `SaveSoon()`.
- [ ] 4.2 Only capture size/position while `WindowState == Normal`; a maximized or minimized window
      updates `IsMaximized` (minimized ⇒ `false`) and leaves the stored geometry untouched.
- [ ] 4.3 Use an `_applyingLayout` flag so the restore in §3 cannot feed its own events back in.
- [ ] 4.4 Verify no handler can throw (the dispatcher-death rule) — wrap the capture body and log.

## 5. Tests for the wiring

- [ ] 5.1 Headless `UI/WindowStateMemoryTests`: build the shell (`DeferringScheduler` +
      `DesktopLifetimeScope` per the skill's notes), resize/maximize the window, and assert
      `_config.MainWindow` reflects it — including that maximizing does not overwrite the normal
      geometry.
- [ ] 5.2 Headless test: a `Config` carrying a layout is applied to a fresh window at startup
      (maximized case and normal case).
- [ ] 5.3 Confirm each test FAILS against the pre-change code (per the standing "a test that passes
      while the bug survives is broken" rule) before calling the group done.

## 6. Finish

- [ ] 6.1 `dotnet build Downloader.Desktop.sln -t:Rebuild --nologo` → `0 Warning(s)`, `0 Error(s)`.
- [ ] 6.2 Full bounded suite green:
      `timeout -k 30 900 dotnet test Downloader.Desktop.Tests/Downloader.Desktop.Tests.csproj -v q
      --nologo --blame-hang --blame-hang-timeout 180s --blame-crash`.
- [ ] 6.3 Author's manual check (cannot be verified headlessly): maximize → quit from the tray →
      relaunch; move to a second monitor → disconnect it → relaunch; resize → OS restart → relaunch.
- [ ] 6.4 No screenshot refresh expected (no view's appearance changes) — confirm before skipping.
- [ ] 6.5 Commit and push to `develop`; reference issue #15 in the commit message.
