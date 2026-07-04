## Why

The most common way a user starts a download is: copy a link somewhere on the web, then open the app and paste it into the Add dialog. Today the Add dialog (`AddDownloadItemView`) never looks at the clipboard — the user must remember to paste manually every time. When the dialog opens and its URL box is still empty, and the clipboard already holds something that looks like a URL (or a newline-separated list of URLs), the app can save that manual step by offering the clipboard content as a one-key-away suggestion.

## What Changes

- When `AddDownloadItemView` opens and its multi-line URL box (`Urls`) is empty, the app reads the current clipboard text via Avalonia's clipboard API.
- If the clipboard content parses into one or more valid http/https URLs (reusing the existing `AddDownloadItemViewModel.UrlSeparators`/`ParsedUrls` splitting logic), it is offered as a suggestion rather than auto-inserted — shown as dimmed/placeholder-style inline text in the box (distinct from real typed/pasted foreground text) so the user can tell at a glance it's a suggestion, not committed input.
- Pressing **Enter** or **Tab** while the suggestion is showing and the box is still empty accepts it, populating `Urls` with the real (non-placeholder) text and continuing normal parsing/resolution (`TriggerResolve`) as if the user had pasted it.
- If the user starts typing/pastes something else, or the clipboard doesn't contain a URL, no suggestion is shown — behavior is unchanged from today.
- The suggestion is offered only when the dialog opens with **no existing URLs already entered** (per this request's "user have no any link added before" condition) — it never overwrites or appends to text the user has already entered.
- This is a first pass; further refinement of the exact visual treatment is expected in a follow-up discussion with the author (noted in the request) — this change ships the simplest version (placeholder-style dimmed text + Enter/Tab-to-accept) and keeps the design isolated so it can be iterated on without touching parsing/download logic.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `add-download`: the Add dialog gains clipboard-URL suggestion behavior when opened with an empty URL box.

## Impact

- `src/Downloader.Desktop/Views/AddDownloadItemView.axaml` / `.axaml.cs` — new placeholder-suggestion rendering and Enter/Tab accept handling on the URL `TextBox`.
- `src/Downloader.Desktop/ViewModels/AddDownloadItemViewModel.cs` — new clipboard-read-on-open logic, reusing existing `UrlSeparators`/`ParsedUrls` for validation; no change to `StartDownload`/engine wiring.
- No new dependency: Avalonia's `TopLevel.Clipboard` API already covers read access; no platform-specific code needed beyond what's already used for clipboard writes elsewhere (`DialogHelper.CopyTextAsync`).
