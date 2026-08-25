# Design — hls-only-quality-picker

## Decision

The plugin does one job: expand a direct HLS playlist into segments and assemble them. Video-site extraction is gone because it is an arms race with those sites, not a download-manager feature we can keep green.

## Quality picker

`HlsResolver.GetVariantsAsync`:

1. `CanResolve` (path ends `.m3u8`/`.m3u`, or optional content-type probe). Else null.
2. GET the playlist. Media playlist → null (no choice).
3. Master → one `LinkVariant` per `#EXT-X-STREAM-INF`, id = bandwidth (collision suffix `-index`), label `{height}p (≈size)` or `{kbps} kbps`, default = highest bandwidth.
4. Size = `bandwidth/8 * duration` of the **best** media playlist (same VOD content, different bitrate). Best-effort: fetch failure → labels without size.

Playlist GETs cache 5 minutes so list + resolve share one fetch.

`ResolveAsync` still downloads: master → `Pick(variantId)` or `Best()` → media segments + Concat recipe. Unknown/missing `VariantId` = best (the "download with default" path).

## Runtime deps

Only `FfmpegBinary`. `YtDlpBinary`, deno, `SiteExtractor`, `IYtDlp` deleted.

## Specs

`video-site-extraction` requirements are **removed**. `link-variants` HLS scenarios now talk about master playlists, not yt-dlp heights. New `hls-download` capability for the playlist pipeline.
