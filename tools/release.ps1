# Release helper: bumps the version, runs the tests, exports the game, builds the installer and the portable
# zip, commits, tags, pushes and publishes the GitHub release; then keeps the Scoop bucket and winget current.
# Usage: .\tools\release.ps1 -Version 0.2.0 [-Notes "..."] [-NoPush]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes = "",
    [switch]$NoPush
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
(Get-Content $iss -Raw) -replace '#define MyAppVersion "[\d\.]+"', ('#define MyAppVersion "' + $Version + '"') | Set-Content $iss -Encoding UTF8 -NoNewline

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
Copy-Item (Join-Path $root 'build\GrudgeLap.exe') $stage
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
gh release create "v$Version" $setup "$setup.sha256" $portable "$portable.sha256" --title "GrudgeLap $Version" --notes $Notes
Write-Host "Released v$Version"

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
