# [Downloader](https://github.com/bezzad/downloader) Desktop
Fast, cross-platform and reliable multipart downloader with desktop UI for MacOS, Linux and Windows


### Deploy on MacOS
> dotnet publish -r osx-arm64 -c Release --self-contained true -p:DebugType=None -p:DebugSymbols=false -p:PublishSingleFile=true -p:PublishTrimmed=true -p:TrimMode=link -o "bin/Publish/osx-arm64/"