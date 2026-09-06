# Released in v2.10.0 (2026-09-04)

Two fixes made straight on `develop` (no OpenSpec change — both were small and reported live by the
author), shipped together as **v2.10.0**:

- **The local API served one request at a time** (`eba1907`). A slow `/api/variants` lookup — it runs
  the site tool and had NO deadline — held every other caller, so from another tab the extension's
  `/ping` never answered (grey status dot) and a Download click sat on "…" with nothing reaching the
  app. Each request is now handled without awaiting it in the accept loop, the variants lookup got the
  Add window's 90 s valve, and every app-facing fetch in the extension goes through `appFetch`
  (abort + race). Extension **1.13.0**.
- **The extension install dialog could not say the installed copy was stale** (`82068b6`). "Available"
  is now the newer of the release catalog and the copy bundled in the app — the catalog is empty on
  every machine today, so the startup check could never fire. The dialog says "Out of date — vX is
  available", the button becomes "Update the files", Settings says it too, and an update rewrites the
  SAME folder (so the browser keeps its extension) with a notice to reload it.

## Channels

- **Tag**: `v2.10.0` on `main` (merge commit `12592e5`); develop head at release `552611c`
- **Pre-release gates**: solution rebuild `0 Warning(s) / 0 Error(s)`; app suite 1596 passed / 1 skipped;
  extension unit 151/151; Playwright e2e 29/29 (`--workers=1`); `web-ext lint` 0 errors / 0 warnings
- **GitHub Release**: published 2026-09-03T22:44Z with curated Highlights + auto "What's Changed";
  12 assets (4 platform archives, 2 extension zips, 3 optional plugins + catalog, `.deb`,
  `extension-catalog.json`). `release.yml` run `33814374176` — every job green, AUR included.
- **curl installer**: `releases/latest` → `v2.10.0`
- **Snap**: `latest/stable` = 2.10.0 (revision 26)
- **Homebrew**: `bezzad/homebrew-tap` at 2.10.0 (`7e9b41d`; arm64 `413e96ed…`, x64 `0b856942…`);
  in-repo mirror synced (`f31445a`)
- **winget**: PR [microsoft/winget-pkgs#429075](https://github.com/microsoft/winget-pkgs/pull/429075)
  (awaits moderator merge); in-repo mirror bumped (`9a13e58`, win-x64 sha `A9B56D9F…`)
- **AUR**: `downloader-bin` **published** at 2.10.0-1 by the `aur` job (linux-x64 sha `c90d96c0…`);
  in-repo mirror bumped (`552611c`). The RPC index lags a few minutes behind the git repo.

## Known red check (pre-existing, NOT caused by this release) — FIXED same day

`extension.yml` ("Publish to Mozilla AMO") **fails on every `main` push and succeeds on every
`develop` push** — 2026-09-02 and 2026-09-03 both show that pattern. Its bump guard diffs
`src/browser-extension` between the push's base and head; on the release merge that span covers every
extension commit since the last release, while the manifest version is already the one the `develop`
run published to AMO minutes earlier. So it reports "code changed without a version bump" for code
that IS published. 1.13.0 did reach AMO (run `33795414435`, green). The guard needs to compare against
what AMO actually has, or simply not run on `main`; nothing about the release artifacts depends on it —
the extension zips are attached by `release.yml`, which was green.

**Fixed** in `a84b6b6` + `b94608d`: the guard's base is now the commit that last SET the current
manifest version, so it asks "has anything changed since the bump?" — branch-independent, and the
question it always meant. It also runs on `workflow_dispatch`, so it can be exercised on demand
instead of being unverifiable until the next release merge. Proven on GitHub's own runner (run
`33815655…`, dispatch on `develop`): *"Version 1.13.0 was set in eba1907 … Nothing changed since the
bump"*, green — the same inputs that produced the failure. Locally the extracted step script was run
against the real v2.10.0 merge (passes), a synthetic edit-without-bump (fails, correct message), a
docs-only change (passes) and a bump-with-changes (passes). The next release merge carries the fixed
workflow, so it is covered.
