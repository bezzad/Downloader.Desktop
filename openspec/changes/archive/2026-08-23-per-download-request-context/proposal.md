# Proposal — per-download-request-context

Issue #7 (@ray2me123, in the tail of issue #4): let an external tool hand the app a link **together with the
cookies, headers and referer needed to fetch it**, so protected media links (typically an `.m3u8` behind a
signed-in session or a hotlink-protected CDN) actually download.

Split out of `issue4-followups-batch`, which holds the shared analysis for #5/#6/#7. This change covers **#7
only** so it can ship and archive on its own.

## Why

Most of the path already exists and stops one step short of being useful:

- `LocalApiService` accepts a `cookies` array on `POST /api/add` (the `chrome.cookies.getAll` shape).
- Those cookies are written to a temp Netscape file and passed to the **plugin resolver only**
  (`ResolveOptions.CookieFilePath`). They never reach the requests that download the bytes, and they are
  discarded after the first attempt, so a retry is unauthenticated.
- `ApiAddRequest` has no `headers` / `referer` field. Those exist only as **global** values in
  `DownloadSettings` — one referer for every download in the app, which is useless when two downloads need
  different ones.
- `HlsResolver` passes `headers: null` when building its segment plan, so `DownloadPart.Headers` is always
  empty even though `DownloadManager.Plans.ApplyHeaders` already copies it into
  `RequestConfiguration.Headers`.

The core engine needs no change: `RequestConfiguration` already exposes `Headers` (`WebHeaderCollection`),
`CookieContainer`, `Referer` and `UserAgent`, and `SocketClient` applies all four per download.

## What changes

1. **API surface** — `POST /api/add` also accepts `headers` (a string→string object) and `referer`.
2. **Model** — a per-item request context on `DownloadItem`: cookies and headers are **transient**
   (`[JsonIgnore]`, in memory for the session, never written to `config.json` or the log); **referer
   persists** (it is not a credential, and it makes a restart-resume still work).
3. **Download path** — `DownloadManager.Start` applies the item's cookies (→ `CookieContainer`), headers and
   referer to that download's `RequestConfiguration`, so the bytes are fetched with the same context as the
   resolve. Per-item values win over the global `DownloadSettings` equivalents.
4. **Resolver path** — `ResolveOptions` carries the request context; `HlsResolver` uses it for its playlist
   fetches and stamps it on every produced `DownloadPart`, so segments are fetched authenticated too.
5. **Retry** — cookies survive in memory for the session, so retrying a failed protected download re-sends
   them instead of silently going anonymous.

## Decisions (settled with the author before implementation)

- **No `downloader://` URL scheme.** The reporter suggested one. A scheme forces secrets through a command
  line, where they land in process listings and shell history; the loopback POST already carries them out of
  band. If a scheme is ever wanted for plain URLs (no credentials), it is its own change.
- **App side only.** The browser extension is not modified here; it keeps sending cookies as it does today.
  Wiring the extension to capture the page referer/user-agent is a follow-up.
- **Referer persists; headers and cookies stay transient.** Headers can carry `Authorization`/API keys, so
  they get the same treatment cookies already have: memory only, gone on restart.

## Out of scope

- Registering an OS URL scheme (see above).
- Browser-extension changes (follow-up).
- #5 (MPEG-DASH) and #6 (refresh an expired link) — still tracked in `issue4-followups-batch`.
- Any change to the `bezzad/downloader` engine.

## Status at archive

Implemented and archived on 2026-08-23. Build clean; 436 app tests, 33 extension unit tests and 7 Playwright
e2e tests green (the e2e suite needs `--workers=1`).

**Task 4.4 was NOT done — archived anyway at the author's call.** The end-to-end check (send a real protected
`.m3u8` with cookies + referer through `POST /api/add` and watch it download) needs a live signed-in session
on a real site and could not be run. Everything below it is covered by unit tests, but the "a protected link
now actually downloads" claim is unverified against a real server. If a report comes in that a protected link
still fails, start there rather than assuming the plumbing is wrong: `ApplyRequestContext` is tested, the
untested link is whether the site wants something we are not sending.
