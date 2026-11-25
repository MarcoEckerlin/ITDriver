; ITDriver installer (Inno Setup 6). Built by GitHub Actions:
;   iscc /DMyAppVersion=1.2.3 installer\ITDriver.iss   (expects the published app in ..\publish)
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

[Setup]
AppId={{8C3E3B5D-6F0A-4C2B-9E61-1D2A4F7B9C10}
AppName=ITDriver
AppVersion={#MyAppVersion}
AppVerName=ITDriver {#MyAppVersion}
AppPublisher=ITDriver (Open Source)
AppPublisherURL=https://github.com/MarcoEckerlin/ITDriver
AppSupportURL=https://github.com/MarcoEckerlin/ITDriver/issues
AppUpdatesURL=https://github.com/MarcoEckerlin/ITDriver/releases
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\ITDriver
DefaultGroupName=ITDriver
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=ITDriver-Setup
SetupIconFile=..\src\ITDriver.App\Assets\itdriver.ico
UninstallDisplayIcon={app}\ITDriver.exe
UninstallDisplayName=ITDriver
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\ITDriver"; Filename: "{app}\ITDriver.exe"
Name: "{autodesktop}\ITDriver"; Filename: "{app}\ITDriver.exe"; Tasks: desktopicon

[Run]
; Interactive install: offer to start ITDriver. Silent self-update: restart it automatically.
Filename: "{app}\ITDriver.exe"; Description: "{cm:LaunchProgram,ITDriver}"; Flags: nowait postinstall skipifsilent shellexec
Filename: "{app}\ITDriver.exe"; Parameters: "--updated"; Flags: nowait shellexec; Check: WizardSilent

[UninstallRun]
; Remove the scheduled background scan.
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""ITDriver\Treiber-Scan"" /F"; Flags: runhidden; RunOnceId: "RemoveScanTask"

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\ITDriver\Logs"
