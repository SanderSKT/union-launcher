#define MyAppName "UNION Launcher"
#ifndef MyAppVersion
#define MyAppVersion "0.2.5"
#endif
#define MyAppExeName "UnionLauncher.exe"

[Setup]
AppId={{C4ABDD40-8A59-4DE9-9DB9-A6136BD0246B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoTextVersion={#MyAppVersion}
VersionInfoProductTextVersion={#MyAppVersion}
AppPublisher=UNION
AppPublisherURL=https://union.cool
AppSupportURL=https://union.cool
AppUpdatesURL=https://union.cool
DefaultDirName={localappdata}\Programs\UNION Launcher
DefaultGroupName=UNION
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=output
OutputBaseFilename=UNIONSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupLogging=yes
UsePreviousAppDir=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "payload\UnionLauncher.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\UNION Launcher"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\UNION Launcher"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch UNION Launcher"; Flags: nowait postinstall skipifsilent

[Code]
const
  ProtocolKey = 'Software\Classes\union';
  ProtocolCommandKey = 'Software\Classes\union\shell\open\command';
  InstallerStateKey = 'Software\UNION\LauncherInstaller';

function InstalledCommand: String;
begin
  Result := '"' + ExpandConstant('{app}\UnionLauncher.exe') + '" "%1"';
end;

function InitializeSetup: Boolean;
var
  Current: String;
  Recorded: String;
begin
  Result := True;
  if RegQueryStringValue(HKCU, ProtocolCommandKey, '', Current) then
  begin
    // Upgrades of our own installed handler need no additional prompt.
    if RegQueryStringValue(HKCU, InstallerStateKey, 'InstalledCommand', Recorded) then
      if CompareText(Current, Recorded) = 0 then
        Exit;

    // Existing PowerShell/temporary union:// handlers are left untouched
    // if the user cancels; with consent they will be backed up.
    if WizardSilent then
    begin
      Result := False;
      Exit;
    end;
    Result := MsgBox(
      'A union:// launcher is already registered for this Windows user.' + #13#10 +
      'UNION Setup will replace it with the new permanent installation.' + #13#10 +
      'On uninstall the previous handler will be restored.' + #13#10#13#10 +
      'Continue?',
      mbConfirmation, MB_YESNO) = IDYES;
  end;
end;

procedure CapturePreviousHandler;
var
  Current: String;
  Value: String;
begin
  // Save only once; an upgrade must never overwrite the original handler.
  if RegQueryStringValue(HKCU, InstallerStateKey, 'BackupCaptured', Value) then
    Exit;
  RegWriteStringValue(HKCU, InstallerStateKey, 'BackupCaptured', '1');
  if RegQueryStringValue(HKCU, ProtocolCommandKey, '', Current) then
  begin
    RegWriteStringValue(HKCU, InstallerStateKey, 'HadPrevious', '1');
    RegWriteStringValue(HKCU, InstallerStateKey, 'PreviousCommand', Current);
    if RegQueryStringValue(HKCU, ProtocolKey, '', Value) then
      RegWriteStringValue(HKCU, InstallerStateKey, 'PreviousDescription', Value);
  end
  else
    RegWriteStringValue(HKCU, InstallerStateKey, 'HadPrevious', '0');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Command: String;
begin
  if CurStep <> ssPostInstall then
    Exit;

  CapturePreviousHandler;
  Command := InstalledCommand;
  RegWriteStringValue(HKCU, ProtocolKey, '', 'URL:UNION Launcher');
  RegWriteStringValue(HKCU, ProtocolKey, 'URL Protocol', '');
  RegWriteStringValue(HKCU, ProtocolCommandKey, '', Command);
  RegWriteStringValue(HKCU, InstallerStateKey, 'InstalledCommand', Command);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Current: String;
  Ours: String;
  HadPrevious: String;
  Previous: String;
  Description: String;
begin
  if CurUninstallStep <> usUninstall then
    Exit;

  // If another application has since taken over union://, do not touch it.
  if not RegQueryStringValue(HKCU, ProtocolCommandKey, '', Current) then
    Exit;
  if not RegQueryStringValue(HKCU, InstallerStateKey, 'InstalledCommand', Ours) then
    Exit;
  if CompareText(Current, Ours) <> 0 then
    Exit;

  if RegQueryStringValue(HKCU, InstallerStateKey, 'HadPrevious', HadPrevious) and
    (HadPrevious = '1') and
    RegQueryStringValue(HKCU, InstallerStateKey, 'PreviousCommand', Previous) then
  begin
    RegWriteStringValue(HKCU, ProtocolCommandKey, '', Previous);
    if RegQueryStringValue(HKCU, InstallerStateKey, 'PreviousDescription', Description) then
      RegWriteStringValue(HKCU, ProtocolKey, '', Description);
    RegWriteStringValue(HKCU, ProtocolKey, 'URL Protocol', '');
  end
  else
  begin
    RegDeleteKeyIncludingSubkeys(HKCU, ProtocolKey);
  end;
  RegDeleteKeyIncludingSubkeys(HKCU, InstallerStateKey);

  // Never remove %LOCALAPPDATA%\UNION\Versions, keys or player data.
end;
