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
using System.Collections.Immutable;
using System.Text.Json;
using BlackMaple.MachineFramework;
using FluentAssertions;
using Xunit;

namespace MachineWatchTest
{
  public class InspectionTest : IDisposable
  {
    private RepositoryConfig _repoCfg;

    public InspectionTest()
    {
      _repoCfg = RepositoryConfig.InitializeMemoryDB(
        new SerialSettings() { ConvertMaterialIDToSerial = (id) => id.ToString() }
      );
    }

    void IDisposable.Dispose()
    {
      _repoCfg.CloseMemoryConnection();
    }

    [Fact]
    public void Counts()
    {
      using var _insp = _repoCfg.OpenConnection();
      List<InspectCount> cnts = new List<InspectCount>();

      InspectCount cnt = new InspectCount()
      {
        Counter = "Test1",
        Value = 15,
        LastUTC = DateTime.Parse("1/5/2009 4:23:12 GMT"),
      };
      cnts.Add(cnt);

      cnt = new InspectCount()
      {
        Counter = "Test2",
        Value = 1563,
        LastUTC = DateTime.Parse("1/15/2009 3:35:24 GMT"),
      };
      cnts.Add(cnt);

      cnt = new InspectCount()
      {
        Counter = "Test3",
        Value = 532,
        LastUTC = DateTime.Parse("2/12/2009 15:03:55 GMT"),
      };
      cnts.Add(cnt);

      _insp.SetInspectCounts(cnts);

      IList<InspectCount> loaded = _insp.LoadInspectCounts();

      Assert.Equal(loaded, cnts);
    }

    [Fact]
    public void Frequencies()
    {
      using var _insp = _repoCfg.OpenConnection();
      var freqProg = new PathInspection()
      {
        InspectionType = "insp1",
        Counter = "counter1",
        RandomFreq = 0.5,
        MaxVal = 0,
        TimeInterval = TimeSpan.FromHours(100)
      };

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
      using var _insp = _repoCfg.OpenConnection();
      var now = DateTime.UtcNow;
      //set the count as zero, otherwise it chooses a random
      InspectCount cnt = new InspectCount()
      {
        Counter = "counter1",
        Value = 0,
        LastUTC = DateTime.UtcNow.AddHours(-11).AddMinutes(2),
      };
      _insp.SetInspectCounts(new InspectCount[] { cnt });

      //set up a program
      var inspProg = new PathInspection()
      {
        InspectionType = "insp1",
        Counter = "counter1",
        MaxVal = 3,
        RandomFreq = 0,
        TimeInterval = TimeSpan.FromHours(11)
      };

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
      cnt = new InspectCount()
      {
        Counter = "counter1",
        Value = 0,
        LastUTC = DateTime.UtcNow.AddHours(-11).AddMinutes(-2),
      };
      _insp.SetInspectCounts(new InspectCount[] { cnt });

      _insp.MakeInspectionDecisions(4, 2, new[] { inspProg });
      CheckDecision(4, "insp1", "counter1", true, now);
      CheckLastUTC("counter1", now);

      CheckDecisions(new[] { 1L, 2, 3, 4 }, "insp1", "counter1", new[] { false, false, true, true }, now);
    }

    [Fact]
    public void ForcedInspection()
    {
      using var _insp = _repoCfg.OpenConnection();
      DateTime now = DateTime.UtcNow;

      //set up a program
      var inspProg = new PathInspection()
      {
        InspectionType = "insp1",
        Counter = "counter1",
        MaxVal = 13,
        RandomFreq = 0,
        TimeInterval = TimeSpan.FromHours(11)
      };

      //set the count as zero, otherwise it chooses a random
      InspectCount cnt = new InspectCount()
      {
        Counter = "counter1",
        Value = 0,
        LastUTC = DateTime.UtcNow.AddHours(-10),
      };
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
      using var _insp = _repoCfg.OpenConnection();
      DateTime now = DateTime.UtcNow;

      var insps = ImmutableList.Create(
        new PathInspection()
        {
          InspectionType = "insp1",
          Counter = "counter1",
          MaxVal = 3,
          RandomFreq = 0,
          TimeInterval = TimeSpan.FromHours(11)
        }
      );

      //set the count as zero, otherwise it chooses a random
      InspectCount cnt = new InspectCount()
      {
        Counter = "counter1",
        Value = 0,
        LastUTC = DateTime.UtcNow.AddHours(-10),
      };
      _insp.SetInspectCounts(new InspectCount[] { cnt });

      PalletLocation palLoc = new PalletLocation(PalletLocationEnum.Machine, "MC", 1);

      _insp.NextPieceInspection(palLoc, "insp1");
      _insp.CheckMaterialForNextPeiceInspection(palLoc, 1);

      CheckCount("counter1", 0);

      _insp.MakeInspectionDecisions(1, 1, insps, now);
      CheckCount("counter1", 1);
      CheckDecision(1, "insp1", "counter1", true, now, true);
    }

    [Fact]
    public void TranslateCounter()
    {
      using var _insp = _repoCfg.OpenConnection();
      var counter =
        "counter1-"
        + PathInspection.LoadFormatFlag(1)
        + "-"
        + PathInspection.UnloadFormatFlag(1)
        + "-"
        + PathInspection.LoadFormatFlag(2)
        + "-"
        + PathInspection.UnloadFormatFlag(2)
        + "-"
        + PathInspection.PalletFormatFlag(1)
        + "-"
        + PathInspection.PalletFormatFlag(2)
        + "-"
        + PathInspection.StationFormatFlag(1, 1)
        + "-"
        + PathInspection.StationFormatFlag(1, 2)
        + "-"
        + PathInspection.StationFormatFlag(2, 1)
        + "-"
        + PathInspection.StationFormatFlag(2, 2);

      var expandedCounter1 = "counter1-1-2-3-4-1-2-10-11-12-13";
      var expandedCounter2 = "counter1-6-8-7-9-5-4-15-16-18-19";

      //set the count as zero, otherwise it chooses a random
      var cnt = new InspectCount()
      {
        Counter = expandedCounter1,
        Value = 0,
        LastUTC = DateTime.UtcNow.AddHours(-10),
      };
      var cnt2 = new InspectCount()
      {
        Counter = expandedCounter2,
        Value = 0,
        LastUTC = DateTime.UtcNow.AddHours(-10),
      };
      _insp.SetInspectCounts(new[] { cnt, cnt2 });

      var mat1Proc1 = new[] { MkLogMat.Mk(1, "job1", 1, "part1", 2, "", "", "") };
      var mat1Proc2 = new[] { MkLogMat.Mk(1, "job1", 2, "part1", 2, "", "", "") };
      var mat2Proc1 = new[] { MkLogMat.Mk(2, "job1", 1, "part1", 2, "", "", "") };
      var mat2Proc2 = new[] { MkLogMat.Mk(2, "job1", 2, "part1", 2, "", "", "") };

      _lastCycleTime = DateTime.UtcNow.AddDays(-1);

      AddCycle(mat1Proc1, 1, LogType.LoadUnloadCycle, 1);
      AddCycle(mat2Proc1, 5, LogType.LoadUnloadCycle, 6);
      AddCycle(mat1Proc1, 1, LogType.MachineCycle, 10);
      AddCycle(mat2Proc1, 5, LogType.MachineCycle, 15);
      AddCycle(mat1Proc1, 1, LogType.MachineCycle, 11);
      AddCycle(mat2Proc1, 5, LogType.MachineCycle, 16);
      AddCycle(mat1Proc1, 1, LogType.LoadUnloadCycle, 2);
      AddCycle(mat2Proc1, 5, LogType.LoadUnloadCycle, 8);

      AddCycle(mat1Proc2, 2, LogType.LoadUnloadCycle, 3);
      AddCycle(mat2Proc2, 4, LogType.LoadUnloadCycle, 7);
      AddCycle(mat1Proc2, 2, LogType.MachineCycle, 12);
      AddCycle(mat2Proc2, 4, LogType.MachineCycle, 18);
      AddCycle(mat1Proc2, 2, LogType.MachineCycle, 13);
      AddCycle(mat2Proc2, 4, LogType.MachineCycle, 19);
      AddCycle(mat1Proc2, 2, LogType.LoadUnloadCycle, 4);
      AddCycle(mat2Proc2, 4, LogType.LoadUnloadCycle, 9);

      var inspProg = new PathInspection()
      {
        InspectionType = "insp1",
        Counter = counter,
        MaxVal = 10,
        RandomFreq = 0,
        TimeInterval = TimeSpan.FromDays(2)
      };

      var now = DateTime.UtcNow;
      _insp.MakeInspectionDecisions(1, 2, new[] { inspProg }, now);
      CheckDecision(1, "insp1", expandedCounter1, false, now);
      Assert.Equal(2, _insp.LoadInspectCounts().Count);
      CheckCount(expandedCounter1, 1);
      CheckCount(expandedCounter2, 0);
      ExpectPathToBe(
        1,
        "insp1",
        new[]
        {
          new MaterialProcessActualPath()
          {
            MaterialID = 1,
            Process = 1,
            Pallet = 1,
            LoadStation = 1,
            Stops = ImmutableList.Create(
              new MaterialProcessActualPath.Stop() { StationName = "MC", StationNum = 10 },
              new MaterialProcessActualPath.Stop() { StationName = "MC", StationNum = 11 }
            ),
            UnloadStation = 2,
          },
          new MaterialProcessActualPath()
          {
            MaterialID = 1,
            Process = 2,
            Pallet = 2,
            LoadStation = 3,
            Stops = ImmutableList.Create(
              new MaterialProcessActualPath.Stop() { StationName = "MC", StationNum = 12 },
              new MaterialProcessActualPath.Stop() { StationName = "MC", StationNum = 13 }
            ),
            UnloadStation = 4,
          }
        }
      );

      _insp.MakeInspectionDecisions(2, 2, new[] { inspProg });
      CheckDecision(2, "insp1", expandedCounter2, false, now);
      ExpectPathToBe(
        2,
        "insp1",
        new[]
        {
          new MaterialProcessActualPath()
          {
            MaterialID = 2,
            Process = 1,
            Pallet = 5,
            LoadStation = 6,
            Stops = ImmutableList.Create(
              new MaterialProcessActualPath.Stop() { StationName = "MC", StationNum = 15 },
              new MaterialProcessActualPath.Stop() { StationName = "MC", StationNum = 16 }
            ),
            UnloadStation = 8,
          },
          new MaterialProcessActualPath()
          {
            MaterialID = 2,
            Process = 2,
            Pallet = 4,
            LoadStation = 7,
            Stops = ImmutableList.Create(
              new MaterialProcessActualPath.Stop() { StationName = "MC", StationNum = 18 },
              new MaterialProcessActualPath.Stop() { StationName = "MC", StationNum = 19 }
            ),
            UnloadStation = 9,
          }
        }
      );

      Assert.Equal(2, _insp.LoadInspectCounts().Count);
      CheckCount(expandedCounter1, 1);
      CheckCount(expandedCounter2, 1);
    }

    [Fact]
    public void WithoutInspectProgram()
    {
      using var _insp = _repoCfg.OpenConnection();
      DateTime now = DateTime.UtcNow;
      var mat1 = new EventLogMaterial()
      {
        MaterialID = 1,
        Process = 1,
        Face = ""
      };
      var mat2 = new EventLogMaterial()
      {
        MaterialID = 2,
        Process = 1,
        Face = ""
      };
      _insp.ForceInspection(mat1, "myinspection", true, now);
      _insp.ForceInspection(mat2, "myinspection", false, now);

      _insp.MakeInspectionDecisions(1, 1, null, now);
      _insp.MakeInspectionDecisions(2, 1, null, now);

      CheckDecision(1, "myinspection", "", true, now, true);
      CheckDecision(2, "myinspection", "", false, now, true);
      CheckDecisions(new[] { 1L, 2L }, "myinspection", "", new[] { true, false }, now, true);
    }

    private DateTime _lastCycleTime;

    private void AddCycle(LogMaterial[] mat, int pal, LogType loc, int statNum)
    {
      using var _insp = _repoCfg.OpenConnection();
      string name = loc == LogType.MachineCycle ? "MC" : "Load";
      ((Repository)_insp).AddLogEntryFromUnitTest(
        new LogEntry(-1, mat, pal, loc, name, statNum, "", true, _lastCycleTime, "")
      );
      _lastCycleTime = _lastCycleTime.AddMinutes(15);
      ((Repository)_insp).AddLogEntryFromUnitTest(
        new LogEntry(-1, mat, pal, loc, name, statNum, "", false, _lastCycleTime, "")
      );
      _lastCycleTime = _lastCycleTime.AddMinutes(15);
    }

    private void CheckDecision(
      long matID,
      string iType,
      string counter,
      bool inspect,
      DateTime now,
      bool forced = false
    )
    {
      using var _insp = _repoCfg.OpenConnection();
      CheckDecision(matID, _insp.LookupInspectionDecisions(matID), iType, counter, inspect, now, forced);
    }

    private void CheckDecisions(
      IReadOnlyList<long> mats,
      string iType,
      string counter,
      IReadOnlyList<bool> inspect,
      DateTime now,
      bool forced = false
    )
    {
      using var _insp = _repoCfg.OpenConnection();
      var insps = _insp.LookupInspectionDecisions(mats);
      insps.Keys.Should().BeEquivalentTo(mats);
      for (var i = 0; i < mats.Count; i++)
      {
        CheckDecision(mats[i], insps[mats[i]], iType, counter, inspect[i], now, forced);
      }
    }

    private void CheckDecision(
      long matID,
      IReadOnlyList<Decision> decisions,
      string iType,
      string counter,
      bool inspect,
      DateTime now,
      bool forced = false
    )
    {
      int decisionCnt = 0;
      int forcedCnt = 0;
      foreach (var d in decisions)
      {
        if (d.InspType == iType)
        {
          d.Should()
            .BeEquivalentTo(
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
                  .ComparingByMembers<Decision>()
                  .Using<DateTime>(ctx =>
                    ctx.Subject.Should().BeCloseTo(ctx.Expectation, TimeSpan.FromSeconds(4))
                  )
                  .WhenTypeIs<DateTime>()
            );
          if (d.Forced)
            forcedCnt += 1;
          else
            decisionCnt += 1;
        }
      }
      Assert.Equal(1, decisionCnt);
      Assert.Equal(forcedCnt, forced ? 1 : 0);

      using var _insp = _repoCfg.OpenConnection();
      var log = _insp.GetLogForMaterial(matID);
      int inspEntries = 0;
      int forceEntries = 0;
      foreach (var entry in _insp.GetLogForMaterial(matID))
      {
        if (entry.LogType == LogType.Inspection && entry.ProgramDetails["InspectionType"] == iType)
        {
          inspEntries += 1;
          entry.EndTimeUTC.Should().BeCloseTo(now, TimeSpan.FromSeconds(4));
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
      using var _insp = _repoCfg.OpenConnection();
      bool foundEntry = false;
      foreach (var entry in _insp.GetLogForMaterial(matID))
      {
        if (entry.LogType == LogType.Inspection && entry.ProgramDetails["InspectionType"] == iType)
        {
          foundEntry = true;
          var path = JsonSerializer.Deserialize<List<MaterialProcessActualPath>>(
            entry.ProgramDetails["ActualPath"]
          );
          path.Should()
            .BeEquivalentTo(expected, options => options.ComparingByMembers<MaterialProcessActualPath>());
          break;
        }
      }
      Assert.True(foundEntry, "Unable to find inspection path");
    }

    private bool FindDecision(long matID, string iType, string counter)
    {
      using var _insp = _repoCfg.OpenConnection();
      foreach (var d in _insp.LookupInspectionDecisions(matID))
      {
        if (d.Counter == counter && d.InspType == iType)
        {
          return d.Inspect;
        }
      }
      Assert.Fail("Unable to find counter and inspection type");
      return false;
    }

    private void CheckCount(string counter, int val)
    {
      using var _insp = _repoCfg.OpenConnection();
      foreach (var c in _insp.LoadInspectCounts())
      {
        if (c.Counter == counter)
        {
          Assert.Equal(val, c.Value);
          return;
        }
      }
      Assert.Fail("Unable to find counter " + counter);
    }

    private void CheckLastUTC(string counter, DateTime val)
    {
      using var _insp = _repoCfg.OpenConnection();
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
      Assert.Fail("Unable to find counter");
    }
  }
}
