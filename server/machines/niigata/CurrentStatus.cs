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
  public static class BuildCurrentStatus
  {
    public static CurrentStatus Build(IRepository jobDB, CellState status, FMSSettings settings)
    {
      if (status == null)
      {
        return new CurrentStatus();
      }

      // jobs
      var jobsByUniq = ImmutableDictionary.CreateBuilder<string, InProcessJob>();
      foreach (var j in status.UnarchivedJobs)
      {
        var curJob = new InProcessJob(j);
        jobsByUniq.Add(curJob.UniqueStr, curJob);
        var evts = jobDB.GetLogForJobUnique(j.UniqueStr);
        foreach (var e in evts)
        {
          if (e.LogType == LogType.LoadUnloadCycle && e.Result == "UNLOAD")
          {
            foreach (var mat in e.Material)
            {
              if (mat.JobUniqueStr == j.UniqueStr)
              {
                var details = jobDB.GetMaterialDetails(mat.MaterialID);
                int matPath = details?.Paths != null && details.Paths.ContainsKey(mat.Process) ? details.Paths[mat.Process] : 1;
                curJob.AdjustCompleted(mat.Process, matPath, x => x + 1);
              }
            }
          }
        }

        var decrs = jobDB.LoadDecrementsForJob(j.UniqueStr);

        foreach (var d in decrs)
          curJob.Decrements.Add(d);

        // take decremented quantity out of the planned cycles
        for (int proc1path = 1; proc1path <= j.GetNumPaths(process: 1); proc1path += 1)
        {
          int decrQty = decrs.Where(p => p.Proc1Path == proc1path).Sum(d => d.Quantity);
          if (decrQty > 0)
          {
            var planned = curJob.GetPlannedCyclesOnFirstProcess(proc1path);
            if (planned < decrQty)
            {
              curJob.SetPlannedCyclesOnFirstProcess(path: proc1path, numCycles: 0);
            }
            else
            {
              curJob.SetPlannedCyclesOnFirstProcess(path: proc1path, numCycles: planned - decrQty);
            }
          }
        }

        curJob.Workorders = jobDB.GetWorkordersForUnique(j.UniqueStr);
      }

      // set precedence
      var proc1paths =
        jobsByUniq.Values.SelectMany(job =>
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
      var palletsByName = ImmutableDictionary.CreateBuilder<string, MachineWatchInterface.PalletStatus>();
      foreach (var pal in status.Pallets)
      {
        palletsByName.Add(pal.Status.Master.PalletNum.ToString(), new MachineWatchInterface.PalletStatus()
        {
          Pallet = pal.Status.Master.PalletNum.ToString(),
          FixtureOnPallet = "",
          OnHold = pal.Status.Master.Skip,
          CurrentPalletLocation = pal.Status.CurStation.Location,
          NumFaces = pal.Material.Count > 0 ? pal.Material.Max(m => m.Mat.Location.Face ?? 1) : 0
        });
      }

      return new CurrentStatus()
      {
        TimeOfCurrentStatusUTC = status.Status.TimeOfStatusUTC,
        Jobs = jobsByUniq.ToImmutable(),
        Pallets = palletsByName.ToImmutable(),
        Material =
          status.Pallets.SelectMany(pal => pal.Material)
          .Concat(status.QueuedMaterial)
          .Select(m => m.Mat)
          .Concat(SetLongTool(status))
          .ToImmutableList(),
        QueueSizes = settings.Queues.ToImmutableDictionary(),
        Alarms =
          status.Pallets.Where(pal => pal.Status.Tracking.Alarm).Select(pal => AlarmCodeToString(pal.Status.Master.PalletNum, pal.Status.Tracking.AlarmCode))
          .Concat(
            status.Status.Machines.Values.Where(mc => mc.Alarm)
            .Select(mc => "Machine " + mc.MachineNumber.ToString() + " has an alarm"
            )
          )
          .Concat(
            status.Status.Alarm ? new[] { "ICC has an alarm" } : new string[] { }
          )
          .ToImmutableList()
      };
    }

    private static IEnumerable<InProcessMaterial> SetLongTool(CellState status)
    {
      // tool loads/unloads
      foreach (var pal in status.Pallets.Where(p => p.Status.Master.ForLongToolMaintenance && p.Status.HasWork))
      {
        if (pal.Status.CurrentStep == null) continue;
        switch (pal.Status.CurrentStep)
        {
          case LoadStep load:
            if (pal.Status.Tracking.BeforeCurrentStep)
            {
              yield return new InProcessMaterial()
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
              };
            }
            break;
          case UnloadStep unload:
            if (pal.Status.Tracking.BeforeCurrentStep)
            {
              yield return new InProcessMaterial()
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
              };
            }
            break;
        }
      }
    }

    private static string AlarmCodeToString(int palletNum, PalletAlarmCode code)
    {
      string msg = " has alarm code " + code.ToString();
      switch (code)
      {
        case PalletAlarmCode.AlarmSetOnScreen:
          msg = " has alarm set by operator";
          break;
        case PalletAlarmCode.M165:
          msg = " has alarm M165";
          break;
        case PalletAlarmCode.RoutingFault:
          msg = " has routing fault";
          break;
        case PalletAlarmCode.LoadingFromAutoLULStation:
          msg = " is loading from auto L/UL station";
          break;
        case PalletAlarmCode.ProgramRequestAlarm:
          msg = " has program request alarm";
          break;
        case PalletAlarmCode.ProgramRespondingAlarm:
          msg = " has program responding alarm";
          break;
        case PalletAlarmCode.ProgramTransferAlarm:
          msg = " has program transfer alarm";
          break;
        case PalletAlarmCode.ProgramTransferFinAlarm:
          msg = " has program transfer alarm";
          break;
        case PalletAlarmCode.MachineAutoOff:
          msg = " at machine with auto off";
          break;
        case PalletAlarmCode.MachinePowerOff:
          msg = " at machine which is powered off";
          break;
        case PalletAlarmCode.IccExited:
          msg = " can't communicate with ICC";
          break;
      }
      return "Pallet " + palletNum.ToString() + msg;
    }
  }
}