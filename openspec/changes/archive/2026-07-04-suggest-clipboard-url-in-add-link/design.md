## Context

`AddDownloadItemViewModel` already owns URL parsing: `Urls` is a raw multi-line string, `ParsedUrls` splits it on `UrlSeparators` (`\n \r \t space , ;`), and `CanDownload`/`IsMultiple`/`IsSingleLink` are derived from the parsed count. `TriggerResolve()` already debounces a single-link name/size probe via `UrlResolver`. The view (`AddDownloadItemView.axaml`) binds a single multi-line `TextBox` (`AcceptsReturn="True"`) directly to `Urls` (TwoWay). There is currently no clipboard-reading code anywhere in the app (only clipboard *writes*, e.g. `DialogHelper.CopyTextAsync`).

## Goals / Non-Goals

**Goals:**
- When the dialog opens with `Urls` empty, and the clipboard holds text that parses into ≥1 valid http/https URL(s), show it as a non-committed, visually-distinct suggestion.
- Accept the suggestion with Enter or Tab, at which point it behaves exactly like the user having typed/pasted it (parsing, single-link resolve, etc. all unchanged).
- Never touch `Urls` (the bound, "real" value) until the user accepts — so existing behavior (parsing, `CanDownload`, resolve-on-change) stays exactly as-is for anyone who ignores the suggestion.

**Non-Goals:**
- No change to how multiple already-typed URLs are parsed/merged — the suggestion is offered only pre-population (empty box), never merged into existing text.
- No "smart" filtering of clipboard content beyond "does it parse into ≥1 valid URL with the existing separators and validation" — no new URL-validity heuristics.
- No persistence of clipboard history or "recent suggestions" — this reads the live clipboard once, at dialog-open time.
- Final visual polish (exact placeholder styling/animation) is intentionally left loose for a follow-up design pass with the author; this ships the simplest workable version (dimmed placeholder-style text, Enter/Tab to accept).

## Decisions

- **Read the clipboard once, in the ViewModel constructor, only when the incoming `url` seed is empty.** `AddDownloadItemViewModel` already takes a seed `url` (e.g. from an extension capture or "paste link" box) — clipboard suggestion only applies when that seed is empty, matching "user have no any link added before" from the request. This keeps clipboard access out of the hot path (no polling) and avoids ever overwriting a URL the user or extension already provided.
- **Model the suggestion as a separate `ClipboardSuggestion` string property, not by writing into `Urls`.** Keeping it distinct means `CanDownload`/`ParsedUrls`/`TriggerResolve` are completely unaffected until the user explicitly accepts (at which point the VM just sets `Urls = ClipboardSuggestion`, reusing 100% of existing parsing/resolve logic). This avoids adding an "is this real or a suggestion" flag scattered through the existing parsing path.
- **Reuse the existing `UrlSeparators`/parse logic to validate clipboard content** (same splitting rule, then filter parsed tokens to ones starting with `http://`/`https://`) rather than inventing a new URL regex — keeps "what counts as a URL" consistent across the whole dialog.
- **Placeholder-style rendering via a lightweight overlay `TextBlock`, not a real disabled `TextBox.Watermark`.** Avalonia's built-in `Watermark` is a static string on the control, not bindable to dynamic clipboard content in a way that also needs a distinct dimmed style separate from a normal watermark ("no results" vs "clipboard suggestion") — an overlay `TextBlock` (`IsHitTestVisible="False"`, shown only while `Urls` is empty and a suggestion exists) bound to `ClipboardSuggestion`, positioned over the same cell as the real `TextBox`, is simple and keeps the real `TextBox` untouched.
- **Accept key handling in code-behind (`AddDownloadItemView.axaml.cs`)**, mirroring the existing Enter-handling pattern already used for the multi-line box (per the `downloader-desktop` skill's `AcceptsReturn` + `KeyDown` note) — checked for `Key.Enter` (without Shift, to not conflict with the existing "Shift+Enter = newline" convention if the box already has content) or `Key.Tab`, only while the real box is still empty.

## Risks / Trade-offs

- [Clipboard read on every dialog open could surprise privacy-conscious users] → Mitigation: never auto-inserts, always shown as a dismissible/ignorable suggestion; user must explicitly press Enter/Tab to accept; behavior is identical to today if ignored.
- [Clipboard access API differs slightly across platforms/Avalonia versions] → Mitigation: reuse the same `TopLevel.Clipboard` access pattern already validated for the app's existing clipboard *write* path (`DialogHelper.CopyTextAsync`); wrap the read in try/catch and treat any failure as "no suggestion" (fail open, dialog still fully usable).
- [Clipboard content that looks like a URL but isn't one the user wants (e.g. a URL copied for an unrelated reason) gets accidentally accepted] → Mitigation: suggestion is dimmed/placeholder-styled specifically so it reads as "not yet real text," and requires an explicit keypress to commit — no auto-accept on any timer or focus event.
