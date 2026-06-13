# [Downloader](https://github.com/bezzad/downloader) Desktop   (Coming soon ...)
Fast, cross-platform and reliable multipart downloader with a desktop UI for macOS, Linux and Windows.

Built with [Avalonia UI](https://avaloniaui.net/) on .NET, powered by the [Downloader](https://github.com/bezzad/downloader) engine.

## Prerequisites
- **.NET 10 SDK** — https://dotnet.microsoft.com/download
  Verify with:
  ```shell
  dotnet --version
  ```
- Git (to clone the repository).

Check the installed SDK is 10.x; the app targets `net10.0` (and `net8.0-macos` for the macOS app bundle).

## Get the source
```shell
git clone https://github.com/bezzad/Downloader.Desktop.git
cd Downloader.Desktop/src
```
All commands below are run from the `src/` folder (where `Downloader.Desktop.sln` lives).

## Build & run (all platforms)
The same commands work on Linux, macOS and Windows:
```shell
dotnet restore
dotnet build
dotnet run --project Downloader.Desktop/Downloader.Desktop.csproj
```
The config (settings, download list, queues, schedules) is stored at:
- **Linux:** `~/.config/Downloader/config.json`
- **macOS:** `~/Library/Application Support/Downloader/config.json`
- **Windows:** `%APPDATA%\Downloader\config.json`

### Platform notes
- **Linux:** needs an X11 or Wayland session (a desktop). On a headless server you would need a virtual display (e.g. `xvfb`). When running from an IDE debugger (e.g. Rider), the taskbar entry/icon may be grouped under the IDE host — run the built binary directly for the real taskbar icon.
- **macOS:** first run may prompt for network permission. For a distributable `.app` bundle see the section below.
- **Windows:** if SmartScreen warns on an unsigned build, choose *More info → Run anyway* (use a signed build for distribution).

## Publish a self-contained build
Produces a standalone build (no .NET install required on the target machine). Pick the runtime identifier (RID) for your OS/arch:

```shell
# Linux x64
dotnet publish Downloader.Desktop/Downloader.Desktop.csproj -c Release -r linux-x64 --self-contained true -o publish/linux-x64

# Windows x64
dotnet publish Downloader.Desktop/Downloader.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64

# macOS (Apple Silicon / Intel)
dotnet publish Downloader.Desktop/Downloader.Desktop.csproj -c Release -r osx-arm64 --self-contained true -o publish/osx-arm64
dotnet publish Downloader.Desktop/Downloader.Desktop.csproj -c Release -r osx-x64   --self-contained true -o publish/osx-x64
```
Common RIDs: `linux-x64`, `linux-arm64`, `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`.
Add `-p:PublishSingleFile=true` for a single executable, and `-p:PublishTrimmed=true` to reduce size (test after trimming).

## Deploy on macOS (.app bundle)
A typical `.app` bundle has the following structure:

```text
Downloader.app/
  Contents/
    Info.plist
    MacOS/
      Downloader (executable)
    Resources/
      Assets.car
      downloader.icns
```

```shell
mkdir -p "Downloader.Desktop/bin/publish/osx-arm64/Downloader.app/Contents/MacOS" "Downloader.Desktop/bin/publish/osx-arm64/Downloader.app/Contents/Resources"

dotnet publish -r osx-arm64 -c Release --self-contained true -p:DebugType=None -p:DebugSymbols=false -p:PublishSingleFile=true -p:PublishTrimmed=true -p:TrimMode=link -o "Downloader.Desktop/bin/publish/osx-arm64/Downloader.app/Contents/MacOS/"

cp "Downloader.Desktop/Assets/Info.plist" "Downloader.Desktop/bin/publish/osx-arm64/Downloader.app/Contents/"

cp "Downloader.Desktop/Assets/downloader.icns" "Downloader.Desktop/bin/publish/osx-arm64/Downloader.app/Contents/Resources/"
```

### Code Signing
Code signing is a crucial security feature in macOS that verifies the integrity and origin of your application.

*Obtaining a Developer ID Certificate*

To distribute your application outside the Mac App Store, you need a Developer ID Certificate from Apple. Obtain this through your Apple Developer account.
Signing the Application

Use the codesign tool to sign your application:

`codesign --force --options runtime --sign "Developer ID Application: Behzad Khosravifar (1234)" "Downloader.Desktop/bin/publish/osx-arm64/Downloader.app"`

[Reference](https://avaloniaui.net/blog/the-definitive-guide-to-building-and-deploying-avalonia-applications-for-macos)
