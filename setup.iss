; DrCom Campus - Inno Setup 安装脚本 (NativeAOT)
; 无需 .NET 运行时的 NativeAOT 应用
;
; 构建流程:
;   1. dotnet publish DrComCampus/DrComCampus.csproj -c Release -o publish_aot
;   2. ISCC.exe setup.iss

#define MyAppName "DrCom 校园网助手"
#define MyAppVersion "1.0.6"
#define MyAppPublisher "ZincGluxx"
#define MyAppExeName "DrComCampus.exe"
#define MyAppId "{{9E8C5B2A-1F3D-4A6B-8C7D-9E0F1A2B3C4D}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=.
OutputBaseFilename=DrComCampus_v{#MyAppVersion}_Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
SetupIconFile=DrComCampus\Resources\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
VersionInfoDescription={#MyAppName}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式(&D)"; GroupDescription: "附加选项："

[Files]
Source: "publish_aot\DrComCampus.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_aot\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_aot\Resources\icon.ico"; DestDir: "{app}\Resources"; Flags: ignoreversion

[InstallDelete]
Type: files; Name: "{app}\DrcomNET.exe"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 {#MyAppName}"; Flags: nowait skipifsilent postinstall

[Code]
// 安装前检测是否已安装旧版本，提示用户选择卸载或覆盖安装
function InitializeSetup: Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if RegKeyExists(HKEY_LOCAL_MACHINE, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppId}_is1') then
  begin
    if MsgBox('检测到已安装的旧版本，是否先卸载旧版本再安装新版本？' + #13#10 +
              '选择"否"将直接覆盖安装。', mbConfirmation, MB_YESNO) = IDYES then
    begin
      if not Exec(ExpandConstant('{uninstallexe}'), '/SILENT', '', SW_SHOW,
                  ewWaitUntilTerminated, ResultCode) then
      begin
        MsgBox('无法启动旧版本卸载程序，请手动卸载后重试。', mbError, MB_OK);
        Result := False;
      end;
    end;
  end;
end;

// 安装前确保程序未运行
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if CheckForMutexes('DrComCampus-SingleInstance') or CheckForMutexes('DrcomNET-SingleInstance') then
  begin
    Exec(ExpandConstant('{cmd}'), '/C taskkill /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{cmd}'), '/C taskkill /IM DrcomNET.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(500);
    if CheckForMutexes('DrComCampus-SingleInstance') then
      Exec(ExpandConstant('{cmd}'), '/C taskkill /IM {#MyAppExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    if CheckForMutexes('DrcomNET-SingleInstance') then
      Exec(ExpandConstant('{cmd}'), '/C taskkill /IM DrcomNET.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
