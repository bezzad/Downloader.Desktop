# Downloader Desktop — Windows installer.
# Downloads the latest self-contained release, installs it to %LOCALAPPDATA%\Programs\Downloader,
# adds a Start-menu shortcut, and puts the app folder on your user PATH.
#
#   iex (irm https://raw.githubusercontent.com/bezzad/Downloader.Desktop/main/scripts/install.ps1)
#
# Uninstall: delete %LOCALAPPDATA%\Programs\Downloader, the Start-menu shortcut, and the PATH entry.
$ErrorActionPreference = "Stop"

$Repo   = "bezzad/Downloader.Desktop"
$Asset  = "Downloader-win-x64.zip"
$AppDir = Join-Path $env:LOCALAPPDATA "Programs\Downloader"
$Lnk    = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Downloader.lnk"

# Windows PowerShell 5.1 defaults to TLS 1.0 — GitHub requires TLS 1.2+.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

# Resolve via GitHub's "latest/download" redirect, NOT the API (unauthenticated API calls are
# rate-limited to 60/hr per IP; the CDN redirect isn't).
$Url = "https://github.com/$Repo/releases/latest/download/$Asset"
$Tmp = Join-Path ([IO.Path]::GetTempPath()) "downloader-install-$PID.zip"

Write-Host ">> Downloading the latest $Asset ..."
try {
    Invoke-WebRequest -Uri $Url -OutFile $Tmp -UseBasicParsing
} catch {
    Write-Host "Could not download $Asset from the latest release (network error, or it isn't published yet)."
    Write-Host "See https://github.com/$Repo/releases"
    exit 1
}

# A running instance would lock Downloader.exe — close it before replacing the files.
$running = Get-Process -Name "Downloader" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host ">> Closing the running Downloader instance ..."
    $running | Stop-Process
    Start-Sleep -Seconds 2
}

Write-Host ">> Installing to $AppDir ..."
if (Test-Path $AppDir) { Remove-Item $AppDir -Recurse -Force }
New-Item -ItemType Directory -Path $AppDir -Force | Out-Null
Expand-Archive -Path $Tmp -DestinationPath $AppDir -Force
Remove-Item $Tmp -Force

$Exe = Join-Path $AppDir "Downloader.exe"
if (-not (Test-Path $Exe)) {
    # Some archives nest the exe one folder deep — find it and use that folder's contents.
    $found = Get-ChildItem $AppDir -Recurse -Filter "Downloader.exe" | Select-Object -First 1
    if (-not $found) { Write-Host "Downloader.exe not found in the archive."; exit 1 }
    $Exe = $found.FullName
}

Write-Host ">> Creating a Start-menu shortcut ..."
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($Lnk)
$shortcut.TargetPath = $Exe
$shortcut.WorkingDirectory = Split-Path $Exe
$shortcut.Description = "Fast multi-connection download manager"
$shortcut.Save()

# Put the app folder on the user PATH so `Downloader` works from a terminal.
$exeDir = Split-Path $Exe
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if (($userPath -split ";") -notcontains $exeDir) {
    [Environment]::SetEnvironmentVariable("Path", "$userPath;$exeDir", "User")
    Write-Host ">> Added $exeDir to your user PATH (open a new terminal to use it)."
}

Write-Host ""
Write-Host ">> Installed. Launch Downloader from the Start menu, or run: Downloader"
