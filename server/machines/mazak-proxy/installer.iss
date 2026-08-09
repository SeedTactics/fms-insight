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
DisableDirPage=auto
DisableProgramGroupPage=auto
CloseApplications=yes

[Files]
Source: "proxy-build\mazak-proxy.exe"; DestDir: "{app}"; Flags: ignoreversion; BeforeInstall: StopProxyService
Source: "proxy-build\mazak-proxy.pdb"; DestDir: "{app}"; Flags: ignoreversion
Source: "proxy-build\mazak-proxy.exe.config"; DestDir: "{app}"; Flags: ignoreversion

[Run]
Filename: {sys}\sc.exe; Parameters: "create fmsinsightmazakproxy start= auto binPath= ""{app}\mazak-proxy.exe"" DisplayName= ""SeedTactic FMS Insight Mazak Proxy"""; Flags: runhidden; Check: ShouldCreateProxyService
Filename: {sys}\sc.exe; Parameters: "config fmsinsightmazakproxy start= auto binPath= ""{app}\mazak-proxy.exe"" DisplayName= ""SeedTactic FMS Insight Mazak Proxy"""; Flags: runhidden
Filename: {sys}\sc.exe; Parameters: "start fmsinsightmazakproxy"; Flags: runhidden

[UninstallRun]
Filename: {sys}\sc.exe; Parameters: "stop fmsinsightmazakproxy" ; Flags: runhidden
Filename: {sys}\sc.exe; Parameters: "delete fmsinsightmazakproxy" ; Flags: runhidden

[Registry]
Root: HKLM; Subkey: "Software\SeedTactics\FMS Insight Mazak Proxy"; ValueType: string; \
  ValueName: "Port"; ValueData: "{code:GetPort}"; \
  Flags: createvalueifdoesntexist uninsdeletekey
Root: HKLM; Subkey: "Software\SeedTactics\FMS Insight Mazak Proxy"; ValueType: string; \
  ValueName: "DBType"; ValueData: "{code:GetDBType}"; \
  Flags: createvalueifdoesntexist uninsdeletekey
Root: HKLM; Subkey: "Software\SeedTactics\FMS Insight Mazak Proxy"; ValueType: string; \
  ValueName: "SQLConnectionString"; ValueData: "{code:GetSQLConnectionString}"; \
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
  PortPage: TInputQueryWizardPage;
  VersionPage: TInputOptionWizardPage;
  DatabasePage: TInputDirWizardPage;
  SQLConnectionPage: TInputQueryWizardPage;
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

function ProxyServiceExists(): Boolean;
begin
  Result := RegKeyExists(HKLM,
    'SYSTEM\CurrentControlSet\Services\fmsinsightmazakproxy');
end;

function ShouldCreateProxyService(): Boolean;
begin
  Result := not ProxyServiceExists();
end;

procedure StopProxyService;
var
  ResultCode: Integer;
begin
  if ProxyServiceExists() then begin
    Exec(ExpandConstant('{sys}\net.exe'), 'stop fmsinsightmazakproxy', '',
      SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

function GetInstalledValue(ValueName: string; DefaultValue: string): string;
begin
  if not RegQueryStringValue(HKLM,
    'Software\SeedTactics\FMS Insight Mazak Proxy', ValueName, Result) then begin
    Result := DefaultValue;
  end;
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
  VersionPage.Add('Smooth / Neo');

  PortPage := CreateInputQueryPage(VersionPage.ID,
    'Mazak Proxy Port', 'Select the port',
    'Please select the port for the Mazak Proxy Service');
  PortPage.Add('Port: ', False);
  PortPage.Values[0] := '5200';

  DatabasePage := CreateInputDirPage(PortPage.ID,
    'Select Mazak Database Path', 'Where are the Mazak databases located?',
    'Select the folder where the Mazak transaction and read-only databases are located',
    False, '');
  DatabasePage.Add('c:\Mazak\NFMS\DB');

  SQLConnectionPage := CreateInputQueryPage(DatabasePage.ID,
    'Select Mazak SQL Server', 'Configure the Mazak SQL Server connection',
    'The default connects to the local Smooth or Neo PMC databases.');
  SQLConnectionPage.Add('Connection string: ', False);
  SQLConnectionPage.Values[0] := 'Data Source=(local)\PMCSQLSERVER;User ID=mazakpmc;Password=Fms-978';

  LogCSVPage := CreateInputDirPage(SQLConnectionPage.ID,
    'Select Log CSV', 'Please select the directory containing the log CSV files.',
    '', False, '');
  LogCSVPage.Add('c:\Mazak\FMS\Log');

  LoadCSVPage := CreateInputDirPage(LogCSVPage.ID,
    'Select Load CSV', 'Please select the directory containing the load/unload LDS files.',
    '', False, '');
  LoadCSVPage.Add('c:\Mazak\FMS\LDS');

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
      Result := True
    end else begin
      Result := False
    end
  end;

  if PageID = DatabasePage.ID then begin
    Result := VersionPage.Values[2]
  end;

  if PageID = SQLConnectionPage.ID then begin
    Result := not VersionPage.Values[2]
  end;

  if PageID = LoadCSVPage.ID then begin
    Result := VersionPage.Values[2]
  end;
end;

function GetDBType(Param: string): string;
begin
  if IsUpgrade() then begin
    Result := GetInstalledValue('DBType', 'MazakWeb');
    exit;
  end;

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

function GetSQLConnectionString(Param: string): string;
var
  InstalledDBType: string;
begin
  if IsUpgrade() then begin
    InstalledDBType := GetInstalledValue('DBType', 'MazakWeb');
    if InstalledDBType = 'MazakSmooth' then begin
      Result := GetInstalledValue('SQLConnectionString',
        'Data Source=(local)\PMCSQLSERVER;User ID=mazakpmc;Password=Fms-978')
    end else begin
      Result := GetInstalledValue('SQLConnectionString',
        'Provider=Microsoft.Jet.OLEDB.4.0;Password="";User ID=Admin;Mode=Share Deny None;')
    end;
    exit;
  end;

  if VersionPage.Values[2] then begin
    Result := SQLConnectionPage.Values[0]
  end else begin
    Result := 'Provider=Microsoft.Jet.OLEDB.4.0;Password="";User ID=Admin;Mode=Share Deny None;'
  end;
end;

function GetDatabasePath(Param: string): string;
begin
  if IsUpgrade() then begin
    Result := GetInstalledValue('OleDbDatabasePath', '');
  end else begin
    Result := DatabasePage.Values[0];
  end;
end;

function GetLogCSVPath(Param: string): string;
begin
  if IsUpgrade() then begin
    Result := GetInstalledValue('LogCSVPath', '');
  end else begin
    Result := LogCSVPage.Values[0];
  end;
end;

function GetLoadCSVPath(Param: string): string;
begin
  if IsUpgrade() then begin
    Result := GetInstalledValue('LoadCSVPath', '');
  end else begin
    Result := LoadCSVPage.Values[0];
  end;
end;

function GetPort(Param: string): string;
begin
  if IsUpgrade() then begin
    Result := GetInstalledValue('Port', '5200');
  end else begin
    Result := PortPage.Values[0];
  end;
end;
