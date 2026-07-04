## 1. ViewModel: clipboard read + suggestion state
- [x] 1.1 Added a `ClipboardSuggestion` (string, private setter) property to `AddDownloadItemViewModel`, plus `ShowClipboardSuggestion` (visible only while box empty + suggestion exists) and `ClipboardSuggestionReady` (a `Task` tests can await for the async probe).
- [x] 1.2 The constructor kicks off an async clipboard probe only when the seed `url` is empty. Clipboard read is an injectable `Func<Task<string>> readClipboard` param (defaults to reading `DialogHelper.MainWindow`'s clipboard) — matches the existing injectable `resolveFileInfo` pattern and makes it unit-testable. Reuses the extracted static `SplitUrls` + a new `IsHttpUrl` filter (http/https only). Wrapped in try/catch (fail open). NOTE: the read had to go through `DialogHelper.MainWindow` (not `View`) because `View` isn't set until *after* construction.
- [x] 1.3 Added `AcceptClipboardSuggestion()`: sets `Urls = ClipboardSuggestion` (reusing the full parse/resolve pipeline) and clears the suggestion.

## 2. View: suggestion rendering + accept key handling
- [x] 2.1 In `AddDownloadItemView.axaml`, wrapped the URL `TextBox` in a `Panel` with an overlay `TextBlock` (accent-colored, `Opacity=0.65`, `IsHitTestVisible="False"`, ellipsis-trimmed) bound to `ClipboardSuggestion` + a small bottom-right "Press Enter to use clipboard link" hint, both gated on `ShowClipboardSuggestion`. The native `PlaceholderText` is routed through a new VM `LinksPlaceholder` that blanks while the suggestion shows, so the overlay and the built-in placeholder (both render only when empty) don't collide. Added `Add_ClipboardHint` to `en.json` (other locales fall back to English).
- [x] 2.2 Added an `OnUrlBoxKeyDown` handler in `AddDownloadItemView.axaml.cs`: while `ShowClipboardSuggestion` is true, Enter or Tab calls `AcceptClipboardSuggestion()` and moves the caret to the end of the now-populated box; otherwise it falls through to normal typing (Enter/Shift+Enter insert newlines in this multi-line box). (This view had no prior KeyDown handler — the "existing" one referenced in the design was the MainWindow top-box handler, which I mirrored.)

## 3. Tests
- [x] 3.1 `Add_dialog_suggests_single_clipboard_url_and_accepts_it` — empty seed + single valid URL → `ClipboardSuggestion` set, `CanDownload` still false until accept; after accept `Urls` set + `CanDownload` true.
- [x] 3.2 `Add_dialog_suggests_multiple_clipboard_urls_mixed_separators` — space/comma-separated clipboard → `IsMultiple` true after accept, all three URLs present.
- [x] 3.3 `Add_dialog_ignores_clipboard_when_seed_url_present` — non-empty seed → no suggestion, `Urls` unchanged.
- [x] 3.4 `Add_dialog_no_suggestion_for_non_url_clipboard` — prose clipboard → no suggestion. (All 4 in `AppTests.cs`, awaiting `ClipboardSuggestionReady`; full suite 183/183 green.)

## 4. Wrap-up
- [x] 4.1 Full GUI interaction isn't feasible in this headless environment; verified via the 4 unit tests above (covering suggest/accept/ignore/no-suggest paths incl. the `CanDownload`-stays-false-until-accept invariant), a clean build, and the earlier headless launch smoke check. The Add dialog isn't part of the screenshot capture set, so no `docs/screenshots/` refresh was needed.
- [x] 4.2 Per the request and design, the exact visual treatment (dimmed accent overlay + Enter/Tab hint) is a first pass and may be revisited in the author's follow-up discussion; the parsing/download path is untouched so the visuals can be iterated in isolation.
