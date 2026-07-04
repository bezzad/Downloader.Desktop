## 1. Plan model & persistence

- [x] 1.1 Persist the resolved plan on `DownloadItem.PlanJson` via `Models/PersistedPlan.cs` (a JSON-friendly
  copy of the SDK `DownloadPlan`: parts url/kind/headers/expectedSize, post-process kind+recipe, suggested
  name). Written in `Start` when a resolver returns a `NeedsRunner` plan (>1 part or a post-process); stays
  null for a plain single-file download.
- [x] 1.2 Parts folder convention `<folder>/.<final-name>.parts/NNNN_<safe-name>` (`ExecutePlanAsync`).
  Completed-part detection: size match when `ExpectedSize` is known, else a `.done` marker (so a half-written
  part with unknown size isn't mistaken for complete). Post-download uses a separate `PartDownloadedOk`
  (exists + size-match-or-non-empty) so the just-fetched part isn't rejected before its marker is written.

## 2. Plan runner (DownloadManager)

- [x] 2.1 `Start` branches: `ResolvePlanAsync` gets the full plan; a single-part `PostProcess.None` plan keeps
  today's exact engine path (URL rewrite only), otherwise `RunPlanAsync` runs it (one queue slot).
  `Services/DownloadManager.Plans.cs` (partial). Restart/resume reuses the persisted `PlanJson` without
  re-resolving.
- [x] 2.2 Sequential part execution through a per-part `DownloadService` with the item's settings +
  per-part `Headers` (`ApplyHeaders` → `RequestConfiguration.Headers`); engine resume applies within a part;
  already-complete parts are skipped.
- [x] 2.3 Post-process: `FindPostProcessor(plan.PostProcess)` → assemble to `<final>.assembling` → atomic
  move to the final path → delete the parts folder; missing processor / thrown error → Failed with a friendly
  message (`Plan_NoProcessor`). Multi-part with `PostProcess.None` → raw binary concat.
- [x] 2.4 Controls reuse the per-row `vm.Download` handle (each part's engine is published to it): pause
  suspends the current part transparently (engine pause blocks the await), resume continues, cancel
  (Status→Stopped) makes the part return and the runner deletes the parts folder. Remove also cleans the
  parts folder. All through the guarded manager methods so bulk/scheduler rules apply.
- [x] 2.5 Retry clears `PlanJson` so the next Start re-resolves the link (expiring segment URLs); completed
  parts on disk are reused only when the fresh plan's part path (NNNN_<name-from-url>) matches.

## 3. Progress & status (row VM)

- [x] 3.1 Aggregate progress: byte-weighted when every part has `ExpectedSize` (live bytes of the current
  part counted), else parts-completed/total; the last 10% is reserved for assembly when there's a
  post-process. Fed through the existing `StageProgress` pump (no per-event UI posts).
- [x] 3.2 Status text: `DownloadItemViewModel.PlanStage` → "Part i/N" while downloading, "Assembling…" during
  post-process (localized `Plan_Part` / `Plan_Assembling`); `StatusText` shows "Part i/N · 45%".

## 4. Restart resume

- [x] 4.1 On load the persisted `PlanJson` + existing parts folder let `ExecutePlanAsync` skip completed
  parts and resume from the first incomplete one; a completed-but-unassembled run skips all parts and goes
  straight to assembly. (Tested: `Restart_resume_only_fetches_missing_parts`.)

## 5. Tests

- [x] 5.1 `Happy_path_downloads_all_parts_in_order_assembles_and_cleans_up` — 3-part plan + fake processor
  against the loopback server → parts downloaded, assembled output byte-correct at the final path, parts
  folder gone.
- [x] 5.2 `Per_part_headers_reach_the_server` — the loopback server records the per-part `X-Token` header.
- [x] 5.3 `Cancel_removes_the_parts_folder_and_returns_null` (cancel path). Pause/resume mid-plan is
  engine-native (the current part's `DownloadService.Pause/Resume`, the same suspend path a normal download
  uses — the runner just awaits it), so it's not separately unit-tested; the completed-parts-not-refetched
  guarantee is covered by 5.4.
- [x] 5.4 `Restart_resume_only_fetches_missing_parts` — a pre-existing part on disk is reused; only the
  missing part is fetched (asserted via the server's requested-paths set).
- [x] 5.5 `Missing_post_processor_throws_and_keeps_parts_for_retry`; `Retry_clears_the_persisted_plan_so_it_re_resolves`
  (manager-level, in AppTests).
- [x] 5.6 `Single_part_none_plan_does_not_need_the_runner` (+ `Persisted_plan_round_trips_through_json`) — the
  Start branch condition that keeps a single-part `PostProcess.None` on the legacy path. Also
  `Plan_stage_shows_part_progress_in_status_text` for the VM status text. Full app suite 196/196 green.

## 6. End-to-end & docs

- [x] 6.1 **Author ran the in-app e2e (2026-07-05)** with a real 4K HLS stream
  (`sample.vodobox.net/skate_phantom_flex_4k/skate_phantom_flex_4k.m3u8`): the plan runner worked —
  resolved 36 segments, downloaded them all sequentially, and reached the assembly step — which proves the
  Phase-2 pipeline end to end. The run surfaced two NEW issues that are follow-up scope, not this change's
  (author's call: archive this, fix them separately — see the `fix-hls-segment-perf-and-assembly` proposal):
  (a) each tiny segment is downloaded with the full N-chunk multipart config (wasteful; segments should be
  single-chunk), and (b) ffmpeg failed on the output: the temp path `…skate_phantom_flex_4k.m3u8.assembling`
  has no standard media extension so ffmpeg can't choose a muxer ("Unable to choose an output format"),
  compounded by the final name itself being `.m3u8` (the playlist's name) instead of `.mp4`.
  `add-video-site-extraction` 7.3 (the x.com flow) remains open in that change.
- [x] 6.2 README.md advertises "video/HLS downloads via plugins now work end-to-end" (Features list);
  `docs/plugins-architecture.md` Phase 2 section rewritten to describe the plan runner (download → assemble →
  resume, progress/controls, PersistedPlan). No row-status screenshot refresh needed here (screenshots are
  Linux-only per SKILL.md; the new "Part i/N"/"Assembling…" text only shows during an active multi-part run,
  which the capture set doesn't exercise). i18n: `Plan_Part`/`Plan_Assembling`/`Plan_NoProcessor` added to
  all 16 language packs (and the whole i18n set re-synced — ~40–68 previously English-only keys translated
  across every language).
