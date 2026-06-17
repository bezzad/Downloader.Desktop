# winget manifest (template)

These files publish Downloader Desktop to the Windows Package Manager so users can run:

```powershell
winget install bezzad.Downloader
```

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
