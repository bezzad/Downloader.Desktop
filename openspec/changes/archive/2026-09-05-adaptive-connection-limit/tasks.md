## 1. Turn the latch into a count

- [x] 1.1 Replace `DownloadItemViewModel.ForceSingleConnection` (bool) with `AttemptConnections` (`int?`, null = use the ceiling), and update `Start` to apply it — `ChunkCount = n`, `ParallelDownload = n > 1` — instead of hard-coding 1.
- [x] 1.2 Rewrite `TryReduceConnections` to halve `vm.PlannedConnections` (8 → 4 → 2 → 1) rather than latching to one, keeping every existing guard: same address, ahead of `TryNextUrl`, skipped when `vm.LinkRefreshAttempts > 0`, and skipped when `vm.PreAttemptSize is not null` (a resume must keep its partial file).
- [x] 1.3 Cap the reduced attempts per download and reset the count on the paths that already reset the other budgets (`Resume`, `Retry`, and advancing to the next address in `TryNextUrl`).
- [x] 1.4 Unit-test the pure decision: which count follows a refusal at 8/4/2/1, that a resume yields none, that a 403 with one connection in flight yields none, and that the sequence is bounded whatever the ceiling.
- [x] 1.5 End-to-end through the manager: a loopback server that accepts at most N concurrent requests and answers 403 beyond it must leave the download **completed at N**, with the file's bytes asserted — and a test proving a download that settles at 4 never attempts 2 or 1.

## 2. Remember the limit per host

- [x] 2.1 Add the per-host store to `Config` (host → accepted count + when it was learned), with the JSON round-trip and a missing/corrupt entry degrading to "no memory" rather than throwing.
- [x] 2.2 Put the lookup behind a small seam (pure `ChooseStartingCount(host, ceiling, now)`) so every rule below is testable without a download.
- [x] 2.3 Record the limit when a download settles below the ceiling; clear it when a re-test at the ceiling succeeds.
- [x] 2.4 Apply the memory in `Start`: begin at the remembered count, always clamped by the configured ceiling (a remembered 8 must never beat a user who has since chosen 2).
- [x] 2.5 Expire entries after the re-test interval so a host that was strict once is not punished forever, and unit-test the expiry boundary and the clamp.
- [x] 2.6 Test persistence: a recorded limit survives a save/load cycle and still applies.

## 3. Say what the app decided

- [x] 3.1 Add the status wording for a download running below the configured count ("the server refused several connections; using fewer") to all 16 language packs.
- [x] 3.2 Update `DescribeFailure` so a download the app has already stepped down no longer tells the user to lower the setting by hand, while the expired-link message stays untouched.
- [x] 3.3 Test that a stepping-down row reads as working rather than failed, and that the expired-link wording never appears for a concurrency refusal.

## 4. Fold in the existing behaviour

- [x] 4.1 Update `UrlFailoverTests` and `UrlAttemptTests` to assert the count the download settled at rather than the old `ForceSingleConnection` flag — and keep the v2.9.0 regressions they pin (same address first, next address restores full concurrency, a resume keeps its partial file).
- [x] 4.2 Sync the `link-refresh` delta into `openspec/specs/link-refresh/spec.md` and add the new `server-connection-limits` capability.
- [x] 4.3 Note in `.claude/skills/downloader-desktop/SKILL.md` that the count is now stepped and cached per host, and why each step still discards the partial file.

## 5. Verify

- [x] 5.1 `dotnet build Downloader.Desktop.sln -t:Rebuild` — 0 warnings.
- [x] 5.2 `dotnet test` green, plus the extension suites (`node --test src/browser-extension/common.test.js` and the Playwright specs) since the release routine requires all three.
- [ ] 5.3 Reply on [#14](https://github.com/bezzad/Downloader.Desktop/issues/14) with what shipped — draft the text and get the author's OK before posting.
