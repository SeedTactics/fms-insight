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

namespace BlackMaple.FMSInsight.Niigata
{
  public class NiigataJobs : IJobControl
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<NiigataJobs>();
    private JobDB.Config _jobDbCfg;
    private EventLogDB.Config _logDbCfg;
    private FMSSettings _settings;
    private ISyncPallets _sync;
    private readonly NiigataStationNames _statNames;
    private Action<NewJobs> _onNewJobs;
    private Action<EditMaterialInLogEvents> _onEditMatInLog;
    private bool _requireRawMatQueue;
    private bool _requireInProcessQueues;

    public NiigataJobs(JobDB.Config j, EventLogDB.Config l, FMSSettings st, ISyncPallets sy, NiigataStationNames statNames,
                       bool requireRawMatQ, bool requireInProcQ, Action<NewJobs> onNewJobs, Action<EditMaterialInLogEvents> onEditMatInLog)
    {
      _onNewJobs = onNewJobs;
      _jobDbCfg = j;
      _logDbCfg = l;
      _sync = sy;
      _settings = st;
      _statNames = statNames;
      _requireRawMatQueue = requireRawMatQ;
      _requireInProcessQueues = requireInProcQ;
      _onEditMatInLog = onEditMatInLog;
    }

    CurrentStatus IJobControl.GetCurrentStatus()
    {
      using (var jdb = _jobDbCfg.OpenConnection())
      using (var ldb = _logDbCfg.OpenConnection())
      {
        return BuildCurrentStatus.Build(jdb, ldb, _sync.CurrentCellState(), _settings);
      }
    }

    #region Jobs
    List<string> IJobControl.CheckValidRoutes(IEnumerable<JobPlan> newJobs)
    {
      using (var jdb = _jobDbCfg.OpenConnection())
      {
        return CheckJobs(jdb, new NewJobs()
        {
          Jobs = newJobs.ToList()
        });
      }
    }

    public List<string> CheckJobs(JobDB jobDB, NewJobs jobs)
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
        for (var proc = 1; proc <= j.NumProcesses; proc++)
        {
          for (var path = 1; path <= j.GetNumPaths(proc); path++)
          {
            if (!j.LoadStations(proc, path).Any())
            {
              errors.Add("Part " + j.PartName + " does not have any assigned load stations");
            }
            if (!j.UnloadStations(proc, path).Any())
            {
              errors.Add("Part " + j.PartName + " does not have any assigned load stations");
            }
            if (string.IsNullOrEmpty(j.PlannedFixture(proc, path).fixture))
            {
              errors.Add("Part " + j.PartName + " does not have an assigned fixture");
            }
            if (!j.PlannedPallets(proc, path).Any())
            {
              errors.Add("Part " + j.PartName + " does not have any pallets");
            }
            foreach (var pal in j.PlannedPallets(proc, path))
            {
              if (!int.TryParse(pal, out var p))
              {
                errors.Add("Part " + j.PartName + " has non-integer pallets");
              }
            }
            if (!string.IsNullOrEmpty(j.GetInputQueue(proc, path)) && !_settings.Queues.ContainsKey(j.GetInputQueue(proc, path)))
            {
              errors.Add(" Part " + j.PartName + " has an input queue " + j.GetInputQueue(proc, path) + " which is not configured as a local queue in FMS Insight.");
            }
            if (!string.IsNullOrEmpty(j.GetOutputQueue(proc, path)) && !_settings.Queues.ContainsKey(j.GetOutputQueue(proc, path)))
            {
              errors.Add(" Part " + j.PartName + " has an output queue " + j.GetOutputQueue(proc, path) + " which is not configured as a queue in FMS Insight.");
            }
            if (_requireRawMatQueue && proc == 1 && string.IsNullOrEmpty(j.GetInputQueue(proc, path)))
            {
              errors.Add("Input queue is required on process 1 for part " + j.PartName);
            }
            if (_requireInProcessQueues && proc > 1 && string.IsNullOrEmpty(j.GetInputQueue(proc, path)))
            {
              errors.Add("Input queue required for part " + j.PartName + ", process " + proc.ToString());
            }
            if (_requireInProcessQueues && proc < j.NumProcesses && string.IsNullOrEmpty(j.GetOutputQueue(proc, path)))
            {
              errors.Add("Output queue required for part " + j.PartName + ", process " + proc.ToString());
            }

            foreach (var stop in j.GetMachiningStop(proc, path))
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
                if (string.IsNullOrEmpty(stop.ProgramName))
                {
                  errors.Add("Part " + j.PartName + " has no assigned program");
                }
                if (stop.ProgramRevision.HasValue && stop.ProgramRevision.Value > 0)
                {
                  var existing = jobDB.LoadProgram(stop.ProgramName, stop.ProgramRevision.Value) != null;
                  var newProg = jobs.Programs != null && jobs.Programs.Any(p => p.ProgramName == stop.ProgramName && p.Revision == stop.ProgramRevision);
                  if (!existing && !newProg)
                  {
                    errors.Add("Part " + j.PartName + " program " + stop.ProgramName + " rev" + stop.ProgramRevision.Value.ToString() + " is not found");
                  }
                }
                else
                {
                  var existing = jobDB.LoadMostRecentProgram(stop.ProgramName) != null;
                  var newProg = jobs.Programs != null && jobs.Programs.Any(p => p.ProgramName == stop.ProgramName);
                  if (!existing && !newProg)
                  {
                    if (int.TryParse(stop.ProgramName, out int progNum))
                    {
                      if (!cellState.Status.Programs.Values.Any(p => p.ProgramNum == progNum && !AssignNewRoutesOnPallets.IsInsightProgram(p)))
                      {
                        errors.Add("Part " + j.PartName + " program " + stop.ProgramName + " is neither included in the download nor found in the cell controller");
                      }
                    }
                    else
                    {
                      errors.Add("Part " + j.PartName + " program " + stop.ProgramName + " is neither included in the download nor is an integer");
                    }
                  }
                }
              }

            }
          }
        }
      }
      return errors;
    }

    void IJobControl.AddJobs(NewJobs jobs, string expectedPreviousScheduleId)
    {
      using (var jdb = _jobDbCfg.OpenConnection())
      {
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

        foreach (var j in jobs.Jobs)
        {
          j.JobCopiedToSystem = true;
        }
        jdb.AddJobs(jobs, expectedPreviousScheduleId);
      }

      _onNewJobs(jobs);

      _sync.JobsOrQueuesChanged();
    }

    private bool IsJobCompleted(JobPlan job, CellState st)
    {
      if (st == null) return false;

      for (int path = 1; path <= job.GetNumPaths(process: 1); path++)
      {
        if (st.JobQtyRemainingOnProc1.TryGetValue((uniq: job.UniqueStr, proc1path: path), out var qty) && qty > 0)
        {
          return false;
        }
      }

      var matInProc =
        st.Pallets
        .SelectMany(p => p.Material)
        .Select(f => f.Mat)
        .Concat(st.QueuedMaterial)
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
      using (var ldb = _logDbCfg.OpenConnection())
      {
        _sync.DecrementPlannedButNotStartedQty(jdb, ldb);
        return jdb.LoadDecrementQuantitiesAfter(loadDecrementsStrictlyAfterDecrementId);
      }
    }

    List<JobAndDecrementQuantity> IJobControl.DecrementJobQuantites(DateTime loadDecrementsAfterTimeUTC)
    {
      using (var jdb = _jobDbCfg.OpenConnection())
      using (var ldb = _logDbCfg.OpenConnection())
      {
        _sync.DecrementPlannedButNotStartedQty(jdb, ldb);
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
    #endregion

    #region Queues
    private List<InProcessMaterial> AddUnallocatedCastingToQueue(EventLogDB logDB, string casting, int qty, string queue, int position, IList<string> serial, string operatorName)
    {
      if (!_settings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }

      // num proc will be set later once it is allocated to a specific job
      var mats = new List<InProcessMaterial>();
      for (int i = 0; i < qty; i++)
      {
        var matId = logDB.AllocateMaterialIDForCasting(casting);

        Log.Debug("Adding unprocessed casting for casting {casting} to queue {queue} in position {pos} with serial {serial}. " +
                  "Assigned matId {matId}",
          casting, queue, position, serial, matId
        );

        if (i < serial.Count)
        {
          logDB.RecordSerialForMaterialID(
            new BlackMaple.MachineFramework.EventLogDB.EventLogMaterial()
            {
              MaterialID = matId,
              Process = 0,
              Face = ""
            },
            serial[i]);
        }
        var logEvt = logDB.RecordAddMaterialToQueue(
          matID: matId,
          process: 0,
          queue: queue,
          position: position >= 0 ? position + i : -1,
          operatorName: operatorName,
          reason: "SetByOperator");
        mats.Add(new InProcessMaterial()
        {
          MaterialID = matId,
          JobUnique = null,
          PartName = casting,
          Process = 0,
          Path = 1,
          Serial = i < serial.Count ? serial[i] : null,
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
        });
      }

      _sync.JobsOrQueuesChanged();

      return mats;
    }

    InProcessMaterial IJobControl.AddUnallocatedPartToQueue(string partName, string queue, int position, string serial, string operatorName)
    {
      if (!_settings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }

      string casting = partName;

      // try and see if there is a job for this part with an actual casting
      List<JobPlan> unarchived;
      using (var jdb = _jobDbCfg.OpenConnection())
      {
        unarchived = jdb.LoadUnarchivedJobs();
      }
      var job = unarchived.FirstOrDefault(j => j.PartName == partName);
      if (job != null)
      {
        for (int path = 1; path <= job.GetNumPaths(1); path++)
        {
          if (!string.IsNullOrEmpty(job.GetCasting(path)))
          {
            casting = job.GetCasting(path);
            break;
          }
        }
      }

      using (var ldb = _logDbCfg.OpenConnection())
      {
        return
          AddUnallocatedCastingToQueue(ldb, casting, 1, queue, position, string.IsNullOrEmpty(serial) ? new string[] { } : new string[] { serial }, operatorName)
          .FirstOrDefault();
      }
    }

    List<InProcessMaterial> IJobControl.AddUnallocatedCastingToQueue(string casting, int qty, string queue, int position, IList<string> serial, string operatorName)
    {
      using (var ldb = _logDbCfg.OpenConnection())
      {
        return AddUnallocatedCastingToQueue(ldb, casting, qty, queue, position, serial, operatorName);
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

      JobPlan job;
      using (var jdb = _jobDbCfg.OpenConnection())
      {
        job = jdb.LoadJob(jobUnique);
      }
      if (job == null) throw new BlackMaple.MachineFramework.BadRequestException("Unable to find job " + jobUnique);

      int? path = null;
      for (var p = 1; p <= job.GetNumPaths(Math.Max(1, process)); p++)
      {
        if (job.GetPathGroup(Math.Max(1, process), p) == pathGroup)
        {
          path = p;
          break;
        }
      }
      if (!path.HasValue) throw new BlackMaple.MachineFramework.BadRequestException("Unable to find path group " + pathGroup.ToString() + " for job " + jobUnique + " and process " + process.ToString());

      long matId;
      IEnumerable<LogEntry> logEvt;
      using (var ldb = _logDbCfg.OpenConnection())
      {
        matId = ldb.AllocateMaterialID(jobUnique, job.PartName, job.NumProcesses);
        if (!string.IsNullOrEmpty(serial))
        {
          ldb.RecordSerialForMaterialID(
            new BlackMaple.MachineFramework.EventLogDB.EventLogMaterial()
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
        ldb.RecordPathForProcess(matId, Math.Max(1, process), path.Value);
      }

      _sync.JobsOrQueuesChanged();

      return new InProcessMaterial()
      {
        MaterialID = matId,
        JobUnique = jobUnique,
        PartName = job.PartName,
        Process = process,
        Path = path.Value,
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
      using (var ldb = _logDbCfg.OpenConnection())
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
      using (var ldb = _logDbCfg.OpenConnection())
      {
        foreach (var materialId in materialIds)
        {
          var nextProc = ldb.NextProcessForQueuedMaterial(materialId);
          var proc = (nextProc ?? 1) - 1;
          ldb.RecordRemoveMaterialFromAllQueues(materialId, proc, operatorName);
        }
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
      var qMat = st.QueuedMaterial.FirstOrDefault(m => m.MaterialID == materialId);

      if (palMat != null && palMat.Mat.Location.Type == InProcessMaterialLocation.LocType.OnPallet)
      {
        using (var ldb = _logDbCfg.OpenConnection())
        {
          ldb.SignalMaterialForQuarantine(
            mat: new EventLogDB.EventLogMaterial() { MaterialID = materialId, Process = palMat.Mat.Process, Face = "" },
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

    public void SwapMaterialOnPallet(string pallet, long oldMatId, long newMatId, string oldMatPutInQueue = null, string operatorName = null)
    {
      Log.Debug("Overriding {oldMat} to {newMat} on pallet {pal}", oldMatId, newMatId, pallet);

      using (var logDb = _logDbCfg.OpenConnection())
      using (var jdb = _jobDbCfg.OpenConnection())
      {

        var o = logDb.SwapMaterialInCurrentPalletCycle(
          pallet: pallet,
          oldMatId: oldMatId,
          newMatId: newMatId,
          oldMatPutInQueue: oldMatPutInQueue,
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
    #endregion
  }
}