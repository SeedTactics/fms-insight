#nullable enable

using System;
using System.Linq;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface;

/// <summary>Plans edits against current readback from the controller that downloaded the route.</summary>
public static partial class MazakManagedScheduleControl
{
  /// <summary>Read the verified schedule and execution definition without reserving capacity.</summary>
  public static MazakScheduleRow VerifyInstalledRoute(
    IRepository db,
    MazakConfig config,
    MazakAllData observed,
    string jobUnique,
    int process
  ) => VerifySchedule(db, config, observed, jobUnique, process, verifyExecution: true).Schedule;

  /// <summary>Raise the native plan to at least the absolute quantity. A satisfied plan needs no write.</summary>
  public static MazakWriteData? EnsurePlan(
    IRepository db,
    MazakConfig config,
    MazakAllData observed,
    string jobUnique,
    int process,
    int minimum
  )
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimum);
    var schedule = VerifyInstalledRoute(db, config, observed, jobUnique, process);
    return schedule.PlanQuantity >= minimum
      ? null
      : new MazakWriteData
      {
        Prefix = "Increase managed schedule plan",
        Schedules =
        [
          schedule with
          {
            Command = MazakWriteCommand.ScheduleSafeEdit,
            PlanQuantity = minimum,
            Processes = [],
          },
        ],
      };
  }

  /// <summary>Set the outstanding ready quantity on the native schedule's entry process.</summary>
  public static MazakWriteData? SetMaterialOffer(
    IRepository db,
    MazakConfig config,
    MazakAllData observed,
    string jobUnique,
    int process,
    int quantity
  ) =>
    PlanMaterialOffer(db, config, observed, jobUnique, process, quantity, confirmWithdrawal: false);

  /// <summary>Plan a zero offer for a verified route, even when readback already shows zero.</summary>
  /// <remarks>
  /// Use fresh readback to retry a withdrawal whose reply was lost. Save the edit, await the
  /// controller's acknowledgment, and reconcile settled readback before releasing any reservation
  /// associated with the offer.
  /// </remarks>
  public static MazakWriteData WithdrawMaterialOffer(
    IRepository db,
    MazakConfig config,
    MazakAllData observed,
    string jobUnique,
    int process
  ) => PlanMaterialOffer(db, config, observed, jobUnique, process, 0, confirmWithdrawal: true)!;

  private static MazakWriteData? PlanMaterialOffer(
    IRepository db,
    MazakConfig config,
    MazakAllData observed,
    string jobUnique,
    int process,
    int quantity,
    bool confirmWithdrawal
  )
  {
    ArgumentOutOfRangeException.ThrowIfNegative(quantity);
    var (schedule, comment) = VerifySchedule(
      db,
      config,
      observed,
      jobUnique,
      process,
      verifyExecution: quantity > 0
    );
    if (!MazakPart.IsSplitComment(comment) && process != 1)
      throw new BadRequestException("A combined Mazak schedule admits material at process 1.");
    var first = schedule.Processes.Single(p => p.ProcessNumber == 1);
    var committed =
      (long)schedule.CompleteQuantity
      + schedule.Processes.Sum(p => (long)p.ProcessExecuteQuantity + p.ProcessBadQuantity)
      + schedule
        .Processes.Where(p => p.ProcessNumber != 1)
        .Sum(p => (long)p.ProcessMaterialQuantity);
    if (quantity > 0 && committed + quantity > schedule.PlanQuantity)
      throw new ConflictRequestException(
        $"Offered material exceeds the remaining plan for Mazak schedule {schedule.Id}."
      );
    return !confirmWithdrawal && first.ProcessMaterialQuantity == quantity
      ? null
      : new MazakWriteData
      {
        Prefix = "Set managed schedule material",
        Schedules =
        [
          schedule with
          {
            Command = MazakWriteCommand.ScheduleMaterialEdit,
            Processes = [first with { ProcessMaterialQuantity = quantity }],
          },
        ],
      };
  }

  private static (MazakScheduleRow Schedule, string Comment) VerifySchedule(
    IRepository db,
    MazakConfig config,
    MazakAllData observed,
    string jobUnique,
    int process,
    bool verifyExecution
  )
  {
    var job = db.LoadJob(jobUnique);
    if (
      job is null
      || !MazakLiveRoutes.Owns(config, job)
      || job.Archived
      || process < 1
      || process > job.Processes.Count
    )
      throw new BadRequestException(
        $"Job '{jobUnique}' process {process} is not a managed route on Mazak controller '{config.ControllerSource}'."
      );
    var comments = MazakPart.InitialScheduleComments(job);
    var comment = comments.Count == 1 ? comments[0] : comments[process - 1];
    var (part, schedule) = MazakLiveRoutes.Inspect(observed, job, comment);
    if (part is null || schedule is null)
      throw new ConflictRequestException(
        $"Mazak job '{jobUnique}' is missing the installed definition or schedule for route '{comment}'."
      );
    if (
      verifyExecution
      && MazakLiveRoutes.ExecutionConflict(db, observed, config, job, part) is { } conflict
    )
      throw new ConflictRequestException(conflict);
    return (schedule, comment);
  }
}
