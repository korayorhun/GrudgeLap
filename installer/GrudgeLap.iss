; Inno Setup script for GrudgeLap (per-user install, no admin rights needed).
; The game is the Unreal Engine version's packaged build, laid out by tools/package.ps1 in ..\build\unreal: the launcher
; GrudgeLap.exe in the root, the engine's and the game's folders beside it. Until 0.77 it was the Godot game
; (GrudgeLap.exe, GrudgeLap.pck and a data folder): an update from there removes those and keeps the folder, the
; shortcuts and the AppId.
#define MyAppVersion "0.81.0"
#define MyAppPublisher "Koray Orhun"
#define MyAppURL "https://github.com/korayorhun/GrudgeLap"
#define MyAppExeName "GrudgeLap.exe"
; The game's own executable: the launcher starts it and waits for it.
#define MyGameExe "GrudgeLapUE\Binaries\Win64\GrudgeLapUE.exe"
#define MyGameImage "GrudgeLapUE.exe"
; A test build installs beside the real game without touching it:
;   ISCC /DTestBuild /DMyAppName="GrudgeLap Test" /DMyAppId="{{...another GUID...}" /DGameMutex=... /DSourceDir=... /F<name> /O<folder>
; TestBuild also leaves every running game alone (no taskkill at all).
#ifndef MyAppName
  #define MyAppName "GrudgeLap"
#endif
#ifndef MyAppId
  #define MyAppId "{{871E561A-E89E-41ED-870A-A635D3E789C2}"
#endif
#ifndef SourceDir
  #define SourceDir "..\build\unreal"
#endif
; The game's one-copy lock (GlpInstance, and the Godot game's SingleInstance.cs before it): it exists while a copy runs.
#ifndef GameMutex
  #define GameMutex "Global\GrudgeLap.SingleInstance"
#endif
; The megabytes the game takes on disk: tools\release.ps1 passes what it packaged (/DMyAppDiskMB=...), and a build
; without it (a test build) takes this figure. PrepareToInstall asks for that much room before anything is removed.
#ifndef MyAppDiskMB
  #define MyAppDiskMB 1600
#endif

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
; By the application's name: "GrudgeLap" for the game, and a folder of its own for a test build (/DMyAppName=...),
; which must never default to the player's copy.
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=GrudgeLap-Setup-{#MyAppVersion}
SetupIconFile=..\game\assets\GrudgeLap.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
; The cooked content is 0.9 GB and the game's executable 0.35 GB: four block threads keep the compile to minutes.
; Each needs some 700 MB for the 64 MB dictionary, more than the 32-bit compiler's address space holds together: the
; compressor runs in its own 64-bit process (without it ISCC stops with "Out of memory").
Compression=lzma2/ultra64
SolidCompression=yes
LZMANumBlockThreads=4
LZMAUseSeparateProcess=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=..\LICENSE
CloseApplications=yes
CloseApplicationsFilter=*.exe
ShowLanguageDialog=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[CustomMessages]
english.DesktopTask=Create a desktop shortcut
english.RunNow=Start GrudgeLap now
english.Uninstall=Uninstall
; Under 110 characters with its numbers: the game shows the line whole at its next start (it reads it from this log).
english.NoRoom=GrudgeLap needs %1 MB free on this drive and has %2 MB. Nothing was changed: make room and update again.
english.OtherGame=This folder holds another game's files (an "Engine" folder that is not GrudgeLap's). Choose a folder of its own for GrudgeLap.
turkish.DesktopTask=Masaüstü kısayolu oluştur
turkish.RunNow=GrudgeLap'i şimdi başlat
turkish.Uninstall=Kaldır
turkish.NoRoom=GrudgeLap bu sürücüde %1 MB boş alan istiyor, %2 MB var. Hiçbir şey değişmedi: yer açıp yeniden deneyin.
turkish.OtherGame=Bu klasörde başka bir oyunun dosyaları var (GrudgeLap'e ait olmayan bir "Engine" klasörü). GrudgeLap için ayrı bir klasör seçin.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopTask}"

[InstallDelete]
; The Godot game's files (to 0.77): its executable's name is the new launcher's, the rest goes.
Type: files; Name: "{app}\GrudgeLap.pck"
Type: files; Name: "{app}\GrudgeLap.console.exe"
Type: filesandordirs; Name: "{app}\data_GrudgeLap_windows_x86_64"
; An earlier Unreal version's files: what this version no longer has must not stay beside it (the engine mounts every
; pak it finds and loads the libraries it finds). The player's files are not here: %LOCALAPPDATA%\GrudgeLap.
; "Engine" is a name other games use too: it goes only from a folder that held this game's Unreal version.
Type: filesandordirs; Name: "{app}\Engine"; Check: HadUnrealCopy
Type: filesandordirs; Name: "{app}\GrudgeLapUE\Binaries"
Type: filesandordirs; Name: "{app}\GrudgeLapUE\Content"
Type: filesandordirs; Name: "{app}\GrudgeLapUE\Plugins"

[Files]
; The whole packaged game, the launcher and the notices in its root included.
; Not what a run of the game left in the folder it was packaged from (its logs, the engine's own settings).
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "\GrudgeLapUE\Saved\*,\Engine\Saved\*,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName} - {cm:Uninstall}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:RunNow}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: IsSelfUpdate

#ifndef TestBuild
[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#MyAppExeName} /F"; Flags: runhidden; RunOnceId: "KillGrudgeLap"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#MyGameImage} /F"; Flags: runhidden; RunOnceId: "KillGrudgeLapGame"
#endif

[UninstallDelete]
; The logs and the engine's own settings a run leaves in the game's folder.
Type: filesandordirs; Name: "{app}\GrudgeLapUE\Saved"
Type: filesandordirs; Name: "{app}\Engine\Saved"

[Code]
// The game's updater starts this installer itself (/SILENT /NOCANCEL /LOG=...) and then quits through its normal
// shutdown. /UPDATE=1: start the game again afterwards; /UPDATE=2: do not (an update applied as the game closes, or a
// check without a player). Up to 0.76 the game quit first and a PowerShell script ran this installer with /UPDATE=1
// once the game had gone; both ways work.
function StartedByGame: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') <> '0';
end;

function IsSelfUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

// True when nothing holds the executable, so that it can be replaced.
function ExeFree(const Exe: String): Boolean;
var
  Stream: TFileStream;
begin
  Result := True;
  if not FileExists(Exe) then Exit;
  try
    Stream := TFileStream.Create(Exe, fmOpenReadWrite or fmShareExclusive);
    Stream.Free;
  except
    Result := False;
  end;
end;

// The launcher waits for the game it started, so either executable being held means the game is still there.
function GameRunning: Boolean;
begin
  Result := CheckForMutexes('{#GameMutex}') or not ExeFree(ExpandConstant('{app}\{#MyAppExeName}'))
    or not ExeFree(ExpandConstant('{app}\{#MyGameExe}'));
end;

// Whether the folder held this game's Unreal version when the installer started: asked for [InstallDelete], whose
// entries run one after another, so the answer is taken once, before the first of them removes anything.
var
  UnrealCopyKnown, UnrealCopyFound: Boolean;

function HadUnrealCopy: Boolean;
begin
  if not UnrealCopyKnown then
  begin
    UnrealCopyFound := FileExists(ExpandConstant('{app}\{#MyGameExe}'));
    UnrealCopyKnown := True;
  end;
  Result := UnrealCopyFound;
end;

// The megabytes of one file, and of a folder's files with its sub-folders; nothing when it is not there.
function SizeMB(const Rec: TFindRec): Extended;
begin
  Result := (Rec.SizeHigh * 4294967296.0 + Rec.SizeLow) / 1048576.0;
end;

function FileMB(const Name: String): Extended;
var
  Rec: TFindRec;
begin
  Result := 0;
  if FindFirst(Name, Rec) then
  begin
    Result := SizeMB(Rec);
    FindClose(Rec);
  end;
end;

function FolderMB(const Dir: String): Extended;
var
  Rec: TFindRec;
begin
  Result := 0;
  if FindFirst(AddBackslash(Dir) + '*', Rec) then
  try
    repeat
      if Rec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0 then
      begin
        if (Rec.Name <> '.') and (Rec.Name <> '..') then
          Result := Result + FolderMB(AddBackslash(Dir) + Rec.Name);
      end
      else
        Result := Result + SizeMB(Rec);
    until not FindNext(Rec);
  finally
    FindClose(Rec);
  end;
end;

// A folder that holds another Unreal Engine game is refused. "Engine" is every such game's folder name: this game's
// files would be written into it, and the first update, which by then finds this game's executable in the folder,
// would remove it whole ([InstallDelete]). A folder with this game in it, as either version, is an update.
function NextButtonClick(CurPageID: Integer): Boolean;
var
  Dir: String;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    Dir := AddBackslash(WizardDirValue);
    if DirExists(Dir + 'Engine') and not FileExists(Dir + '{#MyGameExe}') and not FileExists(Dir + 'GrudgeLap.pck') then
    begin
      Log('Refused the folder ' + Dir + ': its Engine folder is not this game''s');
      SuppressibleMsgBox(CustomMessage('OtherGame'), mbError, MB_OK, IDOK);
      Result := False;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode, Waited: Integer;
  App: String;
  FreeMB, TotalMB: Cardinal;
  Room, Freed, Need: Extended;
begin
  // Before anything is removed or replaced.
  HadUnrealCopy;
  // Room first. An update takes no folder page, and with it none of Setup's own questions about free space: on a
  // drive that is nearly full [InstallDelete] would remove the old game, the new one would not fit, Setup would roll
  // back, and an update the player may not even have asked for (0.77 fetched it by itself) would leave no game at
  // all. What the old version's files give back is counted in, so an update of this game needs little room.
  App := ExpandConstant('{app}');
  Freed := FileMB(App + '\GrudgeLap.pck') + FolderMB(App + '\data_GrudgeLap_windows_x86_64') + FolderMB(App + '\GrudgeLapUE\Binaries')
    + FolderMB(App + '\GrudgeLapUE\Content') + FolderMB(App + '\GrudgeLapUE\Plugins');
  if HadUnrealCopy then
    Freed := Freed + FolderMB(App + '\Engine');
  Need := {#MyAppDiskMB} + 64 - Freed;
  if GetSpaceOnDisk(App, True, FreeMB, TotalMB) then
  begin
    Room := FreeMB;
    Log('Room in ' + App + ': ' + IntToStr(FreeMB) + ' MB free, ' + IntToStr(Round(Need)) + ' MB needed ('
      + IntToStr(Round(Freed)) + ' MB of the version in place counted in)');
    if Room < Need then
    begin
      Log('Not enough room: nothing was changed');
      Result := FmtMessage(CustomMessage('NoRoom'), [IntToStr(Round(Need)), IntToStr(FreeMB)]);
      Exit;
    end;
  end;
  Waited := 0;
  // Started by the game: it is on its way out (saving, letting go of its lock; its executable stays held for a second
  // or two after its process ends), so it is waited for rather than killed. A copy still there after a minute, or any
  // copy when the installer was started by hand, is closed as before.
  if StartedByGame then
    while GameRunning and (Waited < 60000) do
    begin
      Sleep(250);
      Waited := Waited + 250;
    end;
  Log(Format('Waited %d ms for the game to exit', [Waited]));
  if GameRunning then
  begin
#ifdef TestBuild
    Log('The game is still running: a test build leaves it alone');
#else
    Log('The game is still running: closing it');
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#MyAppExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#MyGameImage} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(500);
#endif
  end;
  Result := '';
end;
