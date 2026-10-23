#define MyAppName "DiskMap"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "DiskMap Project"
#define MyAppExeName "DiskMap.exe"

[Setup]
AppId={{B5E2A0D4-6C3F-4B7A-9E81-2F5C7A9D1E63}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\DiskMap
DefaultGroupName=DiskMap
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=DiskMap-Setup-{#MyAppVersion}
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
RestartApplications=no
VersionInfoVersion=1.0.0.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=DiskMap - Disk Space Map
VersionInfoProductName={#MyAppName}
LicenseFile=..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
Source: "..\publish\DiskMap.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\DiskMap"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,DiskMap}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\DiskMap"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,DiskMap}"; Flags: nowait postinstall skipifsilent
