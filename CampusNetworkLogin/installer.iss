; Inno Setup 脚本 - Drcom .NET for JLU
; 使用 Inno Setup 6 编译
;
; 方案说明：
;   1. 应用自身包含所有需要的 .NET 的组件（Trimmed Self-Contained 单文件）
;   2. 不再依赖任何外部 .NET Runtime 安装包，不需要进行额外的网络下载或捆绑
;   3. 通过 LZMA2/Max 和应用程序内置压缩使得体积远小于之前捆绑了 50MB 依赖环境的方案

#define MyAppName "Drcom .NET for JLU"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "CampusNetworkLogin"
#define MyAppURL ""
#define MyAppExeName "Drcom NET.exe"

[Setup]
AppId={{F8A7B3C4-5D6E-7F8A-9B0C-1D2E3F4A5B6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Drcom NET
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
OutputDir=..
OutputBaseFilename=Drcom_NET_Setup_Standalone_v1.0
SetupIconFile=Resources\icon.ico
WizardStyle=modern
PrivilegesRequired=admin
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式:"; Flags: checkedonce

[Files]
; 框架依赖以及所有代码发布在一个文件夹中(不使用单文件，但 Self-Contained 保证无系统依赖)
Source: "bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "运行 Drcom .NET"; Flags: postinstall nowait skipifsilent unchecked

