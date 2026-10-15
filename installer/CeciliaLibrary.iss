; 塞西莉亚的图书馆安装包。由 scripts\build.ps1 -Installer 传入版本、运行时与格式登记后编译。
#ifndef AppVersion
  #error 请通过 scripts\build.ps1 -Installer 编译
#endif

#define AppName "塞西莉亚的图书馆"
#define AppExe "LightReader.exe"
#define ProgId "CeciliaLibrary.Document"
#define Capabilities "Software\CeciliaLibrary\Capabilities"

[Setup]
AppId={{7C1D3B52-4E0A-4C47-9F3B-CEC111A0B001}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=1606835963aa
AppPublisherURL=https://github.com/1606835963aa/cecilia-library
AppSupportURL=https://github.com/1606835963aa/cecilia-library/issues
AppUpdatesURL=https://github.com/1606835963aa/cecilia-library/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=CeciliaLibrary-Setup-{#AppVersion}-x64
SetupIconFile={#SourceRoot}\src\LightReader.App\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
ChangesAssociations=yes
CloseApplications=yes
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}

[Languages]
Name: "chs"; MessagesFile: "{#SourceRoot}\installer\ChineseSimplified.isl"

[CustomMessages]
chs.TaskAssociate=登记到 Windows“打开方式”列表（可在系统设置中选为默认应用）
chs.TaskDesktop=创建桌面快捷方式
chs.RuntimeTitle=正在下载 .NET 桌面运行时
chs.RuntimeDescription=软件需要微软 .NET {#RuntimeVersion} 桌面运行时，安装程序将从微软官方下载。
chs.RuntimeMemo=将从微软官方下载并安装 .NET {#RuntimeVersion} 桌面运行时（约 {#RuntimeMiB} MB），安装时 Windows 会请求一次管理员确认。
chs.RuntimeInstalling=正在安装 .NET 桌面运行时……
chs.RuntimeFailed=.NET 桌面运行时没有安装成功（代码 %1）。可以稍后从 https://dotnet.microsoft.com/download/dotnet/10.0 手动安装“.NET Desktop Runtime”，再运行本软件。
chs.WebViewMissing=这台电脑缺少 Microsoft Edge WebView2 运行时，Word、表格和网页文档将无法显示。安装完成后可以从 https://developer.microsoft.com/microsoft-edge/webview2/ 下载“常青版引导程序”补装。
chs.RemoveData=是否同时删除设置、缓存和已下载的组件？%n%n位置：%1%n%n选择“否”可在重新安装后继续使用。
chs.LaunchApp=启动塞西莉亚的图书馆

[Tasks]
Name: "associate"; Description: "{cm:TaskAssociate}"
Name: "desktopicon"; Description: "{cm:TaskDesktop}"; Flags: unchecked

[Files]
Source: "{#SourceRoot}\dist\LightReader\*"; DestDir: "{app}"; Excludes: "*.pdb,\Microsoft.Web.WebView2.*.xml"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; 安装位置固定后再登记，避免指向开发目录。
Root: HKA; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#AppExe}"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"",0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: associate
Root: HKA; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueName: ""; ValueData: "{#AppName}文档"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "{#AppName}文档"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\{#ProgId}\Application"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#AppName}"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\{#ProgId}\Application"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"",0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: associate
Root: HKA; Subkey: "Software\CeciliaLibrary"; ValueType: string; ValueName: "CeciliaLibraryRegistration"; ValueData: "1"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "{#Capabilities}"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#AppName}"; Tasks: associate
Root: HKA; Subkey: "{#Capabilities}"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "查看文字、表格、演示、PDF 和图片，提供文本基础编辑。"; Tasks: associate
Root: HKA; Subkey: "{#Capabilities}"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: associate
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#AppName}"; ValueData: "{#Capabilities}"; Flags: uninsdeletevalue; Tasks: associate
#include Associations

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[Code]
var
  RuntimeDownload: TDownloadWizardPage;
  RuntimeReady: Boolean;

function RuntimeInstalled: Boolean;
var
  Search: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\{#RuntimeMajor}.*'), Search);
  if Result then
    FindClose(Search);
end;

function WebViewInstalled: Boolean;
var
  Version: String;
begin
  Result :=
    (RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

function SkipRuntime: Boolean;
begin
  // 自动检查在隔离目录试装时使用，普通安装不会出现这个参数。
  Result := ExpandConstant('{param:SKIPRUNTIME|0}') = '1';
end;

function NeedsRuntime: Boolean;
begin
  Result := not SkipRuntime and not RuntimeInstalled;
end;

procedure InitializeWizard;
begin
  RuntimeDownload := CreateDownloadPage(CustomMessage('RuntimeTitle'), CustomMessage('RuntimeDescription'), nil);
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo;
  if MemoTasksInfo <> '' then
    Result := Result + NewLine + NewLine + MemoTasksInfo;
  if NeedsRuntime then
    Result := Result + NewLine + NewLine + CustomMessage('RuntimeMemo');
end;

function DownloadRuntime: Boolean;
begin
  DownloadTemporaryFile('{#RuntimeUrl}', 'dotnet-desktop-runtime.exe', '{#RuntimeSha256}', nil);
  Result := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and NeedsRuntime and not RuntimeReady then
  begin
    RuntimeDownload.Clear;
    RuntimeDownload.Add('{#RuntimeUrl}', 'dotnet-desktop-runtime.exe', '{#RuntimeSha256}');
    RuntimeDownload.Show;
    try
      try
        RuntimeDownload.Download;
        RuntimeReady := True;
      except
        if not RuntimeDownload.AbortedByUser then
          SuppressibleMsgBox(AddPeriod(GetExceptionMessage), mbCriticalError, MB_OK, IDOK);
        Result := False;
      end;
    finally
      RuntimeDownload.Hide;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if not NeedsRuntime then
    Exit;
  if not RuntimeReady then
  begin
    try
      RuntimeReady := DownloadRuntime;
    except
      Result := GetExceptionMessage;
      Exit;
    end;
  end;
  WizardForm.StatusLabel.Caption := CustomMessage('RuntimeInstalling');
  // 微软运行时安装器会自行请求管理员确认；3010 表示安装成功但需要重启。
  if not Exec(ExpandConstant('{tmp}\dotnet-desktop-runtime.exe'), '/install /passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;
  if ResultCode = 3010 then
    NeedsRestart := True
  else if (ResultCode <> 0) or not RuntimeInstalled then
    Result := FmtMessage(CustomMessage('RuntimeFailed'), [IntToStr(ResultCode)]);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and not WebViewInstalled then
    SuppressibleMsgBox(CustomMessage('WebViewMissing'), mbInformation, MB_OK, IDOK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataRoot: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;
  DataRoot := ExpandConstant('{localappdata}\LightReader');
  if DirExists(DataRoot) and not UninstallSilent then
    if MsgBox(FmtMessage(CustomMessage('RemoveData'), [DataRoot]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(DataRoot, True, True, True);
end;
