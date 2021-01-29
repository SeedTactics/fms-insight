/* Copyright (c) 2017, John Lenz

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
using Xunit;
using Microsoft.Data.Sqlite;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;
using FluentAssertions;

namespace MachineWatchTest
{
  public class InspectionTest : IDisposable
  {

    private RepositoryConfig _repoCfg;
    private IRepository _insp;

    public InspectionTest()
    {
      _repoCfg = RepositoryConfig.InitializeSingleThreadedMemoryDB(new FMSSettings());
      _insp = _repoCfg.OpenConnection();
    }

    public void Dispose()
    {
      _repoCfg.CloseMemoryConnection();
    }

    [Fact]
    public void Counts()
    {
      List<InspectCount> cnts = new List<InspectCount>();

      InspectCount cnt = new InspectCount();

      cnt.Counter = "Test1";
      cnt.Value = 15;
      cnt.LastUTC = DateTime.Parse("1/5/2009 4:23:12 GMT");
      cnts.Add(cnt);

      cnt = new InspectCount();

      cnt.Counter = "Test2";
      cnt.Value = 1563;
      cnt.LastUTC = DateTime.Parse("1/15/2009 3:35:24 GMT");
      cnts.Add(cnt);

      cnt = new InspectCount();

      cnt.Counter = "Test3";
      cnt.Value = 532;
      cnt.LastUTC = DateTime.Parse("2/12/2009 15:03:55 GMT");
      cnts.Add(cnt);

      _insp.SetInspectCounts(cnts);

      IList<InspectCount> loaded = _insp.LoadInspectCounts();

      Assert.Equal(loaded, cnts);
    }

    [Fact]
    public void Frequencies()
    {
      var freqProg = new PathInspection() { InspectionType = "insp1", Counter = "counter1", RandomFreq = 0.5, TimeInterval = TimeSpan.FromHours(100) };

      for (int i = 0; i < 100; i++)
        _insp.MakeInspectionDecisions(i, 1, new[] { freqProg });

      int numInsp = 0;
      for (int i = 0; i < 100; i++)
      {
        if (FindDecision(i, "insp1", "counter1"))
          numInsp += 1;
      }

      Assert.True(numInsp > 0);
      Assert.True(numInsp < 100);
    }

    [Fact]
    public void Inspections()
    {
      var now = DateTime.UtcNow;
      //set the count as zero, otherwise it chooses a random
      InspectCount cnt = new InspectCount();
      cnt.Counter = "counter1";
      cnt.Value = 0;
      cnt.LastUTC = DateTime.UtcNow.AddHours(-11).AddMinutes(2);
      _insp.SetInspectCounts(new InspectCount[] { cnt });

      //set up a program
      var inspProg = new PathInspection() { InspectionType = "insp1", Counter = "counter1", MaxVal = 3, TimeInterval = TimeSpan.FromHours(11) };

      //the lastutc should be 2 minutes too short, so only inspections from the counter should take place

      _insp.MakeInspectionDecisions(1, 2, new[] { inspProg }, now);
      _insp.MakeInspectionDecisions(1, 2, new[] { inspProg }, now); // twice should have no effect
      CheckDecision(1, "insp1", "counter1", false, now);
      CheckCount("counter1", 1);
      CheckLastUTC("counter1", cnt.LastUTC);

      _insp.MakeInspectionDecisions(2, 2, new[] { inspProg }, now);
      CheckDecision(2, "insp1", "counter1", false, now);
      CheckCount("counter1", 2);
      CheckLastUTC("counter1", cnt.LastUTC);

      _insp.MakeInspectionDecisions(3, 2, new[] { inspProg }, now);
      CheckDecision(3, "insp1", "counter1", true, now);

      CheckCount("counter1", 0);
      CheckLastUTC("counter1", DateTime.UtcNow);

      //now check lastutc. set lastutc to be 2 minutes
      cnt = new InspectCount();
      cnt.Counter = "counter1";
      cnt.Value = 0;
      cnt.LastUTC = DateTime.UtcNow.AddHours(-11).AddMinutes(-2);
      _insp.SetInspectCounts(new InspectCount[] { cnt });

      _insp.MakeInspectionDecisions(4, 2, new[] { inspProg });
      CheckDecision(4, "insp1", "counter1", true, now);
      CheckLastUTC("counter1", now);
    }

    [Fact]
    public void ForcedInspection()
    {
      DateTime now = DateTime.UtcNow;

      //set up a program
      var inspProg = new PathInspection() { InspectionType = "insp1", Counter = "counter1", MaxVal = 13, TimeInterval = TimeSpan.FromHours(11) };

      //set the count as zero, otherwise it chooses a random
      InspectCount cnt = new InspectCount();
      cnt.Counter = "counter1";
      cnt.Value = 0;
      cnt.LastUTC = DateTime.UtcNow.AddHours(-10);
      _insp.SetInspectCounts(new InspectCount[] { cnt });

      //try making a decision
      _insp.ForceInspection(2, "insp1");

      _insp.MakeInspectionDecisions(1, 1, new[] { inspProg }, now);
      _insp.MakeInspectionDecisions(1, 1, new[] { inspProg }, now);
      CheckDecision(1, "insp1", "counter1", false, now);
      CheckCount("counter1", 1);

      _insp.MakeInspectionDecisions(2, 1, new[] { inspProg }, now);
      CheckDecision(2, "insp1", "counter1", true, now, true);

      CheckCount("counter1", 2);
    }

    [Fact]
    public void NextPiece()
    {
      DateTime now = DateTime.UtcNow;
      var job = new JobPlan("job1", 1);
      job.PartName = "part1";

      //set up a program
      var inspProg = new PathInspection() { InspectionType = "insp1", Counter = "counter1", MaxVal = 3, TimeInterval = TimeSpan.FromHours(11) };
      job.PathInspections(1, 1).Add(inspProg);

      //set the count as zero, otherwise it chooses a random
      InspectCount cnt = new InspectCount();
      cnt.Counter = "counter1";
      cnt.Value = 0;
      cnt.LastUTC = DateTime.UtcNow.AddHours(-10);
      _insp.SetInspectCounts(new InspectCount[] { cnt });

      PalletLocation palLoc = new PalletLocation(PalletLocationEnum.Machine, "MC", 1);

      _insp.NextPieceInspection(palLoc, "insp1");
      _insp.CheckMaterialForNextPeiceInspection(palLoc, 1);

      CheckCount("counter1", 0);

      _insp.MakeInspectionDecisions(1, 1, new[] { inspProg }, now);
      CheckCount("counter1", 1);
      CheckDecision(1, "insp1", "counter1", true, now, true);
    }

    [Fact]
    public void TranslateCounter()
    {
      var counter = "counter1-" +
        JobInspectionData.LoadFormatFlag(1) + "-" +
        JobInspectionData.UnloadFormatFlag(1) + "-" +
        JobInspectionData.LoadFormatFlag(2) + "-" +
        JobInspectionData.UnloadFormatFlag(2) + "-" +
        JobInspectionData.PalletFormatFlag(1) + "-" +
        JobInspectionData.PalletFormatFlag(2) + "-" +
        JobInspectionData.StationFormatFlag(1, 1) + "-" +
        JobInspectionData.StationFormatFlag(1, 2) + "-" +
        JobInspectionData.StationFormatFlag(2, 1) + "-" +
        JobInspectionData.StationFormatFlag(2, 2);

      var expandedCounter1 = "counter1-1-2-3-4-P1-P2-10-11-12-13";
      var expandedCounter2 = "counter1-6-8-7-9-P5-P4-15-16-18-19";

      //set the count as zero, otherwise it chooses a random
      var cnt = new InspectCount();
      cnt.Counter = expandedCounter1;
      cnt.Value = 0;
      cnt.LastUTC = DateTime.UtcNow.AddHours(-10);
      var cnt2 = new InspectCount();
      cnt2.Counter = expandedCounter2;
      cnt2.Value = 0;
      cnt2.LastUTC = DateTime.UtcNow.AddHours(-10);
      _insp.SetInspectCounts(new[] { cnt, cnt2 });


      var mat1Proc1 = new[] { new LogMaterial(1, "job1", 1, "part1", 2, "", "", "") };
      var mat1Proc2 = new[] { new LogMaterial(1, "job1", 2, "part1", 2, "", "", "") };
      var mat2Proc1 = new[] { new LogMaterial(2, "job1", 1, "part1", 2, "", "", "") };
      var mat2Proc2 = new[] { new LogMaterial(2, "job1", 2, "part1", 2, "", "", "") };

      _lastCycleTime = DateTime.UtcNow.AddDays(-1);

      AddCycle(mat1Proc1, "P1", LogType.LoadUnloadCycle, 1, false);
      AddCycle(mat2Proc1, "P5", LogType.LoadUnloadCycle, 6, false);
      AddCycle(mat1Proc1, "P1", LogType.MachineCycle, 10, false);
      AddCycle(mat2Proc1, "P5", LogType.MachineCycle, 15, false);
      AddCycle(mat1Proc1, "P1", LogType.MachineCycle, 11, false);
      AddCycle(mat2Proc1, "P5", LogType.MachineCycle, 16, false);
      AddCycle(mat1Proc1, "P1", LogType.LoadUnloadCycle, 2, false);
      AddCycle(mat2Proc1, "P5", LogType.LoadUnloadCycle, 8, false);

      AddCycle(mat1Proc2, "P2", LogType.LoadUnloadCycle, 3, false);
      AddCycle(mat2Proc2, "P4", LogType.LoadUnloadCycle, 7, false);
      AddCycle(mat1Proc2, "P2", LogType.MachineCycle, 12, false);
      AddCycle(mat2Proc2, "P4", LogType.MachineCycle, 18, false);
      AddCycle(mat1Proc2, "P2", LogType.MachineCycle, 13, false);
      AddCycle(mat2Proc2, "P4", LogType.MachineCycle, 19, false);
      AddCycle(mat1Proc2, "P2", LogType.LoadUnloadCycle, 4, true);
      AddCycle(mat2Proc2, "P4", LogType.LoadUnloadCycle, 9, true);

      var inspProg = new PathInspection() { InspectionType = "insp1", Counter = counter, MaxVal = 10, TimeInterval = TimeSpan.FromDays(2) };

      var now = DateTime.UtcNow;
      _insp.MakeInspectionDecisions(1, 2, new[] { inspProg }, now);
      CheckDecision(1, "insp1", expandedCounter1, false, now);
      Assert.Equal(2, _insp.LoadInspectCounts().Count);
      CheckCount(expandedCounter1, 1);
      CheckCount(expandedCounter2, 0);
      ExpectPathToBe(1, "insp1", new[] {
        new MaterialProcessActualPath() {
          MaterialID = 1,
          Process = 1,
          Pallet = "P1",
          LoadStation = 1,
          Stops = {
            new MaterialProcessActualPath.Stop() {StationName = "MC", StationNum = 10},
            new MaterialProcessActualPath.Stop() {StationName = "MC", StationNum = 11}
          },
          UnloadStation = 2,
        },
        new MaterialProcessActualPath() {
          MaterialID = 1,
          Process = 2,
          Pallet = "P2",
          LoadStation = 3,
          Stops = {
            new MaterialProcessActualPath.Stop() {StationName = "MC", StationNum = 12},
            new MaterialProcessActualPath.Stop() {StationName = "MC", StationNum = 13}
          },
          UnloadStation = 4,
        }
      });

      _insp.MakeInspectionDecisions(2, 2, new[] { inspProg });
      CheckDecision(2, "insp1", expandedCounter2, false, now);
      ExpectPathToBe(2, "insp1", new[] {
        new MaterialProcessActualPath() {
          MaterialID = 2,
          Process = 1,
          Pallet = "P5",
          LoadStation = 6,
          Stops = {
            new MaterialProcessActualPath.Stop() {StationName = "MC", StationNum = 15},
            new MaterialProcessActualPath.Stop() {StationName = "MC", StationNum = 16}
          },
          UnloadStation = 8,
        },
        new MaterialProcessActualPath() {
          MaterialID = 2,
          Process = 2,
          Pallet = "P4",
          LoadStation = 7,
          Stops = {
            new MaterialProcessActualPath.Stop() {StationName = "MC", StationNum = 18},
            new MaterialProcessActualPath.Stop() {StationName = "MC", StationNum = 19}
          },
          UnloadStation = 9,
        }
      });

      Assert.Equal(2, _insp.LoadInspectCounts().Count);
      CheckCount(expandedCounter1, 1);
      CheckCount(expandedCounter2, 1);
    }

    [Fact]
    public void WithoutInspectProgram()
    {
      DateTime now = DateTime.UtcNow;
      var mat1 = new EventLogMaterial() { MaterialID = 1, Process = 1 };
      var mat2 = new EventLogMaterial() { MaterialID = 2, Process = 1 };
      _insp.ForceInspection(mat1, "myinspection", true, now);
      _insp.ForceInspection(mat2, "myinspection", false, now);

      _insp.MakeInspectionDecisions(1, 1, null, now);
      _insp.MakeInspectionDecisions(2, 1, null, now);

      CheckDecision(1, "myinspection", "", true, now, true);
      CheckDecision(2, "myinspection", "", false, now, true);
    }

    private DateTime _lastCycleTime;
    private void AddCycle(LogMaterial[] mat, string pal, LogType loc, int statNum, bool end)
    {
      string name = loc == LogType.MachineCycle ? "MC" : "Load";
      ((Repository)_insp).AddLogEntryFromUnitTest(new LogEntry(-1, mat, pal, loc, name, statNum, "", true, _lastCycleTime, "", end));
      _lastCycleTime = _lastCycleTime.AddMinutes(15);
      ((Repository)_insp).AddLogEntryFromUnitTest(new LogEntry(-1, mat, pal, loc, name, statNum, "", false, _lastCycleTime, "", end));
      _lastCycleTime = _lastCycleTime.AddMinutes(15);
    }

    private void CheckDecision(long matID, string iType, string counter, bool inspect, DateTime now, bool forced = false)
    {
      int decisionCnt = 0;
      int forcedCnt = 0;
      foreach (var d in _insp.LookupInspectionDecisions(matID))
      {
        if (d.InspType == iType)
        {
          d.Should().BeEquivalentTo(
              new Decision()
              {
                MaterialID = matID,
                InspType = iType,
                Counter = d.Forced ? "" : counter,
                Inspect = inspect,
                Forced = d.Forced,
                CreateUTC = now
              },
              options =>
                options
                  .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, 1000))
                    .WhenTypeIs<DateTime>()
            );
          if (d.Forced) forcedCnt += 1; else decisionCnt += 1;
        }
      }
      Assert.Equal(1, decisionCnt);
      Assert.Equal(forcedCnt, forced ? 1 : 0);

      var log = _insp.GetLogForMaterial(matID);
      int inspEntries = 0;
      int forceEntries = 0;
      foreach (var entry in _insp.GetLogForMaterial(matID))
      {
        if (entry.LogType == LogType.Inspection && entry.ProgramDetails["InspectionType"] == iType)
        {
          inspEntries += 1;
          entry.EndTimeUTC.Should().BeCloseTo(now, 1000);
          entry.Program.Should().Be(counter);
          entry.Result.Should().Be(inspect.ToString());
        }
        else if (entry.LogType == LogType.InspectionForce && entry.Program == iType)
        {
          forceEntries += 1;
          entry.Result.Should().Be(inspect.ToString());
        }
      }
      inspEntries.Should().Be(1);
      forceEntries.Should().Be(forced ? 1 : 0);
    }

    private void ExpectPathToBe(long matID, string iType, IEnumerable<MaterialProcessActualPath> expected)
    {
      bool foundEntry = false;
      foreach (var entry in _insp.GetLogForMaterial(matID))
      {
        if (entry.LogType == LogType.Inspection && entry.ProgramDetails["InspectionType"] == iType)
        {
          foundEntry = true;
          var path = Newtonsoft.Json.JsonConvert.DeserializeObject<List<MaterialProcessActualPath>>(
            entry.ProgramDetails["ActualPath"]);
          path.Should().BeEquivalentTo(expected);
          break;
        }
      }
      Assert.True(foundEntry, "Unable to find inspection path");
    }

    private bool FindDecision(long matID, string iType, string counter)
    {
      foreach (var d in _insp.LookupInspectionDecisions(matID))
      {
        if (d.Counter == counter && d.InspType == iType)
        {
          return d.Inspect;
        }
      }
      Assert.True(false, "Unable to find counter and inspection type");
      return false;
    }

    private void CheckCount(string counter, int val)
    {
      foreach (var c in _insp.LoadInspectCounts())
      {
        if (c.Counter == counter)
        {
          Assert.Equal(val, c.Value);
          return;
        }
      }
      Assert.True(false, "Unable to find counter " + counter);
    }

    private void CheckLastUTC(string counter, DateTime val)
    {
      foreach (var c in _insp.LoadInspectCounts())
      {
        if (c.Counter == counter)
        {
          if (val == DateTime.MaxValue)
            Assert.Equal(DateTime.MaxValue, c.LastUTC);
          else
            Assert.True(5 >= Math.Abs(val.Subtract(c.LastUTC).TotalMinutes));
          return;
        }
      }
      Assert.True(false, "Unable to find counter");
    }
  }
}
