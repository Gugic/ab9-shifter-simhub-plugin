<#
.SYNOPSIS
    Proves a stub-built AB9ActiveShifter.dll binds against the real SimHub assemblies.

.DESCRIPTION
    CI has no SimHub install, so it compiles the plugin against the reference stubs in
    build\refs. Those stubs only help if every signature matches SimHub's real one exactly:
    the compiler bakes the difference between a field read and a property call, and between
    one enum constant and another, straight into our IL. A stub that is subtly wrong produces
    a DLL that builds cleanly in CI and throws MissingMethodException on the rig.

    This script closes that gap. It loads the stub-built DLL with the real SimHub assemblies
    on the resolve path and asks the JIT to prepare every method. Preparing a method forces
    the runtime to resolve every token in its body - every external type, method and field -
    without executing any of it. Anything the stubs got wrong fails here.

    Run it on a machine with SimHub installed, after building with -p:UseSimHubStubs=true.
    It must run 32-bit, because the real vJoy wrapper is a mixed-mode x86 assembly.

.EXAMPLE
    pwsh -File tools\Verify-StubBuild.ps1
    pwsh -File tools\Verify-StubBuild.ps1 -Dll artifacts\AB9ActiveShifter.dll
#>
[CmdletBinding()]
param(
    [string]$Dll,
    [string]$SimHubDir = 'C:\Program Files (x86)\SimHub\'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Definition)

# A trailing backslash would escape the closing quote when this relaunches itself 32-bit.
$SimHubDir = $SimHubDir.TrimEnd('\', '/')

if (-not $Dll) { $Dll = Join-Path $root 'artifacts\AB9ActiveShifter.dll' }
if (-not (Test-Path $Dll)) {
    throw "Plugin DLL not found: $Dll. Build it first: dotnet build src\AB9ActiveShifter\AB9ActiveShifter.csproj -c Release -p:UseSimHubStubs=true"
}
if (-not (Test-Path (Join-Path $SimHubDir 'SimHub.Plugins.dll'))) {
    throw "SimHub not found at $SimHubDir. This check needs the real assemblies; it cannot run on CI."
}

# The real vJoy wrapper is mixed-mode x86, so a 64-bit host cannot load it at all.
if ([IntPtr]::Size -ne 4) {
    $ps32 = Join-Path $env:WINDIR 'SysWOW64\WindowsPowerShell\v1.0\powershell.exe'
    if (-not (Test-Path $ps32)) { throw 'Need a 32-bit PowerShell to load the vJoy wrapper.' }
    Write-Host 'Re-launching 32-bit (the vJoy wrapper is x86-only)...' -ForegroundColor Cyan
    & $ps32 -NoProfile -ExecutionPolicy Bypass -File $MyInvocation.MyCommand.Definition -Dll $Dll -SimHubDir $SimHubDir
    exit $LASTEXITCODE
}

$script:SimHub = $SimHubDir
$onResolve = [System.ResolveEventHandler] {
    param($sender, $e)
    $simple = ($e.Name -split ',')[0]
    $candidate = Join-Path $script:SimHub "$simple.dll"
    if (Test-Path $candidate) { return [System.Reflection.Assembly]::LoadFrom($candidate) }
    return $null
}
[System.AppDomain]::CurrentDomain.add_AssemblyResolve($onResolve)

Write-Host "Verifying $Dll" -ForegroundColor Cyan
Write-Host "  against the real assemblies in $SimHubDir`n"

$asm = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $Dll))

# Every assembly our IL names must resolve to something real, and for the SimHub ones that
# must be the install's copy rather than a stub left lying next to the DLL.
$stubNames = 'SimHub.Plugins', 'GameReaderCommon', 'SimHub.Logging', 'vJoyInterfaceWrap'
foreach ($ref in $asm.GetReferencedAssemblies()) {
    $loaded = $null
    try { $loaded = [System.Reflection.Assembly]::Load($ref) } catch { }
    if (-not $loaded) { Write-Host ("  UNRESOLVED  {0}" -f $ref.FullName) -ForegroundColor Red; continue }
    $mark = ''
    if ($stubNames -contains $ref.Name) {
        $fromInstall = $loaded.Location -and $loaded.Location.StartsWith("$SimHubDir\", 'OrdinalIgnoreCase')
        $mark = if ($fromInstall) { 'real' } else { 'NOT THE INSTALL COPY' }
    }
    Write-Host ("  {0,-24} -> {1} {2}" -f $ref.Name, (Split-Path -Leaf $loaded.Location), $mark)
}

$flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly'
$prepared = 0
$skipped = 0
$failures = New-Object System.Collections.ArrayList

foreach ($type in $asm.GetTypes()) {
    if ($type.IsGenericTypeDefinition) { $skipped++; continue }

    $methods = @()
    $methods += $type.GetMethods($flags)
    $methods += $type.GetConstructors($flags)

    foreach ($m in $methods) {
        # Abstract and generic methods have no body to resolve; delegate plumbing is compiler
        # generated and calls nothing external.
        if ($m.IsAbstract -or $m.ContainsGenericParameters) { $skipped++; continue }
        try {
            [System.Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($m.MethodHandle)
            $prepared++
        }
        catch {
            $inner = $_.Exception
            while ($inner.InnerException) { $inner = $inner.InnerException }
            [void]$failures.Add([pscustomobject]@{
                Method = "$($type.FullName).$($m.Name)"
                Error  = $inner.GetType().Name
                Detail = $inner.Message
            })
        }
    }
}

Write-Host ("`n  prepared {0} methods, skipped {1} (abstract or generic)" -f $prepared, $skipped)

if ($failures.Count -eq 0) {
    Write-Host "`nOK - every external type, method and field reference resolved against the real SimHub." -ForegroundColor Green
    exit 0
}

Write-Host "`n$($failures.Count) method(s) failed to bind:" -ForegroundColor Red
foreach ($f in $failures) {
    Write-Host ("  {0}`n      {1}: {2}" -f $f.Method, $f.Error, $f.Detail) -ForegroundColor Red
}
Write-Host "`nA stub signature in build\refs does not match the real assembly. Fix the stub." -ForegroundColor Yellow
exit 1
