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
using BlackMaple.MachineWatchInterface;
using MWI = BlackMaple.MachineWatchInterface;
using System.Collections.Generic;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface
{
  public class LogTranslation
  {
    private BlackMaple.MachineFramework.JobDB _jobDB;
    private BlackMaple.MachineFramework.JobLogDB _log;
    private BlackMaple.MachineFramework.FMSSettings _settings;
    private IMachineGroupName _machGroupName;
    private Action<LogEntry> _onMazakLog;
    private MazakSchedulesAndLoadActions _mazakSchedules;
    private Dictionary<string, JobPlan> _jobs;

    private static Serilog.ILogger Log = Serilog.Log.ForContext<LogTranslation>();

    public LogTranslation(BlackMaple.MachineFramework.JobDB jDB,
                          BlackMaple.MachineFramework.JobLogDB logDB,
                          MazakSchedulesAndLoadActions mazakSch,
                          IMachineGroupName machineGroupName,
                          BlackMaple.MachineFramework.FMSSettings settings,
                          Action<LogEntry> onMazakLogMessage)
    {
      _jobDB = jDB;
      _log = logDB;
      _machGroupName = machineGroupName;
      _mazakSchedules = mazakSch;
      _settings = settings;
      _onMazakLog = onMazakLogMessage;
      _jobs = new Dictionary<string, JobPlan>();
    }

    private JobPlan GetJob(string unique)
    {
      if (_jobs.ContainsKey(unique))
        return _jobs[unique];
      else
      {
        var j = _jobDB.LoadJob(unique);
        _jobs.Add(unique, j);
        return j;
      }
    }

    #region Events
    public List<BlackMaple.MachineFramework.MaterialToSendToExternalQueue> HandleEvent(LogEntry e)
    {
      var cycle = new List<MWI.LogEntry>();
      var sendToExternal = new List<BlackMaple.MachineFramework.MaterialToSendToExternalQueue>();
      if (e.Pallet >= 1)
        cycle = _log.CurrentPalletLog(e.Pallet.ToString());

      var jobs = new Dictionary<string, JobPlan>();

      Log.Debug("Handling mazak event {@event}", e);

      switch (e.Code)
      {

        case LogCode.LoadBegin:

          _log.RecordLoadStart(
            mats: CreateMaterialWithoutIDs(e),
            pallet: e.Pallet.ToString(),
            lulNum: e.StationNumber,
            timeUTC: e.TimeUTC,
            foreignId: e.ForeignID);

          break;

        case LogCode.LoadEnd:

          _log.AddPendingLoad(e.Pallet.ToString(), PendingLoadKey(e), e.StationNumber,
                              CalculateElapsed(e, cycle, LogType.LoadUnloadCycle, e.StationNumber),
                              CalculateActiveLoadTime(e),
                              e.ForeignID);
          break;

        case LogCode.MachineCycleStart:

          // There should never be any pending loads since the pallet movement event should have fired.
          // Just in case, we check for pending loads here
          cycle = CheckPendingLoads(e.Pallet, e.TimeUTC.AddSeconds(-1), "", false, cycle);

          _log.RecordMachineStart(
            mats: GetMaterialOnPallet(e, cycle).Select(m => m.Mat),
            pallet: e.Pallet.ToString(),
            statName: _machGroupName.MachineGroupName,
            statNum: e.StationNumber,
            program: e.Program,
            timeUTC: e.TimeUTC,
            pockets: ToolsToSnapshot(e.StationNumber, _mazakSchedules.Tools),
            foreignId: e.ForeignID);

          break;

        case LogCode.MachineCycleEnd:

          var machStart = FindMachineStart(e, cycle, e.StationNumber);
          TimeSpan elapsed;
          IEnumerable<JobLogDB.ToolPocketSnapshot> toolsAtStart;
          if (machStart != null)
          {
            elapsed = e.TimeUTC.Subtract(machStart.EndTimeUTC);
            toolsAtStart = _log.ToolPocketSnapshotForCycle(machStart.Counter);
          }
          else
          {
            Log.Debug("Calculating elapsed time for {@entry} did not find a previous cycle event", e);
            elapsed = TimeSpan.Zero;
            toolsAtStart = Enumerable.Empty<JobLogDB.ToolPocketSnapshot>();
          }
          var toolsAtEnd = ToolsToSnapshot(e.StationNumber, _mazakSchedules.Tools);

          if (elapsed > TimeSpan.FromSeconds(30))
          {
            var machineMats = GetMaterialOnPallet(e, cycle);
            var s = _log.RecordMachineEnd(
              mats: machineMats.Select(m => m.Mat),
              pallet: e.Pallet.ToString(),
              statName: _machGroupName.MachineGroupName,
              statNum: e.StationNumber,
              program: e.Program,
              timeUTC: e.TimeUTC,
              result: "",
              elapsed: elapsed,
              active: CalculateActiveMachining(machineMats),
              tools: JobLogDB.ToolPocketSnapshot.DiffSnapshots(toolsAtStart, toolsAtEnd),
              pockets: toolsAtEnd,
              foreignId: e.ForeignID);
            HandleMachiningCompleted(s, machineMats);
          }
          else
          {
            //TODO: add this with a FAIL result and skip the event in Update Log?
            Log.Warning("Ignoring machine cycle at {time} on pallet {pallet} because it is less than 30 seconds",
              e.TimeUTC, e.Pallet);
          }

          break;

        case LogCode.UnloadBegin:

          _log.RecordUnloadStart(
            mats: GetMaterialOnPallet(e, cycle).Select(m => m.Mat),
            pallet: e.Pallet.ToString(),
            lulNum: e.StationNumber,
            timeUTC: e.TimeUTC,
            foreignId: e.ForeignID);

          break;

        case LogCode.UnloadEnd:

          //TODO: test for rework
          var loadElapsed = CalculateElapsed(e, cycle, LogType.LoadUnloadCycle, e.StationNumber);

          var mats = GetMaterialOnPallet(e, cycle);
          var queues = FindUnloadQueues(mats);
          sendToExternal.AddRange(FindSendToExternalQueue(mats));

          _log.RecordUnloadEnd(
            mats: mats.Select(m => m.Mat),
            pallet: e.Pallet.ToString(),
            lulNum: e.StationNumber,
            timeUTC: e.TimeUTC,
            elapsed: loadElapsed,
            active: CalculateActiveUnloadTime(mats),
            foreignId: e.ForeignID,
            unloadIntoQueues: queues);

          break;

        case LogCode.PalletMoving:

          if (e.FromPosition != null && e.FromPosition.StartsWith("LS"))
          {
            CheckPendingLoads(e.Pallet, e.TimeUTC, e.ForeignID, true, cycle);
          }

          break;
      }

      _onMazakLog(e);

      return sendToExternal;
    }
    #endregion

    #region Material
    private List<JobLogDB.EventLogMaterial> CreateMaterialWithoutIDs(LogEntry e)
    {
      _mazakSchedules.FindSchedule(e.FullPartName, e.Process, out string unique, out int path, out int numProc);

      var ret = new List<JobLogDB.EventLogMaterial>();
      ret.Add(new JobLogDB.EventLogMaterial() { MaterialID = -1, Process = e.Process, Face = "" });
      return ret;
    }

    private struct LogMaterialAndPath
    {
      public JobLogDB.EventLogMaterial Mat { get; set; }
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
        oldEvents: oldEvents);
      _mazakSchedules.FindSchedule(e.FullPartName, e.Process, out string unique, out int path, out int numProc);

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
          ret.Add(new LogMaterialAndPath()
          {
            Mat = byFace[face],
            PartName = e.JobPartName,
            Unique = unique,
            Path = path
          });
        }
        else
        {
          //something went wrong, must create material
          ret.Add(new LogMaterialAndPath()
          {
            Mat = new JobLogDB.EventLogMaterial()
            {
              MaterialID = _log.AllocateMaterialID(unique, e.JobPartName, numProc),
              Process = e.Process,
              Face = face
            },
            PartName = e.JobPartName,
            Unique = unique,
            Path = path
          });

          Log.Warning("When attempting to find material for event {@event} on unique {unique} path {path}, there was no previous cycles with material on face {face}",
            e, unique, path, face);
        }
      }

      foreach (var m in ret)
      {
        _log.RecordPathForProcess(m.Mat.MaterialID, m.Mat.Process, m.Path);
      }

      return ret;
    }

    private SortedList<string, JobLogDB.EventLogMaterial> ParseMaterialFromPreviousEvents(string jobPartName, int proc, int fixQty, bool isUnloadEnd, IList<MWI.LogEntry> oldEvents)
    {
      var byFace = new SortedList<string, JobLogDB.EventLogMaterial>(); //face -> material

      for (int i = oldEvents.Count - 1; i >= 0; i -= 1)
      {
        // When looking for material for an unload event, we want to skip over load events,
        // since an ending load event might have come through with the new material id that is loaded.
        if (isUnloadEnd && oldEvents[i].Result == "LOAD")
          continue;

        foreach (LogMaterial mat in oldEvents[i].Material)
        {
          if (mat.PartName == jobPartName
              && mat.Process == proc
              && mat.MaterialID >= 0
              && !byFace.ContainsKey(mat.Face))
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

            byFace[newFace] =
              new JobLogDB.EventLogMaterial() { MaterialID = mat.MaterialID, Process = proc, Face = newFace };
          }
        }
      }

      return byFace;
    }

    private string PendingLoadKey(LogEntry e)
    {
      return e.FullPartName + "," + e.Process.ToString() + "," + e.FixedQuantity.ToString();
    }

    private List<MWI.LogEntry> CheckPendingLoads(int pallet, DateTime t, string foreignID, bool palletCycle, List<MWI.LogEntry> cycle)
    {
      var pending = _log.PendingLoads(pallet.ToString());

      if (pending.Count == 0)
      {
        if (palletCycle)
        {
          bool hasCompletedUnload = false;
          foreach (var e in cycle)
            if (e.LogType == LogType.LoadUnloadCycle
                && e.StartOfCycle == false
                && e.Result == "UNLOAD")
              hasCompletedUnload = true;
          if (hasCompletedUnload)
            _log.CompletePalletCycle(pallet.ToString(), t, foreignID);
          else
            Log.Debug("Skipping pallet cycle at time {time} because we detected a pallet cycle without unload", t);
        }

        return cycle;
      }

      var mat = new Dictionary<string, IEnumerable<JobLogDB.EventLogMaterial>>();

      foreach (var p in pending)
      {
        Log.Debug("Processing pending load {@pending}", p);
        var s = p.Key.Split(',');
        if (s.Length != 3) continue;

        string fullPartName = s[0];
        string jobPartName = MazakPart.ExtractPartNameFromMazakPartName(fullPartName);

        int proc;
        int fixQty;
        if (!int.TryParse(s[1], out proc)) proc = 1;
        if (!int.TryParse(s[2], out fixQty)) fixQty = 1;

        _mazakSchedules.FindSchedule(fullPartName, proc, out string unique, out int path, out int numProc);

        JobPlan job = GetJob(unique);

        var mats = new List<JobLogDB.EventLogMaterial>();
        if (job != null && !string.IsNullOrEmpty(job.GetInputQueue(proc, path)))
        {
          // search input queue for material
          Log.Debug("Searching queue {queue} for {unique}-{proc} to load",
            job.GetInputQueue(proc, path), unique, proc);

          // TODO: filter paths
          var qs = _log.GetMaterialInQueue(job.GetInputQueue(proc, path)).Where(q => q.Unique == unique).ToList();

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
              mats.Add(new JobLogDB.EventLogMaterial() { MaterialID = qmat.MaterialID, Process = proc, Face = face });
            }
            else
            {
              // not enough material in queue
              Log.Warning("Not enough material in queue {queue} for {part}-{proc}, creating new material for {@pending}",
                job.GetInputQueue(proc, path), fullPartName, proc, p);
              mats.Add(new JobLogDB.EventLogMaterial()
              {
                MaterialID = _log.AllocateMaterialID(unique, jobPartName, numProc),
                Process = proc,
                Face = face
              });
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

            mats.Add(new JobLogDB.EventLogMaterial()
            {
              MaterialID = _log.AllocateMaterialID(unique, jobPartName, numProc),
              Process = proc,
              Face = face
            });
          }

        }
        else
        {
          // search on pallet in the previous process for material
          Log.Debug("Searching on pallet for unique {unique} process {proc} to load into process {proc}", unique, proc - 1, proc);
          var byFace = ParseMaterialFromPreviousEvents(
            jobPartName: jobPartName,
            proc: proc - 1,
            fixQty: fixQty,
            isUnloadEnd: false,
            oldEvents: cycle);
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
              mats.Add(new JobLogDB.EventLogMaterial()
              {
                MaterialID = old.MaterialID,
                Process = proc,
                Face = nextFace
              });
            }
            else
            {
              //something went wrong, must create material
              mats.Add(new JobLogDB.EventLogMaterial()
              {
                MaterialID = _log.AllocateMaterialID(unique, jobPartName, numProc),
                Process = proc,
                Face = nextFace
              });

              Log.Warning("Could not find material on pallet {pallet} for previous process {proc}, creating new material for {@pending}",
                pallet, proc - 1, p);
            }
          }
        }

        mat[p.Key] = mats;
      }

      _log.CompletePalletCycle(pallet.ToString(), t, foreignID, mat, generateSerials: true);

      if (palletCycle)
        return cycle;
      else
        return _log.CurrentPalletLog(pallet.ToString());
    }

    private Dictionary<long, string> FindUnloadQueues(IEnumerable<LogMaterialAndPath> mats)
    {
      var ret = new Dictionary<long, string>();

      foreach (var mat in mats)
      {
        JobPlan job = GetJob(mat.Unique);
        if (job != null)
        {
          var q = job.GetOutputQueue(process: mat.Mat.Process, path: mat.Path);
          if (!string.IsNullOrEmpty(q) && _settings.Queues.ContainsKey(q))
          {
            ret[mat.Mat.MaterialID] = q;
          }
        }
      }

      return ret;
    }

    private IEnumerable<BlackMaple.MachineFramework.MaterialToSendToExternalQueue> FindSendToExternalQueue(IEnumerable<LogMaterialAndPath> mats)
    {
      var ret = new List<BlackMaple.MachineFramework.MaterialToSendToExternalQueue>();

      foreach (var mat in mats)
      {
        JobPlan job = GetJob(mat.Unique);
        if (job != null)
        {
          var q = job.GetOutputQueue(process: mat.Mat.Process, path: mat.Path);
          if (!string.IsNullOrEmpty(q) && _settings.ExternalQueues.ContainsKey(q))
          {
            ret.Add(new BlackMaple.MachineFramework.MaterialToSendToExternalQueue()
            {
              Server = _settings.ExternalQueues[q],
              PartName = mat.PartName,
              Queue = q,
              Serial = _log.GetMaterialDetails(mat.Mat.MaterialID)?.Serial
            });
          }
        }
      }

      return ret;
    }
    #endregion

    #region Elapsed
    private static MWI.LogEntry FindMachineStart(LogEntry e, IList<MWI.LogEntry> oldEvents, int statNum)
    {
      return oldEvents.LastOrDefault(old => old.LogType == LogType.MachineCycle && old.StartOfCycle && old.LocationNum == statNum);
    }
    private static TimeSpan CalculateElapsed(LogEntry e, IList<MWI.LogEntry> oldEvents, LogType ty, int statNum)
    {
      for (int i = oldEvents.Count - 1; i >= 0; i -= 1)
      {
        if (oldEvents[i].LogType == ty && oldEvents[i].LocationNum == statNum)
        {
          var ev = oldEvents[i];

          switch (e.Code)
          {
            case LogCode.LoadEnd:
              if (ev.StartOfCycle == true && ev.Result == "LOAD")
                return e.TimeUTC.Subtract(ev.EndTimeUTC);
              break;

            case LogCode.MachineCycleEnd:
              if (ev.StartOfCycle)
                return e.TimeUTC.Subtract(ev.EndTimeUTC);
              break;

            case LogCode.UnloadEnd:
              if (ev.StartOfCycle == true && ev.Result == "UNLOAD")
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
      var procs = mats
        .Select(m => new { Unique = m.Unique, Process = m.Mat.Process })
        .Distinct();
      foreach (var proc in procs)
      {
        var job = GetJob(proc.Unique);
        if (job == null) continue;
        var stop = job.GetMachiningStop(proc.Process, 1).FirstOrDefault();
        if (stop == null) continue;
        total += stop.ExpectedCycleTime;
      }

      return total;
    }

    private TimeSpan CalculateActiveLoadTime(LogEntry e)
    {
      _mazakSchedules.FindSchedule(e.FullPartName, e.Process, out string unique, out int path, out int numProc);
      var job = GetJob(unique);
      if (job == null) return TimeSpan.Zero;
      return TimeSpan.FromTicks(job.GetExpectedLoadTime(e.Process, path).Ticks * e.FixedQuantity);
    }

    private TimeSpan CalculateActiveUnloadTime(IEnumerable<LogMaterialAndPath> mats)
    {
      var ticks = mats
        .Select(m => GetJob(m.Unique)?.GetExpectedUnloadTime(m.Mat.Process, m.Path) ?? TimeSpan.Zero)
        .Select(t => t.Ticks)
        .Sum();
      return TimeSpan.FromTicks(ticks);
    }
    #endregion

    #region Inspections
    private void HandleMachiningCompleted(BlackMaple.MachineWatchInterface.LogEntry cycle, IEnumerable<LogMaterialAndPath> mats)
    {
      foreach (LogMaterialAndPath mat in mats)
      {
        if (mat.Mat.MaterialID < 0 || mat.Unique == null || mat.Unique == "")
        {
          Log.Debug("HandleMachiningCompleted: Skipping material id " + mat.Mat.MaterialID.ToString() +
                    " part " + mat.PartName +
                    " because the job unique string is empty");
          continue;
        }

        var job = GetJob(mat.Unique);
        if (job == null)
        {
          Log.Debug("Couldn't find job for material {uniq}", mat.Unique);
          continue;
        }

        _log.MakeInspectionDecisions(mat.Mat.MaterialID, mat.Mat.Process, job.PathInspections(mat.Mat.Process, mat.Path));
        Log.Debug("Making inspection decision for " + string.Join(",", job.PathInspections(mat.Mat.Process, mat.Path).Select(x => x.InspectionType)) + " material " + mat.Mat.MaterialID.ToString() +
                  " proc " + mat.Mat.Process.ToString() + " path " + mat.Path.ToString() + " completed at time " + cycle.EndTimeUTC.ToLocalTime().ToString() + " on pallet " + cycle.Pallet.ToString() +
                  " part " + mat.PartName);
      }
    }
    #endregion

    #region Tools
    private static IEnumerable<JobLogDB.ToolPocketSnapshot> ToolsToSnapshot(int machine, IEnumerable<ToolPocketRow> tools)
    {
      if (tools == null) return null;
      return tools
        .Where(t => t.MachineNumber == machine && (t.IsToolDataValid ?? false) && t.PocketNumber.HasValue && !string.IsNullOrEmpty(t.GroupNo))
        .Select(t => new JobLogDB.ToolPocketSnapshot()
        {
          PocketNumber = t.PocketNumber ?? -1,
          Tool = t.GroupNo,
          CurrentUse = TimeSpan.FromSeconds(t.LifeUsed ?? 0),
          ToolLife = TimeSpan.FromSeconds(t.LifeSpan ?? 0)
        })
        .ToList();
    }

    #endregion
  }
}

