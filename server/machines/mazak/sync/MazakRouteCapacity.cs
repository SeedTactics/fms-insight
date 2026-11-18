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
  // Serialize with the controller's bulk writer and other edits. Save at most NextWrite, then
  // recalculate from a fresh observation even if the write failed or its reply was lost. The
  // caller chooses when to expose material and ensures editing the observed offer is safe.
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
    int offeredQuantity = 0,
    string? fullPartName = null
  )
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(planQuantity);
    ArgumentOutOfRangeException.ThrowIfNegative(offeredQuantity);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(offeredQuantity, planQuantity);

    var job = db.LoadJob(jobUnique);
    if (
      !config.UseDurableRouteIdentity
      || job == null
      || job.Archived
      || !config.UsesInsightJobQuantities(job)
      || !ConvertJobsToMazakParts.IsSplitScheduleJob(job)
      || process <= 0
      || process > job.Processes.Count
      || path != 1
      || job.Processes.Any(p => p.Paths.Count != 1)
    )
      throw new BadRequestException(
        $"Job '{jobUnique}' process {process} path {path} must be an accepted, Insight-owned split Mazak route."
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

    var definitions = ConvertJobsToMazakParts.BuildSplitDefinition(
      db,
      observed,
      config,
      settings,
      job,
      identity,
      installed,
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
    var nativePart = installed ?? transactions.Parts.Single();
    var setSize = Math.Max(1, nativePart.Processes.Single().FixQuantity);
    if (offeredQuantity % setSize != 0)
      throw Conflict("Offered material must consist of whole pallet sets.");
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
        planQuantity,
        offeredQuantity
      );

    // No controller writes have occurred. The exact full name survives all subsequent phases.
    db.ReserveMazakRouteIdentity(identity);
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
    int planned,
    int offered
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
    if (offered > 0 && committed + offered > planned)
      throw new ConflictRequestException(
        $"Offered material must fit Mazak schedule {schedule.Id}'s remaining plan."
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
    return process.ProcessMaterialQuantity == offered
      ? null
      : new MazakWriteData
      {
        Prefix = "Set selected Mazak schedule material",
        Schedules =
        [
          schedule with
          {
            Command = MazakWriteCommand.ScheduleMaterialEdit,
            Processes = [process with { ProcessMaterialQuantity = offered }],
          },
        ],
      };
  }
}
