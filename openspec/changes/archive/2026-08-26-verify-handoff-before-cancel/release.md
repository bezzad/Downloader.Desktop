# Released in v2.7.0 (2026-08-29)

Ships the two issue #9 follow-up changes archived on 2026-08-26 —
`intercept-extensionless-downloads` and `verify-handoff-before-cancel` — plus the test-coverage
work another session landed on `develop` in the meantime.

- **Tag**: `v2.7.0` on `main`; released from a fresh, up-to-date `develop` clone (`98e6186`),
  develop 20 commits ahead of `bf652cb`.
- **Minor, not patch**: the desktop app's own behaviour is unchanged since v2.6.1, but the extension
  gains real capability (it now intercepts the signed-CDN links the major sites actually serve), so
  a feature-level bump was the honest choice.
- **Pre-release gates**: app suite **883/883** (up from 525 — the new coverage work); extension unit
  **71/71**; Playwright e2e **14 passed / 1 skipped**.
- **GitHub Release**: [v2.7.0](https://github.com/bezzad/Downloader.Desktop/releases/tag/v2.7.0),
  10 assets, curated notes; run
  [33234764372](https://github.com/bezzad/Downloader.Desktop/actions/runs/33234764372) — all 10 jobs green.
- **Verified in the published artifact**: `downloader-extension-chrome.zip` reports extension
  **1.5.0**, contains the options page, and its bundled `common.js` carries both fixes
  (`confirmAppFetching`, `filenameFromUrlQuery`, `xapk`).
- **Snap**: run [33234764392](https://github.com/bezzad/Downloader.Desktop/actions/runs/33234764392)
  green — **revision 21**, `latest/stable`, confirmed 2.7.0 via the store API.
- **Homebrew**: tap at 2.7.0; in-repo mirror synced.
- **winget**: PR [microsoft/winget-pkgs#425985](https://github.com/microsoft/winget-pkgs/pull/425985)
  open; mirror bumped (`44e240a`).
- **AUR**: mirror bumped (`1589461`), publish warn-skipped again (`AUR_SSH_PRIVATE_KEY` unset) —
  `downloader-bin` remains at **2.2.0**.

## Run notes

Clean run, exit 0. Notably the winget step opened its PR without help this time: the lost-manifest
race that needed a manual retry on v2.6.1 did not recur. It is still unguarded — `submit_winget`'s
three `gh api -X PUT` calls have no `retry()` around them and swallow stderr — so it remains a
latent flake rather than a fixed bug.

The winget fork still cannot sync (`merge-upstream` → 422, the `gh` token lacks the `workflow`
scope). Harmless per release, but the fork drifts further from upstream each time.

## Issue follow-up

Issue #9 stays OPEN. @ray2me123's 2026-08-26 test report is answered by this release for three of
his four cases (GitHub, APKPure, Softpedia ZIP). The fourth — Softpedia "Secure Download" — could
not be reproduced from this environment (Cloudflare blocks the IP), so its root cause is still one
of two candidates; v2.7.0 makes it safe either way (the browser's download is no longer cancelled
without proof the app is really fetching) and may fix it outright via the forwarded User-Agent.
Confirmation needs the reporter.
