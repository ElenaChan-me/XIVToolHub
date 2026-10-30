; XIVToolHub のインストーラー（Inno Setup 6）
; scripts/build-installer.ps1 から、バージョンと exe の場所を渡してビルドする。
; 管理者の権限なしで利用者のフォルダに入れるので、XIVToolHub 自身の更新（exe の入れ替え）もそのままできる。

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\publish\win-x64\XIVToolHub.exe"
#endif
#ifndef OutputDir
  #define OutputDir "..\publish"
#endif

; 自己更新のときに「アプリと機能」の版を書き換えるため、SelfUpdater.InstallerAppId と同じ値にする
#define AppId "316C7E01-90F4-4529-AB38-66106DD0AC6B"

[Setup]
AppId={{{#AppId}}
AppName=XIVToolHub
AppVersion={#AppVersion}
AppVerName=XIVToolHub {#AppVersion}
AppPublisher=ElenaChan-me
AppPublisherURL=https://github.com/ElenaChan-me/XIVToolHub
AppSupportURL=https://github.com/ElenaChan-me/XIVToolHub/issues
AppUpdatesURL=https://github.com/ElenaChan-me/XIVToolHub/releases
DefaultDirName={localappdata}\Programs\XIVToolHub
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=XIVToolHub-Setup
UninstallDisplayIcon={app}\XIVToolHub.exe
UninstallDisplayName=XIVToolHub
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; 更新でインストーラーを実行したとき、起動中の XIVToolHub を閉じてから入れ替える
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
japanese.LaunchHub=XIVToolHub を起動して、使うツールを選ぶ
english.LaunchHub=Launch XIVToolHub and choose the tools to install
japanese.DataKept=XIVToolHub でインストールしたツール（ACT・Triggevent Plus など）と設定は、%n%1%nに残っています。不要な場合は、このフォルダを削除してください。
english.DataKept=Tools installed with XIVToolHub (ACT, Triggevent Plus, ...) and settings are kept in%n%1%nDelete this folder if you no longer need them.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\XIVToolHub"; Filename: "{app}\XIVToolHub.exe"
Name: "{autodesktop}\XIVToolHub"; Filename: "{app}\XIVToolHub.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\XIVToolHub.exe"; Description: "{cm:LaunchHub}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 自己更新で退避した古い exe
Type: files; Name: "{app}\XIVToolHub.exe.old"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // ツールや設定はアンインストールで消さない（ゲームのプラグインの登録などが残ったまま消えると困るため）。場所だけ伝える
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    MsgBox(FmtMessage(CustomMessage('DataKept'), [ExpandConstant('{localappdata}\XIVToolHub')]), mbInformation, MB_OK);
end;
