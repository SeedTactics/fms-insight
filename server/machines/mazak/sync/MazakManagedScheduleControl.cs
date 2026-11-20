#nullable enable

using System;
using System.Collections.Immutable;
using System.Linq;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface;

/// <summary>Plans edits against current readback from the controller that downloaded the route.</summary>
public static class MazakManagedScheduleControl
{
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
    var schedule = Verify(db, config, observed, jobUnique, process).Schedule;
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
  )
  {
    ArgumentOutOfRangeException.ThrowIfNegative(quantity);
    var (binding, schedule) = Verify(db, config, observed, jobUnique, process);
    if (!MazakPart.IsSplitComment(binding.Comment) && process != 1)
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
    return first.ProcessMaterialQuantity == quantity
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

  private static (MazakManagedSchedule Binding, MazakScheduleRow Schedule) Verify(
    IRepository db,
    MazakConfig config,
    MazakAllData observed,
    string jobUnique,
    int process
  )
  {
    var job = db.LoadJob(jobUnique);
    if (
      !ManagedMazakSchedules.Owns(config, job)
      || job.Archived
      || process < 1
      || process > job.Processes.Count
    )
      throw new BadRequestException(
        $"Job '{jobUnique}' process {process} is not an active managed route on Mazak controller '{config.ControllerSource}'."
      );
    var comments = MazakPart.InitialScheduleComments(job);
    var comment = comments.Length == 1 ? comments[0] : comments[process - 1];
    var binding = db.LoadMazakManagedSchedules(config.ControllerSource)
      .SingleOrDefault(r => r.JobUnique == jobUnique && r.Comment == comment);
    if (binding?.ConfirmedScheduleId is null)
      throw new ConflictRequestException(
        $"Mazak job '{jobUnique}' has no confirmed schedule for route '{comment}'."
      );
    var (part, schedule) = ManagedMazakSchedules.Inspect(
      observed,
      job,
      binding.PartName,
      comment,
      binding.ConfirmedScheduleId
    );
    if (part is null || schedule is null)
      throw new ConflictRequestException(
        $"Mazak job '{jobUnique}' is missing the installed definition or schedule for route '{comment}'."
      );
    return (binding, schedule);
  }
}
