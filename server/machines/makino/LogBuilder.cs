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
using System.Linq;
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Makino
{
  public class LogBuilder(IMakinoDB makinoDB, IRepository logDb)
  {
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<LogBuilder>();

    public bool CheckLogs(DateTime lastDate)
    {
      var devices = makinoDB.Devices();

      var machine = makinoDB.QueryMachineResults(lastDate, DateTime.UtcNow.AddMinutes(1));
      var loads = makinoDB.QueryLoadUnloadResults(lastDate, DateTime.UtcNow.AddMinutes(1));

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

      return newLogEntries;
    }

    private void AddMachineToLog(
      DateTime timeToSkip,
      IDictionary<int, PalletLocation> devices,
      MachineResults m
    )
    {
      //find the location
      PalletLocation loc;
      if (devices.TryGetValue(m.DeviceID, out PalletLocation value))
        loc = value;
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
      var matList = FindOrCreateMaterial(
        m.PalletID,
        m.FixtureNumber,
        m.EndDateTimeUTC,
        m.OrderName,
        m.PartName,
        m.ProcessNum,
        numParts
      );

      var elapsed = m.EndDateTimeUTC.Subtract(m.StartDateTimeUTC);

      //check if the cycle already exists
      if (
        timeToSkip == m.EndDateTimeUTC
        && logDb.CycleExists(m.EndDateTimeUTC, m.PalletID, LogType.MachineCycle, "MC", loc.Num)
      )
      {
        return;
      }

      var extraData = new Dictionary<string, string>();
      if (matList.Count > 0)
      {
        var matID1 = matList[0].MaterialID;
        Log.Debug(
          "Starting load of common values between the times of {start} and {end} on DeviceID {deviceID}."
            + "These values will be attached to part {part} with matid {matid}",
          m.StartDateTimeLocal,
          m.EndDateTimeLocal,
          m.DeviceID,
          m.PartName,
          matID1
        );

        foreach (var v in makinoDB.QueryCommonValues(m))
        {
          Log.Debug("Common value with number {num} and value {val}", +v.Number, v.Value);
          extraData[v.Number.ToString()] = v.Value;
        }
      }
      logDb.RecordMachineEnd(
        mats: matList,
        pallet: m.PalletID,
        statName: "MC",
        statNum: loc.Num,
        program: m.Program,
        result: "",
        timeUTC: m.EndDateTimeUTC,
        elapsed: elapsed,
        active: TimeSpan.FromSeconds(m.SpindleTimeSeconds),
        extraData: extraData,
        foreignId: MkForeignID(m.PalletID, m.FixtureNumber, m.OrderName, m.EndDateTimeUTC)
      );

      AddInspection(m, matList);
    }

    private void AddInspection(MachineResults m, IList<EventLogMaterial> material)
    {
      if (logDb == null)
        return;

      var job = logDb.LoadJob(m.OrderName);
      if (job == null)
        return;

      foreach (var mat in material)
      {
        logDb.MakeInspectionDecisions(
          mat.MaterialID,
          m.ProcessNum,
          job.Processes[m.ProcessNum - 1].Paths[0].Inspections
        );
      }
    }

    private static string MkForeignID(int pallet, int fixture, string order, DateTime loadedUTC)
    {
      return order + "-" + pallet.ToString() + "-" + fixture.ToString() + "-" + loadedUTC.ToString("o");
    }

    private static bool ForeignIDMatchesPallet(int pallet, int fixture, string order, string foreignID)
    {
      return foreignID.StartsWith(order + "-" + pallet.ToString() + "-" + fixture.ToString() + "-");
    }

    public static LogEntry FindLogByForeign(
      int pallet,
      int fixture,
      string order,
      DateTime time,
      IRepository logDb
    )
    {
      var mostRecent = logDb.MostRecentLogEntryLessOrEqualToForeignID(
        MkForeignID(pallet, fixture, order, time)
      );
      if (
        mostRecent == null
        || !ForeignIDMatchesPallet(pallet, fixture, order, logDb.ForeignIDForCounter(mostRecent.Counter))
      )
      {
        return null;
      }
      else
      {
        return mostRecent;
      }
    }

    private void AddLoadToLog(DateTime timeToSkip, IDictionary<int, PalletLocation> devices, WorkSetResults w)
    {
      //find the location
      PalletLocation loc;
      if (devices.TryGetValue(w.DeviceID, out PalletLocation value))
        loc = value;
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
        var matList = FindOrCreateMaterial(
          w.PalletID,
          w.FixtureNumber,
          w.EndDateTimeUTC,
          w.UnloadOrderName,
          w.UnloadPartName,
          w.UnloadProcessNum,
          numParts
        );

        //check if the cycle already exists
        if (
          timeToSkip == w.EndDateTimeUTC
          && logDb.CycleExists(w.EndDateTimeUTC, w.PalletID, LogType.LoadUnloadCycle, "L/U", loc.Num)
        )
          return;

        logDb.RecordUnloadEnd(
          mats: matList,
          pallet: w.PalletID,
          lulNum: loc.Num,
          timeUTC: w.EndDateTimeUTC,
          elapsed: elapsed,
          active: elapsed,
          foreignId: MkForeignID(w.PalletID, w.FixtureNumber, w.UnloadOrderName, w.EndDateTimeUTC)
        );
      }

      //Pallet Cycle
      logDb.CompletePalletCycle(w.PalletID, w.EndDateTimeUTC, "");

      //now the load cycle
      numParts = 0;
      foreach (var i in w.LoadQuantities)
        numParts += i;

      if (numParts > 0)
      {
        //create the material
        var matList = AllocateMatIds(
          numParts,
          w.LoadOrderName,
          w.LoadPartName,
          w.LoadProcessNum,
          w.EndDateTimeUTC.AddSeconds(1)
        );

        logDb.RecordLoadEnd(
          toLoad: new[]
          {
            new MaterialToLoadOntoPallet()
            {
              LoadStation = loc.Num,
              Elapsed = elapsed,
              Faces =
              [
                new MaterialToLoadOntoFace()
                {
                  MaterialIDs = System.Collections.Immutable.ImmutableList.CreateRange(matList),
                  FaceNum = 1,
                  Process = w.LoadProcessNum,
                  Path = null,
                  ActiveOperationTime = elapsed,
                }
              ]
            }
          },
          pallet: w.PalletID,
          timeUTC: w.EndDateTimeUTC.AddSeconds(1),
          foreignId: MkForeignID(w.PalletID, w.FixtureNumber, w.LoadOrderName, w.EndDateTimeUTC.AddSeconds(1))
        );
      }
    }

    private List<long> AllocateMatIds(int count, string order, string part, int numProcess, DateTime endUTC)
    {
      var matIds = new List<long>();
      for (int i = 0; i < count; i++)
      {
        matIds.Add(logDb.AllocateMaterialIDAndGenerateSerial(order, part, numProcess, endUTC, out var _));
      }
      return matIds;
    }

    private List<EventLogMaterial> FindOrCreateMaterial(
      int pallet,
      int fixturenum,
      DateTime endUTC,
      string order,
      string part,
      int process,
      int count
    )
    {
      var mostRecent = FindLogByForeign(pallet, fixturenum, order, endUTC, logDb);

      if (mostRecent == null)
      {
        Log.Warning(
          "Unable to find any material ids for pallet {pal} on fixture {fix} for order {ord} at time {time}, got {mostRecent}",
          pallet,
          fixturenum,
          order,
          endUTC,
          mostRecent
        );
        return AllocateMatIds(count, order, part, process, endUTC)
          .Select(matId => new EventLogMaterial()
          {
            MaterialID = matId,
            Process = process,
            Face = 1
          })
          .ToList();
      }

      var mats = mostRecent
        .Material.Select(m => new EventLogMaterial()
        {
          MaterialID = m.MaterialID,
          Process = process,
          Face = 1
        })
        .ToList();

      if (mats.Count < count)
      {
        Log.Warning(
          "Pallet {pal} on fixture {fix} for order {ord} at time {time} has {mats} material ids, but expected {count}",
          pallet,
          fixturenum,
          order,
          endUTC,
          mats,
          count
        );

        mats.AddRange(
          AllocateMatIds(count - mats.Count, order, part, process, endUTC)
            .Select(matId => new EventLogMaterial()
            {
              MaterialID = matId,
              Process = process,
              Face = 1
            })
        );
      }

      return mats;
    }
  }
}
