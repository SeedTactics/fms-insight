namespace BlackMaple.SeedTactics.FMSInsight.Benchmark;

public class QueuedMats
{
  private RepositoryConfig repo;

  public QueuedMats()
  {
    repo = RepositoryConfig.InitializeEventDatabase(
      new SerialSettings() { ConvertMaterialIDToSerial = (id) => id.ToString() },
      Environment.GetEnvironmentVariable("INSIGHT_DB_FILE")
    );
  }

  [Benchmark]
  public List<object> All()
  {
    using (var db = repo.OpenConnection())
    {
      var allMats = db.GetMaterialInAllQueues();
      var ret = new List<object>();
      foreach (var m in allMats)
      {
        ret.Add(new { mat = m });
      }
      return ret;
    }
  }

  [Benchmark]
  public List<object> AllAndInsps()
  {
    using (var db = repo.OpenConnection())
    {
      var allMats = db.GetMaterialInAllQueues();
      var insps = db.LookupInspectionDecisions(allMats.Select(m => m.MaterialID));
      var ret = new List<object>();
      foreach (var m in allMats)
      {
        ret.Add(new { mat = m, insp = insps[m.MaterialID] });
      }
      return ret;
    }
  }
}
