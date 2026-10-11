; Inno Setup 6 Script for Modern Explorer (Windows x64, ARM64, x86)
#ifndef AppVersion
  #define AppVersion "1.0.2"
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
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutputDir}
OutputBaseFilename=ModernExplorer-Setup-{#TargetRid}
SetupIconFile=..\..\src\ModernExplorer.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\ModernExplorer.exe
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=yes
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
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Dirs]
Name: "{app}"; Permissions: users-modify

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Modern Explorer"; Filename: "{app}\ModernExplorer.exe"; IconFilename: "{app}\Assets\AppIcon.ico"
Name: "{autodesktop}\Modern Explorer"; Filename: "{app}\ModernExplorer.exe"; IconFilename: "{app}\Assets\AppIcon.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\ModernExplorer.exe"; Description: "{cm:LaunchProgram,Modern Explorer}"; Flags: nowait postinstall


