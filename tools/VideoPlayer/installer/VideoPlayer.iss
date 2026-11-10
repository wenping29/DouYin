#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "VideoPlayer"
; The build script publishes to a fresh empty directory and passes /DPublishDir,
; avoiding the MSBuild CopyParallel stack overflow on reused output folders.
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif

[Setup]
AppId={{B1E2A68D-90A3-4E65-9F1A-5A8D47D3034A}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=VideoPlayer
DefaultDirName={autopf}\VPlayer
DefaultGroupName=VideoPlayer
UninstallDisplayIcon={app}\VideoPlayer.exe
SetupIconFile=..\src\VideoPlayer\Assets\app.ico
OutputDir=..\artifacts\installer
OutputBaseFilename=VideoPlayer-Setup-{#AppVersion}-win-x64
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.17763
PrivilegesRequired=admin
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
DisableProgramGroupPage=yes
Uninstallable=yes

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\VideoPlayer"; Filename: "{app}\VideoPlayer.exe"
Name: "{autodesktop}\VideoPlayer"; Filename: "{app}\VideoPlayer.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\VideoPlayer.exe"; Parameters: "--configure-default-player"; Description: "注册 VideoPlayer 并打开 Windows 默认应用设置"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{app}\VideoPlayer.exe"; Parameters: "--unregister-file-associations"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterVideoPlayerFileAssociations"
