# Homebrew cask template for Downloader Desktop (macOS).
# Before each release, update `version` and the two `sha256` values (shasum -a 256 <archive>).
# The release archive contains a proper "Downloader.app" bundle (Spotlight-visible, launches detached).
cask "downloader" do
  version "1.1.1"

  on_arm do
    sha256 "REPLACE_WITH_SHA256_OF_Downloader-osx-arm64.tar.gz"
    url "https://github.com/bezzad/Downloader.Desktop/releases/download/v#{version}/Downloader-osx-arm64.tar.gz"
  end
  on_intel do
    sha256 "REPLACE_WITH_SHA256_OF_Downloader-osx-x64.tar.gz"
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
