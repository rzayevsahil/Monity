; Monity Inno Setup Script
; MyAppVersion: build-release.ps1 csproj'daki Version'i okuyup /DMyAppVersion= ile verir.
; IDE'den tek basina derlersen asagidaki varsayilan kullanilir.

#define MyAppName "Monity"
#ifndef MyAppVersion
#define MyAppVersion "0.0.0"
#endif
#define MyAppPublisher "Monity"
#define MyAppURL "https://github.com/rzayevsahil/Monity"
#define MyAppExeName "Monity.App.exe"

; Publish klasörleri (installer/ klasörüne göre)
#define AppPublishDir "..\src\Monity.App\bin\Release\net8.0-windows\win-x64\publish"
#define UpdaterExe "..\src\Monity.Updater\bin\Release\net8.0-windows\win-x64\publish\Monity.Updater.exe"

[Setup]
AppId={{A8B9C7D6-E5F4-3210-ABCD-1234567890AB}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={localappdata}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=Output
OutputBaseFilename=Monity-Setup-{#MyAppVersion}
SetupIconFile=..\src\Monity.App\Assets\AppIcon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1; Check: not IsAdminInstallMode

[Files]
; Uygulama publish klasöründeki tüm dosyalar
Source: "{#AppPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Updater.exe (Monity.Updater.exe -> Updater.exe olarak kopyala)
Source: "{#UpdaterExe}"; DestDir: "{app}"; DestName: "Updater.exe"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: quicklaunchicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: dirifempty; Name: "{app}"
