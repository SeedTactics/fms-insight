/* Copyright (c) 2023, John Lenz

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

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace BlackMaple.MachineFramework;

public static class BuildCellState
{
  public record MaterialInQueue
  {
    public required QueuedMaterial QMat { get; init; }
    public required InProcessMaterial InProc { get; init; }
    public required Job? Job { get; init; }
  }

  public static ImmutableList<MaterialInQueue> AllQueuedMaterial(IRepository db, JobCache? jobCache)
  {
    var mats = ImmutableList.CreateBuilder<MaterialInQueue>();
    var queuedMats = db.GetMaterialInAllQueues();
    var insps = db.LookupInspectionDecisions(queuedMats.Select(m => m.MaterialID));

    foreach (var mat in queuedMats)
    {
      var lastProc = (mat.NextProcess ?? 1) - 1;

      mats.Add(
        new MaterialInQueue()
        {
          QMat = mat,
          Job = string.IsNullOrEmpty(mat.Unique) || jobCache == null ? null : jobCache.Lookup(mat.Unique),
          InProc = new InProcessMaterial()
          {
            MaterialID = mat.MaterialID,
            JobUnique = mat.Unique,
            PartName = mat.PartNameOrCasting,
            Process = lastProc,
            Path = mat.Paths != null && mat.Paths.TryGetValue(Math.Max(1, lastProc), out var path) ? path : 1,
            Serial = mat.Serial,
            WorkorderId = mat.Workorder,
            SignaledInspections = insps[mat.MaterialID]
              .Where(x => x.Inspect)
              .Select(x => x.InspType)
              .Distinct()
              .ToImmutableList(),
            Location = new InProcessMaterialLocation()
            {
              Type = InProcessMaterialLocation.LocType.InQueue,
              CurrentQueue = mat.Queue,
              QueuePosition = mat.Position,
            },
            Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting }
          }
        }
      );
    }

    return mats.ToImmutable();
  }

  public record LoadedMaterial
  {
    public required InProcessMaterial InProc { get; init; }
    public required Job? Job { get; init; }
    public required LogEntry LoadEnd { get; init; }
  }

  public record PalletMaterial
  {
    public required int Pallet { get; init; }
    public required ImmutableList<LoadedMaterial> LoadedMaterial { get; init; }
    public required IReadOnlyList<LogEntry> Log { get; init; }
  }

  public static PalletMaterial CurrentMaterialOnPallet(int pallet, IRepository db, JobCache jobs)
  {
    var loadedMats = ImmutableList.CreateBuilder<LoadedMaterial>();
    var log = db.CurrentPalletLog(pallet.ToString());

    var lastLoaded = log.Where(
        e => e.LogType == LogType.LoadUnloadCycle && e.Result == "LOAD" && !e.StartOfCycle
      )
      .SelectMany(e => e.Material);

    foreach (var mat in lastLoaded)
    {
      var details = db.GetMaterialDetails(mat.MaterialID);
      var inProcMat = new InProcessMaterial()
      {
        MaterialID = mat.MaterialID,
        JobUnique = mat.JobUniqueStr,
        PartName = mat.PartName,
        Process = mat.Process,
        Path =
          details.Paths != null && details.Paths.ContainsKey(mat.Process) ? details.Paths[mat.Process] : 1,
        Serial = details.Serial,
        WorkorderId = details.Workorder,
        SignaledInspections = db.LookupInspectionDecisions(mat.MaterialID)
          .Where(x => x.Inspect)
          .Select(x => x.InspType)
          .ToImmutableList(),
        LastCompletedMachiningRouteStopIndex = null,
        Location = new InProcessMaterialLocation()
        {
          Type = InProcessMaterialLocation.LocType.OnPallet,
          Pallet = pallet.ToString(),
          Face = int.TryParse(mat.Face, out var face) ? face : 1
        },
        Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
      };

      var logsForMat = log.Where(e => e.Material.Any(m => m.MaterialID == mat.MaterialID));
      var loadEnd = logsForMat.Last(
        e => e.LogType == LogType.LoadUnloadCycle && e.Result == "LOAD" && !e.StartOfCycle
      );

      loadedMats.Add(
        new LoadedMaterial()
        {
          InProc = inProcMat,
          Job = jobs.Lookup(mat.JobUniqueStr),
          LoadEnd = loadEnd,
        }
      );
    }

    return new PalletMaterial()
    {
      Pallet = pallet,
      LoadedMaterial = loadedMats.ToImmutable(),
      Log = log
    };
  }

  public record CurrentMachiningStop
  {
    public required MachiningStop? JobStop { get; init; }
    public required LogEntry? MachineStart { get; init; }
    public required LogEntry? MachineEnd { get; init; }
  }

  // In many cells, only a single piece of material is on a pallet at once.
  public record SingleLoadedMaterial : LoadedMaterial
  {
    public required int Pallet { get; init; }
    public required ImmutableList<CurrentMachiningStop> Stops { get; init; }
    public required LogEntry? UnloadBegin { get; init; }
    public required IReadOnlyList<LogEntry> AllEntries { get; init; }
  }

  public static SingleLoadedMaterial? CurrentSingleMaterialOnPallet(int pallet, IRepository db, JobCache jobs)
  {
    var palMats = BuildCellState.CurrentMaterialOnPallet(pallet, db, jobs);
    var mat = palMats.LoadedMaterial.FirstOrDefault();
    if (mat == null)
    {
      return null;
    }
    else if (jobs == null)
    {
      var stops = ImmutableList.CreateBuilder<CurrentMachiningStop>();

      LogEntry? machineBegin = null;
      foreach (var log in palMats.Log.Where(e => e.LogType == LogType.MachineCycle))
      {
        if (log.StartOfCycle && machineBegin != null)
        {
          // machine started while another was in-process, add the stop as a half-unfinished stop
          stops.Add(
            new CurrentMachiningStop()
            {
              JobStop = null,
              MachineStart = machineBegin,
              MachineEnd = null
            }
          );
          machineBegin = log;
        }
        else if (log.StartOfCycle)
        {
          // machine started with previous begin null, all is good
          machineBegin = log;
        }
        else if (
          machineBegin != null
          && log.LocationName == machineBegin.LocationName
          && log.LocationNum == machineBegin.LocationNum
          && log.Program == machineBegin.Program
        )
        {
          // machine end matching the machine start, all is good
          stops.Add(
            new CurrentMachiningStop()
            {
              JobStop = null,
              MachineStart = machineBegin,
              MachineEnd = log
            }
          );
          machineBegin = null;
        }
        else if (machineBegin != null)
        {
          // machine begin and end don't match locations or programs, add both as half-unfinished stops
          stops.Add(
            new CurrentMachiningStop()
            {
              JobStop = null,
              MachineStart = machineBegin,
              MachineEnd = null,
            }
          );
          stops.Add(
            new CurrentMachiningStop()
            {
              JobStop = null,
              MachineStart = null,
              MachineEnd = log
            }
          );
          machineBegin = null;
        }
        else
        {
          // machine begin is null, but we have a machine end
          stops.Add(
            new CurrentMachiningStop()
            {
              JobStop = null,
              MachineStart = null,
              MachineEnd = log
            }
          );
        }
      }

      return new SingleLoadedMaterial
      {
        Pallet = pallet,
        InProc = mat.InProc,
        Job = mat.Job,
        LoadEnd = mat.LoadEnd,
        Stops = stops.ToImmutable(),
        UnloadBegin = palMats.Log.LastOrDefault(e => e.LogType == LogType.LoadUnloadCycle && e.StartOfCycle),
        AllEntries = palMats.Log
      };
    }
    else
    {
      throw new NotImplementedException("TODO: match with job steps too");
    }
  }

  // Additional Helpers to consider here are FindMaterialToLoad, but currently the different implementations
  // have slightly different behavior.
}
