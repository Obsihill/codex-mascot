#ifndef SourceDir
  #error SourceDir must point to a self-contained win-x64 publish folder
#endif
#ifndef OutputDir
  #error OutputDir must be specified
#endif
#ifndef SetupIconPath
  #error SetupIconPath must point to the app icon file
#endif
#ifndef AppVersion
  #define AppVersion "0.2.5"
#endif

[Setup]
AppId={{15E76B2F-0E2F-42C4-BB49-63D0AFC0E721}
AppName=Agent Mascot
AppVersion={#AppVersion}
AppPublisher=Agent Mascot contributors
AppPublisherURL=https://github.com/Obsihill/codex-mascot
AppSupportURL=https://github.com/Obsihill/codex-mascot/issues
DefaultDirName={localappdata}\Programs\Agent Mascot
DefaultGroupName=Agent Mascot
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
AppMutex=Local\CodexMascot.v02
UninstallDisplayName=Agent Mascot
UninstallDisplayIcon={app}\AgentMascot.exe
SetupIconFile={#SetupIconPath}
LicenseFile={#SourceDir}\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=AgentMascot-Setup-{#AppVersion}-win-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
LanguageDetectionMethod=uilanguage
ShowLanguageDialog=auto
CloseApplications=yes
RestartApplications=no
VersionInfoProductName=Agent Mascot
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[CustomMessages]
english.RemovalDataWarning=Uninstall also permanently deletes settings, logs and registered mascot copies stored inside the installation folder. Original files outside that folder are not deleted. Continue?
korean.RemovalDataWarning=제거하면 설치 폴더 안의 설정, 로그, 등록한 마스코트 복사본도 영구 삭제됩니다. 설치 폴더 밖의 원본 파일은 삭제하지 않습니다. 계속할까요?
english.DesktopShortcut=Create a desktop shortcut
korean.DesktopShortcut=바탕 화면에 바로 가기 만들기
english.Shortcuts=Additional shortcuts:
korean.Shortcuts=추가 바로 가기:
english.Repair=Repair the existing installation
korean.Repair=기존 설치 복구
english.Remove=Uninstall Agent Mascot
korean.Remove=Agent Mascot 제거
english.Maintenance=Existing installation:
korean.Maintenance=기존 설치 관리:
english.RepairConflict=Choose either repair or uninstall, not both.
korean.RepairConflict=복구와 제거 중 하나만 선택해 주세요.
english.MissingUninstaller=The uninstaller was not found. Uninstall Agent Mascot from Installed apps in Windows Settings.
korean.MissingUninstaller=기존 설치의 제거 프로그램을 찾을 수 없습니다. Windows 설정의 설치된 앱에서 Agent Mascot을 제거해 주세요.
english.UninstallFailed=Could not start the uninstaller. Uninstall Agent Mascot from Installed apps in Windows Settings.
korean.UninstallFailed=제거 마법사를 실행하지 못했습니다. Windows 설정의 설치된 앱에서 Agent Mascot을 제거해 주세요.
english.UninstallCancelled=Uninstall was cancelled. Setup will now close.
korean.UninstallCancelled=제거가 취소되었습니다. 설치 프로그램을 종료합니다.
english.UninstallComplete=Agent Mascot was uninstalled. Setup will now close.
korean.UninstallComplete=Agent Mascot이 제거되었습니다. 설치 프로그램을 종료합니다.
english.Launch=Launch Agent Mascot
korean.Launch=Agent Mascot 실행
english.Startup=Start when I sign in to Windows
korean.Startup=Windows 로그인 시 시작

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:Shortcuts}"; Flags: unchecked
Name: "startup"; Description: "{cm:Startup}"; Flags: unchecked
Name: "repair"; Description: "{cm:Repair}"; GroupDescription: "{cm:Maintenance}"; Check: ExistingInstallationDetected; Flags: unchecked
Name: "remove"; Description: "{cm:Remove}"; GroupDescription: "{cm:Maintenance}"; Check: ExistingInstallationDetected; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Agent Mascot"; Filename: "{app}\AgentMascot.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Agent Mascot"; Filename: "{app}\AgentMascot.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[InstallDelete]
Type: files; Name: "{autoprograms}\Agent Mascot 제거.lnk"
Type: files; Name: "{autoprograms}\Uninstall Agent Mascot.lnk"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CodexMascot"; ValueData: """{app}\AgentMascot.exe"" --tray"; Tasks: startup; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "CodexMascot"; Tasks: not startup; Flags: deletevalue

[Code]
var
  ExistingInstallDir: string;
  ForceClose: Boolean;
  CleanupRoot: string;

function IsSafeCleanupRoot(const Root: string): Boolean;
var
  Current, Parent: string;
  Info: TFindRec;
begin
  Result := False;
  if (Root = '') or (Length(Root) <= 3) then Exit;
  if (CompareText(Root, ExpandConstant('{win}')) = 0) or
     (CompareText(Root, ExpandConstant('{sys}')) = 0) or
     (CompareText(Root, RemoveBackslashUnlessRoot(GetEnv('USERPROFILE'))) = 0) or
     (CompareText(Root, RemoveBackslashUnlessRoot(GetEnv('LOCALAPPDATA'))) = 0) or
     (CompareText(Root, RemoveBackslashUnlessRoot(GetEnv('APPDATA'))) = 0) or
     (CompareText(Root, ExpandConstant('{autopf}')) = 0) or
     (CompareText(Root, AddBackslash(GetEnv('LOCALAPPDATA')) + 'Programs') = 0) then Exit;
  // Never traverse a junction/symlink in the installation path.
  Current := Root;
  while Length(Current) > 3 do
  begin
    if not FindFirst(Current, Info) then Exit;
    try
      if (Info.Attributes and FILE_ATTRIBUTE_REPARSE_POINT) <> 0 then Exit;
    finally
      FindClose(Info);
    end;
    Parent := RemoveBackslashUnlessRoot(ExtractFileDir(Current));
    if Parent = Current then Exit;
    Current := Parent;
  end;
  Result := True;
end;

function InitializeUninstall(): Boolean;
begin
  CleanupRoot := RemoveBackslashUnlessRoot(ExpandFileName(ExpandConstant('{app}')));
  if not IsSafeCleanupRoot(CleanupRoot) then
  begin
    Log('Skipping additional data cleanup: unsafe installation path.');
    CleanupRoot := '';
  end;
  Result := True;
  if (CleanupRoot <> '') and not UninstallSilent then
    Result := MsgBox(CustomMessage('RemovalDataWarning'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure RemoveOwnedData(const Name: string);
var
  Target: string;
begin
  if (CleanupRoot = '') or not IsSafeCleanupRoot(CleanupRoot) then Exit;
  // Only literal app-owned child directories are passed here. Never delete
  // {app} recursively, external media paths, or shared fallback AppData.
  Target := AddBackslash(CleanupRoot) + Name;
  if DirExists(Target) and not DelTree(Target, True, True, True) then
    Log('Could not fully remove application data: ' + Target);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // Clear generated data before Inno removes tracked files and directories,
  // so its normal final cleanup can also remove the application directory.
  if CurUninstallStep = usUninstall then
  begin
    RemoveOwnedData('config');
    RemoveOwnedData('library');
    RemoveOwnedData('logs');
    RemoveOwnedData('work');
    RemoveOwnedData('assets');
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    // Unknown user files are preserved; remove the installation folder only
    // when empty. Inno also removes its own remaining uninstaller files.
    if (CleanupRoot <> '') and IsSafeCleanupRoot(CleanupRoot) then
      RemoveDir(CleanupRoot);
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  ExistingInstallDir := '';
  ForceClose := False;

  if not RegQueryStringValue(HKCU64,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\{15E76B2F-0E2F-42C4-BB49-63D0AFC0E721}_is1',
    'Inno Setup: App Path', ExistingInstallDir) and
     not RegQueryStringValue(HKCU64,
      'Software\Microsoft\Windows\CurrentVersion\Uninstall\{15E76B2F-0E2F-42C4-BB49-63D0AFC0E721}_is1',
    'InstallLocation', ExistingInstallDir) and
     not RegQueryStringValue(HKCU32,
      'Software\Microsoft\Windows\CurrentVersion\Uninstall\{15E76B2F-0E2F-42C4-BB49-63D0AFC0E721}_is1',
      'Inno Setup: App Path', ExistingInstallDir) and
     not RegQueryStringValue(HKCU32,
      'Software\Microsoft\Windows\CurrentVersion\Uninstall\{15E76B2F-0E2F-42C4-BB49-63D0AFC0E721}_is1',
      'InstallLocation', ExistingInstallDir) and
     not RegQueryStringValue(HKLM64,
      'Software\Microsoft\Windows\CurrentVersion\Uninstall\{15E76B2F-0E2F-42C4-BB49-63D0AFC0E721}_is1',
      'Inno Setup: App Path', ExistingInstallDir) and
     not RegQueryStringValue(HKLM32,
      'Software\Microsoft\Windows\CurrentVersion\Uninstall\{15E76B2F-0E2F-42C4-BB49-63D0AFC0E721}_is1',
      'Inno Setup: App Path', ExistingInstallDir) then
    ExistingInstallDir := '';

  if (ExistingInstallDir <> '') and not DirExists(ExistingInstallDir) then
    ExistingInstallDir := '';
end;

function ExistingInstallationDetected(): Boolean;
begin
  Result := ExistingInstallDir <> '';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  UninstallerPath: string;
  ResultCode: Integer;
begin
  Result := True;
  if (CurPageID = wpSelectTasks) and ExistingInstallationDetected() then
  begin
    if WizardIsTaskSelected('repair') and WizardIsTaskSelected('remove') then
    begin
      MsgBox(CustomMessage('RepairConflict'), mbError, MB_OK);
      Result := False;
      Exit;
    end;

    if WizardIsTaskSelected('remove') then
    begin
      UninstallerPath := AddBackslash(ExistingInstallDir) + 'unins000.exe';
      if not FileExists(UninstallerPath) then
      begin
        MsgBox(CustomMessage('MissingUninstaller'),
          mbError, MB_OK);
        Result := False;
        Exit;
      end;

      if not Exec(UninstallerPath, '', ExistingInstallDir, SW_SHOW, ewWaitUntilTerminated, ResultCode) then
      begin
        MsgBox(CustomMessage('UninstallFailed'),
          mbError, MB_OK);
        Result := False;
        Exit;
      end;

      if ResultCode <> 0 then
        MsgBox(CustomMessage('UninstallCancelled'), mbInformation, MB_OK)
      else
        MsgBox(CustomMessage('UninstallComplete'), mbInformation, MB_OK);
      ForceClose := True;
      WizardForm.Close;
      Result := False;
    end;
  end;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  if ForceClose then
    Confirm := False;
end;

[Run]
Filename: "{app}\AgentMascot.exe"; Description: "{cm:Launch}"; WorkingDir: "{app}"; Flags: postinstall nowait skipifsilent
