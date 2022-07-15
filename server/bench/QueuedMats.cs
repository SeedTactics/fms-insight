namespace BlackMaple.SeedTactics.FMSInsight.Benchmark;

public class QueuedMats
{
  private RepositoryConfig repo;

  public QueuedMats()
  {
    repo = RepositoryConfig.InitializeEventDatabase(new FMSSettings(), Environment.GetEnvironmentVariable("INSIGHT_DB_FILE"));
  }

  [Benchmark]
  public List<object> LoadQueuedMats()
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
}