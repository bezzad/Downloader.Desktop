# Homebrew cask template for Downloader Desktop (macOS).
# Before each release, update `version` and the two `sha256` values (shasum -a 256 <archive>).
# The release archive contains a proper "Downloader.app" bundle (Spotlight-visible, launches detached).
cask "downloader" do
  version "2.2.1"

  on_arm do
    sha256 "d1095535d100cee989457ceccf0a246af15ec0dff3e20229de02f6950e269d88"
    url "https://github.com/bezzad/Downloader.Desktop/releases/download/v#{version}/Downloader-osx-arm64.tar.gz"
  end
  on_intel do
    sha256 "dd2fdc680c6d5303bd239f88561d20d02908aa98f47ad955e51d3bfb3bad55fc"
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
