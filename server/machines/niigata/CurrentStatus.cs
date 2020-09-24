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
using System.Threading;

namespace BlackMaple.FMSInsight.Niigata
{
  public static class BuildCurrentStatus
  {
    public static CurrentStatus Build(JobDB jobDB, EventLogDB logDB, CellState status, FMSSettings settings)
    {
      var curStatus = new CurrentStatus();
      foreach (var k in settings.Queues) curStatus.QueueSizes[k.Key] = k.Value;

      // jobs
      foreach (var j in status.UnarchivedJobs)
      {
        var curJob = new InProcessJob(j);
        curStatus.Jobs.Add(curJob.UniqueStr, curJob);
        var evts = logDB.GetLogForJobUnique(j.UniqueStr);
        foreach (var e in evts)
        {
          if (e.LogType == LogType.LoadUnloadCycle && e.Result == "UNLOAD")
          {
            foreach (var mat in e.Material)
            {
              if (mat.JobUniqueStr == j.UniqueStr)
              {
                var details = logDB.GetMaterialDetails(mat.MaterialID);
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
      foreach (var pal in status.Pallets.Where(p => p.Status.Master.ForLongToolMaintenance && p.Status.HasWork))
      {
        switch (pal.Status.CurrentStep)
        {
          case LoadStep load:
            if (pal.Status.Tracking.BeforeCurrentStep)
            {
              curStatus.Material.Add(new InProcessMaterial()
              {
                MaterialID = -1,
                JobUnique = "",
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
                JobUnique = "",
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
  }
}