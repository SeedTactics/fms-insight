#nullable enable

using System;
using System.Collections.Immutable;
using System.Linq;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface;

public sealed record MazakRouteWrite
{
  public required MazakRouteIdentity Identity { get; init; }
  public required MazakWriteData? NextWrite { get; init; }
}

public static class MazakRouteCapacity
{
  // Count physical loads, including attempts whose subsequent machining was invalidated.
  // The caller supplies the history boundary for the native schedule being accounted for.
  public static long RecordedLoadQuantity(
    IRepository db,
    MazakConfig config,
    MazakRouteIdentity route,
    long afterCounter
  )
  {
    if (route.Source != config.ControllerSource)
      throw new ArgumentException(
        "The route belongs to a different Mazak controller.",
        nameof(route)
      );
    var sourcePrefix = config.LogForeignId("LG");
    return db.GetLogForJobUnique(route.JobUnique)
      .Where(entry =>
        entry.Counter > afterCounter
        && entry.LogType == LogType.LoadUnloadCycle
        && !entry.StartOfCycle
        && entry.Result == "LOAD"
        && entry.ForeignID?.StartsWith(sourcePrefix, StringComparison.Ordinal) == true
        && entry.ProgramDetails?.GetValueOrDefault(MazakRouteHistory.NativePartNameKey)
          == route.PartName
      )
      .Sum(entry =>
        (long)
          entry.Material.Count(material =>
            material.JobUniqueStr == route.JobUnique
            && material.Process == route.JobProcess
            && (material.Path ?? 1) == route.Path
          )
      );
  }

  // Serialize with the controller's bulk writer and other edits. Save at most NextWrite, then
  // recalculate from a fresh observation even if the write failed or its reply was lost. The
  // calculation prepares native capacity. Material admission is a separate caller decision.
  // An explicit name can select a saved definition or adopt a live legacy definition whose
  // historical ownership the caller can establish. A retired legacy name needs a reservation.
  public static MazakRouteWrite Calculate(
    IRepository db,
    MazakAllData observed,
    MazakConfig config,
    FMSSettings settings,
    string jobUnique,
    int process,
    int path,
    int planQuantity,
    string? fullPartName = null
  ) =>
    Plan(
      db,
      observed,
      config,
      settings,
      jobUnique,
      process,
      path,
      planQuantity,
      fullPartName,
      reserve: true
    );

  // Use the same qualification and next-write calculation for decisions that must leave the
  // repository untouched. Call Calculate again under the controller owner before any native write.
  public static MazakRouteWrite Preview(
    IRepository db,
    MazakAllData observed,
    MazakConfig config,
    FMSSettings settings,
    string jobUnique,
    int process,
    int path,
    int planQuantity,
    string? fullPartName = null
  ) =>
    Plan(
      db,
      observed,
      config,
      settings,
      jobUnique,
      process,
      path,
      planQuantity,
      fullPartName,
      reserve: false
    );

  private static MazakRouteWrite Plan(
    IRepository db,
    MazakAllData observed,
    MazakConfig config,
    FMSSettings settings,
    string jobUnique,
    int process,
    int path,
    int planQuantity,
    string? fullPartName,
    bool reserve
  )
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(planQuantity);

    var job = db.LoadJob(jobUnique);
    if (
      !config.UseDurableRouteIdentity
      || job == null
      || job.Archived
      || !config.UsesInsightJobQuantities(job)
      || process <= 0
      || process > job.Processes.Count
      || path != 1
      || job.Processes.Any(p => p.Paths.Count != 1)
    )
      throw new BadRequestException(
        $"Job '{jobUnique}' process {process} path {path} must be an accepted, Insight-owned Mazak route with one path per process."
      );

    var comment = MazakPart.SplitComment(jobUnique, process, path);
    var saved = db.LoadMazakRouteIdentities(config.ControllerSource, jobUnique)
      .Where(r => r.JobProcess == process && r.Path == path)
      .ToImmutableList();
    if (fullPartName == null && saved.Count > 1)
      throw Conflict("The route has multiple reserved names. Select a full part name.");
    fullPartName ??= saved.SingleOrDefault()?.PartName;
    if (
      fullPartName == null
      && (
        observed.Parts.Any(p => p.Comment == comment)
        || observed.Schedules.Any(s => s.Comment == comment)
      )
    )
      throw Conflict("The route has a legacy definition. Select its full part name to adopt it.");

    var allocating = fullPartName == null;
    fullPartName ??= MazakPart.SplitPartName(
      job,
      WriteJobs.FindUnusedDownloadId(
        observed,
        db.LoadReservedMazakPartNames(config.ControllerSource)
      ),
      partIndex: 1,
      process: process
    );
    var installedParts = observed
      .Parts.Where(p => string.Equals(p.PartName, fullPartName, StringComparison.OrdinalIgnoreCase))
      .Take(2)
      .ToImmutableList();
    if (installedParts.Count > 1 || installedParts.Any(p => p.PartName != fullPartName))
      throw Conflict($"Native part name '{fullPartName}' is ambiguous.");
    var installed = installedParts.SingleOrDefault();
    var identity = new MazakRouteIdentity
    {
      Source = config.ControllerSource,
      PartName = fullPartName,
      NativeProcess = 1,
      JobUnique = jobUnique,
      JobProcess = process,
      Path = path,
    };
    var existing = db.LoadMazakRouteIdentity(identity.Source, fullPartName, identity.NativeProcess);
    if (existing != null && existing != identity)
      throw Conflict($"Native part '{fullPartName}' is reserved for a different route.");
    if (
      (!allocating && existing == null && installed == null)
      || !MazakPart.IsSailPart(fullPartName, comment)
      || MazakPart.ExtractPartNameFromMazakPartName(fullPartName) != job.PartName
    )
      throw Conflict(
        $"Native part '{fullPartName}' is neither reserved nor an installed definition of the route."
      );

    var schedules = observed
      .Schedules.Where(s =>
        string.Equals(s.PartName, fullPartName, StringComparison.OrdinalIgnoreCase)
        || s.Comment == comment
      )
      .Take(2)
      .ToImmutableList();
    if (
      schedules.Count > 1
      || schedules.Any(s =>
        s.PartName != fullPartName
        || s.Comment != comment
        || s.Processes.Count != 1
        || s.Processes[0].ProcessNumber != identity.NativeProcess
        || observed.Schedules.Count(other => other.Id == s.Id) != 1
      )
      || !MazakRouteHistory.AgreesWithObservation(identity, observed)
      || (installed == null && schedules.Count != 0)
      || (installed != null && string.IsNullOrEmpty(installed.Processes[0].Fixture))
    )
      throw Conflict("Observed parts or schedules disagree with the selected split route.");

    var savedPreparation = db.LoadMazakRoutePreparation(
      identity.Source,
      identity.PartName,
      identity.NativeProcess
    );
    var preparation = savedPreparation;
    if (
      preparation != null
      && installed != null
      && installed.Processes[0].Fixture != preparation.FixtureName
    )
      throw Conflict("The installed part uses a different fixture from its saved preparation.");

    var definitions = ConvertJobsToMazakParts.BuildSplitDefinition(
      db,
      observed,
      config,
      settings,
      job,
      identity,
      installed,
      preparation,
      out var expected
    );
    var fixture = installed?.Processes[0].Fixture ?? definitions.Fixtures.Single().MazakFixtureName;
    if (string.IsNullOrEmpty(fixture) || observed.Fixtures.Count(f => f.FixtureName == fixture) > 1)
      throw Conflict($"Native fixture '{fixture}' for '{fullPartName}' is ambiguous.");
    var dependencies = definitions.AddFixtureAndProgramDatabaseRows(
      [fixture],
      [expected],
      db.LoadProgramContent,
      config.ProgramDirectory
    );
    var transactions = definitions.CreatePartPalletDatabaseRows(config);
    if (installed == null && preparation == null)
    {
      var chosenGroup = observed.Pallets.FirstOrDefault(p => p.Fixture == fixture)?.FixtureGroup
        ?? (config.OverrideFixtureGroupToZero ? 0 : definitions.Fixtures.Single().FixtureGroup);
      preparation = new MazakRoutePreparation { FixtureName = fixture, FixtureGroup = chosenGroup };
    }
    if (preparation != null)
    {
      var addProgram = dependencies.Programs.SingleOrDefault()?.MainProgram;
      if (
        addProgram != null
        && preparation.AddedProgram != null
        && preparation.AddedProgram != addProgram
      )
        throw Conflict("The selected program differs from the saved preparation.");
      var requiredProgram = expected.Processes.Single().PartProgram.CellControllerProgramName;
      if (preparation.RequiredProgram != null && preparation.RequiredProgram != requiredProgram)
        throw Conflict("The required native program differs from the saved preparation.");
      preparation = preparation with
      {
        AddedProgram = preparation.AddedProgram ?? addProgram,
        RequiredProgram = requiredProgram,
      };
      var expectedPallets = expected
        .Processes.Single()
        .Pallets()
        .Select(config.InversePalletNumber)
        .ToImmutableHashSet();
      if (
        observed.Pallets.Any(p =>
          p.Fixture == fixture
          && (
            p.FixtureGroup != preparation.FixtureGroup || !expectedPallets.Contains(p.PalletNumber)
          )
        )
      )
        throw Conflict($"Prepared fixture '{fixture}' has incompatible pallet mappings.");
      // An out-of-band write can still claim a group while the fixture has no mappings.
      if (
        config.UseStartingOffsetForDueDate
        && preparation.FixtureGroup != 0
        && observed.Pallets.Any(p =>
          p.Fixture != fixture
          && p.FixtureGroup == preparation.FixtureGroup
          && expectedPallets.Contains(p.PalletNumber)
        )
      )
        throw Conflict(
          $"Prepared fixture group {preparation.FixtureGroup} is occupied on a selected pallet."
        );
    }
    if (installed != null)
    {
      var expectedRow = expected.ToDatabaseRow();
      expected.Processes.Single().CreateDatabaseRow(expectedRow, fixture, config);
      ValidateInstalledPart(
        observed,
        config,
        installed,
        expectedRow.Processes.Single(),
        expected.Processes.Single()
      );
    }
    var next =
      dependencies.Fixtures.Count > 0 || dependencies.Programs.Count > 0 ? dependencies
      : transactions.Parts.Count > 0 ? transactions with { Pallets = [] }
      : transactions.Pallets.Count > 0 ? transactions
      : BuildScheduleWrite(
        observed,
        config,
        job,
        identity,
        schedules.SingleOrDefault(),
        planQuantity
      );

    // No controller writes have occurred. The exact full name survives all subsequent phases.
    if (reserve)
    {
      if (preparation != null && preparation != savedPreparation)
        db.ReserveMazakRoutePreparation(identity, preparation);
      else if (existing == null)
        db.ReserveMazakRouteIdentity(identity);
    }
    return new MazakRouteWrite { Identity = identity, NextWrite = next };

    ConflictRequestException Conflict(string reason) =>
      new($"Mazak job '{jobUnique}' process {process} path {path}. {reason}");
  }

  private static void ValidateInstalledPart(
    MazakAllData data,
    MazakConfig config,
    MazakPartRow installed,
    MazakPartProcessRow expected,
    MazakProcess expectedProcess
  )
  {
    var actual = installed.Processes.Single();
    var expectedPallets = expectedProcess
      .Pallets()
      .Select(config.InversePalletNumber)
      .ToImmutableHashSet();
    var actualPallets = data.Pallets.Where(p => p.Fixture == actual.Fixture).ToImmutableList();
    if (
      actual.FixQuantity != expected.FixQuantity
      || actual.ContinueCut != expected.ContinueCut
      || actual.WashType != expected.WashType
      || actual.CutMc?.TrimEnd() != expected.CutMc?.TrimEnd()
      || actual.FixLDS?.TrimEnd() != expected.FixLDS?.TrimEnd()
      || actual.RemoveLDS?.TrimEnd() != expected.RemoveLDS?.TrimEnd()
      || actual.MainProgram?.TrimEnd() != expected.MainProgram?.TrimEnd()
      || actualPallets.Select(p => p.FixtureGroup).Distinct().Count() > 1
      || actualPallets.Any(p => !expectedPallets.Contains(p.PalletNumber))
      || actualPallets.Select(p => p.PalletNumber).Distinct().Count() != actualPallets.Count
    )
      throw new ConflictRequestException(
        $"Installed Mazak part '{installed.PartName}' disagrees with the accepted route, program, or pallet mappings."
      );
  }

  private static MazakWriteData? BuildScheduleWrite(
    MazakAllData data,
    MazakConfig config,
    Job job,
    MazakRouteIdentity route,
    MazakScheduleRow? schedule,
    int planned
  )
  {
    if (schedule == null)
      return new MazakWriteData
      {
        Prefix = "Add selected Mazak schedule",
        Schedules = [BuildMazakSchedules.CreateSplitSchedule(data, route, job, config, planned)],
      };

    var process = schedule.Processes[0];
    var committed =
      (long)schedule.CompleteQuantity + process.ProcessExecuteQuantity + process.ProcessBadQuantity;
    if (planned < schedule.PlanQuantity)
      throw new ConflictRequestException(
        $"Reducing Mazak schedule {schedule.Id} requires retirement or backout."
      );
    if (planned > schedule.PlanQuantity)
    {
      if (committed + process.ProcessMaterialQuantity > planned)
        throw new ConflictRequestException(
          $"Mazak schedule {schedule.Id}'s current quantities exceed the new plan."
        );
      return new MazakWriteData
      {
        Prefix = "Increase selected Mazak schedule plan",
        Schedules =
        [
          schedule with
          {
            Command = MazakWriteCommand.ScheduleSafeEdit,
            PlanQuantity = planned,
            Processes = [],
          },
        ],
      };
    }
    return null;
  }

  // Set the outstanding offer on a freshly observed split schedule. After an uncertain write
  // the owner must recalculate what is still available to load: Mazak may have consumed it.
  public static MazakWriteData? SetMaterialQuantity(MazakScheduleRow schedule, int offeredQuantity)
  {
    ArgumentOutOfRangeException.ThrowIfNegative(offeredQuantity);
    if (
      !MazakPart.IsSplitComment(schedule.Comment)
      || schedule.Processes is not [{ ProcessNumber: 1 } process]
    )
      throw new BadRequestException(
        "Material admission requires a single-process split Mazak schedule."
      );
    var committed =
      (long)schedule.CompleteQuantity + process.ProcessExecuteQuantity + process.ProcessBadQuantity;
    if (offeredQuantity > 0 && committed + offeredQuantity > schedule.PlanQuantity)
      throw new ConflictRequestException(
        $"Offered material must fit Mazak schedule {schedule.Id}'s remaining plan."
      );
    return process.ProcessMaterialQuantity == offeredQuantity
      ? null
      : new MazakWriteData
      {
        Prefix = "Set selected Mazak schedule material",
        Schedules =
        [
          schedule with
          {
            Command = MazakWriteCommand.ScheduleMaterialEdit,
            Processes = [process with { ProcessMaterialQuantity = offeredQuantity }],
          },
        ],
      };
  }
}
