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

  /// <summary>Count recorded physical attempts for this route's schedule history.</summary>
  /// <remarks>Returns null for schedules adopted with no known creation boundary.</remarks>
  public static MazakManagedLoadCount? RecordedLoads(
    IRepository db,
    MazakConfig config,
    string jobUnique,
    int process
  )
  {
    var (job, binding) = FindBinding(db, config, jobUnique, process, includeRetired: true);
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
        || entry.ForeignID?.StartsWith(source, StringComparison.Ordinal) != true
        || entry.ProgramDetails?.GetValueOrDefault(ManagedMazakSchedules.NativeProcessKey)
          != nativeProcessText
      )
        continue;
      identified += entry.Material.Count(m =>
        m.MaterialID >= 0
        && m.JobUniqueStr == job.UniqueStr
        && m.Process == process
        && m.Face == nativeProcess
        && (m.Path ?? 1) == 1
      );
      if (
        int.TryParse(
          entry.ProgramDetails?.GetValueOrDefault(ManagedMazakSchedules.UnidentifiedQuantityKey),
          NumberStyles.None,
          CultureInfo.InvariantCulture,
          out var unknown
        )
      )
        unidentified += unknown;
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
    var binding = db.LoadMazakManagedSchedules(config.ControllerSource, includeRetired)
      .SingleOrDefault(r => r.JobUnique == jobUnique && r.Comment == comment);
    if (binding?.ConfirmedScheduleId is null)
      throw new ConflictRequestException(
        $"Mazak job '{jobUnique}' has no confirmed schedule for route '{comment}'."
      );
    return (job, binding);
  }

  private static (MazakManagedSchedule Binding, MazakScheduleRow Schedule) Verify(
    IRepository db,
    MazakConfig config,
    MazakAllData observed,
    string jobUnique,
    int process
  )
  {
    var (job, binding) = FindBinding(db, config, jobUnique, process, includeRetired: false);
    var (part, schedule) = ManagedMazakSchedules.Inspect(
      observed,
      job,
      binding.PartName,
      binding.Comment,
      binding.ConfirmedScheduleId
    );
    if (part is null || schedule is null)
      throw new ConflictRequestException(
        $"Mazak job '{jobUnique}' is missing the installed definition or schedule for route '{binding.Comment}'."
      );
    return (binding, schedule);
  }
}
