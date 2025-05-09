/* Copyright (c) 2023, John Lenz

All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.

    * Redistributions in binary form must reproduce the above
      copyright notice, this list of conditions and the following
      disclaimer in the documentation and/or other materials provided
      with the distribution.

    * Neither the name of John Lenz, Black Maple Software, SeedTactics,
      nor the names of other contributors may be used to endorse or
      promote products derived from this software without specific
      prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

namespace BlackMaple.MachineFramework;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

public interface IJobCache
{
  HistoricJob? Lookup(string uniq);
  IEnumerable<HistoricJob> AllJobs { get; }
  IEnumerable<(HistoricJob job, int proc, int path)> JobsSortedByPrecedence { get; }
}

public record DefaultPathInformation
{
  public required IReadOnlyList<MachiningStop> Stops { get; init; }
  public required bool IsFinalProcess { get; init; }
  public required string? OutputQueue { get; init; }
  public required TimeSpan? ExpectedLoadTimeForOnePieceOfMaterial { get; init; }
  public required TimeSpan? ExpectedUnloadTimeForOnePieceOfMaterial { get; init; }
}

public interface IJobCacheWithDefaultStops : IJobCache
{
  DefaultPathInformation DefaultPathInfo(ImmutableList<InProcessMaterial> matOnFace);
}

public class JobCache : IJobCache
{
  private sealed record JobSortKey : IComparable<JobSortKey>
  {
    public required bool ManuallyCreated { get; init; }
    public required DateTime RouteStart { get; init; }
    public required DateTime PathStart { get; init; }
    public required string Unique { get; init; }
    public required int Process { get; init; }
    public required int Path { get; init; }

    public int CompareTo(JobSortKey? other)
    {
      if (other == null)
        return 1;
      if (ManuallyCreated != other.ManuallyCreated)
      {
        return ManuallyCreated ? -1 : 1;
      }

      if (RouteStart != other.RouteStart)
      {
        return RouteStart.CompareTo(other.RouteStart);
      }

      if (PathStart != other.PathStart)
      {
        return PathStart.CompareTo(other.PathStart);
      }

      if (Unique != other.Unique)
      {
        return string.Compare(Unique, other.Unique, StringComparison.Ordinal);
      }

      if (Process != other.Process)
      {
        return Process.CompareTo(other.Process);
      }

      return Path.CompareTo(other.Path);
    }
  }

  private readonly Dictionary<string, HistoricJob> _jobs;
  private readonly SortedList<JobSortKey, (HistoricJob job, int proc, int path)> _precedence;
  private readonly IRepository _repo;
  private readonly Func<HistoricJob, HistoricJob>? _jobAdjustment;

  public IEnumerable<HistoricJob> AllJobs => _jobs.Values;
  public IEnumerable<(HistoricJob job, int proc, int path)> JobsSortedByPrecedence => _precedence.Values;

  public JobCache(IRepository repo, Func<HistoricJob, HistoricJob>? jobAdjustment = null)
  {
    _repo = repo;
    _jobAdjustment = jobAdjustment;
    if (jobAdjustment == null)
    {
      _jobs = repo.LoadUnarchivedJobs().ToDictionary(j => j.UniqueStr, j => j);
    }
    else
    {
      _jobs = repo.LoadUnarchivedJobs().Select(jobAdjustment).ToDictionary(j => j.UniqueStr, j => j);
    }
    _precedence = new SortedList<JobSortKey, (HistoricJob job, int proc, int path)>();
    foreach (var j in _jobs.Values)
    {
      AddJobToPrecedence(j);
    }
  }

  private void AddJobToPrecedence(HistoricJob j)
  {
    for (var proc = 1; proc <= j.Processes.Count; proc++)
    {
      for (var path = 1; path <= j.Processes[proc - 1].Paths.Count; path++)
      {
        var key = new JobSortKey()
        {
          ManuallyCreated = j.ManuallyCreated,
          PathStart = j.Processes[proc - 1].Paths[path - 1].SimulatedStartingUTC,
          RouteStart = j.RouteStartUTC,
          Unique = j.UniqueStr,
          Process = proc,
          Path = path,
        };
        _precedence.Add(key, (j, proc, path));
      }
    }
  }

  public HistoricJob? Lookup(string uniq)
  {
    if (string.IsNullOrEmpty(uniq))
    {
      return null;
    }

    if (_jobs.TryGetValue(uniq, out var j))
    {
      return j;
    }
    else
    {
      var job = _repo.LoadJob(uniq);
      if (job != null && _jobAdjustment != null)
      {
        job = _jobAdjustment(job);
      }
      if (job != null && job.Archived)
      {
        _repo.UnarchiveJob(job.UniqueStr);
        job = job with { Archived = false };
      }
      if (job != null)
      {
        _jobs.Add(uniq, job);
        AddJobToPrecedence(job);
      }
      return job;
    }
  }
}

public static class JobHelpers
{
  public static ImmutableDictionary<string, ActiveJob> BuildActiveJobs(
    this IJobCache cache,
    IEnumerable<InProcessMaterial> allMaterial,
    IRepository db,
    DateTime? archiveCompletedBefore = null,
    DateTime? archiveActiveBefore = null
  )
  {
    var precedence = cache
      .JobsSortedByPrecedence.Select((j, idx) => new { j, idx })
      .ToDictionary(x => (uniq: x.j.job.UniqueStr, proc: x.j.proc, path: x.j.path), x => (long)x.idx);

    return cache
      .AllJobs.SelectMany(j =>
      {
        var loadingCnt = allMaterial
          .Where(m =>
            m.JobUnique == j.UniqueStr
            && m.Action.Type == InProcessMaterialAction.ActionType.Loading
            && m.Action.ProcessAfterLoad == 1
          )
          .Count();

        // loaded and completed
        var loadedMats = new HashSet<long>();
        var completed = j
          .Processes.Select(proc =>
            new int[proc.Paths.Count] // defaults to fill with zeros
          )
          .ToArray();
        var maxUnloadTime = DateTime.MinValue;
        var maxLULTime = j.RouteEndUTC;

        foreach (var e in db.GetLogForJobUnique(j.UniqueStr))
        {
          if (e.LogType != LogType.LoadUnloadCycle)
            continue;
          if (e.StartOfCycle)
            continue;

          if (e.EndTimeUTC > maxLULTime)
          {
            maxLULTime = e.EndTimeUTC;
          }

          if (e.Result == "LOAD")
          {
            foreach (var mat in e.Material)
            {
              if (mat.JobUniqueStr == j.UniqueStr)
              {
                loadedMats.Add(mat.MaterialID);
              }
            }
          }
          else if (e.Result == "UNLOAD")
          {
            if (e.EndTimeUTC > maxUnloadTime)
            {
              maxUnloadTime = e.EndTimeUTC;
            }
            foreach (var mat in e.Material)
            {
              if (mat.JobUniqueStr == j.UniqueStr)
              {
                completed[mat.Process - 1][(mat.Path ?? 1) - 1] += 1;
              }
            }
          }
        }

        // take decremented quantity out of the planned cycles
        int decrQty = j.Decrements?.Sum(d => d.Quantity) ?? 0;
        var newPlanned = j.Cycles - decrQty;
        var remainingToStart = decrQty > 0 ? 0 : Math.Max(newPlanned - loadedMats.Count - loadingCnt, 0);

        // archive old completed jobs
        if (
          remainingToStart == 0
          && archiveCompletedBefore.HasValue
          && maxUnloadTime < archiveCompletedBefore.Value
          && !allMaterial.Where(m => m.JobUnique == j.UniqueStr).Any()
        )
        {
          db.ArchiveJob(j.UniqueStr);
          return Enumerable.Empty<ActiveJob>();
        }

        if (
          archiveActiveBefore.HasValue
          && maxLULTime < archiveActiveBefore.Value
          && !allMaterial.Where(m => m.JobUnique == j.UniqueStr).Any()
        )
        {
          db.ArchiveJob(j.UniqueStr);
          return Enumerable.Empty<ActiveJob>();
        }

        return new[]
        {
          j.CloneToDerived<ActiveJob, HistoricJob>() with
          {
            Archived = false,
            Completed = completed.Select(c => ImmutableList.Create(c)).ToImmutableList(),
            RemainingToStart = remainingToStart,
            Cycles = newPlanned,
            Precedence = j
              .Processes.Select(
                (proc, procIdx) =>
                {
                  return proc
                    .Paths.Select(
                      (_, pathIdx) =>
                        precedence.GetValueOrDefault(
                          (uniq: j.UniqueStr, proc: procIdx + 1, path: pathIdx + 1),
                          0
                        )
                    )
                    .ToImmutableList();
                }
              )
              .ToImmutableList(),
            AssignedWorkorders = db.GetWorkordersForUnique(j.UniqueStr),
          },
        };
      })
      .ToImmutableDictionary(j => j.UniqueStr, j => j);
  }

  public static ImmutableList<NewDecrementQuantity> BuildJobsToDecrement(
    this CurrentStatus status,
    Func<ActiveJob, bool>? decrementJobFilter = null
  )
  {
    var decrs = ImmutableList.CreateBuilder<NewDecrementQuantity>();
    foreach (var j in status.Jobs.Values)
    {
      if (j.ManuallyCreated || j.Decrements?.Count > 0)
        continue;

      if (decrementJobFilter != null && !decrementJobFilter(j))
      {
        continue;
      }

      int toStart = (int)(j.RemainingToStart ?? 0);
      if (toStart > 0)
      {
        decrs.Add(
          new NewDecrementQuantity()
          {
            JobUnique = j.UniqueStr,
            Part = j.PartName,
            Quantity = toStart,
          }
        );
      }
    }
    return decrs.ToImmutable();
  }
}
