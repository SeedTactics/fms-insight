#nullable enable

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.ExceptionServices;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface;

internal static partial class ManagedMazakSchedules
{
  public static bool Cleanup(
    IRepository db,
    MazakConfig config,
    MazakAllData data,
    IMazakDB controller
  )
  {
    if (config.UseInsightJobQuantities is null)
      return false;
    var rejected = ImmutableHashSet<string>.Empty;
    ExceptionDispatchInfo? firstRejection = null;
    while (PlanCleanup(db, config, data, rejected) is { } next)
    {
      try
      {
        controller.Save(next.Write);
        return true;
      }
      catch (Exception ex)
      {
        firstRejection ??= ExceptionDispatchInfo.Capture(ex);
        rejected = rejected.Add(next.PartName);
        BuildMazakSchedules.Log.Warning(
          ex,
          "Could not clean up archived Mazak part {Part} on {Controller}",
          next.PartName,
          config.ControllerSource
        );
        // The write may already have taken effect. Recheck dependencies before trying a peer route.
        data = controller.LoadAllData();
      }
    }
    firstRejection?.Throw();
    return false;
  }

  private static (string PartName, MazakWriteData Write)? PlanCleanup(
    IRepository db,
    MazakConfig config,
    MazakAllData data,
    ImmutableHashSet<string> rejected
  )
  {
    var partNames = data.Parts.Select(p => p.PartName).ToImmutableHashSet(StringComparer.Ordinal);
    if (data.PalletSubStatuses is null || data.Schedules.Any(s => !partNames.Contains(s.PartName)))
      return null;

    var occupied = data.PalletSubStatuses.Where(s => s.FixQuantity > 0).ToImmutableList();
    var routes = db.LoadMazakManagedSchedules(config.ControllerSource);
    foreach (var route in routes.Where(r => !rejected.Contains(r.PartName)))
    {
      var job = db.LoadJob(route.JobUnique);
      if (!Owns(config, job) || !job.Archived)
        continue;
      MazakPartRow? part;
      MazakScheduleRow? schedule;
      try
      {
        (part, schedule) = Inspect(data, job, route);
      }
      catch (ConflictRequestException conflict)
      {
        BuildMazakSchedules.Log.Warning("{Conflict}", conflict.Message);
        continue;
      }

      if (
        data.LoadActions.Any(a =>
          a.Comment == route.Comment
          || string.Equals(a.Part, route.PartName, StringComparison.OrdinalIgnoreCase)
          || (string.IsNullOrEmpty(a.Comment) && a.Part == job.PartName)
        )
        || occupied.Any(a =>
          string.Equals(a.PartName, route.PartName, StringComparison.OrdinalIgnoreCase)
          || schedule is not null && a.ScheduleID == schedule.Id
        )
      )
        continue;
      if (schedule is not null)
      {
        if (
          schedule.Processes.Any(p =>
            p.ProcessMaterialQuantity != 0 || p.ProcessExecuteQuantity != 0
          )
        )
          continue;
        return (
          route.PartName,
          BuildMazakSchedules.RemoveSchedules(data, s => s.Id == schedule.Id).Item1
        );
      }
      if (part is not null)
      {
        return (
          route.PartName,
          new MazakWriteData
          {
            Prefix = "Delete archived managed part",
            Parts = [MazakJobs.PartDelete(part)],
          }
        );
      }

      var otherRoutes = routes.Where(r => r.PartName != route.PartName).ToImmutableList();
      var remainingProcesses = data.Parts.SelectMany(p => p.Processes).ToImmutableList();
      var usedFixtures = remainingProcesses
        .Select(p => p.Fixture)
        .Concat(occupied.Select(a => a.FixtureName))
        .Concat(otherRoutes.SelectMany(r => r.Fixtures))
        .ToHashSet(StringComparer.Ordinal);
      var mappings = data
        .Pallets.Where(p =>
          route.Fixtures.Contains(p.Fixture)
          && MazakPart.IsInsightFixture(p.Fixture)
          && !usedFixtures.Contains(p.Fixture)
        )
        .Select(p => p with { Command = MazakWriteCommand.Delete })
        .ToImmutableList();
      if (!mappings.IsEmpty)
      {
        return (
          route.PartName,
          new MazakWriteData
          {
            Prefix = "Delete archived managed pallet mappings",
            Pallets = mappings,
          }
        );
      }
      usedFixtures.UnionWith(data.Pallets.Select(p => p.Fixture));
      var usedPrograms = remainingProcesses
        .Select(p => p.MainProgram)
        .Concat(otherRoutes.SelectMany(r => r.Programs))
        .ToImmutableHashSet(StringComparer.Ordinal);
      var dependencies = new MazakJobs
      {
        OldMazakData = data,
        UsedFixtures = usedFixtures,
        UsedMainProgramComments = data
          .MainPrograms.Where(p => usedPrograms.Contains(p.MainProgram))
          .Select(p => p.Comment)
          .ToHashSet(StringComparer.Ordinal),
      }.DeleteFixtureAndProgramDatabaseRows();
      dependencies = dependencies with
      {
        Fixtures = dependencies
          .Fixtures.Where(f => route.Fixtures.Contains(f.FixtureName))
          .ToImmutableList(),
        Programs = dependencies
          .Programs.Where(p => route.Programs.Contains(p.MainProgram))
          .ToImmutableList(),
      };
      if (dependencies.Fixtures.Count != 0 || dependencies.Programs.Count != 0)
      {
        return (route.PartName, dependencies);
      }
      // Surviving named dependencies can be shared or retained by the standard program policy.
      db.RetireMazakManagedSchedule(config.ControllerSource, route.PartName);
      routes = routes.Remove(route);
    }
    return null;
  }
}
