# Proposal — packaging-donate-batch

Fourth reported batch (2026-07-17). Four independent deliverables the author asked for in one go.

## 1. x.com "some links won't download" — cookie-free syndication fallback

**Report:** `https://x.com/rozbeh1538493/status/2077984305343578598/video/1` (and other tweets) fail to download.

**Diagnosis (live, this machine):** the whole chain is actually sound on current `develop` — yt-dlp
(2026.07.04) extracts 9 formats for that exact link, `SiteExtractor` correctly picks the `http-2176`
progressive MP4 (`LikelyCombined`, 720×1280), and that stream is a plain public 6.4 MB `video/mp4`
(HTTP 200, no auth). So the happy path works *right now*.

The real problem is **intermittency**: x.com's anonymous guest-token GraphQL API periodically stops
returning tweet media ("No video could be found in this tweet") — that's exactly what the previous fix
(`1bb08e6`, HLS 1.3.1) responded to by retrying with `--cookies-from-browser`. But browser-cookie
reading is the fragile path: it hangs on the macOS keychain / Chrome app-bound encryption and needs a
signed-in browser, so for a user without that hand-off the retry effectively fails → "won't download".

**Fix:** before touching browser cookies, retry x.com/twitter URLs through yt-dlp's **Twitter
syndication API** (`--extractor-args "twitter:api=syndication"`). It's a cookie-free public endpoint
that serves public tweet media even when the guest-token API returns nothing (verified live: 9 formats,
same as anonymous). Order becomes: extension cookies (if any) → anonymous → **syndication (twitter only)**
→ browser-cookie loop. Bump HLS plugin 1.3.1 → 1.3.2.

## 2. `apt install downloader` — GitHub Pages APT repository

Ship a Debian package and host a signed APT repo on GitHub Pages so users can:
```
curl -fsSL https://bezzad.github.io/Downloader.Desktop/apt/pubkey.gpg | sudo gpg --dearmor -o /usr/share/keyrings/downloader.gpg
echo "deb [signed-by=/usr/share/keyrings/downloader.gpg] https://bezzad.github.io/Downloader.Desktop/apt stable main" | sudo tee /etc/apt/sources.list.d/downloader.list
sudo apt update && sudo apt install downloader
```
A one-word `apt install downloader` on a stock machine (no repo added) needs the official Debian/Ubuntu
archive (maintainer sponsorship) — out of scope; the added-repo flow above is the achievable path and
auto-updates via `apt upgrade`. `scripts/build-deb.sh` builds the `.deb` from the linux-x64 self-contained
publish; a `deb` CI job (in `release.yml`) builds it, regenerates the apt repo metadata (`dpkg-scanpackages`
+ signed `Release`/`InRelease`), and deploys `docs/apt` to Pages. A repo GPG signing key is generated;
public key committed at `packaging/apt/pubkey.gpg`, private key stored as the `APT_GPG_PRIVATE_KEY` secret.

## 3. GitHub Sponsors + Donate modal

Add `.github/FUNDING.yml` (`github: [bezzad]`, `liberapay: bezzad`, plus the existing custom links) so the
repo shows a Sponsor button. Add a **GitHub Sponsors** entry to the in-app Donate modal
(`DonateViewModel`/`DonateView`) alongside Liberapay + USDT, and to `Donate.md` + `AboutViewModel`.
Lights up once the author enrolls at github.com/sponsors; the link/config are wired now.

## 4. MSIX packaging (build-only, self-signed)

Add Windows MSIX packaging: an `AppxManifest.xml` (Publisher `CN=bezzad`, self-signed for sideload),
assets, and `scripts/build-msix.sh`/`.ps1` that wrap the win-x64 publish with `makeappx` + `signtool`
(self-signed cert). A `msix` CI job builds an unsigned/self-signed `.msix` artifact. Store submission is
left ready for the author to finish once a Partner Center account + Publisher identity exist (documented
in `packaging/msix/README.md`). Not submitted (external, author-gated).

## Out of scope / author-gated
- Official Debian/Ubuntu archive inclusion; Launchpad PPA.
- Actual GitHub Sponsors enrollment (bank/Stripe) — author only.
- Microsoft Store submission + Partner Center registration — author only.
- Enabling GitHub Pages on the repo (Settings → Pages → GitHub Actions) — author only.

## Released in v2.2.0 (2026-07-18)

- Tag `v2.2.0` on `main` (release commit `82bc441`); GitHub Release live with all assets incl.
  `Downloader_2.2.0_amd64.deb`. Notes: curated Highlights + auto changelog.
- **APT repo LIVE** at https://bezzad.github.io/Downloader.Desktop/apt (Pages, build_type=workflow).
  Verified end-to-end: `InRelease` signature validates against the served `pubkey.gpg`; the pooled
  `.deb` is reachable; `Packages` lists `downloader 2.2.0`.
- MSIX self-signed artifact built by the `msix` job. Homebrew tap → 2.2.0; winget PR
  microsoft/winget-pkgs#403950; AUR `downloader-bin` 2.2.0; Snap published.
- **CI fix during the release:** the `deb` job first failed at the `github-pages` environment gate —
  it only allowed the `main` branch but a release runs on the tag `refs/tags/v2.2.0`, which blocked the
  whole job (including the `.deb` attach). Fixed by adding a `v*` **tag** deployment-branch policy to
  the `github-pages` environment (`POST .../environments/github-pages/deployment-branch-policies`,
  `{name:"v*",type:"tag"}`) — a one-time repo-config change that also unblocks every future tag release;
  no workflow edit needed. Re-ran the `deb` job → `.deb` attached + Pages deployed.
