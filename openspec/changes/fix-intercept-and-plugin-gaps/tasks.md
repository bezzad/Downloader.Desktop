> **Read first (this change will be implemented from a different session).** Start with
> `openspec show fix-intercept-and-plugin-gaps`, then `proposal.md` → `design.md` → the delta specs under
> `specs/`. `design.md` — Context lists what has already been reproduced and measured; do not re-derive it.
>
> **Every task ships with tests. A task is done when its test exists, is new or changed for this task, and
> the whole suite is green — never on "it builds" or "I tried it by hand".** Pure decisions get unit tests
> (`common.test.js` via `node --test`, or `Downloader.Desktop.Tests/Unit`); user-visible paths get a
> headless UI test or a Playwright e2e test. Per CLAUDE.md, at the end of a batch run all three suites
> (`dotnet build Downloader.Desktop.sln -t:Rebuild` with **0 warnings**, `dotnet test`,
> `node --test src/browser-extension/common.test.js`, and the `e2e/` Playwright suite) before reporting done.
>
> Groups 1–2 are the issue #9 follow-up and ship first, on their own. Groups 3–5 are independent tracks and
> may ship in later versions. Commit per logical step on `develop`.

## 1. Interception: get the file type right (issue #9 — APKPure)

- [x] 1.1 Add failing unit tests in `common.test.js` for the reproduced shapes — `d.apkpure.com/b/XAPK/com.instagram.android?version=latest`, `d.apkpure.com/b/APK/com.whatsapp?version=latest`, `d.cdnpure.com/b/XAPK/com.foo.bar` — asserting `shouldIntercept` returns `{intercept:true}` with the default type list; verify they fail against today's code for the reason design.md records (`ext === "android"`).
- [x] 1.2 Add a path-extension plausibility filter (design decision 2) with unit tests covering both directions: `com.instagram.android`/`example.co.uk`/`v1.2.3` contribute no candidate, while `installer.msi`, `app.appimage` and an unusual-but-real `.zst` still do.
- [x] 1.3 Replace the single-answer lookup with `candidateExts(item)` and match the user's list against the whole set (design decision 1), keeping `resolveDownloadExt` for display; unit-test allow-mode ("any candidate listed ⇒ intercept"), deny-mode ("any candidate listed ⇒ decline"), and that `reason` still distinguishes `type-unknown` from `type-not-allowed`.
- [x] 1.4 Cache `Content-Disposition` + `Content-Type` from the existing `webRequest.onHeadersReceived` listener into a bounded LRU and feed it into the decision (design decision 3); unit-test the cache (bound respected, entries expire, miss is harmless) and assert **no manifest permission change** by diffing `manifest.json`/`manifest.firefox.json` permissions in a test.
- [x] 1.5 Add a Playwright e2e case serving an extensionless URL whose `Content-Disposition` names a `.xapk`, and assert the extension hands it to the stub app instead of letting the browser keep it.
- [x] 1.6 Verify the whole group against the real regression: `node --test src/browser-extension/common.test.js` green, `npm test` in `e2e/` green, and the tests from 1.1 now passing.

## 2. Interception: hand over a link the app can actually fetch (issue #9 — Softpedia)

- [x] 2.1 Send the clicked link as primary and the redirect chain's end as a mirror (design decision 4); unit-test the hand-off body for the redirected case (`url` = `item.url`, `mirrors` = `[finalUrl]`), the non-redirected case (no `mirrors`), and that cookies are captured for the primary link's host.
- [x] 2.2 Add an app-side test that `/api/add` with `mirrors` produces a `DownloadItem` whose `Urls` are primary-then-mirror in order (`LocalApiService` already merges them — the test pins it against regression).
- [x] 2.3 Mark a download that arrived from the extension on the `DownloadItem`, set it in `/api/add` when the request carried extension context, and unit-test that it round-trips through save/load with `false` for existing records.
- [x] 2.4 Allow one auto link-refresh from zero bytes for such downloads (design decision 5) and unit-test all four cases: extension hand-off at 0 bytes retries once; a pasted link at 0 bytes still fails immediately; the bound still holds; a non-expired failure never retries.
- [x] 2.5 Add the honest failure message for an extension hand-off the app could not fetch while the browser still has the file (design decision 6), with the new key added to **all 16** `Assets/i18n/*.json` packs; unit-test the message selection and add a test asserting no i18n pack is missing the key.
- [x] 2.6 Add a Playwright e2e case where the stub app accepts then fails the transfer, asserting the browser's download is never cancelled and the user is told — pinning the v2.7.0 safety net against regression.

## 3. Interception: diagnostics and the app-detection report

- [x] 3.1 Show an explicit "Downloader not found on ports 15151–15155" state in the popup when discovery fails, clearing when the app returns; cover both with a Playwright e2e case (app stub down, then up).
- [ ] 3.2 Record in the issue thread — from the evidence in design.md — that nothing in extension 1.5.0 requires app 2.7.0 (`/ping` since 2.5.0, `id` since v1.6.0, app diff v2.6.1→v2.7.0 touches only the updater), and ask the reporter to confirm with the new diagnostic. **Draft the text and get the author's explicit OK before posting** (standing rule).
- [x] 3.3 Bump the extension version, refresh `PUBLISHING.md`/`README.md` where they state what is fixed, and verify the packaged zip loads unpacked in Chromium with no console errors.

## 4. YouTube: an optional site-extraction plugin (author's choice — see design decision 7)

- [x] 4.1 Create the optional plugin project `Downloader.Desktop.Plugins.SiteMedia` (id `com.bezzad.site-media`), NOT referenced by the app and NOT in the app csproj's `StageBundledPlugins` allow-list; verify with `PluginIsolationTests` and by grepping a `dotnet publish` output for the assembly (the check `release.yml` performs).
- [x] 4.2 Implement claiming + resolution of a supported site page into media parts with a title-derived file name, behind an interface so tests are network-free; unit-test claim/decline, the parts produced from a recorded tool output, and the "unextractable page" failure message.
- [x] 4.3 Offer per-quality link variants through the existing `link-variants` mechanism; unit-test that several qualities produce several variants and that the chosen one is what gets downloaded.
- [x] 4.4 Fetch + sha256-verify the extraction tool on first use, run it from an absolute path with no shell (reuse the `BinaryFile`/`FfmpegBinary` pattern); unit-test that a checksum mismatch discards and never executes, and **extend `NoShellSpawnTests` to scan this plugin's source** rather than exempting it.
- [x] 4.5 Pass the extension's cookies through `ResolveOptions`/`CookieFile` into the extraction, and assert by test that no browser profile/cookie-store path is ever read and that cookies are neither persisted with the download record nor written to the log.
- [x] 4.6 Make the extension's unsupported-site state conditional on what the running app can handle (design decision 7); unit-test the popup decision (plugin present ⇒ offer the page; absent ⇒ today's message naming the plugin, never "sign in"), plus a Playwright e2e case for both states.
- [x] 4.7 Replace the misleading "you must be signed in in the browser" wording on the manual-paste path with one that names the real cause, in all 16 i18n packs; unit-test the message and the pack coverage.
- [x] 4.8 Add the plugin to `packaging/plugins/optional-plugins.json` + `scripts/build-plugins.sh` with a `minAppVersion`, and verify the generated `plugins-catalog.json` lists it with a real version and sha256.

## 5. Ollama plugin: HuggingFace models and the lost install offer

- [x] 5.1 Reproduce the missing "Add to Ollama" offer with a failing test first, driving a completed Ollama-resolved row through each of the three completion routes (`DownloadManager.cs:1359`, `Plans.cs:104`, `Transfers.cs:60`) and asserting `PostDownloadActionLabel` is non-null and the offer notification fires; record which route breaks and why before changing code.
- [x] 5.2 Fix the identified cause and keep all three route tests green, plus a test that the offer survives a save/load restart cycle (`ResolverPluginId` and the source URL persisted).
- [x] 5.3 Add a headless UI test that a completed model row actually shows the action button — the offer must be visible, not merely computable.
- [x] 5.4 Claim HuggingFace model repo URLs (`https://huggingface.co/<owner>/<repo>`, revision and `resolve/...` file forms) with no network I/O in the claim check; unit-test the reporter's link `https://huggingface.co/empero-ai/Qwen3.8-2B-Distill-GGUF` is claimed and that datasets/spaces/profile pages are not.
- [x] 5.5 List a repo's GGUF files from the HuggingFace API behind a test seam and offer them as variants with quantisation + size; unit-test multi-file (variants offered), single-file (no prompt), no-model-file and missing/private repo failures — all network-free.
- [x] 5.6 Install a downloaded HuggingFace GGUF into the local Ollama store under `hf.co/<owner>/<repo>:<quant>`, verifying against what the repo publishes for the file, never moving the user's download; unit-test success against a temp store root, checksum mismatch writes nothing, and missing Ollama fails with the "where it looked" message.
- [x] 5.7 State the sharded-GGUF limitation as an explicit, tested failure message rather than a partial download.
- [x] 5.8 Bump `Downloader.Desktop.Plugins.Ollama.csproj` `<Version>` (standing rule — the catalog compares versions) and verify the new version appears in Settings → Plugins.

## 6. Close-out

- [x] 6.1 Full solution rebuild with **0 warnings**, `dotnet test` green, `node --test` green, Playwright `npm test` green — all four, per CLAUDE.md's standing apply-session step.
- [x] 6.2 Refresh `docs/screenshots/` if any view changed (`DLDESKTOP_CAPTURE=1 dotnet test --filter FullyQualifiedName~CaptureScreenshots`) and **view the PNGs** before committing them.
- [x] 6.3 Update `CLAUDE.md` and `docs/codebase-index.md` for the new plugin and the interception changes, and append any non-obvious gotcha found here to `.claude/skills/downloader-desktop/SKILL.md`.
- [ ] 6.4 Draft the issue #9 reply (what was fixed, what needs the reporter's confirmation on Softpedia's secure mirror) and **wait for the author's explicit OK before posting** — state the request and the current state only, never our proposed approach.
- [ ] 6.5 `/opsx:sync` the delta specs into `openspec/specs/`, then `/opsx:archive` this change.
