; Inno Setup script for GrudgeLap (per-user install, no admin rights needed).
; The exe and its data folder come from tools/build.ps1 (Godot export into ..\build).
#define MyAppName "GrudgeLap"
#define MyAppVersion "0.77.0"
#define MyAppPublisher "Koray Orhun"
#define MyAppURL "https://github.com/korayorhun/GrudgeLap"
#define MyAppExeName "GrudgeLap.exe"
; The game's one-copy lock (game/src/SingleInstance.cs): it exists while a copy runs. A test build may name another.
#ifndef GameMutex
  #define GameMutex "Global\GrudgeLap.SingleInstance"
#endif

[Setup]
AppId={{871E561A-E89E-41ED-870A-A635D3E789C2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\GrudgeLap
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=GrudgeLap-Setup-{#MyAppVersion}
SetupIconFile=..\game\assets\GrudgeLap.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
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
turkish.DesktopTask=Masaüstü kısayolu oluştur
turkish.RunNow=GrudgeLap'i şimdi başlat
turkish.Uninstall=Kaldır

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopTask}"

[Files]
Source: "..\build\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; The game's data sits beside the exe rather than inside it: the exe then stays the stock engine for a
; version, and antivirus software that holds back a new unsigned executable on its first launch does so less.
Source: "..\build\GrudgeLap.pck"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\data_GrudgeLap_windows_x86_64\*"; DestDir: "{app}\data_GrudgeLap_windows_x86_64"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD_PARTY.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName} - {cm:Uninstall}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:RunNow}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: IsSelfUpdate

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#MyAppExeName} /F"; Flags: runhidden; RunOnceId: "KillGrudgeLap"

[Code]
// The game's updater starts this installer itself (/SILENT /NOCANCEL /LOG=...) and then quits through its normal
// shutdown. /UPDATE=1: start the game again afterwards; /UPDATE=2: do not (an update applied as the game closes, or
// the updater run on its own with --update-only=apply). Up to 0.76 the game quit first and a PowerShell script ran this
// installer with /UPDATE=1 once the game had gone; both ways work.
function StartedByGame: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') <> '0';
end;

function IsSelfUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

// True when nothing holds the game's executable, so that it can be replaced.
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

function GameRunning(const Exe: String): Boolean;
begin
  Result := CheckForMutexes('{#GameMutex}') or not ExeFree(Exe);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode, Waited: Integer;
  Exe: String;
begin
  Exe := ExpandConstant('{app}\{#MyAppExeName}');
  Waited := 0;
  // Started by the game: it is on its way out (saving, letting go of its lock; its executable stays held for a second
  // or two after its process ends), so it is waited for rather than killed. A copy still there after a minute, or any
  // copy when the installer was started by hand, is closed as before.
  if StartedByGame then
    while GameRunning(Exe) and (Waited < 60000) do
    begin
      Sleep(250);
      Waited := Waited + 250;
    end;
  Log(Format('Waited %d ms for the game to exit', [Waited]));
  if GameRunning(Exe) then
  begin
    Log('The game is still running: closing it');
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#MyAppExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(500);
  end;
  Result := '';
end;
