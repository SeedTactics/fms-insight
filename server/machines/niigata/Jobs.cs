/* Copyright (c) 2019, John Lenz

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
  public class NiigataJobs : IJobControl, IDisposable
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<NiigataJobs>();
    private JobDB _jobs;
    private JobLogDB _log;
    private FMSSettings _settings;
    private ISyncPallets _sync;

    public NiigataJobs(JobDB j, JobLogDB l, FMSSettings st, ISyncPallets sy)
    {
      _jobs = j;
      _log = l;
      _sync = sy;
      _settings = st;

      _sync.OnPalletsChanged += BuildCurrentStatus;
    }

    void IDisposable.Dispose()
    {
      _sync.OnPalletsChanged -= BuildCurrentStatus;
    }

    #region Status
    public event NewCurrentStatus OnNewCurrentStatus;
    private object _curStLock = new object();
    private CurrentStatus _lastStatus = new CurrentStatus();

    public CurrentStatus GetCurrentStatus()
    {
      lock (_curStLock)
      {
        return _lastStatus;
      }
    }

    private void BuildCurrentStatus(IList<JobPallet> palMaster)
    {
      var curStatus = new CurrentStatus();
      foreach (var k in _settings.Queues) curStatus.QueueSizes[k.Key] = k.Value;

      // jobs
      var jobs = _jobs.LoadUnarchivedJobs();
      foreach (var j in jobs.Jobs)
      {
        var curJob = new InProcessJob(j);
        curStatus.Jobs.Add(curJob.UniqueStr, curJob);
        var evts = _log.GetLogForJobUnique(j.UniqueStr);
        foreach (var e in evts)
        {
          if (e.LogType == LogType.LoadUnloadCycle && e.Result == "UNLOAD")
          {
            foreach (var mat in e.Material)
            {
              if (mat.JobUniqueStr == j.UniqueStr)
              {
                int matPath = 1;
                for (int path = 1; path <= j.GetNumPaths(mat.Process); path++)
                {
                  if (j.PlannedPallets(mat.Process, path).Contains(e.Pallet))
                  {
                    matPath = path;
                    break;
                  }
                }
                curJob.AdjustCompleted(mat.Process, matPath, x => x + 1);
              }
            }
          }
        }

        foreach (var d in _jobs.LoadDecrementsForJob(j.UniqueStr))
          curJob.Decrements.Add(d);
      }

      // pallets
      foreach (var pal in palMaster)
      {
        curStatus.Pallets.Add(pal.Master.PalletNum.ToString(), new PalletStatus()
        {
          Pallet = pal.Master.PalletNum.ToString(),
          FixtureOnPallet = "",
          OnHold = pal.Master.Skip,
          CurrentPalletLocation = BuildCurrentLocation(pal),
          NumFaces = pal.Master.NumFaces
        });
      }

      // material on pallets
      var matsOnPallets = new HashSet<long>();
      foreach (var pal in palMaster)
      {
        foreach (var mat in pal.Material)
        {
          matsOnPallets.Add(mat.MaterialID);
          curStatus.Material.Add(mat);
        }
      }

      // queued mats
      foreach (var mat in _log.GetMaterialInAllQueues())
      {
        if (matsOnPallets.Contains(mat.MaterialID)) continue;
        var matLogs = _log.GetLogForMaterial(mat.MaterialID);
        int lastProc = 0;
        foreach (var entry in _log.GetLogForMaterial(mat.MaterialID))
        {
          foreach (var entryMat in entry.Material)
          {
            if (entryMat.MaterialID == mat.MaterialID)
            {
              lastProc = Math.Max(lastProc, entryMat.Process);
            }
          }
        }
        var matDetails = _log.GetMaterialDetails(mat.MaterialID);
        curStatus.Material.Add(new InProcessMaterial()
        {
          MaterialID = mat.MaterialID,
          JobUnique = mat.Unique,
          PartName = mat.PartName,
          Process = lastProc,
          Path = 1,
          Serial = matDetails?.Serial,
          WorkorderId = matDetails?.Workorder,
          SignaledInspections =
                _log.LookupInspectionDecisions(mat.MaterialID)
                .Where(x => x.Inspect)
                .Select(x => x.InspType)
                .Distinct()
                .ToList(),
          Location = new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.InQueue,
            CurrentQueue = mat.Queue,
            QueuePosition = mat.Position,
          },
          Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Waiting
          }
        });
      }

      //alarms
      foreach (var pal in palMaster)
      {
        if (pal.Master.Alarm)
        {
          curStatus.Alarms.Add("Pallet " + pal.Master.PalletNum.ToString() + " has alarm");
        }
      }

      lock (_curStLock)
      {
        _lastStatus = curStatus;
      }
      OnNewCurrentStatus?.Invoke(curStatus);
    }
    #endregion

    #region Jobs
    public List<string> CheckValidRoutes(IEnumerable<JobPlan> newJobs)
    {
      return new List<string>();
    }

    public void AddJobs(NewJobs jobs, string expectedPreviousScheduleId)
    {
      if (jobs.ArchiveCompletedJobs)
      {
        var existingJobs = _jobs.LoadUnarchivedJobs();
        foreach (var j in existingJobs.Jobs)
        {
          if (IsJobCompleted(j))
          {
            _jobs.ArchiveJob(j.UniqueStr);
          }
        }
      }

      foreach (var j in jobs.Jobs)
      {
        j.JobCopiedToSystem = true;
      }
      _jobs.AddJobs(jobs, expectedPreviousScheduleId);

      _sync.JobsOrQueuesChanged();
    }

    private bool IsJobCompleted(JobPlan job)
    {
      var planned = Enumerable.Range(1, job.GetNumPaths(process: 1)).Sum(job.GetPlannedCyclesOnFirstProcess);
      foreach (var decr in _jobs.LoadDecrementsForJob(job.UniqueStr))
      {
        planned -= decr.Quantity;
      }

      var evts = _log.GetLogForJobUnique(job.UniqueStr);
      var completed = 0;
      foreach (var e in evts)
      {
        if (e.LogType == LogType.LoadUnloadCycle && e.Result == "UNLOAD")
        {
          foreach (var mat in e.Material)
          {
            if (mat.JobUniqueStr == job.UniqueStr && mat.Process == mat.NumProcesses)
            {
              completed += 1;
            }
          }
        }
      }
      return completed >= planned;
    }

    public List<JobAndDecrementQuantity> DecrementJobQuantites(long loadDecrementsStrictlyAfterDecrementId)
    {
      _sync.DecrementPlannedButNotStartedQty();
      return _jobs.LoadDecrementQuantitiesAfter(loadDecrementsStrictlyAfterDecrementId);
    }

    public List<JobAndDecrementQuantity> DecrementJobQuantites(DateTime loadDecrementsAfterTimeUTC)
    {
      _sync.DecrementPlannedButNotStartedQty();
      return _jobs.LoadDecrementQuantitiesAfter(loadDecrementsAfterTimeUTC);
    }
    #endregion

    #region Queues
    public void AddUnallocatedCastingToQueue(string part, string queue, int position, string serial)
    {
      if (!_settings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }
      // num proc will be set later once it is allocated to a specific job
      var matId = _log.AllocateMaterialIDForCasting(part, 1);

      Log.Debug("Adding unprocessed casting for part {part} to queue {queue} in position {pos} with serial {serial}. " +
                "Assigned matId {matId}",
        part, queue, position, serial, matId
      );

      if (!string.IsNullOrEmpty(serial))
      {
        _log.RecordSerialForMaterialID(
          new BlackMaple.MachineFramework.JobLogDB.EventLogMaterial()
          {
            MaterialID = matId,
            Process = 0,
            Face = ""
          },
          serial);
      }
      _log.RecordAddMaterialToQueue(matId, 0, queue, position);
      _sync.JobsOrQueuesChanged();
    }

    public void AddUnprocessedMaterialToQueue(string jobUnique, int process, string queue, int position, string serial)
    {
      if (!_settings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }

      Log.Debug("Adding unprocessed material for job {job} proc {proc} to queue {queue} in position {pos} with serial {serial}",
        jobUnique, process, queue, position, serial
      );

      var job = _jobs.LoadJob(jobUnique);
      if (job == null) throw new BlackMaple.MachineFramework.BadRequestException("Unable to find job " + jobUnique);
      var matId = _log.AllocateMaterialID(jobUnique, job.PartName, job.NumProcesses);
      if (!string.IsNullOrEmpty(serial))
      {
        _log.RecordSerialForMaterialID(
          new BlackMaple.MachineFramework.JobLogDB.EventLogMaterial()
          {
            MaterialID = matId,
            Process = process,
            Face = ""
          },
          serial);
      }
      _log.RecordAddMaterialToQueue(matId, process, queue, position);
      _sync.JobsOrQueuesChanged();
    }

    public void SetMaterialInQueue(long materialId, string queue, int position)
    {
      if (!_settings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }
      Log.Debug("Adding material {matId} to queue {queue} in position {pos}",
        materialId, queue, position
      );
      var proc =
        _log.GetLogForMaterial(materialId)
        .SelectMany(e => e.Material)
        .Where(m => m.MaterialID == materialId)
        .Select(m => m.Process)
        .DefaultIfEmpty(0)
        .Max();
      _log.RecordAddMaterialToQueue(materialId, proc, queue, position);
      _sync.JobsOrQueuesChanged();
    }

    public void RemoveMaterialFromAllQueues(long materialId)
    {
      Log.Debug("Removing {matId} from all queues", materialId);
      _log.RecordRemoveMaterialFromAllQueues(materialId);
      _sync.JobsOrQueuesChanged();
    }
    #endregion


    private static PalletLocation BuildCurrentLocation(JobPallet pal)
    {
      switch (pal.Loc)
      {
        case LoadUnloadLoc l:
          return new PalletLocation(PalletLocationEnum.LoadUnload, "L/U", l.LoadStation);
        case MachineOrWashLoc m:
          switch (m.Position)
          {
            case MachineOrWashLoc.Rotary.Input:
            case MachineOrWashLoc.Rotary.Output:
              return new PalletLocation(PalletLocationEnum.MachineQueue, "MC", m.Station);
            case MachineOrWashLoc.Rotary.Worktable:
              return new PalletLocation(PalletLocationEnum.Machine, "MC", m.Station);
          }
          break;
        case StockerLoc s:
          return new PalletLocation(PalletLocationEnum.Buffer, "Buffer", pal.Master.PalletNum);
        case CartLoc c:
          return new PalletLocation(PalletLocationEnum.Cart, "Cart", 1);
      }
      return new PalletLocation(PalletLocationEnum.Buffer, "Buffer", 0);
    }
  }
}