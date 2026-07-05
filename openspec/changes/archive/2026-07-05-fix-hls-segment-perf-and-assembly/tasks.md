## 1. Single-chunk segments (D1 — recommended)

- [x] 1.1 `IsSingleChunkPart` (pure): `PartKind.Segment` parts (any size) and known-≤8 MB parts override the
  per-part config to `ChunkCount = 1`, `ParallelDownload = false`; big non-segment parts keep full multipart.
- [x] 1.2 Test `Segment_and_small_parts_are_single_chunk` covers the decision matrix (segment/any-size,
  small combined, big video, unknown-size combined).

## 2. Assembly naming fixes (D2 + D3 — required)

- [x] 2.1 `AssemblingPath(finalPath)` = `{stem}.assembling{ext}` (extension LAST) used by the post-process
  path (`ConcatFiles`' temp keeps its old suffix — no ffmpeg involved there).
- [x] 2.2 Pure `NormalizeAssembledName(name, plan)`: post-processed plans never keep `.m3u8`/`.m3u`/empty
  extensions — `.mp4` unless the plugin suggests another concrete extension; user's concrete extension
  preserved; no-post-process plans untouched. Applied in `RunPlanAsync`, and the row's `FileName` is synced
  to the normalized output name.
- [x] 2.3 Tests `Assembling_path_keeps_the_extension_last` + `Playlist_final_names_normalize_to_media_extensions`
  (incl. the author's exact failing name `skate_phantom_flex_4k.m3u8` → `.mp4`).

## 3. Parallel segment downloads (D4 — optional, author's call)

- [x] 3.1 Segment-only plans (>2 pending segment parts) download with a `SemaphoreSlim(4)` bounded loop,
  each single-chunk; assembly stays strictly index-ordered (part paths are index-prefixed). Cancel stops
  scheduling and `CancelTaskAsync`s in-flight parts + cleans the parts folder. Pause suspends the current
  part's engine (published handle) and in-flight small segments simply finish; mixed/big-part plans stay
  strictly sequential.
- [x] 3.2 Test `Parallel_segments_assemble_in_order` (6 concurrent segments, byte-exact ordered output);
  cancel-cleanup covered by the existing `Cancel_removes_the_parts_folder_and_returns_null`. Full suite
  215/215 green.

## 4. Verify & wrap-up

- [x] 4.4 **(reprocess — author feedback: "segments look serial")** Verified the parallel path is REAL:
  a new test (`Parallel_segments_actually_download_concurrently`, slow loopback server tracking in-flight
  requests) proves ≥3 overlapping segment requests through the real engine. The serial IMPRESSION came
  from the UI — "Part i/N" ticks one-by-one as parts complete and each segment shows 1 connection. The
  status now says **"Parts 12/36 · ×4"** while several segments are in flight (`Plan_PartsParallel`, all
  16 packs). Note: the author's byte-offset idea can't apply — HLS segments are separate server files,
  not ranges of one file, so parallel whole-segments is the correct equivalent (as they suspected).
- [x] 4.5 **(reprocess — author feedback: "×2/×4 is cryptic; show segments as chunks in Details")** The
  "×N" status suffix is GONE (back to plain "Part i/N"). Instead the Details window now renders every
  plan segment as its own connection row: a new `PlanRunState` board (one slot per segment: waiting /
  downloading / done + bytes/speed) is written by the runner from download threads and published on
  `DownloadItemViewModel.PlanRun`; `DownloadDetailsViewModel` polls it (400 ms timer) instead of
  attaching engine chunks (each segment is a single-chunk engine — attaching those showed one
  endlessly-resetting row). Opening Details during an HLS run now shows e.g. 36 rows: 4 "Downloading"
  with live speed, the finished ones "Completed", the rest "Pending" — exactly the requested UX. The
  summary line says "36 segments" (`Det_SegCount`, all 16 packs; `Plan_PartsParallel` key removed).
  +2 tests (runner fills the board to Done with real byte counts; the dialog renders waiting/active/done
  rows). Note: per-segment BYTE-range chunking remains impossible — segments are separate server files.
- [x] 4.1 **Author-verified (2026-07-05):** re-ran the m3u8 e2e — download + ffmpeg merge produce a
  playable .mp4, and the Details window shows all segments as parallel connection rows (~4 downloading,
  rest pending/completed). Archived on the author's explicit `/opsx:archive … done`.
- [x] 4.2 i18n: no new user-facing strings were needed.
- [x] 4.3 `docs/plugins-architecture.md` plan-runner section updated with the segment/naming rules;
  SKILL.md note added.
