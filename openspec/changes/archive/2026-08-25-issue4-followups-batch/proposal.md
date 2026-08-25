# Proposal — issue4-followups-batch

Three feature requests raised by @ray2me123 in the tail of issue #4 (the Bitdefender report, fixed in
v2.4.0 and closed). Each has its own tracking issue; the issues deliberately state only the request and
the current state, so **this document is where the technical analysis lives**.

- #5 — MPEG-DASH (`.mpd`) support
- #6 — refreshing an expired download link
- #7 — accepting a link together with its cookies / headers / referer (Cat Catch interop)

## Feasibility: desktop app vs. core engine

The first question asked was whether these belong here or in the `bezzad/downloader` engine. Verified by
reading the engine source (`src/Downloader`, tip of `main`): **all three are app-side. The core library
needs no change for any of them.** Evidence per item below.

## 1. Cookies / headers / referer per download (#7)

**Request.** Let an external tool (Cat Catch was named) hand the app a link *plus* the cookies, headers and
referer needed to fetch it, so protected m3u8/HLS links actually download. The reporter suggested a
`downloader://…?params` URL scheme.

**Current state.** Most of the path already exists and stops one step short of being useful:

- `LocalApiService` listens on `127.0.0.1:15151–15155`; `POST /api/add` already accepts a `cookies` array
  (the `chrome.cookies.getAll` shape) alongside url/filename/path/queue/mirrors/start.
- Those cookies are written to a temp Netscape file and passed to the **plugin resolver only**
  (`ResolveOptions.CookieFilePath`). They never reach the requests that download the bytes.
- `ApiAddRequest` has no `headers` / `referer` field at all — those exist only as global values in
  `DownloadSettings`, not per item.
- `HlsResolver` hard-codes `headers: null` when building its segment plan, so `DownloadPart.Headers` is
  always empty even though `DownloadManager.Plans.ApplyHeaders` already copies it into
  `RequestConfiguration.Headers`.

**Engine side.** Nothing missing: `RequestConfiguration` exposes `Headers` (`WebHeaderCollection`),
`CookieContainer`, `Referer`, `UserAgent`, `Authorization`, `Credentials` and `ClientCertificates`, and
each download builds its own configuration instance.

**Scope.** Add `headers` + `referer` to the add request and persist them on `DownloadItem`; apply the
item's cookies/headers/referer to the engine's `RequestConfiguration` for the download itself, not just
the resolver; pass them into resolver-produced plans so segment parts carry them.

**On `downloader://`.** A URL scheme forces secrets through a command line, where they land in process
listings and shell history; the loopback POST already carries them out of band. Recommendation is to
extend the local API rather than register a scheme — but the reporter asked for the scheme, so this is the
author's call, not a settled decision. If a scheme is still wanted for plain URLs (no credentials), it can
be a separate change.

## 2. Refreshing an expired download link (#6)

**Request.** A very large file downloaded over several days outlives its signed/time-limited URL. Give the
existing download a fresh link and continue it instead of losing the partial file.

**Current state.** Detection exists, repair does not. `DownloadManager.LooksExpiredOrInvalid` flags a
"completed" download that is small and returns HTML (→ Failed, `Error_LinkExpired`), and
`LooksCorruptedAfterResume` catches a resume that finishes short of the known size. But the primary URL of
an item cannot be edited — the Details window edits only mirrors (`Urls[1..]`), never `Urls[0]`.

**Engine side — already supported.** `IDownloadService` exposes
`DownloadFileTaskAsync(DownloadPackage package, string[] urls, CancellationToken ct)`, and
`AbstractDownloadService.InitialDownloader` assigns `Package.Urls` from those new addresses while keeping
the package's chunks and received bytes. Resuming an existing partial file against a **different** URL is a
first-class engine capability today.

**Mirrors are not a substitute** (checked because it looks like one): `DownloadService.GetChunksTasks`
assigns `RequestInstances[i % RequestInstances.Count]` and pins each chunk to its request for the whole
transfer; `DownloadChunk`'s catch records the error and cancels the siblings without ever retrying against
another URL; and the only retry that exists — the single-connection fallback from engine issue #231 — fires
only on a transient transport error with `ReceivedBytesSize == 0` and rebuilds one whole-file chunk that
again takes `RequestInstances[0]`. The file-info probe is `GetFileInfoAsync(RequestInstances.First(), …)`,
so an expired primary fails before any mirror is consulted. Mirrors are load spreading, not failover.

**Scope.** A "Replace link" action on a failed/stopped row; validate the new link before committing
(resolve it and compare reported size, and ETag where available, against the item's known size — resuming
against a different file silently corrupts the output); persist onto `DownloadItem.Urls[0]` and resume
through the existing package rather than restarting.

**Possible engine follow-up, out of scope here.** Automatic failover across the URL list when one URL starts
returning 403/410 mid-transfer *would* be a core-library change. This change is the manual, user-driven
refresh only.

## 3. MPEG-DASH support (#5)

**Request.** Download DASH streams the way HLS is supported today.

**Current state.** Not supported anywhere: `HlsResolver` parses `#EXT-X-STREAM-INF` / media playlists only,
and the browser extension's `MEDIA_EXTENSIONS` / `MEDIA_CONTENT_TYPES` carry neither `.mpd` nor
`application/dash+xml`, so a manifest is not even detected. DASH was explicitly ruled out of scope in the
archived `extension-media-details` change; this reopens it.

**Where it belongs.** The engine is a generic multi-connection HTTP *file* downloader with no binary
dependencies — manifest parsing plus an ffmpeg mux step does not belong there. Same shape as HLS, which is
already an optional desktop plugin.

**Scope.** A new optional/catalog-tier plugin mirroring `Downloader.Desktop.Plugins.Hls`: an `ILinkResolver`
claiming `.mpd` / `application/dash+xml` that parses the manifest (`SegmentTemplate` / `SegmentBase` byte
ranges) into segment `DownloadPart`s, `GetVariantsAsync` exposing representations as a quality picker, and
`PostProcess.Mux` for separate video/audio adaptation sets. The multi-part plan runner
(`DownloadManager.Plans.cs`) and the ffmpeg post-processor already exist, so this is plugin-sized work with
no app-core change. Extension sniffing needs `.mpd` + `application/dash+xml` added.

## Suggested order

1. **#7** — smallest change, largest immediate effect (it is the piece that makes protected m3u8 links work).
2. **#6** — moderate; UI plus model, engine API already in place.
3. **#5** — largest; a new plugin, its own catalog entry and version.

## Out of scope

- Any change to the `bezzad/downloader` engine, including URL failover (noted above as a possible separate
  follow-up in that repo).
- Registering a `downloader://` OS URL scheme, unless the author decides in favour of it.
- The automatic-interception problem the reporter mentioned — no details supplied yet; he was asked to open
  its own issue with the site, the expected capture and what the extension popup showed.

## Status

Proposal only, at the author's request — `design.md` and `tasks.md` are deliberately not created yet.
Nothing is implemented.

## Outcome (archived 2026-08-25)

**Superseded — every thread analysed here has been delivered by its own change.** This document was
always analysis rather than work (no `tasks.md`, no delta specs, by request), so it is archived with no
tasks ticked and nothing abandoned. It did its job: each item was implemented in the order it
recommended.

| Item | Delivered by |
|---|---|
| #7 — cookies / headers / referer per download | `archive/2026-08-23-per-download-request-context`, then `archive/2026-08-25-issue7-followup-fixes` |
| #6 — refreshing an expired link | `archive/2026-08-24-refresh-expired-link` |
| #5 — MPEG-DASH (`.mpd`) | `archive/2026-08-24-dash-mpd-support` |
| The automatic-interception problem listed under "Out of scope" | became issue #9 → `archive/2026-08-25-browser-download-interception` |

Two recommendations in here were followed rather than overruled, and are worth not relitigating:

- **`downloader://` was not registered.** The local API's loopback POST carries credentials out of band;
  a URL scheme would push them through a command line, into process listings and shell history. The
  request context arrives over `/api/add` instead — and as of the issue #7 follow-up, the GET form
  accepts it too, for tools that can only build a URL.
- **DASH landed inside the HLS plugin, not as a new plugin.** The analysis above proposed a separate
  catalog plugin; `dash-mpd-support` folded it into `com.bezzad.hls` instead, so users do not download a
  second ~80 MB ffmpeg into a second data directory. The two resolvers claim disjoint extensions.

The one item explicitly deferred to the engine repo — **automatic failover across the URL list when one
URL starts returning 403/410 mid-transfer** — was never opened against `bezzad/downloader` and remains
the only unaddressed thread from this analysis.
