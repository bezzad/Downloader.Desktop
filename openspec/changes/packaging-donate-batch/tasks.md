# Tasks — packaging-donate-batch

Keep build + full `dotnet test` green; commit to `develop`, push, per logical step.

## 1. x.com syndication fallback (HLS 1.3.2)

- [x] 1.1 Tests: `BuildArgs` emits `--extractor-args "twitter:api=syndication"` when passed; `IsTwitter` matches x.com/twitter (+ subdomains), rejects look-alikes.
- [x] 1.2 Add `extractorArgs` param to `BuildArgs`, `SyndicationArgs` const, `IsTwitter(url)`; insert a cookie-free syndication retry for twitter URLs after the anonymous attempt, before the browser-cookie loop. Bump csproj `<Version>` 1.3.1 → 1.3.2.
- [x] 1.3 Build + full tests green.

## 2. Debian .deb + GitHub Pages APT repo

- [x] 2.1 `scripts/build-deb.sh` — build a `Downloader_<ver>_amd64.deb` from a linux-x64 self-contained publish (control, desktop file, icon, `/usr/bin/downloader` symlink, postinst/prerm).
- [x] 2.2 `packaging/apt/` — generate a GPG signing key; commit `pubkey.gpg`; `build-apt-repo.sh` runs `dpkg-scanpackages` + signs `Release`→`InRelease`/`Release.gpg`.
- [x] 2.3 `release.yml` `deb` job: build the .deb, attach to the release, rebuild the apt repo into `docs/apt`, deploy to Pages (needs `APT_GPG_PRIVATE_KEY` secret + Pages enabled — documented).
- [x] 2.4 README + `packaging/apt/README.md`: the add-repo one-liner + install command.

## 3. GitHub Sponsors + Donate modal

- [x] 3.1 `.github/FUNDING.yml` (github/liberapay/custom).
- [x] 3.2 `DonateViewModel`: add `GitHubSponsorsUrl` + `OpenSponsorsCommand`; `DonateView.axaml`: a GitHub Sponsors button. i18n key in all 16 packs. `AboutViewModel` const + `Donate.md` section.
- [x] 3.3 Test for the new command/URL; build + full tests green; regenerate Donate/About screenshots if the UI changed.

## 4. MSIX packaging (build-only)

- [x] 4.1 `packaging/msix/AppxManifest.xml` + assets (Publisher `CN=bezzad`); `scripts/build-msix.ps1` (+ `.sh` wrapper) using `makeappx`/`signtool` with a self-signed cert.
- [x] 4.2 `release.yml` `msix` job builds the `.msix` artifact (self-signed) on Windows.
- [x] 4.3 `packaging/msix/README.md`: sideload steps + the Partner Center / Store-submission checklist left for the author.

## Wrap-up

- [x] W.1 `dotnet build` clean; full `dotnet test` green (451, +7 new); Donate modal rendered + eyeballed.
- [x] W.2 Commit/push on `develop`. **NOT archived yet** — code/infra is done but each deliverable needs
  an author-gated activation step before it's live (see proposal "Out of scope / author-gated"):
  enroll GitHub Sponsors; enable Pages + add `APT_GPG_PRIVATE_KEY` secret; register Partner Center.
  Archive once those are done and a release has verified the apt/msix jobs.
