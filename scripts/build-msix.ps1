<#
.SYNOPSIS
  Builds a Windows MSIX package for Downloader from a win-x64 self-contained publish.

.DESCRIPTION
  Wraps the published app with the AppxManifest in packaging/msix, packs it with makeappx, and (for
  sideload/testing) signs it with a self-signed certificate whose subject matches the manifest Publisher
  (CN=bezzad). For a Microsoft Store submission you do NOT sign — you upload the unsigned .msix to Partner
  Center, which signs it with the Store identity (see packaging/msix/README.md).

.PARAMETER Version
  3- or 4-part version (e.g. 2.1.0). A 3-part value is padded to x.y.z.0 for the package Identity.

.PARAMETER PublishDir
  An existing win-x64 publish dir (must contain Downloader.exe). If omitted, the script publishes one.

.PARAMETER SelfSign
  Create/reuse a self-signed cert (CN=bezzad) and sign the package for sideloading. Default: on.
  Requires the Windows SDK (makeappx.exe, signtool.exe) on PATH or under Program Files.

.EXAMPLE
  ./scripts/build-msix.ps1 -Version 2.1.0
#>
[CmdletBinding()]
param(
  [string]$Version = "0.0.0",
  [string]$PublishDir = "",
  [switch]$SelfSign = $true
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# Normalize to a 4-part version for the MSIX Identity.
$v = $Version.TrimStart("v")
$parts = $v.Split(".")
while ($parts.Count -lt 4) { $parts += "0" }
$pkgVersion = ($parts[0..3] -join ".")

# 1) Publish win-x64 if no dir was supplied.
if (-not $PublishDir) {
  Write-Host ">> Publishing win-x64 ..."
  $PublishDir = "dist/win-x64"
  Remove-Item -Recurse -Force $PublishDir -ErrorAction SilentlyContinue
  dotnet publish "src/Downloader.Desktop/Downloader.Desktop.csproj" -c Release -r win-x64 `
    --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $PublishDir
  if (Test-Path "$PublishDir/Downloader.Desktop.exe") {
    Move-Item -Force "$PublishDir/Downloader.Desktop.exe" "$PublishDir/Downloader.exe"
  }
}
if (-not (Test-Path "$PublishDir/Downloader.exe")) { throw "Downloader.exe not found in $PublishDir" }

# 2) Stage the package layout: app payload + manifest (version-substituted) + assets.
$stage = "dist/msix/stage"
Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item -Recurse -Force "$PublishDir/*" $stage
(Get-Content "packaging/msix/AppxManifest.xml") -replace "\{VERSION\}", $pkgVersion |
  Set-Content "$stage/AppxManifest.xml" -Encoding UTF8
Copy-Item -Recurse -Force "packaging/msix/Assets" "$stage/Assets"

# 3) Locate the Windows SDK tools.
function Find-SdkTool([string]$name) {
  $cmd = Get-Command $name -ErrorAction SilentlyContinue
  if ($cmd) { return $cmd.Source }
  $hit = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter $name `
           -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match "x64" } |
           Sort-Object FullName -Descending | Select-Object -First 1
  if ($hit) { return $hit.FullName }
  throw "$name not found — install the Windows 10/11 SDK."
}
$makeappx = Find-SdkTool "makeappx.exe"

# 4) Pack.
New-Item -ItemType Directory -Force -Path "dist/msix" | Out-Null
$msix = "dist/msix/Downloader_${pkgVersion}_x64.msix"
& $makeappx pack /o /d $stage /p $msix
if ($LASTEXITCODE -ne 0) { throw "makeappx failed ($LASTEXITCODE)" }
Write-Host ">> Packed $msix"

# 5) Self-sign for sideload testing (Store submission skips this).
if ($SelfSign) {
  $signtool = Find-SdkTool "signtool.exe"
  $subject = "CN=bezzad"
  $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject } |
            Select-Object -First 1
  if (-not $cert) {
    Write-Host ">> Creating a self-signed cert ($subject) ..."
    $cert = New-SelfSignedCertificate -Type Custom -Subject $subject -KeyUsage DigitalSignature `
      -FriendlyName "Downloader sideload" -CertStoreLocation "Cert:\CurrentUser\My" `
      -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
  }
  $pfx = "dist/msix/downloader-selfsign.pfx"
  $pw = ConvertTo-SecureString -String "downloader" -Force -AsPlainText
  Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $pw | Out-Null
  & $signtool sign /fd SHA256 /a /f $pfx /p "downloader" $msix
  if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE)" }
  # Export the public cert so testers can trust it before Add-AppxPackage.
  Export-Certificate -Cert $cert -FilePath "dist/msix/downloader-selfsign.cer" | Out-Null
  Write-Host ">> Signed $msix (self-signed). Trust dist/msix/downloader-selfsign.cer to sideload."
}

Write-Host ">> Done: $msix"
