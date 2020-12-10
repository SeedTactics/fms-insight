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
using System.Collections.Generic;
using System.Linq;
using Xunit;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;
using FluentAssertions;
using AutoFixture;

namespace MachineWatchTest
{
  public class JobComparisonHelpers
  {
    public static void CheckEqual(LogEntry x, LogEntry y)
    {
      x.Should().BeEquivalentTo(y, options =>
        options.Excluding(l => l.Counter)
      );
    }
  }

  public class JobLogTest : JobComparisonHelpers, IDisposable
  {
    private EventLogDB _jobLog;

    public JobLogTest()
    {
      _jobLog = EventLogDB.Config.InitializeSingleThreadedMemoryDB(new FMSSettings()).OpenConnection();
    }

    public void Dispose()
    {
      _jobLog.Close();
    }

    [Fact]
    public void MaterialIDs()
    {
      long m1 = _jobLog.AllocateMaterialID("U1", "P1", 52);
      long m2 = _jobLog.AllocateMaterialID("U2", "P2", 66);
      long m3 = _jobLog.AllocateMaterialID("U3", "P3", 566);
      m1.Should().Be(1);
      m2.Should().Be(2);
      m3.Should().Be(3);

      _jobLog.RecordPathForProcess(m1, 1, 60);
      _jobLog.RecordPathForProcess(m1, 2, 88);
      _jobLog.RecordPathForProcess(m2, 6, 5);
      _jobLog.RecordPathForProcess(m2, 6, 10);

      _jobLog.GetMaterialDetails(m1).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = m1,
        JobUnique = "U1",
        PartName = "P1",
        NumProcesses = 52,
        Paths = new Dictionary<int, int> { { 1, 60 }, { 2, 88 } }
      });

      _jobLog.GetMaterialDetails(m2).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = m2,
        JobUnique = "U2",
        PartName = "P2",
        NumProcesses = 66,
        Paths = new Dictionary<int, int> { { 6, 10 } }
      });

      _jobLog.GetMaterialDetails(m3).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = m3,
        JobUnique = "U3",
        PartName = "P3",
        NumProcesses = 566,
        Paths = new Dictionary<int, int>()
      });

      long m4 = _jobLog.AllocateMaterialIDForCasting("P4");
      _jobLog.GetMaterialDetails(m4).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = m4,
        PartName = "P4",
        NumProcesses = 1,
        Paths = new Dictionary<int, int>()
      });

      _jobLog.SetDetailsForMaterialID(m4, "U4", "P4444", 77);
      _jobLog.GetMaterialDetails(m4).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = m4,
        JobUnique = "U4",
        PartName = "P4444",
        NumProcesses = 77,
        Paths = new Dictionary<int, int>()
      });

      _jobLog.GetWorkordersForUnique("U1").Should().BeEmpty();

      _jobLog.RecordWorkorderForMaterialID(m1, 1, "work1");
      _jobLog.RecordWorkorderForMaterialID(m2, 1, "work2");
      _jobLog.RecordWorkorderForMaterialID(m3, 1, "work1");

      _jobLog.GetMaterialForWorkorder("work1").Should().BeEquivalentTo(new[] {
        new MaterialDetails
        {
          MaterialID = m1,
          JobUnique = "U1",
          PartName = "P1",
          NumProcesses = 52,
          Workorder = "work1",
          Paths = new Dictionary<int, int> { { 1, 60 }, { 2, 88 } }
        },
        new MaterialDetails() {
          MaterialID = m3,
          JobUnique = "U3",
          PartName = "P3",
          NumProcesses = 566,
          Workorder = "work1",
          Paths = new Dictionary<int, int>()
        }
      });

      _jobLog.GetWorkordersForUnique("U1").Should().BeEquivalentTo(new[] { "work1" });
      _jobLog.GetWorkordersForUnique("unused").Should().BeEmpty();

      _jobLog.GetMaterialForJobUnique("U1").Should().BeEquivalentTo(new[] {
        new MaterialDetails
        {
          MaterialID = m1,
          JobUnique = "U1",
          PartName = "P1",
          NumProcesses = 52,
          Workorder = "work1",
          Paths = new Dictionary<int, int> { { 1, 60 }, { 2, 88 } }
        },
      });

      _jobLog.GetMaterialForJobUnique("unused").Should().BeEmpty();
    }

    [Fact]
    public void AddLog()
    {
      Assert.Equal(DateTime.MinValue, _jobLog.MaxLogDate());

      System.DateTime start = DateTime.UtcNow.AddHours(-10);

      List<LogEntry> logs = new List<LogEntry>();
      var logsForMat1 = new List<LogEntry>();
      var logsForMat2 = new List<LogEntry>();

      LogMaterial mat1 = new LogMaterial(
          _jobLog.AllocateMaterialID("grgaegr", "pp2", 23), "grgaegr", 7, "pp2", 23, "", "", "face22");
      LogMaterial mat19 = new LogMaterial(
          _jobLog.AllocateMaterialID("unique", "pp1", 53), "unique", 2, "pp1", 53, "", "", "face55");

      var loadStartActualCycle = _jobLog.RecordLoadStart(
          mats: new[] { mat1, mat19 }.Select(EventLogDB.EventLogMaterial.FromLogMat),
          pallet: "pal",
          lulNum: 2,
          timeUTC: start.AddHours(1)
      );
      loadStartActualCycle.Should().BeEquivalentTo(
          new LogEntry(
              loadStartActualCycle.Counter, new LogMaterial[] { mat1, mat19 }, "pal",
              LogType.LoadUnloadCycle, "L/U", 2,
              "LOAD", true, start.AddHours(1), "LOAD", false)
      );
      logs.Add(loadStartActualCycle);
      logsForMat1.Add(loadStartActualCycle);

      var mat2 = new LogMaterial(
          _jobLog.AllocateMaterialID("ahre", "gewoiweg", 13), "ahre", 1, "gewoiweg", 13, "", "", "");
      var mat15 = new LogMaterial(
          _jobLog.AllocateMaterialID("qghr4e", "ppp532", 14), "qghr4e", 3, "ppp532", 14, "", "", "");

      var loadEndActualCycle = _jobLog.RecordLoadEnd(
          mats: new[] { mat2, mat15 }.Select(EventLogDB.EventLogMaterial.FromLogMat),
          pallet: "aaaa",
          lulNum: 16,
          timeUTC: start.AddHours(3),
          elapsed: TimeSpan.FromMinutes(52),
          active: TimeSpan.FromMinutes(25)
      );
      loadEndActualCycle.Should().BeEquivalentTo(new[] {
                new LogEntry(
                    loadEndActualCycle.First().Counter, new LogMaterial[] { mat2, mat15 }, "aaaa",
                    LogType.LoadUnloadCycle, "L/U", 16,
                    "LOAD", false, start.AddHours(3), "LOAD", false,
                    TimeSpan.FromMinutes(52), TimeSpan.FromMinutes(25))
            });
      logs.Add(loadEndActualCycle.First());
      logsForMat2.Add(loadEndActualCycle.First());
      _jobLog.ToolPocketSnapshotForCycle(loadEndActualCycle.First().Counter).Should().BeEmpty();

      var arriveStocker = _jobLog.RecordPalletArriveStocker(
        mats: new[] { mat2 }.Select(EventLogDB.EventLogMaterial.FromLogMat),
        pallet: "bbbb",
        stockerNum: 23,
        waitForMachine: true,
        timeUTC: start.AddHours(4)
      );
      arriveStocker.Should().BeEquivalentTo(
        new LogEntry(
          -1,
          mat: new[] { mat2 },
          pal: "bbbb",
          ty: LogType.PalletInStocker,
          locName: "Stocker",
          locNum: 23,
          prog: "Arrive",
          start: true,
          endTime: start.AddHours(4),
          result: "WaitForMachine",
          endOfRoute: false
        ),
        options => options.Excluding(l => l.Counter)
      );
      logs.Add(arriveStocker);
      logsForMat2.Add(arriveStocker);

      var departStocker = _jobLog.RecordPalletDepartStocker(
        mats: new[] { mat2, mat15 }.Select(EventLogDB.EventLogMaterial.FromLogMat),
        pallet: "cccc",
        stockerNum: 34,
        waitForMachine: true,
        timeUTC: start.AddHours(4).AddMinutes(10),
        elapsed: TimeSpan.FromMinutes(10)
      );
      departStocker.Should().BeEquivalentTo(
        new LogEntry(
          -1,
          mat: new[] { mat2, mat15 },
          pal: "cccc",
          ty: LogType.PalletInStocker,
          locName: "Stocker",
          locNum: 34,
          prog: "Depart",
          start: false,
          endTime: start.AddHours(4).AddMinutes(10),
          result: "WaitForMachine",
          elapsed: TimeSpan.FromMinutes(10),
          active: TimeSpan.Zero,
          endOfRoute: false
        ),
        options => options.Excluding(l => l.Counter)
      );
      logs.Add(departStocker);
      logsForMat2.Add(departStocker);

      var arriveInbound = _jobLog.RecordPalletArriveRotaryInbound(
        mats: new[] { mat15 }.Select(EventLogDB.EventLogMaterial.FromLogMat),
        pallet: "bbbb",
        statName: "thestat",
        statNum: 77,
        timeUTC: start.AddHours(4).AddMinutes(20)
      );
      arriveInbound.Should().BeEquivalentTo(
        new LogEntry(
          -1,
          mat: new[] { mat15 },
          pal: "bbbb",
          ty: LogType.PalletOnRotaryInbound,
          locName: "thestat",
          locNum: 77,
          prog: "Arrive",
          start: true,
          endTime: start.AddHours(4).AddMinutes(20),
          result: "Arrive",
          endOfRoute: false
        ),
        options => options.Excluding(l => l.Counter)
      );
      logs.Add(arriveInbound);

      var departInbound = _jobLog.RecordPalletDepartRotaryInbound(
        mats: new[] { mat15 }.Select(EventLogDB.EventLogMaterial.FromLogMat),
        pallet: "dddd",
        statName: "thestat2",
        statNum: 88,
        rotateIntoWorktable: true,
        timeUTC: start.AddHours(4).AddMinutes(45),
        elapsed: TimeSpan.FromMinutes(25)
      );
      departInbound.Should().BeEquivalentTo(
        new LogEntry(
          -1,
          mat: new[] { mat15 },
          pal: "dddd",
          ty: LogType.PalletOnRotaryInbound,
          locName: "thestat2",
          locNum: 88,
          prog: "Depart",
          result: "RotateIntoWorktable",
          start: false,
          endTime: start.AddHours(4).AddMinutes(45),
          elapsed: TimeSpan.FromMinutes(25),
          active: TimeSpan.Zero,
          endOfRoute: false
        ),
        options => options.Excluding(l => l.Counter)
      );
      logs.Add(departInbound);

      var machineStartPockets = new List<EventLogDB.ToolPocketSnapshot> {
        new EventLogDB.ToolPocketSnapshot() {
          PocketNumber = 10, Tool = "tool1", CurrentUse = TimeSpan.FromSeconds(10), ToolLife = TimeSpan.FromMinutes(20)
        },
        new EventLogDB.ToolPocketSnapshot() {
          PocketNumber = 20, Tool = "tool2", CurrentUse = TimeSpan.FromSeconds(20), ToolLife = TimeSpan.FromMinutes(40)
        }
      };
      var machineStartActualCycle = _jobLog.RecordMachineStart(
          mats: new[] { mat15 }.Select(EventLogDB.EventLogMaterial.FromLogMat),
          pallet: "rrrr",
          statName: "ssssss",
          statNum: 152,
          program: "progggg",
          pockets: machineStartPockets,
          timeUTC: start.AddHours(5).AddMinutes(10)
      );
      machineStartActualCycle.Should().BeEquivalentTo(
          new LogEntry(
              machineStartActualCycle.Counter, new LogMaterial[] { mat15 }, "rrrr",
              LogType.MachineCycle, "ssssss", 152,
              "progggg", true, start.AddHours(5).AddMinutes(10), "", false)
      );
      logs.Add(machineStartActualCycle);
      _jobLog.ToolPocketSnapshotForCycle(machineStartActualCycle.Counter).Should().BeEquivalentTo(machineStartPockets);

      var machineEndActualCycle = _jobLog.RecordMachineEnd(
          mats: new[] { mat2 }.Select(EventLogDB.EventLogMaterial.FromLogMat),
          pallet: "www",
          statName: "xxx",
          statNum: 177,
          program: "progggg",
          result: "4444",
          timeUTC: start.AddHours(5).AddMinutes(19),
          elapsed: TimeSpan.FromMinutes(12),
          active: TimeSpan.FromMinutes(99),
          extraData: new Dictionary<string, string>() {
                    {"aa", "AA"}, {"bb", "BB"}
          },
          tools: new Dictionary<string, ToolUse>() {
            {"tool1", new ToolUse() {
                ToolUseDuringCycle = TimeSpan.FromMinutes(10),
                TotalToolUseAtEndOfCycle = TimeSpan.FromMinutes(20),
                ConfiguredToolLife = TimeSpan.FromMinutes(30)}
            },
            {"tool2", new ToolUse() {
                ToolUseDuringCycle = TimeSpan.FromMinutes(40),
                TotalToolUseAtEndOfCycle = TimeSpan.FromMinutes(50),
                ConfiguredToolLife = TimeSpan.FromMinutes(60)}
            },
          }
      );
      var machineEndExpectedCycle =
          new LogEntry(
              machineEndActualCycle.Counter, new LogMaterial[] { mat2 }, "www",
              LogType.MachineCycle, "xxx", 177,
              "progggg", false, start.AddHours(5).AddMinutes(19), "4444", false,
              TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(99));
      machineEndExpectedCycle.ProgramDetails["aa"] = "AA";
      machineEndExpectedCycle.ProgramDetails["bb"] = "BB";
      machineEndExpectedCycle.Tools["tool1"] = new ToolUse()
      {
        ToolUseDuringCycle = TimeSpan.FromMinutes(10),
        TotalToolUseAtEndOfCycle = TimeSpan.FromMinutes(20),
        ConfiguredToolLife = TimeSpan.FromMinutes(30)
      };
      machineEndExpectedCycle.Tools["tool2"] = new ToolUse()
      {
        ToolUseDuringCycle = TimeSpan.FromMinutes(40),
        TotalToolUseAtEndOfCycle = TimeSpan.FromMinutes(50),
        ConfiguredToolLife = TimeSpan.FromMinutes(60)
      };
      machineEndActualCycle.Should().BeEquivalentTo(machineEndExpectedCycle);
      logs.Add(machineEndActualCycle);
      logsForMat2.Add(machineEndActualCycle);

      var unloadStartActualCycle = _jobLog.RecordUnloadStart(
          mats: new[] { mat15, mat19 }.Select(EventLogDB.EventLogMaterial.FromLogMat),
          pallet: "rrr",
          lulNum: 87,
          timeUTC: start.AddHours(6).AddMinutes(10)
      );
      unloadStartActualCycle.Should().BeEquivalentTo(
          new LogEntry(
              unloadStartActualCycle.Counter, new LogMaterial[] { mat15, mat19 }, "rrr",
              LogType.LoadUnloadCycle, "L/U", 87,
              "UNLOAD", true, start.AddHours(6).AddMinutes(10), "UNLOAD", false)
      );
      logs.Add(unloadStartActualCycle);

      var unloadEndActualCycle = _jobLog.RecordUnloadEnd(
          mats: new[] { mat2, mat19 }.Select(EventLogDB.EventLogMaterial.FromLogMat),
          pallet: "bb",
          lulNum: 14,
          timeUTC: start.AddHours(7),
          elapsed: TimeSpan.FromMinutes(152),
          active: TimeSpan.FromMinutes(55)
      );
      unloadEndActualCycle.Should().BeEquivalentTo(new[] {
                new LogEntry(
                    unloadEndActualCycle.First().Counter, new LogMaterial[] { mat2, mat19 }, "bb",
                    LogType.LoadUnloadCycle, "L/U", 14,
                    "UNLOAD", false, start.AddHours(7), "UNLOAD", true,
                    TimeSpan.FromMinutes(152), TimeSpan.FromMinutes(55))
            });
      logs.Add(unloadEndActualCycle.First());
      logsForMat2.Add(unloadEndActualCycle.First());

      var newRawLog = _jobLog.RawAddLogEntries(new[] {
        new EventLogDB.NewEventLogEntry() {
          Material = Enumerable.Empty<EventLogDB.EventLogMaterial>(),
          LogType = LogType.GeneralMessage,
          EndTimeUTC = start.AddHours(6.8),
          LocationName = "CustomMsg",
          LocationNum = 1234,
          Pallet = "",
          Program = "",
          Result = "My Message"
        }
      });
      logs.AddRange(newRawLog);


      // ----- check loading of logs -----

      Assert.Equal(start.AddHours(7), _jobLog.MaxLogDate());

      IList<LogEntry> otherLogs = null;

      otherLogs = _jobLog.GetLogEntries(start, DateTime.UtcNow);
      CheckLog(logs, otherLogs, start);

      otherLogs = _jobLog.GetLogEntries(start.AddHours(5), DateTime.UtcNow);
      CheckLog(logs, otherLogs, start.AddHours(5));

      otherLogs = _jobLog.GetLog(loadEndActualCycle.First().Counter);
      CheckLog(logs, otherLogs, start.AddHours(4));

      otherLogs = _jobLog.GetLog(unloadStartActualCycle.Counter);
      CheckLog(logs, otherLogs, start.AddHours(6.5));

      otherLogs = _jobLog.GetLog(newRawLog.First().Counter);
      Assert.Equal(0, otherLogs.Count);

      foreach (var c in logs)
        Assert.True(_jobLog.CycleExists(c.EndTimeUTC, c.Pallet, c.LogType, c.LocationName, c.LocationNum), "Checking " + c.EndTimeUTC.ToString());

      Assert.False(_jobLog.CycleExists(
          DateTime.Parse("4/6/2011"), "Pallll", LogType.MachineCycle, "MC", 3));

      CheckLog(logsForMat1, _jobLog.GetLogForMaterial(1), start);
      CheckLog(logsForMat1.Concat(logsForMat2).ToList(), _jobLog.GetLogForMaterial(new[] { 1, mat2.MaterialID }), start);
      _jobLog.GetLogForMaterial(18).Should().BeEmpty();

      var markLog = _jobLog.RecordSerialForMaterialID(EventLogDB.EventLogMaterial.FromLogMat(mat1), "ser1");
      logsForMat1.Add(new LogEntry(-1, new LogMaterial[] { mat1 }, "",
                                       LogType.PartMark, "Mark", 1,
                                       "MARK", false, markLog.EndTimeUTC, "ser1", false));
      logsForMat1 = logsForMat1.Select(TransformLog(mat1.MaterialID, SetSerialInMat("ser1"))).ToList();
      logs = logs.Select(TransformLog(mat1.MaterialID, SetSerialInMat("ser1"))).ToList();
      mat1 = SetSerialInMat("ser1")(mat1);
      CheckLog(logsForMat1, _jobLog.GetLogForSerial("ser1"), start);
      _jobLog.GetLogForSerial("ser2").Should().BeEmpty();

      var orderLog = _jobLog.RecordWorkorderForMaterialID(EventLogDB.EventLogMaterial.FromLogMat(mat1), "work1");
      logsForMat1.Add(new LogEntry(-1, new LogMaterial[] { mat1 }, "",
                                       LogType.OrderAssignment, "Order", 1,
                                       "", false, orderLog.EndTimeUTC, "work1", false));
      logsForMat1 = logsForMat1.Select(TransformLog(mat1.MaterialID, SetWorkorderInMat("work1"))).ToList();
      logs = logs.Select(TransformLog(mat1.MaterialID, SetWorkorderInMat("work1"))).ToList();
      mat1 = SetWorkorderInMat("work1")(mat1);
      var finalize = _jobLog.RecordFinalizedWorkorder("work1");
      CheckLog(logsForMat1.Append(finalize).ToList(), _jobLog.GetLogForWorkorder("work1"), start);
      _jobLog.GetLogForWorkorder("work2").Should().BeEmpty();

      CheckLog(logsForMat1, _jobLog.GetLogForJobUnique(mat1.JobUniqueStr), start);
      _jobLog.GetLogForJobUnique("sofusadouf").Should().BeEmpty();

      //inspection, wash, and general
      var inspCompLog = _jobLog.RecordInspectionCompleted(
          EventLogDB.EventLogMaterial.FromLogMat(mat1), 5, "insptype1", true, new Dictionary<string, string> { { "a", "aaa" }, { "b", "bbb" } },
          TimeSpan.FromMinutes(100), TimeSpan.FromMinutes(5));
      var expectedInspLog = new LogEntry(-1, new LogMaterial[] { mat1 }, "",
          LogType.InspectionResult, "Inspection", 5, "insptype1", false, inspCompLog.EndTimeUTC, "True", false,
          TimeSpan.FromMinutes(100), TimeSpan.FromMinutes(5));
      expectedInspLog.ProgramDetails.Add("a", "aaa");
      expectedInspLog.ProgramDetails.Add("b", "bbb");
      logsForMat1.Add(expectedInspLog);

      var washLog = _jobLog.RecordWashCompleted(
          EventLogDB.EventLogMaterial.FromLogMat(mat1), 7, new Dictionary<string, string> { { "z", "zzz" }, { "y", "yyy" } },
          TimeSpan.FromMinutes(44), TimeSpan.FromMinutes(9));
      var expectedWashLog = new LogEntry(-1, new LogMaterial[] { mat1 }, "",
          LogType.Wash, "Wash", 7, "", false, washLog.EndTimeUTC, "", false,
          TimeSpan.FromMinutes(44), TimeSpan.FromMinutes(9));
      expectedWashLog.ProgramDetails.Add("z", "zzz");
      expectedWashLog.ProgramDetails.Add("y", "yyy");
      logsForMat1.Add(expectedWashLog);

      var generalLog = _jobLog.RecordGeneralMessage(EventLogDB.EventLogMaterial.FromLogMat(mat1), "The program msg", "The result msg",
                                                    extraData: new Dictionary<string, string> { { "extra1", "value1" } });
      var expectedGeneralLog = new LogEntry(-1, new LogMaterial[] { mat1 }, "",
          LogType.GeneralMessage, "Message", 1, "The program msg", false, generalLog.EndTimeUTC, "The result msg", false
      );
      expectedGeneralLog.ProgramDetails["extra1"] = "value1";
      logsForMat1.Add(expectedGeneralLog);

      var notesLog = _jobLog.RecordOperatorNotes(mat1.MaterialID, mat1.Process, "The notes content", "Opername");
      var expectedNotesLog = new LogEntry(-1, new LogMaterial[] {
                                                new LogMaterial(mat1.MaterialID, mat1.JobUniqueStr, mat1.Process, mat1.PartName, mat1.NumProcesses, mat1.Serial, mat1.Workorder, "") },
                                           "", LogType.GeneralMessage, "Message", 1, "OperatorNotes", false, notesLog.EndTimeUTC, "Operator Notes", false);
      expectedNotesLog.ProgramDetails["note"] = "The notes content";
      expectedNotesLog.ProgramDetails["operator"] = "Opername";
      logsForMat1.Add(expectedNotesLog);

      CheckLog(logsForMat1, _jobLog.GetLogForJobUnique(mat1.JobUniqueStr), start);
    }

    [Fact]
    public void LookupByPallet()
    {
      _jobLog.CurrentPalletLog("pal1").Should().BeEmpty();
      _jobLog.CurrentPalletLog("pal2").Should().BeEmpty();
      Assert.Equal(DateTime.MinValue, _jobLog.LastPalletCycleTime("pal1"));

      var pal1Initial = new List<LogEntry>();
      var pal1Cycle = new List<LogEntry>();
      var pal2Cycle = new List<LogEntry>();

      var mat1 = new LogMaterial(
          _jobLog.AllocateMaterialID("unique", "part1", 2), "unique", 1, "part1", 2, "", "", "face1");
      var mat2 = new LogMaterial(
          _jobLog.AllocateMaterialID("unique2", "part2", 2), "unique2", 2, "part2", 2, "", "", "face2");

      DateTime pal1InitialTime = DateTime.UtcNow.AddHours(-4);

      // *********** Add load cycle on pal1
      pal1Initial.Add(new LogEntry(0, new LogMaterial[] { mat1, mat2 },
          "pal1", LogType.LoadUnloadCycle, "Load", 2,
          "prog1",
          true, //start of event
          pal1InitialTime,
          "result",
          false)); //end of route
      pal1Initial.Add(new LogEntry(0, new LogMaterial[] { mat1, mat2 },
          "pal1", LogType.LoadUnloadCycle, "Load", 2,
          "prog1",
          false, //start of event
          pal1InitialTime.AddMinutes(5),
          "result",
          false)); //end of route

      // *********** Add machine cycle on pal1
      pal1Initial.Add(new LogEntry(0, new LogMaterial[] { mat1, mat2 },
          "pal1", LogType.MachineCycle, "MC", 2,
          "prog1",
          true, //start of event
          pal1InitialTime.AddMinutes(10),
          "result",
          false)); //end of route
      pal1Initial.Add(new LogEntry(0, new LogMaterial[] { mat1, mat2 },
          "pal1", LogType.MachineCycle, "MC", 2,
          "prog1",
          false, //start of event
          pal1InitialTime.AddMinutes(20),
          "result",
          true)); //end of route
                  // ***********  End of Route for pal1

      AddToDB(pal1Initial);

      CheckLog(pal1Initial, _jobLog.CurrentPalletLog("pal1"), DateTime.UtcNow.AddHours(-10));
      _jobLog.CurrentPalletLog("pal2").Should().BeEmpty();

      _jobLog.CompletePalletCycle("pal1", pal1InitialTime.AddMinutes(25), "");

      pal1Initial.Add(new LogEntry(0, new LogMaterial[] { }, "pal1", LogType.PalletCycle, "Pallet Cycle", 1,
          "", false, pal1InitialTime.AddMinutes(25), "PalletCycle", false, TimeSpan.Zero, TimeSpan.Zero));

      Assert.Equal(pal1InitialTime.AddMinutes(25), _jobLog.LastPalletCycleTime("pal1"));
      CheckLog(pal1Initial, _jobLog.GetLogEntries(DateTime.UtcNow.AddHours(-10), DateTime.UtcNow), DateTime.UtcNow.AddHours(-50));
      _jobLog.CurrentPalletLog("pal1").Should().BeEmpty();
      _jobLog.CurrentPalletLog("pal2").Should().BeEmpty();

      DateTime pal2CycleTime = DateTime.UtcNow.AddHours(-3);

      // *********** Add pal2 load event
      pal2Cycle.Add(new LogEntry(0, new LogMaterial[] { mat1, mat2 },
          "pal2", LogType.LoadUnloadCycle, "Load", 2,
          "prog1",
          true, //start of event
          pal2CycleTime,
          "result",
          false)); //end of route
      pal2Cycle.Add(new LogEntry(0, new LogMaterial[] { mat1, mat2 },
          "pal2", LogType.LoadUnloadCycle, "Load", 2,
          "prog1",
          false, //start of event
          pal2CycleTime.AddMinutes(10),
          "result",
          false)); //end of route

      AddToDB(pal2Cycle);

      _jobLog.CurrentPalletLog("pal1").Should().BeEmpty();
      CheckLog(pal2Cycle, _jobLog.CurrentPalletLog("pal2"), DateTime.UtcNow.AddHours(-10));

      DateTime pal1CycleTime = DateTime.UtcNow.AddHours(-2);

      // ********** Add pal1 load event
      pal1Cycle.Add(new LogEntry(0, new LogMaterial[] { mat1, mat2 },
          "pal1", LogType.LoadUnloadCycle, "Load", 2,
          "prog1",
          true, //start of event
          pal1CycleTime,
          "result",
          false)); //end of route
      pal1Cycle.Add(new LogEntry(0, new LogMaterial[] { mat1, mat2 },
          "pal1", LogType.LoadUnloadCycle, "Load", 2,
          "prog1",
          false, //start of event
          pal1CycleTime.AddMinutes(15),
          "result",
          false)); //end of route

      // *********** Add pal1 start of machining
      pal1Cycle.Add(new LogEntry(0, new LogMaterial[] { mat1, mat2 },
          "pal1", LogType.MachineCycle, "MC", 4,
          "prog1",
          true, //start of event
          pal1CycleTime.AddMinutes(20),
          "result",
          false)); //end of route

      AddToDB(pal1Cycle);

      CheckLog(pal1Cycle, _jobLog.CurrentPalletLog("pal1"), DateTime.UtcNow.AddHours(-10));
      CheckLog(pal2Cycle, _jobLog.CurrentPalletLog("pal2"), DateTime.UtcNow.AddHours(-10));

      //********  Complete the pal1 machining
      pal1Cycle.Add(new LogEntry(0, new LogMaterial[] { mat1, mat2 },
          "pal1", LogType.MachineCycle, "MC", 4,
          "prog1",
          false, //start of event
          pal1CycleTime.AddMinutes(30),
          "result",
          true)); //end of route

      _jobLog.AddLogEntryFromUnitTest(pal1Cycle[pal1Cycle.Count - 1]);

      CheckLog(pal1Cycle, _jobLog.CurrentPalletLog("pal1"), DateTime.UtcNow.AddHours(-10));
      CheckLog(pal2Cycle, _jobLog.CurrentPalletLog("pal2"), DateTime.UtcNow.AddHours(-10));

      _jobLog.CompletePalletCycle("pal1", pal1CycleTime.AddMinutes(40), "");
      _jobLog.CompletePalletCycle("pal1", pal1CycleTime.AddMinutes(40), ""); //filter duplicates

      var elapsed = pal1CycleTime.AddMinutes(40).Subtract(pal1InitialTime.AddMinutes(25));
      pal1Cycle.Add(new LogEntry(0, new LogMaterial[] { }, "pal1", LogType.PalletCycle, "Pallet Cycle", 1,
          "", false, pal1CycleTime.AddMinutes(40), "PalletCycle", false, elapsed, TimeSpan.Zero));

      Assert.Equal(pal1CycleTime.AddMinutes(40), _jobLog.LastPalletCycleTime("pal1"));
      _jobLog.CurrentPalletLog("pal1").Should().BeEmpty();
      CheckLog(pal1Cycle, _jobLog.GetLogEntries(pal1CycleTime.AddMinutes(-5), DateTime.UtcNow), DateTime.UtcNow.AddHours(-50));

      CheckLog(pal2Cycle, _jobLog.CurrentPalletLog("pal2"), DateTime.UtcNow.AddHours(-10));
    }

    [Fact]
    public void ForeignID()
    {
      var mat1 = new LogMaterial(
          _jobLog.AllocateMaterialID("unique", "part1", 2), "unique", 1, "part1", 2, "", "", "face1");
      var mat2 = new LogMaterial(
          _jobLog.AllocateMaterialID("unique2", "part2", 2), "unique2", 2, "part2", 2, "", "", "face2");

      var log1 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                            "pal1", LogType.GeneralMessage, "ABC", 1,
                            "prog1", false, DateTime.UtcNow, "result1", false, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(11));
      var log2 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                            "pal2", LogType.MachineCycle, "MC", 1,
                            "prog2", false, DateTime.UtcNow, "result2", false, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(16));
      var log3 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                            "pal3", LogType.LoadUnloadCycle, "Load", 1,
                            "prog3", false, DateTime.UtcNow, "result3", false, TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(21));

      Assert.Equal("", _jobLog.MaxForeignID());
      _jobLog.AddLogEntryFromUnitTest(log1, "for1");
      Assert.Equal("for1", _jobLog.MaxForeignID());
      _jobLog.AddLogEntryFromUnitTest(log2, "for2");
      Assert.Equal("for2", _jobLog.MaxForeignID());
      _jobLog.AddLogEntryFromUnitTest(log3);
      Assert.Equal("for2", _jobLog.MaxForeignID());
      _jobLog.AddPendingLoad("p", "k", 1, TimeSpan.Zero, TimeSpan.Zero, "for4");
      Assert.Equal("for4", _jobLog.MaxForeignID());
      var mat = new Dictionary<string, IEnumerable<EventLogDB.EventLogMaterial>>();
      mat["k"] = new EventLogDB.EventLogMaterial[] { };
      _jobLog.CompletePalletCycle("p", DateTime.UtcNow, "for3", mat, generateSerials: false);
      Assert.Equal("for4", _jobLog.MaxForeignID()); // for4 should be copied

      var load1 = _jobLog.StationLogByForeignID("for1");
      load1.Count.Should().Be(1);
      CheckEqual(log1, load1[0]);

      var load2 = _jobLog.StationLogByForeignID("for2");
      load2.Count.Should().Be(1);
      CheckEqual(log2, load2[0]);

      var load3 = _jobLog.StationLogByForeignID("for3");
      load3.Count.Should().Be(1);
      Assert.Equal(LogType.PalletCycle, load3[0].LogType);

      var load4 = _jobLog.StationLogByForeignID("for4");
      load4.Count.Should().Be(1);
      Assert.Equal(LogType.LoadUnloadCycle, load4[0].LogType);

      _jobLog.StationLogByForeignID("abwgtweg").Should().BeEmpty();

      Assert.Equal("for1", _jobLog.ForeignIDForCounter(load1[0].Counter));
      Assert.Equal("for2", _jobLog.ForeignIDForCounter(load2[0].Counter));
      Assert.Equal("", _jobLog.ForeignIDForCounter(load2[0].Counter + 30));
    }

    [Fact]
    public void OriginalMessage()
    {
      var mat1 = new LogMaterial(
          _jobLog.AllocateMaterialID("uniqqqq", "pppart66", 5), "uniqqqq", 1, "pppart66", 5, "", "", "facce");
      var mat2 = new LogMaterial(
          _jobLog.AllocateMaterialID("uuuuuniq", "part5", 2), "uuuuuniq", 2, "part5", 2, "", "", "face2");

      var log1 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                            "palllet16", LogType.GeneralMessage, "Hello", 5,
                            "program125", false, DateTime.UtcNow, "result66", false, TimeSpan.FromMinutes(166), TimeSpan.FromMinutes(74));

      Assert.Equal("", _jobLog.MaxForeignID());
      _jobLog.AddLogEntryFromUnitTest(log1, "foreign1", "the original message");
      Assert.Equal("foreign1", _jobLog.MaxForeignID());

      var load1 = _jobLog.StationLogByForeignID("foreign1");
      load1.Count.Should().Be(1);
      CheckEqual(log1, load1[0]);

      Assert.Equal("the original message", _jobLog.OriginalMessageByForeignID("foreign1"));
      Assert.Equal("", _jobLog.OriginalMessageByForeignID("abc"));
    }

    [Fact]
    public void WorkorderSummary()
    {
      var t = DateTime.UtcNow.AddHours(-1);

      //one material across two processes
      var mat1 = _jobLog.AllocateMaterialID("uniq1", "part1", 2);
      var mat1_proc1 = new LogMaterial(mat1, "uniq1", 1, "part1", 2, "", "", "");
      var mat1_proc2 = new LogMaterial(mat1, "uniq1", 2, "part1", 2, "", "", "");
      var mat2_proc1 = new LogMaterial(_jobLog.AllocateMaterialID("uniq1", "part1", 2), "uniq1", 1, "part1", 2, "", "", "");

      //not adding all events, but at least one non-endofroute and one endofroute
      _jobLog.AddLogEntryFromUnitTest(
        new LogEntry(0, new LogMaterial[] { mat1_proc1 },
                             "pal1", LogType.MachineCycle, "MC", 5, "prog1", false,
                             t.AddMinutes(5), "", false,
               TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20))
      );
      _jobLog.AddLogEntryFromUnitTest(
        new LogEntry(0, new LogMaterial[] { mat1_proc2 },
                             "pal1", LogType.MachineCycle, "MC", 5, "prog2", false,
                             t.AddMinutes(6), "", false,
               TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(40))
      );
      _jobLog.AddLogEntryFromUnitTest(
        new LogEntry(0, new LogMaterial[] { mat1_proc2, mat2_proc1 }, //mat2_proc1 should be ignored since it isn't final process
                             "pal1", LogType.LoadUnloadCycle, "Load", 5, "UNLOAD", false,
                             t.AddMinutes(7), "UNLOAD", false,
               TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(60))
      );

      //four materials on the same pallet but different workorders
      var mat3 = new LogMaterial(_jobLog.AllocateMaterialID("uniq2", "part1", 1), "uniq2", 1, "part1", 1, "", "", "");
      var mat4 = new LogMaterial(_jobLog.AllocateMaterialID("uniq2", "part2", 1), "uniq2", 1, "part2", 1, "", "", "");
      var mat5 = new LogMaterial(_jobLog.AllocateMaterialID("uniq2", "part3", 1), "uniq2", 1, "part3", 1, "", "", "");
      var mat6 = new LogMaterial(_jobLog.AllocateMaterialID("uniq2", "part3", 1), "uniq2", 1, "part3", 1, "", "", "");

      _jobLog.AddLogEntryFromUnitTest(
        new LogEntry(0, new LogMaterial[] { mat3, mat4, mat5, mat6 },
                             "pal1", LogType.MachineCycle, "MC", 5, "progdouble", false,
                             t.AddMinutes(15), "", false,
               TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(4))
      );
      _jobLog.AddLogEntryFromUnitTest(
        new LogEntry(0, new LogMaterial[] { mat3, mat4, mat5, mat6 },
                             "pal1", LogType.LoadUnloadCycle, "Load", 5, "UNLOAD", false,
                             t.AddMinutes(17), "UNLOAD", false,
               TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(6))
      );


      //now record serial and workorder
      _jobLog.RecordSerialForMaterialID(EventLogDB.EventLogMaterial.FromLogMat(mat1_proc2), "serial1");
      _jobLog.RecordSerialForMaterialID(EventLogDB.EventLogMaterial.FromLogMat(mat2_proc1), "serial2");
      _jobLog.RecordSerialForMaterialID(EventLogDB.EventLogMaterial.FromLogMat(mat3), "serial3");
      _jobLog.RecordSerialForMaterialID(EventLogDB.EventLogMaterial.FromLogMat(mat4), "serial4");
      _jobLog.RecordSerialForMaterialID(EventLogDB.EventLogMaterial.FromLogMat(mat5), "serial5");
      _jobLog.RecordSerialForMaterialID(EventLogDB.EventLogMaterial.FromLogMat(mat6), "serial6");
      Assert.Equal("serial1", _jobLog.GetMaterialDetails(mat1_proc2.MaterialID).Serial);
      _jobLog.GetMaterialDetailsForSerial("serial1").Should().BeEquivalentTo(new[] {
        new MaterialDetails() {
          MaterialID = mat1_proc2.MaterialID,
          JobUnique = "uniq1",
          PartName = "part1",
          NumProcesses = 2,
          Serial = "serial1",
          Paths = new Dictionary<int, int>()
        }
      });
      _jobLog.GetMaterialDetailsForSerial("waoheufweiuf").Should().BeEmpty();

      _jobLog.RecordWorkorderForMaterialID(EventLogDB.EventLogMaterial.FromLogMat(mat1_proc2), "work1");
      _jobLog.RecordWorkorderForMaterialID(EventLogDB.EventLogMaterial.FromLogMat(mat3), "work1");
      _jobLog.RecordWorkorderForMaterialID(EventLogDB.EventLogMaterial.FromLogMat(mat4), "work1");
      _jobLog.RecordWorkorderForMaterialID(EventLogDB.EventLogMaterial.FromLogMat(mat5), "work2");
      _jobLog.RecordWorkorderForMaterialID(EventLogDB.EventLogMaterial.FromLogMat(mat6), "work2");
      Assert.Equal("work2", _jobLog.GetMaterialDetails(mat5.MaterialID).Workorder);

      var summary = _jobLog.GetWorkorderSummaries(new[] { "work1", "work2" });
      Assert.Equal(2, summary.Count);

      //work1 contains part1 from mat1 and mat3, and part2 from mat4
      Assert.Equal("work1", summary[0].WorkorderId);
      Assert.False(summary[0].FinalizedTimeUTC.HasValue);
      Assert.Equal(new[] { "serial1", "serial3", "serial4" }, summary[0].Serials);
      var work1Parts = summary[0].Parts;
      Assert.Equal(2, work1Parts.Count);
      var work1Part1 = work1Parts[0];
      var work1Part2 = work1Parts[1];

      Assert.Equal("part1", work1Part1.Part);
      Assert.Equal(2, work1Part1.PartsCompleted); //mat1 and mat3
      Assert.Equal(2, work1Part1.ElapsedStationTime.Count);
      Assert.Equal(2, work1Part1.ActiveStationTime.Count);

      double c2Cnt = 4; //number of material on cycle 2

      //10 + 30 from mat1, 3*1/4 for mat3
      Assert.Equal(TimeSpan.FromMinutes(10 + 30 + 3 * 1 / c2Cnt), work1Part1.ElapsedStationTime["MC"]);

      //50/2 from mat1_proc2, and 5*1/4 for mat3
      Assert.Equal(TimeSpan.FromMinutes(50 / 2 + 5 * 1 / c2Cnt), work1Part1.ElapsedStationTime["Load"]);

      //20 + 40 from mat1, 4*1/4 for mat3
      Assert.Equal(TimeSpan.FromMinutes(20 + 40 + 4 * 1 / c2Cnt), work1Part1.ActiveStationTime["MC"]);

      //60/2 from mat1_proc2, and 6*1/4 for mat3
      Assert.Equal(TimeSpan.FromMinutes(60 / 2 + 6 * 1 / c2Cnt), work1Part1.ActiveStationTime["Load"]);

      Assert.Equal("part2", work1Part2.Part);
      Assert.Equal(1, work1Part2.PartsCompleted); //part2 is just mat4
      Assert.Equal(2, work1Part2.ElapsedStationTime.Count);
      Assert.Equal(2, work1Part2.ActiveStationTime.Count);
      Assert.Equal(TimeSpan.FromMinutes(3 * 1 / c2Cnt), work1Part2.ElapsedStationTime["MC"]);
      Assert.Equal(TimeSpan.FromMinutes(5 * 1 / c2Cnt), work1Part2.ElapsedStationTime["Load"]);
      Assert.Equal(TimeSpan.FromMinutes(4 * 1 / c2Cnt), work1Part2.ActiveStationTime["MC"]);
      Assert.Equal(TimeSpan.FromMinutes(6 * 1 / c2Cnt), work1Part2.ActiveStationTime["Load"]);

      //------- work2

      Assert.Equal("work2", summary[1].WorkorderId);
      Assert.False(summary[1].FinalizedTimeUTC.HasValue);
      Assert.Equal(new[] { "serial5", "serial6" }, summary[1].Serials);
      summary[1].Parts.Count.Should().Be(1);
      var work2Part2 = summary[1].Parts[0];
      Assert.Equal("part3", work2Part2.Part); //part3 is mat5 and 6
      Assert.Equal(2, work2Part2.PartsCompleted); //part3 is mat5 and 6
      Assert.Equal(2, work2Part2.ElapsedStationTime.Count);
      Assert.Equal(2, work2Part2.ActiveStationTime.Count);
      Assert.Equal(TimeSpan.FromMinutes(3 * 2 / c2Cnt), work2Part2.ElapsedStationTime["MC"]);
      Assert.Equal(TimeSpan.FromMinutes(5 * 2 / c2Cnt), work2Part2.ElapsedStationTime["Load"]);
      Assert.Equal(TimeSpan.FromMinutes(4 * 2 / c2Cnt), work2Part2.ActiveStationTime["MC"]);
      Assert.Equal(TimeSpan.FromMinutes(6 * 2 / c2Cnt), work2Part2.ActiveStationTime["Load"]);

      //---- test finalize
      var finalizedEntry = _jobLog.RecordFinalizedWorkorder("work1");
      Assert.Equal("", finalizedEntry.Pallet);
      Assert.Equal("work1", finalizedEntry.Result);
      Assert.Equal(LogType.FinalizeWorkorder, finalizedEntry.LogType);

      summary = _jobLog.GetWorkorderSummaries(new[] { "work1" });
      Assert.Equal("work1", summary[0].WorkorderId);
      Assert.True(summary[0].FinalizedTimeUTC.HasValue);
      Assert.InRange(summary[0].FinalizedTimeUTC.Value.Subtract(DateTime.UtcNow),
          TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void LoadCompletedParts()
    {
      var old = DateTime.UtcNow.AddHours(-50);
      var recent = DateTime.UtcNow.AddHours(-1);

      //material
      var mat1 = _jobLog.AllocateMaterialID("uniq1", "part1", 2);
      var mat1_proc1 = new LogMaterial(mat1, "uniq1", 1, "part1", 2, "", "", "");
      var mat1_proc2 = new LogMaterial(mat1, "uniq1", 2, "part1", 2, "", "", "");
      var mat2 = _jobLog.AllocateMaterialID("uniq1", "part1", 2);
      var mat2_proc1 = new LogMaterial(mat2, "uniq1", 1, "part1", 2, "", "", "");
      var mat2_proc2 = new LogMaterial(mat2, "uniq1", 2, "part1", 2, "", "", "");

      var mat3 = new LogMaterial(_jobLog.AllocateMaterialID("uniq1", "part1", 1), "uniq1", 1, "part1", 1, "", "", "");
      var mat4 = new LogMaterial(_jobLog.AllocateMaterialID("uniq1", "part1", 1), "uniq1", 1, "part1", 1, "", "", "");

      //mat1 has proc1 in old, proc2 in recent so everything should be loaded
      var mat1_proc1old = AddLogEntry(
        new LogEntry(0, new LogMaterial[] { mat1_proc1 },
                             "pal1", LogType.MachineCycle, "MC", 5, "prog1", false,
                             old.AddMinutes(5), "", false,
               TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20))
      );
      var mat1_proc1complete = AddLogEntry(
  new LogEntry(0, new LogMaterial[] { mat1_proc1 },
                       "pal1", LogType.LoadUnloadCycle, "Load", 5, "prog1", false,
                       old.AddMinutes(6), "", false,
         TimeSpan.FromMinutes(11), TimeSpan.FromMinutes(21))
);
      var mat1_proc2old = AddLogEntry(
        new LogEntry(0, new LogMaterial[] { mat1_proc2 },
                             "pal1", LogType.MachineCycle, "MC", 5, "prog2", false,
                             old.AddMinutes(7), "", false,
               TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(22))
      );
      var mat1_proc2complete = AddLogEntry(
        new LogEntry(0, new LogMaterial[] { mat1_proc2 },
                             "pal1", LogType.LoadUnloadCycle, "Load", 5, "UNLOAD", false,
                             recent.AddMinutes(4), "UNLOAD", false,
               TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(40))
      );

      //mat2 has everything in recent, (including proc1 complete) but no complete on proc2
      AddLogEntry(
  new LogEntry(0, new LogMaterial[] { mat2_proc1 },
                       "pal1", LogType.MachineCycle, "MC", 5, "mach2", false,
                       recent.AddMinutes(5), "", false,
         TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(60))
);
      AddLogEntry(
  new LogEntry(0, new LogMaterial[] { mat2_proc1 },
                       "pal1", LogType.LoadUnloadCycle, "Load", 5, "load2", false,
                       recent.AddMinutes(6), "UNLOAD", false,
         TimeSpan.FromMinutes(51), TimeSpan.FromMinutes(61))
);
      AddLogEntry(
        new LogEntry(0, new LogMaterial[] { mat2_proc2 },
                             "pal1", LogType.MachineCycle, "MC", 5, "mach2", false,
                             old.AddMinutes(7), "", false,
               TimeSpan.FromMinutes(52), TimeSpan.FromMinutes(62))
      );

      //mat3 has everything in old
      AddLogEntry(
  new LogEntry(0, new LogMaterial[] { mat3 },
                       "pal1", LogType.MachineCycle, "MC", 5, "prog3", false,
                       old.AddMinutes(20), "", false,
         TimeSpan.FromMinutes(70), TimeSpan.FromMinutes(80))
);
      AddLogEntry(
  new LogEntry(0, new LogMaterial[] { mat3 },
                       "pal1", LogType.LoadUnloadCycle, "Load", 5, "load3", false,
                       old.AddMinutes(25), "UNLOAD", false,
         TimeSpan.FromMinutes(71), TimeSpan.FromMinutes(81))
);

      //mat4 has everything in new
      var mat4recent = AddLogEntry(
  new LogEntry(0, new LogMaterial[] { mat4 },
                       "pal1", LogType.MachineCycle, "MC", 5, "prog44", false,
                       recent.AddMinutes(40), "", false,
         TimeSpan.FromMinutes(90), TimeSpan.FromMinutes(100))
);
      var mat4complete = AddLogEntry(
  new LogEntry(0, new LogMaterial[] { mat4 },
                       "pal1", LogType.LoadUnloadCycle, "Load", 5, "load4", false,
                       recent.AddMinutes(45), "UNLOAD", false,
         TimeSpan.FromMinutes(91), TimeSpan.FromMinutes(101))
);

      CheckLog(
        new[] {mat1_proc1old, mat1_proc1complete, mat1_proc2old, mat1_proc2complete,
                      mat4recent, mat4complete},
       _jobLog.GetCompletedPartLogs(recent.AddHours(-4), recent.AddHours(4)),
        DateTime.MinValue);
    }

    [Fact]
    public void Queues()
    {
      var start = DateTime.UtcNow.AddHours(-10);

      var otherQueueMat = new LogMaterial(100, "uniq100", 100, "part100", 100, "", "", "");
      _jobLog.CreateMaterialID(100, "uniq100", "part100", 100);
      _jobLog.RecordAddMaterialToQueue(EventLogDB.EventLogMaterial.FromLogMat(otherQueueMat), "BBBB", 0, "theoper", start.AddHours(-1))
          .Should().BeEquivalentTo(new[] { AddToQueueExpectedEntry(otherQueueMat, 1, "BBBB", 0, start.AddHours(-1), "theoper") });


      var expectedLogs = new List<LogEntry>();

      var mat1 = new LogMaterial(1, "uniq1", 15, "part111", 19, "", "", "");
      _jobLog.CreateMaterialID(1, "uniq1", "part111", 19);
      var mat2 = new LogMaterial(2, "uniq2", 1, "part2", 22, "", "", "");
      _jobLog.CreateMaterialID(2, "uniq2", "part2", 22);
      var mat3 = new LogMaterial(3, "uniq3", 3, "part3", 36, "", "", "");
      _jobLog.CreateMaterialID(3, "uniq3", "part3", 36);
      var mat4 = new LogMaterial(4, "uniq4", 4, "part4", 44, "", "", "");
      _jobLog.CreateMaterialID(4, "uniq4", "part4", 44);

      // add via LogMaterial with position -1
      _jobLog.RecordAddMaterialToQueue(EventLogDB.EventLogMaterial.FromLogMat(mat1), "AAAA", -1, null, start)
          .Should().BeEquivalentTo(new[] { AddToQueueExpectedEntry(mat1, 2, "AAAA", 0, start) });
      expectedLogs.Add(AddToQueueExpectedEntry(mat1, 2, "AAAA", 0, start));

      _jobLog.GetMaterialInQueue("AAAA")
          .Should().BeEquivalentTo(new[] {
                    new EventLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 0, Unique = "uniq1", PartNameOrCasting = "part111", NumProcesses = 19, AddTimeUTC = start}
          });
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().BeNull();
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().BeNull();
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().BeNull();

      //adding with LogMaterial with position -1 and existing queue
      _jobLog.RecordAddMaterialToQueue(EventLogDB.EventLogMaterial.FromLogMat(mat2), "AAAA", -1, null, start.AddMinutes(10))
          .Should().BeEquivalentTo(new[] { AddToQueueExpectedEntry(mat2, 3, "AAAA", 1, start.AddMinutes(10)) });
      expectedLogs.Add(AddToQueueExpectedEntry(mat2, 3, "AAAA", 1, start.AddMinutes(10)));

      _jobLog.GetMaterialInQueue("AAAA")
          .Should().BeEquivalentTo(new[] {
                    new EventLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 0, Unique = "uniq1", PartNameOrCasting = "part111", NumProcesses = 19, AddTimeUTC = start},
                    new EventLogDB.QueuedMaterial() { MaterialID = 2, Queue = "AAAA", Position = 1, Unique = "uniq2", PartNameOrCasting = "part2", NumProcesses = 22, AddTimeUTC = start.AddMinutes(10)}
          });
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().BeNull();
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().BeNull();


      //inserting into queue with LogMaterial
      _jobLog.RecordAddMaterialToQueue(EventLogDB.EventLogMaterial.FromLogMat(mat3), "AAAA", 1, "opernnnn", start.AddMinutes(20))
          .Should().BeEquivalentTo(new[] { AddToQueueExpectedEntry(mat3, 4, "AAAA", 1, start.AddMinutes(20), "opernnnn") });
      expectedLogs.Add(AddToQueueExpectedEntry(mat3, 4, "AAAA", 1, start.AddMinutes(20), "opernnnn"));

      _jobLog.GetMaterialInQueue("AAAA")
          .Should().BeEquivalentTo(new[] {
                    new EventLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 0, Unique = "uniq1", PartNameOrCasting = "part111", NumProcesses = 19, AddTimeUTC = start},
                    new EventLogDB.QueuedMaterial() { MaterialID = 3, Queue = "AAAA", Position = 1, Unique = "uniq3", PartNameOrCasting = "part3", NumProcesses = 36, AddTimeUTC = start.AddMinutes(20)},
                    new EventLogDB.QueuedMaterial() { MaterialID = 2, Queue = "AAAA", Position = 2, Unique = "uniq2", PartNameOrCasting = "part2", NumProcesses = 22, AddTimeUTC = start.AddMinutes(10)}
          });
      _jobLog.GetMaterialInAllQueues()
          .Should().BeEquivalentTo(new[] {
                    new EventLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 0, Unique = "uniq1", PartNameOrCasting = "part111", NumProcesses = 19, AddTimeUTC = start},
                    new EventLogDB.QueuedMaterial() { MaterialID = 3, Queue = "AAAA", Position = 1, Unique = "uniq3", PartNameOrCasting = "part3", NumProcesses = 36, AddTimeUTC = start.AddMinutes(20)},
                    new EventLogDB.QueuedMaterial() { MaterialID = 2, Queue = "AAAA", Position = 2, Unique = "uniq2", PartNameOrCasting = "part2", NumProcesses = 22, AddTimeUTC = start.AddMinutes(10)},
                    new EventLogDB.QueuedMaterial() { MaterialID = 100, Queue = "BBBB", Position = 0, Unique = "uniq100", PartNameOrCasting = "part100", NumProcesses = 100, AddTimeUTC = start.AddHours(-1)}
          });
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().BeNull();

      //removing from queue with LogMaterial
      _jobLog.RecordRemoveMaterialFromAllQueues(EventLogDB.EventLogMaterial.FromLogMat(mat3), "operyy", start.AddMinutes(40))
          .Should().BeEquivalentTo(new[] { RemoveFromQueueExpectedEntry(mat3, 5, "AAAA", 1, 40 - 20, start.AddMinutes(40), "operyy") });
      expectedLogs.Add(RemoveFromQueueExpectedEntry(mat3, 5, "AAAA", 1, 40 - 20, start.AddMinutes(40), "operyy"));

      _jobLog.GetMaterialInQueue("AAAA")
          .Should().BeEquivalentTo(new[] {
                    new EventLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 0, Unique = "uniq1", PartNameOrCasting = "part111", NumProcesses = 19, AddTimeUTC = start},
                    new EventLogDB.QueuedMaterial() { MaterialID = 2, Queue = "AAAA", Position = 1, Unique = "uniq2", PartNameOrCasting = "part2", NumProcesses = 22, AddTimeUTC = start.AddMinutes(10)}
          });
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().BeNull();


      //add back in with matid only
      _jobLog.RecordAddMaterialToQueue(mat3.MaterialID, mat3.Process, "AAAA", 2, null, start.AddMinutes(45))
          .Should().BeEquivalentTo(new[] { AddToQueueExpectedEntry(mat3, 6, "AAAA", 2, start.AddMinutes(45)) });
      expectedLogs.Add(AddToQueueExpectedEntry(mat3, 6, "AAAA", 2, start.AddMinutes(45)));

      _jobLog.GetMaterialInQueue("AAAA")
          .Should().BeEquivalentTo(new[] {
                    new EventLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 0, Unique = "uniq1", PartNameOrCasting = "part111", NumProcesses = 19, AddTimeUTC = start},
                    new EventLogDB.QueuedMaterial() { MaterialID = 2, Queue = "AAAA", Position = 1, Unique = "uniq2", PartNameOrCasting = "part2", NumProcesses = 22, AddTimeUTC = start.AddMinutes(10)},
                    new EventLogDB.QueuedMaterial() { MaterialID = 3, Queue = "AAAA", Position = 2, Unique = "uniq3", PartNameOrCasting = "part3", NumProcesses = 36, AddTimeUTC = start.AddMinutes(45)}
          });
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().BeNull();

      //move item backwards in queue
      _jobLog.RecordAddMaterialToQueue(EventLogDB.EventLogMaterial.FromLogMat(mat1), "AAAA", 1, null, start.AddMinutes(50))
          .Should().BeEquivalentTo(new[] {
                    RemoveFromQueueExpectedEntry(mat1, 7, "AAAA", 0, 50, start.AddMinutes(50)),
                    AddToQueueExpectedEntry(mat1, 8, "AAAA", 1, start.AddMinutes(50))
          });
      expectedLogs.Add(RemoveFromQueueExpectedEntry(mat1, 7, "AAAA", 0, 50, start.AddMinutes(50)));
      expectedLogs.Add(AddToQueueExpectedEntry(mat1, 8, "AAAA", 1, start.AddMinutes(50)));

      _jobLog.GetMaterialInQueue("AAAA")
          .Should().BeEquivalentTo(new[] {
                    new EventLogDB.QueuedMaterial() { MaterialID = 2, Queue = "AAAA", Position = 0, Unique = "uniq2", PartNameOrCasting = "part2", NumProcesses = 22, AddTimeUTC = start.AddMinutes(10)},
                    new EventLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 1, Unique = "uniq1", PartNameOrCasting = "part111", NumProcesses = 19, AddTimeUTC = start.AddMinutes(50)},
                    new EventLogDB.QueuedMaterial() { MaterialID = 3, Queue = "AAAA", Position = 2, Unique = "uniq3", PartNameOrCasting = "part3", NumProcesses = 36, AddTimeUTC = start.AddMinutes(45)},
          });
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().BeNull();

      //move item forwards in queue
      _jobLog.RecordAddMaterialToQueue(EventLogDB.EventLogMaterial.FromLogMat(mat3), "AAAA", 1, null, start.AddMinutes(55))
          .Should().BeEquivalentTo(new[] {
                    RemoveFromQueueExpectedEntry(mat3, 9, "AAAA", 2, 55 - 45, start.AddMinutes(55)),
                    AddToQueueExpectedEntry(mat3, 10, "AAAA", 1, start.AddMinutes(55))
          });
      expectedLogs.Add(RemoveFromQueueExpectedEntry(mat3, 9, "AAAA", 2, 55 - 45, start.AddMinutes(55)));
      expectedLogs.Add(AddToQueueExpectedEntry(mat3, 10, "AAAA", 1, start.AddMinutes(55)));

      _jobLog.GetMaterialInQueue("AAAA")
          .Should().BeEquivalentTo(new[] {
                    new EventLogDB.QueuedMaterial() { MaterialID = 2, Queue = "AAAA", Position = 0, Unique = "uniq2", PartNameOrCasting = "part2", NumProcesses = 22, AddTimeUTC = start.AddMinutes(10)},
                    new EventLogDB.QueuedMaterial() { MaterialID = 3, Queue = "AAAA", Position = 1, Unique = "uniq3", PartNameOrCasting = "part3", NumProcesses = 36, AddTimeUTC = start.AddMinutes(55)  },
                    new EventLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 2, Unique = "uniq1", PartNameOrCasting = "part111", NumProcesses = 19, AddTimeUTC = start.AddMinutes(50)},
          });
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().BeNull();

      //add large position
      _jobLog.RecordAddMaterialToQueue(EventLogDB.EventLogMaterial.FromLogMat(mat4), "AAAA", 500, null, start.AddMinutes(58))
          .Should().BeEquivalentTo(new[] {
                    AddToQueueExpectedEntry(mat4, 11, "AAAA", 3, start.AddMinutes(58))
          });
      expectedLogs.Add(AddToQueueExpectedEntry(mat4, 11, "AAAA", 3, start.AddMinutes(58)));

      _jobLog.GetMaterialInQueue("AAAA")
          .Should().BeEquivalentTo(new[] {
                    new EventLogDB.QueuedMaterial() { MaterialID = 2, Queue = "AAAA", Position = 0, Unique = "uniq2", PartNameOrCasting = "part2", NumProcesses = 22, AddTimeUTC = start.AddMinutes(10)},
                    new EventLogDB.QueuedMaterial() { MaterialID = 3, Queue = "AAAA", Position = 1, Unique = "uniq3", PartNameOrCasting = "part3", NumProcesses = 36, AddTimeUTC = start.AddMinutes(55)},
                    new EventLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 2, Unique = "uniq1", PartNameOrCasting = "part111", NumProcesses = 19, AddTimeUTC = start.AddMinutes(50)},
                    new EventLogDB.QueuedMaterial() { MaterialID = 4, Queue = "AAAA", Position = 3, Unique = "uniq4", PartNameOrCasting = "part4", NumProcesses = 44, AddTimeUTC = start.AddMinutes(58)},
          });
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().Be(5);

      _jobLog.SignalMaterialForQuarantine(EventLogDB.EventLogMaterial.FromLogMat(mat1), "pal", "QQQ", start.AddMinutes(59), "theoper")
        .Should().BeEquivalentTo(SignalQuarantineExpectedEntry(mat1, 12, "pal", "QQQ", start.AddMinutes(59), "theoper"));
      expectedLogs.Add(SignalQuarantineExpectedEntry(mat1, 12, "pal", "QQQ", start.AddMinutes(59), "theoper"));

      // hasn't moved yet
      _jobLog.GetMaterialInQueue("AAAA")
          .Should().BeEquivalentTo(new[] {
                    new EventLogDB.QueuedMaterial() { MaterialID = 2, Queue = "AAAA", Position = 0, Unique = "uniq2", PartNameOrCasting = "part2", NumProcesses = 22, AddTimeUTC = start.AddMinutes(10)},
                    new EventLogDB.QueuedMaterial() { MaterialID = 3, Queue = "AAAA", Position = 1, Unique = "uniq3", PartNameOrCasting = "part3", NumProcesses = 36, AddTimeUTC = start.AddMinutes(55)},
                    new EventLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 2, Unique = "uniq1", PartNameOrCasting = "part111", NumProcesses = 19, AddTimeUTC = start.AddMinutes(50)},
                    new EventLogDB.QueuedMaterial() { MaterialID = 4, Queue = "AAAA", Position = 3, Unique = "uniq4", PartNameOrCasting = "part4", NumProcesses = 44, AddTimeUTC = start.AddMinutes(58)},
          });
      _jobLog.GetMaterialInQueue("QQQ").Should().BeEmpty();
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().Be(5);


      //removing from queue with matid
      var mat2proc8 = new LogMaterial(mat2.MaterialID, mat2.JobUniqueStr, 1, mat2.PartName, mat2.NumProcesses, mat2.Serial, mat2.Workorder, mat2.Face);
      _jobLog.RecordRemoveMaterialFromAllQueues(mat2.MaterialID, 1, null, start.AddMinutes(60))
          .Should().BeEquivalentTo(new[] { RemoveFromQueueExpectedEntry(mat2proc8, 13, "AAAA", 0, 60 - 10, start.AddMinutes(60)) });
      expectedLogs.Add(RemoveFromQueueExpectedEntry(mat2proc8, 13, "AAAA", 0, 60 - 10, start.AddMinutes(60)));

      _jobLog.GetMaterialInQueue("AAAA")
          .Should().BeEquivalentTo(new[] {
                    new EventLogDB.QueuedMaterial() { MaterialID = 3, Queue = "AAAA", Position = 0, Unique = "uniq3", PartNameOrCasting = "part3", NumProcesses = 36, AddTimeUTC = start.AddMinutes(55)},
                    new EventLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 1, Unique = "uniq1", PartNameOrCasting = "part111", NumProcesses = 19, AddTimeUTC = start.AddMinutes(50)},
                    new EventLogDB.QueuedMaterial() { MaterialID = 4, Queue = "AAAA", Position = 2, Unique = "uniq4", PartNameOrCasting = "part4", NumProcesses = 44, AddTimeUTC = start.AddMinutes(58)},
          });
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().Be(5);


      _jobLog.GetLogEntries(start, DateTime.UtcNow)
          .Should().BeEquivalentTo(expectedLogs);
    }

    [Fact]
    public void LoadUnloadIntoQueues()
    {
      var start = DateTime.UtcNow.AddHours(-10);
      var expectedLogs = new List<LogEntry>();

      var mat1 = new LogMaterial(1, "uniq1", 15, "part111", 19, "", "", "");
      _jobLog.CreateMaterialID(1, "uniq1", "part111", 19);
      var mat2 = new LogMaterial(2, "uniq2", 1, "part2", 22, "", "", "");
      _jobLog.CreateMaterialID(2, "uniq2", "part2", 22);
      var mat3 = new LogMaterial(3, "uniq3", 3, "part3", 36, "", "", "");
      _jobLog.CreateMaterialID(3, "uniq3", "part3", 36);
      var mat4 = new LogMaterial(4, "uniq4", 4, "part4", 47, "", "", "");
      _jobLog.CreateMaterialID(4, "uniq4", "part4", 47);

      // add two material into queue 1
      _jobLog.RecordAddMaterialToQueue(new EventLogDB.EventLogMaterial() { MaterialID = mat1.MaterialID, Process = 14, Face = "" }, "AAAA", -1, null, start);
      expectedLogs.Add(AddToQueueExpectedEntry(SetProcInMat(14)(mat1), 1, "AAAA", 0, start));
      _jobLog.RecordAddMaterialToQueue(new EventLogDB.EventLogMaterial() { MaterialID = mat2.MaterialID, Process = 0, Face = "" }, "AAAA", -1, null, start);
      expectedLogs.Add(AddToQueueExpectedEntry(SetProcInMat(0)(mat2), 2, "AAAA", 1, start));

      _jobLog.GetMaterialInQueue("AAAA")
          .Should().BeEquivalentTo(new[] {
                    new EventLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 0, Unique = "uniq1", PartNameOrCasting = "part111", NumProcesses = 19, AddTimeUTC = start},
                    new EventLogDB.QueuedMaterial() { MaterialID = 2, Queue = "AAAA", Position = 1, Unique = "uniq2", PartNameOrCasting = "part2", NumProcesses = 22, AddTimeUTC = start}
          });

      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(15);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(1);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().BeNull();
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().BeNull();


      // loading should remove from queue
      var loadEndActual = _jobLog.RecordLoadEnd(new[] { mat1 }.Select(EventLogDB.EventLogMaterial.FromLogMat), "pal1", 16, start.AddMinutes(10), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20));
      loadEndActual.Should().BeEquivalentTo(new[] {
                new LogEntry(4, new [] {mat1}, "pal1",
                    LogType.LoadUnloadCycle, "L/U", 16,
                    "LOAD", false, start.AddMinutes(10), "LOAD", false,
                    TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20)),

                RemoveFromQueueExpectedEntry(SetProcInMat(mat1.Process - 1)(mat1), 3, "AAAA", 0, 10, start.AddMinutes(10))
            });
      expectedLogs.AddRange(loadEndActual);

      _jobLog.GetMaterialInQueue("AAAA")
          .Should().BeEquivalentTo(new[] {
                    new EventLogDB.QueuedMaterial() { MaterialID = 2, Queue = "AAAA", Position = 0, Unique = "uniq2", PartNameOrCasting = "part2", NumProcesses = 22, AddTimeUTC = start}
          });

      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(1);

      //unloading should add to queue
      var unloadEndActual = _jobLog.RecordUnloadEnd(
          new[] { mat1, mat3, mat4 }.Select(EventLogDB.EventLogMaterial.FromLogMat), "pal5", 77, start.AddMinutes(30), TimeSpan.FromMinutes(52), TimeSpan.FromMinutes(23),
          new Dictionary<long, string>() { { 1, "AAAA" }, { 3, "AAAA" } });
      unloadEndActual.Should().BeEquivalentTo(new[] {
                new LogEntry(7, new [] {mat1, mat3, mat4}, "pal5",
                    LogType.LoadUnloadCycle, "L/U", 77,
                    "UNLOAD", false, start.AddMinutes(30), "UNLOAD", true,
                    TimeSpan.FromMinutes(52), TimeSpan.FromMinutes(23)),

                AddToQueueExpectedEntry(mat1, 5, "AAAA", 1, start.AddMinutes(30)),
                AddToQueueExpectedEntry(mat3, 6, "AAAA", 2, start.AddMinutes(30)),
            });
      expectedLogs.AddRange(unloadEndActual);

      _jobLog.GetMaterialInQueue("AAAA")
          .Should().BeEquivalentTo(new[] {
                    new EventLogDB.QueuedMaterial() { MaterialID = 2, Queue = "AAAA", Position = 0, Unique = "uniq2", PartNameOrCasting = "part2", NumProcesses = 22, AddTimeUTC = start},
                    new EventLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 1, Unique = "uniq1", PartNameOrCasting = "part111", NumProcesses = 19, AddTimeUTC = start.AddMinutes(30)},
                    new EventLogDB.QueuedMaterial() { MaterialID = 3, Queue = "AAAA", Position = 2, Unique = "uniq3", PartNameOrCasting = "part3", NumProcesses = 36, AddTimeUTC = start.AddMinutes(30)}
          });


      _jobLog.GetLogEntries(start, DateTime.UtcNow)
          .Should().BeEquivalentTo(expectedLogs);

      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(1); // unchanged, wasn't unloaded
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().Be(5);
    }

    [Fact]
    public void AllocateCastingsFromQueues()
    {
      var mat1 = new LogMaterial(
          _jobLog.AllocateMaterialIDForCasting("casting1"), "", 0, "casting1", 1, "", "", "");
      var mat2 = new LogMaterial(
          _jobLog.AllocateMaterialIDForCasting("casting1"), "", 0, "casting1", 1, "", "", "");
      var mat3 = new LogMaterial(
          _jobLog.AllocateMaterialIDForCasting("casting3"), "", 0, "casting3", 1, "", "", "");

      _jobLog.RecordAddMaterialToQueue(EventLogDB.EventLogMaterial.FromLogMat(mat1), "queue1", 0);
      _jobLog.RecordAddMaterialToQueue(EventLogDB.EventLogMaterial.FromLogMat(mat2), "queue1", 1);
      _jobLog.RecordAddMaterialToQueue(EventLogDB.EventLogMaterial.FromLogMat(mat3), "queue1", 2);

      _jobLog.GetMaterialDetails(mat1.MaterialID).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = mat1.MaterialID,
        JobUnique = null,
        PartName = "casting1",
        NumProcesses = 1,
        Paths = new Dictionary<int, int>()
      });

      _jobLog.GetMaterialDetails(mat2.MaterialID).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = mat2.MaterialID,
        JobUnique = null,
        PartName = "casting1",
        NumProcesses = 1,
        Paths = new Dictionary<int, int>()
      });

      _jobLog.GetMaterialDetails(mat3.MaterialID).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = mat3.MaterialID,
        JobUnique = null,
        PartName = "casting3",
        NumProcesses = 1,
        Paths = new Dictionary<int, int>()
      });

      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(1);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(1);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(1);

      _jobLog.AllocateCastingsInQueue(queue: "queue1", casting: "unused", unique: "uniqAAA", part: "part1", proc1Path: 1000, numProcesses: 15, count: 2)
          .Should().BeEquivalentTo(new long[] { });

      _jobLog.AllocateCastingsInQueue(queue: "queue1", casting: "casting1", unique: "uniqAAA", part: "part1", proc1Path: 1234, numProcesses: 6312, count: 50)
          .Should().BeEquivalentTo(new long[] { });

      _jobLog.AllocateCastingsInQueue(queue: "queue1", casting: "casting1", unique: "uniqAAA", part: "part1", proc1Path: 1234, numProcesses: 6312, count: 2)
          .Should().BeEquivalentTo(new[] { mat1.MaterialID, mat2.MaterialID });

      _jobLog.GetMaterialDetails(mat1.MaterialID).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = mat1.MaterialID,
        JobUnique = "uniqAAA",
        PartName = "part1",
        NumProcesses = 6312,
        Paths = new Dictionary<int, int>() { { 1, 1234 } }
      });

      _jobLog.GetMaterialDetails(mat2.MaterialID).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = mat2.MaterialID,
        JobUnique = "uniqAAA",
        PartName = "part1",
        NumProcesses = 6312,
        Paths = new Dictionary<int, int>() { { 1, 1234 } }
      });

      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(1);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(1);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(1);

      _jobLog.MarkCastingsAsUnallocated(new[] { mat1.MaterialID }, casting: "newcasting");

      _jobLog.GetMaterialDetails(mat1.MaterialID).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = mat1.MaterialID,
        JobUnique = null,
        PartName = "newcasting",
        NumProcesses = 6312,
        Paths = new Dictionary<int, int>()
      });

      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(1);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(1);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(1);
    }

    [Fact]
    public void ToolSnapshotDiff()
    {
      var start = new List<EventLogDB.ToolPocketSnapshot>();
      var end = new List<EventLogDB.ToolPocketSnapshot>();
      var expectedUse = new Dictionary<string, TimeSpan>();
      var expectedTotalUse = new Dictionary<string, TimeSpan>();
      var expectedLife = new Dictionary<string, TimeSpan>();
      var expectedChange = new HashSet<string>();

      // first a normal use
      start.Add(
        new EventLogDB.ToolPocketSnapshot() { PocketNumber = 0, Tool = "tool1", CurrentUse = TimeSpan.FromSeconds(10), ToolLife = TimeSpan.FromSeconds(100) }
      );
      end.Add(
        new EventLogDB.ToolPocketSnapshot() { PocketNumber = 0, Tool = "tool1", CurrentUse = TimeSpan.FromSeconds(50), ToolLife = TimeSpan.FromSeconds(100) }
      );
      expectedUse["tool1"] = TimeSpan.FromSeconds(50 - 10);
      expectedTotalUse["tool1"] = TimeSpan.FromSeconds(50);
      expectedLife["tool1"] = TimeSpan.FromSeconds(100);

      // now an unused tool
      start.Add(
        new EventLogDB.ToolPocketSnapshot() { PocketNumber = 1, Tool = "tool2", CurrentUse = TimeSpan.FromSeconds(10), ToolLife = TimeSpan.FromSeconds(100) }
      );
      end.Add(
        new EventLogDB.ToolPocketSnapshot() { PocketNumber = 1, Tool = "tool2", CurrentUse = TimeSpan.FromSeconds(10), ToolLife = TimeSpan.FromSeconds(100) }
      );

      // now a tool which is replaced and used
      start.Add(
        new EventLogDB.ToolPocketSnapshot() { PocketNumber = 2, Tool = "tool3", CurrentUse = TimeSpan.FromSeconds(70), ToolLife = TimeSpan.FromSeconds(100) }
      );
      end.Add(
        new EventLogDB.ToolPocketSnapshot() { PocketNumber = 2, Tool = "tool3", CurrentUse = TimeSpan.FromSeconds(20), ToolLife = TimeSpan.FromSeconds(100) }
      );
      expectedUse["tool3"] = TimeSpan.FromSeconds(100 - 70 + 20);
      expectedTotalUse["tool3"] = TimeSpan.FromSeconds(20);
      expectedLife["tool3"] = TimeSpan.FromSeconds(100);
      expectedChange.Add("tool3");

      // now a pocket with two tools
      start.Add(
        new EventLogDB.ToolPocketSnapshot() { PocketNumber = 3, Tool = "tool4", CurrentUse = TimeSpan.FromSeconds(60), ToolLife = TimeSpan.FromSeconds(100) }
      );
      start.Add(
        new EventLogDB.ToolPocketSnapshot() { PocketNumber = 3, Tool = "tool5", CurrentUse = TimeSpan.FromSeconds(80), ToolLife = TimeSpan.FromSeconds(200) }
      );
      end.Add(
        new EventLogDB.ToolPocketSnapshot() { PocketNumber = 3, Tool = "tool4", CurrentUse = TimeSpan.FromSeconds(0), ToolLife = TimeSpan.FromSeconds(100) }
      );
      end.Add(
        new EventLogDB.ToolPocketSnapshot() { PocketNumber = 3, Tool = "tool5", CurrentUse = TimeSpan.FromSeconds(110), ToolLife = TimeSpan.FromSeconds(200) }
      );
      expectedUse["tool4"] = TimeSpan.FromSeconds(100 - 60);
      expectedTotalUse["tool4"] = TimeSpan.Zero;
      expectedLife["tool4"] = TimeSpan.FromSeconds(100);
      expectedChange.Add("tool4");
      expectedUse["tool5"] = TimeSpan.FromSeconds(110 - 80);
      expectedTotalUse["tool5"] = TimeSpan.FromSeconds(110);
      expectedLife["tool5"] = TimeSpan.FromSeconds(200);

      // now a tool which is removed and a new tool added
      start.Add(
        new EventLogDB.ToolPocketSnapshot() { PocketNumber = 4, Tool = "tool6", CurrentUse = TimeSpan.FromSeconds(65), ToolLife = TimeSpan.FromSeconds(100) }
      );
      end.Add(
        new EventLogDB.ToolPocketSnapshot() { PocketNumber = 4, Tool = "tool7", CurrentUse = TimeSpan.FromSeconds(30), ToolLife = TimeSpan.FromSeconds(120) }
      );
      expectedUse["tool6"] = TimeSpan.FromSeconds(100 - 65);
      expectedTotalUse["tool6"] = TimeSpan.Zero;
      expectedLife["tool6"] = TimeSpan.Zero;
      expectedChange.Add("tool6");
      expectedUse["tool7"] = TimeSpan.FromSeconds(30);
      expectedTotalUse["tool7"] = TimeSpan.FromSeconds(30);
      expectedLife["tool7"] = TimeSpan.FromSeconds(120);

      // now a tool which is removed and nothing added
      start.Add(
        new EventLogDB.ToolPocketSnapshot() { PocketNumber = 5, Tool = "tool8", CurrentUse = TimeSpan.FromSeconds(80), ToolLife = TimeSpan.FromSeconds(100) }
      );
      expectedUse["tool8"] = TimeSpan.FromSeconds(100 - 80);
      expectedTotalUse["tool8"] = TimeSpan.Zero;
      expectedLife["tool8"] = TimeSpan.Zero;
      expectedChange.Add("tool8");

      // now a new tool which is appears
      end.Add(
        new EventLogDB.ToolPocketSnapshot() { PocketNumber = 6, Tool = "tool9", CurrentUse = TimeSpan.FromSeconds(15), ToolLife = TimeSpan.FromSeconds(100) }
      );
      expectedUse["tool9"] = TimeSpan.FromSeconds(15);
      expectedTotalUse["tool9"] = TimeSpan.FromSeconds(15);
      expectedLife["tool9"] = TimeSpan.FromSeconds(100);

      // a new unused tool
      end.Add(
        new EventLogDB.ToolPocketSnapshot() { PocketNumber = 7, Tool = "tool10", CurrentUse = TimeSpan.FromSeconds(0), ToolLife = TimeSpan.FromSeconds(100) }
      );

      // same tools in separate pockets are accumulated
      start.Add(
        new EventLogDB.ToolPocketSnapshot() { PocketNumber = 8, Tool = "tool11", CurrentUse = TimeSpan.FromSeconds(50), ToolLife = TimeSpan.FromSeconds(100) }
      );
      end.Add(
        new EventLogDB.ToolPocketSnapshot() { PocketNumber = 8, Tool = "tool11", CurrentUse = TimeSpan.FromSeconds(77), ToolLife = TimeSpan.FromSeconds(100) }
      );
      start.Add(
        new EventLogDB.ToolPocketSnapshot() { PocketNumber = 9, Tool = "tool11", CurrentUse = TimeSpan.FromSeconds(80), ToolLife = TimeSpan.FromSeconds(100) }
      );
      end.Add(
        new EventLogDB.ToolPocketSnapshot() { PocketNumber = 9, Tool = "tool11", CurrentUse = TimeSpan.FromSeconds(13), ToolLife = TimeSpan.FromSeconds(100) }
      );
      expectedUse["tool11"] = TimeSpan.FromSeconds((77 - 50) + (100 - 80) + 13);
      expectedTotalUse["tool11"] = TimeSpan.FromSeconds(77 + 13);
      expectedLife["tool11"] = TimeSpan.FromSeconds(100 + 100);
      expectedChange.Add("tool11");

      EventLogDB.ToolPocketSnapshot.DiffSnapshots(start, end).Should().BeEquivalentTo(
        expectedUse.Select(x => new
        {
          Tool = x.Key,
          Use = new ToolUse() { ToolUseDuringCycle = x.Value, TotalToolUseAtEndOfCycle = expectedTotalUse[x.Key], ConfiguredToolLife = expectedLife[x.Key], ToolChangeOccurred = expectedChange.Contains(x.Key) }
        })
        .ToDictionary(x => x.Tool, x => x.Use)
      );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OverrideMatOnPal(bool firstPalletCycle)
    {
      var now = DateTime.UtcNow.AddHours(-5);

      if (!firstPalletCycle)
      {
        var firstMatId = _jobLog.AllocateMaterialID("uniq1", "part1", 2);
        var firstMat = new EventLogDB.EventLogMaterial() { MaterialID = firstMatId, Process = 1, Face = "1" };
        _jobLog.RecordSerialForMaterialID(firstMat, "aaaa", now);
        _jobLog.RecordLoadEnd(new[] { firstMat }, pallet: "5", lulNum: 3, timeUTC: now.AddMinutes(1), elapsed: TimeSpan.FromMinutes(5), active: TimeSpan.FromMinutes(4));
        _jobLog.RecordMachineEnd(new[] { firstMat }, pallet: "5", statName: "Mach", statNum: 4, program: "proggg", result: "proggg", timeUTC: now.AddMinutes(2), elapsed: TimeSpan.FromMinutes(10), active: TimeSpan.FromMinutes(11));
        _jobLog.CompletePalletCycle(pal: "5", timeUTC: now.AddMinutes(5), foreignID: "");

        now = now.AddMinutes(5).AddSeconds(1);
      }

      // ------------------------------------------------------
      // Material
      // ------------------------------------------------------

      var initiallyLoadedMatProc0 = new EventLogDB.EventLogMaterial()
      {
        MaterialID = _jobLog.AllocateMaterialID("uniq1", "part1", 2),
        Process = 0,
        Face = ""
      };
      var initiallyLoadedMatProc1 = new EventLogDB.EventLogMaterial()
      {
        MaterialID = initiallyLoadedMatProc0.MaterialID,
        Process = 1,
        Face = "1"
      };


      var initialMatAddToQueueTime = now;
      _jobLog.RecordAddMaterialToQueue(initiallyLoadedMatProc0, queue: "rawmat", position: -1, timeUTC: now);
      _jobLog.RecordSerialForMaterialID(initiallyLoadedMatProc0, "bbbb", now);
      _jobLog.RecordPathForProcess(initiallyLoadedMatProc0.MaterialID, process: 1, path: 5);
      _jobLog.NextProcessForQueuedMaterial(initiallyLoadedMatProc0.MaterialID).Should().Be(1);

      now = now.AddMinutes(1);

      var newMatProc0 = new EventLogDB.EventLogMaterial()
      {
        MaterialID = _jobLog.AllocateMaterialID("uniq1", "part1", 2),
        Process = 0,
        Face = ""
      };
      var newMatProc1 = new EventLogDB.EventLogMaterial()
      {
        MaterialID = newMatProc0.MaterialID,
        Process = 1,
        Face = "1"
      };

      var newMatAddToQueueTime = now;
      _jobLog.RecordAddMaterialToQueue(newMatProc0, queue: "rawmat", position: -1, timeUTC: now);
      _jobLog.RecordSerialForMaterialID(newMatProc0, "cccc", now);
      _jobLog.NextProcessForQueuedMaterial(newMatProc0.MaterialID).Should().Be(1);

      now = now.AddMinutes(1);

      // ------------------------------------------------------
      // Original Events
      // ------------------------------------------------------

      var origLog = new List<LogEntry>();

      var loadEndOrigEvts = _jobLog.RecordLoadEnd(new[] { initiallyLoadedMatProc1 }, pallet: "5", lulNum: 2, timeUTC: now, elapsed: TimeSpan.FromMinutes(4), active: TimeSpan.FromMinutes(5));
      loadEndOrigEvts.Count().Should().Be(2);
      loadEndOrigEvts.First().LogType.Should().Be(LogType.RemoveFromQueue);
      loadEndOrigEvts.First().Material.First().MaterialID.Should().Be(initiallyLoadedMatProc1.MaterialID);
      loadEndOrigEvts.First().Material.First().Process.Should().Be(0);
      loadEndOrigEvts.Last().LogType.Should().Be(LogType.LoadUnloadCycle);
      origLog.Add(loadEndOrigEvts.Last());

      var initialMatRemoveQueueTime = now;

      now = now.AddMinutes(1);

      origLog.Add(
        _jobLog.RecordPalletArriveStocker(new[] { initiallyLoadedMatProc1 }, pallet: "5", stockerNum: 5, timeUTC: now, waitForMachine: false)
      );

      now = now.AddMinutes(2);

      origLog.Add(
        _jobLog.RecordPalletDepartStocker(new[] { initiallyLoadedMatProc1 }, pallet: "5", stockerNum: 5, timeUTC: now, waitForMachine: false, elapsed: TimeSpan.FromMinutes(2))
      );

      now = now.AddMinutes(1);

      origLog.Add(
        _jobLog.RecordPalletArriveRotaryInbound(new[] { initiallyLoadedMatProc1 }, pallet: "5", statName: "Mach", statNum: 3, timeUTC: now)
      );

      now = now.AddMinutes(1);

      origLog.Add(
        _jobLog.RecordPalletDepartRotaryInbound(new[] { initiallyLoadedMatProc1 }, pallet: "5", statName: "Mach", statNum: 3, timeUTC: now, elapsed: TimeSpan.FromMinutes(5), rotateIntoWorktable: true)
      );

      now = now.AddMinutes(1);

      origLog.Add(
        _jobLog.RecordMachineStart(new[] { initiallyLoadedMatProc1 }, pallet: "5", statName: "Mach", statNum: 3, program: "prog11", timeUTC: now)
      );

      now = now.AddMinutes(1);

      // ------------------------------------------------------
      // Do the swap
      // ------------------------------------------------------

      var result = _jobLog.SwapMaterialInCurrentPalletCycle(
        pallet: "5",
        oldMatId: initiallyLoadedMatProc1.MaterialID,
        newMatId: newMatProc1.MaterialID,
        oldMatPutInQueue: "quarantine",
        operatorName: "theoper",
        timeUTC: now
      );

      // ------------------------------------------------------
      // Check Logs
      // ------------------------------------------------------

      var initiallyLoadedLogMatProc0 = new LogMaterial(matID: initiallyLoadedMatProc0.MaterialID, uniq: "uniq1", part: "part1", proc: 0, numProc: 2, serial: "bbbb", workorder: "", face: "");
      var newLogMatProc0 = new LogMaterial(matID: newMatProc1.MaterialID, uniq: "uniq1", part: "part1", proc: 0, numProc: 2, serial: "cccc", workorder: "", face: "");

      var expectedGeneralMsg = new LogEntry(
        cntr: 0,
        mat: new[] { initiallyLoadedLogMatProc0, newLogMatProc0 },
        pal: "5",
        ty: LogType.GeneralMessage,
        locName: "Message",
        locNum: 1,
        prog: "MaterialOverride",
        start: false,
        endTime: now,
        result: "Replace bbbb with cccc on pallet 5",
        endOfRoute: false
      );

      var newLog = origLog.Select(TransformLog(initiallyLoadedMatProc1.MaterialID, mat =>
        new LogMaterial(matID: newMatProc1.MaterialID, uniq: "uniq1", proc: 1, part: "part1", numProc: 2, serial: "cccc", workorder: "", face: "1")
      )).ToList();

      result.ChangedLogEntries.Should().BeEquivalentTo(newLog,
        options => options.Excluding(e => e.Counter)
      );

      result.NewLogEntries.Should().BeEquivalentTo(new[] {
        expectedGeneralMsg,
        AddToQueueExpectedEntry(
          mat: initiallyLoadedLogMatProc0,
          cntr: 0,
          queue: "quarantine",
          position: 0,
          timeUTC: now,
          operName: "theoper"
        ),
        RemoveFromQueueExpectedEntry(
          mat: newLogMatProc0,
          cntr: 0,
          queue: "rawmat",
          position: 0,
          elapsedMin: now.Subtract(newMatAddToQueueTime).TotalMinutes,
          timeUTC: now,
          operName: "theoper"
        )
      }, options => options.Excluding(e => e.Counter));


      // log for initiallyLoadedMatProc matches, and importantly has only process 0 as max
      _jobLog.GetLogForMaterial(initiallyLoadedMatProc0.MaterialID).SelectMany(c => c.Material).Select(m => m.Process).Max()
        .Should().Be(0);
      _jobLog.NextProcessForQueuedMaterial(initiallyLoadedLogMatProc0.MaterialID).Should().Be(1);

      _jobLog.GetLogForMaterial(initiallyLoadedMatProc0.MaterialID).Should().BeEquivalentTo(new[] {
        RecordSerialExpectedEntry(mat: initiallyLoadedLogMatProc0, cntr: 0, serial: "bbbb", timeUTC: initialMatAddToQueueTime),
        AddToQueueExpectedEntry(
          mat: initiallyLoadedLogMatProc0,
          cntr: 0,
          queue: "rawmat",
          position: 0,
          timeUTC: initialMatAddToQueueTime
        ),
        RemoveFromQueueExpectedEntry(
          mat: initiallyLoadedLogMatProc0,
          cntr: 0,
          queue: "rawmat",
          position: 0,
          timeUTC: initialMatRemoveQueueTime,
          elapsedMin: initialMatRemoveQueueTime.Subtract(initialMatAddToQueueTime).TotalMinutes
        ),
        expectedGeneralMsg,
        AddToQueueExpectedEntry(
          mat: initiallyLoadedLogMatProc0,
          cntr: 0,
          queue: "quarantine",
          position: 0,
          timeUTC: now,
          operName: "theoper"
        ),
      }, options => options.Excluding(e => e.Counter));

      // log for newMat matches
      _jobLog.GetLogForMaterial(newMatProc1.MaterialID).Should().BeEquivalentTo(
        newLog.Concat(new[] {
          RecordSerialExpectedEntry(mat: newLogMatProc0, cntr: 0, serial: "cccc", timeUTC: newMatAddToQueueTime),
          AddToQueueExpectedEntry(
            mat: newLogMatProc0,
            cntr: 0,
            queue: "rawmat",
            position: 1,
            timeUTC: newMatAddToQueueTime
          ),
          expectedGeneralMsg,
          RemoveFromQueueExpectedEntry(
            mat: newLogMatProc0,
            cntr: 0,
            queue: "rawmat",
            position: 0,
            elapsedMin: now.Subtract(newMatAddToQueueTime).TotalMinutes,
            timeUTC: now,
            operName: "theoper"
          )
      }), options => options.Excluding(c => c.Counter));

      _jobLog.GetLogForMaterial(newMatProc1.MaterialID)
        .Where(e => e.LogType != LogType.MachineCycle && e.LogType != LogType.LoadUnloadCycle && e.LogType != LogType.PalletInStocker && e.LogType != LogType.PalletOnRotaryInbound)
        .SelectMany(e => e.Material)
        .Select(m => m.Process)
        .Max()
        .Should().Be(0);

      _jobLog.GetMaterialDetails(newMatProc0.MaterialID).Paths.Should().BeEquivalentTo(new Dictionary<int, int> {
        {1, 5}
      });
    }

    [Fact]
    public void ErrorsOnBadOverrideMatOnPal()
    {
      var now = DateTime.UtcNow.AddHours(-5);

      var firstMatId = _jobLog.AllocateMaterialID("uniq1", "part1", 2);
      var firstMatProc0 = new EventLogDB.EventLogMaterial() { MaterialID = firstMatId, Process = 0, Face = "" };
      var firstMat = new EventLogDB.EventLogMaterial() { MaterialID = firstMatId, Process = 1, Face = "1" };
      _jobLog.RecordSerialForMaterialID(firstMatProc0, serial: "aaaa", endTimeUTC: now);
      _jobLog.RecordLoadEnd(new[] { firstMat }, pallet: "5", lulNum: 3, timeUTC: now.AddMinutes(1), elapsed: TimeSpan.FromMinutes(5), active: TimeSpan.FromMinutes(4));
      _jobLog.RecordMachineEnd(new[] { firstMat }, pallet: "5", statName: "Mach", statNum: 4, program: "proggg", result: "proggg", timeUTC: now.AddMinutes(2), elapsed: TimeSpan.FromMinutes(10), active: TimeSpan.FromMinutes(11));
      _jobLog.RecordPathForProcess(firstMatId, process: 1, path: 10);

      var differentUniqMatId = _jobLog.AllocateMaterialID("uniq2", "part1", 2);

      now = now.AddMinutes(5).AddSeconds(1);

      _jobLog.Invoking(j => j.SwapMaterialInCurrentPalletCycle(
        pallet: "5",
        oldMatId: 12345,
        newMatId: 98765,
        oldMatPutInQueue: null,
        operatorName: null
      )).Should().Throw<ConflictRequestException>().WithMessage("Unable to find material");

      _jobLog.Invoking(j => j.SwapMaterialInCurrentPalletCycle(
        pallet: "5",
        oldMatId: firstMatId,
        newMatId: differentUniqMatId,
        oldMatPutInQueue: null,
        operatorName: null
      )).Should().Throw<ConflictRequestException>().WithMessage("Overriding material on pallet must use material from the same job");

      var existingPathMatId = _jobLog.AllocateMaterialID("uniq1", "part1", 2);
      _jobLog.RecordPathForProcess(existingPathMatId, process: 1, path: 10);

      _jobLog.Invoking(j => j.SwapMaterialInCurrentPalletCycle(
        pallet: "5",
        oldMatId: firstMatId,
        newMatId: differentUniqMatId,
        oldMatPutInQueue: null,
        operatorName: null
      )).Should().Throw<ConflictRequestException>().WithMessage("Overriding material on pallet must use material from the same job");
    }

    [Fact]
    public void InvalidatesCycle()
    {
      var now = DateTime.UtcNow.AddHours(-5);

      // ------------------------------------------------------
      // Material
      // ------------------------------------------------------

      var matProc0 = new EventLogDB.EventLogMaterial()
      {
        MaterialID = _jobLog.AllocateMaterialID("uniq1", "part1", 2),
        Process = 0,
        Face = ""
      };
      var matProc1 = new EventLogDB.EventLogMaterial()
      {
        MaterialID = matProc0.MaterialID,
        Process = 1,
        Face = "1"
      };


      var initialMatAddToQueueTime = now;
      _jobLog.RecordAddMaterialToQueue(matProc0, queue: "rawmat", position: -1, timeUTC: now);
      _jobLog.RecordSerialForMaterialID(matProc0, "bbbb", now);
      _jobLog.RecordPathForProcess(matProc0.MaterialID, process: 1, path: 5);
      _jobLog.NextProcessForQueuedMaterial(matProc0.MaterialID).Should().Be(1);

      now = now.AddMinutes(1);

      // ------------------------------------------------------
      // Original Events
      // ------------------------------------------------------

      var origLog = new List<LogEntry>();

      var loadEndOrigEvts = _jobLog.RecordLoadEnd(new[] { matProc1 }, pallet: "5", lulNum: 2, timeUTC: now, elapsed: TimeSpan.FromMinutes(4), active: TimeSpan.FromMinutes(5));
      loadEndOrigEvts.Count().Should().Be(2);
      loadEndOrigEvts.First().LogType.Should().Be(LogType.RemoveFromQueue);
      loadEndOrigEvts.First().Material.First().MaterialID.Should().Be(matProc1.MaterialID);
      loadEndOrigEvts.First().Material.First().Process.Should().Be(0);
      loadEndOrigEvts.Last().LogType.Should().Be(LogType.LoadUnloadCycle);
      origLog.Add(loadEndOrigEvts.Last());

      var initialMatRemoveQueueTime = now;

      now = now.AddMinutes(1);

      origLog.Add(
        _jobLog.RecordPalletArriveStocker(new[] { matProc1 }, pallet: "5", stockerNum: 5, timeUTC: now, waitForMachine: false)
      );

      now = now.AddMinutes(2);

      origLog.Add(
        _jobLog.RecordPalletDepartStocker(new[] { matProc1 }, pallet: "5", stockerNum: 5, timeUTC: now, waitForMachine: false, elapsed: TimeSpan.FromMinutes(2))
      );

      now = now.AddMinutes(1);

      origLog.Add(
        _jobLog.RecordPalletArriveRotaryInbound(new[] { matProc1 }, pallet: "5", statName: "Mach", statNum: 3, timeUTC: now)
      );

      now = now.AddMinutes(1);

      origLog.Add(
        _jobLog.RecordPalletDepartRotaryInbound(new[] { matProc1 }, pallet: "5", statName: "Mach", statNum: 3, timeUTC: now, elapsed: TimeSpan.FromMinutes(5), rotateIntoWorktable: true)
      );

      now = now.AddMinutes(1);

      origLog.Add(
        _jobLog.RecordMachineStart(new[] { matProc1 }, pallet: "5", statName: "Mach", statNum: 3, program: "prog11", timeUTC: now)
      );

      now = now.AddMinutes(1);

      _jobLog.NextProcessForQueuedMaterial(matProc0.MaterialID).Should().Be(2);

      // ------------------------------------------------------
      // Invalidate
      // ------------------------------------------------------

      var result = _jobLog.InvalidatePalletCycle(
        eventsToInvalidate: origLog,
        oldMatPutInQueue: "quarantine",
        operatorName: "theoper",
        timeUTC: now
      );

      // ------------------------------------------------------
      // Check Logs
      // ------------------------------------------------------

      var logMatProc0 = new LogMaterial(matID: matProc0.MaterialID, uniq: "uniq1", part: "part1", proc: 0, numProc: 2, serial: "bbbb", workorder: "", face: "");

      var expectedInvalidateMsg = new LogEntry(
        cntr: 0,
        mat: new[] { SetProcInMat(proc: 1)(logMatProc0) },
        pal: "",
        ty: LogType.InvalidateCycle,
        locName: "InvalidateCycle",
        locNum: 1,
        prog: "InvalidateCycle",
        start: false,
        endTime: now,
        result: "Invalidate all events on cycle for pallet 5",
        endOfRoute: false
      );
      expectedInvalidateMsg.ProgramDetails["EditedCounters"] = string.Join(",", origLog.Select(e => e.Counter));

      var newLog = origLog.Select(RemoveActiveTime()).Select(evt =>
      {
        evt.ProgramDetails["PalletCycleInvalidated"] = "1";
        return evt;
      }).ToList();

      result.Should().BeEquivalentTo(new[] {
        expectedInvalidateMsg,
        AddToQueueExpectedEntry(
          mat: logMatProc0,
          cntr: 0,
          queue: "quarantine",
          position: 0,
          timeUTC: now,
          operName: "theoper"
        )
      }, options => options.Excluding(e => e.Counter));

      // log for initiallyLoadedMatProc matches, and importantly has only process 0 as max
      _jobLog.NextProcessForQueuedMaterial(matProc0.MaterialID).Should().Be(1);

      _jobLog.GetLogForMaterial(matProc0.MaterialID).Should().BeEquivalentTo(
        newLog.Concat(
          new[] {
            RecordSerialExpectedEntry(mat: logMatProc0, cntr: 0, serial: "bbbb", timeUTC: initialMatAddToQueueTime),
            AddToQueueExpectedEntry(
              mat: logMatProc0,
              cntr: 0,
              queue: "rawmat",
              position: 0,
              timeUTC: initialMatAddToQueueTime
            ),
            RemoveFromQueueExpectedEntry(
              mat: logMatProc0,
              cntr: 0,
              queue: "rawmat",
              position: 0,
              timeUTC: initialMatRemoveQueueTime,
              elapsedMin: initialMatRemoveQueueTime.Subtract(initialMatAddToQueueTime).TotalMinutes
            ),
            expectedInvalidateMsg,
            AddToQueueExpectedEntry(
              mat: logMatProc0,
              cntr: 0,
              queue: "quarantine",
              position: 0,
              timeUTC: now,
              operName: "theoper"
            ),
          }
        ), options => options.Excluding(e => e.Counter));
    }
    #region Helpers
    private LogEntry AddLogEntry(LogEntry l)
    {
      _jobLog.AddLogEntryFromUnitTest(l);
      return l;
    }

    private System.DateTime AddToDB(IList<LogEntry> logs)
    {
      System.DateTime last = default(System.DateTime);

      foreach (var l in logs)
      {
        _jobLog.AddLogEntryFromUnitTest(l);

        if (l.EndTimeUTC > last)
        {
          last = l.EndTimeUTC;
        }
      }

      return last;
    }

    public static long CheckLog(IList<LogEntry> logs, IList<LogEntry> otherLogs, System.DateTime start)
    {
      logs.Where(l => l.EndTimeUTC >= start).Should().BeEquivalentTo(otherLogs, options =>
        options.Excluding(l => l.Counter)
      );
      return otherLogs.Select(l => l.Counter).Max();
    }

    private LogEntry RecordSerialExpectedEntry(LogMaterial mat, long cntr, string serial, DateTime timeUTC)
    {
      return new LogEntry(
          cntr: cntr,
          mat: new[] { mat },
          pal: "",
          ty: LogType.PartMark,
          locName: "Mark",
          locNum: 1,
          prog: "MARK",
          start: false,
          endTime: timeUTC,
          result: serial,
          endOfRoute: false);
    }

    private LogEntry AddToQueueExpectedEntry(LogMaterial mat, long cntr, string queue, int position, DateTime timeUTC, string operName = null)
    {
      var e = new LogEntry(
          cntr: cntr,
          mat: new[] { mat },
          pal: "",
          ty: LogType.AddToQueue,
          locName: queue,
          locNum: position,
          prog: "",
          start: false,
          endTime: timeUTC,
          result: "",
          endOfRoute: false);
      if (!string.IsNullOrEmpty(operName))
      {
        e.ProgramDetails.Add("operator", operName);
      }
      return e;
    }

    private LogEntry SignalQuarantineExpectedEntry(LogMaterial mat, long cntr, string pal, string queue, DateTime timeUTC, string operName = null)
    {
      var e = new LogEntry(
          cntr: cntr,
          mat: new[] { mat },
          pal: pal,
          ty: LogType.SignalQuarantine,
          locName: queue,
          locNum: -1,
          prog: "QuarantineAfterUnload",
          start: false,
          endTime: timeUTC,
          result: "QuarantineAfterUnload",
          endOfRoute: false);
      if (!string.IsNullOrEmpty(operName))
      {
        e.ProgramDetails.Add("operator", operName);
      }
      return e;
    }

    private LogEntry RemoveFromQueueExpectedEntry(LogMaterial mat, long cntr, string queue, int position, double elapsedMin, DateTime timeUTC, string operName = null)
    {
      var e = new LogEntry(
          cntr: cntr,
          mat: new[] { mat },
          pal: "",
          ty: LogType.RemoveFromQueue,
          locName: queue,
          locNum: position,
          prog: "",
          start: false,
          endTime: timeUTC,
          result: "",
          endOfRoute: false,
          elapsed: TimeSpan.FromMinutes(elapsedMin),
          active: TimeSpan.Zero);
      if (!string.IsNullOrEmpty(operName))
      {
        e.ProgramDetails.Add("operator", operName);
      }
      return e;
    }

    private Func<LogMaterial, LogMaterial> SetSerialInMat(string serial)
    {
      return m => new LogMaterial(
        matID: m.MaterialID,
        uniq: m.JobUniqueStr,
        proc: m.Process,
        part: m.PartName,
        numProc: m.NumProcesses,
        serial: serial,
        workorder: m.Workorder,
        face: m.Face
      );
    }

    private Func<LogMaterial, LogMaterial> SetWorkorderInMat(string work)
    {
      return m => new LogMaterial(
        matID: m.MaterialID,
        uniq: m.JobUniqueStr,
        proc: m.Process,
        part: m.PartName,
        numProc: m.NumProcesses,
        serial: m.Serial,
        workorder: work,
        face: m.Face
      );
    }

    private Func<LogMaterial, LogMaterial> SetProcInMat(int proc)
    {
      return m => new LogMaterial(
        matID: m.MaterialID,
        uniq: m.JobUniqueStr,
        proc: proc,
        part: m.PartName,
        numProc: m.NumProcesses,
        serial: m.Serial,
        workorder: m.Workorder,
        face: m.Face
      );
    }

    private Func<LogEntry, LogEntry> TransformLog(long matID, Func<LogMaterial, LogMaterial> transformMat)
    {
      return copy =>
      {
        var l = new LogEntry(
          cntr: copy.Counter,
          mat: copy.Material.Select(m => m.MaterialID == matID ? transformMat(m) : m),
          pal: copy.Pallet,
          ty: copy.LogType,
          locName: copy.LocationName,
          locNum: copy.LocationNum,
          prog: copy.Program,
          start: copy.StartOfCycle,
          endTime: copy.EndTimeUTC,
          result: copy.Result,
          endOfRoute: copy.EndOfRoute,
          elapsed: copy.ElapsedTime,
          active: copy.ActiveOperationTime
        );
        foreach (var e in copy.ProgramDetails)
        {
          l.ProgramDetails[e.Key] = e.Value;
        }
        return l;
      };
    }

    private static Func<LogEntry, LogEntry> RemoveActiveTime()
    {
      return copy =>
      {
        var l = new LogEntry(
          cntr: copy.Counter,
          mat: copy.Material,
          pal: copy.Pallet,
          ty: copy.LogType,
          locName: copy.LocationName,
          locNum: copy.LocationNum,
          prog: copy.Program,
          start: copy.StartOfCycle,
          endTime: copy.EndTimeUTC,
          result: copy.Result,
          endOfRoute: copy.EndOfRoute,
          elapsed: copy.ElapsedTime,
          active: TimeSpan.Zero
        );
        foreach (var e in copy.ProgramDetails)
        {
          l.ProgramDetails[e.Key] = e.Value;
        }
        return l;
      };
    }
    #endregion
  }

  public class LogOneSerialPerMaterialSpec : IDisposable
  {
    private EventLogDB _jobLog;

    public LogOneSerialPerMaterialSpec()
    {
      var settings = new FMSSettings() { SerialType = SerialType.AssignOneSerialPerMaterial, SerialLength = 10 };
      _jobLog = EventLogDB.Config.InitializeSingleThreadedMemoryDB(settings).OpenConnection();
    }

    public void Dispose()
    {
      _jobLog.Close();
    }

    [Fact]
    public void PendingLoadOneSerialPerMat()
    {
      _jobLog.PendingLoads("pal1").Should().BeEmpty();
      _jobLog.AllPendingLoads().Should().BeEmpty();

      var mat1 = new LogMaterial(
          _jobLog.AllocateMaterialID("unique", "part1", 1), "unique", 1, "part1", 1, "0000000001", "", "face1");
      var mat2 = new LogMaterial(
          _jobLog.AllocateMaterialID("unique2", "part2", 2), "unique2", 2, "part2", 2, "0000000002", "", "face2");
      var mat3 = new LogMaterial(
          _jobLog.AllocateMaterialID("unique3", "part3", 3), "unique3", 3, "part3", 3, "0000000003", "", "face3");
      var mat4 = new LogMaterial(
          _jobLog.AllocateMaterialID("unique4", "part4", 4), "unique4", 4, "part4", 4, "themat4serial", "", "face4");

      var serial1 = FMSSettings.ConvertToBase62(mat1.MaterialID).PadLeft(10, '0');
      var serial2 = FMSSettings.ConvertToBase62(mat2.MaterialID).PadLeft(10, '0');
      var serial3 = FMSSettings.ConvertToBase62(mat3.MaterialID).PadLeft(10, '0');

      var t = DateTime.UtcNow.AddHours(-1);

      //mat4 already has a serial
      _jobLog.RecordSerialForMaterialID(EventLogDB.EventLogMaterial.FromLogMat(mat4), "themat4serial", t.AddMinutes(1));
      var ser4 = new LogEntry(0, new LogMaterial[] { mat4 },
                            "", LogType.PartMark, "Mark", 1, "MARK", false, t.AddMinutes(1), "themat4serial", false);

      var log1 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                            "pal1", LogType.GeneralMessage, "ABC", 1,
                            "prog1", false, t, "result1", false, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(12));
      var log2 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                            "pal2", LogType.MachineCycle, "MC", 1,
                            "prog2", false, t.AddMinutes(20), "result2", false, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(17));

      _jobLog.AddLogEntryFromUnitTest(log1);
      _jobLog.AddLogEntryFromUnitTest(log2);

      _jobLog.AddPendingLoad("pal1", "key1", 5, TimeSpan.FromMinutes(32), TimeSpan.FromMinutes(38), "for1");
      _jobLog.AddPendingLoad("pal1", "key2", 7, TimeSpan.FromMinutes(44), TimeSpan.FromMinutes(49), "for2");

      _jobLog.PendingLoads("pal1").Should().BeEquivalentTo(new[] {
                new EventLogDB.PendingLoad() {
                    Pallet = "pal1",
                    Key = "key1",
                    LoadStation = 5,
                    Elapsed = TimeSpan.FromMinutes(32),
                    ForeignID = "for1",
                    ActiveOperationTime = TimeSpan.FromMinutes(38)
                },
                new EventLogDB.PendingLoad() {
                    Pallet = "pal1",
                    Key = "key2",
                    LoadStation = 7,
                    Elapsed = TimeSpan.FromMinutes(44),
                    ForeignID = "for2",
                    ActiveOperationTime = TimeSpan.FromMinutes(49)
                }
            });
      _jobLog.AllPendingLoads().Should().BeEquivalentTo(new[] {
                new EventLogDB.PendingLoad() {
                    Pallet = "pal1",
                    Key = "key1",
                    LoadStation = 5,
                    Elapsed = TimeSpan.FromMinutes(32),
                    ForeignID = "for1",
                    ActiveOperationTime = TimeSpan.FromMinutes(38)
                },
                new EventLogDB.PendingLoad() {
                    Pallet = "pal1",
                    Key = "key2",
                    LoadStation = 7,
                    Elapsed = TimeSpan.FromMinutes(44),
                    ForeignID = "for2",
                    ActiveOperationTime = TimeSpan.FromMinutes(49)
                }
            });

      var mat = new Dictionary<string, IEnumerable<EventLogDB.EventLogMaterial>>();

      var palCycle = new LogEntry(0, new LogMaterial[] { }, "pal1",
        LogType.PalletCycle, "Pallet Cycle", 1, "", false,
        t.AddMinutes(45), "PalletCycle", false, TimeSpan.Zero, TimeSpan.Zero);

      mat["key1"] = new LogMaterial[] { mat1, mat2 }.Select(EventLogDB.EventLogMaterial.FromLogMat);

      var nLoad1 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                              "pal1", LogType.LoadUnloadCycle, "L/U", 5, "LOAD", false,
                              t.AddMinutes(45).AddSeconds(1), "LOAD", false, TimeSpan.FromMinutes(32), TimeSpan.FromMinutes(38));

      var ser1 = new LogEntry(0, new LogMaterial[] { mat1 },
                            "", LogType.PartMark, "Mark", 1, "MARK", false,
        t.AddMinutes(45).AddSeconds(2), serial1, false);

      var ser2 = new LogEntry(0, new LogMaterial[] { mat2 },
                            "", LogType.PartMark, "Mark", 1, "MARK", false,
        t.AddMinutes(45).AddSeconds(2), serial2, false);

      mat["key2"] = new LogMaterial[] { mat3, mat4 }.Select(EventLogDB.EventLogMaterial.FromLogMat);

      var nLoad2 = new LogEntry(0, new LogMaterial[] { mat3, mat4 },
                              "pal1", LogType.LoadUnloadCycle, "L/U", 7, "LOAD", false,
                              t.AddMinutes(45).AddSeconds(1), "LOAD", false, TimeSpan.FromMinutes(44), TimeSpan.FromMinutes(49));

      var ser3 = new LogEntry(0, new LogMaterial[] { mat3 },
                            "", LogType.PartMark, "Mark", 1, "MARK", false,
        t.AddMinutes(45).AddSeconds(2), serial3, false);

      _jobLog.CompletePalletCycle("pal1", t.AddMinutes(45), "for3", mat, generateSerials: true);

      JobLogTest.CheckLog(new LogEntry[] { ser4, log1, log2, palCycle, nLoad1, nLoad2, ser1, ser2, ser3 },
               _jobLog.GetLogEntries(t.AddMinutes(-10), t.AddHours(1)), t.AddMinutes(-10));

      JobLogTest.CheckEqual(nLoad1, _jobLog.StationLogByForeignID("for1")[0]);
      JobLogTest.CheckEqual(nLoad2, _jobLog.StationLogByForeignID("for2")[0]);
      JobLogTest.CheckEqual(palCycle, _jobLog.StationLogByForeignID("for3")[0]);

      _jobLog.PendingLoads("pal1").Should().BeEmpty();
    }

  }

  public class LogOneSerialPerCycleSpec : IDisposable
  {
    private EventLogDB _jobLog;

    public LogOneSerialPerCycleSpec()
    {
      var settings = new FMSSettings() { SerialType = SerialType.AssignOneSerialPerCycle, SerialLength = 10 };
      _jobLog = EventLogDB.Config.InitializeSingleThreadedMemoryDB(settings).OpenConnection();
    }

    public void Dispose()
    {
      _jobLog.Close();
    }

    [Fact]
    public void PendingLoadOneSerialPerCycle()
    {
      var mat1 = new LogMaterial(
          _jobLog.AllocateMaterialID("unique", "part1", 1), "unique", 1, "part1", 1, "0000000001", "", "face1");
      var mat2 = new LogMaterial(
          _jobLog.AllocateMaterialID("unique2", "part2", 2), "unique2", 2, "part2", 2, "0000000001", "", "face2"); // note mat2 gets same serial as mat1
      var mat3 = new LogMaterial(
          _jobLog.AllocateMaterialID("unique3", "part3", 3), "unique3", 3, "part3", 3, "0000000003", "", "face3");
      var mat4 = new LogMaterial(
          _jobLog.AllocateMaterialID("unique4", "part4", 4), "unique4", 4, "part4", 4, "themat4serial", "", "face4");

      var serial1 = FMSSettings.ConvertToBase62(mat1.MaterialID).PadLeft(10, '0');
      var serial3 = FMSSettings.ConvertToBase62(mat3.MaterialID).PadLeft(10, '0');

      var t = DateTime.UtcNow.AddHours(-1);

      //mat4 already has a serial
      _jobLog.RecordSerialForMaterialID(EventLogDB.EventLogMaterial.FromLogMat(mat4), "themat4serial", t.AddMinutes(1));
      var ser4 = new LogEntry(0, new LogMaterial[] { mat4 },
                            "", LogType.PartMark, "Mark", 1, "MARK", false, t.AddMinutes(1), "themat4serial", false);

      var log1 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                            "pal1", LogType.GeneralMessage, "ABC", 1,
                            "prog1", false, t, "result1", false, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(11));
      var log2 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                            "pal2", LogType.MachineCycle, "MC", 1,
                            "prog2", false, t.AddMinutes(20), "result2", false, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(22));

      _jobLog.AddLogEntryFromUnitTest(log1);
      _jobLog.AddLogEntryFromUnitTest(log2);

      _jobLog.AddPendingLoad("pal1", "key1", 5, TimeSpan.FromMinutes(32), TimeSpan.FromMinutes(38), "for1");
      _jobLog.AddPendingLoad("pal1", "key2", 7, TimeSpan.FromMinutes(44), TimeSpan.FromMinutes(49), "for2");
      _jobLog.AddPendingLoad("pal1", "key3", 6, TimeSpan.FromMinutes(55), TimeSpan.FromMinutes(61), "for2.5");

      var mat = new Dictionary<string, IEnumerable<EventLogDB.EventLogMaterial>>();

      var palCycle = new LogEntry(0, new LogMaterial[] { }, "pal1",
      LogType.PalletCycle, "Pallet Cycle", 1, "", false,
        t.AddMinutes(45), "PalletCycle", false, TimeSpan.Zero, TimeSpan.Zero);

      mat["key1"] = new LogMaterial[] { mat1, mat2 }.Select(EventLogDB.EventLogMaterial.FromLogMat);

      var nLoad1 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                              "pal1", LogType.LoadUnloadCycle, "L/U", 5, "LOAD", false,
                              t.AddMinutes(45).AddSeconds(1), "LOAD", false, TimeSpan.FromMinutes(32), TimeSpan.FromMinutes(38));

      var ser1 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                            "", LogType.PartMark, "Mark", 1, "MARK", false,
        t.AddMinutes(45).AddSeconds(2), serial1, false);

      mat["key2"] = new LogMaterial[] { mat3 }.Select(EventLogDB.EventLogMaterial.FromLogMat);

      var nLoad2 = new LogEntry(0, new LogMaterial[] { mat3 },
                              "pal1", LogType.LoadUnloadCycle, "L/U", 7, "LOAD", false,
                              t.AddMinutes(45).AddSeconds(1), "LOAD", false, TimeSpan.FromMinutes(44), TimeSpan.FromMinutes(49));

      var ser3 = new LogEntry(0, new LogMaterial[] { mat3 },
                            "", LogType.PartMark, "Mark", 1, "MARK", false,
        t.AddMinutes(45).AddSeconds(2), serial3, false);

      mat["key3"] = new LogMaterial[] { mat4 }.Select(EventLogDB.EventLogMaterial.FromLogMat);

      var nLoad3 = new LogEntry(0, new LogMaterial[] { mat4 },
                              "pal1", LogType.LoadUnloadCycle, "L/U", 6, "LOAD", false,
                              t.AddMinutes(45).AddSeconds(1), "LOAD", false, TimeSpan.FromMinutes(55), TimeSpan.FromMinutes(61));

      _jobLog.CompletePalletCycle("pal1", t.AddMinutes(45), "for3", mat, generateSerials: true);

      JobLogTest.CheckLog(new LogEntry[] { ser4, log1, log2, palCycle, nLoad1, nLoad2, nLoad3, ser1, ser3 },
               _jobLog.GetLogEntries(t.AddMinutes(-10), t.AddHours(1)), t.AddMinutes(-10));

      JobLogTest.CheckEqual(nLoad1, _jobLog.StationLogByForeignID("for1")[0]);
      JobLogTest.CheckEqual(nLoad2, _jobLog.StationLogByForeignID("for2")[0]);
      JobLogTest.CheckEqual(palCycle, _jobLog.StationLogByForeignID("for3")[0]);

      _jobLog.PendingLoads("pal1").Should().BeEmpty();
    }
  }


  public class LogStartingMaterialIDSpec : IDisposable
  {
    private Microsoft.Data.Sqlite.SqliteConnection _connection;

    public LogStartingMaterialIDSpec()
    {
      _connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
      _connection.Open();
    }

    public void Dispose()
    {
      _connection.Close();
    }

    [Fact]
    public void ConvertSerials()
    {
      var fixture = new Fixture();
      var matId = fixture.Create<long>();
      var st = new FMSSettings();
      st.ConvertSerialToMaterialID(st.ConvertMaterialIDToSerial(matId)).Should().Be(matId);
    }


    [Fact]
    public void MaterialIDs()
    {
      var logDB = EventLogDB.Config.InitializeSingleThreadedMemoryDB(new FMSSettings() { StartingSerial = "AbCd12" }, _connection, createTables: true).OpenConnection();
      long m1 = logDB.AllocateMaterialID("U1", "P1", 52);
      long m2 = logDB.AllocateMaterialID("U2", "P2", 66);
      long m3 = logDB.AllocateMaterialID("U3", "P3", 566);
      m1.Should().Be(33_152_428_148);
      m2.Should().Be(33_152_428_149);
      m3.Should().Be(33_152_428_150);

      logDB.GetMaterialDetails(m1).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = m1,
        JobUnique = "U1",
        PartName = "P1",
        NumProcesses = 52,
        Paths = new Dictionary<int, int>()
      });
    }

    [Fact]
    public void ErrorsTooLarge()
    {
      Action act = () =>
        EventLogDB.Config.InitializeSingleThreadedMemoryDB(new FMSSettings() { StartingSerial = "A000000000" });
      act.Should().Throw<Exception>().WithMessage("Serial A000000000 is too large");
    }

    [Fact]
    public void AdjustsStartingSerial()
    {
      var logFromCreate =
        EventLogDB.Config.InitializeSingleThreadedMemoryDB(new FMSSettings() { StartingSerial = "AbCd12" }, _connection, createTables: true).OpenConnection();

      long m1 = logFromCreate.AllocateMaterialID("U1", "P1", 52);
      m1.Should().Be(33_152_428_148);

      var logFromUpgrade =
        EventLogDB.Config.InitializeSingleThreadedMemoryDB(new FMSSettings() { StartingSerial = "B3t24s" }, _connection, createTables: false).OpenConnection();

      long m2 = logFromUpgrade.AllocateMaterialID("U1", "P1", 2);
      long m3 = logFromUpgrade.AllocateMaterialID("U2", "P2", 4);
      m2.Should().Be(33_948_163_268);
      m3.Should().Be(33_948_163_269);
    }

    [Fact]
    public void AvoidsAdjustingSerialBackwards()
    {
      var logFromCreate =
        EventLogDB.Config.InitializeSingleThreadedMemoryDB(new FMSSettings() { StartingSerial = "AbCd12" }, _connection, createTables: true).OpenConnection();

      long m1 = logFromCreate.AllocateMaterialID("U1", "P1", 52);
      m1.Should().Be(33_152_428_148);

      var logFromUpgrade =
        EventLogDB.Config.InitializeSingleThreadedMemoryDB(new FMSSettings() { StartingSerial = "w53122" }, _connection, createTables: false).OpenConnection();

      long m2 = logFromUpgrade.AllocateMaterialID("U1", "P1", 2);
      m2.Should().Be(33_152_428_149);
    }
  }
}
