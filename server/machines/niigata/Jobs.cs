/* Copyright (c) 2020, John Lenz

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
using System;
using System.Linq;
using System.Collections.Generic;
using BlackMaple.MachineFramework;
using System.Collections.Immutable;

namespace BlackMaple.FMSInsight.Niigata
{
  public class NiigataJobs : IJobControl
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<NiigataJobs>();

    public event NewJobsDelegate OnNewJobs;

    private RepositoryConfig _jobDbCfg;
    private FMSSettings _settings;
    private ISyncPallets _sync;
    private readonly NiigataStationNames _statNames;
    private bool _requireProgramsInJobs;
    private Func<NewJobs, CellState, IRepository, IEnumerable<string>> _additionalJobChecks;

    public NiigataJobs(RepositoryConfig j, FMSSettings st, ISyncPallets sy, NiigataStationNames statNames,
                       bool requireProgsInJobs, Func<NewJobs, CellState, IRepository, IEnumerable<string>> additionalJobChecks
                       )
    {
      _jobDbCfg = j;
      _sync = sy;
      _settings = st;
      _statNames = statNames;
      _additionalJobChecks = additionalJobChecks;
      _requireProgramsInJobs = requireProgsInJobs;
    }

    CurrentStatus IJobControl.GetCurrentStatus()
    {
      return _sync.CurrentCellState().CurrentStatus;
    }

    #region Jobs
    List<string> IJobControl.CheckValidRoutes(IEnumerable<Job> newJobs)
    {
      using (var jdb = _jobDbCfg.OpenConnection())
      {
        return CheckJobs(jdb, new NewJobs()
        {
          Jobs = newJobs.ToImmutableList()
        });
      }
    }

    public List<string> CheckJobs(IRepository jobDB, NewJobs jobs)
    {
      var errors = new List<string>();
      var cellState = _sync.CurrentCellState();
      if (cellState == null)
      {
        errors.Add("FMS Insight just started and is not yet ready for new jobs");
        return errors;
      }

      foreach (var j in jobs.Jobs)
      {
        for (var proc = 1; proc <= j.Processes.Count; proc++)
        {
          for (var path = 1; path <= j.Processes[proc - 1].Paths.Count; path++)
          {
            var pathData = j.Processes[proc - 1].Paths[path - 1];
            if (!pathData.Load.Any())
            {
              errors.Add("Part " + j.PartName + " does not have any assigned load stations");
            }
            if (!pathData.Unload.Any())
            {
              errors.Add("Part " + j.PartName + " does not have any assigned load stations");
            }
            if (string.IsNullOrEmpty(pathData.Fixture))
            {
              errors.Add("Part " + j.PartName + " does not have an assigned fixture");
            }
            if (!pathData.Pallets.Any())
            {
              errors.Add("Part " + j.PartName + " does not have any pallets");
            }
            foreach (var pal in pathData.Pallets)
            {
              if (!int.TryParse(pal, out var p))
              {
                errors.Add("Part " + j.PartName + " has non-integer pallets");
              }
            }
            if (!string.IsNullOrEmpty(pathData.InputQueue) && !_settings.Queues.ContainsKey(pathData.InputQueue))
            {
              errors.Add(" Part " + j.PartName + " has an input queue " + pathData.InputQueue + " which is not configured as a local queue in FMS Insight.");
            }
            if (!string.IsNullOrEmpty(pathData.OutputQueue) && !_settings.Queues.ContainsKey(pathData.OutputQueue))
            {
              errors.Add(" Part " + j.PartName + " has an output queue " + pathData.OutputQueue + " which is not configured as a queue in FMS Insight.");
            }

            foreach (var stop in pathData.Stops)
            {
              if (_statNames != null && _statNames.ReclampGroupNames.Contains(stop.StationGroup))
              {
                if (!stop.Stations.Any())
                {
                  errors.Add("Part " + j.PartName + " does not have any assigned load stations for intermediate load stop");
                }
              }
              else
              {
                if (string.IsNullOrEmpty(stop.Program))
                {
                  if (_requireProgramsInJobs)
                  {
                    errors.Add("Part " + j.PartName + " has no assigned program");
                  }
                }
                else
                {
                  CheckProgram(stop.Program, stop.ProgramRevision, jobs.Programs, cellState, jobDB, "Part " + j.PartName, errors);
                }
              }
            }
          }
        }
      }

      foreach (var w in jobs.CurrentUnfilledWorkorders ?? Enumerable.Empty<Workorder>())
      {
        if (w.Programs != null)
        {
          foreach (var prog in w.Programs)
          {
            CheckProgram(prog.ProgramName, prog.Revision, jobs.Programs, cellState, jobDB, "Workorder " + w.WorkorderId, errors);
          }
        }
      }

      if (_additionalJobChecks != null)
      {
        errors.AddRange(_additionalJobChecks(jobs, cellState, jobDB));
      }
      return errors;
    }

    private void CheckProgram(string programName, long? rev, IEnumerable<MachineFramework.NewProgramContent> newPrograms, CellState cellState, IRepository jobDB, string errHdr, IList<string> errors)
    {
      if (rev.HasValue && rev.Value > 0)
      {
        var existing = jobDB.LoadProgram(programName, rev.Value) != null;
        var newProg = newPrograms != null && newPrograms.Any(p => p.ProgramName == programName && p.Revision == rev.Value);
        if (!existing && !newProg)
        {
          errors.Add(errHdr + " program " + programName + " rev" + rev.Value.ToString() + " is not found");
        }
      }
      else
      {
        var existing = jobDB.LoadMostRecentProgram(programName) != null;
        var newProg = newPrograms != null && newPrograms.Any(p => p.ProgramName == programName);
        if (!existing && !newProg)
        {
          if (int.TryParse(programName, out int progNum))
          {
            if (!cellState.Status.Programs.Values.Any(p => p.ProgramNum == progNum && !AssignNewRoutesOnPallets.IsInsightProgram(p)))
            {
              errors.Add(errHdr + " program " + programName + " is neither included in the download nor found in the cell controller");
            }
          }
          else
          {
            errors.Add(errHdr + " program " + programName + " is neither included in the download nor is an integer");
          }
        }
      }
    }

    void IJobControl.AddJobs(NewJobs jobs, string expectedPreviousScheduleId, bool waitForCopyToCell)
    {
      using (var jdb = _jobDbCfg.OpenConnection())
      {
        Log.Debug("Adding new jobs {@jobs}", jobs);
        var errors = CheckJobs(jdb, jobs);
        if (errors.Any())
        {
          throw new BadRequestException(string.Join(Environment.NewLine, errors));
        }

        CellState curSt = _sync.CurrentCellState();
        foreach (var j in curSt.CurrentStatus.Jobs.Values)
        {
          if (IsJobCompleted(j, curSt))
          {
            jdb.ArchiveJob(j.UniqueStr);
          }
        }

        Log.Debug("Adding jobs to database");

        jdb.AddJobs(jobs, expectedPreviousScheduleId, addAsCopiedToSystem: true);
      }

      Log.Debug("Sending new jobs on websocket");

      OnNewJobs?.Invoke(jobs);

      Log.Debug("Signaling new jobs available for routes");

      _sync.JobsOrQueuesChanged();
    }

    private bool IsJobCompleted(ActiveJob job, CellState st)
    {
      if (st == null) return false;
      if (job.RemainingToStart > 0) return false;

      var matInProc =
        st.Pallets
        .SelectMany(p => p.Material)
        .Concat(st.QueuedMaterial)
        .Select(f => f.Mat)
        .Where(m => m.JobUnique == job.UniqueStr)
        .Any();

      if (matInProc)
      {
        return false;
      }
      else
      {
        return true;
      }
    }

    List<JobAndDecrementQuantity> IJobControl.DecrementJobQuantites(long loadDecrementsStrictlyAfterDecrementId)
    {
      using (var jdb = _jobDbCfg.OpenConnection())
      {
        _sync.DecrementPlannedButNotStartedQty(jdb);
        return jdb.LoadDecrementQuantitiesAfter(loadDecrementsStrictlyAfterDecrementId);
      }
    }

    List<JobAndDecrementQuantity> IJobControl.DecrementJobQuantites(DateTime loadDecrementsAfterTimeUTC)
    {
      using (var jdb = _jobDbCfg.OpenConnection())
      {
        _sync.DecrementPlannedButNotStartedQty(jdb);
        return jdb.LoadDecrementQuantitiesAfter(loadDecrementsAfterTimeUTC);
      }
    }

    void IJobControl.SetJobComment(string jobUnique, string comment)
    {
      using (var jdb = _jobDbCfg.OpenConnection())
      {
        jdb.SetJobComment(jobUnique, comment);
      }
      _sync.JobsOrQueuesChanged();
    }

    public void ReplaceWorkordersForSchedule(string scheduleId, IEnumerable<Workorder> newWorkorders, IEnumerable<MachineFramework.NewProgramContent> programs)
    {
      var cellState = _sync.CurrentCellState();
      if (cellState == null) return;

      using (var jdb = _jobDbCfg.OpenConnection())
      {
        var errors = new List<string>();
        foreach (var w in newWorkorders ?? Enumerable.Empty<Workorder>())
        {
          if (w.Programs != null)
          {
            foreach (var prog in w.Programs)
            {
              CheckProgram(prog.ProgramName, prog.Revision, programs, cellState, jdb, "Workorder " + w.WorkorderId, errors);
            }
          }
        }
        if (errors.Any())
        {
          throw new BadRequestException(string.Join(Environment.NewLine, errors));
        }

        jdb.ReplaceWorkordersForSchedule(scheduleId, newWorkorders, programs);
      }

      _sync.JobsOrQueuesChanged();
    }
    #endregion

  }
}