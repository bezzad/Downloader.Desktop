# Homebrew cask template for Downloader Desktop (macOS).
# Before each release, update `version` and the two `sha256` values (shasum -a 256 <archive>).
# The release archive contains a proper "Downloader.app" bundle (Spotlight-visible, launches detached).
cask "downloader" do
  version "1.2.0"

  on_arm do
    sha256 "4106036d944bd2c8bf511a7e59ba84b1f23be66a3ce36e537f37be81848349e9"
    url "https://github.com/bezzad/Downloader.Desktop/releases/download/v#{version}/Downloader-osx-arm64.tar.gz"
  end
  on_intel do
    sha256 "aa300445ffc119d2c2540cb4973421565a97a19af5030115775e8ce9bc209594"
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
