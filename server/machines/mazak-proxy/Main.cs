using MazakMachineInterface;
using Serilog;

namespace BlackMaple.FMSInsight.Mazak.Proxy;

public class ProxyService : System.ServiceProcess.ServiceBase
{
  private MazakConfig _cfg;
  private ICurrentLoadActions _load;
  private IMazakDB _db;
  private IHttpServer _server;

  public ProxyService()
  {
    _cfg = MazakConfig.LoadFromRegistry();
  }

  public ProxyService(MazakConfig cfg)
  {
    _cfg = cfg;
  }

  public static void Main(string[] args)
  {
#if DEBUG
    if (args.Length > 0 && args[0] == "--console")
    {
      var svc = new ProxyService(
        new MazakConfig()
        {
          DBType = MazakDbType.MazakWeb,
          OleDbDatabasePath = "c:\\Mazak\\NFMS\\DB",
          SQLConnectionString = MazakConfig.DefaultConnectionStr,
          LogCSVPath = "c:\\Mazak\\FMS\\Log",
          LoadCSVPath = "c:\\Mazak\\FMS\\LDS",
        }
      );
      svc.OnStart(args);
      System.Console.WriteLine("Press enter to stop");
      System.Console.ReadLine();
      svc.OnStop();
      return;
    }
#endif

    System.ServiceProcess.ServiceBase.Run(new ProxyService());
  }

  protected override void OnStart(string[] args)
  {
    Log.Information(
      string.Join(
        "",
        [
          "Starting Mazak Proxy Service with config: ",
          " DBType: " + _cfg.DBType,
          " OleDbDatabasePath: " + _cfg.OleDbDatabasePath,
          " LogCSVPath: " + _cfg.LogCSVPath,
          " LoadCSVPath: " + _cfg.LoadCSVPath,
          " SQLConnectionString: " + _cfg.SQLConnectionString,
        ]
      )
    );

    _load = new LoadOperationsFromFile(_cfg);
    _db = new OpenDatabaseKitDB(_cfg, _load);
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
    Log.Information("Stopping Mazak Proxy Service");

    _server?.Dispose();
    _load = null;
    _db = null;
    _server = null;
  }
}
