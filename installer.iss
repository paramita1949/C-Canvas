; Canvas Cast 安装脚本
; 使用 Inno Setup 编译此文件即可生成安装包

#define MyAppName "Canvas Cast"
#define MyAppVersion "4.0"
#define MyAppPublisher "Canvas Cast"
#define MyAppURL "https://your-website.com"
#define MyAppExeName "Canvas Cast.exe"

[Setup]
; 应用程序基本信息
AppId={{A1B2C3D4-E5F6-4789-A012-3456789ABCDE}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=installer_output
OutputBaseFilename=CanvasCast_v{#MyAppVersion}_Setup
SetupIconFile=baodian.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; 需要管理员权限（如果需要写入 Program Files）
PrivilegesRequired=admin
; 64位安装模式
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; 卸载图标
UninstallDisplayIcon={app}\{#MyAppExeName}
; 许可协议（可选）
; LicenseFile=LICENSE.txt
; 安装前后的图片（可选）
; WizardImageFile=installer-image.bmp
; WizardSmallImageFile=installer-small.bmp

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标:"
Name: "quicklaunchicon"; Description: "创建快速启动栏快捷方式"; GroupDescription: "附加图标:"; Flags: unchecked

[Files]
; 主程序和所有依赖文件
Source: "bin\Release\net8.0-windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; 注意：Resources.pak 会自动包含在上面的通配符中

[Icons]
; 开始菜单图标
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
; 桌面图标
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
; 快速启动栏图标
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: quicklaunchicon

[Run]
; 安装完成后询问是否运行程序
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
// 检查 .NET 8.0 Runtime 是否已安装
function IsDotNet8Installed: Boolean;
var
  ResultCode: Integer;
begin
  // 尝试检测 .NET 8.0 runtime
  Result := Exec('dotnet', '--list-runtimes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if not Result then
  begin
    // 如果 dotnet 命令不存在，则未安装
    Result := False;
  end;
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  
  // 检查 .NET 8.0 运行时
  if not IsDotNet8Installed then
  begin
    if MsgBox('此程序需要 .NET 8.0 Desktop Runtime。' + #13#10 + 
              '是否要前往下载页面？', mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/8.0', '', '', SW_SHOW, ewNoWait, ResultCode);
    end;
    Result := False;
  end;
end;

