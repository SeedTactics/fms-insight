#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface;

public sealed record MazakManagedLoadCount(long IdentifiedQuantity, long UnidentifiedQuantity)
{
  public long PhysicalQuantity => IdentifiedQuantity + UnidentifiedQuantity;
}

/// <summary>Plans edits against current readback from the controller that downloaded the route.</summary>
public static class MazakManagedScheduleControl
{
  /// <summary>Read the verified schedule and execution definition without reserving capacity.</summary>
  public static MazakScheduleRow VerifyInstalledRoute(
    IRepository db,
    MazakConfig config,
    MazakAllData observed,
    string jobUnique,
    int process
  )
  {
    var (job, binding) = FindBinding(db, config, jobUnique, process, includeRetired: false);
    return VerifySchedule(db, config, observed, job, binding, verifyExecution: true);
  }

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
    var (job, binding) = FindBinding(db, config, jobUnique, process, includeRetired: false);
    if (!MazakPart.IsSplitComment(binding.Comment) && process != 1)
      throw new BadRequestException("A combined Mazak schedule admits material at process 1.");
    var schedule = VerifySchedule(
      db,
      config,
      observed,
      job,
      binding,
      verifyExecution: quantity > 0
    );
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

  /// <summary>Count recorded physical attempts for this route's schedule history.</summary>
  /// <remarks>Returns null for schedules adopted with no known creation boundary.</remarks>
  public static MazakManagedLoadCount? RecordedLoads(
    IRepository db,
    MazakConfig config,
    string jobUnique,
    int process
  )
  {
    var (_, binding) = FindBinding(db, config, jobUnique, process, includeRetired: true);
    if (binding.BeforeLoadCounter is not { } after)
      return null;
    var nativeProcess = MazakPart.IsSplitComment(binding.Comment) ? 1 : process;
    var nativeProcessText = nativeProcess.ToString(CultureInfo.InvariantCulture);
    var source = config.LogForeignId("LG");
    long identified = 0,
      unidentified = 0;
    foreach (
      var entry in db.GetRecentLogForProgramDetail(
        after,
        ManagedMazakSchedules.NativePartKey,
        binding.PartName
      )
    )
    {
      if (
        entry.LogType != LogType.LoadUnloadCycle
        || entry.StartOfCycle
        || entry.Result != "LOAD"
        || entry.ProgramDetails?.ContainsKey(ManagedMazakSchedules.RouteConflictKey) == true
        || entry.ForeignID?.StartsWith(source, StringComparison.Ordinal) != true
        || entry.ProgramDetails?.GetValueOrDefault(ManagedMazakSchedules.NativeProcessKey)
          != nativeProcessText
      )
        continue;
      // The route and quantity belong to the physical transfer, even after reassignment.
      var quantities = MazakMaterialHistory.TransferQuantities(entry);
      identified += quantities.Identified;
      unidentified += quantities.Unidentified;
    }
    return new(identified, unidentified);
  }

  private static (Job Job, MazakManagedSchedule Binding) FindBinding(
    IRepository db,
    MazakConfig config,
    string jobUnique,
    int process,
    bool includeRetired
  )
  {
    var job = db.LoadJob(jobUnique);
    if (
      !ManagedMazakSchedules.Owns(config, job)
      || (!includeRetired && job.Archived)
      || process < 1
      || process > job.Processes.Count
    )
      throw new BadRequestException(
        $"Job '{jobUnique}' process {process} is not a managed route on Mazak controller '{config.ControllerSource}'."
      );
    var comments = MazakPart.InitialScheduleComments(job);
    var comment = comments.Count == 1 ? comments[0] : comments[process - 1];
    var binding = db.LoadMazakManagedSchedules(config.ControllerSource, includeRetired, jobUnique)
      .SingleOrDefault(r => r.Comment == comment);
    if (binding?.ConfirmedScheduleId is null)
      throw new ConflictRequestException(
        $"Mazak job '{jobUnique}' has no confirmed schedule for route '{comment}'."
      );
    return (job, binding);
  }

  private static MazakScheduleRow VerifySchedule(
    IRepository db,
    MazakConfig config,
    MazakAllData observed,
    Job job,
    MazakManagedSchedule binding,
    bool verifyExecution
  )
  {
    var (part, schedule) = ManagedMazakSchedules.Inspect(
      observed,
      job,
      binding,
      verifyDefinition: verifyExecution
    );
    if (part is null || schedule is null)
      throw new ConflictRequestException(
        $"Mazak job '{job.UniqueStr}' is missing the installed definition or schedule for route '{binding.Comment}'."
      );
    if (
      verifyExecution
      && ManagedMazakSchedules.ExecutionConflict(db, observed, config, job, part) is { } conflict
    )
      throw new ConflictRequestException(conflict);
    return schedule;
  }
}
