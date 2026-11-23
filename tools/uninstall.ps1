<#
.SYNOPSIS
    Removes the per-user NexaDock installation created by tools\install.ps1.

.DESCRIPTION
    - Closes NexaDock cleanly (restores the Windows taskbar if NexaDock replaced it).
    - Removes the Start menu shortcut, the "start with Windows" entry and the program folder.
    - Keeps your settings, to-dos and notes in %LOCALAPPDATA%\NexaDock unless -RemoveData is given.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\uninstall.ps1
#>
param(
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
$target = Join-Path $env:LOCALAPPDATA 'Programs\NexaDock'
$exe = Join-Path $target 'NexaDock.exe'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'NexaDock.lnk'

if (Get-Process -Name 'NexaDock' -ErrorAction SilentlyContinue) {
    Write-Host 'Closing NexaDock...'
    if (Test-Path $exe) { Start-Process -FilePath $exe -ArgumentList '--exit' -Wait }
    for ($i = 0; $i -lt 50 -and (Get-Process -Name 'NexaDock' -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 200 }
    if (Get-Process -Name 'NexaDock' -ErrorAction SilentlyContinue) {
        throw 'NexaDock did not close. Exit it from the tray icon and run this again.'
    }
}

# Remove the startup entry only if it points at this installation.
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$value = (Get-ItemProperty -Path $run -Name 'NexaDock' -ErrorAction SilentlyContinue).NexaDock
if ($value -and $value -like "*$target*") {
    Remove-ItemProperty -Path $run -Name 'NexaDock'
    Write-Host 'Removed the start-with-Windows entry.'
}

if (Test-Path $shortcut) { Remove-Item -Force $shortcut; Write-Host 'Removed the Start menu shortcut.' }
if (Test-Path $target) { Remove-Item -Recurse -Force $target; Write-Host "Removed $target" }

if ($RemoveData) {
    $data = Join-Path $env:LOCALAPPDATA 'NexaDock'
    if (Test-Path $data) { Remove-Item -Recurse -Force $data; Write-Host "Removed $data" }
} else {
    Write-Host 'Settings, to-dos and notes were kept in %LOCALAPPDATA%\NexaDock (use -RemoveData to delete them).'
}
