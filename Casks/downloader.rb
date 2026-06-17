# Homebrew cask template for Downloader Desktop (macOS & Linux via Homebrew).
#
# Publish via a tap repo named "homebrew-tap" under your account, e.g.:
#   1. Create github.com/bezzad/homebrew-tap
#   2. Put this file at Casks/downloader.rb in it
#   3. Users install with:
#        brew tap bezzad/tap
#        brew install --cask downloader
#
# Before each release, update `version` and the two `sha256` values (shasum -a 256 <archive>).
cask "downloader" do
  version "1.0.0"

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

  # The release archive contains the self-contained "Downloader" binary.
  binary "Downloader"

  caveats <<~EOS
    Downloader is not notarized yet. On first launch macOS may block it:
    right-click the app and choose Open, or run:
      xattr -dr com.apple.quarantine "$(brew --prefix)/bin/Downloader"
  EOS
end
