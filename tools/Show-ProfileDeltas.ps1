<#
.SYNOPSIS
    Shows how a live settings file differs from the plugin's bare defaults, ready to be pasted
    into DefaultProfiles.cs.

.DESCRIPTION
    The profiles a fresh install starts with live in src\AB9ActiveShifter\DefaultProfiles.cs,
    written as differences from a bare ShifterSettings so the tuning reads as tuning. Retuning
    happens on the rig, in SimHub, and lands in the saved settings file - so refreshing what is
    shipped means turning that file back into a list of differences.

    That is what this does. It builds a bare ShifterSettings by reflection over the built DLL,
    reads the saved settings JSON, and prints the differences per profile in C# assignment form.

    Comparison is by the property's own type, so an enum stored as 0 does not read as different
    from H7R. Derived adapters (PatternIndex, MouthShapeIndex, ThrowFromCentre) are skipped: they are
    written by the dials they derive from, and assigning both invites an ordering bug.

    SimHub rewrites its settings file on exit, so stop SimHub before reading it.

.EXAMPLE
    powershell -File tools\Show-ProfileDeltas.ps1

.EXAMPLE
    powershell -File tools\Show-ProfileDeltas.ps1 -Settings C:\some\other\GeneralSettings.json
#>
[CmdletBinding()]
param(
    [string]$Settings = 'C:\Program Files (x86)\SimHub\PluginsData\Common\AB9ShifterPlugin.GeneralSettings.json',
    [string]$Dll
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Definition)

if (-not $Dll) { $Dll = Join-Path $root 'src\AB9ActiveShifter\bin\Debug\AB9ActiveShifter.dll' }
if (-not (Test-Path $Dll)) { throw "Plugin DLL not found: $Dll. Run dotnet build first." }
if (-not (Test-Path $Settings)) { throw "Settings file not found: $Settings" }

# Derived from the dials they follow; assigning them as well would depend on ordering.
$derived = 'PatternIndex', 'MouthShapeIndex', 'ThrowFromCentre'

$asm = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $Dll))
$type = $asm.GetType('AB9ActiveShifter.ShifterSettings')
$defaults = [System.Activator]::CreateInstance($type)
$props = $type.GetProperties() | Where-Object { $_.CanRead -and $_.CanWrite } | Sort-Object Name

$json = Get-Content $Settings -Raw | ConvertFrom-Json

Write-Host "Settings : $Settings"
Write-Host "Active   : $($json.ActiveProfile)`n"

foreach ($profile in $json.Profiles) {
    Write-Host "// === $($profile.Name) ===" -ForegroundColor Cyan
    $saved = $profile.Settings
    $savedNames = $saved.PSObject.Properties.Name

    $count = 0
    foreach ($p in $props) {
        if ($derived -contains $p.Name) { continue }
        if ($savedNames -notcontains $p.Name) { continue }

        $def = $p.GetValue($defaults, $null)
        $val = $saved.($p.Name)

        $same = $false
        $literal = "$val"
        if ($p.PropertyType.IsEnum) {
            $same = ([int]$def -eq [int]$val)
            $literal = "{0}.{1}" -f $p.PropertyType.Name, [Enum]::GetName($p.PropertyType, [int]$val)
        }
        elseif ($p.PropertyType -eq [double]) {
            $same = ([math]::Abs([double]$def - [double]$val) -lt 1e-9)
            $literal = ([double]$val).ToString([System.Globalization.CultureInfo]::InvariantCulture)
        }
        elseif ($p.PropertyType -eq [bool]) {
            $same = ([bool]$def -eq [bool]$val)
            $literal = if ([bool]$val) { 'true' } else { 'false' }
        }
        elseif ($p.PropertyType -eq [string]) {
            $same = ("$def" -eq "$val")
            $literal = '"' + "$val" + '"'
        }
        else {
            $same = ("$def" -eq "$val")
        }

        if (-not $same) {
            "    s.{0} = {1};" -f $p.Name, $literal
            $count++
        }
    }
    Write-Host "// $count differences from bare defaults`n"
}

Write-Host 'Enabled and PolarityConfirmed must stay at their defaults in DefaultProfiles.cs -' -ForegroundColor Yellow
Write-Host 'forces ship off, and the 10% cap guards a base nobody has measured. Tests enforce both.' -ForegroundColor Yellow
