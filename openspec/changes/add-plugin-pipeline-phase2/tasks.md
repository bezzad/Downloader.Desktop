## 1. Plan model & persistence

- [ ] 1.1 Persist the resolved plan on `DownloadItem` (`PlanJson`: parts url/kind/headers/expectedSize,
  post-process kind+recipe, suggested name); write it when resolution returns a multi-part/post-process
  plan; keep it null for plain downloads.
- [ ] 1.2 Parts folder convention: `<folder>/.<final-name>.parts/NNN_<safe-name>` + completed-part
  detection (expected size match, else `.done` marker).

## 2. Plan runner (DownloadManager)

- [ ] 2.1 `Start` branches: single-part `PostProcess.None` plans keep today's exact path; otherwise run the
  plan via a `PlanRunner` partial (`Services/DownloadManager.Plans.cs`) occupying one queue slot.
- [ ] 2.2 Sequential part execution through the engine with the item's settings + per-part headers; engine
  resume applies within a part; skip already-completed parts.
- [ ] 2.3 Post-process: `FindPostProcessor` → assemble to temp name → atomic move to final path → delete
  parts folder; missing processor / thrown error → Failed with friendly message.
- [ ] 2.4 Controls: pause stops at the current part (state persists), resume continues, cancel deletes the
  parts folder; all through the existing guarded manager choke points so bulk/scheduler rules apply.
- [ ] 2.5 Retry re-resolves the original link; reuse completed parts only when the fresh plan matches,
  else start clean.

## 3. Progress & status (row VM)

- [ ] 3.1 Aggregate progress: byte-weighted when sizes known, else parts-completed/total; reserve a tail
  for assembly; feed through the existing `StageProgress` pump (no per-event UI posts).
- [ ] 3.2 Status text: `part i/N` while downloading, "Assembling…" during post-process; localized keys.

## 4. Restart resume

- [ ] 4.1 On load, an item with a persisted plan and an existing parts folder resumes from the first
  incomplete part when started; a completed-but-unassembled run goes straight to assembly.

## 5. Tests

- [ ] 5.1 Runner happy path against loopback server: 3-part plan + fake post-processor → parts downloaded
  in order, assembled output at final path, parts folder gone.
- [ ] 5.2 Headers test: loopback asserts per-part request headers arrive.
- [ ] 5.3 Pause/resume mid-plan (completed parts not re-fetched) and cancel (parts folder removed).
- [ ] 5.4 Restart-resume: new manager instance over the same config/parts → only remaining parts fetched.
- [ ] 5.5 Missing post-processor and failing part → Failed with friendly message; Retry re-resolves
  (resolver called again).
- [ ] 5.6 Regression: single-part `PostProcess.None` plan takes the legacy path (no parts folder).

## 6. End-to-end & docs

- [ ] 6.1 In-app e2e with the real HLS plugin: direct `.m3u8` → playable MP4 in the save folder; then the
  x.com flow — also closes `add-video-site-extraction` task 7.3 (note result there before archiving it).
- [ ] 6.2 README.md + docs: advertise "video/HLS downloads via plugins now work end-to-end" per the
  standing first-glance rule; update `docs/plugins-architecture.md` pipeline section; refresh screenshots
  if row status UI changed.
