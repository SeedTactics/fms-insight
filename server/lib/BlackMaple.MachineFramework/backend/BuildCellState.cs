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

  public static ImmutableList<MaterialInQueue> AllQueuedMaterial(IRepository db, IJobCache? jobCache)
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

  public record ExecutedMachiningStop : MachiningStop
  {
    public required LogEntry MachineStart { get; init; }
    public required LogEntry MachineEnd { get; init; }
  }

  public record CurrentMachiningStop : MachiningStop
  {
    public required LogEntry MachineStart { get; init; }
  }

  public record LoadedFace
  {
    public required Job? Job { get; init; }
    public required int Process { get; init; }
    public required int Path { get; init; }
    public required ImmutableList<InProcessMaterial> Material { get; init; }
    public required ImmutableList<ExecutedMachiningStop> ExecutedStops { get; init; }
    public required MachiningStop? CurrentStop { get; init; }
    public required ImmutableList<MachiningStop> RemainingStops { get; init; }
  }

  public record LoadedPallet
  {
    public required int Pallet { get; init; }
    public required ImmutableDictionary<int, LoadedFace> Faces { get; init; }
    public required IReadOnlyList<LogEntry> Log { get; init; }
  }

  public static LoadedPallet CurrentMaterialOnPallet(
    int pallet,
    IRepository db,
    IJobCacheWithDefaultStops jobs
  )
  {
    var faces = ImmutableDictionary.CreateBuilder<int, LoadedFace>();
    var log = db.CurrentPalletLog(pallet.ToString());

    var lastLoaded = log.Where(
        e =>
          e.LogType == LogType.LoadUnloadCycle
          && e.Result == "LOAD"
          && !e.StartOfCycle
          && string.IsNullOrEmpty(e.ProgramDetails?.GetValueOrDefault("PalletCycleInvalidated", ""))
      )
      .SelectMany(e => e.Material);

    foreach (var matGroup in lastLoaded.GroupBy(m => int.TryParse(m.Face, out var faceNum) ? faceNum : 1))
    {
      var loadedMats = matGroup
        .Select(mat =>
        {
          var details = db.GetMaterialDetails(mat.MaterialID);
          return new InProcessMaterial()
          {
            MaterialID = mat.MaterialID,
            JobUnique = mat.JobUniqueStr,
            PartName = mat.PartName,
            Process = mat.Process,
            Path =
              details.Paths != null && details.Paths.ContainsKey(mat.Process)
                ? details.Paths[mat.Process]
                : 1,
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
        })
        .ToImmutableList();

      var firstMat = loadedMats.First();
      var job = jobs.Lookup(firstMat.JobUnique);
      var stops =
        job?.Processes?[firstMat.Process - 1]?.Paths?[firstMat.Path - 1]?.Stops
        ?? jobs.DefaultStopsForPath(firstMat.PartName, firstMat.Process, firstMat.Path);

      // TODO: split stops based on completed, executing, and remaining

      faces.Add(
        matGroup.Key,
        new LoadedFace()
        {
          Job = job,
          Process = firstMat.Process,
          Path = firstMat.Path,
          Material = loadedMats,
          ExecutedStops = ImmutableList<ExecutedMachiningStop>.Empty,
          CurrentStop = null,
          RemainingStops = stops.ToImmutableList()
        }
      );
    }

    return new LoadedPallet()
    {
      Pallet = pallet,
      Faces = faces.ToImmutable(),
      Log = log
    };
  }

  // TODO: Function to FastForward Stops based on currently running program (would also create log events and update Action)


  // Additional Helpers to consider here are FindMaterialToLoad, but currently the different implementations
  // have slightly different behavior.
}
