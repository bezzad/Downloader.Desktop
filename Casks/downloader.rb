# Homebrew cask template for Downloader Desktop (macOS).
# Before each release, update `version` and the two `sha256` values (shasum -a 256 <archive>).
# The release archive contains a proper "Downloader.app" bundle (Spotlight-visible, launches detached).
cask "downloader" do
  version "2.3.0"

  on_arm do
    sha256 "49ce6bc8cc4c855ab9afdeaf0a085ab0e710f180b8a5f2d5c50cbeac8e52ec01"
    url "https://github.com/bezzad/Downloader.Desktop/releases/download/v#{version}/Downloader-osx-arm64.tar.gz"
  end
  on_intel do
    sha256 "c93d8d2f47d663cb1fd231b2ea14719cd0c8aa14be55f1e05c985a5ef9e39118"
    url "https://github.com/bezzad/Downloader.Desktop/releases/download/v#{version}/Downloader-osx-x64.tar.gz"
  end

  name "Downloader"
  desc "Fast multi-connection download manager"
  homepage "https://github.com/bezzad/Downloader.Desktop"

  app "Downloader.app"

  # Downloader is not notarized yet, so Homebrew quarantines it and macOS blocks the first
  # launch ("unidentified developer"). Strip the quarantine flag on install so the user does
  # not have to run `xattr -dr com.apple.quarantine ...` by hand after every install/upgrade.
  postflight do
    system_command "/usr/bin/xattr",
                   args: ["-dr", "com.apple.quarantine", "#{appdir}/Downloader.app"]
  end

  caveats <<~EOS
    Downloader is not notarized yet. The quarantine flag is removed automatically on install,
    so it should launch normally. If macOS still blocks it, right-click Downloader in
    Applications and choose Open, or run:
      xattr -dr com.apple.quarantine "#{appdir}/Downloader.app"
  EOS
end
