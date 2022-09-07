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
  public static class BuildCurrentStatus
  {
    public static CurrentStatus Build(IRepository jobDB, CellState status, FMSSettings settings)
    {
      if (status == null)
      {
        return new CurrentStatus();
      }

      var proc1paths =
        status.UnarchivedJobs.SelectMany(job =>
          Enumerable.Range(1, job.Processes[0].Paths.Count).Select(proc1path => (job, proc1path))
        )
        .OrderBy(x => x.job.RouteStartUTC)
        .ThenBy(x => x.job.Processes[0].Paths[x.proc1path - 1].SimulatedStartingUTC)
        .Select((x, idx) => new { Uniq = x.job.UniqueStr, Proc1Path = x.proc1path, Prec = idx + 1 })
        .ToLookup(x => x.Uniq);

      // jobs
      var jobsByUniq = ImmutableDictionary.CreateBuilder<string, ActiveJob>();
      foreach (var j in status.UnarchivedJobs)
      {
        // completed
        var completed =
          j.Processes.Select(
            proc => new int[proc.Paths.Count] // defaults to fill with zeros
          ).ToArray();
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
                completed[mat.Process - 1][matPath - 1] += 1;
              }
            }
          }
        }

        // precedence
        var proc1precs = proc1paths.Contains(j.UniqueStr) ? proc1paths[j.UniqueStr] : null;
        var precedence =
          j.Processes.Select((proc, procIdx) =>
          {
            if (procIdx == 0)
            {
              return proc.Paths.Select((_, pathIdx) =>
                proc1precs?.FirstOrDefault(x => x.Proc1Path == pathIdx + 1)?.Prec ?? 0L
              ).ToArray();
            }
            else
            {
              return proc.Paths.Select(path =>
                proc1precs?.FirstOrDefault()?.Prec ?? 0L
              ).ToArray();
            }
          }).ToArray();

        // take decremented quantity out of the planned cycles
        int decrQty = j.Decrements?.Sum(d => d.Quantity) ?? 0;
        var newPlanned = j.Cycles - decrQty;

        var started = 0;
        if (status.CyclesStartedOnProc1 != null && status.CyclesStartedOnProc1.TryGetValue(j.UniqueStr, out var startedOnProc1))
        {
          started = startedOnProc1;
        }

        var workorders = jobDB.GetWorkordersForUnique(j.UniqueStr);

        var curJob = j.CloneToDerived<ActiveJob, Job>() with
        {
          CopiedToSystem = true,
          Completed = completed.Select(c => ImmutableList.Create(c)).ToImmutableList(),
          RemainingToStart = decrQty > 0 ? 0 : Math.Max(newPlanned - started, 0),
          Cycles = newPlanned,
          ScheduleId = j.ScheduleId,
          Precedence = precedence.Select(p => ImmutableList.Create(p)).ToImmutableList(),
          Decrements = j.Decrements == null || j.Decrements.Count == 0 ? null : j.Decrements,
          AssignedWorkorders = workorders == null || workorders.Count == 0 ? null : workorders
        };
        jobsByUniq.Add(curJob.UniqueStr, curJob);
      }

      // pallets
      var palletsByName = ImmutableDictionary.CreateBuilder<string, MachineFramework.PalletStatus>();
      foreach (var pal in status.Pallets)
      {
        palletsByName.Add(pal.Status.Master.PalletNum.ToString(), new MachineFramework.PalletStatus()
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