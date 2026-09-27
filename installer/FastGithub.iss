; FastGithub Windows 安装包脚本（Inno Setup 6）
; 编译示例：ISCC.exe /DMyAppVersion=2.1.6 installer\FastGithub.iss
; 打包内容取自 CI 的发布输出目录 ./publish/win-x64

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

#define MyAppName "FastGithub"
#define MyAppPublisher "FastGithub"
#define MyAppExeName "FastGithub.UI.exe"

[Setup]
AppId={{7C2E5B41-9A3D-4F18-8B6E-5D2C1A7F4E93}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=FastGithub-{#MyAppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\FastGithub.UI\app.ico

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\FastGithub"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 FastGithub"; Filename: "{uninstallexe}"
Name: "{autodesktop}\FastGithub"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 FastGithub"; Flags: nowait postinstall skipifsilent
