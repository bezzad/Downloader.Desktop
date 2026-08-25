## Why

v2.5.0 shipped the per-download request context (issue #7), and the reporter came back on
2026-08-24 with a follow-up (issue #7 comment 5398393968) describing three symptoms from real
use. All three are confirmed defects in our code, not in his setup:

1. He hands us cookies through the **GET** form of `/api/add` (a third-party capture tool's
   "invoke application" template). `ApiAddRequest.FromQuery` parses `url`, `filename`, `path`,
   `queue`, `start` and `referer` — and **silently discards `cookies` and `headers`**. His
   session hand-off has never worked; only the referer gets through, which is exactly why he
   reports "works with normal videos most of the time" and fails on gated ones.
2. He observed that **Pause does not stop a multi-part/HLS download**. He later retracted this
   as his own mistake; he was right the first time. `Pause` pauses only `vm.Download` — the most
   recently published part engine — while three other segments keep running, and the plan runner
   only ever checks for *Stopped*, so it keeps launching the rest of the playlist while the row
   reads "Paused" and its progress bar is frozen.
3. An encrypted HLS stream downloaded and then **failed at around 99%**. Every segment is fetched
   with the download's cookies/referer, but the AES-128 key request at assembly time goes out of a
   bare `HttpClient` with no context at all — so on a protected origin the key fetch is the one
   request that can fail, and it happens at the very end. Strong suspect, not yet proven from his
   log.

## What Changes

- `GET /api/add` accepts `cookies` and `headers`, so the query form carries the same per-download
  context the JSON body already does. `cookies` in the query is the browser **Cookie-header form**
  (`name=value; name=value`) that capture tools emit, not the JSON array shape.
- The add response reports how much context was accepted, so a caller can tell a working hand-off
  from a silently-dropped one.
- **Pause genuinely pauses a multi-part download**: every in-flight part engine is paused, and the
  plan runner starts no new part while the row is paused. Resume continues from where it stopped.
- The HLS post-processor's AES-128 key request carries the download's request context, like every
  other request made for that download.

## Capabilities

### New Capabilities
<!-- none: all three defects are failures to meet requirements that already exist -->

### Modified Capabilities
- `local-api`: the `/api/add` GET form gains `cookies` and `headers`; the success response reports
  the accepted context.
- `request-context`: "every request made for the download" is made explicit and binding — it covers
  the query form of the API, and requests made during assembly, not only the byte transfers.
- `plugins`: pausing a multi-part plan must halt **all** in-flight parts and start no further ones.

## Impact

- `src/Downloader.Desktop/Services/LocalApiService.cs` — `ApiAddRequest.FromQuery`, a Cookie-header
  parser, the `201` response body.
- `src/Downloader.Desktop/Services/DownloadManager.cs` — `Pause`/`Resume` over the active part set.
- `src/Downloader.Desktop/Services/DownloadManager.Plans.cs` — an `isPaused` gate beside
  `isCancelled`; track the active engines rather than only publishing the latest.
- `src/Downloader.Desktop.Plugins/Downloader.Desktop.Plugins.Hls/` — `ConcatRecipe` carries key
  request headers; `HlsPostProcessor` uses them. Plugin `<Version>` bumps (standing rule).
- `docs/local-api.md` — the query form's context parameters.
- No change to the persistence split: cookies and headers stay transient, referer stays persisted.
