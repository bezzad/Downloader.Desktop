## Context

Both defects were located from evidence, and the evidence is worth keeping because it is what stops
the next session re-deriving (or re-guessing) it.

- The reported download's record: `~/.config/Downloader/config.json` → `Downloads` entry with
  `Urls[0] = https://video.twimg.com/amplify_video/2094957000274612227/pl/avc1/720x1280/skgcZqs3uNDMcbFa.m3u8`,
  `Status = 5` (Completed), 3.95 MB.
- `curl` on that playlist: `#EXT-X-MAP:URI="/amplify_video/…/vid/avc1/0/0/720x1280/….mp4"` and
  `/vid/avc1/…m4s` segments — a **video-only** fMP4 rendition.
- The master's address **cannot be derived** from a rendition's: `…/pl/<name>.m3u8`,
  `…?container=fmp4`, and `…/pl/mp4a/128000/<name>.m3u8` all answer 404. So an app handed a rendition
  has no route back to the audio.
- The installed plugin at the time was HLS 2.2.1 (dll dated 31 Aug), i.e. the user's test ran against
  the pre-fix plugin AND the pre-fix extension. There are two installs on this box (plain
  `~/.config/Downloader` and snap `~/snap/downloader/current/.config/Downloader`), each with its own
  plugins; compare `config.json` mtimes to tell which one is live.

## Goals / Non-Goals

- **Goal**: an HLS download whose audio is a separate rendition comes out with sound, whether the link
  is pasted into the app or handed over by the extension's quality picker.
- **Goal**: the choice a user makes in the popup survives the hand-off.
- **Non-Goal**: recovering audio for a rendition URL whose master was never observed (see proposal).
- **Non-Goal**: re-encoding video, changing the output container, or teaching the plugin any
  site-specific URL shape.

## Decisions

### Reuse the DASH two-group concat path instead of a new post-process kind

`ConcatRecipe.Streams` (added for DASH) already means "concatenate each group, then mux the two". The
HLS resolver emits `[video, audio]` groups and the post-processor needs no change at all. A new
`PostProcessKind` would have to be learned by every external plugin; a second code path would have to
be kept in step with the first. `Streams == null` still means one stream, so every recipe written
before this deserializes and behaves exactly as it did.

### `AudioFor` requires proof, not a guess

- Variant names an `AUDIO` group → take that group's `DEFAULT=YES` rendition (else its first).
- Rendition without a `URI` → the audio is muxed into the variant; download nothing extra.
- Variant names no group → attach the master's default audio **only** when its `CODECS` lists no audio
  codec (`DeclaresNoAudio`). An **absent** `CODECS` proves nothing and must not trigger a guess: a
  genuinely silent stream would then be given someone else's audio track.

### Explicit `-map` in both muxers

ffmpeg's default stream selection picks one stream *per type across all inputs*, so a "video-only"
file that carries a stray or secondary audio track can win and the real audio is dropped. `-map
0:v:0 -map 1:a:0` states the intent. `aac_adtstoasc` is applied only for `.ts`/`.aac` audio: AAC in
MPEG-TS is ADTS-framed and illegal in MP4, while MP4/fMP4 audio already carries its ASC and must not
be filtered. The DASH path (`.mp4` intermediates) is therefore untouched.

### Audio format is judged by codec, not by file extension

Extracted stream URLs carry **no** file extension, so any check on the downloaded part's name is
useless — `SiteExtractor.IsMp4NativeAudio` reads `acodec` and falls back to `ext` from the extraction
JSON. This is also why the earlier attempt to decide the mux arguments from the audio file's
extension was abandoned on the site-media path.

### The variant id crosses the API as the resolving plugin's own id

`HlsResolver.UniqueId` is the variant's `BANDWIDTH`, and that is what the extension sends. This is a
coupling, and it is deliberate: `Pick` falls back to `Best()` for an unknown or absent id, so a
caller that guesses wrong gets the best quality **with** audio. Audio always beats an exact quality
match. `variantId` is not a secret, so — like `path` — it travels in the GET query and does not force
the POST form.

### The popup keeps two URLs per option

An option's `url` stays the rendition (that is what was size-probed, what `childUris` dedups against,
and what the thumbnail index matches); `sendUrl` + `variantId` are what get sent. Collapsing them
would have broken probing, dedup and previews to fix the send.

## Risks / Trade-offs

- **Two extra HTTP requests** at resolve time for an audio-bearing master (its playlist, plus its init
  segment as a part). Acceptable: the alternative is a silent file.
- **A same-basename intermediate for both groups**: the post-processor already suffixes them
  `.s0.concat` / `.s1.concat`, so no collision.
- **Preferring AAC over Opus can cost a little audio quality** (129 vs 130 kbps in the observed
  YouTube case). Trading an inaudible difference for audio that actually plays is the right way round.
- **An older app ignores `variantId`** and downloads the master's best variant — still with audio.

## Verification

- `dotnet build Downloader.Desktop.sln -t:Rebuild` → 0 warnings; full suite green (1551).
- `node --test src/browser-extension/common.test.js` → 141 green;
  `npx playwright test --workers=1` → green (the thumbnail spec's known flake passes on a re-run of
  its own file); `npx web-ext@8 lint` on the packaged Firefox zip → 0 errors, 0 warnings.
- End-to-end **on a real x.com video, by the author**: audio downloaded and muxed with HLS 2.3.0.
  A real-stream ffmpeg run cannot be verified on this box (no desktop session, and the static ffmpeg
  build here segfaults on MPEG-TS).
