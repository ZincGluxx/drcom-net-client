; 校园网登录 - Inno Setup 安装脚本
; NativeAOT 单文件发布，无需 .NET 运行时
#define MyAppName "校园网登录"
#define MyAppVersion "1.0.4"
#define MyAppPublisher "Drcom NET"
#define MyAppURL ""
#define MyAppExeName "DrcomNET.exe"

[Setup]
AppId={{9E8C5B2A-1F3D-4A6B-8C7D-9E0F1A2B3C4D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=.
OutputBaseFilename=校园网登录_v{#MyAppVersion}_Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
SetupIconFile=CampusNetworkLogin\Resources\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："

[Files]
Source: "publish_aot\DrcomNET.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_aot\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_aot\Resources\icon.ico"; DestDir: "{app}\Resources"; Flags: ignoreversion
[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 {#MyAppName}"; Flags: postinstall nowait skipifsilent

[Code]
function InitializeSetup: Boolean;
begin
  Result := True;
end;

