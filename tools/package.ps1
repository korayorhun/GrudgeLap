# Packages the game: the Unreal Engine version, the only one since 0.78 (the Godot game is frozen at 0.77.0 and
# tools/build.ps1 still exports it). It runs the tests, publishes the two native libraries, has the engine build,
# cook and stage the game, and lays out build\unreal as the installer and the portable zip ship it:
#   GrudgeLap.exe          the engine's small launcher under the game's name: it starts the executable below and
#                          waits for it (the name the Godot game had, so shortcuts and the updaters of 0.77 still fit)
#   Engine\, GrudgeLapUE\  the engine's and the game's files (GrudgeLapUE\Binaries\Win64\GrudgeLapUE.exe is the game,
#                          with the Visual C++ runtime beside it: nothing has to be installed in Windows first)
#   README.md, LICENSE, THIRD_PARTY.md, CREDITS-car-models.txt
# Then it starts the packaged game once, hidden and silent, and checks that it reaches the road.
# The level is not built here: it is the one on disk (unreal\Scripts\build.cmd), as it was last looked at.
# Usage: .\tools\package.ps1 [-SkipTests] [-SkipSmoke] [-Engine C:\UE\5.8]
param(
    [switch]$SkipTests,
    [switch]$SkipSmoke,
    [string]$Engine = $(if ($env:UE_ROOT) { $env:UE_ROOT } else { 'C:\UE\5.8' })
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$project = Join-Path $root 'unreal\GrudgeLapUE.uproject'
$uat = Join-Path $Engine 'Engine\Build\BatchFiles\RunUAT.bat'
if (-not (Test-Path $uat)) { throw "Unreal Engine not found at $Engine (pass -Engine or set UE_ROOT)" }

if (-not $SkipTests) {
    foreach ($suite in 'physics.tests\GrudgeLap.Physics.Tests.csproj', 'audio.tests\GrudgeLap.Audio.Tests.csproj', 'update.tests\GrudgeLap.Update.Tests.csproj') {
        dotnet test (Join-Path $root $suite) -c Release --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "tests failed: $suite" }
    }
}

# ---- the native libraries: the simulation core and the updater (NativeAOT) ---------------------------------
# The linker is found through the Visual Studio installer's vswhere, which is not on every shell's PATH.
$vsInstaller = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer'
if ((Test-Path $vsInstaller) -and ($env:PATH -notlike "*$vsInstaller*")) { $env:PATH += ";$vsInstaller" }
dotnet publish (Join-Path $root 'physics.native') -c Release -r win-x64 -o (Join-Path $root 'artifacts\physics-native') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'physics.native did not publish' }
dotnet publish (Join-Path $root 'update.native') -c Release -r win-x64 -o (Join-Path $root 'artifacts\update-native') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'update.native did not publish' }

# ---- the game's icon on its executables ---------------------------------------------------------------------
$icon = Join-Path $root 'unreal\Build\Windows\Application.ico'
New-Item -ItemType Directory -Force (Split-Path -Parent $icon) | Out-Null
Copy-Item (Join-Path $root 'game\assets\GrudgeLap.ico') $icon -Force

# ---- build, cook, stage, archive ----------------------------------------------------------------------------
New-Item -ItemType Directory -Force (Join-Path $root 'build') | Out-Null
$archive = Join-Path $root 'build\unreal-archive'
$log = Join-Path $root 'build\package.log'
# The cook runs in the editor with the game's module as the editor has it: built here first, so that the classes the
# cook writes are the ones the game reads (the packaging itself builds only the game, -nocompileeditor).
$editorLog = Join-Path $root 'build\package-editor.log'
& (Join-Path $Engine 'Engine\Build\BatchFiles\Build.bat') GrudgeLapUEEditor Win64 Development "-Project=$project" -WaitMutex *> $editorLog
if ($LASTEXITCODE -ne 0) { throw "the editor's module did not build: see $editorLog" }
# The Visual C++ runtime beside the game. The engine's launcher (GrudgeLap.exe) starts the game only where it finds
# the runtime the engine was built with or a newer one: beside the game's executable, or installed in Windows. A
# computer that has an older one (most have: 14.50 is Visual Studio 2026's) would get a message box and no game, and
# installing the runtime asks for an administrator. So its ten libraries ship beside the executable, where Windows
# looks first; the launcher then asks for nothing. Only the runtime is taken from the engine's set: the Universal CRT
# is a part of Windows 10 and 11, and the DirectX audio libraries of 2010 are not used.
$runtimeSource = Join-Path $Engine 'Engine\Binaries\ThirdParty\AppLocalDependencies\Win64\x64\Microsoft.VC.CRT'
if (-not (Test-Path (Join-Path $runtimeSource 'vcruntime140_1.dll'))) { throw "the engine's Visual C++ runtime is not in $runtimeSource" }
$appLocal = Join-Path $root 'build\applocal'
$runtimeStage = Join-Path $appLocal 'Win64\x64\Microsoft.VC.CRT'
if (Test-Path $appLocal) { Remove-Item $appLocal -Recurse -Force }
New-Item -ItemType Directory -Force $runtimeStage | Out-Null
Copy-Item (Join-Path $runtimeSource '*.dll') $runtimeStage

# The archive folder is emptied first: the engine's archive step only adds and overwrites, so a file that an earlier
# version had and this one has not (a plugin's library, a pak under another name) would stay in it, be copied into
# the folder that ships and be loaded by the engine there.
if (Test-Path $archive) { Remove-Item $archive -Recurse -Force }

Write-Host "packaging (the log: $log)"
& $uat BuildCookRun "-project=$project" -noP4 -platform=Win64 -clientconfig=Development -build -cook -stage -pak -archive "-archivedirectory=$archive" "-applocaldirectory=$appLocal" -unattended -nocompileeditor *> $log
if ($LASTEXITCODE -ne 0) { throw "packaging failed: see $log" }
# A material whose shaders did not compile is cooked all the same, and the game draws the engine's default material
# in its place; nothing fails. The editor compiles only the shaders it draws with, the cook every shader a material
# can be asked for, so a material can look right in every run from the editor and be missing from what ships (0.80:
# the windscreen's picture read a texture that only its own pass declares, and the packaged game drew the default
# material over the seat's view). The cook's log is read for it.
$uncompiled = @(Select-String -Path $log -Pattern 'Failed to compile Material (\S+)' -AllMatches |
    ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
if ($uncompiled.Count -gt 0) {
    throw "the cook could not compile $($uncompiled.Count) material(s), which the game would draw as the engine's default: $($uncompiled -join ', ') (see $log)"
}

# ---- the folder that ships ----------------------------------------------------------------------------------
# Without the debug symbols (2.6 GB), the build's manifests, any Saved folder of a run, the DLSS plugin's
# development libraries (150 MB that the engine stages with a Development build as debug files: the plugin loads them
# only when it is told to, and NVIDIA's are for development machines, not for players), and the engine's extras (a
# viewer for GPU dumps with the scripts that open it: a developer's tool that a Development build brings along).
$stage = Join-Path $root 'build\unreal'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$dlssDevelopment = Join-Path $archive 'Windows\GrudgeLapUE\Plugins\DLSS\Binaries\ThirdParty\Win64\Development'
$engineExtras = Join-Path $archive 'Windows\Engine\Extras'
robocopy (Join-Path $archive 'Windows') $stage /E /XF *.pdb Manifest_*.txt /XD Saved $dlssDevelopment $engineExtras /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "copying the packaged game failed (robocopy $LASTEXITCODE)" }
Rename-Item (Join-Path $stage 'GrudgeLapUE.exe') 'GrudgeLap.exe'
Copy-Item (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD_PARTY.md') $stage
# The car models' credits beside the game, as their CC BY licences ask of every copy.
python (Join-Path $root 'tools\portable_zip.py') --credits (Join-Path $stage 'CREDITS-car-models.txt')
if ($LASTEXITCODE -ne 0) { throw 'the car models'' credits could not be written' }
foreach ($needed in 'GrudgeLap.exe', 'GrudgeLapUE\Binaries\Win64\GrudgeLapUE.exe', 'GrudgeLapUE\Binaries\Win64\GrudgeLap.Physics.Native.dll',
        'GrudgeLapUE\Binaries\Win64\GrudgeLap.Update.Native.dll', 'GrudgeLapUE\Content\Paks\GrudgeLapUE-Windows.pak') {
    if (-not (Test-Path (Join-Path $stage $needed))) { throw "the packaged game lacks $needed" }
}
# The two libraries the launcher looks at, against the version it asks for (the engine's own figure): a package
# that fails here would start on this computer and on few others.
$header = Join-Path $Engine 'Engine\Source\Runtime\ApplicationCore\Internal\Windows\WindowsRedistributableValidation.h'
if (-not ((Test-Path $header) -and ((Get-Content $header -Raw) -match 'MinRedistVersion\s*=\s*\{\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\}'))) {
    throw "the runtime version the launcher asks for could not be read from $header"
}
$runtimeMinimum = [version]::new([int]$Matches[1], [int]$Matches[2], [int]$Matches[3], [int]$Matches[4])
foreach ($library in 'msvcp140_2.dll', 'vcruntime140_1.dll') {
    $path = Join-Path $stage "GrudgeLapUE\Binaries\Win64\$library"
    if (-not (Test-Path $path)) { throw "the packaged game lacks the Visual C++ runtime beside its executable ($library)" }
    $info = (Get-Item $path).VersionInfo
    $have = [version]::new($info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart, $info.FilePrivatePart)
    if ($have -lt $runtimeMinimum) { throw "$library is $have and the launcher asks for $runtimeMinimum" }
}
Write-Host "Visual C++ runtime beside the game: $runtimeMinimum or newer, as the launcher asks"

# ---- one hidden, silent start of what ships -----------------------------------------------------------------
if (-not $SkipSmoke) {
    $data = Join-Path $env:TEMP "GrudgeLap-smoke-$PID"
    $smoke = Join-Path $root 'build\smoke.log'
    if (Test-Path $smoke) { Remove-Item $smoke -Force }
    $arguments = "-RenderOffScreen -NoSound -unattended -nosplash -ResX=1280 -ResY=720 -GlpAttract -GlpQuitAfter=6 `"-GlpData=$data`" -ExecCmds=`"t.MaxFPS 60`" `"-abslog=$smoke`""
    $game = Start-Process (Join-Path $stage 'GrudgeLap.exe') -ArgumentList $arguments -PassThru -WindowStyle Hidden
    # While it runs, what its process listens on is read every second. The game must open no port for others to
    # reach (Windows Firewall asks every player about a program that does, the first time it runs from a folder):
    # the engine's trace control is compiled out (unreal\Source\GrudgeLapUE.Target.cs), and this catches the next one.
    $gameExe = Join-Path $stage 'GrudgeLapUE\Binaries\Win64\GrudgeLapUE.exe'
    $listening = [System.Collections.Generic.SortedSet[string]]::new()
    $deadline = (Get-Date).AddMinutes(5)
    while (-not $game.HasExited) {
        if ((Get-Date) -gt $deadline) { $game.Kill(); throw 'the packaged game did not quit within five minutes' }
        Start-Sleep -Seconds 1
        $running = Get-Process GrudgeLapUE -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $gameExe } | Select-Object -First 1
        if (-not $running) { continue }
        Get-NetTCPConnection -OwningProcess $running.Id -State Listen -ErrorAction SilentlyContinue |
            ForEach-Object { [void]$listening.Add("TCP $($_.LocalAddress):$($_.LocalPort)") }
        Get-NetUDPEndpoint -OwningProcess $running.Id -ErrorAction SilentlyContinue |
            ForEach-Object { [void]$listening.Add("UDP $($_.LocalAddress):$($_.LocalPort)") }
    }
    $text = if (Test-Path $smoke) { Get-Content $smoke -Raw } else { '' }
    foreach ($line in 'simulation core loaded', 'updater ready', 'new session', 'quitting after') {
        if ($text -notlike "*$line*") { throw "the packaged game's log lacks `"$line`": see $smoke" }
    }
    # The same from the game's side: a material it loaded without its shaders (the cook's check above).
    $defaulted = @($text -split "`n" | Where-Object { $_ -match 'will use default material|Default Material will be used' })
    if ($defaulted.Count -gt 0) { throw "the packaged game drew the engine's default material for one of its own: $($defaulted[0].Trim()) (see $smoke)" }
    # A port on the loopback address is reached from this computer only, and the firewall does not ask about it.
    $open = @($listening | Where-Object { $_ -notmatch '^(TCP|UDP) (127\.|::1:)' })
    if ($open.Count -gt 0 -or $text -like '*Control listening on port*') {
        throw "the packaged game listens on the network ($($open -join ', ')): Windows Firewall would ask every player about it"
    }
    Write-Host 'smoke run: the game opened no network port'
    # Not a failure (a build machine may have no RTX card), but worth a line: what the upscaler found.
    $upscaler = ($text -split "`n" | Where-Object { $_ -like '*LogGlpGame: upscaler:*' } | Select-Object -First 1)
    if ($upscaler) { Write-Host ('smoke run: ' + ($upscaler -replace '^.*LogGlpGame: ', '').Trim()) }
    Write-Host "smoke run: the packaged game reached the road and quit (the log: $smoke)"
    Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue
    # What that run left in the folder that ships: the game's logs and settings, and the engine's own (Engine\Saved,
    # which the installer would otherwise have taken along).
    foreach ($saved in 'GrudgeLapUE\Saved', 'Engine\Saved') {
        $left = Join-Path $stage $saved
        if (Test-Path $left) { Remove-Item $left -Recurse -Force }
    }
}
$size = (Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("packaged: {0}  ({1:N0} MB)" -f $stage, ($size / 1MB))
