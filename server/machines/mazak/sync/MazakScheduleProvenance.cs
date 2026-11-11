#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using BlackMaple.MachineFramework;
using RouteProvenance = BlackMaple.MachineFramework.MazakRouteProvenance;

namespace MazakMachineInterface;

internal sealed class MazakIdentityException(string message) : InvalidOperationException(message);

// Controller definitions are disposable. Exact full part names retain the interpretation of
// their native processes after deletion; recycled numeric schedule IDs never establish ownership.
internal static class MazakScheduleProvenance
{
  internal static void RecordCreated(
    IRepository repository,
    MazakJobs jobs,
    MazakWriteData definitions
  )
  {
    var routes = jobs
      .AllParts.SelectMany(part =>
        part.Processes.Select(process =>
        {
          var native = definitions
            .Parts.Single(row => row.PartName == part.PartName)
            .Processes.Single(row => row.ProcessNumber == process.ProcessNumber);
          return new RouteProvenance
          {
            FullPartName = part.PartName,
            NativeProcess = process.ProcessNumber,
            JobUnique = part.Job.UniqueStr,
            JobPartName = part.Job.PartName,
            JobProcess = process.JobProcessNumber,
            Path = process.Path,
            NumProcesses = part.Job.Processes.Count,
            NativeProgram = native.MainProgram ?? "",
            NativeFixture = native.Fixture ?? "",
            ProgramName = string.IsNullOrEmpty(process.PartProgram.ProgramName)
              ? null
              : process.PartProgram.ProgramName,
            ProgramRevision = string.IsNullOrEmpty(process.PartProgram.ProgramName)
              ? null
              : process.PartProgram.Revision,
            DownloadUid = part.DownloadID,
          };
        })
      )
      .ToImmutableList();
    repository.RecordMazakRouteProvenance(routes);
  }

  internal static RouteProvenance? Historical(
    IRepository repository,
    MazakConfig configuration,
    string fullPartName,
    int nativeProcess
  )
  {
    if (
      configuration.UseInsightJobQuantities is null
      || string.IsNullOrEmpty(fullPartName)
      || nativeProcess <= 0
    )
      return null;
    var route = repository.LoadMazakRouteProvenance(fullPartName, nativeProcess);
    return
      route is not null
      && configuration.UsesInsightJobQuantities(repository.LoadJob(route.JobUnique))
      ? route
      : null;
  }

  internal static bool MayBeSelected(
    IRepository repository,
    MazakAllData data,
    MazakConfig configuration,
    string fullPartName
  )
  {
    if (configuration.UseInsightJobQuantities is null || string.IsNullOrEmpty(fullPartName))
      return false;
    var schedules = data
      .Schedules.Where(schedule => schedule.PartName == fullPartName)
      .ToImmutableList();
    var parts = data.Parts.Where(part => part.PartName == fullPartName).ToImmutableList();
    if (schedules.Count == 1 && parts.Count == 1 && schedules[0].Comment == parts[0].Comment)
    {
      var owner = repository.LoadJob(MazakPart.UniqueFromComment(schedules[0].Comment ?? ""));
      if (owner is not null)
        return configuration.UsesInsightJobQuantities(owner);
    }
    var retained = repository.LoadUnarchivedJobs();
    var baseName = MazakPart.ExtractPartNameFromMazakPartName(fullPartName);
    return retained.Any(job =>
        configuration.UsesInsightJobQuantities(job) && job.PartName == baseName
      )
      || data.Parts.Where(part => part.PartName == fullPartName)
        .Any(part =>
          configuration.UsesInsightJobQuantities(
            repository.LoadJob(MazakPart.UniqueFromComment(part.Comment))
          )
        );
  }
}
