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
using MWI = BlackMaple.MachineFramework;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface
{
  public interface ILogTranslation
  {
    public record HandleEventResult
    {
      public IEnumerable<BlackMaple.MachineFramework.MaterialToSendToExternalQueue> MatsToSendToExternal { get; init; }
      public bool StoppedBecauseRecentMachineEnd { get; init; }
    }

    HandleEventResult HandleEvent(LogEntry e);
    bool CheckPalletStatusMatchesLogs(DateTime? timeUTC = null);
  }

  public class LogTranslation : ILogTranslation
  {
    private MazakConfig _mazakConfig;
    private BlackMaple.MachineFramework.IRepository _log;
    private BlackMaple.MachineFramework.FMSSettings _settings;
    private IMachineGroupName _machGroupName;
    private Action<LogEntry> _onMazakLog;
    private MazakCurrentStatusAndTools _mazakSchedules;
    private Dictionary<string, Job> _jobs;

    private static Serilog.ILogger Log = Serilog.Log.ForContext<LogTranslation>();

    public LogTranslation(
      BlackMaple.MachineFramework.IRepository logDB,
      MazakCurrentStatusAndTools mazakSch,
      IMachineGroupName machineGroupName,
      BlackMaple.MachineFramework.FMSSettings settings,
      Action<LogEntry> onMazakLogMessage,
      MazakConfig mazakConfig
    )
    {
      _log = logDB;
      _machGroupName = machineGroupName;
      _mazakConfig = mazakConfig;
      _mazakSchedules = mazakSch;
      _settings = settings;
      _onMazakLog = onMazakLogMessage;
      _jobs = new Dictionary<string, Job>();
    }

    private Job GetJob(string unique)
    {
      if (_jobs.ContainsKey(unique))
        return _jobs[unique];
      else
      {
        var j = _log.LoadJob(unique);
        _jobs.Add(unique, j);
        return j;
      }
    }

    #region Events
    public ILogTranslation.HandleEventResult HandleEvent(LogEntry e)
    {
      var cycle = new List<MWI.LogEntry>();
      IEnumerable<BlackMaple.MachineFramework.MaterialToSendToExternalQueue> sendToExternal = null;
      if (e.Pallet >= 1)
        cycle = _log.CurrentPalletLog(e.Pallet.ToString());

      Log.Debug("Handling mazak event {@event}", e);

      switch (e.Code)
      {
        case LogCode.LoadBegin:

          _log.RecordLoadStart(
            mats: CreateMaterialWithoutIDs(e),
            pallet: e.Pallet.ToString(),
            lulNum: e.StationNumber,
            timeUTC: e.TimeUTC,
            foreignId: e.ForeignID
          );

          break;

        case LogCode.LoadEnd:

          _log.AddPendingLoad(
            e.Pallet.ToString(),
            PendingLoadKey(e),
            e.StationNumber,
            CalculateElapsed(e, cycle, LogType.LoadUnloadCycle, e.StationNumber),
            CalculateActiveLoadTime(e),
            e.ForeignID
          );
          break;

        case LogCode.MachineCycleStart:
        {
          // There should never be any pending loads since the pallet movement event should have fired.
          // Just in case, we check for pending loads here
          cycle = CheckPendingLoads(e.Pallet, e.TimeUTC.AddSeconds(-1), "", false, cycle);
          IEnumerable<ToolSnapshot> pockets = null;
          if ((DateTime.UtcNow - e.TimeUTC).Duration().TotalMinutes < 5)
          {
            pockets = ToolsToSnapshot(e.StationNumber, _mazakSchedules.Tools);
          }

          var machineMats = GetMaterialOnPallet(e, cycle);
          LookupProgram(machineMats, e, out var progName, out var progRev);
          _log.RecordMachineStart(
            mats: machineMats.Select(m => m.Mat),
            pallet: e.Pallet.ToString(),
            statName: _machGroupName.MachineGroupName,
            statNum: e.StationNumber,
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

          // Tool snapshots take ~5 seconds from the end of the cycle until the updated tools are available in open database kit,
          // so stop processing log entries if the machine cycle end occurred 15 seconds in the past.
          var timeSinceEnd = DateTime.UtcNow.Subtract(e.TimeUTC);
          if (TimeSpan.FromSeconds(-15) <= timeSinceEnd && timeSinceEnd <= TimeSpan.FromSeconds(15))
          {
            return new ILogTranslation.HandleEventResult()
            {
              MatsToSendToExternal = Enumerable.Empty<MaterialToSendToExternalQueue>(),
              StoppedBecauseRecentMachineEnd = true
            };
          }

          var machStart = FindMachineStart(e, cycle, e.StationNumber);
          TimeSpan elapsed;
          IEnumerable<ToolSnapshot> toolsAtStart;
          if (machStart != null)
          {
            elapsed = e.TimeUTC.Subtract(machStart.EndTimeUTC);
            toolsAtStart = _log.ToolPocketSnapshotForCycle(machStart.Counter);
          }
          else
          {
            Log.Debug("Calculating elapsed time for {@entry} did not find a previous cycle event", e);
            elapsed = TimeSpan.Zero;
            toolsAtStart = Enumerable.Empty<ToolSnapshot>();
          }
          var toolsAtEnd = ToolsToSnapshot(e.StationNumber, _mazakSchedules.Tools);

          if (elapsed > TimeSpan.FromSeconds(30))
          {
            var machineMats = GetMaterialOnPallet(e, cycle);
            LookupProgram(machineMats, e, out var progName, out var progRev);
            var s = _log.RecordMachineEnd(
              mats: machineMats.Select(m => m.Mat),
              pallet: e.Pallet.ToString(),
              statName: _machGroupName.MachineGroupName,
              statNum: e.StationNumber,
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
            HandleMachiningCompleted(s, machineMats);
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

        case LogCode.UnloadBegin:

          _log.RecordUnloadStart(
            mats: GetMaterialOnPallet(e, cycle).Select(m => m.Mat),
            pallet: e.Pallet.ToString(),
            lulNum: e.StationNumber,
            timeUTC: e.TimeUTC,
            foreignId: e.ForeignID
          );

          break;

        case LogCode.UnloadEnd:

          //TODO: test for rework
          var loadElapsed = CalculateElapsed(e, cycle, LogType.LoadUnloadCycle, e.StationNumber);

          var mats = GetMaterialOnPallet(e, cycle);
          var queues = FindUnloadQueues(mats, cycle);
          sendToExternal = FindSendToExternalQueue(mats);

          _log.RecordUnloadEnd(
            mats: mats.Select(m => m.Mat),
            pallet: e.Pallet.ToString(),
            lulNum: e.StationNumber,
            timeUTC: e.TimeUTC,
            elapsed: loadElapsed,
            active: CalculateActiveUnloadTime(mats),
            foreignId: e.ForeignID,
            unloadIntoQueues: queues
          );

          break;

        case LogCode.StartRotatePalletIntoMachine:
          _log.RecordPalletDepartRotaryInbound(
            mats: GetAllMaterialOnPallet(cycle).Select(EventLogMaterial.FromLogMat),
            pallet: e.Pallet.ToString(),
            statName: _machGroupName.MachineGroupName,
            statNum: e.StationNumber,
            timeUTC: e.TimeUTC,
            rotateIntoWorktable: true,
            elapsed: CalculateElapsed(e, cycle, LogType.PalletOnRotaryInbound, e.StationNumber),
            foreignId: e.ForeignID
          );
          break;

        case LogCode.PalletMoving:
          if (e.FromPosition != null && e.FromPosition.StartsWith("LS"))
          {
            cycle = CheckPendingLoads(e.Pallet, e.TimeUTC, e.ForeignID, true, cycle);
          }
          // Mxx1 is inbound at machine xx, Mxx2 is table at machine xx
          else if (
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
              _log.RecordPalletDepartRotaryInbound(
                mats: GetAllMaterialOnPallet(cycle).Select(EventLogMaterial.FromLogMat),
                pallet: e.Pallet.ToString(),
                statName: _machGroupName.MachineGroupName,
                statNum: mcNum,
                rotateIntoWorktable: false,
                timeUTC: e.TimeUTC,
                elapsed: CalculateElapsed(e, cycle, LogType.PalletOnRotaryInbound, mcNum),
                foreignId: e.ForeignID
              );
            }
          }
          else if (e.FromPosition != null && e.FromPosition.StartsWith("S") && e.FromPosition != "STA")
          {
            if (int.TryParse(e.FromPosition.Substring(1), out var stockerNum))
            {
              _log.RecordPalletDepartStocker(
                mats: GetAllMaterialOnPallet(cycle).Select(EventLogMaterial.FromLogMat),
                pallet: e.Pallet.ToString(),
                stockerNum: stockerNum,
                timeUTC: e.TimeUTC,
                waitForMachine: !cycle.Any(c => c.LogType == LogType.MachineCycle),
                elapsed: CalculateElapsed(e, cycle, LogType.PalletInStocker, stockerNum),
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
              _log.RecordPalletArriveRotaryInbound(
                mats: GetAllMaterialOnPallet(cycle).Select(EventLogMaterial.FromLogMat),
                pallet: e.Pallet.ToString(),
                statName: _machGroupName.MachineGroupName,
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
              _log.RecordPalletArriveStocker(
                mats: GetAllMaterialOnPallet(cycle).Select(EventLogMaterial.FromLogMat),
                pallet: e.Pallet.ToString(),
                stockerNum: stockerNum,
                waitForMachine: !cycle.Any(c => c.LogType == LogType.MachineCycle),
                timeUTC: e.TimeUTC,
                foreignId: e.ForeignID
              );
            }
          }
          break;
      }

      _onMazakLog(e);

      return new ILogTranslation.HandleEventResult()
      {
        MatsToSendToExternal = sendToExternal ?? Enumerable.Empty<MaterialToSendToExternalQueue>(),
        StoppedBecauseRecentMachineEnd = false
      };
    }
    #endregion

    #region Material
    private List<EventLogMaterial> CreateMaterialWithoutIDs(LogEntry e)
    {
      var ret = new List<EventLogMaterial>();
      ret.Add(
        new EventLogMaterial()
        {
          MaterialID = -1,
          Process = e.Process,
          Face = ""
        }
      );
      return ret;
    }

    private List<MWI.LogMaterial> GetAllMaterialOnPallet(IList<MWI.LogEntry> oldEvents)
    {
      return oldEvents
        .Where(e => e.LogType == LogType.LoadUnloadCycle && !e.StartOfCycle && e.Result == "LOAD")
        .SelectMany(e => e.Material)
        .Where(m => !_log.IsMaterialInQueue(m.MaterialID))
        .GroupBy(m => m.MaterialID)
        .Select(ms => ms.First())
        .ToList();
    }

    private struct LogMaterialAndPath
    {
      public EventLogMaterial Mat { get; set; }
      public string Unique { get; set; }
      public string PartName { get; set; }
      public int Path { get; set; }
    }

    private List<LogMaterialAndPath> GetMaterialOnPallet(LogEntry e, IList<MWI.LogEntry> oldEvents)
    {
      var byFace = ParseMaterialFromPreviousEvents(
        jobPartName: e.JobPartName,
        proc: e.Process,
        fixQty: e.FixedQuantity,
        isUnloadEnd: e.Code == LogCode.UnloadEnd,
        oldEvents: oldEvents
      );
      _mazakSchedules.FindSchedule(
        e.FullPartName,
        e.Process,
        out string unique,
        out int path,
        out int numProc
      );

      if (GetJob(unique) == null)
      {
        unique = "";
        path = 1;
      }

      var ret = new List<LogMaterialAndPath>();

      for (int i = 1; i <= e.FixedQuantity; i += 1)
      {
        string face;
        if (e.FixedQuantity == 1)
          face = e.Process.ToString();
        else
          face = e.Process.ToString() + "-" + i.ToString();

        if (byFace.ContainsKey(face))
        {
          ret.Add(
            new LogMaterialAndPath()
            {
              Mat = byFace[face],
              PartName = e.JobPartName,
              Unique = unique,
              Path = path
            }
          );
        }
        else
        {
          //something went wrong, must create material
          ret.Add(
            new LogMaterialAndPath()
            {
              Mat = new EventLogMaterial()
              {
                MaterialID = _log.AllocateMaterialID(unique, e.JobPartName, numProc),
                Process = e.Process,
                Face = face
              },
              PartName = e.JobPartName,
              Unique = unique,
              Path = path
            }
          );

          Log.Warning(
            "When attempting to find material for event {@event} on unique {unique} path {path}, there was no previous cycles with material on face {face}",
            e,
            unique,
            path,
            face
          );
        }
      }

      foreach (var m in ret)
      {
        _log.RecordPathForProcess(m.Mat.MaterialID, m.Mat.Process, m.Path);
      }

      return ret;
    }

    private SortedList<string, EventLogMaterial> ParseMaterialFromPreviousEvents(
      string jobPartName,
      int proc,
      int fixQty,
      bool isUnloadEnd,
      IList<MWI.LogEntry> oldEvents
    )
    {
      var byFace = new SortedList<string, EventLogMaterial>(); //face -> material

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
        if (oldEvents[i].Material.Any(m => _log.IsMaterialInQueue(m.MaterialID)))
        {
          continue;
        }

        foreach (LogMaterial mat in oldEvents[i].Material)
        {
          if (
            mat.PartName == jobPartName
            && mat.Process == proc
            && mat.MaterialID >= 0
            && !byFace.ContainsKey(mat.Face)
          )
          {
            string newFace;
            if (fixQty == 1)
              newFace = proc.ToString();
            else
            {
              int idx = mat.Face.IndexOf('-');
              if (idx >= 0 && idx < mat.Face.Length)
                newFace = proc.ToString() + mat.Face.Substring(idx);
              else
                newFace = proc.ToString();
            }

            byFace[newFace] = new EventLogMaterial()
            {
              MaterialID = mat.MaterialID,
              Process = proc,
              Face = newFace
            };
          }
        }
      }

      return byFace;
    }

    private string PendingLoadKey(LogEntry e)
    {
      return e.FullPartName + "," + e.Process.ToString() + "," + e.FixedQuantity.ToString();
    }

    private List<MWI.LogEntry> CheckPendingLoads(
      int pallet,
      DateTime t,
      string foreignID,
      bool palletCycle,
      List<MWI.LogEntry> cycle
    )
    {
      var pending = _log.PendingLoads(pallet.ToString());

      if (pending.Count == 0)
      {
        if (palletCycle)
        {
          bool hasCompletedUnload = false;
          foreach (var e in cycle)
            if (e.LogType == LogType.LoadUnloadCycle && e.StartOfCycle == false && e.Result == "UNLOAD")
              hasCompletedUnload = true;
          if (hasCompletedUnload)
            _log.CompletePalletCycle(pallet.ToString(), t, foreignID);
          else
            Log.Debug(
              "Skipping pallet cycle at time {time} because we detected a pallet cycle without unload",
              t
            );
        }

        return cycle;
      }

      var mat = new Dictionary<string, IEnumerable<EventLogMaterial>>();

      foreach (var p in pending)
      {
        try
        {
          Log.Debug("Processing pending load {@pending}", p);
          var s = p.Key.Split(',');
          if (s.Length != 3)
            continue;

          string fullPartName = s[0];
          string jobPartName = MazakPart.ExtractPartNameFromMazakPartName(fullPartName);

          int proc;
          int fixQty;
          if (!int.TryParse(s[1], out proc))
            proc = 1;
          if (!int.TryParse(s[2], out fixQty))
            fixQty = 1;

          _mazakSchedules.FindSchedule(fullPartName, proc, out string unique, out int path, out int numProc);

          Log.Debug("Found job {unique} with path {path} and {numProc}", unique, path, numProc);

          Job job = string.IsNullOrEmpty(unique) ? null : GetJob(unique);
          if (job == null)
          {
            Log.Warning("Unable to find job for pending load {@pending} with unique {@uniq}", p, unique);
            unique = "";
            path = 1;
          }

          var mats = new List<EventLogMaterial>();
          if (job != null && !string.IsNullOrEmpty(job.Processes[proc - 1].Paths[path - 1].InputQueue))
          {
            var info = job.Processes[proc - 1].Paths[path - 1];
            // search input queue for material
            Log.Debug("Searching queue {queue} for {unique}-{proc} to load", info.InputQueue, unique, proc);

            var qs = MazakQueues.QueuedMaterialForLoading(
              job.UniqueStr,
              _log.GetMaterialInQueueByUnique(info.InputQueue, job.UniqueStr),
              proc,
              path,
              _log
            );

            for (int i = 1; i <= fixQty; i++)
            {
              string face;
              if (fixQty == 1)
              {
                face = proc.ToString();
              }
              else
              {
                face = proc.ToString() + "-" + i.ToString();
              }
              if (i <= qs.Count)
              {
                var qmat = qs[i - 1];
                mats.Add(
                  new EventLogMaterial()
                  {
                    MaterialID = qmat.MaterialID,
                    Process = proc,
                    Face = face
                  }
                );
              }
              else
              {
                // not enough material in queue
                Log.Warning(
                  "Not enough material in queue {queue} for {part}-{proc}, creating new material for {@pending}",
                  info.InputQueue,
                  fullPartName,
                  proc,
                  p
                );
                mats.Add(
                  new EventLogMaterial()
                  {
                    MaterialID = _log.AllocateMaterialID(unique, jobPartName, numProc),
                    Process = proc,
                    Face = face
                  }
                );
              }
            }
          }
          else if (proc == 1)
          {
            // create new material
            Log.Debug("Creating new material for unique {unique} process 1", unique);
            for (int i = 1; i <= fixQty; i += 1)
            {
              string face;
              if (fixQty == 1)
                face = proc.ToString();
              else
                face = proc.ToString() + "-" + i.ToString();

              mats.Add(
                new EventLogMaterial()
                {
                  MaterialID = _log.AllocateMaterialID(unique, jobPartName, numProc),
                  Process = proc,
                  Face = face
                }
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
            var byFace = ParseMaterialFromPreviousEvents(
              jobPartName: jobPartName,
              proc: proc - 1,
              fixQty: fixQty,
              isUnloadEnd: false,
              oldEvents: cycle
            );
            for (int i = 1; i <= fixQty; i += 1)
            {
              string prevFace;
              string nextFace;
              if (fixQty == 1)
              {
                prevFace = (proc - 1).ToString();
                nextFace = proc.ToString();
              }
              else
              {
                prevFace = (proc - 1).ToString() + "-" + i.ToString();
                nextFace = proc.ToString() + "-" + i.ToString();
              }

              if (byFace.ContainsKey(prevFace))
              {
                var old = byFace[prevFace];
                mats.Add(
                  new EventLogMaterial()
                  {
                    MaterialID = old.MaterialID,
                    Process = proc,
                    Face = nextFace
                  }
                );
              }
              else
              {
                //something went wrong, must create material
                mats.Add(
                  new EventLogMaterial()
                  {
                    MaterialID = _log.AllocateMaterialID(unique, jobPartName, numProc),
                    Process = proc,
                    Face = nextFace
                  }
                );

                Log.Warning(
                  "Could not find material on pallet {pallet} for previous process {proc}, creating new material for {@pending}",
                  pallet,
                  proc - 1,
                  p
                );
              }
            }
          }

          mat[p.Key] = mats;
        }
        catch (Exception ex)
        {
          Log.Error(ex, "Error processing pending load {@pending}", p);
          _log.CancelPendingLoads(p.ForeignID);
        }
      }

      _log.CompletePalletCycle(pallet.ToString(), t, foreignID, mat, generateSerials: true);

      if (palletCycle)
        return cycle;
      else
        return _log.CurrentPalletLog(pallet.ToString());
    }

    private Dictionary<long, string> FindUnloadQueues(
      IEnumerable<LogMaterialAndPath> mats,
      IEnumerable<MWI.LogEntry> cycle
    )
    {
      var ret = new Dictionary<long, string>();

      foreach (var mat in mats)
      {
        var signalQuarantine = cycle.LastOrDefault(
          e =>
            e.LogType == LogType.SignalQuarantine && e.Material.Any(m => m.MaterialID == mat.Mat.MaterialID)
        );

        if (signalQuarantine != null)
        {
          ret[mat.Mat.MaterialID] = signalQuarantine.LocationName ?? _settings.QuarantineQueue;
        }
        else
        {
          Job job = GetJob(mat.Unique);

          if (job != null)
          {
            var q = job.Processes[mat.Mat.Process - 1].Paths[mat.Path - 1].OutputQueue;
            if (!string.IsNullOrEmpty(q) && _settings.Queues.ContainsKey(q))
            {
              ret[mat.Mat.MaterialID] = q;
            }
          }
        }
      }

      return ret;
    }

    private IEnumerable<BlackMaple.MachineFramework.MaterialToSendToExternalQueue> FindSendToExternalQueue(
      IEnumerable<LogMaterialAndPath> mats
    )
    {
      var ret = new List<BlackMaple.MachineFramework.MaterialToSendToExternalQueue>();

      foreach (var mat in mats)
      {
        Job job = GetJob(mat.Unique);
        if (job != null)
        {
          var q = job.Processes[mat.Mat.Process - 1].Paths[mat.Path - 1].OutputQueue;
          if (!string.IsNullOrEmpty(q) && _settings.ExternalQueues.ContainsKey(q))
          {
            ret.Add(
              new BlackMaple.MachineFramework.MaterialToSendToExternalQueue()
              {
                Server = _settings.ExternalQueues[q],
                PartName = mat.PartName,
                Queue = q,
                Serial = _log.GetMaterialDetails(mat.Mat.MaterialID)?.Serial
              }
            );
          }
        }
      }

      return ret;
    }
    #endregion

    #region Compare Status With Events
    public bool CheckPalletStatusMatchesLogs(DateTime? timeUTC = null)
    {
      if (string.IsNullOrEmpty(_settings.QuarantineQueue))
        return false;

      bool matMovedToQueue = false;
      foreach (var pal in _mazakSchedules.PalletPositions.Where(p => !p.PalletPosition.StartsWith("LS")))
      {
        var oldEvts = _log.CurrentPalletLog(pal.PalletNumber.ToString());

        // start with everything on the pallet
        List<LogMaterial> matsOnPal = GetAllMaterialOnPallet(oldEvts);

        foreach (var st in _mazakSchedules.PalletSubStatuses.Where(s => s.PalletNumber == pal.PalletNumber))
        {
          // remove material from matsOnPal that matches this PalletSubStatus
          var sch = _mazakSchedules.Schedules.FirstOrDefault(s => s.Id == st.ScheduleID);
          if (sch == null)
            continue;
          MazakPart.ParseComment(sch.Comment, out string unique, out var procToPath, out bool manual);
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
          _log.RecordAddMaterialToQueue(
            mat: new EventLogMaterial()
            {
              MaterialID = extraMat.MaterialID,
              Process = extraMat.Process,
              Face = ""
            },
            queue: _settings.QuarantineQueue,
            position: -1,
            operatorName: null,
            reason: "MaterialMissingOnPallet",
            timeUTC: timeUTC
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
      return oldEvents.LastOrDefault(
        old => old.LogType == LogType.MachineCycle && old.StartOfCycle && old.LocationNum == statNum
      );
    }

    private static TimeSpan CalculateElapsed(
      LogEntry e,
      IList<MWI.LogEntry> oldEvents,
      LogType ty,
      int statNum
    )
    {
      for (int i = oldEvents.Count - 1; i >= 0; i -= 1)
      {
        if (oldEvents[i].LogType == ty && oldEvents[i].LocationNum == statNum)
        {
          var ev = oldEvents[i];

          switch (e.Code)
          {
            case LogCode.LoadEnd:
              if (ev.StartOfCycle && ev.Result == "LOAD")
                return e.TimeUTC.Subtract(ev.EndTimeUTC);
              break;

            case LogCode.UnloadEnd:
              if (ev.StartOfCycle && ev.Result == "UNLOAD")
                return e.TimeUTC.Subtract(ev.EndTimeUTC);
              break;

            default:
              if (ev.StartOfCycle)
                return e.TimeUTC.Subtract(ev.EndTimeUTC);
              break;
          }
        }
      }

      Log.Debug("Calculating elapsed time for {@entry} did not find a previous cycle event", e);

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

    private TimeSpan CalculateActiveLoadTime(LogEntry e)
    {
      _mazakSchedules.FindSchedule(
        e.FullPartName,
        e.Process,
        out string unique,
        out int path,
        out int numProc
      );
      var job = GetJob(unique);
      if (job == null)
        return TimeSpan.Zero;
      return TimeSpan.FromTicks(
        job.Processes[e.Process - 1].Paths[path - 1].ExpectedLoadTime.Ticks * e.FixedQuantity
      );
    }

    private TimeSpan CalculateActiveUnloadTime(IEnumerable<LogMaterialAndPath> mats)
    {
      var ticks = mats.Select(m =>
        {
          var job = GetJob(m.Unique);
          if (job == null)
            return TimeSpan.Zero;
          return job.Processes[m.Mat.Process - 1].Paths[m.Path - 1].ExpectedUnloadTime;
        })
        .Select(t => t.Ticks)
        .Sum();
      return TimeSpan.FromTicks(ticks);
    }

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

      // try and find path
      int path = 1;

      var part = _mazakSchedules.Parts?.FirstOrDefault(p => p.PartName == e.FullPartName);
      if (part != null && MazakPart.IsSailPart(part.PartName, part.Comment))
      {
        MazakPart.ParseComment(part.Comment, out string uniq, out var procToPath, out bool manual);
        if (uniq == firstMat.Unique)
        {
          path = procToPath.PathForProc(firstMat.Mat.Process);
        }
      }

      var stop = job.Processes[firstMat.Mat.Process - 1].Paths[path - 1].Stops.FirstOrDefault();
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

      var part = _mazakSchedules.Parts?.FirstOrDefault(p => p.PartName == entry.FullPartName);
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
    #endregion

    #region Inspections
    private void HandleMachiningCompleted(MWI.LogEntry cycle, IEnumerable<LogMaterialAndPath> mats)
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

        var insps = job.Processes[mat.Mat.Process - 1].Paths[mat.Path - 1].Inspections;
        if (insps != null && insps.Count > 0)
        {
          _log.MakeInspectionDecisions(mat.Mat.MaterialID, mat.Mat.Process, insps);
          Log.Debug(
            "Making inspection decision for "
              + string.Join(",", insps.Select(x => x.InspectionType))
              + " material "
              + mat.Mat.MaterialID.ToString()
              + " proc "
              + mat.Mat.Process.ToString()
              + " path "
              + mat.Path.ToString()
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
    #endregion

    #region Tools
    private IEnumerable<ToolSnapshot> ToolsToSnapshot(int machine, IEnumerable<ToolPocketRow> tools)
    {
      if (tools == null)
        return null;
      return tools
        .Where(
          t =>
            t.MachineNumber == machine
            && (t.IsToolDataValid ?? false)
            && t.PocketNumber.HasValue
            && !string.IsNullOrEmpty(t.GroupNo)
        )
        .Select(t =>
        {
          var toolName = t.GroupNo;
          if (_mazakConfig != null && _mazakConfig.ExtractToolName != null)
          {
            toolName = _mazakConfig.ExtractToolName(t);
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
