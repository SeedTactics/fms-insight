[Setup]
AppName=FMS Insight Mazak Proxy
AppVersion={#VERSION}
AppPublisher=SeedTactics
AppPublisherURL=https://www.seedtactics.com
AppId=FMSInsightMazakProxy
AppCopyright=Black Maple Software, LLC
VersionInfoVersion={#VERSION}

DefaultDirName={pf}\SeedTactics\FMS Insight Mazak Proxy
DefaultGroupName=SeedTactics
UninstallDisplayIcon={app}\mazak-proxy.exe
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
DisableDirPage=auto
DisableProgramGroupPage=auto
CloseApplications=yes

[Files]
Source: "bin\Release\net35\mazak-proxy.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\net35\mazak-proxy.pdb"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\net35\mazak-proxy.exe.config"; DestDir: "{app}"; Flags: ignoreversion

[Run]
Filename: {sys}\sc.exe; Parameters: "create fmsinsightmazakproxy start=auto binPath=""{app}\mazak-proxy.exe"" displayname=""SeedTactic FMS Insight Mazak Proxy"""; Flags: runhidden

[UninstallRun]
Filename: {sys}\sc.exe; Parameters: "stop fmsinsightmazakproxy" ; Flags: runhidden
Filename: {sys}\sc.exe; Parameters: "delete fmsinsightmazakproxy" ; Flags: runhidden

[Code]
var
  DatabasePage: TInputDirWizardPage;
  SqlDatabasePage: TInputQueryWizardPage;
  VersionPage: TInputOptionWizardPage;
  LogCSVPage: TInputDirWizardPage;
  StartingOffsetPage: TInputOptionWizardPage;
  DecrPriorityPage: TInputOptionWizardPage;
  configansi : AnsiString;
  configstr: String;

function IsUpgrade(): Boolean;
var
   sPrevPath: String;
begin
  sPrevPath := '';
  if not RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#emit SetupSetting("AppID")}_is1', 'UninstallString', sPrevpath) then
    RegQueryStringValue(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#emit SetupSetting("AppID")}_is1', 'UninstallString', sPrevpath);
  Result := (sPrevPath <> '');
end;

procedure InitializeWizard;
begin
  { Create page }

  if IsUpgrade() then
    exit;

  VersionPage := CreateInputOptionPage(wpSelectDir,
    'Mazak Version', 'Please select the Mazak Software Version',
    'Select the Mazak Software Version',
    True, False);
  VersionPage.Add('Version E');
  VersionPage.Add('Web');
  VersionPage.Add('Smooth');

  DatabasePage := CreateInputDirPage(VersionPage.ID,
    'Select Mazak Database Path', 'Where are the Mazak databases located?',
    'Select the folder where the Mazak transaction and read-only databases are located',
    False, '');
  DatabasePage.Add('');

  SqlDatabasePage := CreateInputQueryPage(DatabasePage.ID,
    'Select Mazak SQL Server Location', 'What computer is running Mazak Smooth?',
    'Select the DNS name or IP address of the computer running SQL Server');
  SqlDatabasePage.Add('Server Loc:', False);
  SqlDatabasePage.Values[0] := 'localhost';

  LogCSVPage := CreateInputDirPage(SqlDatabasePage.ID,
    'Select Log CSV', 'Please select the directory containing the log CSV files.',
    '', False, '');
  LogCSVPage.Add('');

  StartingOffsetPage := CreateInputOptionPage(LogCSVPage.ID,
    'Use Starting Offset',  '',
    'When creating schedules, use the starting offset from the simulation to set due dates.',
    True, False);
  StartingOffsetPage.Add('Yes');
  StartingOffsetPage.Add('No');

  DecrPriorityPage := CreateInputOptionPage(StartingOffsetPage.ID,
    'Decerment Schedule Priority',  '',
    'When downloading, decrement the priority of all existing schedules by one.',
    True, False);
  DecrPriorityPage.Add('Yes');
  DecrPriorityPage.Add('No');
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  if IsUpgrade() then
    begin
      Result := False;
      exit;
    end;

  if PageID = LogCSVPage.ID then begin
    if VersionPage.Values[0] then begin
      Result := True;
    end else begin
      Result := False;
    end;
  end;

  if PageID = DatabasePage.ID then begin
    if VersionPage.Values[2] then begin
      Result := True
    end else begin
      Result := False
    end;
  end;

  if PageID = SqlDatabasePage.ID then begin
    if VersionPage.Values[2] then begin
      Result := False
    end else begin
      Result := True
    end;
  end;
end;

procedure ReplaceSetting(key: String; value: String);
begin
  StringChangeEx(configstr, '<appSettings>',
     '<appSettings>' + #13#10 + '<add key="' + key + '" value="' + value + '"/>', False);
end;

; TODO: switch to registry
procedure SaveSettings(filename: String);
begin
  if not IsUpgrade() and LoadStringFromFile(ExpandConstant(filename), configansi) then
    begin
      configstr := String(configansi);
      if VersionPage.Values[1] then begin
        ReplaceSetting('Mazak Web Version', 'True');
        ReplaceSetting('Mazak Log CSV Path', LogCSVPage.Values[0]);
      end else begin
        ReplaceSetting('Mazak Web Version', 'False');
      end;
      if VersionPage.Values[2] then begin
        ReplaceSetting('Mazak Smooth Version', 'True');
        ReplaceSetting('Mazak Log CSV Path', LogCSVPage.Values[0]);
        ReplaceSetting('Mazak Database Path', SqlDatabasePage.Values[0]);
      end else begin
        ReplaceSetting('Mazak Smooth Version', 'False');
        ReplaceSetting('Mazak Database Path', DatabasePage.Values[0]);
      end;
      if StartingOffsetPage.Values[0] then begin
        ReplaceSetting('Mazak Use Starting Offset For Due Date', 'True');
      end else begin
        ReplaceSetting('Mazak Use Starting Offset For Due Date', 'False');
      end;
      if DecrPriorityPage.Values[0] then begin
        ReplaceSetting('Mazak Decrement Priority On Download', 'True');
      end else begin
        ReplaceSetting('Mazak Decrement Priority On Download', 'False');
      end;
      SaveStringToFile(ExpandConstant(filename), configstr, False);
    end;
end;