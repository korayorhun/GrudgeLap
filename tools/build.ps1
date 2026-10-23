# Builds and exports GrudgeLap for Windows.
# Usage: ./tools/build.ps1 [-Godot <path to Godot_v4.x_mono_win64_console.exe>] [-SkipTests]
param(
    [string]$Godot = "",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

if ($Godot -eq "") {
    # @() keeps a single hit an array; otherwise [0] would index the first character of the path.
    $candidates = @(@(
        (Get-Command godot -ErrorAction SilentlyContinue | ForEach-Object { $_.Source }),
        (Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono*\*\Godot_v*_mono_win64_console.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1 | ForEach-Object { $_.FullName })
    ) | Where-Object { $_ })
    if ($candidates.Count -eq 0) { throw "Godot .NET console executable not found; pass -Godot <path>." }
    $Godot = [string]$candidates[0]
}

if (-not $SkipTests) {
    dotnet test "$root/physics.tests/GrudgeLap.Physics.Tests.csproj" --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "tests failed" }
    dotnet test "$root/audio.tests/GrudgeLap.Audio.Tests.csproj" --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "audio tests failed" }
}

dotnet build "$root/game/GrudgeLap.csproj" -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "game build failed" }

New-Item -ItemType Directory -Force "$root/build" | Out-Null
# Import the assets (audio loops) first; a fresh checkout has no .godot/imported cache and the export would ship nothing.
& $Godot --headless --path "$root/game" --import 2>&1 | Out-Null
& $Godot --headless --path "$root/game" --export-release "Windows Desktop" "$root/build/GrudgeLap.exe"
if ($LASTEXITCODE -ne 0) { throw "export failed (are the export templates for this Godot version installed?)" }

Write-Host "exported: $root/build/GrudgeLap.exe"
