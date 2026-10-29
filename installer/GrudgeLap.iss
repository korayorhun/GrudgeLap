; Inno Setup script for GrudgeLap (per-user install, no admin rights needed).
; The exe and its data folder come from tools/build.ps1 (Godot export into ..\build).
#define MyAppName "GrudgeLap"
#define MyAppVersion "0.8.1"
#define MyAppPublisher "Koray Orhun"
#define MyAppURL "https://github.com/korayorhun/GrudgeLap"
#define MyAppExeName "GrudgeLap.exe"

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
turkish.DesktopTask=MasaÃ¼stÃ¼ kÄ±sayolu oluÅŸtur
turkish.RunNow=GrudgeLap'i ÅŸimdi baÅŸlat
turkish.Uninstall=KaldÄ±r

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
// The game's updater runs the new installer with /UPDATE=1 and quits; the installer then relaunches the game.
function IsSelfUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#MyAppExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
  Result := '';
end;
