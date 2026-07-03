# Extension release automation (GitHub assets + Mozilla auto-publish)

## Why

The browser extension now evolves alongside the app (v1.1.0 shipped silent add in app v1.6.0), but distributing it is fully manual: the author must run `scripts/build-extension.sh` locally and upload zips by hand in each store dashboard. Users browsing a GitHub Release also can't grab the matching extension. The author wants (a) the two extension zips attached to every GitHub Release like the app packages, and (b) automatic deployment to Mozilla AMO (listing `downloader-browser-integration`) whenever the extension version changes — Mozilla only; Chrome/Edge stay manual.

## What Changes

- **Release assets**: `.github/workflows/release.yml` gains a job that runs `scripts/build-extension.sh` and attaches `downloader-extension-chrome.zip` + `downloader-extension-firefox.zip` to the GitHub Release for every `v*` tag.
- **Mozilla auto-publish**: a new `.github/workflows/extension.yml` triggers on pushes that touch `src/browser-extension/**`; when the manifest `version` differs from the latest version on AMO, it builds the Firefox zip and submits it to AMO via the add-ons submission API (`web-ext sign --channel listed` with `AMO_JWT_ISSUER`/`AMO_JWT_SECRET` repo secrets). Same-version pushes and doc-only edits are skipped (AMO rejects duplicate versions).
- **Secrets/setup**: the author generates AMO API credentials once (addons.mozilla.org → API keys) and adds the two repo secrets; the workflow fails soft with a clear message until they exist.
- **Docs**: `src/browser-extension/PUBLISHING.md` updated — Firefox is automated, Chrome/Edge remain a manual dashboard upload of the release-page zip; release skill updated to mention the new assets.

## Capabilities

### New Capabilities
- `extension-distribution`: extension zips attached to every GitHub Release, and version-gated automatic publishing of the Firefox build to Mozilla AMO.

### Modified Capabilities

_None — no existing spec's requirements change._

## Impact

- `.github/workflows/release.yml` (new extension-assets job), new `.github/workflows/extension.yml`.
- Repo secrets: `AMO_JWT_ISSUER`, `AMO_JWT_SECRET` (author-provided; one-time).
- `src/browser-extension/PUBLISHING.md`, `.claude/skills/release/SKILL.md`.
- No app-code changes; no new runtime dependencies (CI uses `web-ext` from npm).

---
**Archive note (2026-07-03):** implemented and live-tested (version gate + fail-soft verified on the
workflow's first real run). A bump guard was added during implementation (code change without a
version bump now fails CI). Two tasks remain externally blocked at archive time: 1.2 self-verifies on
the next `v*` tag; 3.1 is the author's one-time AMO API-key setup (`AMO_JWT_ISSUER`/`AMO_JWT_SECRET`),
after which a workflow_dispatch run submits extension 1.1.0 to AMO.
