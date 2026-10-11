; Inno Setup 6 Script for Modern Explorer (Windows x64, ARM64, x86)
#ifndef AppVersion
  #define AppVersion "1.3.0"
#endif
#ifndef TargetRid
  #define TargetRid "win-x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\src\ModernExplorer.App\bin\x64\Release\net9.0-windows10.0.19041.0\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\dist"
#endif

[Setup]
AppId={{8F4B9A2E-5C31-4E9B-A712-9D6E4B12C8F0}
AppName=Modern Explorer
AppVersion={#AppVersion}
AppVerName=Modern Explorer {#AppVersion} ({#TargetRid})
AppPublisher=Skybeat
AppPublisherURL=https://github.com/Skybeat/ModernExplorer
AppSupportURL=https://github.com/Skybeat/ModernExplorer/issues
AppUpdatesURL=https://github.com/Skybeat/ModernExplorer/releases
DefaultDirName={autopf}\ModernExplorer
DefaultGroupName=Modern Explorer
UsePreviousAppDir=yes
DisableWelcomePage=yes
DisableDirPage=auto
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableFinishedPage=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutputDir}
OutputBaseFilename=ModernExplorer-Setup-{#TargetRid}
SetupIconFile=..\..\src\ModernExplorer.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\ModernExplorer.exe
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no
#if TargetRid == "win-x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#elif TargetRid == "win-arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; Check: not IsUpgradeInstalled

[Dirs]
Name: "{app}"; Permissions: users-modify

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Permissions: users-modify

[Icons]
Name: "{autoprograms}\Modern Explorer"; Filename: "{app}\ModernExplorer.exe"; IconFilename: "{app}\Assets\AppIcon.ico"
Name: "{autodesktop}\Modern Explorer"; Filename: "{app}\ModernExplorer.exe"; IconFilename: "{app}\Assets\AppIcon.ico"; Tasks: desktopicon

[Run]
Filename: "icacls.exe"; Parameters: """{app}"" /grant *S-1-5-32-545:(OI)(CI)M /T /C /Q"; Flags: runhidden waituntilterminated
Filename: "{app}\ModernExplorer.exe"; Description: "{cm:LaunchProgram,Modern Explorer}"; Flags: nowait postinstall

[Code]
const
  BM_CLICK = $00F5;

function IsUpgradeInstalled(): Boolean;
begin
  Result :=
    RegKeyExists(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8F4B9A2E-5C31-4E9B-A712-9D6E4B12C8F0}_is1') or
    RegKeyExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8F4B9A2E-5C31-4E9B-A712-9D6E4B12C8F0}_is1') or
    FileExists(ExpandConstant('{autopf}\ModernExplorer\ModernExplorer.exe'));
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if IsUpgradeInstalled() and ((CurPageID = wpWelcome) or (CurPageID = wpSelectDir) or (CurPageID = wpSelectTasks) or (CurPageID = wpReady)) then
  begin
    PostMessage(WizardForm.NextButton.Handle, BM_CLICK, 0, 0);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM ModernExplorer.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(400);
  Result := '';
end;


