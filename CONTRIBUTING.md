# Contributing / building from source

Developer docs for building, packaging and releasing **Downloader Desktop**. End users don't need any
of this — see the [README](README.md) for install instructions.

## Prerequisites
- **.NET 10 SDK** — https://dotnet.microsoft.com/download (verify with `dotnet --version`)
- Git

## Get the source
```shell
git clone https://github.com/bezzad/Downloader.Desktop.git
cd Downloader.Desktop/src
```
All commands below run from the `src/` folder (where `Downloader.Desktop.sln` lives).

## Run & test
```shell
dotnet restore
dotnet build
dotnet run  --project Downloader.Desktop/Downloader.Desktop.csproj
dotnet test
```

Platform notes:
- **Linux:** needs a desktop session (X11/Wayland). Running from an IDE debugger (e.g. Rider) can group the taskbar entry under the IDE host — run the built binary directly for the real taskbar icon.
- **Windows:** an unsigned build may trigger SmartScreen — choose *More info → Run anyway* (sign builds for distribution).

## Publish a self-contained build
```shell
# one command, outputs to dist/ (self-contained single file, no end-user dependencies)
./scripts/publish.sh linux-x64 win-x64 osx-arm64 osx-x64
```
Or per RID:
```shell
dotnet publish Downloader.Desktop/Downloader.Desktop.csproj -c Release -r linux-x64 \
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/linux-x64
```
Common RIDs: `linux-x64`, `win-x64`, `osx-x64`, `osx-arm64`.

## Releasing a new version
The version is automatic: bump `VersionPrefix` (major.minor) in `Downloader.Desktop.csproj` when you want;
build/revision come from the build time. To cut a release:
```shell
git checkout main && git pull
dotnet test
git tag v1.0.0          # match major.minor to VersionPrefix
git push origin v1.0.0  # triggers .github/workflows/release.yml
```
`release.yml` builds self-contained executables for `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`,
creates the GitHub Release for the tag, and attaches the archives. `dotnet-desktop.yml` runs the test
suite on every push/PR. To re-run a release, delete the tag (`git tag -d v1.0.0 && git push origin :refs/tags/v1.0.0`)
and the GitHub Release, then re-tag.

## Package listings
- **winget:** templates + steps in [`packaging/winget/`](packaging/winget/) (submit via `wingetcreate` to microsoft/winget-pkgs).
- **Homebrew:** cask in [`Casks/downloader.rb`](Casks/downloader.rb) — publish via a `homebrew-tap` repo.
- **Linux installer:** [`scripts/install.sh`](scripts/install.sh) (curl | bash) installs the latest release + a `.desktop` entry and icon.

## macOS .app bundle + signing
```text
Downloader.app/Contents/{Info.plist, MacOS/Downloader, Resources/downloader.icns}
```
```shell
mkdir -p "publish/osx-arm64/Downloader.app/Contents/MacOS" "publish/osx-arm64/Downloader.app/Contents/Resources"
dotnet publish -r osx-arm64 -c Release --self-contained true -p:PublishSingleFile=true \
  -o "publish/osx-arm64/Downloader.app/Contents/MacOS/"
cp Downloader.Desktop/Assets/Info.plist        "publish/osx-arm64/Downloader.app/Contents/"
cp Downloader.Desktop/Assets/downloader.icns   "publish/osx-arm64/Downloader.app/Contents/Resources/"
# Distribute outside the App Store: sign with a Developer ID certificate
codesign --force --options runtime --sign "Developer ID Application: Behzad Khosravifar (XXXX)" \
  "publish/osx-arm64/Downloader.app"
```
[Avalonia macOS packaging guide](https://avaloniaui.net/blog/the-definitive-guide-to-building-and-deploying-avalonia-applications-for-macos)

## Architecture
See [`CLAUDE.md`](CLAUDE.md) and the project skill in `.claude/skills/downloader-desktop/` for the full
architecture, conventions and per-feature notes.
