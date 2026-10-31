; Built by installer\build.ps1, which supplies AppVersion, PublishDir and OutputDir.

#define AppName "ScreenChick"
#define AppExe "ScreenChick.exe"

[Setup]
; Upgrades find the existing install through this id, so it must never change.
AppId={{CFC88BFF-BFF3-4B95-9C85-95751FA85EC1}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=EvgLans
AppPublisherURL=https://github.com/EvgLans/ScreenChick
AppSupportURL=https://github.com/EvgLans/ScreenChick/issues
VersionInfoVersion={#AppVersion}
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
OutputDir={#OutputDir}
OutputBaseFilename=ScreenChick-Setup-{#AppVersion}
SetupIconFile=..\src\ScreenChick\app.ico
; Per-user: no admin rights, and it installs under %LOCALAPPDATA%\Programs.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
Compression=lzma2/ultra64
SolidCompression=yes
ShowLanguageDialog=no
WizardStyle=modern
WizardImageFile=wizard-large.bmp
WizardSmallImageFile=wizard-small.bmp
; The running app holds this mutex. Setup asks to quit it from the tray rather than
; killing it, because shots in the strip live only in its memory.
AppMutex=Local\ScreenChick.SingleInstance

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
english.TaskAutostart=Start ScreenChick when I sign in to Windows
russian.TaskAutostart=Запускать ScreenChick при входе в Windows
english.TaskDesktop=Create a desktop icon
russian.TaskDesktop=Создать значок на рабочем столе

[Tasks]
Name: "autostart"; Description: "{cm:TaskAutostart}"
Name: "desktopicon"; Description: "{cm:TaskDesktop}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; The same value the app's own autostart setting reads and writes.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ScreenChick"; ValueData: """{app}\{#AppExe}"""; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// Autostart may have been turned on in the app's settings, so this can't depend on the install task.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ScreenChick');
end;
