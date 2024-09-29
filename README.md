# [Downloader](https://github.com/bezzad/downloader) Desktop
Fast, cross-platform and reliable multipart downloader with desktop UI for MacOS, Linux and Windows


## Deploy on MacOS
A typical .app bundle has the following structure:

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
$ mkdir -p "Downloader.Desktop/bin/publish/osx-arm64/Downloader.app/Contents/MacOS" "Downloader.Desktop/bin/publish/osx-arm64/Downloader.app/Contents/Resources"`

$ dotnet publish -r osx-arm64 -c Release --self-contained true -p:DebugType=None -p:DebugSymbols=false -p:PublishSingleFile=true -p:PublishTrimmed=true -p:TrimMode=link -o "Downloader.Desktop/bin/publish/osx-arm64/Downloader.app/Contents/MacOS/"`

$ cp "Downloader.Desktop/Assets/Info.plist" "Downloader.Desktop/bin/publish/osx-arm64/Downloader.app/Contents/"`

$ cp "Downloader.Desktop/Assets/downloader.icns" "Downloader.Desktop/bin/publish/osx-arm64/Downloader.app/Contents/Resources/"`
```

### Code Signing
Code signing is a crucial security feature in macOS that verifies the integrity and origin of your application.

*Obtaining a Developer ID Certificate*

To distribute your application outside the Mac App Store, you need a Developer ID Certificate from Apple. Obtain this through your Apple Developer account.
Signing the Application

Use the codesign tool to sign your application:

`codesign --force --options runtime --sign "Developer ID Application: Behzad Khosravifar (1234)" "Downloader.Desktop/bin/publish/osx-arm64/Downloader.app"`



[Reference](https://avaloniaui.net/blog/the-definitive-guide-to-building-and-deploying-avalonia-applications-for-macos) 
