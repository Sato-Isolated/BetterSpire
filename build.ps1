[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$destination = Join-Path $root 'dist/BetterSpire2Lite'
$log = Join-Path $root 'build.log'
$transcribing = $false
$exitCode = 0

try {
    Start-Transcript -Path $log -Force | Out-Null
    $transcribing = $true
    Set-Location -LiteralPath $root
    # Remove only our generated deliverables. Never touch the game installation.
    foreach ($file in @('BetterSpire2Lite.dll', 'BetterSpire2Lite.json', 'BUILD-SUCCESS.txt')) {
        $old = Join-Path $destination $file
        if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Force }
    }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'Install Microsoft .NET SDK 9 (not just the runtime), then run build.cmd again.'
    }
    $sdkList = & dotnet --list-sdks
    if ($LASTEXITCODE -ne 0 -or -not ($sdkList -match '^9\.')) {
        throw 'Microsoft .NET SDK 9 is required. Run: dotnet --list-sdks'
    }
    foreach ($reference in @('sts2.dll', 'GodotSharp.dll', '0Harmony.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $root ('references/' + $reference)))) {
            throw ('Missing compilation reference: references/' + $reference)
        }
    }
    Write-Host '1/9 Running the C# simulator and overhead HUD tests...'
    & dotnet run --project (Join-Path $root 'tests/Guardian.Core.Tests.csproj') --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'C# tests failed. No release was produced.' }
    Write-Host '2/9 Running the C# journal tests...'
    & dotnet run --project (Join-Path $root 'tests/Journal.Core.Tests.csproj') --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Journal tests failed. No release was produced.' }
    Write-Host '3/9 Running the C# run damage meter tests...'
    & dotnet run --project (Join-Path $root 'tests/DamageMeter.Core.Tests.csproj') --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Damage meter tests failed. No release was produced.' }
    Write-Host '4/9 Running C# refresh/cache/checkpoint regression tests...'
    & dotnet run --project (Join-Path $root 'tests/Performance.Core.Tests.csproj') --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Performance regression tests failed. No release was produced.' }
    Write-Host '5/9 Running C# HUD dependency, navigation and geometry tests...'
    & dotnet run --project (Join-Path $root 'tests/Hud.Core.Tests.csproj') --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'HUD regression tests failed. No release was produced.' }
    Write-Host '6/9 Running C# poison provenance and async isolation tests...'
    & dotnet run --project (Join-Path $root 'tests/Poison.Core.Tests.csproj') --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Poison attribution tests failed. No release was produced.' }
    Write-Host '7/9 Running source-linked game-boundary tests...'
    & dotnet run --project (Join-Path $root 'tests/GameBoundary.Core.Tests.csproj') --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Game boundary tests failed. No release was produced.' }
    Write-Host '8/9 Building the actual mod against the supplied DLL references...'
    & dotnet build (Join-Path $root 'BetterSpire2Lite.csproj') --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'The mod did not compile. No release was produced.' }
    Write-Host '9/9 Checking the v0.111 metadata contract and patch targets...'
    & dotnet run --project (Join-Path $root 'tests/GameApiCompatibility.Tests.csproj') --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'The v0.111 API compatibility contract failed. No release was produced.' }
    $output = Join-Path $root 'bin/Release/net9.0'
    $dll = Join-Path $output 'BetterSpire2Lite.dll'
    $manifest = Join-Path $output 'BetterSpire2Lite.json'
    if (-not (Test-Path -LiteralPath $dll) -or -not (Test-Path -LiteralPath $manifest)) {
        throw 'Build outputs are missing.'
    }
    $json = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
    if ($json.id -ne 'BetterSpire2Lite' -or -not $json.has_dll -or $json.has_pck) {
        throw 'Unexpected mod manifest. No release was produced.'
    }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath $dll -Destination $destination
    Copy-Item -LiteralPath $manifest -Destination $destination
    @(
        ('Built locally: ' + (Get-Date -Format o)),
        'C# core, overhead, journal, damage meter, performance, HUD, poison, game-boundary and v0.111 API/IL contract tests: passed. Mod compilation: passed.',
        'In-game validation: NOT performed by this script.',
        ('DLL SHA256: ' + (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash)
    ) | Set-Content -LiteralPath (Join-Path $destination 'BUILD-SUCCESS.txt') -Encoding UTF8
    Write-Host ''
    Write-Host ('SUCCESS: ' + $destination)
    Write-Host 'Install ONLY BetterSpire2Lite.dll and BetterSpire2Lite.json. Never copy references/ into the game.'
    Write-Host 'This build script does not install the mod or modify your game.'
} catch {
    $exitCode = 1
    Write-Host ''
    Write-Host ('BUILD FAILED: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host ('Diagnostics: ' + $log)
} finally {
    if ($transcribing) { Stop-Transcript | Out-Null }
}
exit $exitCode
