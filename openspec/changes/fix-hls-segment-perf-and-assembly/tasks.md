## 1. Single-chunk segments (D1 — recommended)

- [ ] 1.1 In `ExecutePlanAsync`'s part loop, override the per-part config for `PartKind.Segment` parts
  (and any part with `ExpectedSize` < ~8 MB): `ChunkCount = 1`, `ParallelDownload = false`.
- [ ] 1.2 Test: a plan with `Kind = Segment` parts runs with a single-chunk configuration (assert via
  the loopback server seeing no Range-split requests / via the config passed to the service).

## 2. Assembly naming fixes (D2 + D3 — required)

- [ ] 2.1 Replace `finalPath + ".assembling"` with `AssemblingPath(finalPath)` = `{stem}.assembling{ext}`
  (extension last) in both the post-process and concat paths.
- [ ] 2.2 Add pure `NormalizeAssembledName(name, plan)`: post-processed plans never keep `.m3u8`/`.m3u`
  (or empty) extensions — swap to `.mp4`, unless the plugin's `SuggestedFileName` supplies a different
  concrete media extension. Apply where `RunPlanAsync` picks the final name.
- [ ] 2.3 Tests: temp path shape (`video.assembling.mp4`), `.m3u8` → `.mp4` normalization, user-typed
  `.mkv` name preserved, plugin-suggested extension wins.

## 3. Parallel segment downloads (D4 — optional, author's call)

- [ ] 3.1 Bounded-concurrency part loop (M = min(4, user parallel setting)) for segment parts, single
  chunk each; ordered assembly by part index; cancel stops all in-flight parts; pause pauses them.
- [ ] 3.2 Tests: ordering preserved with concurrency, aggregate progress monotonic, cancel cleans up.

## 4. Verify & wrap-up

- [ ] 4.1 Author re-runs the failing e2e (`skate_phantom_flex_4k.m3u8`) → assembled playable `.mp4` in
  the save folder; segments visibly faster (single-chunk, and parallel if D4 landed).
- [ ] 4.2 i18n: no new user-facing strings expected; if any are added, sync ALL 16 language packs.
- [ ] 4.3 Update `docs/plugins-architecture.md` plan-runner section with the naming/config rules; note
  in SKILL.md.
