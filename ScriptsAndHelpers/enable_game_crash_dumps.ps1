<#
.SYNOPSIS
Makes Windows save a crash dump when Clair Obscur: Expedition 33 crashes.

.DESCRIPTION
The game is a shipping build: it writes no log of its own, and its crash reporter sends reports online instead of
keeping them. With this turned on, Windows Error Reporting saves a small dump (a few MB) of every game crash into
%LOCALAPPDATA%\CrashDumps\Expedition33. The dump shows where the game crashed, which helps to find the file of the
mod that causes it.

Run it in PowerShell as administrator (Windows Error Reporting settings are machine-wide):
    powershell -ExecutionPolicy Bypass -File enable_game_crash_dumps.ps1
Turn it off again with -Disable.
#>
param([switch]$Disable)

$ErrorActionPreference = "Stop"
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "Run this script in PowerShell started as administrator." -ForegroundColor Red
    exit 1
}

# Steam and Game Pass executables of the game
$executables = "SandFall-Win64-Shipping.exe", "SandFall-WinGDK-Shipping.exe"
$localDumps = "HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps"
$dumpFolder = "%LOCALAPPDATA%\CrashDumps\Expedition33"

foreach ($exe in $executables) {
    $key = Join-Path $localDumps $exe
    if ($Disable) {
        if (Test-Path $key) { Remove-Item $key -Recurse }
        continue
    }
    New-Item -Path $key -Force | Out-Null
    New-ItemProperty -Path $key -Name DumpFolder -PropertyType ExpandString -Value $dumpFolder -Force | Out-Null
    New-ItemProperty -Path $key -Name DumpType -PropertyType DWord -Value 1 -Force | Out-Null   # 1 = mini dump
    New-ItemProperty -Path $key -Name DumpCount -PropertyType DWord -Value 5 -Force | Out-Null
}

if ($Disable) {
    Write-Host "Crash dumps for the game are turned off."
} else {
    Write-Host "Crash dumps for the game are turned on."
    Write-Host "After a crash, look in $([Environment]::ExpandEnvironmentVariables($dumpFolder)) for a .dmp file."
}
