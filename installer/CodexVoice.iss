#define ProductName "CODEX Voice Control"
#define ProductVersion "0.4.1"
#define ProductExe "CODEX.exe"

[Setup]
AppId={code:InstallationId}
UsePreviousLanguage=no
AppName={#ProductName}
AppVersion={#ProductVersion}
AppPublisher=Azfar99
AppCopyright=Copyright (c) 2026 Azfar99
DefaultDirName={code:DefaultFolder}
DefaultGroupName={#ProductName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\codex-voice.ico
UninstallDisplayIcon={app}\{#ProductExe}
LicenseFile=..\LICENSE.txt
OutputDir=..\releases
OutputBaseFilename=CODEX-Voice-Control-{#ProductVersion}-Windows-x64-Setup
CloseApplications=no
RestartApplications=no
Uninstallable=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop icon"; Flags: checkedonce
Name: "autostart"; Description: "Start the small voice control when I sign in"; Flags: checkedonce

[Files]
Source: "..\CODEX.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Start-CODEX.vbs"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\QUICKSTART.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\README.bn.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\GITHUB-PUBLISH.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "README.md"; DestDir: "{app}\installer"; Flags: ignoreversion
Source: "..\assets\codex-voice.ico"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "..\assets\orb-states.png"; DestDir: "{app}\assets"; Flags: ignoreversion

[Icons]
Name: "{autodesktop}\{#ProductName}"; Filename: "{app}\{#ProductExe}"; WorkingDir: "{app}"; Tasks: desktopicon; Check: not TestInstall
Name: "{group}\{#ProductName}"; Filename: "{app}\{#ProductExe}"; WorkingDir: "{app}"; Check: not TestInstall
Name: "{group}\Uninstall {#ProductName}"; Filename: "{uninstallexe}"; Check: not TestInstall

[Run]
Filename: "{app}\{#ProductExe}"; Parameters: "--configure-install {code:StartupFlag}"; Flags: runhidden waituntilterminated; Check: not TestInstall
Filename: "{app}\{#ProductExe}"; Description: "Start CODEX Voice Control"; Flags: postinstall nowait skipifsilent; Check: not TestInstall
Filename: "https://learn.chatgpt.com/docs/app/windows"; Description: "Open the official OpenAI desktop app install guide"; Flags: postinstall shellexec skipifsilent unchecked; Check: NeedsCodex

[UninstallRun]
Filename: "{app}\{#ProductExe}"; Parameters: "--shutdown"; Flags: runhidden waituntilterminated; Check: not TestInstall

[UninstallDelete]
Type: files; Name: "{app}\installer-test.flag"; Check: TestInstall

[Code]
var PrerequisiteResult: Integer;

function TestInstall: Boolean;
begin
  Result := ExpandConstant('{param:TESTMODE|0}') = '1';
end;

function InstallationId(Param: String): String;
begin
  Result := '{70E77688-01C3-4200-9056-CE0C442EF83F}';
  if TestInstall then Result := Result + '-QA';
end;

function NeedsCodex: Boolean;
begin
  Result := (not TestInstall) and ((PrerequisiteResult and 1) <> 0);
end;

function TestUninstall: Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\installer-test.flag'));
end;

function DefaultFolder(Param: String): String;
begin
  if DirExists('F:\') then Result := 'F:\CodexVoiceAssistant'
  else Result := ExpandConstant('{localappdata}\Programs\CodexVoiceControl');
end;

function StartupFlag(Param: String): String;
begin
  if WizardIsTaskSelected('autostart') then Result := '1' else Result := '0';
end;

function InitializeSetup: Boolean;
var Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 461808);
  if not Result then MsgBox('Microsoft .NET Framework 4.7.2 or newer is required. Install Windows updates first.', mbError, MB_OK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var ExitCode: Integer;
begin
  Result := '';
  if RemoveBackslashUnlessRoot(WizardDirValue) = AddBackslash(ExtractFileDrive(WizardDirValue)) then begin
    Result := 'Choose a folder for CODEX Voice Control, not a drive root.';
    Exit;
  end;
  if (not TestInstall) and FileExists(ExpandConstant('{app}\{#ProductExe}')) then begin
    Exec(ExpandConstant('{app}\{#ProductExe}'), '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
    Sleep(1200);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var ExitCode: Integer;
begin
  if (CurStep = ssPostInstall) and TestInstall then
    SaveStringToFile(ExpandConstant('{app}\installer-test.flag'), 'isolated installer validation', False)
  else if (CurStep = ssPostInstall) then begin
    Exec(ExpandConstant('{app}\{#ProductExe}'), '--probe-prerequisites', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
    PrerequisiteResult := ExitCode;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpFinished) and (not TestInstall) then begin
    if (PrerequisiteResult and 1) <> 0 then
      WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 + 'Install the OpenAI desktop app, sign in to your own account and choose Codex mode before using voice control.';
    if (PrerequisiteResult and 2) <> 0 then
      WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 + 'For wake/Stop/Send commands, add Windows English (United States) speech recognition. Manual Record still uses Codex dictation.';
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Value: String;
begin
  if (CurUninstallStep = usUninstall) and (not TestUninstall) then begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CodexVoiceCompanion', Value) then
      if Pos(Lowercase(ExpandConstant('{app}\{#ProductExe}')), Lowercase(Value)) > 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CodexVoiceCompanion');
  end;
end;
