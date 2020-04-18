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
using System.Collections.Generic;
using BlackMaple.MachineWatchInterface;
using BlackMaple.MachineFramework;

namespace Makino
{
  public class LogTimer
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<LogTimer>();
    private object _lock;
    private JobLogDB _log;
    private JobDB _jobDB;
    private MakinoDB _makinoDB;
    private StatusDB _status;
    private System.Timers.Timer _timer;

    public delegate void LogsProcessedDel();
    public event LogsProcessedDel LogsProcessed;

    public FMSSettings Settings
    {
      get;
      set;
    }

    public LogTimer(
      JobLogDB log, JobDB jobDB, MakinoDB makinoDB, StatusDB status, FMSSettings settings)
    {
      _lock = new object();
      _log = log;
      _jobDB = jobDB;
      Settings = settings;
      _makinoDB = makinoDB;
      _status = status;
      TimerSignaled(null, null);
      _timer = new System.Timers.Timer(TimeSpan.FromMinutes(1).TotalMilliseconds);
      _timer.Elapsed += TimerSignaled;
      _timer.Start();
    }

    public void Halt()
    {
      _timer.Stop();
      _timer.Elapsed -= TimerSignaled;
    }

    private void TimerSignaled(object sender, System.Timers.ElapsedEventArgs e)
    {
      try
      {
        lock (_lock)
        {
          // Load one month
          var lastDate = _log.MaxLogDate();
          if (DateTime.UtcNow.Subtract(lastDate) > TimeSpan.FromDays(30))
            lastDate = DateTime.UtcNow.AddDays(-30);

          CheckLogs(lastDate);
        }

      }
      catch (Exception ex)
      {
        Log.Error(ex, "Unhandled error recording log data");
      }
    }

    /* This has public instead of private for testing */
    public void CheckLogs(DateTime lastDate)
    {
      var devices = _makinoDB.Devices();

      var machine = _makinoDB.QueryMachineResults(lastDate, DateTime.UtcNow.AddMinutes(1));
      var loads = _makinoDB.QueryLoadUnloadResults(lastDate, DateTime.UtcNow.AddMinutes(1));

      //need to iterate machines and loads by date
      machine.Sort((x, y) => x.EndDateTimeUTC.CompareTo(y.EndDateTimeUTC));
      loads.Sort((x, y) => x.EndDateTimeUTC.CompareTo(y.EndDateTimeUTC));

      var mE = machine.GetEnumerator();
      var lE = loads.GetEnumerator();
      var moreMachines = mE.MoveNext();
      var moreLoads = lE.MoveNext();

      bool newLogEntries = false;
      while (moreMachines || moreLoads)
      {
        newLogEntries = true;
        if (moreMachines && moreLoads)
        {

          //check which event occured first and process it
          if (mE.Current.EndDateTimeUTC < lE.Current.EndDateTimeUTC)
          {
            AddMachineToLog(lastDate, devices, mE.Current);
            moreMachines = mE.MoveNext();
          }
          else
          {
            AddLoadToLog(lastDate, devices, lE.Current);
            moreLoads = lE.MoveNext();
          }

        }
        else if (moreMachines)
        {
          AddMachineToLog(lastDate, devices, mE.Current);
          moreMachines = mE.MoveNext();
        }
        else
        {
          AddLoadToLog(lastDate, devices, lE.Current);
          moreLoads = lE.MoveNext();
        }
      }

      if (newLogEntries)
        LogsProcessed?.Invoke();
    }

    private void AddMachineToLog(
        DateTime timeToSkip, IDictionary<int, PalletLocation> devices, MakinoDB.MachineResults m)
    {
      //find the location
      PalletLocation loc;
      if (devices.ContainsKey(m.DeviceID))
        loc = devices[m.DeviceID];
      else
        loc = new PalletLocation(PalletLocationEnum.Buffer, "Unknown", 0);

      if (loc.Location != PalletLocationEnum.Machine)
      {
        Log.Error("Creating machine cycle for device that is not a machine: " + loc.Location.ToString());
      }

      //count the number of parts
      int numParts = 0;
      foreach (var i in m.OperQuantities)
        numParts += i;
      if (numParts <= 0)
        return;

      //create the material
      var matList = FindOrCreateMaterial(m.PalletID, m.FixtureNumber, m.EndDateTimeUTC,
                                         m.OrderName, m.PartName, m.ProcessNum, numParts);

      var elapsed = m.EndDateTimeUTC.Subtract(m.StartDateTimeUTC);

      //check if the cycle already exists
      if (timeToSkip == m.EndDateTimeUTC
    && _log.CycleExists(m.EndDateTimeUTC, m.PalletID.ToString(), LogType.MachineCycle, "MC", loc.Num))
      {
        return;
      }

      var extraData = new Dictionary<string, string>();
      if (matList.Count > 0)
      {
        var matID1 = matList[0].MaterialID;
        Log.Debug(
            "Starting load of common values between the times of {start} and {end} on DeviceID {deviceID}." +
            "These values will be attached to part {part} with serial {serial}",
            m.StartDateTimeLocal, m.EndDateTimeLocal, m.DeviceID, m.PartName, Settings.ConvertMaterialIDToSerial(matID1));

        foreach (var v in _makinoDB.QueryCommonValues(m))
        {
          Log.Debug("Common value with number {num} and value {val}", +v.Number, v.Value);
          extraData[v.Number.ToString()] = v.Value;
        }
      }
      _log.RecordMachineEnd(
        mats: matList,
        pallet: m.PalletID.ToString(),
        statName: "MC",
        statNum: loc.Num,
        program: m.Program,
        result: "",
        timeUTC: m.EndDateTimeUTC,
        elapsed: elapsed,
        active: TimeSpan.FromSeconds(m.SpindleTimeSeconds),
        extraData: extraData);

      AddInspection(m, matList);
    }

    private void AddInspection(MakinoDB.MachineResults m, IList<JobLogDB.EventLogMaterial> material)
    {
      if (_jobDB == null && _log == null)
        return;

      var job = _jobDB.LoadJob(m.OrderName);
      if (job == null)
        return;

      foreach (var mat in material)
      {
        _log.MakeInspectionDecisions(mat.MaterialID, m.ProcessNum, job.PathInspections(m.ProcessNum, path: 1));
      }
    }

    private void AddLoadToLog(
      DateTime timeToSkip, IDictionary<int, PalletLocation> devices, MakinoDB.WorkSetResults w)
    {
      //find the location
      PalletLocation loc;
      if (devices.ContainsKey(w.DeviceID))
        loc = devices[w.DeviceID];
      else
        loc = new PalletLocation(PalletLocationEnum.Buffer, "Unknown", 1);

      if (loc.Location != PalletLocationEnum.LoadUnload)
      {
        Log.Error("Creating machine cycle for device that is not a load: " + loc.Location.ToString());
      }

      //calculate the elapsed time
      var elapsed = w.EndDateTimeUTC.Subtract(w.StartDateTimeUTC);
      elapsed = new TimeSpan(elapsed.Ticks / 2);

      //count the number of unloaded parts
      int numParts = 0;
      foreach (var i in w.UnloadNormalQuantities)
        numParts += i;

      //Only process unload cycles if remachine is false
      if (numParts > 0 && !w.Remachine)
      {
        //create the material for unload
        var matList = FindOrCreateMaterial(w.PalletID, w.FixtureNumber, w.EndDateTimeUTC,
                                         w.UnloadOrderName, w.UnloadPartName, w.UnloadProcessNum, numParts);

        //check if the cycle already exists
        if (timeToSkip == w.EndDateTimeUTC
          && _log.CycleExists(w.EndDateTimeUTC, w.PalletID.ToString(), LogType.LoadUnloadCycle, "L/U", loc.Num))
          return;

        _log.RecordUnloadEnd(
          mats: matList,
          pallet: w.PalletID.ToString(),
          lulNum: loc.Num,
          timeUTC: w.EndDateTimeUTC,
          elapsed: elapsed,
          active: elapsed);
      }

      //Pallet Cycle
      _log.CompletePalletCycle(w.PalletID.ToString(), w.EndDateTimeUTC, "");

      //now the load cycle
      numParts = 0;
      foreach (var i in w.LoadQuantities)
        numParts += i;

      if (numParts > 0)
      {
        //create the material
        var matList = CreateMaterial(w.PalletID, w.FixtureNumber, w.EndDateTimeUTC.AddSeconds(1),
                                     w.LoadOrderName, w.LoadPartName, w.LoadProcessNum, numParts);

        _log.RecordLoadEnd(
          mats: matList,
          pallet: w.PalletID.ToString(),
          lulNum: loc.Num,
          timeUTC: w.EndDateTimeUTC.AddSeconds(1),
          elapsed: elapsed,
          active: elapsed);
      }
    }

    private IReadOnlyList<long> AllocateMatIds(int count, string order, string part, int numProcess)
    {
      var matIds = new List<long>();
      for (int i = 0; i < count; i++)
      {
        matIds.Add(_log.AllocateMaterialID(order, part, numProcess));
      }
      return matIds;
    }

    private IList<JobLogDB.EventLogMaterial> CreateMaterial(int pallet, int fixturenum, DateTime endUTC, string order, string part, int process, int count)
    {
      var rows = _status.CreateMaterialIDs(pallet, fixturenum, endUTC, order, AllocateMatIds(count, order, part, process), 0);

      var ret = new List<JobLogDB.EventLogMaterial>();
      foreach (var row in rows)
        ret.Add(new JobLogDB.EventLogMaterial() { MaterialID = row.MatID, Process = process, Face = "" });
      return ret;
    }

    private IList<JobLogDB.EventLogMaterial> FindOrCreateMaterial(int pallet, int fixturenum, DateTime endUTC, string order, string part, int process, int count)
    {
      var rows = _status.FindMaterialIDs(pallet, fixturenum, endUTC);

      if (rows.Count == 0)
      {
        Log.Warning("Unable to find any material ids for pallet " + pallet.ToString() + "-" +
          fixturenum.ToString() + " for order " + order + " for event at time " + endUTC.ToString());
        rows = _status.CreateMaterialIDs(pallet, fixturenum, endUTC, order, AllocateMatIds(count, order, part, process), 0);
      }

      if (rows[0].Order != order)
      {
        Log.Warning("MaterialIDs for pallet " + pallet.ToString() + "-" + fixturenum.ToString() +
          " for event at time " + endUTC.ToString() + " does not have matching orders: " +
          "expected " + order + " but found " + rows[0].Order);

        rows = _status.CreateMaterialIDs(pallet, fixturenum, endUTC, order, AllocateMatIds(count, order, part, process), 0);
      }

      if (rows.Count < count)
      {
        Log.Warning("Pallet " + pallet.ToString() + "-" + fixturenum.ToString() +
          " at event time " + endUTC.ToString() + " with order " + order + " was expected to have " +
          count.ToString() + " material ids, but only " + rows.Count + " were loaded");

        int maxCounter = -1;
        foreach (var row in rows)
        {
          if (row.LocCounter > maxCounter)
            maxCounter = row.LocCounter;
        }

        //stupid that IList doesn't have AddRange
        foreach (var row in _status.CreateMaterialIDs(
          pallet, fixturenum, rows[0].LoadedUTC, order, AllocateMatIds(count - rows.Count, order, part, process), maxCounter + 1))
        {
          rows.Add(row);
        }
      }

      var ret = new List<JobLogDB.EventLogMaterial>();
      foreach (var row in rows)
      {
        if (Settings.SerialType != SerialType.NoAutomaticSerials)
          CreateSerial(row.MatID, order, part, process, fixturenum.ToString(), _log, Settings);
        ret.Add(new JobLogDB.EventLogMaterial() { MaterialID = row.MatID, Process = process, Face = "" });
      }
      return ret;
    }

    public static void CreateSerial(long matID, string jobUniqe, string partName, int process, string face,
                                        JobLogDB _log, FMSSettings Settings)
    {
      foreach (var stat in _log.GetLogForMaterial(matID))
      {
        if (stat.LogType == LogType.PartMark &&
            stat.LocationNum == 1)
        {
          foreach (LogMaterial mat in stat.Material)
          {
            if (mat.Process == process)
            {
              //We have recorded the serial already
              return;
            }
          }
        }
      }

      var serial = Settings.ConvertMaterialIDToSerial(matID);

      //length 10 gets us to 1.5e18 which is not quite 2^64
      //still large enough so we will practically never roll around
      serial = serial.Substring(0, Math.Min(Settings.SerialLength, serial.Length));
      serial = serial.PadLeft(Settings.SerialLength, '0');

      Log.Debug("Recording serial for matid: {matid} {serial}", matID, serial);


      var logMat = new JobLogDB.EventLogMaterial() { MaterialID = matID, Process = process, Face = face };
      _log.RecordSerialForMaterialID(logMat, serial);
    }

#if DEBUG
    internal static List<string> errors = new List<string>();
#endif
  }
}

