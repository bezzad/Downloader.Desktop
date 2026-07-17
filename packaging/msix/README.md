# Windows MSIX packaging

Packages Downloader as an `.msix` for Windows. Two use cases:

1. **Sideload / testing now** — a self-signed `.msix` you can install locally. No account needed.
2. **Microsoft Store** — the same package, submitted through Partner Center. **Author-gated** (needs a
   paid Partner Center account + a reserved app identity); not automated here.

## Build

```powershell
# from the repo root, on Windows with the Windows 10/11 SDK installed:
./scripts/build-msix.ps1 -Version 2.1.0
```

Produces `dist/msix/Downloader_2.1.0.0_x64.msix`, a self-signed cert `downloader-selfsign.cer`, and the
`.pfx` used to sign. In CI, the `msix` job (`.github/workflows/release.yml`) builds this on every `v*` tag
and uploads it as the `Downloader-msix` workflow artifact.

## Sideload it (test machine)

The self-signed cert must be trusted first (one-time, admin):

```powershell
Import-Certificate -FilePath downloader-selfsign.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage -Path Downloader_2.1.0.0_x64.msix
```

Then launch **Downloader** from the Start menu. Remove with `Get-AppxPackage *Downloader* | Remove-AppxPackage`.

## Microsoft Store submission (author, once Partner Center exists)

1. **Reserve the app name** in [Partner Center](https://partner.microsoft.com/dashboard) → Apps and games →
   New product → MSIX/PWA app. Note the **Package identity** it assigns:
   - `Package/Identity/@Name` (e.g. `12345bezzad.Downloader`)
   - `Package/Identity/@Publisher` (e.g. `CN=ABCD1234-...`)
   - `PublisherDisplayName`
2. **Edit `packaging/msix/AppxManifest.xml`** — replace `Name`, `Publisher`, and `PublisherDisplayName`
   with those exact values. (The committed values `bezzad.Downloader` / `CN=bezzad` are for self-signed
   sideloading only and will be rejected by the Store.)
3. **Build unsigned** for submission — the Store re-signs with the Store identity, so do NOT self-sign:
   ```powershell
   ./scripts/build-msix.ps1 -Version <ver> -SelfSign:$false
   ```
4. In Partner Center, create a submission, **upload the `.msix`**, fill listing/age-rating/pricing, submit.
   Certification then takes a few hours to a couple of days.

Nothing here is submitted automatically — steps 1–4 are manual and require the author's account.
