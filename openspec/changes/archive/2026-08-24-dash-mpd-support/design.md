# Design — dash-mpd-support

## Where the code lives

`src/Downloader.Desktop.Plugins/Downloader.Desktop.Plugins.Hls/Dash/`, namespace
`Downloader.Desktop.Plugins.Hls.Dash`. The plugin assembly, id and catalog entry stay
`com.bezzad.hls`; only the display name, description and `<Version>` change (2.1.0 → 2.2.0 — a feature, per
the repo's plugin-versioning rule).

| File | Role |
|---|---|
| `MpdModels.cs` | Parsed manifest: `DashManifest`, `DashRepresentation`, `DashSegment` |
| `IMpdParser.cs` / `MpdParser.cs` | XML → model, behind an interface so the resolver is testable |
| `DashResolver.cs` | `ILinkResolver` for `.mpd`: variants + plan |
| `DashException.cs` | Carries the user-facing refusal reason (live / DRM / unsupported) |

`HlsPlugin.Initialize` registers the new resolver next to the existing one. Both resolvers are regular
(non-fallback), and their `CanResolve` checks are disjoint by extension, so the two-pass resolver lookup in
`PluginManager` is unaffected.

## Parsing an MPD

`System.Xml.Linq`, namespace-agnostic (match on `LocalName` — MPDs in the wild use several namespace URIs,
and a few omit it). The work is:

1. **Reject early.** `type="dynamic"` → live. Any `ContentProtection` element anywhere → DRM. Both throw a
   `DashException` whose message the row shows.
2. **Resolve base URLs.** `BaseURL` may appear at MPD, Period, AdaptationSet and Representation level and
   composes in that order, each resolved against the previous (and ultimately against the manifest's own
   final URL, after redirects).
3. **Pick the period.** First `Period` only. Multi-period manifests (ad breaks) would need concatenating
   periods with different codecs; out of scope, and the first period is what a single-asset VOD MPD has.
4. **Expand each representation into segment URLs**, by addressing mode:
   - **`SegmentTemplate` + `SegmentTimeline`** — walk `<S t d r>` entries; `r` repeats, `t` restarts the
     clock. `$Time$` substitutes the running start time, `$Number$` the running index from `startNumber`.
   - **`SegmentTemplate` without a timeline** — segment count =
     `ceil(periodDuration / (duration / timescale))`, numbered from `startNumber`.
   - **`SegmentList`** — the `<SegmentURL media>` entries verbatim, `Initialization@sourceURL` first.
   - **`SegmentBase`** / bare `BaseURL` — the representation IS one file; a single segment, no init.
   - Placeholders `$RepresentationID$`, `$Bandwidth$`, `$Number%0Nd$`, `$Time%0Nd$` and `$$` are all
     substituted (the `%0Nd` width form is common and breaks naive replacement).

`AdaptationSet@mimeType`/`@contentType` (falling back to the representation's `mimeType`, then to codecs)
decides video vs audio.

### Why not byte ranges for `SegmentBase`

`SegmentBase` declares `indexRange`/`Initialization@range` so a *player* can seek. A downloader wants the
whole representation, which is exactly the whole file — so it becomes one ordinary part and the engine
multi-chunks it as usual. Emitting `Range` headers would fight the engine's own ranged chunking.

## Part kinds (they drive the plan runner)

`DownloadManager.Plans` gives `PartKind.Segment` parts one chunk each and downloads them 4-at-a-time when a
plan is segments-only; larger parts get the normal parallel-chunk treatment. So:

- Segmented representations emit `PartKind.Segment` parts (many small files, parallel).
- A `SegmentBase`/`BaseURL` single-file representation emits one `PartKind.Video` / `PartKind.Audio` part,
  which is then multi-chunk downloaded — the right call for a 500 MB file.

Part order is **all video parts, then all audio parts**, each preceded by its init segment. The recipe
describes that split; the runner preserves order.

## Assembly: streams in the concat recipe

Neither existing post-process kind fits on its own: `Concat` assumes one stream, `Mux` assumes exactly two
already-complete files. DASH needs concat-then-mux. Rather than add an SDK enum value (which every external
plugin would have to learn), `ConcatRecipe` grows two optional fields:

```csharp
public List<StreamGroup>? Streams { get; set; }   // null = one group, today's behaviour
public string IntermediateExtension { get; set; } = ".ts";
```

`StreamGroup { bool HasInitSegment; int SegmentCount; }`. The flat `Segments` list stays 1:1 with the media
segments across all groups in order, so AES-128 support is untouched and existing recipes deserialize
unchanged (`Streams == null` → one group built from `HasInitSegment` + `Segments.Count`).

`HlsPostProcessor.ProcessAsync` then: concat each group to its own intermediate file → one group, remux →
two groups, mux(video, audio) → more than two, error. DASH sets `IntermediateExtension = ".mp4"` because its
segments are fMP4, not MPEG-TS.

## Size estimates for the picker

`bandwidth / 8 * mediaPresentationDuration`, the same formula HLS uses, and it needs no extra request
because an MPD declares its duration up front — so `GetVariantsAsync` is a single GET, cached (5 min) and
shared with the resolve that follows.

## Browser extension

`.mpd` joins `.m3u8` in the media-extension list and in `groupKey` (each manifest is its own group — the
representations inside it are the app's business, not the extension's), and the popup labels it `dash`.
No manifest-permission changes.

## What can't be verified here

A real end-to-end DASH download (fetch → concat → ffmpeg mux → playable file) needs ffmpeg and a live
stream. Unit tests cover the parser against fixtures for every addressing mode, the resolver's plan/variant
shape, and the multi-stream post-processor with a stubbed ffmpeg; a loopback-server test covers manifest →
plan. The author's manual check stays on the task list.
