; OpenGG Inno Setup Script
; Builds a complete standalone Windows installer with all dependencies, DLLs, and icons

#define MyAppName "OpenGG"
#ifndef MyAppVersion
#define MyAppVersion "0.1.0"
#endif
#define MyAppPublisher "GUHPY"
#define MyAppURL "https://github.com/GUHPY/OpenGG"
#define MyAppExeName "OpenGG.exe"

#ifdef Minimal
#define MyAppOutputBase "OpenGG-" + MyAppVersion + "-Setup-Lite"
#define SourceDir "..\artifacts\portable-minimal"
#else
#define MyAppOutputBase "OpenGG-" + MyAppVersion + "-Setup"
#define SourceDir "..\artifacts\portable"
#endif

[Setup]
AppId={{E68A8D5B-4731-4F29-87D9-B7C9337F94A2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir=..\artifacts
OutputBaseFilename={#MyAppOutputBase}
SetupIconFile=..\src\OpenGG.Desktop\Assets\Brand\OpenGG.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardImageFile=installer-assets\wizard.bmp
WizardSmallImageFile=installer-assets\wizard-small.bmp
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
RestartApplications=no
DisableWelcomePage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

#ifndef Minimal
[Types]
Name: "full"; Description: "Full installation (Application + Offline Research Tools)"
Name: "compact"; Description: "Standard installation (OpenGG Application only)"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "app"; Description: "OpenGG Desktop Application (Self-contained .NET 10 Runtime, Native DLLs, Driverless HID)"; Types: full compact custom; Flags: fixed
Name: "research"; Description: "Offline Protocol Research Tools (Wireshark, USBPcap, WinSDK, Python & Frida installers)"; Types: full
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
#ifdef Minimal
; Standalone application and self-contained .NET 10 / WPF runtime DLLs
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#else
; Main application files and all self-contained .NET 10 / WPF runtime DLLs
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: app; Excludes: "tooling\installers\*,tooling\runtime\*"
; Optional offline protocol research tools and offline installers
Source: "{#SourceDir}\tooling\installers\*"; DestDir: "{app}\tooling\installers"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: research
#endif

[Icons]
Name: "{autoprograms}\{#MyAppName}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autoprograms}\{#MyAppName}\OpenGG Documentation"; Filename: "{app}\README.md"
Name: "{autoprograms}\{#MyAppName}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\tooling\runtime"
