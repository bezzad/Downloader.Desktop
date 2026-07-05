# Design — extension-release-automation

## Context

`scripts/build-extension.sh` already produces the two store zips deterministically (verified: both carry manifest 1.1.0). `release.yml` builds app archives per RID on `v*` tags and attaches them with `softprops/action-gh-release`. The AMO listing exists (`downloader-browser-integration`), so submissions are *updates* on the listed channel. Mozilla's submission API is stable and scriptable; Chrome's requires a paid OAuth setup the author doesn't want automated.

## Goals / Non-Goals

**Goals:**
- Every `v*` GitHub Release carries `downloader-extension-chrome.zip` and `downloader-extension-firefox.zip`.
- A version bump in `src/browser-extension/manifest*.json` pushed to `develop` or `main` publishes the Firefox build to AMO automatically, exactly once per version.
- Zero manual steps for Firefox after the one-time API-key setup; clear failure messages before it.

**Non-Goals:**
- No Chrome Web Store / Edge Add-ons automation (manual dashboard upload stays; the release-page zip is the artifact to upload).
- No AMO listing-metadata management (description/screenshots stay dashboard-managed).
- No coupling of extension version to app version — they ship on their own cadence.

## Decisions

1. **Release assets via a dedicated job in `release.yml`** that runs on ubuntu, executes `scripts/build-extension.sh`, and uploads both zips with the same `softprops/action-gh-release` action. CRITICAL (learned on v1.4.0): the release-create steps race when several jobs create the release concurrently — the extension job must run `needs: build` (after the matrix) so the release already exists, and must not set `generate_release_notes`.

2. **AMO publish in a separate `extension.yml` workflow**, not inside `release.yml` — the extension version changes independently of app tags ("when a version of browser-extension changed" is the trigger the author asked for). Trigger: `push` to `develop`/`main` with `paths: src/browser-extension/**` + `workflow_dispatch` for manual re-runs.

3. **Version gate instead of diff parsing**: the job reads `manifest.firefox.json`'s `version` and queries AMO's public API (`GET /api/v5/addons/addon/downloader-browser-integration/versions/`) for existing versions; it submits only when the version is new. This is idempotent (safe on force-pushes, re-runs, doc edits) and avoids fragile git-diff logic. AMO would reject a duplicate version anyway — the gate keeps the workflow green instead of red on no-ops.

4. **Submission via `web-ext sign --channel listed`** (official Mozilla tool, npm `web-ext`): builds/uploads the xpi from the Firefox-manifest source dir and waits for validation. Auth = `AMO_JWT_ISSUER`/`AMO_JWT_SECRET` repo secrets (author creates at addons.mozilla.org/developers → "Manage API keys"). Listed-channel submissions still go through AMO review — "automated deploy" means submitted-for-review automatically; Mozilla's human/auto review then publishes. The workflow surfaces the submitted-version URL in its summary.

5. **Source layout for web-ext**: web-ext expects the Firefox manifest named `manifest.json`. The job stages `src/browser-extension` into a temp dir, swaps `manifest.firefox.json` → `manifest.json` (exactly what `build-extension.sh` does), and points `web-ext sign` at it.

6. **Fail-soft on missing secrets**: if the secrets are unset, the job exits 0 with a prominent notice ("AMO secrets not configured — skipped") rather than failing every extension push before the one-time setup.

## Risks / Trade-offs

- [AMO review can reject a submission] → the workflow reports the AMO validation output; rejection emails go to the author as today. Nothing is lost — resubmit after fixes.
- [`web-ext sign` can time out waiting for review on listed channel] → use `--no-wait` (submit and exit) so CI isn't held hostage by review queues; the gate's version check keeps idempotency.
- [Secrets leakage] → secrets only in the official web-ext env vars (`WEB_EXT_API_KEY`/`WEB_EXT_API_SECRET`), never echoed.
- [Chrome zip on the release page could drift from what's on the Chrome store] → intended: the release asset IS the artifact the author uploads manually; PUBLISHING.md states it.

## Open Questions

_None — the author specified: release-page zips for both targets, automation for Mozilla only._
