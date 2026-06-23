# R&D: Downloading video-site media (HLS/.m3u8) — Instagram, YouTube, etc.

**Status:** research + **direction locked**, implementation **deferred** ("not now", 2026-06-22). No code yet.
**Asked:** "I want this app to download Instagram videos. R&D and tell me the plan; if it's simple, do it."
**Short answer:** it's **not simple**, so this is a plan, not an implementation.

## ✅ Locked decisions (2026-06-22 — author)
The direction is settled; only the *timing* is deferred. When picked up, build to these:
1. **Scope = full Option A (multi-site).** yt-dlp + ffmpeg behind an `IMediaExtractor` abstraction —
   YouTube, Instagram, TikTok, X, etc. (NOT an IG-only scraper; NOT .m3u8-paste-only.)
2. **Binaries = download on first use** into the app data dir (keep the installer lean; yt-dlp self-updates).
   Do **not** bundle them in the installer.
3. **Framing = generic "video sites"** in the UI/README ("download video from supported sites") + a
   personal-use disclaimer. Do **not** headline "Instagram/YouTube downloader" (ToS/reputational).
4. **Ship = not yet** — revisit later; everything below is the agreed implementation shape for then.

## Why it isn't a normal download
Our engine (`Downloader`) downloads a **direct file URL** with HTTP range/multipart support. An
Instagram link (`instagram.com/reel/…`, `/p/…`) is an **HTML page**, not a file. The real video lives
on a CDN (`*.cdninstagram.com` / `*.fbcdn.net`) behind a **short-lived signed URL** that:
- isn't in the link the user pastes — it must be **extracted** from the page/JSON/API,
- often requires a **logged-in session** (cookies) for non-public or higher-quality media,
- is protected by **anti-bot measures + rate limits**, and the page structure **changes frequently**,
- may be **HLS/DASH** (separate audio+video segments) that must be **downloaded and muxed** (FFmpeg),
  not just fetched as one MP4.

So we need a **media extractor** layer in front of the engine. This is the same capability the deferred
"m3u8/HLS + YouTube" roadmap item needs — solving it once covers Instagram **and** YouTube, TikTok,
Twitter/X, Facebook, etc.

## Options evaluated

| Approach | Reliability | Cost / downside |
|---|---|---|
| **A. Bundle `yt-dlp` (+ `ffmpeg`) and shell out** | **High** (maintained extractors for IG + hundreds of sites) | Large per-platform binaries (~ffmpeg 70–100 MB, yt-dlp ~10–30 MB); needs an update path (sites change → yt-dlp updates often); GPL/LGPL (ffmpeg) distribution considerations |
| B. .NET library | Low | No robust, maintained .NET Instagram extractor. `InstagramApiSharp` is a private-API client (login, fragile, ToS-risky). `YoutubeExplode` is YouTube-only |
| C. Official Instagram Graph API / oEmbed | N/A for this use case | oEmbed returns an embed/thumbnail, not a video file; Graph API only exposes media you **own/manage** (business accounts) — not arbitrary public videos |
| D. Roll our own scraper (parse `og:video`/embedded JSON) | Low–medium, **fragile** | Breaks whenever IG changes markup; login walls; a permanent maintenance treadmill |

## Recommended plan — option A behind a `MediaExtractor` abstraction
This is the only **sustainable** path and it also unlocks the broader roadmap item.

1. **Detect page URLs vs direct files.** If the pasted URL is a known media-site page (not a direct
   downloadable file), route it through the extractor instead of straight to the engine.
2. **`Services/IMediaExtractor` + `YtDlpExtractor`.** Run `yt-dlp -J <url>` (JSON) to get the available
   formats + (possibly signed) direct URLs + whether audio/video are separate.
3. **Pick the download path:**
   - **Progressive MP4, single stream** → hand the resolved direct URL to our existing multipart engine
     → we keep our speed/pause/resume advantage. (Most IG reels are progressive MP4.)
   - **HLS/DASH or split audio+video** → let `yt-dlp` + `ffmpeg` download and mux (no multipart, but
     correct output). Bridge `yt-dlp` progress lines to the row's progress.
4. **Binary management.** Ship nothing in the installer; **download yt-dlp/ffmpeg on first use** into the
   app data dir (keeps the app lean and lets yt-dlp stay current). Add a periodic yt-dlp self-update.
5. **Settings + disclaimer.** A toggle to enable "video sites", and a short notice that downloads are for
   **personal use** and the user is responsible for copyright/platform terms (see Legal below).
6. **Errors.** Friendly messages for login-walled/expired/private media and "this site isn't supported".
7. **Tests.** Mock the extractor (feed a canned JSON) so format-selection + routing are unit-tested
   without network; keep the real binary calls out of CI.

## Legal / ToS note (decision for the author)
Downloading Instagram content is **against Meta's Terms of Service**, and redistributing copyrighted
media is on the end user. Since this app is **published (brew/winget) under your name**, shipping a
headline "download Instagram videos" feature carries some reputational/ToS risk. Mitigations: frame it
generically ("download video from supported sites"), add the personal-use disclaimer, and don't bundle
account-login/scraping of private content. **Your call on whether to ship it at all.**

## Effort & recommendation
- **Effort:** medium–large (new service, process management, format selection, progress bridging, binary
  download/update, settings + disclaimer, tests). Roughly a multi-day feature, not a quick add.
- **Recommendation:** if you want it, do **option A** and scope it as the general
  **"video-site downloads (yt-dlp)"** feature (IG + YouTube + others), not IG-only — same effort, far
  more value. A throwaway IG-only scraper (option D) is cheap but will break repeatedly; not worth
  shipping as a promised feature.
- **Interim experiment (optional):** a best-effort public-reel scraper (extract `og:video`/JSON → feed
  the engine) could be a 1–2 hour spike to demo the flow, clearly labeled "experimental / may break".

## Decision points for the author — ANSWERED (2026-06-22)
1. Ship this at all? → **Yes, but generic framing** (and **not now** — deferred).
2. Scope? → **Full Option A (yt-dlp, multi-site).**
3. Binaries? → **Download on first use** (don't bundle).

See "Locked decisions" at the top — these are settled; revisit only the timing.
