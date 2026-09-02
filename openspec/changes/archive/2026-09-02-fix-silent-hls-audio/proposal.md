## Why

A video downloaded from x.com (and YouTube) played but had **no sound**. Two independent defects
produced that one symptom, and both were confirmed against real data rather than reasoned about:

1. **The HLS resolver only ever downloaded the chosen `#EXT-X-STREAM-INF` variant.** In a master
   playlist whose variants carry `AUDIO="grp"`, that variant's playlist is *video only* and the audio
   lives in a separate `#EXT-X-MEDIA:TYPE=AUDIO,…,URI="…"` rendition — the shape YouTube's HLS
   manifests and many CDN masters use. `M3u8Parser` ignored `#EXT-X-MEDIA` entirely, so the audio
   playlist was never fetched and there was nothing for ffmpeg to mux.

2. **The extension's quality picker handed the app a rendition, not the master.** The reported item's
   own record (`config.json` → `Downloads`) held
   `https://video.twimg.com/amplify_video/<id>/pl/avc1/720x1280/<name>.m3u8`, whose segments resolve
   to `/vid/avc1/…` fMP4 — video only, verified with `curl`. `popup.js buildGroups` replaced a master
   group's options with the parsed variants and set each option's value to the **variant URI**, so
   "Download" sent the rendition and the app never saw the master. That file could not have had sound
   no matter what the plugin did.

A third, related cause was found while proving the first two: on the yt-dlp path the "best audio" is
usually **Opus**, which *can* be written into MP4 but which most desktop players refuse to decode
there — indistinguishable, to the user, from a missing audio track.

## What Changes

- **HLS plugin** parses `#EXT-X-MEDIA` renditions; a variant that points at an audio group now
  downloads that rendition as a **second concat stream group**, reusing the existing
  concat-each-then-mux path (added for DASH). A self-contained variant still produces the old
  single-group recipe unchanged.
- **fMP4 HLS** (an `#EXT-X-MAP`) labels its intermediate `.mp4` instead of `.ts`.
- **Both muxers** map streams explicitly (`-map 0:v:0 -map 1:a:0`) instead of trusting ffmpeg's
  default selection, and `aac_adtstoasc` is applied only to TS/AAC audio.
- **Site-media plugin** prefers an MP4-native audio format (AAC/ALAC/AC-3) over both yt-dlp's own
  `requested_formats` pick and a higher-bitrate Opus stream. Opus is still used when it is the only
  audio available — never refuse a download over this.
- **`/api/add` accepts `variantId`** (JSON body, GET query, and the forwarded CLI add) and puts it on
  `DownloadItem.VariantId`, which `DownloadManager.Start` already passes to the resolver.
- **Extension popup** sends the **master** URL plus the chosen quality's `variantId`; each option
  keeps its own rendition URL for probing/dedup/thumbnails.

Versions bumped so the fix actually reaches users: HLS `2.2.1 → 2.3.0`, SiteMedia `1.0.1 → 1.1.0`,
extension `1.8.1 → 1.9.0`, app `2.8.2 → 2.9.0`. **No release is performed in this change** — the
release routine runs separately.

## Impact

- Affected specs: `hls-download`, `local-api`, `browser-extension`, `site-media-download` (new).
- Affected code: `Downloader.Desktop.Plugins.Hls` (`M3u8Parser`, `M3u8Models`, `HlsResolver`,
  `FfmpegBinary`), `Downloader.Desktop.Plugins.SiteMedia` (`SiteExtractor`, `MediaMuxer`),
  `Downloader.Desktop/Services/LocalApiService.cs`, `src/browser-extension`
  (`popup.js`, `common.js`, `background.js`, both manifests).
- **Not fixed, deliberately**: a rendition sniffed with **no master anywhere**. Nothing in a media
  playlist says where its audio group lives, and guessing x.com's `/pl/mp4a/<bitrate>/…` sibling is
  exactly the site-specific arms race HLS 2.0.0 dropped. For those links the page URL plus
  `com.bezzad.site-media` is the answer. Whether the extension should prefer the page URL on sites the
  app reports it can handle (`/api/can-handle` already exists) is left to the author — it changes what
  the popup's "Download" button means.
- **Verified by the author on a real x.com video**: HLS 2.3.0 downloaded and muxed the audio.
