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
  public class NiigataJobs : IJobControl, IDisposable
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<NiigataJobs>();
    private JobDB.Config _jobDbCfg;
    private JobLogDB _log;
    private FMSSettings _settings;
    private ISyncPallets _sync;
    private readonly NiigataStationNames _statNames;

    public NiigataJobs(JobDB.Config j, JobLogDB l, FMSSettings st, ISyncPallets sy, NiigataStationNames statNames)
    {
      _jobDbCfg = j;
      _log = l;
      _sync = sy;
      _settings = st;
      _statNames = statNames;

      _sync.OnPalletsChanged += OnNewCellState;
    }

    public void Dispose()
    {
      _sync.OnPalletsChanged -= OnNewCellState;
    }

    #region Status
    public event NewCurrentStatus OnNewCurrentStatus;
    private object _curStLock = new object();
    private CellState _lastCellState = null;

    private void OnNewCellState(JobDB jobDB, CellState s)
    {
      lock (_curStLock)
      {
        _lastCellState = s;
      }
      OnNewCurrentStatus?.Invoke(BuildCurrentStatus(jobDB, s));
    }

    public CurrentStatus GetCurrentStatus()
    {
      lock (_curStLock)
      {
        if (_lastCellState == null)
        {
          return new CurrentStatus();
        }
        else
        {
          using (var jdb = _jobDbCfg.OpenConnection())
          {
            return BuildCurrentStatus(jdb, _lastCellState);
          }
        }
      }
    }

    private CurrentStatus BuildCurrentStatus(JobDB jobDB, CellState status)
    {
      var curStatus = new CurrentStatus();
      foreach (var k in _settings.Queues) curStatus.QueueSizes[k.Key] = k.Value;

      // jobs
      foreach (var j in status.Schedule.Jobs)
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
                var details = _log.GetMaterialDetails(mat.MaterialID);
                int matPath = details.Paths != null && details.Paths.ContainsKey(mat.Process) ? details.Paths[mat.Process] : 1;
                curJob.AdjustCompleted(mat.Process, matPath, x => x + 1);
              }
            }
          }
        }

        foreach (var d in jobDB.LoadDecrementsForJob(j.UniqueStr))
          curJob.Decrements.Add(d);
      }

      // set precedence
      var proc1paths =
        curStatus.Jobs.Values.SelectMany(job =>
          Enumerable.Range(1, job.GetNumPaths(1)).Select(proc1path => (job, proc1path))
        )
        .OrderBy(x => x.job.RouteStartingTimeUTC)
        .ThenBy(x => x.job.GetSimulatedStartingTimeUTC(1, x.proc1path));
      int precedence = 0;
      foreach (var (j, proc1path) in proc1paths)
      {
        precedence += 1;
        j.SetPrecedence(1, proc1path, precedence);

        // now set the rest of the processes
        for (int proc = 2; proc <= j.NumProcesses; proc++)
        {
          for (int path = 1; path <= j.GetNumPaths(proc); path++)
          {
            if (j.GetPathGroup(proc, path) == j.GetPathGroup(1, proc1path))
            {
              // only set if it hasn't already been set
              if (j.GetPrecedence(proc, path) <= 0)
              {
                j.SetPrecedence(proc, path, precedence);
              }
            }
          }
        }
      }


      // pallets
      foreach (var pal in status.Pallets)
      {
        curStatus.Pallets.Add(pal.Status.Master.PalletNum.ToString(), new MachineWatchInterface.PalletStatus()
        {
          Pallet = pal.Status.Master.PalletNum.ToString(),
          FixtureOnPallet = "",
          OnHold = pal.Status.Master.Skip,
          CurrentPalletLocation = pal.Status.CurStation.Location,
          NumFaces = pal.Material.Count > 0 ? pal.Material.Max(m => m.Mat.Location.Face ?? 1) : 0
        });
      }

      // material on pallets
      foreach (var mat in status.Pallets.SelectMany(pal => pal.Material))
      {
        curStatus.Material.Add(mat.Mat);
      }

      // queued mats
      foreach (var mat in status.QueuedMaterial)
      {
        curStatus.Material.Add(mat);
      }

      // tool loads/unloads
      foreach (var pal in status.Pallets.Where(p => p.Status.Master.ForLongToolMaintenance))
      {
        switch (pal.Status.CurrentStep)
        {
          case LoadStep load:
            if (pal.Status.Tracking.BeforeCurrentStep)
            {
              curStatus.Material.Add(new InProcessMaterial()
              {
                MaterialID = -1,
                JobUnique = null,
                PartName = "LongTool",
                Process = 1,
                Path = 1,
                Location = new InProcessMaterialLocation()
                {
                  Type = InProcessMaterialLocation.LocType.Free,
                },
                Action = new InProcessMaterialAction()
                {
                  Type = InProcessMaterialAction.ActionType.Loading,
                  LoadOntoPallet = pal.Status.Master.PalletNum.ToString(),
                  LoadOntoFace = 1,
                  ProcessAfterLoad = 1,
                  PathAfterLoad = 1
                }
              });
            }
            break;
          case UnloadStep unload:
            if (pal.Status.Tracking.BeforeCurrentStep)
            {
              curStatus.Material.Add(new InProcessMaterial()
              {
                MaterialID = -1,
                JobUnique = null,
                PartName = "LongTool",
                Process = 1,
                Path = 1,
                Location = new InProcessMaterialLocation()
                {
                  Type = InProcessMaterialLocation.LocType.OnPallet,
                  Pallet = pal.Status.Master.PalletNum.ToString(),
                  Face = 1
                },
                Action = new InProcessMaterialAction()
                {
                  Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
                }
              });
            }
            break;
        }

      }

      //alarms
      foreach (var pal in status.Pallets)
      {
        if (pal.Status.Tracking.Alarm)
        {
          curStatus.Alarms.Add("Pallet " + pal.Status.Master.PalletNum.ToString() + " has alarm " + pal.Status.Tracking.AlarmCode.ToString());
        }
      }
      foreach (var mc in status.Status.Machines.Values)
      {
        if (mc.Alarm)
        {
          curStatus.Alarms.Add("Machine " + mc.MachineNumber.ToString() + " has an alarm");
        }
      }
      if (status.Status.Alarm)
      {
        curStatus.Alarms.Add("ICC has an alarm");
      }

      return curStatus;

    }
    #endregion

    #region Jobs
    public event NewJobsDelegate OnNewJobs;

    public List<string> CheckValidRoutes(IEnumerable<JobPlan> newJobs)
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
                if (stop.ProgramRevision.HasValue && stop.ProgramRevision.Value < 0)
                {
                  errors.Add("Part " + j.PartName + " is not allowed to have negative revision");
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
                      lock (_curStLock)
                      {
                        if (!_lastCellState.Status.Programs.Values.Any(p => p.ProgramNum == progNum && !AssignNewRoutesOnPallets.IsInsightProgram(p)))
                        {
                          errors.Add("Part " + j.PartName + " program " + stop.ProgramName + " is neither included in the download nor found in the cell controller");
                        }
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

    public void AddJobs(NewJobs jobs, string expectedPreviousScheduleId)
    {
      using (var jdb = _jobDbCfg.OpenConnection())
      {
        var errors = CheckJobs(jdb, jobs);
        if (errors.Any())
        {
          throw new BadRequestException(string.Join(Environment.NewLine, errors));
        }

        CellState curSt;
        lock (_curStLock)
        {
          curSt = _lastCellState;
        }

        var existingJobs = jdb.LoadUnarchivedJobs();
        foreach (var j in existingJobs.Jobs)
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

      OnNewJobs?.Invoke(jobs);

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

    public List<JobAndDecrementQuantity> DecrementJobQuantites(long loadDecrementsStrictlyAfterDecrementId)
    {
      using (var jdb = _jobDbCfg.OpenConnection())
      {
        _sync.DecrementPlannedButNotStartedQty(jdb);
        return jdb.LoadDecrementQuantitiesAfter(loadDecrementsStrictlyAfterDecrementId);
      }
    }

    public List<JobAndDecrementQuantity> DecrementJobQuantites(DateTime loadDecrementsAfterTimeUTC)
    {
      using (var jdb = _jobDbCfg.OpenConnection())
      {
        _sync.DecrementPlannedButNotStartedQty(jdb);
        return jdb.LoadDecrementQuantitiesAfter(loadDecrementsAfterTimeUTC);
      }
    }

    public void SetJobComment(string jobUnique, string comment)
    {
      using (var jdb = _jobDbCfg.OpenConnection())
      {
        jdb.SetJobComment(jobUnique, comment);
      }
      _sync.JobsOrQueuesChanged();
    }
    #endregion

    #region Queues
    public InProcessMaterial AddUnallocatedPartToQueue(string partName, string queue, int position, string serial, string operatorName)
    {
      if (!_settings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }

      string casting = partName;

      // try and see if there is a job for this part with an actual casting
      PlannedSchedule sch;
      using (var jdb = _jobDbCfg.OpenConnection())
      {
        sch = jdb.LoadUnarchivedJobs();
      }
      var job = sch.Jobs.FirstOrDefault(j => j.PartName == partName);
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

      return
        AddUnallocatedCastingToQueue(casting, 1, queue, position, string.IsNullOrEmpty(serial) ? new string[] { } : new string[] { serial }, operatorName)
        .FirstOrDefault();
    }

    public List<InProcessMaterial> AddUnallocatedCastingToQueue(string casting, int qty, string queue, int position, IList<string> serial, string operatorName)
    {
      if (!_settings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }

      // num proc will be set later once it is allocated to a specific job
      var mats = new List<InProcessMaterial>();
      for (int i = 0; i < qty; i++)
      {
        var matId = _log.AllocateMaterialIDForCasting(casting);

        Log.Debug("Adding unprocessed casting for casting {casting} to queue {queue} in position {pos} with serial {serial}. " +
                  "Assigned matId {matId}",
          casting, queue, position, serial, matId
        );

        if (i < serial.Count)
        {
          _log.RecordSerialForMaterialID(
            new BlackMaple.MachineFramework.JobLogDB.EventLogMaterial()
            {
              MaterialID = matId,
              Process = 0,
              Face = ""
            },
            serial[i]);
        }
        var logEvt = _log.RecordAddMaterialToQueue(matId, 0, queue, position >= 0 ? position + i : -1, operatorName: operatorName);
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

    public InProcessMaterial AddUnprocessedMaterialToQueue(string jobUnique, int process, int pathGroup, string queue, int position, string serial, string operatorName)
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
      var logEvt = _log.RecordAddMaterialToQueue(matId, process, queue, position, operatorName: operatorName);
      _log.RecordPathForProcess(matId, Math.Max(1, process), path.Value);
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

    public void SetMaterialInQueue(long materialId, string queue, int position, string operatorName)
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
      _log.RecordAddMaterialToQueue(materialId, proc, queue, position, operatorName);
      _sync.JobsOrQueuesChanged();
    }

    public void RemoveMaterialFromAllQueues(IList<long> materialIds, string operatorName)
    {
      Log.Debug("Removing {@matId} from all queues", materialIds);
      foreach (var materialId in materialIds)
        _log.RecordRemoveMaterialFromAllQueues(materialId, operatorName);
      _sync.JobsOrQueuesChanged();
    }
    #endregion
  }
}