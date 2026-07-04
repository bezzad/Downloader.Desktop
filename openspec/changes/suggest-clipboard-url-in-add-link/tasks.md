## 1. ViewModel: clipboard read + suggestion state
- [ ] 1.1 Add a `ClipboardSuggestion` (string, read-only externally) property to `AddDownloadItemViewModel`.
- [ ] 1.2 In the constructor, only when the seed `url` is empty: read the clipboard text (via the `TopLevel`/`Clipboard` API used elsewhere for writes), reuse `UrlSeparators` splitting + http/https filtering to validate it, and set `ClipboardSuggestion` if it parses into ≥1 URL. Wrap in try/catch — any failure means no suggestion, dialog still opens normally.
- [ ] 1.3 Add an `AcceptClipboardSuggestion()` method: sets `Urls = ClipboardSuggestion` (reusing existing parsing/resolve pipeline) and clears `ClipboardSuggestion`.

## 2. View: suggestion rendering + accept key handling
- [ ] 2.1 In `AddDownloadItemView.axaml`, add an overlay `TextBlock` (dimmed/placeholder style, `IsHitTestVisible="False"`) bound to `ClipboardSuggestion`, visible only while the real `Urls` box is empty and a suggestion exists.
- [ ] 2.2 In `AddDownloadItemView.axaml.cs`, extend the existing URL-box `KeyDown` handler: while the box is empty and a suggestion is showing, Enter or Tab calls `AcceptClipboardSuggestion()` and moves focus/caret into the (now-populated) real box; otherwise keep existing Enter/Shift+Enter behavior unchanged.

## 3. Tests
- [ ] 3.1 Headless/unit test: empty seed URL + a fake clipboard with a single valid URL → `ClipboardSuggestion` is set; accepting it sets `Urls` and `CanDownload` becomes true.
- [ ] 3.2 Headless/unit test: empty seed URL + a fake clipboard with multiple URLs (mixed separators) → suggestion contains all of them; accepting parses into the same count via `ParsedUrls`.
- [ ] 3.3 Headless/unit test: non-empty seed URL → no suggestion is read/shown regardless of clipboard content.
- [ ] 3.4 Headless/unit test: clipboard with non-URL text (or empty clipboard) → no suggestion shown, dialog unaffected.

## 4. Wrap-up
- [ ] 4.1 Manually verify in the running app: open Add dialog after copying a URL, confirm placeholder-style suggestion appears and Enter/Tab accepts it; confirm typing over it works normally.
- [ ] 4.2 Note in this change (before archiving) that the exact suggestion visual treatment may be revisited per the author's follow-up discussion.
