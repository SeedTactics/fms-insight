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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

public class JobCache
{
  private Dictionary<string, HistoricJob> _jobs;
  private IRepository _repo;

  public JobCache(IRepository repo)
  {
    _repo = repo;
    _jobs = repo.LoadUnarchivedJobs().ToDictionary(j => j.UniqueStr, j => j);
  }

  public HistoricJob Lookup(string uniq)
  {
    if (_jobs.TryGetValue(uniq, out var j))
    {
      return j;
    }
    else
    {
      var job = _repo.LoadJob(uniq);
      if (job != null && job.Archived)
      {
        _repo.UnarchiveJob(job.UniqueStr);
        job = job with { Archived = false };
      }
      _jobs.Add(uniq, job);
      return job;
    }
  }

  public IEnumerable<HistoricJob> AllJobs => _jobs.Values;

  public static ActiveJob HistoricToActiveJob(
    HistoricJob j,
    IReadOnlyDictionary<(string uniq, int proc, int path), long> precedence,
    IReadOnlyDictionary<string, int> cyclesStartedOnProc1,
    IRepository db
  )
  {
    // completed
    var completed = j.Processes
      .Select(
        proc => new int[proc.Paths.Count] // defaults to fill with zeros
      )
      .ToArray();
    var unloads = db.GetLogForJobUnique(j.UniqueStr)
      .Where(e => e.LogType == LogType.LoadUnloadCycle && e.Result == "UNLOAD")
      .ToList();
    foreach (var e in unloads)
    {
      foreach (var mat in e.Material)
      {
        if (mat.JobUniqueStr == j.UniqueStr)
        {
          var details = db.GetMaterialDetails(mat.MaterialID);
          int matPath =
            details?.Paths != null && details.Paths.ContainsKey(mat.Process) ? details.Paths[mat.Process] : 1;
          completed[mat.Process - 1][matPath - 1] += 1;
        }
      }
    }

    // take decremented quantity out of the planned cycles
    int decrQty = j.Decrements?.Sum(d => d.Quantity) ?? 0;
    var newPlanned = j.Cycles - decrQty;

    var started = 0;
    if (cyclesStartedOnProc1 != null && cyclesStartedOnProc1.TryGetValue(j.UniqueStr, out var startedOnProc1))
    {
      started = startedOnProc1;
    }

    return j.CloneToDerived<ActiveJob, HistoricJob>() with
    {
      Completed = completed.Select(c => ImmutableList.Create(c)).ToImmutableList(),
      RemainingToStart = decrQty > 0 ? 0 : System.Math.Max(newPlanned - started, 0),
      Cycles = newPlanned,
      Precedence = j.Processes
        .Select(
          (proc, procIdx) =>
          {
            return proc.Paths
              .Select(
                (_, pathIdx) =>
                  precedence.GetValueOrDefault((uniq: j.UniqueStr, proc: procIdx + 1, path: pathIdx + 1), 0)
              )
              .ToImmutableList();
          }
        )
        .ToImmutableList(),
      AssignedWorkorders = db.GetWorkordersForUnique(j.UniqueStr)
    };
  }
}