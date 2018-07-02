/* Copyright (c) 2018, John Lenz

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
using BlackMaple.MachineWatchInterface;
using MWI = BlackMaple.MachineWatchInterface;
using System.Collections.Generic;

namespace MazakMachineInterface
{
  public class LogTranslation
  {
    private BlackMaple.MachineFramework.JobLogDB _log;
    private ILogData _loadLogData;
    private System.Diagnostics.TraceSource trace;
    private IReadDataAccess _readDB;

    private object _lock;
    private System.Timers.Timer _timer;
    public BlackMaple.MachineFramework.FMSSettings FMSSettings { get; set; }

    public LogTranslation(BlackMaple.MachineFramework.JobLogDB log, IReadDataAccess readDB,
                              BlackMaple.MachineFramework.FMSSettings settings,
                          ILogData loadLogData, System.Diagnostics.TraceSource t)
    {
      _log = log;
      _loadLogData = loadLogData;
      _readDB = readDB;
      FMSSettings = settings;
      trace = t;
      _lock = new object();
      _timer = new System.Timers.Timer(TimeSpan.FromMinutes(1).TotalMilliseconds);
      _timer.Elapsed += HandleElapsed;
      _timer.Start();
    }

    public void Halt()
    {
      _timer.Stop();
    }

    public delegate void MachiningCompletedDel(MWI.LogEntry cycle, ReadOnlyDataSet dset);
    public event MachiningCompletedDel MachiningCompleted;
    public delegate void PalletMoveDel(int pallet, string fromStation, string toStation);
    public event PalletMoveDel PalletMove;
    public delegate void NewEntriesDel(ReadOnlyDataSet dset);
    public event NewEntriesDel NewEntries;

    #region Events
    /* This is public instead of private for testing ONLY */
    public void HandleElapsed(object sender, System.Timers.ElapsedEventArgs e)
    {
      lock (_lock)
      {
        try
        {
          var dset = _readDB.LoadReadOnly();

          var logs = _loadLogData.LoadLog(_log.MaxForeignID());
          foreach (var ev in logs)
          {
            try
            {
              HandleEvent(ev, dset);
            }
            catch (Exception ex)
            {
              trace.TraceEvent(System.Diagnostics.TraceEventType.Error, 0,
                               "Error translating log event at time " + ev.TimeUTC.ToLocalTime().ToString() + Environment.NewLine
                               + ex.ToString());
            }
          }

          _loadLogData.DeleteLog(_log.MaxForeignID(), trace);

          if (logs.Count > 0) {
            NewEntries?.Invoke(dset);
          }

        }
        catch (Exception ex)
        {
          trace.TraceEvent(System.Diagnostics.TraceEventType.Error, 0,
                           "Unhandled error processing log" + Environment.NewLine + ex.ToString());
        }
      }
    }

    /* This is public instead of private for testing ONLY */
    public void HandleEvent(LogEntry e, ReadOnlyDataSet dset)
    {
      var cycle = new List<MWI.LogEntry>();
      if (e.Pallet >= 1)
        cycle = _log.CurrentPalletLog(e.Pallet.ToString());

      switch (e.Code)
      {

        case LogCode.LoadBegin:

          _log.RecordLoadStart(
            mats: CreateMaterialWithoutIDs(e, dset),
            pallet: e.Pallet.ToString(),
            lulNum: e.StationNumber,
            timeUTC: e.TimeUTC,
            foreignId: e.ForeignID);

          break;

        case LogCode.LoadEnd:

          _log.AddPendingLoad(e.Pallet.ToString(), PendingLoadKey(e), e.StationNumber,
                              CalculateElapsed(e, cycle, LogType.LoadUnloadCycle, e.StationNumber),
                              TimeSpan.Zero, //Mazak doesn't give actual operation time for loads
                              e.ForeignID);
          break;

        case LogCode.MachineCycleStart:

          // There should never be any pending loads since the pallet movement event should have fired.
          // Just in case, we check for pending loads here
          cycle = CheckPendingLoads(e.Pallet, e.TimeUTC.AddSeconds(-1), "", dset, false, cycle);

          _log.RecordMachineStart(
            mats: FindMaterial(e, dset, cycle),
            pallet: e.Pallet.ToString(),
            statName: "MC",
            statNum: e.StationNumber,
            program: e.Program,
            timeUTC: e.TimeUTC,
            foreignId: e.ForeignID);

          break;

        case LogCode.MachineCycleEnd:

          var elapsed = CalculateElapsed(e, cycle, LogType.MachineCycle, e.StationNumber);

          if (elapsed > TimeSpan.FromSeconds(30))
          {
            var s = _log.RecordMachineEnd(
              mats: FindMaterial(e, dset, cycle),
              pallet: e.Pallet.ToString(),
              statName: "MC",
              statNum: e.StationNumber,
              program: e.Program,
              timeUTC: e.TimeUTC,
              result: "",
              elapsed: elapsed,
              active: TimeSpan.FromMinutes(-1), //TODO: check if mazak records active time anywhere
              foreignId: e.ForeignID);
            if (MachiningCompleted != null)
              MachiningCompleted(s, dset);

          }
          else
          {
            //TODO: add this with a FAIL result and skip the event in Update Log?
            trace.TraceEvent(System.Diagnostics.TraceEventType.Warning, 0, "Ignoring machine cycle at " +
                             e.TimeUTC.ToLocalTime().ToString() + " on pallet " + e.Pallet.ToString() + " because it" +
                             " is less than 30 seconds.");
          }

          break;

        case LogCode.UnloadBegin:

          _log.RecordUnloadStart(
            mats: FindMaterial(e, dset, cycle),
            pallet: e.Pallet.ToString(),
            lulNum: e.StationNumber,
            timeUTC: e.TimeUTC,
            foreignId: e.ForeignID);

          break;

        case LogCode.UnloadEnd:

          //TODO: test for rework
          var loadElapsed = CalculateElapsed(e, cycle, LogType.LoadUnloadCycle, e.StationNumber);

          _log.RecordUnloadEnd(
            mats: FindMaterial(e, dset, cycle),
            pallet: e.Pallet.ToString(),
            lulNum: e.StationNumber,
            timeUTC: e.TimeUTC,
            elapsed: loadElapsed,
            active: loadElapsed,
            foreignId: e.ForeignID);

          break;

        case LogCode.PalletMoving:

          if (e.FromPosition != null && e.FromPosition.StartsWith("LS"))
          {
            CheckPendingLoads(e.Pallet, e.TimeUTC, e.ForeignID, dset, true, cycle);
          }

          if (PalletMove != null)
          {
            PalletMove(e.Pallet, e.FromPosition, e.TargetPosition);
          }

          break;

      }
    }
    #endregion

    #region Material
    private List<LogMaterial> CreateMaterialWithoutIDs(LogEntry e, ReadOnlyDataSet dset)
    {
      string unique;
      string partName;
      int numProc;
      FindPart(dset, e, out unique, out partName, out numProc);

      var ret = new List<LogMaterial>();
      ret.Add(new LogMaterial(-1, unique, e.Process, partName, numProc, ""));

      return ret;
    }

    private List<LogMaterial> FindMaterial(LogEntry e, ReadOnlyDataSet dset, IList<MWI.LogEntry> oldEvents)
    {
      return FindMaterialByProcess(e, e.Process, dset, oldEvents);
    }

    private List<LogMaterial> FindMaterialByProcess(LogEntry e, int procToCheck, ReadOnlyDataSet dset, IList<MWI.LogEntry> oldEvents)
    {
      SortedList<string, LogMaterial> byFace; //face -> material

      if (procToCheck >= 1)
      {
        byFace = CheckOldCycles(e, procToCheck, oldEvents);
      }
      else
      {
        byFace = new SortedList<string, LogMaterial>();
      }

      var ret = new List<LogMaterial>();

      if (e.FixedQuantity == 1)
      {

        if (byFace.ContainsKey(e.Process.ToString()))
        {
          ret.Add(byFace[e.Process.ToString()]);

        }
        else
        {

          //must create material
          string unique;
          string partName;
          int numProc;
          FindPart(dset, e, out unique, out partName, out numProc);
          ret.Add(new LogMaterial(_log.AllocateMaterialID(unique, partName, numProc), unique,
                                     e.Process, partName, numProc, e.Process.ToString()));

          trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
                           "Creating material " + ret[0].MaterialID + " for code " + e.Code.ToString() + " at " +
                           e.StationNumber.ToString() + " for " + e.FullPartName + " - " + e.Process.ToString() + " on " +
                           "pallet " + e.Pallet.ToString() + " unique " + unique);
        }

      }
      else
      {

        string unique = null;
        string partName = null;
        int numProc = 1;

        for (int i = 1; i <= e.FixedQuantity; i += 1)
        {
          string face = e.Process.ToString() + "-" + i.ToString();

          if (byFace.ContainsKey(face))
          {
            ret.Add(byFace[face]);
          }
          else
          {
            //must create material
            if (unique == null)
              FindPart(dset, e, out unique, out partName, out numProc);
            ret.Add(new LogMaterial(_log.AllocateMaterialID(unique, partName, numProc), unique,
                                       e.Process, partName, numProc, face));

            trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
                             "Creating material " + ret[ret.Count - 1].MaterialID + " for code " + e.Code.ToString() + " at " +
                             e.StationNumber.ToString() + " for " + e.FullPartName + " - " + e.Process.ToString() + " " +
                             "index " + i.ToString() + " on " +
                             "pallet " + e.Pallet.ToString() + " unique " + unique);

          }
        }
      }

      return ret;
    }

    private SortedList<string, LogMaterial> CheckOldCycles(LogEntry e, int procToCheck, IList<MWI.LogEntry> oldEvents)
    {
      var byFace = new SortedList<string, LogMaterial>(); //face -> material

      for (int i = oldEvents.Count - 1; i >= 0; i -= 1)
      {
        // When looking for material for an unload event, we want to skip over load events,
        // since an ending load event might have come through with the new material id that is loaded.
        if (e.Code == LogCode.UnloadEnd && oldEvents[i].Result == "LOAD")
          continue;

        foreach (LogMaterial mat in oldEvents[i].Material)
        {
          if (mat.PartName == e.JobPartName
              && mat.Process == procToCheck
              && mat.MaterialID >= 0
              && !byFace.ContainsKey(mat.Face))
          {

            string newFace;
            if (e.FixedQuantity == 1)
              newFace = e.Process.ToString();
            else
            {
              int idx = mat.Face.IndexOf('-');
              if (idx >= 0 && idx < mat.Face.Length)
                newFace = e.Process.ToString() + mat.Face.Substring(idx);
              else
                newFace = e.Process.ToString();
            }

            byFace[newFace] =
              new LogMaterial(mat.MaterialID, mat.JobUniqueStr, e.Process, mat.PartName, mat.NumProcesses, newFace);
          }
        }
      }

      return byFace;
    }

    private string PendingLoadKey(LogEntry e)
    {
      return e.FullPartName + "," + e.Process.ToString() + "," + e.FixedQuantity.ToString();
    }

    private List<MWI.LogEntry> CheckPendingLoads(int pallet, DateTime t, string foreignID, ReadOnlyDataSet dset, bool palletCycle, List<MWI.LogEntry> cycle)
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
            trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
                             "Skipping a pallet cycle at time " + t.ToString() + " because we detected remachining");

        }

        return cycle;
      }

      var mat = new Dictionary<string, IEnumerable<LogMaterial>>();

      foreach (var p in pending)
      {
        var s = p.Key.Split(',');
        if (s.Length != 3) continue;

        var e = new LogEntry();
        e.Code = LogCode.LoadEnd;
        e.TimeUTC = t.AddSeconds(1);
        e.ForeignID = "";

        e.Pallet = pallet;
        e.Program = "";
        e.TargetPosition = "";
        e.FromPosition = "";

        e.FullPartName = s[0];
        int idx = e.FullPartName.IndexOf(':');
        if (idx >= 0)
          e.JobPartName = e.FullPartName.Substring(0, idx);
        else
          e.JobPartName = e.FullPartName;

        if (!int.TryParse(s[1], out e.Process))
          e.Process = 1;
        if (!int.TryParse(s[2], out e.FixedQuantity))
          e.FixedQuantity = 1;

        mat[p.Key] = FindMaterialByProcess(e, e.Process - 1, dset, cycle);
      }

      _log.CompletePalletCycle(pallet.ToString(), t, foreignID, mat,
                FMSSettings.SerialType, FMSSettings.SerialLength);

      if (palletCycle)
        return cycle;
      else
        return _log.CurrentPalletLog(pallet.ToString());
    }

    private void FindPart(ReadOnlyDataSet dset, LogEntry e, out string unique, out string partName, out int numProc)
    {
      unique = "";
      numProc = e.Process;
      partName = e.JobPartName;

      //first search pallets for the given schedule id.  Since the part name usually includes the UID assigned for this
      //download, even if old log entries are being processed the correct unique string will still be loaded.
      int scheduleID = -1;
      foreach (ReadOnlyDataSet.PalletSubStatusRow palRow in dset.PalletSubStatus.Rows)
      {
        if (palRow.PalletNumber == e.Pallet && palRow.PartName == e.FullPartName && palRow.PartProcessNumber == e.Process)
        {
          scheduleID = palRow.ScheduleID;
          break;
        }
      }

      if (scheduleID >= 0)
      {
        foreach (ReadOnlyDataSet.ScheduleRow schRow in dset.Schedule.Rows)
        {
          if (schRow.ScheduleID == scheduleID && !schRow.IsCommentNull())
          {
            int path;
            bool manual;
            MazakPart.ParseComment(schRow.Comment, out unique, out path, out manual);
            numProc = schRow.GetScheduleProcessRows().Length;
            if (numProc < e.Process) numProc = e.Process;
            return;
          }
        }
      }

      trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
                       "Unable to find schedule ID for " + e.FullPartName + "-" + e.Process.ToString() + " on pallet " + e.Pallet.ToString() +
                       " at " + e.TimeUTC.ToLocalTime().ToString());

      // search for the first schedule for this part
      foreach (ReadOnlyDataSet.ScheduleRow schRow in dset.Schedule.Rows)
      {
        if (schRow.PartName == e.FullPartName && !schRow.IsCommentNull())
        {
          int path;
          bool manual;
          MazakPart.ParseComment(schRow.Comment, out unique, out path, out manual);
          numProc = schRow.GetScheduleProcessRows().Length;
          if (numProc < e.Process) numProc = e.Process;
          return;
        }
      }

      trace.TraceEvent(System.Diagnostics.TraceEventType.Warning, 0, "Unable to find any schedule for part " + e.FullPartName);
    }
    #endregion

    #region Elapsed
    private TimeSpan CalculateElapsed(LogEntry e, IList<MWI.LogEntry> oldEvents, LogType ty, int statNum)
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

      trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
                       "Calculating elapsed time for " + e.Code.ToString() + " - " + e.TimeUTC.ToString() +
                " did not find a previous cycle event");

      return TimeSpan.Zero;
    }
    #endregion
  }
}

