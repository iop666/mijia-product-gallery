; 米家产品示例图库 安装包脚本（Inno Setup 6）
; 构建示例：
;   ISCC.exe /DPublishDir=D:\...\publish /DOutputDir=D:\...\0.1.0 installer.iss

#ifndef AppName
#define AppName "米家产品示例图库"
#endif
#ifndef AppExeName
#define AppExeName "MijiaProductGallery.App.exe"
#endif
#ifndef AppVersion
#define AppVersion "0.1.0"
#endif
#ifndef PublishDir
#define PublishDir "publish"
#endif
#ifndef OutputDir
#define OutputDir "Output"
#endif

[Setup]
AppId={{7E1A2C64-9B3D-4E8F-A6C1-0D4F2B8E51AA}}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher=mijia-product-gallery
DefaultDirName={autopf}\MijiaProductGallery
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=MijiaProductGallery-{#AppVersion}-Setup
SetupIconFile=..\src\MijiaProductGallery.App\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式(&D)"; GroupDescription: "附加任务："

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\卸载 {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "立即运行 {#AppName}"; Flags: nowait postinstall skipifsilent

; 卸载不删除用户数据：Data 目录为运行期生成，Inno 卸载仅移除安装期文件。