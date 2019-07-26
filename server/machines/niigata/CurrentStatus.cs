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
  public interface IBuildCurrentStatus
  {
    CurrentStatus GetCurrentStatus();
    bool IsJobCompleted(JobPlan job);
    int CountStartedOrInProcess(JobPlan job);
  }

  public class BuildCurrentStatus : IBuildCurrentStatus
  {
    private JobDB _jobs;
    private JobLogDB _log;
    private ICurrentPallets _curPal;
    private FMSSettings fmsSettings;

    public BuildCurrentStatus(JobDB j, JobLogDB l, FMSSettings s, ICurrentPallets c)
    {
      _jobs = j;
      _log = l;
      _curPal = c;
      fmsSettings = s;
    }

    public CurrentStatus GetCurrentStatus()
    {
      var curStatus = new CurrentStatus();
      foreach (var k in fmsSettings.Queues) curStatus.QueueSizes[k.Key] = k.Value;

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
      var palMaster = _curPal.LoadPallets();
      foreach (var pal in palMaster.Values)
      {
        curStatus.Pallets.Add(pal.PalletNum.ToString(), new PalletStatus()
        {
          Pallet = pal.PalletNum.ToString(),
          FixtureOnPallet = "",
          OnHold = pal.Skip,
          CurrentPalletLocation = BuildCurrentLocation(pal),
          NumFaces = pal.NumFaces
        });
      }

      // material on pallets
      var matsOnPallets = new HashSet<long>();
      foreach (var pal in palMaster.Values)
      {
        switch (pal.Tracking.RouteNum)
        {
          //before loading
          case 1 when pal.Tracking.Before:
            foreach (var m in MaterialLoadingOntoPallet(pal))
            {
              matsOnPallets.Add(m.MaterialID);
              curStatus.Material.Add(m);
            }
            break;

          // after loading, before machining, after machining -> mat in waiting state
          case 1 when !pal.Tracking.Before:
          case 2 when pal.Tracking.Before:
          case 2 when !pal.Tracking.Before:
            foreach (var m in MaterialForPallet(pal.PalletNum.ToString()))
            {
              // TODO: get current machine data, change mat to machining
              matsOnPallets.Add(m.MaterialID);
              curStatus.Material.Add(m);
            }
            break;

          // before unload -> mat in unload state
          case 3 when pal.Tracking.Before:
            foreach (var m in MaterialForPallet(pal.PalletNum.ToString()))
            {
              var job = _jobs.LoadJob(m.JobUnique);
              if (job != null && m.Process < job.NumProcesses)
              {
                m.Action.Type = InProcessMaterialAction.ActionType.UnloadToInProcess;
              }
              else
              {
                m.Action.Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial;
              }
              m.Action.UnloadIntoQueue = job.GetOutputQueue(m.Process, m.Path);
              matsOnPallets.Add(m.MaterialID);
              curStatus.Material.Add(m);
            }
            if (pal.RemainingPalletCycles > 1)
            {
              foreach (var m in MaterialLoadingOntoPallet(pal))
              {
                matsOnPallets.Add(m.MaterialID);
                curStatus.Material.Add(m);
              }
            }
            break;

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
      foreach (var pal in palMaster.Values)
      {
        if (pal.Alarm)
        {
          curStatus.Alarms.Add("Pallet " + pal.PalletNum.ToString() + " has alarm");
        }
      }

      return curStatus;
    }

    public bool IsJobCompleted(JobPlan job)
    {
      var planned = Enumerable.Range(1, job.GetNumPaths(process: 1)).Sum(job.GetPlannedCyclesOnFirstProcess);

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

    public int CountStartedOrInProcess(JobPlan job)
    {
      var mats = new HashSet<long>();

      foreach (var e in _log.GetLogForJobUnique(job.UniqueStr))
      {
        foreach (var mat in e.Material)
        {
          if (mat.JobUniqueStr == job.UniqueStr)
          {
            mats.Add(mat.MaterialID);
          }
        }
      }

      //TODO: add material for pallet which is moving to load station to receive new part to load?

      return mats.Count;
    }

    private List<InProcessMaterial> MaterialLoadingOntoPallet(PalletMaster pal)
    {
      // TODO: write me
      return new List<InProcessMaterial>();
    }

    private List<InProcessMaterial> MaterialForPallet(string pal)
    {
      IEnumerable<LogMaterial> mats = null;
      foreach (var evt in _log.CurrentPalletLog(pal))
      {
        if (evt.LogType == LogType.LoadUnloadCycle && evt.Result == "LOAD" && !evt.StartOfCycle)
        {
          mats = evt.Material;
          break;
        }
      }
      if (mats == null)
      {
        // TODO: handle when missing load!
      }
      return mats.Select(mat => new InProcessMaterial()
      {
        MaterialID = mat.MaterialID,
        JobUnique = mat.JobUniqueStr,
        PartName = mat.PartName,
        Process = mat.Process,
        Path = PathForMaterial(mat),
        Serial = mat.Serial,
        WorkorderId = mat.Workorder,
        SignaledInspections =
            _log.LookupInspectionDecisions(mat.MaterialID)
            .Where(x => x.Inspect)
            .Select(x => x.InspType)
            .Distinct()
            .ToList(),
        Location = new InProcessMaterialLocation()
        {
          Type = InProcessMaterialLocation.LocType.OnPallet,
          Pallet = pal,
          Face = 1
        },
        Action = new InProcessMaterialAction()
        {
          Type = InProcessMaterialAction.ActionType.Waiting
        }
      }).ToList();
    }

    private static int PathForMaterial(LogMaterial m)
    {
      var idx = m.Face.IndexOf("-");
      if (idx >= 0 && idx + 1 < m.Face.Length && int.TryParse(m.Face.Substring(idx + 1), out int path))
      {
        return path;
      }
      else
      {
        return 1;
      }
    }

    private static PalletLocation BuildCurrentLocation(PalletMaster p)
    {
      switch (p.Loc)
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
          return new PalletLocation(PalletLocationEnum.Buffer, "Buffer", p.PalletNum);
        case CartLoc c:
          return new PalletLocation(PalletLocationEnum.Cart, "Cart", 1);
      }
      return new PalletLocation(PalletLocationEnum.Buffer, "Buffer", 0);
    }

  }
}