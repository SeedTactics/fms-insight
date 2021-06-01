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
using BlackMaple.MachineWatchInterface;
using System.Collections.Immutable;

namespace BlackMaple.FMSInsight.Niigata
{
  public class NiigataJobs : IJobControl
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<NiigataJobs>();
    private RepositoryConfig _jobDbCfg;
    private FMSSettings _settings;
    private ISyncPallets _sync;
    private readonly NiigataStationNames _statNames;
    private Action<NewJobs> _onNewJobs;
    private Action<EditMaterialInLogEvents> _onEditMatInLog;
    private bool _requireProgramsInJobs;
    private Func<NewJobs, CellState, IRepository, IEnumerable<string>> _additionalJobChecks;

    public NiigataJobs(RepositoryConfig j, FMSSettings st, ISyncPallets sy, NiigataStationNames statNames,
                       bool requireProgsInJobs, Action<NewJobs> onNewJobs, Action<EditMaterialInLogEvents> onEditMatInLog,
                       Func<NewJobs, CellState, IRepository, IEnumerable<string>> additionalJobChecks
                       )
    {
      _onNewJobs = onNewJobs;
      _jobDbCfg = j;
      _sync = sy;
      _settings = st;
      _statNames = statNames;
      _additionalJobChecks = additionalJobChecks;
      _requireProgramsInJobs = requireProgsInJobs;
      _onEditMatInLog = onEditMatInLog;
    }

    CurrentStatus IJobControl.GetCurrentStatus()
    {
      using (var jdb = _jobDbCfg.OpenConnection())
      {
        return BuildCurrentStatus.Build(jdb, _sync.CurrentCellState(), _settings);
      }
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
            if (pathData.PathGroup != 0)
            {
              errors.Add(" Part " + j.PartName + " uses obsolete path groups");
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

      foreach (var w in jobs.CurrentUnfilledWorkorders ?? Enumerable.Empty<PartWorkorder>())
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

    private void CheckProgram(string programName, long? rev, IEnumerable<MachineWatchInterface.ProgramEntry> newPrograms, CellState cellState, IRepository jobDB, string errHdr, IList<string> errors)
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

        var existingJobs = jdb.LoadUnarchivedJobs();
        foreach (var j in existingJobs)
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

      _onNewJobs(jobs);

      Log.Debug("Signaling new jobs available for routes");

      _sync.JobsOrQueuesChanged();
    }

    private bool IsJobCompleted(HistoricJob job, CellState st)
    {
      if (st == null) return false;

      for (int path = 1; path <= job.Processes[0].Paths.Count; path++)
      {
        if (st.JobQtyRemainingOnProc1.TryGetValue((uniq: job.UniqueStr, proc1path: path), out var qty) && qty > 0)
        {
          return false;
        }
      }

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

    public void ReplaceWorkordersForSchedule(string scheduleId, IEnumerable<PartWorkorder> newWorkorders, IEnumerable<MachineWatchInterface.ProgramEntry> programs)
    {
      var cellState = _sync.CurrentCellState();
      if (cellState == null) return;

      using (var jdb = _jobDbCfg.OpenConnection())
      {
        var errors = new List<string>();
        foreach (var w in newWorkorders ?? Enumerable.Empty<PartWorkorder>())
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

    #region Queues
    private List<InProcessMaterial> AddUnallocatedCastingToQueue(IRepository logDB, string casting, int qty, string queue, IList<string> serial, string operatorName)
    {
      if (!_settings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }

      // num proc will be set later once it is allocated to a specific job
      var mats = new List<InProcessMaterial>();

      var newMats = logDB.BulkAddNewCastingsInQueue(casting, qty, queue, serial, operatorName, reason: "SetByOperator");

      foreach (var log in newMats.Logs.Where(l => l.LogType == LogType.AddToQueue))
      {
        mats.Add(new InProcessMaterial()
        {
          MaterialID = log.Material.First().MaterialID,
          JobUnique = null,
          PartName = casting,
          Process = 0,
          Path = 1,
          Serial = log.Material.First().Serial,
          Location = new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.InQueue,
            CurrentQueue = queue,
            QueuePosition = log.LocationNum
          },
          Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Waiting
          }
        });
      }

      _sync.JobsOrQueuesChanged();

      return mats;
    }

    InProcessMaterial IJobControl.AddUnallocatedPartToQueue(string partName, string queue, string serial, string operatorName)
    {
      if (!_settings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }

      string casting = partName;

      // try and see if there is a job for this part with an actual casting
      IReadOnlyList<HistoricJob> unarchived;
      using (var jdb = _jobDbCfg.OpenConnection())
      {
        unarchived = jdb.LoadUnarchivedJobs();
      }
      var job = unarchived.FirstOrDefault(j => j.PartName == partName);
      if (job != null)
      {
        for (int path = 1; path <= job.Processes[0].Paths.Count; path++)
        {
          var jobCasting = job.Processes[0].Paths[path - 1].Casting;
          if (!string.IsNullOrEmpty(jobCasting))
          {
            casting = jobCasting;
            break;
          }
        }
      }

      using (var ldb = _jobDbCfg.OpenConnection())
      {
        return
          AddUnallocatedCastingToQueue(ldb, casting, 1, queue, string.IsNullOrEmpty(serial) ? new string[] { } : new string[] { serial }, operatorName)
          .FirstOrDefault();
      }
    }

    List<InProcessMaterial> IJobControl.AddUnallocatedCastingToQueue(string casting, int qty, string queue, IList<string> serial, string operatorName)
    {
      using (var ldb = _jobDbCfg.OpenConnection())
      {
        return AddUnallocatedCastingToQueue(ldb, casting, qty, queue, serial, operatorName);
      }
    }

    InProcessMaterial IJobControl.AddUnprocessedMaterialToQueue(string jobUnique, int process, int pathGroup, string queue, int position, string serial, string operatorName)
    {
      if (!_settings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }

      Log.Debug("Adding unprocessed material for job {job} proc {proc} to queue {queue} in position {pos} with serial {serial}",
        jobUnique, process, queue, position, serial
      );

      HistoricJob job;
      using (var jdb = _jobDbCfg.OpenConnection())
      {
        job = jdb.LoadJob(jobUnique);
      }
      if (job == null) throw new BlackMaple.MachineFramework.BadRequestException("Unable to find job " + jobUnique);

      int procToCheck = Math.Max(1, process);
      if (procToCheck > job.Processes.Count) throw new BlackMaple.MachineFramework.BadRequestException("Invalid process " + process.ToString());

      long matId;
      IEnumerable<LogEntry> logEvt;
      using (var ldb = _jobDbCfg.OpenConnection())
      {
        matId = ldb.AllocateMaterialID(jobUnique, job.PartName, job.Processes.Count);
        if (!string.IsNullOrEmpty(serial))
        {
          ldb.RecordSerialForMaterialID(
            new BlackMaple.MachineFramework.EventLogMaterial()
            {
              MaterialID = matId,
              Process = process,
              Face = ""
            },
            serial);
        }
        logEvt = ldb.RecordAddMaterialToQueue(
          matID: matId,
          process: process,
          queue: queue,
          position: position,
          operatorName: operatorName,
          reason: "SetByOperator");
      }

      _sync.JobsOrQueuesChanged();

      return new InProcessMaterial()
      {
        MaterialID = matId,
        JobUnique = jobUnique,
        PartName = job.PartName,
        Process = process,
        Path = 1,
        Serial = serial,
        Location = new InProcessMaterialLocation()
        {
          Type = InProcessMaterialLocation.LocType.InQueue,
          CurrentQueue = queue,
          QueuePosition = logEvt.LastOrDefault()?.LocationNum
        },
        Action = new InProcessMaterialAction()
        {
          Type = InProcessMaterialAction.ActionType.Waiting
        }
      };
    }

    void IJobControl.SetMaterialInQueue(long materialId, string queue, int position, string operatorName)
    {
      if (!_settings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }
      Log.Debug("Adding material {matId} to queue {queue} in position {pos}",
        materialId, queue, position
      );
      using (var ldb = _jobDbCfg.OpenConnection())
      {
        var nextProc = ldb.NextProcessForQueuedMaterial(materialId);
        var proc = (nextProc ?? 1) - 1;
        ldb.RecordAddMaterialToQueue(
          matID: materialId,
          process: proc,
          queue: queue,
          position: position,
          operatorName: operatorName,
          reason: "SetByOperator");
      }

      _sync.JobsOrQueuesChanged();
    }

    void IJobControl.RemoveMaterialFromAllQueues(IList<long> materialIds, string operatorName)
    {
      Log.Debug("Removing {@matId} from all queues", materialIds);
      using (var ldb = _jobDbCfg.OpenConnection())
      {
        ldb.BulkRemoveMaterialFromAllQueues(materialIds, operatorName);
      }
      _sync.JobsOrQueuesChanged();
    }

    void IJobControl.SignalMaterialForQuarantine(long materialId, string queue, string operatorName)
    {
      Log.Debug("Signaling {matId} for quarantine", materialId);
      if (!_settings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }

      var st = _sync.CurrentCellState();

      // first, see if it is on a pallet
      var palMat = st.Pallets
        .SelectMany(p => p.Material)
        .FirstOrDefault(m => m.Mat.MaterialID == materialId);
      var qMat = st.QueuedMaterial.FirstOrDefault(m => m.Mat.MaterialID == materialId);

      if (palMat != null && palMat.Mat.Location.Type == InProcessMaterialLocation.LocType.OnPallet)
      {
        using (var ldb = _jobDbCfg.OpenConnection())
        {
          ldb.SignalMaterialForQuarantine(
            mat: new EventLogMaterial() { MaterialID = materialId, Process = palMat.Mat.Process, Face = "" },
            pallet: palMat.Mat.Location.Pallet,
            queue: queue,
            timeUTC: null,
            operatorName: operatorName
          );
        }

        _sync.JobsOrQueuesChanged();

        return;
      }
      else if (qMat != null || (palMat != null && palMat.Mat.Location.Type == InProcessMaterialLocation.LocType.InQueue))
      {
        ((IJobControl)this).SetMaterialInQueue(materialId, queue, -1, operatorName);
      }
      else
      {
        throw new BadRequestException("Unable to find material to quarantine");
      }
    }

    public void SwapMaterialOnPallet(string pallet, long oldMatId, long newMatId, string operatorName = null)
    {
      Log.Debug("Overriding {oldMat} to {newMat} on pallet {pal}", oldMatId, newMatId, pallet);

      using (var logDb = _jobDbCfg.OpenConnection())
      {

        var o = logDb.SwapMaterialInCurrentPalletCycle(
          pallet: pallet,
          oldMatId: oldMatId,
          newMatId: newMatId,
          operatorName: operatorName
        );

        _onEditMatInLog(new EditMaterialInLogEvents()
        {
          OldMaterialID = oldMatId,
          NewMaterialID = newMatId,
          EditedEvents = o.ChangedLogEntries,
        });
      }

      _sync.JobsOrQueuesChanged();
    }

    public void InvalidatePalletCycle(long matId, int process, string oldMatPutInQueue = null, string operatorName = null)
    {
      Log.Debug("Invalidating pallet cycle for {matId} and {process}", matId, process);

      using (var logDb = _jobDbCfg.OpenConnection())
      {
        logDb.InvalidatePalletCycle(
          matId: matId,
          process: process,
          oldMatPutInQueue: oldMatPutInQueue,
          operatorName: operatorName
        );
      }

      _sync.JobsOrQueuesChanged();
    }
    #endregion
  }
}