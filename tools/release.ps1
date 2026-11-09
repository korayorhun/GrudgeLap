# Release helper: bumps the version, runs the tests, exports the game, builds the installer and the portable
# zip, commits, tags, pushes and publishes the GitHub release; then keeps the Scoop bucket and winget current.
# Usage: .\tools\release.ps1 -Version 0.2.0 [-Notes "..."] [-NoPush] [-NoStores]
# -NoStores publishes the GitHub release only (the game's updater reads that) and leaves Scoop and winget alone:
# until 1.0 the stores stay at the last version they were given.
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes = "",
    [switch]$NoPush,
    [switch]$NoStores
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
if (-not $NoPush -and (git status --porcelain)) { throw "Working tree not clean - commit or stash first." }

# ---- version stamps -------------------------------------------------------------------------------
$godotProject = Join-Path $root 'game\project.godot'
(Get-Content $godotProject -Raw) -replace 'config/version="[\d\.]+"', ('config/version="' + $Version + '"') | Set-Content $godotProject -Encoding UTF8 -NoNewline
$csproj = Join-Path $root 'game\GrudgeLap.csproj'
$text = Get-Content $csproj -Raw
$text = $text -replace '<Version>[\d\.]+</Version>', "<Version>$Version</Version>"
$text = $text -replace '<FileVersion>[\d\.]+</FileVersion>', "<FileVersion>$Version.0</FileVersion>"
Set-Content $csproj $text -Encoding UTF8 -NoNewline
$iss = Join-Path $root 'installer\GrudgeLap.iss'
# UTF-8 with a BOM: Inno Setup reads a script without one in the system's ANSI code page, and the Turkish strings broke.
(Get-Content $iss -Raw) -replace '#define MyAppVersion "[\d\.]+"', ('#define MyAppVersion "' + $Version + '"') | Set-Content $iss -Encoding utf8BOM -NoNewline

# ---- tests, export, installer, portable zip --------------------------------------------------------
& (Join-Path $PSScriptRoot 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$iscc = Get-ChildItem "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup not found (winget install JRSoftware.InnoSetup)" }
New-Item -ItemType Directory -Force (Join-Path $root 'dist') | Out-Null
& $iscc.FullName /Q $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }
$setup = Join-Path $root "dist\GrudgeLap-Setup-$Version.exe"
if (-not (Test-Path $setup)) { throw "Installer not produced: $setup" }

$portable = Join-Path $root "dist\GrudgeLap-Portable-$Version.zip"
if (Test-Path $portable) { Remove-Item $portable -Force }
$stage = Join-Path $env:TEMP "GrudgeLap-portable-$Version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item (Join-Path $root 'build\GrudgeLap.exe'), (Join-Path $root 'build\GrudgeLap.pck') $stage
Copy-Item (Join-Path $root 'build\data_GrudgeLap_windows_x86_64') (Join-Path $stage 'data_GrudgeLap_windows_x86_64') -Recurse
Copy-Item (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD_PARTY.md') $stage
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $portable -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force

$setupSha = (Get-FileHash $setup -Algorithm SHA256).Hash
"$setupSha  $(Split-Path -Leaf $setup)" | Set-Content "$setup.sha256" -Encoding ASCII -NoNewline
$zipSha = (Get-FileHash $portable -Algorithm SHA256).Hash
"$zipSha  $(Split-Path -Leaf $portable)" | Set-Content "$portable.sha256" -Encoding ASCII -NoNewline
Write-Host "setup  $setup  SHA256=$setupSha"
Write-Host "zip    $portable  SHA256=$zipSha"

# ---- update.json: what the game's updater reads first ---------------------------------------------------
# Read through github.com's releases/latest/download/ route, outside the REST API's 60 calls an hour per address
# (a home connection behind a carrier's shared address can find them used up), with the packages' exact names,
# sizes and SHA-256. Games up to 0.76 read the API and ignore this asset; a release without it still updates through the API.
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
git add -A
git commit -q -m "release: v$Version" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
if ($LASTEXITCODE -ne 0) { Write-Host "nothing new to commit for v$Version" }
git tag "v$Version"
git push -q
git push -q --tags
if (-not $Notes) { $Notes = "GrudgeLap $Version" }
# --latest said outright: the updater follows "latest", and a car model release (created with --latest=false) must
# never take that place from a game version.
gh release create "v$Version" $setup "$setup.sha256" $portable "$portable.sha256" $manifest --title "GrudgeLap $Version" --notes $Notes --latest
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
    "description": "GrudgeLap - a keyboard-playable homage to the 1989 arcade driving simulators: stunt track with loop and drawbridge, traffic, ghost lap, two cars with real engine recordings.",
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
        '-f', "message=grudgelap: Update to version $Version`n`nCo-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>", '-f', "content=$b64")
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
