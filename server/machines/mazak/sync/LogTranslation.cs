/* Copyright (c) 2024, John Lenz

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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BlackMaple.MachineFramework;
using MWI = BlackMaple.MachineFramework;

namespace MazakMachineInterface
{
  public class LogTranslation
  {
    private readonly IRepository repo;
    private readonly MazakAllDataAndLogs mazakData;
    private readonly string machGroupName;
    private readonly FMSSettings fmsSettings;
    private readonly Action<LogEntry> onMazakLog;
    private readonly MazakConfig mazakConfig;
    private readonly Func<IEnumerable<ToolPocketRow>> loadTools;

    private LogTranslation(
      IRepository repo,
      MazakAllDataAndLogs mazakData,
      string machGroupName,
      FMSSettings fmsSettings,
      Action<LogEntry> onMazakLog,
      MazakConfig mazakConfig,
      Func<IEnumerable<ToolPocketRow>> loadTools
    )
    {
      this.repo = repo;
      this.mazakData = mazakData;
      this.machGroupName = machGroupName;
      this.fmsSettings = fmsSettings;
      this.onMazakLog = onMazakLog;
      this.mazakConfig = mazakConfig;
      this.loadTools = loadTools;
    }

    public static HandleEventResult HandleEvents(
      IRepository repo,
      MazakAllDataAndLogs mazakData,
      string machGroupName,
      FMSSettings fmsSettings,
      Action<LogEntry> onMazakLog,
      MazakConfig mazakConfig,
      Func<IEnumerable<ToolPocketRow>> loadTools
    )
    {
      var t = new LogTranslation(
        repo: repo,
        mazakData: mazakData,
        machGroupName: machGroupName,
        fmsSettings: fmsSettings,
        onMazakLog: onMazakLog,
        mazakConfig: mazakConfig,
        loadTools: loadTools
      );
      return t.Process();
    }

    public record HandleEventResult
    {
      public required bool StoppedBecauseRecentMachineEvent { get; init; }
      public required int? PalletWithMostRecentEventAsLoadUnloadEnd { get; init; }
      public required bool PalletStatusChanged { get; init; }
    }

    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<LogTranslation>();

    private readonly Dictionary<string, Job> _jobs = [];

    private Job GetJob(string unique)
    {
      if (_jobs.TryGetValue(unique, out Job value))
        return value;
      else
      {
        var j = repo.LoadJob(unique);
        _jobs.Add(unique, j);
        return j;
      }
    }

    #region Events
    // should be union
    private record LogChunk
    {
      public IReadOnlyList<LogEntry> LulEndChunk { get; init; }
      public LogEntry NonLulEndEvt { get; init; }
      public int? PalletForFinalLULEvents { get; init; }
    }

    private static IEnumerable<LogChunk> ChunkLulEvents(IEnumerable<LogEntry> entries)
    {
      // We want to group the LoadEnd and UnloadEnd events together since Mazak creates
      // one event per process/face. Mazak generates the logs serially and always seems to
      // generate all the load and unload events wit the same filename/foreignID of
      // LGdate-time-001.csv, LGdate-time-002.csv, LGdate-time-003.csv with the date/time identical.
      // Thus, as soon as another non-LUL event is generated, we can assume that the previous
      // chunk is complete and yield it.
      var curChunk = new List<LogEntry>();

      foreach (var e in entries.OrderBy(e => e.ForeignID))
      {
        if (e.Code == LogCode.LoadEnd || e.Code == LogCode.UnloadEnd)
        {
          if (curChunk.Count == 0)
          {
            curChunk.Add(e);
          }
          else if (
            curChunk[0].Pallet == e.Pallet
            // We could check ForeignID prefix matches here, but be more lenient
            // just in case.
            && curChunk[0].TimeUTC.Subtract(e.TimeUTC).Duration() < TimeSpan.FromSeconds(3)
          )
          {
            curChunk.Add(e);
          }
          else
          {
            // yield the current chunk and start a new one
            yield return new LogChunk() { LulEndChunk = curChunk };
            curChunk.Clear();
            curChunk.Add(e);
          }
        }
        else
        {
          // something not LUL, yield the current chunk if there is one
          if (curChunk.Count > 0)
          {
            // yield the current chunk and start a new one
            yield return new LogChunk() { LulEndChunk = curChunk };
            curChunk.Clear();
          }

          yield return new LogChunk() { NonLulEndEvt = e };
        }
      }

      if (curChunk.Count > 0)
      {
        // If the most recent event is a LUL, we may be reading the logs in the middle of
        // Mazak writing them out.  In this case, don't process the events and leave them
        // on disk for the next time we read the logs.
        yield return new LogChunk() { PalletForFinalLULEvents = curChunk[0].Pallet };
      }
    }

    private HandleEventResult Process()
    {
      var stoppedFromMcEvt = false;
      int? palForFinalEvts = null;

      foreach (var e in ChunkLulEvents(mazakData.Logs))
      {
        if (e.LulEndChunk != null)
        {
          try
          {
            HandleLoadEnd(e.LulEndChunk);
            foreach (var lul in e.LulEndChunk)
            {
              onMazakLog(lul);
            }
          }
          catch (Exception ex)
          {
            Log.Error(ex, "Error translating log event at time {t}", e.LulEndChunk[0].TimeUTC);
          }
        }
        else if (e.NonLulEndEvt != null)
        {
          try
          {
            if (!HandleNonLulEndEvent(e.NonLulEndEvt))
            {
              stoppedFromMcEvt = true;
              break;
            }
            else
            {
              onMazakLog(e.NonLulEndEvt);
            }
          }
          catch (Exception ex)
          {
            Log.Error(ex, "Error translating log event at time {t}", e.NonLulEndEvt.TimeUTC);
          }
        }
        else if (e.PalletForFinalLULEvents.HasValue)
        {
          palForFinalEvts = e.PalletForFinalLULEvents.Value;
          break;
        }
      }

      bool palStChanged = false;
      if (!stoppedFromMcEvt && !palForFinalEvts.HasValue)
      {
        palStChanged = CheckPalletStatusMatchesLogs();
      }

      return new HandleEventResult()
      {
        StoppedBecauseRecentMachineEvent = stoppedFromMcEvt,
        PalletWithMostRecentEventAsLoadUnloadEnd = palForFinalEvts,
        PalletStatusChanged = palStChanged,
      };
    }

    private void HandleLoadEnd(IReadOnlyList<LogEntry> es)
    {
      int pallet = es[0].Pallet;

      var cycle = new List<MWI.LogEntry>();
      if (pallet >= 1)
        cycle = repo.CurrentPalletLog(pallet);

      var toLoad = FindMatToLoad(es, cycle);
      var toUnload = FindMatToUnload(es, cycle);

      repo.RecordLoadUnloadComplete(
        toLoad: toLoad,
        previouslyLoaded: null,
        toUnload: toUnload,
        previouslyUnloaded: null,
        pallet: pallet,
        lulNum: es[0].StationNumber,
        totalElapsed: CalculateElapsed(es[0].TimeUTC, LogType.LoadUnloadCycle, cycle, es[0].StationNumber),
        timeUTC: es[0].TimeUTC,
        externalQueues: fmsSettings.ExternalQueues
      );
    }

    private bool HandleNonLulEndEvent(LogEntry e)
    {
      var cycle = new List<MWI.LogEntry>();
      if (e.Pallet >= 1)
        cycle = repo.CurrentPalletLog(e.Pallet);

      Log.Debug(
        "Handling mazak event {@event} with pallets {@palletPos} with {@contents}",
        e,
        mazakData.PalletPositions,
        mazakData.PalletSubStatuses
      );

      switch (e.Code)
      {
        case LogCode.LoadBegin:

          repo.RecordLoadStart(
            mats:
            [
              new EventLogMaterial()
              {
                MaterialID = -1,
                Process = e.Process,
                Face = 0,
              },
            ],
            pallet: e.Pallet,
            lulNum: e.StationNumber,
            timeUTC: e.TimeUTC,
            foreignId: e.ForeignID
          );

          break;

        case LogCode.UnloadBegin:

          repo.RecordUnloadStart(
            mats: GetMaterialOnPallet(e, cycle).Select(m => m.Mat),
            pallet: e.Pallet,
            lulNum: e.StationNumber,
            timeUTC: e.TimeUTC,
            foreignId: e.ForeignID
          );

          break;

        case LogCode.MachineCycleStart:
        {
          int mcNum = e.StationNumber;
          if (mazakConfig.MachineNumbers != null && mcNum > 0 && mcNum <= mazakConfig.MachineNumbers.Count)
          {
            mcNum = mazakConfig.MachineNumbers[mcNum - 1];
          }

          IEnumerable<ToolSnapshot> pockets = null;
          if ((DateTime.UtcNow - e.TimeUTC).Duration().TotalMinutes < 5)
          {
            pockets = ToolsToSnapshot(mcNum, loadTools());
          }

          var machineMats = GetMaterialOnPallet(e, cycle);
          LookupProgram(machineMats, e, out var progName, out var progRev);
          repo.RecordMachineStart(
            mats: machineMats.Select(m => m.Mat),
            pallet: e.Pallet,
            statName: machGroupName,
            statNum: mcNum,
            program: progName,
            timeUTC: e.TimeUTC,
            pockets: pockets,
            foreignId: e.ForeignID,
            extraData: !progRev.HasValue
              ? null
              : new Dictionary<string, string> { { "ProgramRevision", progRev.Value.ToString() } }
          );

          break;
        }

        case LogCode.MachineCycleEnd:
        {
          int mcNum = e.StationNumber;
          if (mazakConfig.MachineNumbers != null && mcNum > 0 && mcNum <= mazakConfig.MachineNumbers.Count)
          {
            mcNum = mazakConfig.MachineNumbers[mcNum - 1];
          }

          // Tool snapshots take ~5 seconds from the end of the cycle until the updated tools are available in open database kit,
          // so stop processing log entries if the machine cycle end occurred 15 seconds in the past.
          if (DateTime.UtcNow.Subtract(e.TimeUTC).Duration() <= TimeSpan.FromSeconds(15))
          {
            return false;
          }

          var machStart = FindMachineStart(e, cycle, mcNum);
          TimeSpan elapsed;
          IEnumerable<ToolSnapshot> toolsAtStart;
          if (machStart != null)
          {
            elapsed = e.TimeUTC.Subtract(machStart.EndTimeUTC);
            toolsAtStart = repo.ToolPocketSnapshotForCycle(machStart.Counter);
          }
          else
          {
            Log.Debug("Calculating elapsed time for {@entry} did not find a previous cycle event", e);
            elapsed = TimeSpan.Zero;
            toolsAtStart = Enumerable.Empty<ToolSnapshot>();
          }
          var toolsAtEnd = ToolsToSnapshot(mcNum, loadTools());

          if (elapsed > TimeSpan.FromSeconds(30))
          {
            var machineMats = GetMaterialOnPallet(e, cycle);
            LookupProgram(machineMats, e, out var progName, out var progRev);
            var s = repo.RecordMachineEnd(
              mats: machineMats.Select(m => m.Mat),
              pallet: e.Pallet,
              statName: machGroupName,
              statNum: mcNum,
              program: progName,
              timeUTC: e.TimeUTC,
              result: "",
              elapsed: elapsed,
              active: CalculateActiveMachining(machineMats),
              tools: ToolSnapshotDiff.Diff(toolsAtStart, toolsAtEnd),
              deleteToolSnapshotsFromCntr: machStart?.Counter,
              pockets: toolsAtEnd,
              foreignId: e.ForeignID,
              extraData: !progRev.HasValue
                ? null
                : new Dictionary<string, string> { { "ProgramRevision", progRev.Value.ToString() } }
            );
            CheckForInspections(s, machineMats);
          }
          else
          {
            //TODO: add this with a FAIL result and skip the event in Update Log?
            Log.Warning(
              "Ignoring machine cycle at {time} on pallet {pallet} because it is less than 30 seconds",
              e.TimeUTC,
              e.Pallet
            );
          }

          break;
        }

        case LogCode.StartRotatePalletIntoMachine:
        {
          int mcNum = e.StationNumber;
          if (mazakConfig.MachineNumbers != null && mcNum > 0 && mcNum <= mazakConfig.MachineNumbers.Count)
          {
            mcNum = mazakConfig.MachineNumbers[mcNum - 1];
          }
          repo.RecordPalletDepartRotaryInbound(
            mats: GetAllMaterialOnPallet(cycle).Select(EventLogMaterial.FromLogMat),
            pallet: e.Pallet,
            statName: machGroupName,
            statNum: mcNum,
            timeUTC: e.TimeUTC,
            rotateIntoWorktable: true,
            elapsed: CalculateElapsed(e.TimeUTC, LogType.PalletOnRotaryInbound, cycle, mcNum),
            foreignId: e.ForeignID
          );
          break;
        }

        case LogCode.PalletMoving:
          // Mxx1 is inbound at machine xx, Mxx2 is table at machine xx
          if (
            e.FromPosition != null
            && e.FromPosition.StartsWith("M")
            && e.FromPosition.Length == 4
            && e.FromPosition.EndsWith("1")
          )
          {
            if (
              LastEventWasRotaryDropoff(cycle) && int.TryParse(e.FromPosition.Substring(1, 2), out var mcNum)
            )
            {
              if (
                mazakConfig.MachineNumbers != null
                && mcNum > 0
                && mcNum <= mazakConfig.MachineNumbers.Count
              )
              {
                mcNum = mazakConfig.MachineNumbers[mcNum - 1];
              }
              repo.RecordPalletDepartRotaryInbound(
                mats: GetAllMaterialOnPallet(cycle).Select(EventLogMaterial.FromLogMat),
                pallet: e.Pallet,
                statName: machGroupName,
                statNum: mcNum,
                rotateIntoWorktable: false,
                timeUTC: e.TimeUTC,
                elapsed: CalculateElapsed(e.TimeUTC, LogType.PalletOnRotaryInbound, cycle, mcNum),
                foreignId: e.ForeignID
              );
            }
          }
          else if (e.FromPosition != null && e.FromPosition.StartsWith("S") && e.FromPosition != "STA")
          {
            if (int.TryParse(e.FromPosition.Substring(1), out var stockerNum))
            {
              repo.RecordPalletDepartStocker(
                mats: GetAllMaterialOnPallet(cycle).Select(EventLogMaterial.FromLogMat),
                pallet: e.Pallet,
                stockerNum: stockerNum,
                timeUTC: e.TimeUTC,
                waitForMachine: !cycle.Any(c => c.LogType == LogType.MachineCycle),
                elapsed: CalculateElapsed(e.TimeUTC, LogType.PalletInStocker, cycle, stockerNum),
                foreignId: e.ForeignID
              );
            }
          }
          break;

        case LogCode.PalletMoveComplete:
          // Mxx1 is inbound at machine xx, Mxx2 is table at machine xx
          if (
            e.TargetPosition != null
            && e.TargetPosition.StartsWith("M")
            && e.TargetPosition.Length == 4
            && e.TargetPosition.EndsWith("1")
          )
          {
            if (int.TryParse(e.TargetPosition.Substring(1, 2), out var mcNum))
            {
              if (
                mazakConfig.MachineNumbers != null
                && mcNum > 0
                && mcNum <= mazakConfig.MachineNumbers.Count
              )
              {
                mcNum = mazakConfig.MachineNumbers[mcNum - 1];
              }
              repo.RecordPalletArriveRotaryInbound(
                mats: GetAllMaterialOnPallet(cycle).Select(EventLogMaterial.FromLogMat),
                pallet: e.Pallet,
                statName: machGroupName,
                statNum: mcNum,
                timeUTC: e.TimeUTC,
                foreignId: e.ForeignID
              );
            }
          }
          else if (e.TargetPosition != null && e.TargetPosition.StartsWith("S") && e.TargetPosition != "STA")
          {
            if (int.TryParse(e.TargetPosition.Substring(1), out var stockerNum))
            {
              repo.RecordPalletArriveStocker(
                mats: GetAllMaterialOnPallet(cycle).Select(EventLogMaterial.FromLogMat),
                pallet: e.Pallet,
                stockerNum: stockerNum,
                waitForMachine: !cycle.Any(c => c.LogType == LogType.MachineCycle),
                timeUTC: e.TimeUTC,
                foreignId: e.ForeignID
              );
            }
          }
          break;
      }

      return true;
    }
    #endregion

    #region Material
    private List<MWI.LogMaterial> GetAllMaterialOnPallet(IList<MWI.LogEntry> oldEvents)
    {
      return oldEvents
        .Where(e => e.LogType == LogType.LoadUnloadCycle && !e.StartOfCycle && e.Result == "LOAD")
        .SelectMany(e => e.Material)
        .Where(m => !repo.IsMaterialInQueue(m.MaterialID))
        .DistinctBy(m => m.MaterialID)
        .ToList();
    }

    private struct LogMaterialAndPath
    {
      public EventLogMaterial Mat { get; set; }
      public string Unique { get; set; }
      public string PartName { get; set; }
    }

    private List<LogMaterialAndPath> GetMaterialOnPallet(LogEntry e, IList<MWI.LogEntry> oldEvents)
    {
      var byMatId = ParseMaterialFromPreviousEvents(
        jobPartName: e.JobPartName,
        proc: e.Process,
        isUnloadEnd: e.Code == LogCode.UnloadEnd,
        oldEvents: oldEvents
      );
      FindSchedule(e.FullPartName, e.Process, out string unique, out int numProc);

      if (GetJob(unique) == null)
      {
        unique = "";
      }

      var ret = new List<LogMaterialAndPath>();

      for (int i = 1; i <= e.FixedQuantity; i += 1)
      {
        if (byMatId.Count > 0)
        {
          ret.Add(
            new LogMaterialAndPath()
            {
              Mat = byMatId.GetValueAtIndex(0),
              PartName = e.JobPartName,
              Unique = unique,
            }
          );
          byMatId.RemoveAt(0);
        }
        else
        {
          //something went wrong, must create material
          ret.Add(
            new LogMaterialAndPath()
            {
              Mat = new EventLogMaterial()
              {
                MaterialID = repo.AllocateMaterialIDAndGenerateSerial(
                  unique,
                  e.JobPartName,
                  numProc,
                  e.TimeUTC,
                  out var _
                ),
                Process = e.Process,
                Face = e.Process,
              },
              PartName = e.JobPartName,
              Unique = unique,
            }
          );

          Log.Warning(
            "When attempting to find material for event {@event} on unique {unique}, there was no previous cycles with material on face",
            e,
            unique
          );
        }
      }

      foreach (var m in ret)
      {
        repo.RecordPathForProcess(m.Mat.MaterialID, m.Mat.Process, path: 1);
      }

      return ret;
    }

    private SortedList<long, EventLogMaterial> ParseMaterialFromPreviousEvents(
      string jobPartName,
      int proc,
      bool isUnloadEnd,
      IList<MWI.LogEntry> oldEvents
    )
    {
      var byMatId = new SortedList<long, EventLogMaterial>();

      for (int i = oldEvents.Count - 1; i >= 0; i -= 1)
      {
        // When looking for material for an unload event, we want to skip over load events,
        // since an ending load event might have come through with the new material id that is loaded.
        if (isUnloadEnd && oldEvents[i].Result == "LOAD")
        {
          continue;
        }

        // material can be queued if it is removed by the operator from the pallet, which is
        // detected in CheckPalletStatusMatchesLogs() below
        if (oldEvents[i].Material.Any(m => repo.IsMaterialInQueue(m.MaterialID)))
        {
          continue;
        }

        foreach (LogMaterial mat in oldEvents[i].Material)
        {
          if (
            mat.PartName == jobPartName
            && mat.Process == proc
            && mat.MaterialID >= 0
            && !byMatId.ContainsKey(mat.MaterialID)
          )
          {
            byMatId[mat.MaterialID] = new EventLogMaterial()
            {
              MaterialID = mat.MaterialID,
              Process = proc,
              Face = proc,
            };
          }
        }
      }

      return byMatId;
    }

    private List<MaterialToLoadOntoFace> FindMatToLoad(
      IEnumerable<LogEntry> events,
      List<MWI.LogEntry> oldPalEvents
    )
    {
      var toLoad = new List<MaterialToLoadOntoFace>();

      foreach (var e in events)
      {
        if (e.Code != LogCode.LoadEnd)
          continue;

        int proc = e.Process;
        int fixQty = e.FixedQuantity;
        FindSchedule(e.FullPartName, proc, out string unique, out int numProc);

        Log.Debug("Found job {unique} with number of procs {numProc}", unique, numProc);

        Job job = string.IsNullOrEmpty(unique) ? null : GetJob(unique);
        if (job == null)
        {
          Log.Warning("Unable to find job for load {@pending} with unique {@uniq}", e, unique);
          unique = "";
        }

        var mats = ImmutableList.CreateBuilder<long>();
        if (job != null && !string.IsNullOrEmpty(job.Processes[proc - 1].Paths[0].InputQueue))
        {
          var info = job.Processes[proc - 1].Paths[0];
          // search input queue for material
          Log.Debug("Searching queue {queue} for {unique}-{proc} to load", info.InputQueue, unique, proc);

          var qs = MazakQueues.QueuedMaterialForLoading(
            job.UniqueStr,
            repo.GetMaterialInQueueByUnique(info.InputQueue, job.UniqueStr),
            proc
          );

          if (fixQty <= qs.Count)
          {
            // enough already assigned, use them
            mats.AddRange(qs.Take(fixQty).Select(qmat => qmat.MaterialID));
          }
          else
          {
            // add everything already assigned (if any)
            mats.AddRange(qs.Select(qmat => qmat.MaterialID));

            // first, try assigning any castings
            if (proc == 1)
            {
              mats.AddRange(
                repo.AllocateCastingsInQueue(
                  queue: job.Processes[0].Paths[0].InputQueue,
                  casting: job.Processes[0].Paths[0].Casting ?? job.PartName,
                  unique: job.UniqueStr,
                  part: job.PartName,
                  proc1Path: 1,
                  numProcesses: job.Processes.Count,
                  count: fixQty - mats.Count
                )
              );
            }

            // if there are still not enough, create them
            if (mats.Count < fixQty)
            {
              Log.Warning(
                "Not enough material in queue {queue} for {part}-{proc}, creating new material for {@pending}",
                info.InputQueue,
                e.FullPartName,
                proc,
                e
              );
              for (int i = mats.Count + 1; i <= fixQty; i++)
              {
                mats.Add(
                  repo.AllocateMaterialIDAndGenerateSerial(
                    unique,
                    e.JobPartName,
                    numProc,
                    e.TimeUTC,
                    out var _
                  )
                );
              }
            }
          }
        }
        else if (proc == 1)
        {
          // no input queue so just create new material
          Log.Debug("Creating new material for unique {unique} process 1", unique);
          for (int i = 1; i <= fixQty; i += 1)
          {
            mats.Add(
              repo.AllocateMaterialIDAndGenerateSerial(unique, e.JobPartName, numProc, e.TimeUTC, out var _)
            );
          }
        }
        else
        {
          // search on pallet in the previous process for material
          Log.Debug(
            "Searching on pallet for unique {unique} process {proc} to load into process {proc}",
            unique,
            proc - 1,
            proc
          );
          var byMatId = ParseMaterialFromPreviousEvents(
            jobPartName: e.JobPartName,
            proc: proc - 1,
            isUnloadEnd: false,
            oldEvents: oldPalEvents
          );
          for (int i = 1; i <= fixQty; i += 1)
          {
            if (byMatId.Count > 0)
            {
              var old = byMatId.GetValueAtIndex(0);
              byMatId.RemoveAt(0);
              mats.Add(old.MaterialID);
            }
            else
            {
              //something went wrong, must create material
              mats.Add(
                repo.AllocateMaterialIDAndGenerateSerial(unique, e.JobPartName, numProc, e.TimeUTC, out var _)
              );

              Log.Warning(
                "Could not find material for previous process {proc}, creating new material for {@event}",
                proc - 1,
                e
              );
            }
          }
        }

        toLoad.Add(
          new MaterialToLoadOntoFace()
          {
            MaterialIDs = mats.ToImmutable(),
            Process = proc,
            FaceNum = proc,
            Path = 1,
            ActiveOperationTime = CalculateActiveLoadTime(e, job),
            ForeignID = e.ForeignID,
          }
        );
      }

      return toLoad;
    }

    private List<MaterialToUnloadFromFace> FindMatToUnload(
      IEnumerable<LogEntry> events,
      List<MWI.LogEntry> oldEvents
    )
    {
      var ret = new List<MaterialToUnloadFromFace>();

      foreach (var e in events)
      {
        if (e.Code != LogCode.UnloadEnd)
          continue;

        var mats = GetMaterialOnPallet(e, oldEvents);

        var matToQueue = ImmutableDictionary.CreateBuilder<long, string>();

        foreach (var mat in mats)
        {
          var signalQuarantine = oldEvents.LastOrDefault(e =>
            e.LogType == LogType.SignalQuarantine && e.Material.Any(m => m.MaterialID == mat.Mat.MaterialID)
          );

          if (signalQuarantine != null)
          {
            matToQueue[mat.Mat.MaterialID] = signalQuarantine.LocationName ?? fmsSettings.QuarantineQueue;
          }
          else
          {
            Job job = GetJob(mat.Unique);

            if (job != null)
            {
              var q = job.Processes[mat.Mat.Process - 1].Paths[0].OutputQueue;
              if (
                !string.IsNullOrEmpty(q)
                && (fmsSettings.Queues.ContainsKey(q) || fmsSettings.ExternalQueues.ContainsKey(q))
              )
              {
                matToQueue[mat.Mat.MaterialID] = q;
              }
              else
              {
                matToQueue[mat.Mat.MaterialID] = null;
              }
            }
            else
            {
              matToQueue[mat.Mat.MaterialID] = null;
            }
          }
        }

        ret.Add(
          new MaterialToUnloadFromFace()
          {
            MaterialIDToQueue = matToQueue.ToImmutable(),
            Process = e.Process,
            FaceNum = e.Process,
            ActiveOperationTime = CalculateActiveUnloadTime(mats),
            ForeignID = e.ForeignID,
          }
        );
      }

      return ret;
    }

    public void FindSchedule(string mazakPartName, int proc, out string unique, out int numProc)
    {
      unique = "";
      numProc = proc;
      foreach (var schRow in mazakData.Schedules)
      {
        if (schRow.PartName == mazakPartName && !string.IsNullOrEmpty(schRow.Comment))
        {
          unique = MazakPart.UniqueFromComment(schRow.Comment);
          numProc = schRow.Processes.Count;
          if (numProc < proc)
            numProc = proc;
          return;
        }
      }
    }
    #endregion

    #region Compare Status With Events
    private bool CheckPalletStatusMatchesLogs()
    {
      if (string.IsNullOrEmpty(fmsSettings.QuarantineQueue))
        return false;

      bool matMovedToQueue = false;
      foreach (var pal in mazakData.PalletPositions.Where(p => !p.PalletPosition.StartsWith("LS")))
      {
        var oldEvts = repo.CurrentPalletLog(pal.PalletNumber);

        // start with everything on the pallet
        List<LogMaterial> matsOnPal = GetAllMaterialOnPallet(oldEvts);

        foreach (var st in mazakData.PalletSubStatuses.Where(s => s.PalletNumber == pal.PalletNumber))
        {
          // remove material from matsOnPal that matches this PalletSubStatus
          var sch = mazakData.Schedules.FirstOrDefault(s => s.Id == st.ScheduleID);
          if (sch == null)
            continue;
          var unique = MazakPart.UniqueFromComment(sch.Comment);
          if (string.IsNullOrEmpty(unique))
            continue;

          var matchingMats = matsOnPal
            .Where(m => m.JobUniqueStr == unique && m.Process == st.PartProcessNumber)
            .ToList();

          if (matchingMats.Count < st.FixQuantity)
          {
            Log.Warning("Pallet {@pal} has material assigned, but no load event", st);
          }
          else
          {
            foreach (var mat in matchingMats)
              matsOnPal.Remove(mat);
          }
        }

        // anything left in matsOnPal disappeared from PalletSubStatuses so should be quarantined
        foreach (var extraMat in matsOnPal)
        {
          repo.RecordAddMaterialToQueue(
            mat: new EventLogMaterial()
            {
              MaterialID = extraMat.MaterialID,
              Process = extraMat.Process,
              Face = 0,
            },
            queue: fmsSettings.QuarantineQueue,
            position: -1,
            operatorName: null,
            reason: "MaterialMissingOnPallet"
          );
          matMovedToQueue = true;
        }
      }

      return matMovedToQueue;
    }
    #endregion

    #region Elapsed
    private static bool LastEventWasRotaryDropoff(IList<MWI.LogEntry> oldEvents)
    {
      var lastWasDropoff = false;
      foreach (var e in oldEvents)
      {
        if (e.LogType == LogType.PalletOnRotaryInbound && e.StartOfCycle)
        {
          lastWasDropoff = true;
        }
        else if (e.LogType == LogType.PalletOnRotaryInbound && !e.StartOfCycle)
        {
          lastWasDropoff = false;
        }
        else if (e.LogType == LogType.MachineCycle)
        {
          lastWasDropoff = false;
        }
      }
      return lastWasDropoff;
    }

    private static MWI.LogEntry FindMachineStart(LogEntry e, IList<MWI.LogEntry> oldEvents, int statNum)
    {
      return oldEvents.LastOrDefault(old =>
        old.LogType == LogType.MachineCycle && old.StartOfCycle && old.LocationNum == statNum
      );
    }

    private static TimeSpan CalculateElapsed(
      DateTime eventTime,
      LogType code,
      IList<MWI.LogEntry> oldEvents,
      int statNum
    )
    {
      for (int i = oldEvents.Count - 1; i >= 0; i -= 1)
      {
        var ev = oldEvents[i];
        if (ev.LogType == code && ev.LocationNum == statNum)
        {
          if (ev.StartOfCycle)
            return eventTime.Subtract(ev.EndTimeUTC);
          break;
        }
      }
      return TimeSpan.Zero;
    }

    private TimeSpan CalculateActiveMachining(IEnumerable<LogMaterialAndPath> mats)
    {
      TimeSpan total = TimeSpan.Zero;
      //for now, assume only one stop per process and each path is the same time
      var procs = mats.Select(m => new { Unique = m.Unique, Process = m.Mat.Process }).Distinct();
      foreach (var proc in procs)
      {
        var job = GetJob(proc.Unique);
        if (job == null)
          continue;
        var stop = job.Processes[proc.Process - 1].Paths[0].Stops.FirstOrDefault();
        if (stop == null)
          continue;
        total += stop.ExpectedCycleTime;
      }

      return total;
    }

    private static TimeSpan CalculateActiveLoadTime(LogEntry e, Job job)
    {
      if (job == null)
        return TimeSpan.Zero;
      return TimeSpan.FromTicks(
        job.Processes[e.Process - 1].Paths[0].ExpectedLoadTime.Ticks * e.FixedQuantity
      );
    }

    private TimeSpan CalculateActiveUnloadTime(IEnumerable<LogMaterialAndPath> mats)
    {
      var ticks = mats.Select(m =>
        {
          var job = GetJob(m.Unique);
          if (job == null)
            return TimeSpan.Zero;
          return job.Processes[m.Mat.Process - 1].Paths[0].ExpectedUnloadTime;
        })
        .Select(t => t.Ticks)
        .Sum();
      return TimeSpan.FromTicks(ticks);
    }
    #endregion

    #region Programs, Inspections, and Tools
    private void LookupProgram(
      IReadOnlyList<LogMaterialAndPath> mats,
      LogEntry e,
      out string progName,
      out long? rev
    )
    {
      if (LookupProgramFromJob(mats, e, out progName, out rev))
      {
        // ok, just return
        return;
      }
      else if (LookupProgramFromMazakDb(e, out progName, out rev))
      {
        // ok, just return
        return;
      }
      else
      {
        progName = e.Program;
        rev = null;
        return;
      }
    }

    private bool LookupProgramFromJob(
      IReadOnlyList<LogMaterialAndPath> mats,
      LogEntry e,
      out string progName,
      out long? rev
    )
    {
      progName = null;
      rev = null;

      if (mats.Count == 0)
        return false;
      ;

      var firstMat = mats.FirstOrDefault();
      if (string.IsNullOrEmpty(firstMat.Unique) || firstMat.Mat == null)
        return false;

      var job = GetJob(firstMat.Unique);
      if (job == null)
        return false;

      var stop = job.Processes[firstMat.Mat.Process - 1].Paths[0].Stops.FirstOrDefault();
      if (stop != null && !string.IsNullOrEmpty(stop.Program))
      {
        progName = stop.Program;
        rev = stop.ProgramRevision;
        return true;
      }

      return false;
    }

    private bool LookupProgramFromMazakDb(LogEntry entry, out string progName, out long? rev)
    {
      progName = null;
      rev = null;

      var part = mazakData.Parts?.FirstOrDefault(p => p.PartName == entry.FullPartName);
      if (part == null)
        return false;

      var proc = part.Processes?.FirstOrDefault(p => p.ProcessNumber == entry.Process);
      if (proc == null)
        return false;

      if (string.IsNullOrEmpty(proc.MainProgram))
        return false;

      progName = proc.MainProgram;
      rev = null;
      return true;
    }

    private void CheckForInspections(MWI.LogEntry cycle, IEnumerable<LogMaterialAndPath> mats)
    {
      foreach (LogMaterialAndPath mat in mats)
      {
        if (mat.Mat.MaterialID < 0 || mat.Unique == null || mat.Unique == "")
        {
          Log.Debug(
            "HandleMachiningCompleted: Skipping material id "
              + mat.Mat.MaterialID.ToString()
              + " part "
              + mat.PartName
              + " because the job unique string is empty"
          );
          continue;
        }

        var job = GetJob(mat.Unique);
        if (job == null)
        {
          Log.Debug("Couldn't find job for material {uniq}", mat.Unique);
          continue;
        }

        var insps = job.Processes[mat.Mat.Process - 1].Paths[0].Inspections;
        if (insps != null && insps.Count > 0)
        {
          repo.MakeInspectionDecisions(mat.Mat.MaterialID, mat.Mat.Process, insps);
          Log.Debug(
            "Making inspection decision for "
              + string.Join(",", insps.Select(x => x.InspectionType))
              + " material "
              + mat.Mat.MaterialID.ToString()
              + " proc "
              + mat.Mat.Process.ToString()
              + " completed at time "
              + cycle.EndTimeUTC.ToLocalTime().ToString()
              + " on pallet "
              + cycle.Pallet.ToString()
              + " part "
              + mat.PartName
          );
        }
      }
    }

    private IEnumerable<ToolSnapshot> ToolsToSnapshot(int machine, IEnumerable<ToolPocketRow> tools)
    {
      if (tools == null)
        return null;
      return tools
        .Where(t =>
          t.MachineNumber == machine
          && (t.IsToolDataValid ?? false)
          && t.PocketNumber.HasValue
          && !string.IsNullOrEmpty(t.GroupNo)
        )
        .Select(t =>
        {
          var toolName = t.GroupNo;
          if (mazakConfig != null && mazakConfig.ExtractToolName != null)
          {
            toolName = mazakConfig.ExtractToolName(t);
          }
          return new ToolSnapshot()
          {
            ToolName = toolName,
            Pocket = t.PocketNumber.Value,
            CurrentUse = TimeSpan.FromSeconds(t.LifeUsed ?? 0),
            TotalLifeTime = TimeSpan.FromSeconds(t.LifeSpan ?? 0),
            Serial = null,
            CurrentUseCount = null,
            TotalLifeCount = null,
          };
        })
        .ToList();
    }

    #endregion
  }
}
