<#
.SYNOPSIS
    Builds AB9 Active Shifter and installs it into SimHub.

.DESCRIPTION
    SimHub keeps the plugin DLL locked while it is running and its install folder needs
    administrator rights, so this script stops SimHub, copies the build output with
    elevation, and optionally starts SimHub again. On a machine with no saved settings it
    also installs the shipped presets from presets\.

.EXAMPLE
    .\install.ps1
    .\install.ps1 -SkipBuild -NoRestart
#>
[CmdletBinding()]
param(
    [string]$SimHubDir = 'C:\Program Files (x86)\SimHub\',
    [string]$Configuration = 'Release',
    [switch]$SkipBuild,
    [switch]$NoRestart
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$project = Join-Path $root 'src\AB9ActiveShifter\AB9ActiveShifter.csproj'
$outputDir = Join-Path $root "src\AB9ActiveShifter\bin\$Configuration"
$dllName = 'AB9ActiveShifter.dll'
$pdbName = 'AB9ActiveShifter.pdb'

if (-not (Test-Path $SimHubDir)) {
    throw "SimHub folder not found: $SimHubDir. Pass -SimHubDir with the correct path."
}

if (-not $SkipBuild) {
    Write-Host "Building $Configuration..." -ForegroundColor Cyan
    & dotnet build $project -c $Configuration -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }
}

$dllPath = Join-Path $outputDir $dllName
if (-not (Test-Path $dllPath)) { throw "Build output not found: $dllPath" }

$simhub = Get-Process -Name 'SimHubWPF' -ErrorAction SilentlyContinue
$wasRunning = $null -ne $simhub
if ($wasRunning) {
    Write-Host 'Stopping SimHub (it locks the plugin DLL)...' -ForegroundColor Yellow
    $simhub | Stop-Process
    # Wait for the file lock to actually clear rather than guessing a sleep duration.
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Process -Name 'SimHubWPF' -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
    }
    if (Get-Process -Name 'SimHubWPF' -ErrorAction SilentlyContinue) {
        throw 'SimHub did not exit; close it manually and re-run.'
    }
}

$files = @($dllPath)
$pdbPath = Join-Path $outputDir $pdbName
if (Test-Path $pdbPath) { $files += $pdbPath }

Write-Host "Copying to $SimHubDir ..." -ForegroundColor Cyan
try {
    Copy-Item -Path $files -Destination $SimHubDir -Force -ErrorAction Stop
    Write-Host 'Copied without elevation.' -ForegroundColor Green
}
catch [System.UnauthorizedAccessException] {
    Write-Host 'Needs administrator rights; requesting elevation...' -ForegroundColor Yellow
    $quoted = ($files | ForEach-Object { "'$_'" }) -join ','
    $command = "Copy-Item -Path $quoted -Destination '$SimHubDir' -Force"
    $process = Start-Process powershell -Verb RunAs -Wait -PassThru `
        -ArgumentList '-NoProfile', '-NonInteractive', '-Command', $command
    if ($process.ExitCode -ne 0) { throw "Elevated copy failed with exit code $($process.ExitCode)." }
    Write-Host 'Copied with elevation.' -ForegroundColor Green
}

# First install only: seed the shipped profiles so the plugin starts from the tuned setup
# rather than bare defaults. Never touches existing saved settings.
$presetSrc = Join-Path $root 'presets\AB9ShifterPlugin.GeneralSettings.json'
$settingsDir = Join-Path $SimHubDir 'PluginsData\Common'
$settingsPath = Join-Path $settingsDir 'AB9ShifterPlugin.GeneralSettings.json'
if ((Test-Path $presetSrc) -and -not (Test-Path $settingsPath)) {
    Write-Host 'No saved settings found; installing the shipped presets...' -ForegroundColor Cyan
    try {
        if (-not (Test-Path $settingsDir)) {
            New-Item -ItemType Directory -Path $settingsDir -Force -ErrorAction Stop | Out-Null
        }
        Copy-Item -Path $presetSrc -Destination $settingsPath -Force -ErrorAction Stop
        Write-Host 'Presets installed. FFB starts disabled, and force is capped until Measure polarity is run.' -ForegroundColor Green
    }
    catch [System.UnauthorizedAccessException] {
        Write-Host 'Needs administrator rights; requesting elevation...' -ForegroundColor Yellow
        $command = "New-Item -ItemType Directory -Path '$settingsDir' -Force | Out-Null; " +
            "Copy-Item -Path '$presetSrc' -Destination '$settingsPath' -Force"
        $process = Start-Process powershell -Verb RunAs -Wait -PassThru `
            -ArgumentList '-NoProfile', '-NonInteractive', '-Command', $command
        if ($process.ExitCode -ne 0) { throw "Elevated preset copy failed with exit code $($process.ExitCode)." }
        Write-Host 'Presets installed with elevation.' -ForegroundColor Green
    }
}

if ($wasRunning -and -not $NoRestart) {
    $exe = Join-Path $SimHubDir 'SimHubWPF.exe'
    if (Test-Path $exe) {
        Write-Host 'Restarting SimHub...' -ForegroundColor Cyan
        Start-Process $exe
    }
}

Write-Host ''
Write-Host 'Installed. In SimHub: Settings -> Plugins -> enable "AB9 Active Shifter".' -ForegroundColor Green
Write-Host 'First run only: SimHub will ask whether to trust the new plugin.' -ForegroundColor Green
