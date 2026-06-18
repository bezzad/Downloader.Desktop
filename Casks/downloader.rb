# Homebrew cask template for Downloader Desktop (macOS).
# Before each release, update `version` and the two `sha256` values (shasum -a 256 <archive>).
# The release archive contains a proper "Downloader.app" bundle (Spotlight-visible, launches detached).
cask "downloader" do
  version "1.1.3"

  on_arm do
    sha256 "422bdf171000a2b6557cfa778f2d782664413d1f80f2d9b1902732803db64c51"
    url "https://github.com/bezzad/Downloader.Desktop/releases/download/v#{version}/Downloader-osx-arm64.tar.gz"
  end
  on_intel do
    sha256 "d979b81d1c8a5e06e323d150b8293fffee789826568db0223ed4b1b093899b96"
    url "https://github.com/bezzad/Downloader.Desktop/releases/download/v#{version}/Downloader-osx-x64.tar.gz"
  end

  name "Downloader"
  desc "Fast multi-connection download manager"
  homepage "https://github.com/bezzad/Downloader.Desktop"

  app "Downloader.app"

  caveats <<~EOS
    Downloader is not notarized yet. On first launch macOS may block it:
    right-click Downloader in Applications and choose Open, or run:
      xattr -dr com.apple.quarantine "#{appdir}/Downloader.app"
  EOS
end
