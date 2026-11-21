#nullable enable

using System.Globalization;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface;

public sealed record MazakManagedLoadCount(long IdentifiedQuantity, long UnidentifiedQuantity)
{
  public long PhysicalQuantity => IdentifiedQuantity + UnidentifiedQuantity;
}

public static partial class MazakManagedScheduleControl
{
  // Indexed in program_details. The route is attached to the transfer because a material ID can
  // later move to another job, and an unresolved transfer has no material ID at all.
  internal const string LoadRouteKey = "MazakManagedLoad";

  internal static string LoadRouteValue(string jobUnique, int process) =>
    process.ToString(CultureInfo.InvariantCulture) + ":" + jobUnique;

  /// <summary>Count physical load attempts recorded for this managed route, including invalidated cycles.</summary>
  /// <remarks>
  /// Counts include only transfers recorded using managed route metadata. Reconcile them with the
  /// live controller before admission, especially when commissioning an existing schedule.
  /// </remarks>
  public static MazakManagedLoadCount RecordedLoads(
    IRepository db,
    MazakConfig config,
    string jobUnique,
    int process
  )
  {
    var job = db.LoadJob(jobUnique);
    if (
      job is null
      || !MazakLiveRoutes.Owns(config, job)
      || process < 1
      || process > job.Processes.Count
    )
      throw new BadRequestException(
        $"Job '{jobUnique}' process {process} does not belong to Mazak controller '{config.ControllerSource}'."
      );

    var (identified, unidentified) = db.GetMazakManagedLoadQuantities(
      LoadRouteValue(jobUnique, process),
      config.LogForeignId("LG"),
      config.LogForeignId("LH")
    );
    return new(identified, unidentified);
  }
}
