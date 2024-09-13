#define VERSION GetDateTimeString('yyyy.mmdd.hhnn', '', '')

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
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
DisableDirPage=auto
DisableProgramGroupPage=auto
CloseApplications=yes

[Files]
Source: "bin\Release\net3.5\publish\mazak-proxy.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\net3.5\publish\mazak-proxy.pdb"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\net3.5\publish\mazak-proxy.exe.config"; DestDir: "{app}"; Flags: ignoreversion

[Run]
Filename: {sys}\sc.exe; Parameters: "create fmsinsightmazakproxy start=auto binPath=""{app}\mazak-proxy.exe"" displayname=""SeedTactic FMS Insight Mazak Proxy"""; Flags: runhidden

[UninstallRun]
Filename: {sys}\sc.exe; Parameters: "stop fmsinsightmazakproxy" ; Flags: runhidden
Filename: {sys}\sc.exe; Parameters: "delete fmsinsightmazakproxy" ; Flags: runhidden

[Registry]
Root: HKLM; Subkey: "Software\SeedTactics\FMS Insight Mazak Proxy"; ValueType: string; \
  ValueName: "DBType"; ValueData: "{code:GetDBType}"; \
Flags: createvalueifdoesntexist uninsdeletekey
Root: HKLM; Subkey: "Software\SeedTactics\FMS Insight Mazak Proxy"; ValueType: string; \
  ValueName: "OleDbDatabasePath"; ValueData: "{code:GetDatabasePath}"; \
  Flags: createvalueifdoesntexist uninsdeletekey
Root: HKLM; Subkey: "Software\SeedTactics\FMS Insight Mazak Proxy"; ValueType: string; \
  ValueName: "LogCSVPath"; ValueData: "{code:GetLogCSVPath}"; \
  Flags: createvalueifdoesntexist uninsdeletekey
Root: HKLM; Subkey: "Software\SeedTactics\FMS Insight Mazak Proxy"; ValueType: string; \
  ValueName: "LoadCSVPath"; ValueData: "{code:GetLoadCSVPath}"; \
  Flags: createvalueifdoesntexist uninsdeletekey

[Code]
var
  VersionPage: TInputOptionWizardPage;
  DatabasePage: TInputDirWizardPage;
  LogCSVPage: TInputDirWizardPage;
  LoadCSVPage: TInputDirWizardPage;

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

  LogCSVPage := CreateInputDirPage(DatabasePage.ID,
    'Select Log CSV', 'Please select the directory containing the log CSV files.',
    '', False, '');
  LogCSVPage.Add('');

  LoadCSVPage := CreateInputDirPage(LogCSVPage.ID,
    'Select Load CSV', 'Please select the directory containing the load/unload LDS files.',
    '', False, '');
  LoadCSVPage.Add('');

end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  if IsUpgrade() then
    begin
      Result := False;
      exit;
    end;

  if PageID = DatabasePage.ID then begin
    if VersionPage.Values[2] then begin
      Result := True
    end else begin
      Result := False
    end;
  end;

  if PageID = LogCSVPage.ID then begin
    if VersionPage.Values[0] then begin
      Result := True
    end else begin
      Result := False
    end
  end;

  if PageID = LoadCSVPage.ID then begin
    if VersionPage.Values[2] then begin
      Result := True
    end else begin
      Result := False
    end;
  end;
end;

function GetDBType(Param: string): string;
begin
  if VersionPage.Values[0] then begin
    Result := 'MazakVersionE'
  end;
  if VersionPage.Values[1] then begin
    Result := 'MazakWeb'
  end;
  if VersionPage.Values[2] then begin
    Result := 'MazakSmooth'
  end;
end;

function GetDatabasePath(Param: string): string;
begin
  Result := DatabasePage.Values[0]
end;

function GetLogCSVPath(Param: string): string;
begin
  Result := LogCSVPage.Values[0]
end;

function GetLoadCSVPath(Param: string): string;
begin
  Result := LoadCSVPage.Values[0]
end;