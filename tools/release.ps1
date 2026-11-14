# Release helper: stamps the version, packages the game (tools\package.ps1: the tests, the native libraries, the
# engine's build, cook and stage, one hidden start of what ships), builds the installer and the portable zip, commits,
# tags, pushes and publishes the GitHub release; then keeps the Scoop bucket and winget current.
# Since 0.78 the game is the Unreal Engine version (unreal\); until 0.77 this script exported the Godot game
# (tools\build.ps1), whose copies update themselves to this one through the same update.json and package names.
# Usage: .\tools\release.ps1 -Version 0.78.0 [-NotesFile notes.md | -Notes "..."] [-NoPush] [-NoStores] [-SkipTests]
# -NoStores publishes the GitHub release only (the game's updater reads that) and leaves Scoop and winget alone:
# until 1.0 the stores stay at the last version they were given.
# -NoPush builds everything in dist\ and stops: nothing is committed, tagged or published.
# Run with PowerShell 7 (pwsh): the installer script is written back with its byte-order mark.
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes = "",
    [string]$NotesFile = "",
    [switch]$NoPush,
    [switch]$NoStores,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "The version must be three numbers, like 0.78.0 (the tag is v$Version)." }
if ($NotesFile -and -not (Test-Path $NotesFile)) { throw "The notes file is not there: $NotesFile" }
if (-not $NoPush -and (git status --porcelain)) { throw "Working tree not clean - commit or stash first." }

# ---- version stamps -------------------------------------------------------------------------------
# The game reads its version from the project's settings (GlpGame: ProjectVersion), the updater compares that with
# the release's, and the installer registers it.
$gameIni = Join-Path $root 'unreal\Config\DefaultGame.ini'
$text = Get-Content $gameIni -Raw
if ($text -notmatch '(?m)^ProjectVersion=[\d\.]+') { throw "$gameIni has no ProjectVersion line" }
$text -replace '(?m)^ProjectVersion=[\d\.]+', "ProjectVersion=$Version" | Set-Content $gameIni -Encoding utf8NoBOM -NoNewline
$iss = Join-Path $root 'installer\GrudgeLap.iss'
# UTF-8 with a BOM: Inno Setup reads a script without one in the system's ANSI code page, and the Turkish strings broke.
(Get-Content $iss -Raw) -replace '#define MyAppVersion "[\d\.]+"', ('#define MyAppVersion "' + $Version + '"') | Set-Content $iss -Encoding utf8BOM -NoNewline

# ---- tests, package, installer, portable zip -------------------------------------------------------
$packageArgs = @{}
if ($SkipTests) { $packageArgs.SkipTests = $true }
& (Join-Path $PSScriptRoot 'package.ps1') @packageArgs
$stage = Join-Path $root 'build\unreal'
if (-not (Test-Path (Join-Path $stage 'GrudgeLap.exe'))) { throw "the packaged game is not in $stage" }

$iscc = Get-ChildItem "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup not found (winget install JRSoftware.InnoSetup)" }
New-Item -ItemType Directory -Force (Join-Path $root 'dist') | Out-Null
$setup = Join-Path $root "dist\GrudgeLap-Setup-$Version.exe"
if (Test-Path $setup) { Remove-Item $setup -Force }
# The megabytes the game takes on disk, for the installer's own question about room: it asks before it removes an
# older version (an update takes no folder page, and so none of Setup's questions about free space).
$stageMB = [int][math]::Ceiling((Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)
Write-Host "building the installer (a few minutes: $stageMB MB through LZMA2)"
& $iscc.FullName /Q "/DMyAppDiskMB=$stageMB" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }
if (-not (Test-Path $setup)) { throw "Installer not produced: $setup" }

$portable = Join-Path $root "dist\GrudgeLap-Portable-$Version.zip"
if (Test-Path $portable) { Remove-Item $portable -Force }
python (Join-Path $PSScriptRoot 'portable_zip.py') $stage $portable
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $portable)) { throw "the portable zip was not produced: $portable" }

# GitHub takes at most 2 GiB for one file of a release; a package over that could not be published at all.
foreach ($package in $setup, $portable) {
    if ((Get-Item $package).Length -ge 2GB) { throw "$package is over GitHub's 2 GiB for a release file" }
}

$setupSha = (Get-FileHash $setup -Algorithm SHA256).Hash
"$setupSha  $(Split-Path -Leaf $setup)" | Set-Content "$setup.sha256" -Encoding ASCII -NoNewline
$zipSha = (Get-FileHash $portable -Algorithm SHA256).Hash
"$zipSha  $(Split-Path -Leaf $portable)" | Set-Content "$portable.sha256" -Encoding ASCII -NoNewline
Write-Host ("setup  {0}  {1:N0} MB  SHA256={2}" -f $setup, ((Get-Item $setup).Length / 1MB), $setupSha)
Write-Host ("zip    {0}  {1:N0} MB  SHA256={2}" -f $portable, ((Get-Item $portable).Length / 1MB), $zipSha)

# ---- update.json: what the game's updater reads first ---------------------------------------------------
# Read through github.com's releases/latest/download/ route, outside the REST API's 60 calls an hour per address
# (a home connection behind a carrier's shared address can find them used up), with the packages' exact names,
# sizes and SHA-256. The Godot game's copies (0.77) read the same file and take the same two packages: the installer
# for an installed copy, the zip for a portable one. Games up to 0.76 read the API and ignore this asset.
$manifest = Join-Path $root 'dist\update.json'
$manifestText = [ordered]@{
    version   = $Version
    tag       = "v$Version"
    published = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    setup     = [ordered]@{ name = (Split-Path -Leaf $setup); size = (Get-Item $setup).Length; sha256 = $setupSha.ToLowerInvariant() }
    portable  = [ordered]@{ name = (Split-Path -Leaf $portable); size = (Get-Item $portable).Length; sha256 = $zipSha.ToLowerInvariant() }
} | ConvertTo-Json -Depth 4
# Without a byte-order mark, whatever PowerShell runs this (Windows PowerShell's UTF8 writes one; the game reads either).
[System.IO.File]::WriteAllText($manifest, $manifestText, [System.Text.UTF8Encoding]::new($false))
Write-Host "update.json  $manifest"

# ---- winget manifests kept in the repo, regenerated for this version ---------------------------------
$wingetDir = Join-Path $root 'winget'
$releaseDate = (Get-Date).ToString('yyyy-MM-dd')
$setupUrl = "https://github.com/korayorhun/GrudgeLap/releases/download/v$Version/GrudgeLap-Setup-$Version.exe"
@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.12.0.schema.json
PackageIdentifier: KorayOrhun.GrudgeLap
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.12.0
"@ | Set-Content (Join-Path $wingetDir 'KorayOrhun.GrudgeLap.yaml') -Encoding UTF8
@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.12.0.schema.json
PackageIdentifier: KorayOrhun.GrudgeLap
PackageVersion: $Version
InstallerType: inno
Scope: user
InstallModes:
- interactive
- silent
- silentWithProgress
UpgradeBehavior: install
ReleaseDate: $releaseDate
Installers:
- Architecture: x64
  InstallerUrl: $setupUrl
  InstallerSha256: $setupSha
ManifestType: installer
ManifestVersion: 1.12.0
"@ | Set-Content (Join-Path $wingetDir 'KorayOrhun.GrudgeLap.installer.yaml') -Encoding UTF8
foreach ($locale in @('en-US', 'tr-TR')) {
    $path = Join-Path $wingetDir "KorayOrhun.GrudgeLap.locale.$locale.yaml"
    (Get-Content $path -Raw) -replace 'PackageVersion: [\d\.]+', "PackageVersion: $Version" -replace 'releases/tag/v[\d\.]+', "releases/tag/v$Version" | Set-Content $path -Encoding UTF8 -NoNewline
}

if ($NoPush) { Write-Host "NoPush: artefacts built, nothing committed or published."; exit 0 }

# ---- commit, tag, push, release ------------------------------------------------------------------
# Only what this script wrote: the two version stamps and the winget manifests. The tree was clean when the script
# began, and the build takes long enough for something else to be edited meanwhile: that is not this release's and
# stays uncommitted. A commit that fails (a hook, a signing key) stops the release here: read as "nothing new", it
# would have put the tag on the commit before and published that.
git add -- unreal/Config/DefaultGame.ini installer/GrudgeLap.iss winget
if ($LASTEXITCODE -ne 0) { throw "git add failed: nothing is committed, tagged or published" }
git diff --cached --quiet
if ($LASTEXITCODE -ne 0) {
    git commit -q -m "release: v$Version" -m "Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>"
    if ($LASTEXITCODE -ne 0) { throw "git commit failed: nothing is tagged or published" }
} else {
    Write-Host "nothing new to commit for v$Version (the stamps and the manifests were in place)"
}
$stray = @(git status --porcelain)
if ($stray.Count -gt 0) { Write-Host ("left uncommitted (changed while the release was built, not a part of it):`n  " + ($stray -join "`n  ")) }
git tag "v$Version"
if ($LASTEXITCODE -ne 0) { throw "the tag v$Version could not be made (does it exist already?)" }
git push -q
if ($LASTEXITCODE -ne 0) { throw "git push failed" }
git push -q --tags
if ($LASTEXITCODE -ne 0) { throw "git push --tags failed" }
# --latest said outright: the updater follows "latest", and a car model release (created with --latest=false) must
# never take that place from a game version.
$releaseArgs = @("v$Version", $setup, "$setup.sha256", $portable, "$portable.sha256", $manifest, '--title', "GrudgeLap $Version", '--latest')
if ($NotesFile) { $releaseArgs += @('--notes-file', $NotesFile) } else { $releaseArgs += @('--notes', $(if ($Notes) { $Notes } else { "GrudgeLap $Version" })) }
Write-Host "publishing the release (two packages of over a gigabyte each: this takes as long as the line needs)"
gh release create @releaseArgs
if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
Write-Host "Released v$Version"

# The route the games read first must give this version before they ask; GitHub can take a few seconds to switch.
$latestManifest = 'https://github.com/korayorhun/GrudgeLap/releases/latest/download/update.json'
$seen = $null
for ($attempt = 1; $attempt -le 24 -and $seen -ne $Version; $attempt++) {
    try {
        $response = Invoke-WebRequest -Uri $latestManifest -UseBasicParsing -TimeoutSec 20
        $text = if ($response.Content -is [byte[]]) { [System.Text.Encoding]::UTF8.GetString($response.Content) } else { [string]$response.Content }
        $seen = ($text.TrimStart([char]0xFEFF) | ConvertFrom-Json).version
    } catch { $seen = $null }
    if ($seen -ne $Version) { Start-Sleep -Seconds 5 }
}
if ($seen -ne $Version) { throw "$latestManifest gives '$seen', not ${Version}: the games' updaters would not see this release first (they fall back to the API)" }
Write-Host "update.json check: $latestManifest gives $seen"
if ($NoStores) { Write-Host "NoStores: Scoop and winget left as they were."; exit 0 }

# ---- scoop: personal bucket (korayorhun/scoop-bucket) ---------------------------------------------
try {
    $json = @"
{
    "version": "$Version",
    "description": "GrudgeLap - a photorealistic homage to the 1989 arcade driving simulators: stunt track with loop and drawbridge, traffic, ghost lap, real cars with real engine recordings.",
    "homepage": "https://github.com/korayorhun/GrudgeLap",
    "license": "MIT",
    "url": "https://github.com/korayorhun/GrudgeLap/releases/download/v$Version/GrudgeLap-Portable-$Version.zip",
    "hash": "$($zipSha.ToLower())",
    "shortcuts": [
        [
            "GrudgeLap.exe",
            "GrudgeLap"
        ]
    ],
    "checkver": "github",
    "autoupdate": {
        "url": "https://github.com/korayorhun/GrudgeLap/releases/download/v`$version/GrudgeLap-Portable-`$version.zip"
    }
}
"@
    $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
    $previous = gh api repos/korayorhun/scoop-bucket/contents/bucket/grudgelap.json --jq .sha 2>$null
    $apiArgs = @('-X', 'PUT', 'repos/korayorhun/scoop-bucket/contents/bucket/grudgelap.json',
        '-f', "message=grudgelap: Update to version $Version`n`nCo-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>", '-f', "content=$b64")
    if ($previous) { $apiArgs += @('-f', "sha=$previous") }
    gh api @apiArgs --jq .commit.sha | Out-Null
    Write-Host "scoop bucket updated to $Version"
} catch { Write-Host "scoop bucket update failed: $_" }

# ---- winget: first version is a new-package PR from the manifests, later ones an update PR ----------
if (Get-Command wingetcreate -ErrorAction SilentlyContinue) {
    $env:WINGET_CREATE_GITHUB_TOKEN = (gh auth token)
    $known = winget show --id KorayOrhun.GrudgeLap --exact --source winget 2>$null
    if ($LASTEXITCODE -eq 0 -and $known) {
        wingetcreate update KorayOrhun.GrudgeLap --version $Version --urls "$setupUrl|x64|user" --out (Join-Path $env:TEMP "grudgelap-winget-$Version") --prtitle "Update version: KorayOrhun.GrudgeLap version $Version" --submit
    } else {
        wingetcreate submit --prtitle "New package: KorayOrhun.GrudgeLap version $Version" $wingetDir
    }
    Remove-Item Env:WINGET_CREATE_GITHUB_TOKEN
} else { Write-Host "winget: wingetcreate not found - submit the manifests in winget/ manually" }
