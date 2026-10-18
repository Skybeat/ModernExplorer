; Inno Setup 6 Script for Modern Explorer Studio — Rust Edition (Windows x64 & ARM64)
#ifndef AppVersion
  #define AppVersion "1.9.8"
#endif
#ifndef TargetRid
  #define TargetRid "win-x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\rust\dist\stage-win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\rust\dist"
#endif

[Setup]
AppId={{C94E7A12-8D41-4F3A-9B25-7E1A3D94F6B2}
AppName=Modern Explorer Studio (Rust Edition)
AppVersion={#AppVersion}
AppVerName=Modern Explorer Studio (Rust) {#AppVersion} ({#TargetRid})
AppPublisher=Skybeat
AppPublisherURL=https://github.com/Skybeat/ModernExplorer
AppSupportURL=https://github.com/Skybeat/ModernExplorer/issues
AppUpdatesURL=https://github.com/Skybeat/ModernExplorer/releases
DefaultDirName={autopf}\ModernExplorer-Rust
DefaultGroupName=Modern Explorer Studio (Rust)
UsePreviousAppDir=yes
DisableWelcomePage=no
DisableDirPage=auto
DisableProgramGroupPage=yes
DisableReadyPage=no
DisableFinishedPage=no
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutputDir}
OutputBaseFilename=ModernExplorer-Rust-Setup-{#TargetRid}
SetupIconFile=..\..\rust\modern-explorer-app\assets\AppIcon.ico
UninstallDisplayIcon={app}\ModernExplorer-Rust.exe
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
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Dirs]
Name: "{app}"; Permissions: users-modify

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Permissions: users-modify

[Icons]
Name: "{autoprograms}\Modern Explorer Studio (Rust)"; Filename: "{app}\ModernExplorer-Rust.exe"; IconFilename: "{app}\Assets\AppIcon.ico"
Name: "{autodesktop}\Modern Explorer Studio (Rust)"; Filename: "{app}\ModernExplorer-Rust.exe"; IconFilename: "{app}\Assets\AppIcon.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\ModernExplorer-Rust.exe"; Description: "{cm:LaunchProgram,Modern Explorer Studio (Rust)}"; Flags: nowait postinstall skipifsilent

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM ModernExplorer-Rust.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(300);
  Result := '';
end;

