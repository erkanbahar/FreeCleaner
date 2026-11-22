; FreeCleaner Pro - Inno Setup 6 Installer Script
; Build locally after publishing portable exe:
;   ISCC.exe /DMyAppVersion=5.1.0 /DSourceExe=..\artifacts\portable\FreeCleaner.exe FreeCleaner.iss

#ifndef MyAppVersion
  #define MyAppVersion "5.1.0"
#endif

#ifndef SourceExe
  #define SourceExe "..\artifacts\portable\FreeCleaner.exe"
#endif

#ifndef OutputDirPath
  #define OutputDirPath "..\dist"
#endif

#define MyAppName "FreeCleaner Pro"
#define MyAppPublisher "Erkan Bahar"
#define MyAppURL "https://github.com/erkanbahar/FreeCleaner"
#define MyAppExeName "FreeCleaner.exe"

[Setup]
AppId={{9F2B8B1A-4D7E-4D62-A68D-7C9E1F340440}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\FreeCleaner Pro
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDirPath}
OutputBaseFilename=FreeCleaner-Setup-win-x64-v{#MyAppVersion}
SetupIconFile=..\FreeCleaner\Assets\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "portableflag"; Description: "Enable local Portable Mode (portable.flag)"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "{#MyAppExeName}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  FlagPath: String;
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('portableflag') then
  begin
    FlagPath := ExpandConstant('{app}\portable.flag');
    SaveStringToFile(FlagPath, 'FreeCleaner Pro Portable Mode enabled by installer', False);
  end;
end;

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
