# Proposal — hls-only-quality-picker

The HLS plugin never listed qualities for a real `.m3u8` link, and page-URL extraction (YouTube/x.com via yt-dlp) failed constantly. Keep the plugin for **HLS only**: a working quality picker on master playlists, and a working default (highest-bandwidth) download. Drop yt-dlp site extraction.

## Why

`GetVariantsAsync` returned null as soon as the URL looked like HLS, so the Add window never showed qualities or sizes. Default download still picked `master.Best()`, but the user had no quality/size signal — and YouTube/x.com extraction kept breaking (bot-checks, truncated yt-dlp/deno, cookies). Author decision: HLS-only, not both jobs.

## Scope

- Master playlist → quality list (bandwidth desc, default = best, size ≈ bandwidth × duration).
- `VariantId` selects that rendition; missing id → best (plain add).
- Media playlist (one rendition) → no picker, download as today.
- Delete yt-dlp / deno / `SiteExtractor`. ffmpeg remains the only runtime dependency.
- Plugin version 1.4.0 → **2.0.0** (breaking: page URLs are no longer claimed).

## Out of scope

- YouTube / x.com / Instagram / … page downloads.
- Separate-audio HLS (`#EXT-X-MEDIA:TYPE=AUDIO`) mux.
- Live (no `EXT-X-ENDLIST`) or DRM playlists.

## Released

Shipped in **v2.3.0** (tag `v2.3.0` = `146de478d25f026a09a1a837cea9954734033a7f`, merge commit
`ef42a35` develop→main, version bump `fa829e5`). Homebrew tap updated to 2.3.0; winget PR
[microsoft/winget-pkgs#422385](https://github.com/microsoft/winget-pkgs/pull/422385) open awaiting
a moderator. `openspec` CLI wasn't available in the release session to run `/opsx:sync` + `/opsx:archive`
— do that from a machine that has it (all 6 tasks are done, build+tests green).
