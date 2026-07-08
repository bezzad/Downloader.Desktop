## Context

The plugin SDK (`Downloader.Desktop.Plugins.Abstractions`) defines a three-phase pipeline —
**resolve → transfer → post-process** — and the host already routes any pasted link to the enabled
plugins' `ILinkResolver`s and downloads the resolver's resolved parts (archived change
`plugin-link-resolution-in-download-flow`; spec `plugins`). The HLS plugin is planned in a separate
monorepo `bezzad/downloader-plugins` (see `docs/plugins-hls-torrent-plan.md`) and was scoped to **direct
`.m3u8` only**, explicitly deferring site extraction (YouTube/x.com/Instagram) to a separate future
plugin.

The author has decided to **fold site extraction into the HLS plugin itself**, using **yt-dlp** as the
extractor, targeting **x.com first plus general sites**. This document covers how that resolver works; it
does not re-specify the base HLS playlist parsing already described in the plan doc.

## Goals / Non-Goals

**Goals:**
- One plugin (`Downloader.Plugins.Hls`) accepts both raw `.m3u8` links and supported **site page URLs**.
- Pasting an x.com status URL downloads its video with no manual link-finding by the user.
- Reuse the existing HLS segment pipeline whenever the extracted format is HLS; reuse ffmpeg mux for
  progressive/DASH (video+audio) results.
- Keep the host engine as the byte downloader (resolver returns `DownloadPart`s; host downloads them).
- Provision yt-dlp on demand into the plugin `DataDirectory` (no bundling), behind a test seam.

**Non-Goals:**
- No host (`Downloader.Desktop`) code changes — the resolver flow already exists.
- No login/cookie management UI for authenticated/private content in v1 (best-effort public media only).
- No exhaustive per-site guarantees — general sites are best-effort; only x.com is verified with tests.
- No change to the SDK contracts.

## Decisions

### D1: yt-dlp as the extractor (over a native x.com API client)
yt-dlp is a maintained process that already solves x.com guest tokens / GraphQL, plus hundreds of other
sites, and self-updates as sites change. A hand-written x.com GraphQL client would be x.com-only and break
frequently. **Chosen:** invoke yt-dlp as a child process. *Alternative considered:* native HttpClient
extraction — rejected as fragile and single-site.

### D2: One plugin, widened `CanResolve` (over a second plugin)
Per the author's decision, the HLS plugin's `CanResolve` returns true for `.m3u8` **and** supported site
hosts. `ResolveAsync` branches: direct media/playlist → existing HLS parse; site page → yt-dlp extract,
then feed the extracted format back through the same plan-building code. *Alternative considered:* a
separate `video-sites` resolver plugin (the original plan) — rejected per the chosen "extend the HLS
plugin itself" option; simpler install, one place for video extraction.

### D3: Extract metadata, let the host download (over `yt-dlp` downloading directly)
Run `yt-dlp -J` (dump-json, no download) to get formats + the real stream URLs + required headers, then
build a `DownloadPlan` the host engine downloads — preserving multipart/pause/resume/queue/speed control,
the whole reason for this app. Only **mux/concat** is delegated to ffmpeg as the post-processor.
*Alternative considered:* `yt-dlp` does the full download — rejected; it bypasses the engine's
multipart/resume/queue features and the unified progress UI.

### D4: Format selection
From `yt-dlp -J`, prefer a single progressive MP4 when present (simplest: one part, no mux). Otherwise
prefer the HLS master/variant (reuse the HLS segment pipeline). Otherwise take best-video + best-audio and
ffmpeg-mux. Honor a quality hint if/when the host exposes one; default to "best reasonable" to keep it
simple for non-technical users.

### D5: yt-dlp provisioning mirrors ffmpeg
An `IYtDlp` abstraction (real impl downloads the per-OS yt-dlp build into `ctx.DataDirectory` on first use,
caches it, and runs it). Tests stub `IYtDlp` to return canned `-J` JSON so extraction logic is verified
with **no network and no real binary** — matching how the plan tests stub `IFfmpeg`.

### D6: Errors throw, host falls back
On no-media / private / extractor failure, `ResolveAsync` throws a clear message. The host's existing
"a resolver failure does not break the download" behavior then leaves the original link intact, and the UI
surfaces the message (consistent with current resolver-failure handling).

## Risks / Trade-offs

- **Site breakage / yt-dlp drift** → yt-dlp self-updates; provision logic can refresh to latest; general
  sites documented as best-effort, only x.com covered by tests.
- **External binary trust/size (yt-dlp + ffmpeg)** → downloaded on demand into a per-plugin dir, not
  bundled; document the source URLs; verify checksums where the source provides them.
- **Private/auth content fails** → out of scope for v1; fail with a clear "content unavailable" message
  rather than half-working cookie handling.
- **Process-spawn portability (Win/Linux/macOS)** → wrap exec behind `IYtDlp`; set the executable bit on
  Unix after download; test the JSON-parsing layer independently of the OS.
- **Legal/ToS sensitivity of site downloading** → the plugin is opt-in (user installs/enables it); the
  host stays neutral; no site names beyond x.com surfaced in the host UI/docs (per repo privacy note).

## Migration Plan

1. (Prerequisite) Ensure the `downloader-plugins` repo and `Downloader.Plugins.Hls` base project exist
   (from `docs/plugins-hls-torrent-plan.md`). If not, create them first.
2. Add the site-extraction code paths additively — direct `.m3u8` behavior is unchanged, so there is no
   data migration and no host change. Rollback = ship the plugin without the widened `CanResolve`.

## Open Questions

- Should a future version expose a quality picker (e.g. 720p/1080p) in the host UI, or keep "best" only?
- Which non-x.com sites, if any, should be advertised as "supported" vs. left as undocumented best-effort?
- Where should the canonical yt-dlp download URL/version be pinned (and how is it refreshed safely)?
