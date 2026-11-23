<#
.SYNOPSIS
    Builds NexaDock in Release mode and installs it for the current user.

.DESCRIPTION
    - Publishes to %LOCALAPPDATA%\Programs\NexaDock (framework-dependent, needs the .NET 10 Desktop Runtime).
    - Closes a running NexaDock cleanly first (NexaDock.exe --exit), so a replaced taskbar is restored.
    - Creates a Start menu shortcut "NexaDock".
    - Starts NexaDock unless -NoStart is given.
    Settings in %LOCALAPPDATA%\NexaDock are kept. Nothing outside your user profile is touched.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\install.ps1
#>
param(
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $env:LOCALAPPDATA 'Programs\NexaDock'
$exe = Join-Path $target 'NexaDock.exe'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'NexaDock.lnk'

function Stop-NexaDock {
    $running = Get-Process -Name 'NexaDock' -ErrorAction SilentlyContinue
    if (-not $running) { return }

    Write-Host 'Closing the running NexaDock...'
    $path = $running | Select-Object -First 1 -ExpandProperty Path -ErrorAction SilentlyContinue
    if ($path -and (Test-Path $path)) {
        Start-Process -FilePath $path -ArgumentList '--exit' -Wait
    }

    for ($i = 0; $i -lt 50 -and (Get-Process -Name 'NexaDock' -ErrorAction SilentlyContinue); $i++) {
        Start-Sleep -Milliseconds 200
    }

    if (Get-Process -Name 'NexaDock' -ErrorAction SilentlyContinue) {
        throw 'NexaDock did not close. Exit it from the tray icon and run the installer again.'
    }
}

Write-Host "Building NexaDock (Release)..."
dotnet publish (Join-Path $root 'src\NexaDock.App\NexaDock.App.csproj') -c Release -r win-x64 --self-contained false -o (Join-Path $root 'artifacts\publish') -nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

Stop-NexaDock

Write-Host "Installing to $target"
if (Test-Path $target) { Remove-Item -Recurse -Force $target }
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item -Recurse -Force (Join-Path $root 'artifacts\publish\*') $target

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.WorkingDirectory = $target
$link.Description = 'NexaDock - smart Liquid Glass bar'
$link.Save()
Write-Host "Start menu shortcut: $shortcut"

if (-not $NoStart) {
    Start-Process -FilePath $exe
    Write-Host 'NexaDock started.'
}

Write-Host 'Done. To start with Windows, turn on Settings > Position & behavior > Start with Windows.'
