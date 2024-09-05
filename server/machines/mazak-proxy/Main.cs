using MazakMachineInterface;

namespace BlackMaple.FMSInsight.Mazak.Proxy;

public class ProxyService : System.ServiceProcess.ServiceBase
{
  private ICurrentLoadActions _load;
  private IMazakDB _db;
  private IHttpServer _server;

  public static void Main()
  {
    System.ServiceProcess.ServiceBase.Run(new ProxyService());
  }

  protected override void OnStart(string[] args)
  {
    var cfg = new MazakConfig()
    {
      DBType = MazakDbType.MazakWeb,
      SQLConnectionString = "Data Source=.;Initial Catalog=SeedTacticsMazak;Integrated Security=True",
      LogCSVPath = @"C:\ProgramData\SeedTactics\Mazak\Logs",
      LoadCSVPath = @"C:\ProgramData\SeedTactics\Mazak\Load",
    };
    _load = new LoadOperationsFromFile(cfg);
    _db = new OpenDatabaseKitDB(cfg, _load);
    _server = new HttpServer("http://*:5000/");

    _server.AddLoadingHandler("/all-data", _db.LoadAllData);
    _server.AddPostHandler<string, MazakAllDataAndLogs>("/all-data-and-logs", _db.LoadAllDataAndLogs);
    _server.AddLoadingHandler("/programs", _db.LoadPrograms);
    _server.AddLoadingHandler("/tools", _db.LoadTools);
    _server.AddPostHandler<MazakWriteData, bool>(
      "/save",
      w =>
      {
        _db.Save(w);
        return true;
      }
    );

    _server.Start();
  }

  protected override void OnStop()
  {
    _server?.Dispose();
    _load = null;
    _db = null;
    _server = null;
  }
}
