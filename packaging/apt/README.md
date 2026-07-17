# Debian package + APT repository

Ships Downloader as a `.deb` and hosts a **signed APT repo on GitHub Pages** so Debian/Ubuntu users can
`apt install downloader` and get updates through `apt upgrade`.

> A bare `apt install downloader` on a stock machine (no repo added) would need the package to be in the
> official Debian/Ubuntu archive, which requires maintainer sponsorship — out of scope. The one-time
> add-repo step below is the achievable path.

## For users

```bash
curl -fsSL https://bezzad.github.io/Downloader.Desktop/apt/pubkey.gpg \
  | sudo gpg --dearmor -o /usr/share/keyrings/downloader.gpg
echo "deb [signed-by=/usr/share/keyrings/downloader.gpg] https://bezzad.github.io/Downloader.Desktop/apt stable main" \
  | sudo tee /etc/apt/sources.list.d/downloader.list
sudo apt update && sudo apt install downloader
```

Or, without adding the repo, grab the `.deb` from a
[release](https://github.com/bezzad/Downloader.Desktop/releases) and `sudo apt install ./Downloader_*_amd64.deb`.

## How it's built

- `scripts/build-deb.sh` builds `dist/Downloader_<ver>_amd64.deb` from a linux-x64 self-contained publish
  (app under `/opt/downloader`, `/usr/bin/downloader` symlink, `.desktop` entry + hicolor icon).
- `scripts/build-apt-repo.sh <out> <deb>...` produces the signed repo (`dists/stable/{Release,InRelease,Release.gpg}`
  + `pool/…`). Needs only `dpkg-scanpackages` + `gpg`.
- The `deb` job in `.github/workflows/release.yml` runs both on every `v*` tag, attaches the `.deb` to the
  release, and deploys the repo to Pages.

## Signing key

- Public key: `packaging/apt/pubkey.gpg` (committed; served at the repo root and under `/apt/pubkey.gpg`).
- Key id: see `packaging/apt/KEYID`.
- Private key: **not in the repo.** It must be added as the `APT_GPG_PRIVATE_KEY` repository secret
  (the full armored `-----BEGIN PGP PRIVATE KEY BLOCK-----`). Without it, the `deb` job still builds and
  attaches the `.deb` but skips the (unsignable) Pages repo deploy.

## One-time author setup

1. **Settings → Pages → Source = "GitHub Actions".**
2. **Settings → Secrets and variables → Actions → New secret** `APT_GPG_PRIVATE_KEY` = the armored private
   key (generated alongside `pubkey.gpg`; stored out-of-repo when the key was created).
3. Push a `v*` tag — the `deb` job publishes the repo. Verify the URLs above resolve.

To rotate the key: regenerate, replace `pubkey.gpg` + `KEYID`, update the secret.
