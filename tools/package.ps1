<#
.SYNOPSIS
    Builds the NexaDock release package for GitHub.

.DESCRIPTION
    Publishes Release (win-x64, framework-dependent, needs the .NET 10 Desktop Runtime) and writes:
      dist\NexaDock-win-x64.zip          the app; attach it to the GitHub release
      dist\NexaDock-win-x64.zip.sha256   its SHA-256; attach it too (the updater checks it)
    Tag the release "v<version>" (for example v0.4.0), matching ProductInfo.Version.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\package.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$staging = Join-Path $dist 'publish'
$zip = Join-Path $dist 'NexaDock-win-x64.zip'

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force $staging | Out-Null

dotnet publish (Join-Path $root 'src\NexaDock.App') -c Release -r win-x64 --self-contained false -o $staging --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

# Debug symbols are not needed to run the app.
Get-ChildItem $staging -Filter *.pdb | Remove-Item -Force

Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $staging -Recurse -Force

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  NexaDock-win-x64.zip" -Encoding ascii -NoNewline

$version = (Select-String -Path (Join-Path $root 'src\NexaDock.Core\ProductInfo.cs') -Pattern 'Version = "([^"]+)"').Matches[0].Groups[1].Value
Write-Host ''
Write-Host "Package: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
Write-Host "SHA-256: $hash"
Write-Host "Release tag: v$version"
