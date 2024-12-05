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
            QuarantineAfterUnload = null,
            Location = new InProcessMaterialLocation()
            {
              Type = InProcessMaterialLocation.LocType.InQueue,
              CurrentQueue = mat.Queue,
              QueuePosition = mat.Position,
            },
            Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
          },
        }
      );
    }

    return mats.ToImmutable();
  }

  public record MachiningStopAndEvents : MachiningStop
  {
    public required int StopIdx { get; init; }
    public required LogEntry? MachineStart { get; init; }
    public required LogEntry? MachineEnd { get; init; }
  }

  public record LoadedFace
  {
    public required int FaceNum { get; init; }
    public required Job? Job { get; init; }
    public required int Process { get; init; }
    public required int Path { get; init; }
    public required bool IsFinalProcess { get; init; }
    public required string? OutputQueue { get; init; }
    public required TimeSpan? ExpectedUnloadTimeForOnePieceOfMaterial { get; init; }

    public required ImmutableList<InProcessMaterial> Material { get; init; }

    public required LogEntry? LoadEnd { get; init; }
    public required ImmutableList<MachiningStopAndEvents> Stops { get; init; }
    public required LogEntry? UnloadStart { get; init; }
    public required LogEntry? UnloadEnd { get; init; }
  }

  public record Pallet
  {
    public required int PalletNum { get; init; }
    public required ImmutableDictionary<int, LoadedFace> Faces { get; init; }
    public required ImmutableList<InProcessMaterial> MaterialLoadingOntoPallet { get; init; }
    public required ImmutableList<LogEntry> Log { get; init; }
    public required LogEntry? LastPalletCycle { get; init; }
    public required LogEntry? LoadBegin { get; init; }
    public required bool NewLogEvents { get; init; }

    public static Pallet Empty(int pallet)
    {
      return new Pallet()
      {
        PalletNum = pallet,
        Faces = ImmutableDictionary<int, LoadedFace>.Empty,
        MaterialLoadingOntoPallet = ImmutableList<InProcessMaterial>.Empty,
        Log = ImmutableList<LogEntry>.Empty,
        LastPalletCycle = null,
        LoadBegin = null,
        NewLogEvents = false,
      };
    }
  }

  public static Pallet CurrentMaterialOnPallet(int pallet, IRepository db, IJobCacheWithDefaultStops jobs)
  {
    var faces = ImmutableDictionary.CreateBuilder<int, LoadedFace>();
    var log = db.CurrentPalletLog(pallet, includeLastPalletCycleEvt: true).ToImmutableList();

    var lastPalletCycle = log.LastOrDefault(e => e.LogType == LogType.PalletCycle);

    var loadBegin = log.LastOrDefault(e =>
      e.LogType == LogType.LoadUnloadCycle && e.Result == "LOAD" && e.StartOfCycle
    );

    var lastLoaded = log.Where(e =>
        e.LogType == LogType.LoadUnloadCycle
        && e.Result == "LOAD"
        && !e.StartOfCycle
        && e.Material != null
        && e.Material.Any()
      )
      .GroupBy(e => e.Material.First().Face)
      .Select(g => new { Face = g.Key, LoadEnd = g.Last() });

    foreach (var face in lastLoaded)
    {
      var loadedMats = face
        .LoadEnd.Material.Select(mat =>
        {
          return new InProcessMaterial()
          {
            MaterialID = mat.MaterialID,
            JobUnique = mat.JobUniqueStr,
            PartName = mat.PartName,
            Process = mat.Process,
            Path = mat.Path ?? 1,
            Serial = mat.Serial == "" ? null : mat.Serial,
            WorkorderId = mat.Workorder == "" ? null : mat.Workorder,
            SignaledInspections = db.LookupInspectionDecisions(mat.MaterialID)
              .Where(x => x.Inspect)
              .Select(x => x.InspType)
              .ToImmutableList(),
            QuarantineAfterUnload =
              log.LastOrDefault(e =>
                e.Material.Any(m => m.MaterialID == mat.MaterialID)
                && (
                  e.LogType == LogType.SignalQuarantine
                  || (e.LogType == LogType.LoadUnloadCycle && !e.StartOfCycle)
                )
              )?.LogType == LogType.SignalQuarantine
                ? true
                : null,
            LastCompletedMachiningRouteStopIndex = null,
            Location = new InProcessMaterialLocation()
            {
              Type = InProcessMaterialLocation.LocType.OnPallet,
              PalletNum = pallet,
              Face = face.Face,
            },
            Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
          };
        })
        .ToImmutableList();

      var firstMat = loadedMats.First();
      var job = jobs.Lookup(firstMat.JobUnique);

      IReadOnlyList<MachiningStop> stops;
      bool isFinalProcess;
      string? outputQueue;
      TimeSpan? expectedUnloadTimeForOnePieceOfMaterial;
      if (job != null)
      {
        stops = job.Processes[firstMat.Process - 1].Paths[firstMat.Path - 1].Stops;
        isFinalProcess = firstMat.Process == job.Processes.Count;
        outputQueue = job.Processes[firstMat.Process - 1].Paths[firstMat.Path - 1].OutputQueue;
        expectedUnloadTimeForOnePieceOfMaterial = job.Processes[firstMat.Process - 1]
          .Paths[firstMat.Path - 1]
          .ExpectedUnloadTime;
      }
      else
      {
        var info = jobs.DefaultPathInfo(matOnFace: loadedMats);
        stops = info.Stops;
        isFinalProcess = info.IsFinalProcess;
        outputQueue = info.OutputQueue;
        expectedUnloadTimeForOnePieceOfMaterial = info.ExpectedUnloadTimeForOnePieceOfMaterial;
      }

      var stopsAndEvts = ImmutableList.CreateBuilder<MachiningStopAndEvents>();
      var stopIdx = 0;
      int? lastCompletedStopIdx = null;
      foreach (var stop in stops)
      {
        var machStart = log.FirstOrDefault(e =>
          e.LogType == LogType.MachineCycle
          && e.StartOfCycle
          && e.LocationName == stop.StationGroup
          && (stop.Program == null || e.Program == stop.Program)
          && e.Material.Any(m => loadedMats.Any(lm => lm.MaterialID == m.MaterialID))
        );
        var machEnd = log.FirstOrDefault(e =>
          e.LogType == LogType.MachineCycle
          && !e.StartOfCycle
          && e.LocationName == stop.StationGroup
          && (stop.Program == null || e.Program == stop.Program)
          && e.Material.Any(m => loadedMats.Any(lm => lm.MaterialID == m.MaterialID))
        );

        if (machEnd != null)
        {
          lastCompletedStopIdx = stopIdx;
        }

        stopsAndEvts.Add(
          new MachiningStopAndEvents()
          {
            StopIdx = stopIdx,
            StationGroup = stop.StationGroup,
            Program = stop.Program,
            Stations = stop.Stations,
            ExpectedCycleTime = stop.ExpectedCycleTime,
            ProgramRevision = stop.ProgramRevision,
            MachineStart = machStart,
            MachineEnd = machEnd,
          }
        );

        stopIdx += 1;
      }

      if (lastCompletedStopIdx.HasValue)
      {
        loadedMats = loadedMats
          .Select(mat => mat with { LastCompletedMachiningRouteStopIndex = lastCompletedStopIdx })
          .ToImmutableList();
      }

      var unloadStart = log.FirstOrDefault(e =>
        e.LogType == LogType.LoadUnloadCycle
        && e.Result == "UNLOAD"
        && e.StartOfCycle
        && e.Material.Any(m => loadedMats.Any(lm => lm.MaterialID == m.MaterialID))
      );

      var unloadEnd = log.FirstOrDefault(e =>
        e.LogType == LogType.LoadUnloadCycle
        && e.Result == "UNLOAD"
        && !e.StartOfCycle
        && e.Material.Any(m => loadedMats.Any(lm => lm.MaterialID == m.MaterialID))
      );

      faces.Add(
        face.Face,
        new LoadedFace()
        {
          FaceNum = face.Face,
          Job = job,
          Process = firstMat.Process,
          Path = firstMat.Path,
          IsFinalProcess = isFinalProcess,
          OutputQueue = outputQueue,
          ExpectedUnloadTimeForOnePieceOfMaterial = expectedUnloadTimeForOnePieceOfMaterial,
          Material = loadedMats,
          LoadEnd = face.LoadEnd,
          Stops = stopsAndEvts.ToImmutable(),
          UnloadStart = unloadStart,
          UnloadEnd = unloadEnd,
        }
      );
    }

    return new Pallet()
    {
      PalletNum = pallet,
      Faces = faces.ToImmutable(),
      MaterialLoadingOntoPallet = ImmutableList<InProcessMaterial>.Empty,
      Log = log,
      LastPalletCycle = lastPalletCycle,
      LoadBegin = loadBegin,
      NewLogEvents = false,
    };
  }

  public static Pallet EnsureLoadBegin(Pallet pal, IRepository db, DateTime nowUTC)
  {
    if (pal.LoadBegin == null)
    {
      var loadBegin = db.RecordLoadStart(
        mats: new EventLogMaterial[] { },
        pallet: pal.PalletNum,
        lulNum: pal.PalletNum,
        timeUTC: nowUTC
      );
      return pal with { LoadBegin = loadBegin, Log = pal.Log.Add(loadBegin), NewLogEvents = true };
    }
    else
    {
      return pal;
    }
  }

  // A discriminated union of the different states a pallet can be in
  public record LoadedPalletStatus
  {
    private LoadedPalletStatus() { }

    public record LulFinished : LoadedPalletStatus
    {
      public required int LoadNum { get; init; }
      public IEnumerable<InProcessMaterial>? MaterialToLoad { get; init; } = null;
    }

    public record MachineStopped() : LoadedPalletStatus;

    public record MachineRunning : LoadedPalletStatus
    {
      public required string MachineGroup { get; init; }
      public required int MachineNum { get; init; }
      public required string Program { get; init; }
    }

    public record Unloading : LoadedPalletStatus
    {
      public required int LoadNum { get; init; }
      public ImmutableList<int> UnloadingFaces { get; init; } = ImmutableList<int>.Empty;
      public ImmutableList<int> UnloadCompletedFaces { get; init; } = ImmutableList<int>.Empty;
      public Func<LoadedFace, ImmutableList<InProcessMaterial>>? AdjustUnloadingMaterial { get; init; } =
        null;
      public ImmutableList<InProcessMaterial>? NewMaterialToLoad { get; init; } = null;
      public DateTime? MachiningStopTime { get; init; } = null;
    }
  }

  public static Pallet UpdatePalletStatus(
    Pallet pal,
    LoadedPalletStatus status,
    IRepository db,
    IMachineControl? machineControl,
    IJobCacheWithDefaultStops jobs,
    DateTime nowUTC,
    FMSSettings settings
  )
  {
    switch (status)
    {
      case LoadedPalletStatus.LulFinished loaded:
        pal = SetMachineNotRunning(pal: pal, db: db, machineControl: machineControl, nowUTC: nowUTC);
        if (!CheckMaterialMatches(pal, loaded.MaterialToLoad))
        {
          pal = CompletePalletCycle(
            pal: pal,
            loadNum: loaded.LoadNum,
            materialToLoad: loaded.MaterialToLoad,
            jobs: jobs,
            db: db,
            nowUTC: nowUTC,
            fmsSettings: settings
          );
        }
        return pal;

      case LoadedPalletStatus.MachineStopped machineStopped:
        return SetMachineNotRunning(pal: pal, db: db, machineControl: machineControl, nowUTC: nowUTC);

      case LoadedPalletStatus.MachineRunning machineRunning:
        return SetMachineRunning(
          pal: pal,
          machineGroup: machineRunning.MachineGroup,
          machineNum: machineRunning.MachineNum,
          program: machineRunning.Program,
          db: db,
          machineControl: machineControl,
          nowUTC: nowUTC
        );

      case LoadedPalletStatus.Unloading unloading:
        pal = SetMachineNotRunning(
          pal: pal,
          db: db,
          machineControl: machineControl,
          nowUTC: unloading.MachiningStopTime ?? nowUTC
        );
        pal = EnsureLoadBegin(pal: pal, db: db, nowUTC: nowUTC);
        foreach (var face in unloading.UnloadingFaces)
        {
          pal = SetUnloading(
            pal: pal,
            faceNum: face,
            lulNum: unloading.LoadNum,
            db: db,
            nowUTC: nowUTC,
            adjustUnloadingMats: unloading.AdjustUnloadingMaterial
          );
        }
        foreach (var face in unloading.UnloadCompletedFaces)
        {
          pal = SetPartialUnloadComplete(
            pal: pal,
            faceNum: face,
            lulNum: unloading.LoadNum,
            materialToLoad: unloading.NewMaterialToLoad,
            fmsSettings: settings,
            db: db,
            nowUTC: nowUTC
          );
        }
        if (unloading.NewMaterialToLoad != null)
        {
          pal = pal with
          {
            MaterialLoadingOntoPallet = pal.MaterialLoadingOntoPallet.AddRange(unloading.NewMaterialToLoad),
          };
        }
        return pal;

      default:
        throw new ArgumentException("Unknown pallet status", nameof(status));
    }
  }

  private static Pallet SetMachineRunning(
    Pallet pal,
    string machineGroup,
    int machineNum,
    string program,
    IRepository db,
    IMachineControl? machineControl,
    DateTime nowUTC
  )
  {
    // a single program could machine multiple faces.  Lookup all stops using this program.

    var stops = new List<(LoadedFace face, MachiningStopAndEvents stop)>();
    foreach (var face in pal.Faces.OrderBy(f => f.Key))
    {
      foreach (var stop in face.Value.Stops)
      {
        if (stop.StationGroup == machineGroup && (stop.Program == null || stop.Program == program))
        {
          stops.Add((face.Value, stop));
        }
      }
    }

    if (stops.Count == 0)
    {
      return pal;
    }

    var machineStart = stops[0].stop.MachineStart;
    var groupName = stops[0].stop.StationGroup;
    var programRevision = stops[0].stop.ProgramRevision;
    var newEvt = false;

    if (machineStart == null)
    {
      machineStart = db.RecordMachineStart(
        mats: stops.SelectMany(stop =>
          stop.face.Material.Select(m => new EventLogMaterial()
          {
            MaterialID = m.MaterialID,
            Process = m.Process,
            Face = stop.face.FaceNum,
          })
        ),
        pallet: pal.PalletNum,
        statName: groupName,
        statNum: machineNum,
        program: program,
        timeUTC: nowUTC,
        pockets: machineControl?.CurrentToolsInMachine(groupName, machineNum),
        extraData: !programRevision.HasValue
          ? null
          : new Dictionary<string, string> { { "ProgramRevision", programRevision.Value.ToString() } }
      );
      newEvt = true;
    }

    var elapsed = nowUTC.Subtract(machineStart.EndTimeUTC);
    var expectedTotalTime = TimeSpan.FromTicks(stops.Sum(s => s.stop.ExpectedCycleTime.Ticks));

    foreach (var stop in stops)
    {
      pal = pal with
      {
        Faces = pal.Faces.SetItem(
          stop.face.FaceNum,
          stop.face with
          {
            Stops = stop.face.Stops.SetItem(
              stop.stop.StopIdx,
              stop.stop with
              {
                MachineStart = machineStart,
              }
            ),
            Material = stop
              .face.Material.Select(oldMat =>
                oldMat with
                {
                  Action = new InProcessMaterialAction()
                  {
                    Type = InProcessMaterialAction.ActionType.Machining,
                    Program = programRevision.HasValue
                      ? program + " rev" + programRevision.Value.ToString()
                      : program,
                    ElapsedMachiningTime = elapsed,
                    ExpectedRemainingMachiningTime =
                      expectedTotalTime == TimeSpan.Zero ? null : expectedTotalTime - elapsed,
                  },
                }
              )
              .ToImmutableList(),
          }
        ),
      };
    }

    return pal with
    {
      Log = newEvt ? pal.Log.Add(machineStart) : pal.Log,
      NewLogEvents = pal.NewLogEvents || newEvt,
    };
  }

  private static Pallet SetMachineNotRunning(
    Pallet pal,
    IRepository db,
    IMachineControl? machineControl,
    DateTime nowUTC
  )
  {
    var runningStops = pal
      .Faces.Values.SelectMany(f => f.Stops.Select(s => (face: f, stop: s)))
      .Where(s => s.stop.MachineStart != null && s.stop.MachineEnd == null)
      .GroupBy(s => s.stop.Program);

    var newEvts = new List<LogEntry>();
    foreach (var stopGroup in runningStops)
    {
      var machineStart = stopGroup.First().stop.MachineStart!;

      var startTools = db.ToolPocketSnapshotForCycle(machineStart.Counter);
      var endTools = machineControl?.CurrentToolsInMachine(
        machineStart.LocationName,
        machineStart.LocationNum
      );
      var toolUse =
        startTools != null && endTools != null ? ToolSnapshotDiff.Diff(startTools, endTools) : null;

      var activeTime = TimeSpan.FromTicks(stopGroup.Sum(s => s.stop.ExpectedCycleTime.Ticks));

      var machineEnd = db.RecordMachineEnd(
        mats: stopGroup.SelectMany(stop =>
          stop.face.Material.Select(m => new EventLogMaterial()
          {
            MaterialID = m.MaterialID,
            Process = m.Process,
            Face = stop.face.FaceNum,
          })
        ),
        pallet: pal.PalletNum,
        statName: machineStart.LocationName,
        statNum: machineStart.LocationNum,
        program: machineStart.Program,
        result: "",
        timeUTC: nowUTC,
        elapsed: nowUTC - machineStart.EndTimeUTC,
        active: activeTime,
        tools: toolUse,
        deleteToolSnapshotsFromCntr: machineStart.Counter
      );
      newEvts.Add(machineEnd);

      foreach (var stop in stopGroup)
      {
        pal = pal with
        {
          Faces = pal.Faces.SetItem(
            stop.face.FaceNum,
            stop.face with
            {
              Stops = stop.face.Stops.SetItem(stop.stop.StopIdx, stop.stop with { MachineEnd = machineEnd }),
              Material = stop
                .face.Material.Select(oldMat =>
                  oldMat with
                  {
                    LastCompletedMachiningRouteStopIndex = stop.stop.StopIdx,
                  }
                )
                .ToImmutableList(),
            }
          ),
        };
      }
    }

    return pal with
    {
      Log = newEvts.Count > 0 ? pal.Log.AddRange(newEvts) : pal.Log,
      NewLogEvents = pal.NewLogEvents || newEvts.Count > 0,
    };
  }

  private static string? OutputQueueForMaterial(LoadedFace face, IEnumerable<LogEntry> log)
  {
    var signalQuarantine = log.FirstOrDefault(e => e.LogType == LogType.SignalQuarantine);
    if (signalQuarantine != null)
    {
      return signalQuarantine.LocationName;
    }
    else
    {
      return face.OutputQueue;
    }
  }

  private static Pallet SetUnloading(
    Pallet pal,
    int faceNum,
    int lulNum,
    IRepository db,
    DateTime nowUTC,
    Func<LoadedFace, ImmutableList<InProcessMaterial>>? adjustUnloadingMats
  )
  {
    var face = pal.Faces.GetValueOrDefault(faceNum);
    if (face == null)
      return pal;
    LogEntry? newEvt = null;

    if (face.UnloadStart == null)
    {
      face = face with
      {
        UnloadStart = db.RecordUnloadStart(
          mats: face.Material.Select(m => new EventLogMaterial()
          {
            MaterialID = m.MaterialID,
            Process = m.Process,
            Face = faceNum,
          }),
          pallet: pal.PalletNum,
          lulNum: lulNum,
          timeUTC: nowUTC
        ),
      };
      newEvt = face.UnloadStart;
    }

    var outputQueue = OutputQueueForMaterial(face, pal.Log);
    face = face with
    {
      Material = face
        .Material.Select(oldMat =>
          oldMat with
          {
            LastCompletedMachiningRouteStopIndex = face.Stops.Count - 1,
            Action = new InProcessMaterialAction()
            {
              Type = face.IsFinalProcess
                ? InProcessMaterialAction.ActionType.UnloadToCompletedMaterial
                : InProcessMaterialAction.ActionType.UnloadToInProcess,
              UnloadIntoQueue = outputQueue,
              ElapsedLoadUnloadTime = nowUTC - face.UnloadStart.EndTimeUTC,
            },
          }
        )
        .ToImmutableList(),
    };

    if (adjustUnloadingMats != null)
    {
      face = face with { Material = adjustUnloadingMats(face) };
    }

    return pal with
    {
      Faces = pal.Faces.SetItem(faceNum, face),
      Log = newEvt != null ? pal.Log.Add(newEvt) : pal.Log,
      NewLogEvents = pal.NewLogEvents || newEvt != null,
    };
  }

  private static MaterialToUnloadFromFace? UnloadMaterial(Pallet pal, LoadedFace face)
  {
    if (face.UnloadEnd == null)
    {
      var outputQueue = OutputQueueForMaterial(face, pal.Log);

      return new MaterialToUnloadFromFace()
      {
        MaterialIDToQueue = face.Material.ToImmutableDictionary(m => m.MaterialID, m => outputQueue),
        Process = face.Process,
        FaceNum = face.FaceNum,
        ActiveOperationTime =
          (face.ExpectedUnloadTimeForOnePieceOfMaterial ?? TimeSpan.Zero) * face.Material.Count,
      };
    }

    return null;
  }

  private static DateTime? LoadUnloadStartTime(Pallet pal)
  {
    // A load/unload starts with a LoadBegin event, then a sequence of partial unloads,
    // then the pallet cycle which contains any remaining unloads and the loads.

    var lastPartialUnload = pal.Log.LastOrDefault(e =>
      e.LogType == LogType.LoadUnloadCycle && e.Result == "UNLOAD" && !e.StartOfCycle
    );

    return lastPartialUnload?.EndTimeUTC ?? pal.LoadBegin?.EndTimeUTC;
  }

  private static Pallet SetPartialUnloadComplete(
    Pallet pal,
    int faceNum,
    int lulNum,
    IEnumerable<InProcessMaterial>? materialToLoad,
    FMSSettings fmsSettings,
    IRepository db,
    DateTime nowUTC
  )
  {
    var face = pal.Faces.GetValueOrDefault(faceNum);
    if (face == null)
    {
      return pal;
    }

    IEnumerable<LogEntry>? newEvts = null;

    var toUnload = UnloadMaterial(pal, face);
    if (toUnload != null)
    {
      if (materialToLoad != null)
      {
        foreach (var m in materialToLoad)
        {
          if (toUnload.MaterialIDToQueue.ContainsKey(m.MaterialID))
          {
            // ensure material being loaded isn't sent to a queue
            toUnload = toUnload with
            {
              MaterialIDToQueue = toUnload.MaterialIDToQueue.Add(m.MaterialID, null),
            };
          }
        }
      }
      newEvts = db.RecordPartialUnloadEnd(
        toUnload: [toUnload],
        totalElapsed: nowUTC - (LoadUnloadStartTime(pal) ?? nowUTC),
        pallet: pal.PalletNum,
        lulNum: lulNum,
        timeUTC: nowUTC,
        externalQueues: fmsSettings.ExternalQueues
      );
    }

    return pal with
    {
      Faces = pal.Faces.Remove(faceNum),
      Log = newEvts != null ? pal.Log.AddRange(newEvts) : pal.Log,
      NewLogEvents = pal.NewLogEvents || newEvts != null,
    };
  }

  private static ImmutableList<(
    MaterialToLoadOntoFace,
    Func<IEnumerable<LogEntry>, LoadedFace>
  )> CalcMaterialToLoad(
    int palletNum,
    IEnumerable<InProcessMaterial> materialToLoad,
    IJobCacheWithDefaultStops jobs
  )
  {
    return materialToLoad
      .Where(m =>
        m.Action.Type == InProcessMaterialAction.ActionType.Loading && m.Action.LoadOntoPalletNum == palletNum
      )
      .GroupBy(m => m.Action.LoadOntoFace ?? 1)
      .Select(face =>
      {
        var job = jobs.Lookup(face.First().JobUnique);
        var process = face.First().Action.ProcessAfterLoad ?? 1;
        var path = face.First().Action.PathAfterLoad ?? 1;

        var matOnFaceAfterLoad = face.Select(m =>
            m with
            {
              Process = process,
              Path = path,
              LastCompletedMachiningRouteStopIndex = null,
              Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.OnPallet,
                PalletNum = palletNum,
                Face = face.Key,
              },
            }
          )
          .ToImmutableList();

        IReadOnlyList<MachiningStop> stops;
        bool isFinalProcess;
        string? outputQueue;
        TimeSpan? expectedLoadTimeForOnePieceOfMaterial;
        TimeSpan? expectedUnloadTimeForOnePieceOfMaterial;
        if (job != null)
        {
          stops = job.Processes[process - 1].Paths[path - 1].Stops;
          isFinalProcess = process == job.Processes.Count;
          outputQueue = job.Processes[process - 1].Paths[path - 1].OutputQueue;
          expectedLoadTimeForOnePieceOfMaterial = job.Processes[process - 1].Paths[path - 1].ExpectedLoadTime;
          expectedUnloadTimeForOnePieceOfMaterial = job.Processes[process - 1]
            .Paths[path - 1]
            .ExpectedUnloadTime;
        }
        else
        {
          var info = jobs.DefaultPathInfo(matOnFaceAfterLoad);
          stops = info.Stops;
          isFinalProcess = info.IsFinalProcess;
          outputQueue = info.OutputQueue;
          expectedLoadTimeForOnePieceOfMaterial = info.ExpectedLoadTimeForOnePieceOfMaterial;
          expectedUnloadTimeForOnePieceOfMaterial = info.ExpectedUnloadTimeForOnePieceOfMaterial;
        }

        var matToLoad = (
          new MaterialToLoadOntoFace()
          {
            FaceNum = face.Key,
            Process = process,
            Path = path,
            ActiveOperationTime = (expectedLoadTimeForOnePieceOfMaterial ?? TimeSpan.Zero) * face.Count(),
            MaterialIDs = face.Select(m => m.MaterialID).ToImmutableList(),
          }
        );

        Func<IEnumerable<LogEntry>, LoadedFace> newFace = loadEnds => new LoadedFace()
        {
          FaceNum = face.Key,
          Job = job,
          Process = process,
          Path = path,
          IsFinalProcess = isFinalProcess,
          OutputQueue = outputQueue,
          ExpectedUnloadTimeForOnePieceOfMaterial = expectedUnloadTimeForOnePieceOfMaterial,
          LoadEnd = loadEnds.First(e =>
            e.LogType == LogType.LoadUnloadCycle
            && e.Material.Any(m => face.Any(f => f.MaterialID == m.MaterialID))
          ),
          Stops = stops
            .Select(
              (stop, stopIdx) =>
                new MachiningStopAndEvents()
                {
                  StopIdx = stopIdx,
                  StationGroup = stop.StationGroup,
                  Program = stop.Program,
                  Stations = stop.Stations,
                  ExpectedCycleTime = stop.ExpectedCycleTime,
                  ProgramRevision = stop.ProgramRevision,
                  MachineStart = null,
                  MachineEnd = null,
                }
            )
            .ToImmutableList(),
          Material = matOnFaceAfterLoad,
          UnloadStart = null,
          UnloadEnd = null,
        };

        return (matToLoad, newFace);
      })
      .ToImmutableList();
  }

  private static bool CheckMaterialMatches(Pallet pal, IEnumerable<InProcessMaterial>? materialToLoad)
  {
    if (materialToLoad == null || !materialToLoad.Any())
    {
      // no material to load, check if there is any material on the pallet
      return pal.Faces.Count == 0;
    }

    if (pal.Faces.Count == 0)
    {
      // material to load, but no faces
      return false;
    }

    var mats = materialToLoad.ToLookup(m =>
      m.Action.Type == InProcessMaterialAction.ActionType.Loading ? m.Action.LoadOntoFace : m.Location.Face
    );

    if (mats.Count != pal.Faces.Count)
    {
      // different number of faces
      return false;
    }

    foreach (var faceToLoad in mats)
    {
      if (faceToLoad.Key.HasValue && pal.Faces.TryGetValue(faceToLoad.Key.Value, out var face))
      {
        // check if list of (MaterialID, Proc)s is different
        if (
          !faceToLoad
            .Select(m =>
              (
                m.MaterialID,
                (
                  m.Action.Type == InProcessMaterialAction.ActionType.Loading
                    ? m.Action.ProcessAfterLoad
                    : null
                ) ?? m.Process
              )
            )
            .OrderBy(mid => mid)
            .SequenceEqual(face.Material.Select(m => (m.MaterialID, m.Process)).OrderBy(mid => mid))
        )
        {
          return false;
        }
      }
      else
      {
        // face not found
        return false;
      }
    }

    return true;
  }

  private static Pallet CompletePalletCycle(
    Pallet pal,
    int loadNum,
    IEnumerable<InProcessMaterial>? materialToLoad,
    IRepository db,
    FMSSettings fmsSettings,
    IJobCacheWithDefaultStops jobs,
    DateTime nowUTC
  )
  {
    var toUnload = new List<MaterialToUnloadFromFace>();
    foreach (var face in pal.Faces.Values)
    {
      var u = UnloadMaterial(pal, face);
      if (u != null)
      {
        toUnload.Add(u);
      }
    }

    var matsToLoad =
      materialToLoad != null
        ? CalcMaterialToLoad(palletNum: pal.PalletNum, materialToLoad: materialToLoad, jobs: jobs)
        : ImmutableList<(MaterialToLoadOntoFace, Func<IEnumerable<LogEntry>, LoadedFace>)>.Empty;

    var newEvts = db.RecordLoadUnloadComplete(
      toLoad: matsToLoad.Select((m) => m.Item1).ToImmutableList(),
      toUnload: toUnload,
      previouslyLoaded: null, // TODO: previouslyLoaded
      previouslyUnloaded: null, // TODO: previouslyUnloaded
      lulNum: loadNum,
      pallet: pal.PalletNum,
      totalElapsed: nowUTC - (LoadUnloadStartTime(pal) ?? nowUTC),
      timeUTC: nowUTC,
      externalQueues: fmsSettings.ExternalQueues
    );

    var cycleEvt = newEvts.First(e => e.LogType == LogType.PalletCycle);
    var loadEvts = newEvts.Where(e => e.EndTimeUTC > nowUTC).ToImmutableList();

    var newFaces = ImmutableDictionary.CreateBuilder<int, LoadedFace>();
    foreach (var (_, face) in matsToLoad)
    {
      var loadedFace = face(loadEvts);
      newFaces.Add(loadedFace.FaceNum, loadedFace);
    }

    pal = pal with
    {
      Faces = newFaces.ToImmutable(),
      Log = loadEvts.ToImmutableList(),
      NewLogEvents = true,
      LastPalletCycle = cycleEvt,
    };

    return pal;
  }
}
