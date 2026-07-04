## 1. Regenerate flag assets
- [ ] 1.1 Regenerate all 16 flag PNGs (`en, fa, es, fr, ar, eo, tr, zh, ja, hi, ru, ko, pt, de, it, az`) at a higher resolution (e.g. 90×60, 3x the current 30×20), same file names, same `Assets/flags/` folder.
- [ ] 1.2 Verify each regenerated flag still matches its correct country/design mapping (en→US, pt→Brazil, ar→UAE, eo→Esperanto star) and that `fa` stays a plain green/white/red tricolor with no emblem.

## 2. Verify in-app
- [ ] 2.1 Run the app (or the headless capture test) and visually confirm the Language combobox renders crisp flags at normal and HiDPI scale.
- [ ] 2.2 Confirm `Downloader.Desktop.Tests` still pass (`Flag != null` assertion at `AppTests.cs:744`), and add a lightweight assertion on the new pixel dimensions if useful.

## 3. Wrap-up
- [ ] 3.1 Regenerate `docs/screenshots/` only if a capture actually shows the language dropdown open; otherwise note that no screenshot refresh was needed.
- [ ] 3.2 Update the `downloader-desktop` skill's flags note (`Round 18 patterns`) with the new resolution if it changes the generation approach worth caching for next time.
