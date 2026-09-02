# Tasks

All tasks are complete: the build is clean (0 warnings), every suite is green, and the author has
confirmed a real x.com download now carries its audio. Commits: `dcbc3f5` (plugins), `ab1af2e`
(extension + local API), and this change's artifacts.

## 1. HLS: download the separate audio rendition

- [x] 1.1 Parse `#EXT-X-MEDIA` into `HlsRendition` (type, group, resolved URI, name, language,
      `DEFAULT`); carry `AUDIO` and `CODECS` on `HlsVariant`.
- [x] 1.2 `HlsMasterPlaylist.AudioFor(variant)` picks the group's `DEFAULT=YES` rendition; skips a
      rendition with no `URI`; falls back to the default rendition only when `DeclaresNoAudio(CODECS)`.
- [x] 1.3 `HlsResolver` fetches the audio playlist and emits its segments as a second
      `ConcatRecipe.StreamGroup`; a self-contained variant still writes `Streams == null`.
- [x] 1.4 fMP4 (an `#EXT-X-MAP`) sets `IntermediateExtension = ".mp4"`.
- [x] 1.5 Tests: `Plugins/Hls/HlsSeparateAudioTests.cs` — parser, `AudioFor` (6 shapes incl. the three
      that must NOT attach audio), resolver plan/recipe/headers/init-segments, the single-stream
      regression guard, and a resolver → post-processor round trip proving the audio bytes reach the mux.

## 2. Muxing: state the intent

- [x] 2.1 `FfmpegBinary.BuildMuxArgs` — `-map 0:v:0 -map 1:a:0`, `aac_adtstoasc` only for `.ts`/`.aac`.
- [x] 2.2 `FfmpegMuxer.BuildMuxArgs` (site-media) — same explicit maps.
- [x] 2.3 Tests: arguments asserted for both, including the TS-vs-MP4 filter decision.

## 3. Site-media: audio that players can actually decode

- [x] 3.1 `SiteExtractor.IsMp4NativeAudio` judges `acodec`, falling back to `ext`.
- [x] 3.2 Prefer an MP4-native audio format over `requested_formats` and over a higher-bitrate Opus;
      keep using Opus when it is the only audio.
- [x] 3.3 Tests: `Plugins/SiteMedia/SiteMediaAudioSelectionTests.cs` (YouTube-shaped JSON + the codec
      decision table).

## 4. Local API: carry the chosen quality

- [x] 4.1 `ApiAddRequest.VariantId` from the JSON body, the GET query, and `ToJson` (forwarded CLI add);
      not part of the extension's `hasContext`, so a plain send keeps its GET form.
- [x] 4.2 `BuildItem` puts it on `DownloadItem.VariantId` (trimmed; blank → null); the `201` body
      reports it back.
- [x] 4.3 Tests: `Unit/ApiVariantChoiceTests.cs` (6).

## 5. Extension: hand over the master, not the rendition

- [x] 5.1 A master group's options keep `url` (probe/dedup/thumbnail identity) and gain `sendUrl` +
      `variantId`; `sendOption` sends those, from both the row button and "Send all".
- [x] 5.2 `sendToAppSilently` carries `variantId` in both wire forms; `background.js` passes it through.
- [x] 5.3 Tests: 3 in `common.test.js`, plus a real-browser e2e that picks 640x480 and asserts the stub
      app received `master.m3u8` with `variantId=1200000` and **not** `high/index.m3u8`.

## 6. Versioning (no release in this change)

- [x] 6.1 HLS plugin `2.2.1 → 2.3.0` (csproj `<Version>` — the catalog's single source).
- [x] 6.2 SiteMedia plugin `1.0.1 → 1.1.0`.
- [x] 6.3 Extension `1.8.1 → 1.9.0` in both manifests; `scripts/build-extension.sh` re-verified.
- [x] 6.4 App `VersionPrefix 2.8.2 → 2.9.0`, so the `/api/add` field ships under a version distinct
      from the released `v2.8.2`.
- [x] 6.5 Release itself deliberately NOT performed — the author runs the release routine separately.

## 7. Close out

- [x] 7.1 Non-obvious findings appended to `.claude/skills/downloader-desktop/SKILL.md` (both root
      causes, the evidence trail, and the "master cannot be derived from a rendition" fact).
- [x] 7.2 Full verification re-run on the final tree; delta specs synced and this change archived.
