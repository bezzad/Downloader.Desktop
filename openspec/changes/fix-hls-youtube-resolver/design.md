## Context

The HLS plugin (`Downloader.Desktop.Plugins.Hls`, migrated from the former `Downloader.Plugins` repo)
already implements real YouTube handling, confirmed by reading the current code, not assuming it from the
skill notes:

- `HlsResolver.SupportedHosts` already claims `youtube.com`/`youtu.be`/`m.youtube.com` (and several other
  sites) for page-URL extraction via yt-dlp (`HlsResolver.ResolveViaExtractionAsync`).
- `YtDlpBinary.ExtractJsonAsync` already: (1) provisions yt-dlp itself (cached → PATH → downloaded from the
  yt-dlp GitHub releases on first use), (2) best-effort provisions **deno** (a JS runtime YouTube's
  extraction needs to solve its anti-bot "n challenge" — without it, yt-dlp gets storyboard images only,
  no real formats), (3) tries extraction anonymously first, and (4) on a "needs sign-in" stderr signature
  (`NeedsCookies`), retries with `--cookies-from-browser <browser>` across `chrome, safari/edge, brave,
  firefox` (per-OS order) until one succeeds, throwing a clear "needs a signed-in browser session" error
  only if every browser fails.

So the reported failure is NOT a missing capability — it's one of: (a) no browser on this machine actually
has an active YouTube session, so every cookie-browser attempt legitimately has nothing to offer; (b) a
browser DOES have a session, but yt-dlp can't read/decrypt its on-disk cookie store (a real, current,
widely-documented problem: recent Chrome versions encrypt cookies with OS-level "app-bound encryption" that
external tools can't always decrypt, and Windows/macOS keychain prompts can also block silent access); (c)
deno provisioning silently failed on this machine, so even a successful cookie retry can't get real formats
back; or (d) something else entirely (yt-dlp/YouTube's extractor is a moving target — very small chance the
specific video hit a not-yet-patched extractor gap). Root cause (b) is the most likely given how the ecosystem
has moved, and it's exactly what the author's own suggested fallback addresses: the browser extension runs
INSIDE the browser and can request the live session's cookies through a proper extension API
(`chrome.cookies.getAll`), which reads the browser's live in-memory/session cookie jar rather than parsing
its on-disk (and increasingly encrypted) store — sidestepping the whole decryption problem.

The app already has a browser extension (`src/browser-extension`) that forwards captured links to the app
over a local HTTP API (`Services/LocalApiService`, `/api/add`, `ApiAddRequest`). That channel is the natural
place to also carry cookies, since it already exists and is already trusted (localhost-only, no CORS).

## Goals / Non-Goals

**Goals:**
- Establish real, current ground truth for why a specific YouTube video currently fails, via a gated live
  test that categorizes the failure — before committing to a specific fix.
- If the failure is cookie/session-related (the likely case): let the browser extension supply a live
  session's cookies to the app, which hands them to yt-dlp via a temp Netscape-format cookie file, as a more
  reliable alternative to `--cookies-from-browser`.
- Make the acceptance bar concrete and testable: a specific public YouTube video resolves to a real
  `DownloadPlan` and downloads successfully end-to-end.
- Handle cookies as sensitive data throughout: never logged, written only to a short-lived temp file,
  deleted promptly after use.

**Non-Goals:**
- Broadening this fix to every other host `HlsResolver` claims (Instagram, TikTok, Facebook, Vimeo, Twitch,
  Reddit, Streamable) — explicit follow-up, only once YouTube is solid.
- Building a full "browser extension manages your login sessions" UI/flow — this is a narrow,
  single-purpose hand-off (cookies for one URL's origin, at the moment the user sends that URL to the app),
  not a general session-sync feature.
- Changing yt-dlp's or deno's provisioning mechanism unless the diagnosis step specifically implicates them.
- Any change to the plugin's built-in vs. optional tier or its release/catalog packaging (out of scope,
  already covered by a prior change).

## Decisions

### D1: Diagnose first — a gated live test categorizes the failure before any fix
Add a test (env-var gated the same way the existing GitHub-plugin live test is, e.g. `DLDESKTOP_NET=1`, so
it never runs in CI/offline) that calls `HlsResolver.ResolveAsync` (or `YtDlpBinary.ExtractJsonAsync`
directly, for a faster/narrower signal) against a known-public test video URL, and asserts/logs which of
the following happened: succeeded anonymously; succeeded after a cookie retry (and which browser); failed
because every cookie retry exhausted with no working session; failed with a deno-provisioning warning in
the logs; failed with some other yt-dlp stderr. This is run and read FIRST, manually, to confirm which
branch of the "what's actually broken" tree applies — the subsequent fix work targets whatever it finds.
The test's assertions never inspect or log the downloaded video's actual bytes/content — only structural
facts about the `DownloadPlan` (part count, kind, whether a URL was returned) and the failure category.
- **Alternative considered**: skip diagnosis and go straight to building the cookie hand-off, since it's the
  author's own hypothesis. Rejected — if the actual cause turns out to be deno provisioning or something
  narrower, building the whole extension/API/temp-file pipeline would be wasted, harder-to-review work for
  a problem a two-line fix would have solved.

### D2: Cookie hand-off travels over the existing local API, as an additive optional field
Extend `ApiAddRequest`/the `/api/add` contract with an optional `cookies` field (a list of
`{name, value, domain, path, secure, expires}` objects — the shape `chrome.cookies.getAll` already returns,
so the extension does no reformatting) alongside the existing `url`/`filename`/`path`/`queue` fields. When
present, `LocalApiService` writes them to a temp file in Netscape cookie-file format (the format yt-dlp's
`--cookies FILE` option already understands) scoped to that one download attempt, and threads the file path
through to the plugin resolve call so `YtDlpBinary` can pass `--cookies <file>` — tried BEFORE the existing
`--cookies-from-browser` loop when cookies were supplied (since a live, extension-supplied session is more
likely to work than reading a local browser's on-disk store), falling back to the existing loop if the
supplied cookies don't work either (e.g. they've expired since capture).
- **Alternative considered**: have the extension pass a `Cookie:` HTTP header string instead of a
  structured list. Rejected — yt-dlp's own cookie handling expects a cookie-jar file (`--cookies`), not an
  arbitrary request header override, and a header string loses per-cookie domain/path/secure/expiry
  metadata a proper jar file preserves.

### D3: Extension reads cookies via the `cookies` API, scoped to the captured URL's origin only
The extension requests the `cookies` permission and calls `chrome.cookies.getAll({url})` for exactly the
URL being sent to the app — not a blanket read of all cookies for all sites. This keeps the new permission's
blast radius as narrow as the feature needs (one site, one moment) and matches the existing MV3 manifest's
narrow, purpose-built permission style (`webRequest`, `tabs`, `scripting`, each used for one specific job).
- **Risk accepted**: adding the `cookies` permission does widen what the extension can technically access
  (a user reviewing store permissions will see it) — documented plainly in the extension's
  README/`PRIVACY.md` (cookies are read only for a URL the user explicitly sent to the app, sent only to
  the local app over localhost, never transmitted elsewhere, never logged, and the temp file is deleted
  right after use).

### D4: Cookies are handled as secrets end-to-end
Never log a cookie value (structural test assertions in D1 and any app-side logging log counts/booleans,
never values); the temp cookie file is created with the most restrictive permissions the platform allows,
written just before the yt-dlp invocation, and deleted in a `finally` immediately after (success or
failure) — mirroring how the existing plugin already handles other transient per-download scratch files
(e.g. the multi-part `.parts/` folder cleanup pattern already in `DownloadManager.Plans.cs`).

## Risks / Trade-offs

- **[Risk] The diagnosis (D1) might show the failure is NOT cookie-related** (e.g. deno provisioning, or a
  genuine yt-dlp extractor gap for this specific video). → Mitigation: D1 is explicitly sequenced before any
  cookie/extension work starts; if the cause is different, the fix work retargets to that actual cause and
  the cookie hand-off (D2/D3/D4) is deferred rather than built speculatively.
- **[Risk] Adding a browser `cookies` permission is a real trust/privacy expansion or the extension.** →
  Mitigation: scoped to a single explicit URL at send-time (D3), documented plainly, and the existing
  extension already asks users to trust it with page content/links — this is a narrower addition than
  `<all_urls>` host permissions it already has.
- **[Risk] YouTube-specific reliability work is inherently a moving target** — YouTube changes its
  extraction requirements over time, and even a fully-working fix today can regress later as yt-dlp/YouTube
  both evolve. → Mitigation: the gated live test from D1 becomes a standing (opt-in, non-CI) regression
  check the author can re-run periodically, not a one-time verification.
- **[Risk] Scope creep toward "fix every site."** → Mitigation: explicit Non-Goal; tasks.md below stops at
  YouTube working end-to-end, with broadening called out as separate follow-up work.

## Migration Plan

No data migration. The local API's new `cookies` field is optional and additive — existing callers (the
extension's current URL-only flow, the CLI, any third-party script hitting `/api/add`) are unaffected. The
extension's new permission is a manifest change that ships in the next extension release; users update
through their browser's normal extension update flow (or reload the unpacked extension in developer mode,
per the existing load-it-now instructions).

## Open Questions

- Does the diagnosis step (D1) actually confirm cookie/session failure as the root cause on the author's
  machine, or does it surface something else (deno, a genuine extractor gap)? This gates whether D2-D4 are
  built at all, or whether a narrower fix suffices — run D1 first and let the result decide.
- Should the temp cookie file live under the plugin's existing per-plugin `DataDirectory` (already used for
  cached yt-dlp/deno binaries) or under `Path.GetTempPath()` (matching how other transient downloads, e.g.
  the update-swap archive, are handled elsewhere in the app)? Lean `Path.GetTempPath()` for a short-lived
  secret (less risk of it lingering in a directory that's otherwise meant for durable cached binaries), but
  confirm during implementation.
