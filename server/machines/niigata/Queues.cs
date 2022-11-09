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
  public class NiigataQueues : IQueueControl
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<NiigataQueues>();

    public event EditMaterialInLogDelegate OnEditMaterialInLog;

    private FMSSettings _settings;
    private ISyncPallets _sync;
    private RepositoryConfig _jobDbCfg;

    public NiigataQueues(RepositoryConfig j, FMSSettings st, ISyncPallets sy)
    {
      _settings = st;
      _sync = sy;
      _jobDbCfg = j;
    }

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

    public InProcessMaterial AddUnallocatedPartToQueue(string partName, string queue, string serial, string operatorName)
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

    public List<InProcessMaterial> AddUnallocatedCastingToQueue(string casting, int qty, string queue, IList<string> serial, string operatorName)
    {
      using (var ldb = _jobDbCfg.OpenConnection())
      {
        return AddUnallocatedCastingToQueue(ldb, casting, qty, queue, serial, operatorName);
      }
    }

    public InProcessMaterial AddUnprocessedMaterialToQueue(string jobUnique, int process, string queue, int position, string serial, string operatorName)
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

    public void SetMaterialInQueue(long materialId, string queue, int position, string operatorName)
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

    public void RemoveMaterialFromAllQueues(IList<long> materialIds, string operatorName)
    {
      Log.Debug("Removing {@matId} from all queues", materialIds);
      using (var ldb = _jobDbCfg.OpenConnection())
      {
        ldb.BulkRemoveMaterialFromAllQueues(materialIds, operatorName);
      }
      _sync.JobsOrQueuesChanged();
    }

    public void SignalMaterialForQuarantine(long materialId, string queue, string operatorName)
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
        this.SetMaterialInQueue(materialId, queue, -1, operatorName);
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
          operatorName: operatorName,
          quarantineQueue: _settings.QuarantineQueue
        );

        OnEditMaterialInLog?.Invoke(new EditMaterialInLogEvents()
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
  }
}