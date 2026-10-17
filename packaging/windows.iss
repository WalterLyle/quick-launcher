#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{C2A25F1C-E05A-4CC3-B73E-313110EDC078}
AppName=快捷启动器
AppVersion={#AppVersion}
AppVerName=快捷启动器 {#AppVersion}
AppPublisher=QuickLauncher contributors
DefaultDirName={localappdata}\Programs\QuickLauncher
DefaultGroupName=快捷启动器
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist\release
OutputBaseFilename=QuickLauncher-{#AppVersion}-windows-setup
SetupIconFile=..\assets\QuickLauncher-flat-1.ico
UninstallDisplayIcon={app}\QuickLauncher.exe
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0
CloseApplications=yes
RestartApplications=no
AppMutex=QuickLauncher.SingleInstance
UninstallDisplayName=快捷启动器

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\dist\QuickLauncher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\快捷启动器"; Filename: "{app}\QuickLauncher.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\快捷启动器"; Filename: "{app}\QuickLauncher.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\QuickLauncher.exe"; Description: "Launch QuickLauncher"; Flags: nowait postinstall skipifsilent

; Never install or uninstall anything in the user's QuickLauncher data directory.
