## Why

Today a user who wants a video from a site like **x.com** (Twitter), YouTube, Instagram or TikTok must
first find the raw `.m3u8`/HLS link themselves and paste *that* — non-technical users have no way to do
this. The planned HLS plugin only understands direct `.m3u8` links. We want a user to paste an ordinary
**page URL** (e.g. `https://x.com/user/status/123`) and have the app find and download the video for them,
which is the whole point of a download manager for end users.

## What Changes

- Extend the **HLS plugin** (the `ILinkResolver` in the `downloader-plugins` repo) so a single plugin
  handles both **raw `.m3u8` links** (existing scope) **and ordinary site page URLs** (new).
- Add a **yt-dlp-backed site extractor**: when the pasted link is a supported site page (not a direct
  media/playlist URL), the resolver invokes a bundled-on-first-use `yt-dlp` to extract the real stream
  URL(s) — typically an HLS `.m3u8` (x.com `amplify_video`) or separate video/audio streams.
- The extracted result flows through the **existing HLS pipeline**: an HLS result is parsed into segment
  `DownloadPart`s + a `Concat`/`Mux` `PostProcess`; a progressive/DASH result becomes video+audio parts
  with an ffmpeg `Mux`. The host engine still does the byte downloading.
- `CanResolve` is widened: in addition to `.m3u8`, it claims URLs whose host is a **supported site**
  (x.com / twitter.com first, then a general yt-dlp-supported set), so the host's existing resolver
  flow routes these links to this plugin.
- `yt-dlp` is **auto-downloaded into the plugin `DataDirectory`** on first use (same pattern as ffmpeg),
  not bundled, and is **refreshable** (sites break often; yt-dlp self-updates).
- First version targets **x.com explicitly** (with tests) and **general yt-dlp sites** opportunistically
  (best-effort, documented as such).

## Capabilities

### New Capabilities
- `video-site-extraction`: A plugin resolver capability that turns a supported **site page URL** into a
  concrete `DownloadPlan` (real stream parts + post-process recipe) by extracting the stream with yt-dlp,
  so end users can download site videos by pasting the page link instead of an HLS link.

### Modified Capabilities
<!-- None. The host's existing "download flow resolves links through enabled plugins" requirement
     (specs/plugins) already routes any claimed link to the plugin resolver; no host requirement changes. -->

## Impact

- **Repo**: `bezzad/downloader-plugins` (the plugins monorepo from `docs/plugins-hls-torrent-plan.md`),
  inside the `Downloader.Plugins.Hls` project. **No code change in `Downloader.Desktop`** — the host
  resolver pipeline already exists. If that repo does not exist yet, it is created first (prerequisite).
- **SDK**: consumes the existing `ILinkResolver` / `DownloadPlan` / `DownloadPart` / `PostProcess`
  contracts in `Downloader.Desktop.Plugins.Abstractions` — no SDK changes required.
- **New runtime dependency**: `yt-dlp` binary (downloaded on demand into the plugin `DataDirectory`),
  plus the existing `ffmpeg` dependency for muxing. No new NuGet for extraction (yt-dlp is a process).
- **Docs**: update `docs/plugins-hls-torrent-plan.md` to fold site extraction into the HLS plugin
  (it previously deferred this to a separate future "video-sites" plugin).
- **Network/privacy**: extraction makes requests to the target site; documented. No new host telemetry.
