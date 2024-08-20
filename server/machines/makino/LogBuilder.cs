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

namespace BlackMaple.FMSInsight.Makino
{
  public class LogBuilder(IMakinoDB makinoDB, IRepository logDb)
  {
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<LogBuilder>();
    private readonly JobCache _jobCache = new(logDb);

    public bool CheckLogs(DateTime lastDate, DateTime nowUTC)
    {
      var results = makinoDB.LoadResults(lastDate, nowUTC.AddMinutes(1));

      var machines = results.MachineResults;
      machines.Sort((x, y) => x.EndDateTimeUTC.CompareTo(y.EndDateTimeUTC));

      var works = results
        .WorkSetResults.GroupBy(w => (w.StartDateTimeUTC, w.EndDateTimeUTC, w.DeviceID, w.PalletID))
        .OrderBy(g => g.Key.EndDateTimeUTC);

      var mE = machines.GetEnumerator();
      var lE = works.GetEnumerator();
      var moreMachines = mE.MoveNext();
      var moreLoads = lE.MoveNext();

      bool newLogEntries = false;
      while (moreMachines || moreLoads)
      {
        if (moreMachines && moreLoads)
        {
          //check which event occured first and process it
          if (mE.Current.EndDateTimeUTC < lE.Current.Key.EndDateTimeUTC)
          {
            AddMachineToLog(lastDate, results.Devices, mE.Current, ref newLogEntries);
            moreMachines = mE.MoveNext();
          }
          else
          {
            AddLoadToLog(lastDate, results.Devices, lE.Current, ref newLogEntries);
            moreLoads = lE.MoveNext();
          }
        }
        else if (moreMachines)
        {
          AddMachineToLog(lastDate, results.Devices, mE.Current, ref newLogEntries);
          moreMachines = mE.MoveNext();
        }
        else
        {
          AddLoadToLog(lastDate, results.Devices, lE.Current, ref newLogEntries);
          moreLoads = lE.MoveNext();
        }
      }

      return newLogEntries;
    }

    private void AddMachineToLog(
      DateTime timeToSkip,
      IReadOnlyDictionary<int, PalletLocation> devices,
      MachineResults m,
      ref bool newLogEntries
    )
    {
      //find the location
      PalletLocation loc;
      if (devices.TryGetValue(m.DeviceID, out var value))
      {
        loc = value;
      }
      else
      {
        loc = new PalletLocation(PalletLocationEnum.Buffer, "Unknown", 0);
      }

      //check if the cycle already exists
      if (
        timeToSkip == m.EndDateTimeUTC
        && logDb.CycleExists(m.EndDateTimeUTC, m.PalletID, LogType.MachineCycle, loc.StationGroup, loc.Num)
      )
      {
        return;
      }

      if (loc.Location != PalletLocationEnum.Machine)
      {
        Log.Error("Creating machine cycle for device that is not a machine: " + loc.Location.ToString());
      }

      //count the number of parts
      int numParts = 0;
      foreach (var i in m.OperQuantities ?? [])
        numParts += i;
      if (numParts <= 0)
        return;
      if (string.IsNullOrEmpty(m.OrderName))
        return;
      if (string.IsNullOrEmpty(m.PartName))
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

      var extraData = new Dictionary<string, string>();
      if (matList.Count > 0)
      {
        foreach (var v in m.CommonValues ?? [])
        {
          extraData[v.Number.ToString()] = v.Value ?? "";
        }
      }

      var job = _jobCache.Lookup(m.OrderName);

      TimeSpan active;
      if (job != null && m.ProcessNum <= job.Processes.Count)
      {
        active = job.Processes[m.ProcessNum - 1].Paths[0].Stops[0].ExpectedCycleTime;
      }
      else
      {
        active = TimeSpan.FromSeconds(m.SpindleTimeSeconds);
      }

      logDb.RecordMachineEnd(
        mats: matList,
        pallet: m.PalletID,
        statName: loc.StationGroup,
        statNum: loc.Num,
        program: m.Program,
        result: "",
        timeUTC: m.EndDateTimeUTC,
        elapsed: elapsed,
        active: active,
        extraData: extraData,
        foreignId: MkForeignID(m.PalletID, m.FixtureNumber, m.OrderName, m.EndDateTimeUTC)
      );
      newLogEntries = true;

      AddInspection(m, matList, m.EndDateTimeUTC);
    }

    private void AddInspection(MachineResults m, IList<EventLogMaterial> material, DateTime time)
    {
      if (logDb == null)
        return;

      if (string.IsNullOrEmpty(m.OrderName))
        return;
      var job = _jobCache.Lookup(m.OrderName);
      if (job == null)
        return;

      foreach (var mat in material)
      {
        logDb.MakeInspectionDecisions(
          mat.MaterialID,
          m.ProcessNum,
          job.Processes[m.ProcessNum - 1].Paths[0].Inspections,
          time
        );
      }
    }

    public static string MkForeignID(int pallet, int fixture, string order, DateTime loadedUTC)
    {
      return order + "-" + pallet.ToString() + "-" + fixture.ToString() + "-" + loadedUTC.ToString("o");
    }

    private static bool ForeignIDMatchesPallet(int pallet, int fixture, string order, string foreignID)
    {
      return foreignID.StartsWith(order + "-" + pallet.ToString() + "-" + fixture.ToString() + "-");
    }

    public static LogEntry? FindLogByForeign(
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

    private void AddLoadToLog(
      DateTime timeToSkip,
      IReadOnlyDictionary<int, PalletLocation> devices,
      IGrouping<
        (DateTime StartDateTimeUTC, DateTime EndDateTimeUTC, int DeviceID, int PalletID),
        WorkSetResults
      > ws,
      ref bool newLogEntries
    )
    {
      //find the location
      PalletLocation loc;
      if (devices.TryGetValue(ws.Key.DeviceID, out var value))
        loc = value;
      else
        loc = new PalletLocation(PalletLocationEnum.Buffer, "Unknown", 1);

      //check if the cycle already exists
      if (
        timeToSkip == ws.Key.EndDateTimeUTC
        && logDb.CycleExists(ws.Key.EndDateTimeUTC, ws.Key.PalletID, LogType.LoadUnloadCycle, "L/U", loc.Num)
      )
      {
        return;
      }

      if (loc.Location != PalletLocationEnum.LoadUnload)
      {
        Log.Error("Creating machine cycle for device that is not a load: " + loc.Location.ToString());
      }

      //calculate the elapsed time
      var elapsed = ws.Key.EndDateTimeUTC.Subtract(ws.Key.StartDateTimeUTC);

      // first unloads
      foreach (var w in ws)
      {
        //count the number of unloaded parts
        int numParts = 0;
        foreach (var i in w.UnloadNormalQuantities ?? [])
          numParts += i;

        //Only process unload cycles if remachine is false
        if (
          numParts > 0
          && !w.Remachine
          && !string.IsNullOrEmpty(w.UnloadOrderName)
          && !string.IsNullOrEmpty(w.UnloadPartName)
        )
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

          TimeSpan active = TimeSpan.Zero;
          Job? unloadJob = _jobCache.Lookup(w.UnloadOrderName);
          if (unloadJob != null && w.UnloadProcessNum <= unloadJob.Processes.Count)
          {
            active = unloadJob.Processes[w.UnloadProcessNum - 1].Paths[0].ExpectedUnloadTime;
          }

          logDb.RecordUnloadEnd(
            mats: matList,
            pallet: w.PalletID,
            lulNum: loc.Num,
            timeUTC: w.EndDateTimeUTC,
            elapsed: elapsed,
            active: active * matList.Count,
            foreignId: MkForeignID(w.PalletID, w.FixtureNumber, w.UnloadOrderName, w.EndDateTimeUTC)
          );
          newLogEntries = true;
        }
      }

      //Pallet Cycle
      if (ws.Any(w => !w.Remachine))
      {
        logDb.CompletePalletCycle(ws.Key.PalletID, ws.Key.EndDateTimeUTC, "");
        newLogEntries = true;
      }

      var faces = ImmutableList.CreateBuilder<MaterialToLoadOntoFace>();

      foreach (var w in ws.OrderBy(w => w.FixtureNumber))
      {
        //now the load cycle
        int numParts = (w.LoadQuantities ?? []).Sum();
        if (numParts == 0 || w.Remachine)
          continue;
        if (string.IsNullOrEmpty(w.LoadOrderName))
          continue;
        if (string.IsNullOrEmpty(w.LoadPartName))
          continue;

        //create the material
        var matList = AllocateMatIds(
          numParts,
          w.LoadOrderName,
          w.LoadPartName,
          w.LoadProcessNum,
          w.EndDateTimeUTC.AddSeconds(1)
        );

        TimeSpan active = TimeSpan.Zero;
        var loadJob = _jobCache.Lookup(w.LoadOrderName);
        if (loadJob != null && w.LoadProcessNum <= loadJob.Processes.Count)
        {
          active = loadJob.Processes[w.LoadProcessNum - 1].Paths[0].ExpectedLoadTime;
        }

        faces.Add(
          new MaterialToLoadOntoFace()
          {
            MaterialIDs = ImmutableList.CreateRange(matList),
            FaceNum = w.FixtureNumber,
            Process = w.LoadProcessNum,
            Path = null,
            ActiveOperationTime = active * matList.Count,
            ForeignID = MkForeignID(
              ws.Key.PalletID,
              w.FixtureNumber,
              w.LoadOrderName,
              w.EndDateTimeUTC.AddSeconds(1)
            ),
          }
        );
      }

      if (faces.Count > 0)
      {
        logDb.RecordLoadEnd(
          toLoad: new[]
          {
            new MaterialToLoadOntoPallet()
            {
              LoadStation = loc.Num,
              Elapsed = elapsed,
              Faces = faces.ToImmutable(),
            },
          },
          pallet: ws.Key.PalletID,
          timeUTC: ws.Key.EndDateTimeUTC.AddSeconds(1)
        );
        newLogEntries = true;
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
            Face = fixturenum,
          })
          .ToList();
      }

      var mats = mostRecent
        .Material.Select(m => new EventLogMaterial()
        {
          MaterialID = m.MaterialID,
          Process = process,
          Face = fixturenum,
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
              Face = fixturenum,
            })
        );
      }

      return mats;
    }
  }
}
