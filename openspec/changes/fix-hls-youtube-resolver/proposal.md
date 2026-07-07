## Why

The author reports that downloading a YouTube video through the app's HLS/video-site plugin fails, and
suspects it needs a signed-in cookie session the plugin can't currently obtain reliably. Investigation
confirms the plugin already has YouTube-specific handling (host claiming, a JS-runtime for YouTube's
anti-bot "n challenge", and a `--cookies-from-browser` retry loop across installed browsers — all migrated
intact from the plugin's previous repo) — so this isn't a missing feature, it's a real-world reliability gap
in how that session is obtained. Reading local browser cookie databases from disk is inherently fragile:
modern browsers (Chrome 127+'s "app-bound encryption", for one) actively work against exactly this kind of
external read, and it only works at all if a supported browser on the SAME machine happens to have an
active, logged-in session for the site. The author's own suggested fallback — have the existing browser
extension hand the app a real, live session's cookies/headers, since the extension runs inside the actual
browser and can request them through a proper extension API instead of parsing an on-disk file — is a sound
direction and is scoped here as the primary fix path if the on-disk approach proves to be the actual
blocker. Priority is getting YouTube reliably working first; broadening to other yt-dlp-supported sites
is an explicit, separate follow-up.

## What Changes

- **Diagnose before fixing.** Add a gated (network-touching, opt-in via an environment variable — the same
  pattern already used by the existing GitHub-plugin live test) integration test in the Hls plugin's test
  project that runs the real resolver/extractor against a known public video URL and reports which stage
  fails and why (no cookies needed at all / needs cookies but no local browser has a session / has a
  session but decrypting its cookie store failed / deno provisioning failed / something else) — without
  ever logging or persisting the video's actual content, only pass/fail and the failure category. This
  establishes real, current ground truth before any fix is attempted, and becomes the regression test the
  fix must turn green.
- **If the failure is a login/cookie-session problem** (the author's suspicion, and the most likely outcome
  given how fragile on-disk browser cookie reading has become): extend the existing browser extension —
  which already forwards links to the app over the local API — to also capture the current session's
  cookies for the target site (via the extension `cookies` API, which reads live from the browser's active
  session rather than an on-disk, possibly-encrypted database) and pass them to the app alongside the URL.
  The app writes them to a short-lived temp file in Netscape cookie-file format and passes `--cookies
  <file>` to yt-dlp — a more robust alternative to `--cookies-from-browser` for exactly the cases where the
  latter fails. The temp file is deleted immediately after use; cookie values are never logged.
- **If the failure is something else** (deno provisioning, a yt-dlp version/extractor gap, or another
  cause the diagnosis step surfaces): scope a targeted fix for that specific cause instead — the diagnosis
  step exists precisely so the actual root cause drives the fix, not an assumption.
- **YouTube first, other sites after.** This change's acceptance bar is a real YouTube video resolving and
  downloading successfully end-to-end. Extending the same fix to the plugin's other claimed hosts
  (Instagram, TikTok, Facebook, Vimeo, etc.) is called out as explicit follow-up work, not blocking this
  change.

## Capabilities

### Modified Capabilities
- `video-site-extraction`: adds a requirement that YouTube video-page URLs resolve and download
  successfully, including when the site requires a signed-in session, via a browser-supplied cookie session
  when on-disk browser cookie reading is unavailable or fails.

## Impact

- **Code**: `Downloader.Desktop.Plugins.Hls` (`YtDlpBinary`, possibly `HlsResolver`/`IYtDlp` to accept an
  optional cookie-file path), `src/browser-extension` (`common.js`, manifest `cookies` permission), the
  app's local API (`Services/LocalApiService`/`ApiAddRequest`, a new optional cookies field alongside the
  existing `url`/`filename`/`path`/`queue` fields).
- **Tests**: a new gated live-network diagnostic test (`Downloader.Desktop.Tests/Plugins/Hls`); once the
  fix lands, this same test is the regression guard.
- **Docs**: `docs/writing-plugins.md`/HLS plugin notes on the cookie hand-off, and a privacy note (extension
  README/`PRIVACY.md`) since this is the first time the extension would read cookie data.
- **Security note**: cookies are sensitive credentials — this design keeps them local-only (the existing
  localhost-bound API, no CORS), never logs them, and deletes the temp cookie file promptly after use.
- **No breaking changes**: the browser extension's new capability is additive (existing URL-only flow keeps
  working for sites that don't need a session); the local API's new field is optional.
