#nullable enable

using System.Linq;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface;

internal static class MazakRouteHistory
{
  private static readonly Serilog.ILogger Log = Serilog.Log.ForContext(typeof(MazakRouteHistory));

  internal static MazakRouteIdentity? Find(
    IRepository db,
    MazakConfig config,
    string? partName,
    int nativeProcess
  ) =>
    config.UseDurableRouteIdentity && !string.IsNullOrEmpty(partName)
      ? db.LoadMazakRouteIdentity(config.ControllerSource, partName, nativeProcess)
      : null;

  internal static bool AgreesWithObservation(MazakRouteIdentity route, MazakAllData observed)
  {
    var comment = MazakPart.SplitComment(route.JobUnique, route.JobProcess, route.Path);
    var parts = observed.Parts.Where(p => p.PartName == route.PartName).Take(2).ToArray();
    if (
      parts.Length <= 1
      && parts.All(p =>
        p.Comment == comment
        && p.Processes.Count == 1
        && p.Processes[0].ProcessNumber == route.NativeProcess
      )
      && observed.Schedules.Where(s => s.PartName == route.PartName).All(s => s.Comment == comment)
    )
      return true;

    Log.Error("Native part or schedule conflicts with reserved Mazak route {@Route}", route);
    return false;
  }
}
