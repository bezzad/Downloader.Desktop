# Homebrew cask template for Downloader Desktop (macOS).
# Before each release, update `version` and the two `sha256` values (shasum -a 256 <archive>).
# The release archive contains a proper "Downloader.app" bundle (Spotlight-visible, launches detached).
cask "downloader" do
  version "1.1.2"

  on_arm do
    sha256 "8393de4afd7f535cac4f0c0661451bfce14ac05dba25a74b7b9a1f26acbddfc8"
    url "https://github.com/bezzad/Downloader.Desktop/releases/download/v#{version}/Downloader-osx-arm64.tar.gz"
  end
  on_intel do
    sha256 "f0db5309badd9334b1591a5a8cab7787a1bf17a8ad3d86a0ba372d8f1327e086"
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
