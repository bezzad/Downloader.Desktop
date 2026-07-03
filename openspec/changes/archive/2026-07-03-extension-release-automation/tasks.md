# Tasks — extension-release-automation

## 1. Release assets

- [x] 1.1 Add an `extension` job to `.github/workflows/release.yml`: `needs: build`, runs `scripts/build-extension.sh`, attaches both zips via `softprops/action-gh-release` (no `generate_release_notes`, release already exists — the v1.4.0 race rule)
- [ ] 1.2 (pending next `v*` tag — job can only run on a tag push) Verify on the next tag (or a re-run of a test tag) that both zips appear as release assets and notes are untouched

## 2. Mozilla AMO auto-publish

- [x] 2.1 New `.github/workflows/extension.yml`: `push` to `develop`/`main` with `paths: src/browser-extension/**` + `workflow_dispatch`
- [x] 2.2 Version gate step: read `manifest.firefox.json` version; query `GET /api/v5/addons/addon/downloader-browser-integration/versions/`; skip (green) when the version already exists on AMO
- [x] 2.3 Submit step: stage the source with `manifest.firefox.json` as `manifest.json`, `npx web-ext sign --channel listed --no-wait` with `WEB_EXT_API_KEY`/`WEB_EXT_API_SECRET` from the `AMO_JWT_ISSUER`/`AMO_JWT_SECRET` secrets; write the submitted version to the job summary
- [x] 2.4 Fail-soft guard: when secrets are unset, exit 0 with a prominent "AMO secrets not configured — skipped" notice

## 3. Author one-time setup (needs the author)

- [ ] 3.1 (author action — cannot be done by AI) Author generates AMO API credentials (addons.mozilla.org/developers → Manage API keys) and adds repo secrets `AMO_JWT_ISSUER` + `AMO_JWT_SECRET`; then re-run `extension.yml` via workflow_dispatch to submit the current 1.1.0

## 4. Docs & wrap-up

- [x] 4.1 Update `src/browser-extension/PUBLISHING.md`: Firefox = automated (how the gate works, where to see submissions), Chrome/Edge = manual upload of the release-page zip
- [x] 4.2 Note the new release assets + AMO automation in `.claude/skills/release/SKILL.md`'s checklist; commit everything to `develop` and push
