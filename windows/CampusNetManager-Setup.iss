#define MyAppName "江西理工大学校园网自动登录"
#define MyAppVersion "1.4.3"

[Setup]
AppId={{B4D0B2E8-7F91-4F42-A7E9-1D7A0E5D47C1}
AppName={#MyAppName}
AppVerName={#MyAppName} {#MyAppVersion}
VersionInfoVersion=1.4.3.0
VersionInfoProductVersion=1.4.3
DefaultDirName={autopf}\CampusNetManager
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=dist
OutputBaseFilename=CampusNetManager-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=CampusNetManager\app.ico
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\CampusNetManager.exe
MinVersion=10.0
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Files]
Source: "publish\CampusNetManager.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "使用说明.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\江西理工大学校园网自动登录"; Filename: "{app}\CampusNetManager.exe"; WorkingDir: "{app}"
Name: "{group}\校园网10次网络诊断"; Filename: "{app}\CampusNetManager.exe"; Parameters: "--background --cycle-test"; WorkingDir: "{app}"
Name: "{group}\使用说明"; Filename: "{app}\使用说明.txt"
Name: "{group}\卸载江西理工大学校园网自动登录"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\CampusNetManager.exe"; Parameters: "--install-startup-task"; Flags: runhidden waituntilterminated
Filename: "{app}\CampusNetManager.exe"; Description: "立即打开江西理工大学校园网自动登录"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\CampusNetManager.exe"; Parameters: "--uninstall-startup"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveCampusNetStartup"
