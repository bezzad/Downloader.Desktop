# Homebrew cask template for Downloader Desktop (macOS).
# Before each release, update `version` and the two `sha256` values (shasum -a 256 <archive>).
# The release archive contains a proper "Downloader.app" bundle (Spotlight-visible, launches detached).
cask "downloader" do
  version "2.1.0"

  on_arm do
    sha256 "1710eeef11cba187e59a3da9604d89613b18217a263d4ed9f205edd33fabe15c"
    url "https://github.com/bezzad/Downloader.Desktop/releases/download/v#{version}/Downloader-osx-arm64.tar.gz"
  end
  on_intel do
    sha256 "bc5aacd52b2e7d7c599bead61676a9da8e1808d4a0efdfbabf86422906a84124"
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
