## Why

The author's first real HLS e2e (36-segment 4K stream, `skate_phantom_flex_4k.m3u8`) surfaced two
problems in the new plan runner:

1. **Per-segment multipart is wasteful.** Each tiny segment (a few hundred KB – few MB) is downloaded
   with the user's full engine config — e.g. 8 chunks + parallel connections **per segment**. That's
   8 range requests + chunk bookkeeping for a file that fits in one read, repeated 36 times. Segments
   should download as a **single chunk**; the real HLS speed win is downloading **several segments at
   once**, not splitting one segment.
2. **Assembly fails in ffmpeg.** The runner hands the post-processor the temp output path
   `<final>.assembling`, and the final name itself was `skate_phantom_flex_4k.m3u8` (auto-resolved
   from the playlist URL by the Add dialog). ffmpeg refuses both:
   `Unable to choose an output format for '….m3u8.assembling'; use a standard extension` → exit 234.
   Two root causes: (a) the `.assembling` suffix hides the extension ffmpeg needs to pick a muxer,
   and (b) a playlist name (`.m3u8`) is the wrong container name for the assembled media anyway.

## What Changes

Three candidate fixes — **the author decides which to include** (2 is required for HLS to work at all;
1 is a cheap clear win; 3 is the bigger perf option):

1. **Single-chunk segments (recommended, cheap):** when running a plan part of `Kind == Segment` (or
   any part with a small/unknown `ExpectedSize`), override the engine config for that part:
   `ChunkCount = 1`, `ParallelDownload = false`. Full multipart stays for big single-file parts
   (e.g. a progressive video+audio pair).
2. **Fix assembly naming (required):**
   - The temp output keeps a standard media extension: `name.assembling.mp4` (extension LAST), not
     `name.mp4.assembling` — so ffmpeg can always choose the muxer.
   - Normalize the final name for post-processed plans: when the chosen name ends in a playlist
     extension (`.m3u8`/`.m3u`) and the plan has a Mux/Concat post-process, replace it with `.mp4`
     (or the plugin's `SuggestedFileName` extension when it provides one). A user-typed name keeps
     its own extension unless it's a playlist one.
3. **Parallel segment downloads (optional, bigger win):** download up to M segments concurrently
   (e.g. 4, capped by the user's parallel setting), each single-chunk, keeping ordered assembly.
   More code (bounded concurrency, per-part progress aggregation, cancel semantics) — worth it for
   long HLS streams, but can ship later.

## Capabilities

### Modified Capabilities
- `plugins`: plan execution downloads segment parts efficiently (single-chunk, optionally N segments
  in parallel) and produces an assembled file whose temp + final names carry a standard media
  extension so ffmpeg-based post-processors work.

## Impact

- `src/Downloader.Desktop/Services/DownloadManager.Plans.cs` — per-part config override, temp-name
  scheme, final-name normalization, (optionally) bounded parallel part loop.
- `Downloader.Desktop.Tests/PlanRunnerTests.cs` — assert single-chunk config for segment parts, the
  `name.assembling.mp4` temp shape, `.m3u8` → `.mp4` normalization, (optionally) parallel ordering.
- No SDK or plugin changes — the HLS plugin already marks segment parts `PartKind.Segment` and its
  ffmpeg call just receives a sane output path.
