# winget manifest (mirror of the published package)

These files mirror what is published for Downloader Desktop in the Windows Package Manager, so users can run:

```powershell
winget install bezzad.Downloader --source winget
```

`--source winget` restricts the search to the community repository. Without it, winget also queries the
`msstore` source, and a machine that can't reach `msstore` (corporate TLS proxy, VPN, blocked CRL/OCSP)
gets an SSL error plus a "specify one of them using --source" prompt instead of an install.

Keep these files in sync with `manifests/b/bezzad/Downloader/<version>/` upstream — `scripts/release.sh`
(`submit_winget`) bumps them and opens the winget-pkgs PR on every release.

## How to publish a release
winget packages live in the community repo [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs);
you submit a PR there (you can't self-host). The easiest path:

1. Build/download the release `Downloader-win-x64.zip`.
2. Install the helper: `winget install wingetcreate`.
3. Run:
   ```powershell
   wingetcreate update bezzad.Downloader `
     --version 1.0.0 `
     --urls https://github.com/bezzad/Downloader.Desktop/releases/download/v1.0.0/Downloader-win-x64.zip `
     --submit
   ```
   `wingetcreate` fills in the SHA256, validates the manifest, and opens the PR to winget-pkgs for you.

The three YAML files below are the manifest shape (Identifier `bezzad.Downloader`). `wingetcreate`
generates/updates these automatically, so you normally don't edit them by hand — they're here for
reference and for the very first submission.
