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
using FluentAssertions;
using AutoFixture;
using System.Collections.Immutable;
using Germinate;

namespace MachineWatchTest
{
  public class JobComparisonHelpers
  {
    public static void CheckEqual(LogEntry x, LogEntry y)
    {
      x.Should()
        .BeEquivalentTo(y, options => options.Excluding(l => l.Counter).ComparingByMembers<LogEntry>());
    }
  }

  public class JobLogTest : JobComparisonHelpers, IDisposable
  {
    public static readonly ProcPathInfo EmptyPath = new ProcPathInfo()
    {
      PalletNums = ImmutableList<int>.Empty,
      Load = ImmutableList<int>.Empty,
      Unload = ImmutableList<int>.Empty,
      ExpectedLoadTime = TimeSpan.Zero,
      ExpectedUnloadTime = TimeSpan.Zero,
      Stops = ImmutableList<MachiningStop>.Empty,
      SimulatedStartingUTC = DateTime.MinValue,
      SimulatedAverageFlowTime = TimeSpan.Zero,
      PartsPerPallet = 1
    };

    private RepositoryConfig _repoCfg;
    private IRepository _jobLog;
    private Fixture _fixture;

    public JobLogTest()
    {
      _repoCfg = RepositoryConfig.InitializeSingleThreadedMemoryDB(
        new SerialSettings() { ConvertMaterialIDToSerial = (id) => id.ToString() }
      );
      _jobLog = _repoCfg.OpenConnection();
      _fixture = new Fixture();
      _fixture.Customizations.Add(new ImmutableSpecimenBuilder());
      _fixture.Customizations.Add(new InjectNullValuesForNullableTypesSpecimenBuilder());
    }

    public void Dispose()
    {
      _repoCfg.CloseMemoryConnection();
    }

    [Fact]
    public void MaterialIDs()
    {
      long m1 = _jobLog.AllocateMaterialID("U1", "P1", 52);
      long m2 = _jobLog.AllocateMaterialIDAndGenerateSerial(
        "U2",
        "P2",
        66,
        DateTime.UtcNow,
        out var serialLogEntry
      );
      // no serial is actually generated since the setting is disabled
      serialLogEntry.Should().BeNull();
      long m3 = _jobLog.AllocateMaterialID("U3", "P3", 566);
      m1.Should().Be(1);
      m2.Should().Be(2);
      m3.Should().Be(3);

      _jobLog.RecordPathForProcess(m1, 1, 60);
      _jobLog.RecordPathForProcess(m1, 2, 88);
      _jobLog.RecordPathForProcess(m2, 6, 5);
      _jobLog.RecordPathForProcess(m2, 6, 10);

      _jobLog
        .GetMaterialDetails(m1)
        .Should()
        .BeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = m1,
            JobUnique = "U1",
            PartName = "P1",
            NumProcesses = 52,
            Paths = ImmutableDictionary<int, int>.Empty.Add(1, 60).Add(2, 88)
          },
          options => options.ComparingByMembers<MaterialDetails>()
        );

      _jobLog
        .GetMaterialDetails(m2)
        .Should()
        .BeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = m2,
            JobUnique = "U2",
            PartName = "P2",
            NumProcesses = 66,
            Paths = ImmutableDictionary<int, int>.Empty.Add(6, 10)
          },
          options => options.ComparingByMembers<MaterialDetails>()
        );

      _jobLog
        .GetMaterialDetails(m3)
        .Should()
        .BeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = m3,
            JobUnique = "U3",
            PartName = "P3",
            NumProcesses = 566,
          }
        );

      long m4 = _jobLog.AllocateMaterialIDForCasting("P4");
      _jobLog
        .GetMaterialDetails(m4)
        .Should()
        .BeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = m4,
            PartName = "P4",
            NumProcesses = 1,
          }
        );

      _jobLog.SetDetailsForMaterialID(m4, "U4", "P4444", 77);
      _jobLog
        .GetMaterialDetails(m4)
        .Should()
        .BeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = m4,
            JobUnique = "U4",
            PartName = "P4444",
            NumProcesses = 77,
          }
        );

      _jobLog.GetWorkordersForUnique("U1").Should().BeEmpty();

      _jobLog.RecordWorkorderForMaterialID(m1, 1, "work1");
      _jobLog.RecordWorkorderForMaterialID(m2, 1, "work2");
      _jobLog.RecordWorkorderForMaterialID(m3, 1, "work1");

      _jobLog
        .GetMaterialForWorkorder("work1")
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new MaterialDetails
            {
              MaterialID = m1,
              JobUnique = "U1",
              PartName = "P1",
              NumProcesses = 52,
              Workorder = "work1",
              Paths = ImmutableDictionary<int, int>.Empty.Add(1, 60).Add(2, 88)
            },
            new MaterialDetails()
            {
              MaterialID = m3,
              JobUnique = "U3",
              PartName = "P3",
              NumProcesses = 566,
              Workorder = "work1",
            }
          }
        );

      _jobLog.GetWorkordersForUnique("U1").Should().BeEquivalentTo(new[] { "work1" });
      _jobLog.GetWorkordersForUnique("unused").Should().BeEmpty();

      _jobLog
        .GetMaterialForJobUnique("U1")
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new MaterialDetails
            {
              MaterialID = m1,
              JobUnique = "U1",
              PartName = "P1",
              NumProcesses = 52,
              Workorder = "work1",
              Paths = ImmutableDictionary<int, int>.Empty.Add(1, 60).Add(2, 88)
            },
          },
          options => options.ComparingByMembers<MaterialDetails>()
        );

      _jobLog.GetMaterialForJobUnique("unused").Should().BeEmpty();

      _jobLog.CreateMaterialID(matID: 555, unique: "55555", part: "part5", numProc: 12);
      _jobLog
        .GetMaterialDetails(555)
        .Should()
        .BeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = 555,
            JobUnique = "55555",
            PartName = "part5",
            NumProcesses = 12,
          }
        );

      // update to new values
      _jobLog.CreateMaterialID(matID: 555, unique: "newuniq", part: "newpart5", numProc: 44);
      _jobLog
        .GetMaterialDetails(555)
        .Should()
        .BeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = 555,
            JobUnique = "newuniq",
            PartName = "newpart5",
            NumProcesses = 44,
          }
        );

      _jobLog.CreateMaterialID(matID: 666, unique: null, part: "abc", numProc: 32);
      _jobLog
        .GetMaterialDetails(666)
        .Should()
        .BeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = 666,
            JobUnique = null,
            PartName = "abc",
            NumProcesses = 32
          }
        );
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
        _jobLog.AllocateMaterialID("grgaegr", "pp2", 23),
        "grgaegr",
        7,
        "pp2",
        23,
        "",
        "",
        "22"
      );
      LogMaterial mat19 = new LogMaterial(
        _jobLog.AllocateMaterialID("unique", "pp1", 53),
        "unique",
        2,
        "pp1",
        53,
        "",
        "",
        "55"
      );

      var loadStartActualCycle = _jobLog.RecordLoadStart(
        mats: new[] { mat1, mat19 }.Select(EventLogMaterial.FromLogMat),
        pallet: 55,
        lulNum: 2,
        timeUTC: start.AddHours(1)
      );
      loadStartActualCycle
        .Should()
        .BeEquivalentTo(
          new LogEntry(
            loadStartActualCycle.Counter,
            new LogMaterial[] { mat1, mat19 },
            55,
            LogType.LoadUnloadCycle,
            "L/U",
            2,
            "LOAD",
            true,
            start.AddHours(1),
            "LOAD"
          ),
          options => options.ComparingByMembers<LogEntry>()
        );
      logs.Add(loadStartActualCycle);
      logsForMat1.Add(loadStartActualCycle);

      var mat2 = new LogMaterial(
        _jobLog.AllocateMaterialID("ahre", "gewoiweg", 13),
        "ahre",
        1,
        "gewoiweg",
        13,
        "",
        "",
        "22"
      );
      var mat15 = new LogMaterial(
        _jobLog.AllocateMaterialID("qghr4e", "ppp532", 14),
        "qghr4e",
        1,
        "ppp532",
        14,
        "",
        "",
        "22"
      );
      var matLoc2Face1 = new LogMaterial(
        _jobLog.AllocateMaterialID("loc2", "face1", 14),
        "loc2",
        3,
        "face1",
        14,
        "",
        "",
        "1"
      );
      var matLoc2Face2 = new LogMaterial(
        _jobLog.AllocateMaterialID("loc2", "face2", 14),
        "loc2",
        4,
        "face2",
        14,
        "",
        "",
        "2"
      );

      var loadEndActualCycle = _jobLog.RecordLoadEnd(
        toLoad: new[]
        {
          new MaterialToLoadOntoPallet()
          {
            LoadStation = 111,
            Elapsed = TimeSpan.FromMinutes(11122),
            Faces = ImmutableList.Create(
              new MaterialToLoadOntoFace()
              {
                FaceNum = 22,
                Process = mat2.Process,
                Path = null,
                MaterialIDs = ImmutableList.Create(mat2.MaterialID, mat15.MaterialID),
                ActiveOperationTime = TimeSpan.FromSeconds(111),
              }
            )
          },
          new MaterialToLoadOntoPallet()
          {
            LoadStation = 222,
            Elapsed = TimeSpan.FromMinutes(22211),
            Faces = ImmutableList.Create(
              new MaterialToLoadOntoFace()
              {
                FaceNum = int.Parse(matLoc2Face1.Face),
                Process = matLoc2Face1.Process,
                Path = 33,
                MaterialIDs = ImmutableList.Create(matLoc2Face1.MaterialID),
                ActiveOperationTime = TimeSpan.FromSeconds(2222),
              },
              new MaterialToLoadOntoFace()
              {
                FaceNum = int.Parse(matLoc2Face2.Face),
                Process = matLoc2Face2.Process,
                Path = 44,
                MaterialIDs = ImmutableList.Create(matLoc2Face2.MaterialID),
                ActiveOperationTime = TimeSpan.FromSeconds(3333),
              }
            )
          }
        },
        pallet: 1234,
        timeUTC: start.AddHours(3)
      );
      loadEndActualCycle
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new LogEntry(
              -1,
              new LogMaterial[] { mat2, mat15 },
              1234,
              LogType.LoadUnloadCycle,
              "L/U",
              111,
              "LOAD",
              false,
              start.AddHours(3),
              "LOAD",
              TimeSpan.FromMinutes(11122),
              TimeSpan.FromSeconds(111)
            ),
            new LogEntry(
              -1,
              new LogMaterial[] { matLoc2Face1 },
              1234,
              LogType.LoadUnloadCycle,
              "L/U",
              222,
              "LOAD",
              false,
              start.AddHours(3),
              "LOAD",
              TimeSpan.FromMinutes(22211),
              TimeSpan.FromSeconds(2222)
            ),
            new LogEntry(
              -1,
              new LogMaterial[] { matLoc2Face2 },
              1234,
              LogType.LoadUnloadCycle,
              "L/U",
              222,
              "LOAD",
              false,
              start.AddHours(3),
              "LOAD",
              TimeSpan.FromMinutes(22211),
              TimeSpan.FromSeconds(3333)
            ),
          },
          options => options.ComparingByMembers<LogEntry>().Excluding(x => x.Counter)
        );
      logs.AddRange(loadEndActualCycle);
      logsForMat2.Add(loadEndActualCycle.First());
      _jobLog.ToolPocketSnapshotForCycle(loadEndActualCycle.First().Counter).Should().BeEmpty();
      _jobLog
        .GetMaterialDetails(matLoc2Face1.MaterialID)
        .Paths.Should()
        .BeEquivalentTo(new Dictionary<int, int>() { { matLoc2Face1.Process, 33 } });
      _jobLog
        .GetMaterialDetails(matLoc2Face2.MaterialID)
        .Paths.Should()
        .BeEquivalentTo(new Dictionary<int, int>() { { matLoc2Face2.Process, 44 } });

      var arriveStocker = _jobLog.RecordPalletArriveStocker(
        mats: new[] { mat2 }.Select(EventLogMaterial.FromLogMat),
        pallet: 4455,
        stockerNum: 23,
        waitForMachine: true,
        timeUTC: start.AddHours(4)
      );
      arriveStocker
        .Should()
        .BeEquivalentTo(
          new LogEntry(
            -1,
            mat: new[] { mat2 },
            pal: 4455,
            ty: LogType.PalletInStocker,
            locName: "Stocker",
            locNum: 23,
            prog: "Arrive",
            start: true,
            endTime: start.AddHours(4),
            result: "WaitForMachine"
          ),
          options => options.Excluding(l => l.Counter).ComparingByMembers<LogEntry>()
        );
      logs.Add(arriveStocker);
      logsForMat2.Add(arriveStocker);

      var departStocker = _jobLog.RecordPalletDepartStocker(
        mats: new[] { mat2, mat15 }.Select(EventLogMaterial.FromLogMat),
        pallet: 2354,
        stockerNum: 34,
        waitForMachine: true,
        timeUTC: start.AddHours(4).AddMinutes(10),
        elapsed: TimeSpan.FromMinutes(10)
      );
      departStocker
        .Should()
        .BeEquivalentTo(
          new LogEntry(
            -1,
            mat: new[] { mat2, mat15 },
            pal: 2354,
            ty: LogType.PalletInStocker,
            locName: "Stocker",
            locNum: 34,
            prog: "Depart",
            start: false,
            endTime: start.AddHours(4).AddMinutes(10),
            result: "WaitForMachine",
            elapsed: TimeSpan.FromMinutes(10),
            active: TimeSpan.Zero
          ),
          options => options.Excluding(l => l.Counter).ComparingByMembers<LogEntry>()
        );
      logs.Add(departStocker);
      logsForMat2.Add(departStocker);

      var arriveInbound = _jobLog.RecordPalletArriveRotaryInbound(
        mats: new[] { mat15 }.Select(EventLogMaterial.FromLogMat),
        pallet: 6578,
        statName: "thestat",
        statNum: 77,
        timeUTC: start.AddHours(4).AddMinutes(20)
      );
      arriveInbound
        .Should()
        .BeEquivalentTo(
          new LogEntry(
            -1,
            mat: new[] { mat15 },
            pal: 6578,
            ty: LogType.PalletOnRotaryInbound,
            locName: "thestat",
            locNum: 77,
            prog: "Arrive",
            start: true,
            endTime: start.AddHours(4).AddMinutes(20),
            result: "Arrive"
          ),
          options => options.Excluding(l => l.Counter).ComparingByMembers<LogEntry>()
        );
      logs.Add(arriveInbound);

      var departInbound = _jobLog.RecordPalletDepartRotaryInbound(
        mats: new[] { mat15 }.Select(EventLogMaterial.FromLogMat),
        pallet: 2434,
        statName: "thestat2",
        statNum: 88,
        rotateIntoWorktable: true,
        timeUTC: start.AddHours(4).AddMinutes(45),
        elapsed: TimeSpan.FromMinutes(25)
      );
      departInbound
        .Should()
        .BeEquivalentTo(
          new LogEntry(
            -1,
            mat: new[] { mat15 },
            pal: 2434,
            ty: LogType.PalletOnRotaryInbound,
            locName: "thestat2",
            locNum: 88,
            prog: "Depart",
            result: "RotateIntoWorktable",
            start: false,
            endTime: start.AddHours(4).AddMinutes(45),
            elapsed: TimeSpan.FromMinutes(25),
            active: TimeSpan.Zero
          ),
          options => options.Excluding(l => l.Counter).ComparingByMembers<LogEntry>()
        );
      logs.Add(departInbound);

      var machineStartPockets = _fixture.CreateMany<ToolSnapshot>(40);
      var machineStartActualCycle = _jobLog.RecordMachineStart(
        mats: new[] { mat15 }.Select(EventLogMaterial.FromLogMat),
        pallet: 12,
        statName: "ssssss",
        statNum: 152,
        program: "progggg",
        pockets: machineStartPockets,
        timeUTC: start.AddHours(5).AddMinutes(10)
      );
      machineStartActualCycle
        .Should()
        .BeEquivalentTo(
          new LogEntry(
            machineStartActualCycle.Counter,
            new LogMaterial[] { mat15 },
            12,
            LogType.MachineCycle,
            "ssssss",
            152,
            "progggg",
            true,
            start.AddHours(5).AddMinutes(10),
            ""
          ),
          options => options.ComparingByMembers<LogEntry>()
        );
      logs.Add(machineStartActualCycle);
      _jobLog
        .ToolPocketSnapshotForCycle(machineStartActualCycle.Counter)
        .Should()
        .BeEquivalentTo(machineStartPockets);

      var machineEndUsage = _fixture.CreateMany<ToolUse>(40);
      var machineEndActualCycle = _jobLog.RecordMachineEnd(
        mats: new[] { mat2 }.Select(EventLogMaterial.FromLogMat),
        pallet: 44,
        statName: "xxx",
        statNum: 177,
        program: "progggg",
        result: "4444",
        timeUTC: start.AddHours(5).AddMinutes(19),
        elapsed: TimeSpan.FromMinutes(12),
        active: TimeSpan.FromMinutes(99),
        extraData: new Dictionary<string, string>() { { "aa", "AA" }, { "bb", "BB" } },
        tools: machineEndUsage.ToImmutableList(),
        deleteToolSnapshotsFromCntr: machineStartActualCycle.Counter
      );
      var machineEndExpectedCycle = new LogEntry(
        machineEndActualCycle.Counter,
        new LogMaterial[] { mat2 },
        44,
        LogType.MachineCycle,
        "xxx",
        177,
        "progggg",
        false,
        start.AddHours(5).AddMinutes(19),
        "4444",
        TimeSpan.FromMinutes(12),
        TimeSpan.FromMinutes(99)
      );
      machineEndExpectedCycle = machineEndExpectedCycle with
      {
        ProgramDetails = ImmutableDictionary<string, string>.Empty.Add("aa", "AA").Add("bb", "BB"),
        Tools = machineEndUsage.ToImmutableList()
      };
      machineEndActualCycle
        .Should()
        .BeEquivalentTo(machineEndExpectedCycle, options => options.ComparingByMembers<LogEntry>());
      logs.Add(machineEndActualCycle);
      logsForMat2.Add(machineEndActualCycle);
      // start snapshot deleted
      _jobLog.ToolPocketSnapshotForCycle(machineStartActualCycle.Counter).Should().BeEmpty();

      var unloadStartActualCycle = _jobLog.RecordUnloadStart(
        mats: new[] { mat15, mat19 }.Select(EventLogMaterial.FromLogMat),
        pallet: 66,
        lulNum: 87,
        timeUTC: start.AddHours(6).AddMinutes(10)
      );
      unloadStartActualCycle
        .Should()
        .BeEquivalentTo(
          new LogEntry(
            unloadStartActualCycle.Counter,
            new LogMaterial[] { mat15, mat19 },
            66,
            LogType.LoadUnloadCycle,
            "L/U",
            87,
            "UNLOAD",
            true,
            start.AddHours(6).AddMinutes(10),
            "UNLOAD"
          ),
          options => options.ComparingByMembers<LogEntry>()
        );
      logs.Add(unloadStartActualCycle);

      var unloadEndActualCycle = _jobLog.RecordUnloadEnd(
        mats: new[] { mat2, mat19 }.Select(EventLogMaterial.FromLogMat),
        pallet: 3,
        lulNum: 14,
        timeUTC: start.AddHours(7),
        elapsed: TimeSpan.FromMinutes(152),
        active: TimeSpan.FromMinutes(55)
      );
      unloadEndActualCycle
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new LogEntry(
              unloadEndActualCycle.First().Counter,
              new LogMaterial[] { mat2, mat19 },
              3,
              LogType.LoadUnloadCycle,
              "L/U",
              14,
              "UNLOAD",
              false,
              start.AddHours(7),
              "UNLOAD",
              TimeSpan.FromMinutes(152),
              TimeSpan.FromMinutes(55)
            )
          },
          options => options.ComparingByMembers<LogEntry>()
        );
      logs.Add(unloadEndActualCycle.First());
      logsForMat2.Add(unloadEndActualCycle.First());

      // ----- check loading of logs -----

      Assert.Equal(start.AddHours(7), _jobLog.MaxLogDate());

      IList<LogEntry> otherLogs = null;

      otherLogs = _jobLog.GetLogEntries(start, DateTime.UtcNow).ToList();
      CheckLog(logs, otherLogs, start);

      otherLogs = _jobLog.GetLogEntries(start.AddHours(5), DateTime.UtcNow).ToList();
      CheckLog(logs, otherLogs, start.AddHours(5));

      otherLogs = _jobLog.GetRecentLog(loadEndActualCycle.Last().Counter).ToList();
      CheckLog(logs, otherLogs, start.AddHours(4));

      otherLogs = _jobLog
        .GetRecentLog(unloadStartActualCycle.Counter, unloadStartActualCycle.EndTimeUTC)
        .ToList();
      CheckLog(logs, otherLogs, start.AddHours(6.5));

      _jobLog
        .Invoking(
          j => j.GetRecentLog(unloadStartActualCycle.Counter, new DateTime(2000, 2, 3, 4, 5, 6)).ToList()
        )
        .Should()
        .Throw<ConflictRequestException>(
          "Counter " + unloadStartActualCycle.Counter.ToString() + " has different end time"
        );

      otherLogs = _jobLog.GetRecentLog(unloadEndActualCycle.First().Counter).ToList();
      Assert.Equal(0, otherLogs.Count);

      foreach (var c in logs)
        Assert.True(
          _jobLog.CycleExists(c.EndTimeUTC, c.Pallet, c.LogType, c.LocationName, c.LocationNum),
          "Checking " + c.EndTimeUTC.ToString()
        );

      Assert.False(_jobLog.CycleExists(DateTime.Parse("4/6/2011"), 123, LogType.MachineCycle, "MC", 3));

      CheckLog(logsForMat1, _jobLog.GetLogForMaterial(1), start);
      CheckLog(
        logsForMat1.Concat(logsForMat2).ToList(),
        _jobLog.GetLogForMaterial(new[] { 1, mat2.MaterialID }),
        start
      );
      _jobLog.GetLogForMaterial(18).Should().BeEmpty();

      var markLog = _jobLog.RecordSerialForMaterialID(
        EventLogMaterial.FromLogMat(mat1),
        "ser1",
        start.AddHours(8)
      );
      logsForMat1.Add(
        new LogEntry(
          -1,
          new LogMaterial[] { mat1 },
          0,
          LogType.PartMark,
          "Mark",
          1,
          "MARK",
          false,
          start.AddHours(8),
          "ser1"
        )
      );
      logsForMat1 = logsForMat1.Select(TransformLog(mat1.MaterialID, SetSerialInMat("ser1"))).ToList();
      logs = logs.Select(TransformLog(mat1.MaterialID, SetSerialInMat("ser1"))).ToList();
      mat1 = SetSerialInMat("ser1")(mat1);
      CheckLog(logsForMat1, _jobLog.GetLogForSerial("ser1").ToList(), start);
      _jobLog.GetLogForSerial("ser2").Should().BeEmpty();

      var orderLog = _jobLog.RecordWorkorderForMaterialID(EventLogMaterial.FromLogMat(mat1), "work1");
      logsForMat1.Add(
        new LogEntry(
          -1,
          new LogMaterial[] { mat1 },
          0,
          LogType.OrderAssignment,
          "Order",
          1,
          "",
          false,
          orderLog.EndTimeUTC,
          "work1"
        )
      );
      logsForMat1 = logsForMat1.Select(TransformLog(mat1.MaterialID, SetWorkorderInMat("work1"))).ToList();
      logs = logs.Select(TransformLog(mat1.MaterialID, SetWorkorderInMat("work1"))).ToList();
      mat1 = SetWorkorderInMat("work1")(mat1);
      var finalize = _jobLog.RecordWorkorderComment("work1", "ccc", null);
      CheckLog(logsForMat1.Append(finalize).ToList(), _jobLog.GetLogForWorkorder("work1").ToList(), start);
      _jobLog.GetLogForWorkorder("work2").Should().BeEmpty();

      CheckLog(logsForMat1, _jobLog.GetLogForJobUnique(mat1.JobUniqueStr).ToList(), start);
      _jobLog.GetLogForJobUnique("sofusadouf").Should().BeEmpty();

      //inspection, wash, and general
      var inspCompLog = _jobLog.RecordInspectionCompleted(
        EventLogMaterial.FromLogMat(mat1),
        5,
        "insptype1",
        true,
        new Dictionary<string, string> { { "a", "aaa" }, { "b", "bbb" } },
        TimeSpan.FromMinutes(100),
        TimeSpan.FromMinutes(5)
      );
      var expectedInspLog = new LogEntry(
        -1,
        new LogMaterial[] { mat1 },
        0,
        LogType.InspectionResult,
        "Inspection",
        5,
        "insptype1",
        false,
        inspCompLog.EndTimeUTC,
        "True",
        TimeSpan.FromMinutes(100),
        TimeSpan.FromMinutes(5)
      );
      expectedInspLog %= e => e.ProgramDetails.Add("a", "aaa");
      expectedInspLog %= e => e.ProgramDetails.Add("b", "bbb");
      logsForMat1.Add(expectedInspLog);

      var washLog = _jobLog.RecordCloseoutCompleted(
        EventLogMaterial.FromLogMat(mat1),
        7,
        "Closety",
        new Dictionary<string, string> { { "z", "zzz" }, { "y", "yyy" } },
        TimeSpan.FromMinutes(44),
        TimeSpan.FromMinutes(9)
      );
      var expectedWashLog = new LogEntry(
        -1,
        new LogMaterial[] { mat1 },
        0,
        LogType.CloseOut,
        "CloseOut",
        7,
        "Closety",
        false,
        washLog.EndTimeUTC,
        "",
        TimeSpan.FromMinutes(44),
        TimeSpan.FromMinutes(9)
      );
      expectedWashLog %= e => e.ProgramDetails.Add("z", "zzz");
      expectedWashLog %= e => e.ProgramDetails.Add("y", "yyy");
      logsForMat1.Add(expectedWashLog);

      var generalLog = _jobLog.RecordGeneralMessage(
        EventLogMaterial.FromLogMat(mat1),
        "The program msg",
        "The result msg",
        extraData: new Dictionary<string, string> { { "extra1", "value1" } }
      );
      var expectedGeneralLog = new LogEntry(
        -1,
        new LogMaterial[] { mat1 },
        0,
        LogType.GeneralMessage,
        "Message",
        1,
        "The program msg",
        false,
        generalLog.EndTimeUTC,
        "The result msg"
      );
      expectedGeneralLog %= e => e.ProgramDetails["extra1"] = "value1";
      logsForMat1.Add(expectedGeneralLog);

      var notesLog = _jobLog.RecordOperatorNotes(
        mat1.MaterialID,
        mat1.Process,
        "The notes content",
        "Opername"
      );
      var expectedNotesLog = new LogEntry(
        -1,
        new LogMaterial[]
        {
          new LogMaterial(
            mat1.MaterialID,
            mat1.JobUniqueStr,
            mat1.Process,
            mat1.PartName,
            mat1.NumProcesses,
            mat1.Serial,
            mat1.Workorder,
            ""
          )
        },
        0,
        LogType.GeneralMessage,
        "Message",
        1,
        "OperatorNotes",
        false,
        notesLog.EndTimeUTC,
        "Operator Notes"
      );
      expectedNotesLog %= e =>
      {
        e.ProgramDetails["note"] = "The notes content";
        e.ProgramDetails["operator"] = "Opername";
      };
      logsForMat1.Add(expectedNotesLog);

      CheckLog(logsForMat1, _jobLog.GetLogForJobUnique(mat1.JobUniqueStr).ToList(), start);
    }

    [Fact]
    public void LookupByPallet()
    {
      _jobLog.CurrentPalletLog(123).Should().BeEmpty();
      _jobLog.CurrentPalletLog(4).Should().BeEmpty();
      Assert.Equal(DateTime.MinValue, _jobLog.LastPalletCycleTime(212));

      var pal1Initial = new List<LogEntry>();
      var pal1Cycle = new List<LogEntry>();
      var pal2Cycle = new List<LogEntry>();

      var mat1 = new LogMaterial(
        _jobLog.AllocateMaterialID("unique", "part1", 2),
        "unique",
        1,
        "part1",
        2,
        "",
        "",
        "face1"
      );
      var mat2 = new LogMaterial(
        _jobLog.AllocateMaterialID("unique2", "part2", 2),
        "unique2",
        2,
        "part2",
        2,
        "",
        "",
        "face2"
      );

      DateTime pal1InitialTime = DateTime.UtcNow.AddHours(-4);

      // *********** Add load cycle on pal1
      pal1Initial.Add(
        new LogEntry(
          0,
          new LogMaterial[] { mat1, mat2 },
          1,
          LogType.LoadUnloadCycle,
          "Load",
          2,
          "prog1",
          true, //start of event
          pal1InitialTime,
          "result"
        )
      ); //end of route
      pal1Initial.Add(
        new LogEntry(
          0,
          new LogMaterial[] { mat1, mat2 },
          1,
          LogType.LoadUnloadCycle,
          "Load",
          2,
          "prog1",
          false, //start of event
          pal1InitialTime.AddMinutes(5),
          "result"
        )
      ); //end of route

      // *********** Add machine cycle on pal1
      pal1Initial.Add(
        new LogEntry(
          0,
          new LogMaterial[] { mat1, mat2 },
          1,
          LogType.MachineCycle,
          "MC",
          2,
          "prog1",
          true, //start of event
          pal1InitialTime.AddMinutes(10),
          "result"
        )
      ); //end of route
      pal1Initial.Add(
        new LogEntry(
          0,
          new LogMaterial[] { mat1, mat2 },
          1,
          LogType.MachineCycle,
          "MC",
          2,
          "prog1",
          false, //start of event
          pal1InitialTime.AddMinutes(20),
          "result"
        )
      ); //end of route
      // ***********  End of Route for pal1

      AddToDB(pal1Initial);

      CheckLog(pal1Initial, _jobLog.CurrentPalletLog(1), DateTime.UtcNow.AddHours(-10));
      _jobLog.CurrentPalletLog(2).Should().BeEmpty();

      _jobLog.CompletePalletCycle(1, pal1InitialTime.AddMinutes(25), "");

      pal1Initial.Add(
        new LogEntry(
          0,
          new LogMaterial[] { },
          1,
          LogType.PalletCycle,
          "Pallet Cycle",
          1,
          "",
          false,
          pal1InitialTime.AddMinutes(25),
          "PalletCycle",
          TimeSpan.Zero,
          TimeSpan.Zero
        )
      );

      Assert.Equal(pal1InitialTime.AddMinutes(25), _jobLog.LastPalletCycleTime(1));
      CheckLog(
        pal1Initial,
        _jobLog.GetLogEntries(DateTime.UtcNow.AddHours(-10), DateTime.UtcNow).ToList(),
        DateTime.UtcNow.AddHours(-50)
      );
      _jobLog.CurrentPalletLog(1).Should().BeEmpty();
      _jobLog
        .CurrentPalletLog(1, includeLastPalletCycleEvt: true)
        .Should()
        .BeEquivalentTo(new[] { pal1Initial.Last() }, options => options.Excluding(x => x.Counter));
      _jobLog.CurrentPalletLog(2).Should().BeEmpty();

      DateTime pal2CycleTime = DateTime.UtcNow.AddHours(-3);

      // *********** Add pal2 load event
      pal2Cycle.Add(
        new LogEntry(
          0,
          new LogMaterial[] { mat1, mat2 },
          2,
          LogType.LoadUnloadCycle,
          "Load",
          2,
          "prog1",
          true, //start of event
          pal2CycleTime,
          "result"
        )
      ); //end of route
      pal2Cycle.Add(
        new LogEntry(
          0,
          new LogMaterial[] { mat1, mat2 },
          2,
          LogType.LoadUnloadCycle,
          "Load",
          2,
          "prog1",
          false, //start of event
          pal2CycleTime.AddMinutes(10),
          "result"
        )
      ); //end of route

      AddToDB(pal2Cycle);

      _jobLog.CurrentPalletLog(1).Should().BeEmpty();
      CheckLog(pal2Cycle, _jobLog.CurrentPalletLog(2), DateTime.UtcNow.AddHours(-10));

      DateTime pal1CycleTime = DateTime.UtcNow.AddHours(-2);

      // ********** Add pal1 load event
      pal1Cycle.Add(
        new LogEntry(
          0,
          new LogMaterial[] { mat1, mat2 },
          1,
          LogType.LoadUnloadCycle,
          "Load",
          2,
          "prog1",
          true, //start of event
          pal1CycleTime,
          "result"
        )
      ); //end of route
      pal1Cycle.Add(
        new LogEntry(
          0,
          new LogMaterial[] { mat1, mat2 },
          1,
          LogType.LoadUnloadCycle,
          "Load",
          2,
          "prog1",
          false, //start of event
          pal1CycleTime.AddMinutes(15),
          "result"
        )
      ); //end of route

      // *********** Add pal1 start of machining
      pal1Cycle.Add(
        new LogEntry(
          0,
          new LogMaterial[] { mat1, mat2 },
          1,
          LogType.MachineCycle,
          "MC",
          4,
          "prog1",
          true, //start of event
          pal1CycleTime.AddMinutes(20),
          "result"
        )
      ); //end of route

      AddToDB(pal1Cycle);

      CheckLog(pal1Cycle, _jobLog.CurrentPalletLog(1), DateTime.UtcNow.AddHours(-10));
      CheckLog(pal2Cycle, _jobLog.CurrentPalletLog(2), DateTime.UtcNow.AddHours(-10));

      //********  Complete the pal1 machining
      pal1Cycle.Add(
        new LogEntry(
          0,
          new LogMaterial[] { mat1, mat2 },
          1,
          LogType.MachineCycle,
          "MC",
          4,
          "prog1",
          false, //start of event
          pal1CycleTime.AddMinutes(30),
          "result"
        )
      ); //end of route

      ((Repository)_jobLog).AddLogEntryFromUnitTest(pal1Cycle[pal1Cycle.Count - 1]);

      CheckLog(pal1Cycle, _jobLog.CurrentPalletLog(1), DateTime.UtcNow.AddHours(-10));
      CheckLog(pal2Cycle, _jobLog.CurrentPalletLog(2), DateTime.UtcNow.AddHours(-10));

      //********  Ignores invalidated and swap events
      var invalidated = new LogEntry(
        cntr: 0,
        mat: new[] { mat1, mat2 },
        pal: 1,
        ty: LogType.MachineCycle,
        locName: "OtherMC",
        locNum: 100,
        prog: "prog22",
        start: true,
        endTime: pal1CycleTime.AddMinutes(31),
        result: "prog22"
      );
      invalidated %= e => e.ProgramDetails["PalletCycleInvalidated"] = "1";
      ((Repository)_jobLog).AddLogEntryFromUnitTest(invalidated);

      var swap = new LogEntry(
        cntr: 0,
        mat: new[] { mat1, mat2 },
        pal: 1,
        ty: LogType.SwapMaterialOnPallet,
        locName: "SwapMatOnPallet",
        locNum: 1,
        prog: "SwapMatOnPallet",
        start: false,
        endTime: pal1CycleTime.AddMinutes(32),
        result: "Replace aaa with bbb"
      );
      ((Repository)_jobLog).AddLogEntryFromUnitTest(swap);

      // neither invalidated nor swap added to pal1Cycle
      CheckLog(pal1Cycle, _jobLog.CurrentPalletLog(1), DateTime.UtcNow.AddHours(-10));

      _jobLog.CompletePalletCycle(1, pal1CycleTime.AddMinutes(40), "");

      var elapsed = pal1CycleTime.AddMinutes(40).Subtract(pal1InitialTime.AddMinutes(25));
      pal1Cycle.Add(
        new LogEntry(
          0,
          new LogMaterial[] { },
          1,
          LogType.PalletCycle,
          "Pallet Cycle",
          1,
          "",
          false,
          pal1CycleTime.AddMinutes(40),
          "PalletCycle",
          elapsed,
          TimeSpan.Zero
        )
      );

      Assert.Equal(pal1CycleTime.AddMinutes(40), _jobLog.LastPalletCycleTime(1));
      _jobLog.CurrentPalletLog(1).Should().BeEmpty();

      // add invalidated and swap when loading all entries
      CheckLog(
        pal1Cycle.Append(invalidated).Append(swap).ToList(),
        _jobLog.GetLogEntries(pal1CycleTime.AddMinutes(-5), DateTime.UtcNow).ToList(),
        DateTime.UtcNow.AddHours(-50)
      );

      CheckLog(pal2Cycle, _jobLog.CurrentPalletLog(2), DateTime.UtcNow.AddHours(-10));
    }

    [Fact]
    public void ForeignID()
    {
      var mat1 = new LogMaterial(
        _jobLog.AllocateMaterialID("unique", "part1", 2),
        "unique",
        1,
        "part1",
        2,
        "",
        "",
        "face1"
      );
      var mat2 = new LogMaterial(
        _jobLog.AllocateMaterialID("unique2", "part2", 2),
        "unique2",
        2,
        "part2",
        2,
        "",
        "",
        "face2"
      );

      var log1 = new LogEntry(
        0,
        new LogMaterial[] { mat1, mat2 },
        1,
        LogType.GeneralMessage,
        "ABC",
        1,
        "prog1",
        false,
        DateTime.UtcNow,
        "result1",
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(11)
      );
      var log2 = new LogEntry(
        0,
        new LogMaterial[] { mat1, mat2 },
        2,
        LogType.MachineCycle,
        "MC",
        1,
        "prog2",
        false,
        DateTime.UtcNow,
        "result2",
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(16)
      );
      var log3 = new LogEntry(
        0,
        new LogMaterial[] { mat1, mat2 },
        3,
        LogType.LoadUnloadCycle,
        "Load",
        1,
        "prog3",
        false,
        DateTime.UtcNow,
        "result3",
        TimeSpan.FromMinutes(20),
        TimeSpan.FromMinutes(21)
      );

      Assert.Equal("", _jobLog.MaxForeignID());
      ((Repository)_jobLog).AddLogEntryFromUnitTest(log1, "for1");
      Assert.Equal("for1", _jobLog.MaxForeignID());
      ((Repository)_jobLog).AddLogEntryFromUnitTest(log2, "for2");
      Assert.Equal("for2", _jobLog.MaxForeignID());
      ((Repository)_jobLog).AddLogEntryFromUnitTest(log3);
      Assert.Equal("for2", _jobLog.MaxForeignID());
      _jobLog.AddPendingLoad(4, "k", 1, TimeSpan.Zero, TimeSpan.Zero, "for4");
      Assert.Equal("for4", _jobLog.MaxForeignID());
      var mat = new Dictionary<string, IEnumerable<EventLogMaterial>>();
      mat["k"] = new[] { EventLogMaterial.FromLogMat(mat1) };
      _jobLog.CompletePalletCycle(
        pal: 4,
        timeUTC: DateTime.UtcNow,
        foreignID: "for3",
        matFromPendingLoads: mat,
        generateSerials: false,
        additionalLoads: null
      );
      Assert.Equal("for4", _jobLog.MaxForeignID()); // for4 should be copied

      var load1 = _jobLog.StationLogByForeignID("for1");
      load1.Count.Should().Be(1);
      CheckEqual(log1, load1[0]);

      CheckEqual(_jobLog.MostRecentLogEntryForForeignID("for1"), load1[0]);

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
      _jobLog.MostRecentLogEntryForForeignID("woeufhwioeuf").Should().BeNull();

      Assert.Equal("for1", _jobLog.ForeignIDForCounter(load1[0].Counter));
      Assert.Equal("for2", _jobLog.ForeignIDForCounter(load2[0].Counter));
      Assert.Equal("", _jobLog.ForeignIDForCounter(load2[0].Counter + 30));

      // add another log with same foreign id
      var t = DateTime.UtcNow.AddHours(-2);
      _jobLog.RecordSerialForMaterialID(EventLogMaterial.FromLogMat(mat1), "ser1", t, foreignID: "for1");

      var mat1WithSer = mat1 with { Serial = "ser1" };

      var expectedFor1Serial = new LogEntry()
      {
        Counter = -1,
        Material = ImmutableList.Create(mat1WithSer),
        Pallet = 0,
        LogType = LogType.PartMark,
        Program = "MARK",
        LocationName = "Mark",
        LocationNum = 1,
        StartOfCycle = false,
        EndTimeUTC = t,
        Result = "ser1",
        ElapsedTime = TimeSpan.FromMinutes(-1),
        ActiveOperationTime = TimeSpan.Zero,
      };

      _jobLog
        .StationLogByForeignID("for1")
        .Should()
        .BeEquivalentTo(
          new[] { load1[0] with { Material = ImmutableList.Create(mat1WithSer, mat2) }, expectedFor1Serial },
          options => options.Excluding(e => e.Counter)
        );

      _jobLog
        .MostRecentLogEntryForForeignID("for1")
        .Should()
        .BeEquivalentTo(expectedFor1Serial, options => options.Excluding(e => e.Counter));
    }

    [Fact]
    public void OriginalMessage()
    {
      var mat1 = new LogMaterial(
        _jobLog.AllocateMaterialID("uniqqqq", "pppart66", 5),
        "uniqqqq",
        1,
        "pppart66",
        5,
        "",
        "",
        "facce"
      );
      var mat2 = new LogMaterial(
        _jobLog.AllocateMaterialID("uuuuuniq", "part5", 2),
        "uuuuuniq",
        2,
        "part5",
        2,
        "",
        "",
        "face2"
      );

      var log1 = new LogEntry(
        0,
        new LogMaterial[] { mat1, mat2 },
        16,
        LogType.GeneralMessage,
        "Hello",
        5,
        "program125",
        false,
        DateTime.UtcNow,
        "result66",
        TimeSpan.FromMinutes(166),
        TimeSpan.FromMinutes(74)
      );

      Assert.Equal("", _jobLog.MaxForeignID());
      ((Repository)_jobLog).AddLogEntryFromUnitTest(log1, "foreign1", "the original message");
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

      var earlierWork = _fixture.Create<Workorder>() with { WorkorderId = "earlierwork" };
      var work1part1 = _fixture.Create<Workorder>() with { WorkorderId = "work1", Part = "part1" };
      var work1part2 = _fixture.Create<Workorder>() with { WorkorderId = "work1", Part = "part2" };
      var work2 = _fixture.Create<Workorder>() with { WorkorderId = "work2", Part = "part3" };

      _jobLog.AddJobs(
        new NewJobs()
        {
          ScheduleId = "aaaa",
          Jobs = ImmutableList<Job>.Empty,
          CurrentUnfilledWorkorders = ImmutableList.Create(earlierWork),
        },
        null,
        true
      );

      _jobLog.AddJobs(
        new NewJobs()
        {
          ScheduleId = "cccc",
          Jobs = ImmutableList<Job>.Empty,
          CurrentUnfilledWorkorders = ImmutableList.Create(work1part1, work1part2, work2),
        },
        null,
        true
      );

      //one material across two processes
      var mat1 = _jobLog.AllocateMaterialID("uniq1", "part1", 2);
      var mat1_proc1 = new LogMaterial(mat1, "uniq1", 1, "part1", 2, "", "", "");
      var mat1_proc2 = new LogMaterial(mat1, "uniq1", 2, "part1", 2, "", "", "");
      var mat2_proc1 = new LogMaterial(
        _jobLog.AllocateMaterialID("uniq1", "part1", 2),
        "uniq1",
        1,
        "part1",
        2,
        "",
        "",
        ""
      );

      //not adding all events, but at least one non-endofroute and one endofroute
      ((Repository)_jobLog).AddLogEntryFromUnitTest(
        new LogEntry(
          0,
          new LogMaterial[] { mat1_proc1 },
          1,
          LogType.MachineCycle,
          "MC",
          5,
          "prog1",
          false,
          t.AddMinutes(5),
          "",
          TimeSpan.FromMinutes(10),
          TimeSpan.FromMinutes(20)
        )
      );
      ((Repository)_jobLog).AddLogEntryFromUnitTest(
        new LogEntry(
          0,
          new LogMaterial[] { mat1_proc2 },
          1,
          LogType.MachineCycle,
          "MC",
          5,
          "prog2",
          false,
          t.AddMinutes(6),
          "",
          TimeSpan.FromMinutes(30),
          TimeSpan.FromMinutes(40)
        )
      );
      ((Repository)_jobLog).AddLogEntryFromUnitTest(
        new LogEntry(
          0,
          new LogMaterial[] { mat1_proc2, mat2_proc1 }, //mat2_proc1 should be ignored since it isn't final process
          1,
          LogType.LoadUnloadCycle,
          "Load",
          5,
          "UNLOAD",
          false,
          t.AddMinutes(7),
          "UNLOAD",
          TimeSpan.FromMinutes(50),
          TimeSpan.FromMinutes(60)
        )
      );

      //four materials on the same pallet but different workorders
      var mat3 = new LogMaterial(
        _jobLog.AllocateMaterialID("uniq2", "part1", 1),
        "uniq2",
        1,
        "part1",
        1,
        "",
        "",
        ""
      );
      var mat4 = new LogMaterial(
        _jobLog.AllocateMaterialID("uniq2", "part2", 1),
        "uniq2",
        1,
        "part2",
        1,
        "",
        "",
        ""
      );
      var mat5 = new LogMaterial(
        _jobLog.AllocateMaterialID("uniq2", "part3", 1),
        "uniq2",
        1,
        "part3",
        1,
        "",
        "",
        ""
      );
      var mat6 = new LogMaterial(
        _jobLog.AllocateMaterialID("uniq2", "part3", 1),
        "uniq2",
        1,
        "part3",
        1,
        "",
        "",
        ""
      );

      ((Repository)_jobLog).AddLogEntryFromUnitTest(
        new LogEntry(
          0,
          new LogMaterial[] { mat3, mat4, mat5, mat6 },
          1,
          LogType.MachineCycle,
          "MC",
          5,
          "progdouble",
          false,
          t.AddMinutes(15),
          "",
          TimeSpan.FromMinutes(3),
          TimeSpan.FromMinutes(4)
        )
      );
      ((Repository)_jobLog).AddLogEntryFromUnitTest(
        new LogEntry(
          0,
          new LogMaterial[] { mat3, mat4, mat5, mat6 },
          1,
          LogType.LoadUnloadCycle,
          "Load",
          5,
          "UNLOAD",
          false,
          t.AddMinutes(17),
          "UNLOAD",
          TimeSpan.FromMinutes(5),
          TimeSpan.FromMinutes(6)
        )
      );

      //now record serial and workorder
      _jobLog.RecordSerialForMaterialID(EventLogMaterial.FromLogMat(mat1_proc2), "serial1", t.AddHours(1));
      _jobLog.RecordSerialForMaterialID(EventLogMaterial.FromLogMat(mat2_proc1), "serial2", t.AddHours(2));
      _jobLog.RecordSerialForMaterialID(EventLogMaterial.FromLogMat(mat3), "serial3", t.AddHours(3));
      _jobLog.RecordSerialForMaterialID(EventLogMaterial.FromLogMat(mat4), "serial4", t.AddHours(4));
      _jobLog.RecordSerialForMaterialID(EventLogMaterial.FromLogMat(mat5), "serial5", t.AddHours(5));
      _jobLog.RecordSerialForMaterialID(EventLogMaterial.FromLogMat(mat6), "serial6", t.AddHours(6));
      Assert.Equal("serial1", _jobLog.GetMaterialDetails(mat1_proc2.MaterialID).Serial);
      _jobLog
        .GetMaterialDetailsForSerial("serial1")
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new MaterialDetails()
            {
              MaterialID = mat1_proc2.MaterialID,
              JobUnique = "uniq1",
              PartName = "part1",
              NumProcesses = 2,
              Serial = "serial1",
            }
          }
        );
      _jobLog.GetMaterialDetailsForSerial("waoheufweiuf").Should().BeEmpty();

      _jobLog.RecordWorkorderForMaterialID(EventLogMaterial.FromLogMat(mat1_proc2), "work1");
      _jobLog.RecordWorkorderForMaterialID(EventLogMaterial.FromLogMat(mat3), "work1");
      _jobLog.RecordWorkorderForMaterialID(EventLogMaterial.FromLogMat(mat4), "work1");
      _jobLog.RecordWorkorderForMaterialID(EventLogMaterial.FromLogMat(mat5), "work2");
      _jobLog.RecordWorkorderForMaterialID(EventLogMaterial.FromLogMat(mat6), "work2");
      Assert.Equal("work2", _jobLog.GetMaterialDetails(mat5.MaterialID).Workorder);

      double c2Cnt = 4; //number of material on cycle 2

      var expectedActiveWorks = new[]
      {
        new ActiveWorkorder()
        {
          WorkorderId = "work1",
          Part = work1part1.Part,
          PlannedQuantity = work1part1.Quantity,
          DueDate = work1part1.DueDate,
          Priority = work1part1.Priority,
          CompletedQuantity = 2, // mat1 and mat3
          Serials = ImmutableList.Create("serial1", "serial3"),
          Comments = null,
          ElapsedStationTime = ImmutableDictionary<string, TimeSpan>.Empty
            .Add("MC", TimeSpan.FromMinutes(10 + 30 + 3 * 1 / c2Cnt)) //10 + 30 from mat1, 3*1/4 for mat3
            .Add(
              "Load",
              TimeSpan.FromMinutes(50 / 2 + 5 * 1 / c2Cnt)
            ) //50/2 from mat1_proc2, and 5*1/4 for mat3
          ,
          ActiveStationTime = ImmutableDictionary<string, TimeSpan>.Empty
            .Add("MC", TimeSpan.FromMinutes(20 + 40 + 4 * 1 / c2Cnt)) //20 + 40 from mat1, 4*1/4 for mat3
            .Add("Load", TimeSpan.FromMinutes(60 / 2 + 6 * 1 / c2Cnt)), //60/2 from mat1_proc2, and 6*1/4 for mat3
          SimulatedFilledUTC = work1part1.SimulatedFilledUTC,
          SimulatedStartUTC = work1part1.SimulatedStartUTC,
        },
        new ActiveWorkorder()
        {
          WorkorderId = "work1",
          Part = work1part2.Part,
          PlannedQuantity = work1part2.Quantity,
          DueDate = work1part2.DueDate,
          Priority = work1part2.Priority,
          CompletedQuantity = 1,
          Serials = ImmutableList.Create("serial4"),
          Comments = null,
          ElapsedStationTime = ImmutableDictionary<string, TimeSpan>.Empty
            .Add("MC", TimeSpan.FromMinutes(3 * 1 / c2Cnt))
            .Add("Load", TimeSpan.FromMinutes(5 * 1 / c2Cnt)),
          ActiveStationTime = ImmutableDictionary<string, TimeSpan>.Empty
            .Add("MC", TimeSpan.FromMinutes(4 * 1 / c2Cnt))
            .Add("Load", TimeSpan.FromMinutes(6 * 1 / c2Cnt)),
          SimulatedFilledUTC = work1part2.SimulatedFilledUTC,
          SimulatedStartUTC = work1part2.SimulatedStartUTC
        },
        new ActiveWorkorder()
        {
          WorkorderId = "work2",
          Part = work2.Part,
          PlannedQuantity = work2.Quantity,
          DueDate = work2.DueDate,
          Priority = work2.Priority,
          CompletedQuantity = 2,
          Serials = ImmutableList.Create("serial5", "serial6"),
          Comments = null,
          ElapsedStationTime = ImmutableDictionary<string, TimeSpan>.Empty
            .Add("MC", TimeSpan.FromMinutes(3 * 2 / c2Cnt))
            .Add("Load", TimeSpan.FromMinutes(5 * 2 / c2Cnt)),
          ActiveStationTime = ImmutableDictionary<string, TimeSpan>.Empty
            .Add("MC", TimeSpan.FromMinutes(4 * 2 / c2Cnt))
            .Add("Load", TimeSpan.FromMinutes(6 * 2 / c2Cnt)),
          SimulatedFilledUTC = work2.SimulatedFilledUTC,
          SimulatedStartUTC = work2.SimulatedStartUTC,
        }
      };

      _jobLog.GetActiveWorkordersForSchedule(scheduleId: "cccc").Should().BeEquivalentTo(expectedActiveWorks);
      _jobLog.GetActiveWorkordersForMostRecentSchedule().Should().BeEquivalentTo(expectedActiveWorks);
      _jobLog.GetActiveWorkordersForSchedule(scheduleId: "unused").Should().BeEmpty();
      _jobLog
        .GetActiveWorkordersForSchedule(scheduleId: "aaaa")
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new ActiveWorkorder()
            {
              WorkorderId = "earlierwork",
              Part = earlierWork.Part,
              PlannedQuantity = earlierWork.Quantity,
              DueDate = earlierWork.DueDate,
              Priority = earlierWork.Priority,
              CompletedQuantity = 0,
              Serials = ImmutableList<string>.Empty,
              Comments = null,
              ElapsedStationTime = ImmutableDictionary<string, TimeSpan>.Empty,
              ActiveStationTime = ImmutableDictionary<string, TimeSpan>.Empty,
              SimulatedFilledUTC = earlierWork.SimulatedFilledUTC,
              SimulatedStartUTC = earlierWork.SimulatedStartUTC,
            }
          }
        );

      //---- test comments
      var finalizedEntry = _jobLog.RecordWorkorderComment(
        "work1",
        comment: "work1ccc",
        operName: "oper1",
        timeUTC: t.AddHours(222)
      );
      Assert.Equal(0, finalizedEntry.Pallet);
      Assert.Equal("work1", finalizedEntry.Result);
      Assert.Equal(LogType.WorkorderComment, finalizedEntry.LogType);
      finalizedEntry.ProgramDetails
        .Should()
        .BeEquivalentTo(
          new Dictionary<string, string> { { "Comment", "work1ccc" }, { "Operator", "oper1" } }
        );
      var expectedComment1 = new WorkorderComment() { Comment = "work1ccc", TimeUTC = t.AddHours(222) };

      _jobLog
        .GetActiveWorkordersForMostRecentSchedule()
        .Should()
        .BeEquivalentTo(
          new[]
          {
            expectedActiveWorks[0] with
            {
              Comments = ImmutableList.Create(expectedComment1)
            },
            expectedActiveWorks[1] with
            {
              Comments = ImmutableList.Create(expectedComment1)
            },
            expectedActiveWorks[2]
          }
        );

      // add a second comment
      _jobLog.RecordWorkorderComment("work1", comment: "work1ddd", operName: null, timeUTC: t.AddHours(333));
      var expectedComment2 = new WorkorderComment() { Comment = "work1ddd", TimeUTC = t.AddHours(333) };

      _jobLog
        .GetActiveWorkordersForMostRecentSchedule()
        .Should()
        .BeEquivalentTo(
          new[]
          {
            expectedActiveWorks[0] with
            {
              Comments = ImmutableList.Create(expectedComment1, expectedComment2)
            },
            expectedActiveWorks[1] with
            {
              Comments = ImmutableList.Create(expectedComment1, expectedComment2)
            },
            expectedActiveWorks[2]
          }
        );

      // test it doesn't load archived orders
      var newWork = _fixture.Create<Workorder>() with
      {
        WorkorderId = "newwork"
      };
      var adjustedWork2 = work2 with { Quantity = work2.Quantity + 50 };
      _jobLog.ReplaceWorkordersForSchedule(scheduleId: "cccc", new[] { newWork, adjustedWork2 }, null);

      _jobLog
        .GetActiveWorkordersForSchedule(scheduleId: "cccc")
        .Should()
        .BeEquivalentTo(
          new[]
          {
            expectedActiveWorks[2] with
            {
              PlannedQuantity = work2.Quantity + 50
            },
            new ActiveWorkorder()
            {
              WorkorderId = "newwork",
              Part = newWork.Part,
              PlannedQuantity = newWork.Quantity,
              DueDate = newWork.DueDate,
              Priority = newWork.Priority,
              CompletedQuantity = 0,
              Serials = ImmutableList<string>.Empty,
              Comments = null,
              ElapsedStationTime = ImmutableDictionary<string, TimeSpan>.Empty,
              ActiveStationTime = ImmutableDictionary<string, TimeSpan>.Empty,
              SimulatedFilledUTC = newWork.SimulatedFilledUTC,
              SimulatedStartUTC = newWork.SimulatedStartUTC,
            }
          }
        );
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

      var mat3 = new LogMaterial(
        _jobLog.AllocateMaterialID("uniq1", "part1", 1),
        "uniq1",
        1,
        "part1",
        1,
        "",
        "",
        ""
      );
      var mat4 = new LogMaterial(
        _jobLog.AllocateMaterialID("uniq1", "part1", 1),
        "uniq1",
        1,
        "part1",
        1,
        "",
        "",
        ""
      );

      //mat1 has proc1 in old, proc2 in recent so everything should be loaded
      var mat1_proc1old = AddLogEntry(
        new LogEntry(
          0,
          new LogMaterial[] { mat1_proc1 },
          1,
          LogType.MachineCycle,
          "MC",
          5,
          "prog1",
          false,
          old.AddMinutes(5),
          "",
          TimeSpan.FromMinutes(10),
          TimeSpan.FromMinutes(20)
        )
      );
      var mat1_proc1complete = AddLogEntry(
        new LogEntry(
          0,
          new LogMaterial[] { mat1_proc1 },
          1,
          LogType.LoadUnloadCycle,
          "Load",
          5,
          "prog1",
          false,
          old.AddMinutes(6),
          "",
          TimeSpan.FromMinutes(11),
          TimeSpan.FromMinutes(21)
        )
      );
      var mat1_proc2old = AddLogEntry(
        new LogEntry(
          0,
          new LogMaterial[] { mat1_proc2 },
          1,
          LogType.MachineCycle,
          "MC",
          5,
          "prog2",
          false,
          old.AddMinutes(7),
          "",
          TimeSpan.FromMinutes(12),
          TimeSpan.FromMinutes(22)
        )
      );
      var mat1_proc2complete = AddLogEntry(
        new LogEntry(
          0,
          new LogMaterial[] { mat1_proc2 },
          1,
          LogType.LoadUnloadCycle,
          "Load",
          5,
          "UNLOAD",
          false,
          recent.AddMinutes(4),
          "UNLOAD",
          TimeSpan.FromMinutes(30),
          TimeSpan.FromMinutes(40)
        )
      );

      //mat2 has everything in recent, (including proc1 complete) but no complete on proc2
      AddLogEntry(
        new LogEntry(
          0,
          new LogMaterial[] { mat2_proc1 },
          1,
          LogType.MachineCycle,
          "MC",
          5,
          "mach2",
          false,
          recent.AddMinutes(5),
          "",
          TimeSpan.FromMinutes(50),
          TimeSpan.FromMinutes(60)
        )
      );
      AddLogEntry(
        new LogEntry(
          0,
          new LogMaterial[] { mat2_proc1 },
          1,
          LogType.LoadUnloadCycle,
          "Load",
          5,
          "load2",
          false,
          recent.AddMinutes(6),
          "UNLOAD",
          TimeSpan.FromMinutes(51),
          TimeSpan.FromMinutes(61)
        )
      );
      AddLogEntry(
        new LogEntry(
          0,
          new LogMaterial[] { mat2_proc2 },
          1,
          LogType.MachineCycle,
          "MC",
          5,
          "mach2",
          false,
          old.AddMinutes(7),
          "",
          TimeSpan.FromMinutes(52),
          TimeSpan.FromMinutes(62)
        )
      );

      //mat3 has everything in old
      AddLogEntry(
        new LogEntry(
          0,
          new LogMaterial[] { mat3 },
          1,
          LogType.MachineCycle,
          "MC",
          5,
          "prog3",
          false,
          old.AddMinutes(20),
          "",
          TimeSpan.FromMinutes(70),
          TimeSpan.FromMinutes(80)
        )
      );
      AddLogEntry(
        new LogEntry(
          0,
          new LogMaterial[] { mat3 },
          1,
          LogType.LoadUnloadCycle,
          "Load",
          5,
          "load3",
          false,
          old.AddMinutes(25),
          "UNLOAD",
          TimeSpan.FromMinutes(71),
          TimeSpan.FromMinutes(81)
        )
      );

      //mat4 has everything in new
      var mat4recent = AddLogEntry(
        new LogEntry(
          0,
          new LogMaterial[] { mat4 },
          1,
          LogType.MachineCycle,
          "MC",
          5,
          "prog44",
          false,
          recent.AddMinutes(40),
          "",
          TimeSpan.FromMinutes(90),
          TimeSpan.FromMinutes(100)
        )
      );
      var mat4complete = AddLogEntry(
        new LogEntry(
          0,
          new LogMaterial[] { mat4 },
          1,
          LogType.LoadUnloadCycle,
          "Load",
          5,
          "load4",
          false,
          recent.AddMinutes(45),
          "UNLOAD",
          TimeSpan.FromMinutes(91),
          TimeSpan.FromMinutes(101)
        )
      );

      CheckLog(
        new[]
        {
          mat1_proc1old,
          mat1_proc1complete,
          mat1_proc2old,
          mat1_proc2complete,
          mat4recent,
          mat4complete
        },
        _jobLog.GetCompletedPartLogs(recent.AddHours(-4), recent.AddHours(4)).ToList(),
        DateTime.MinValue
      );
    }

    [Fact]
    public void Queues()
    {
      var start = DateTime.UtcNow.AddHours(-10);

      var otherQueueMat = new LogMaterial(100, "uniq100", 100, "part100", 100, "", "", "");
      _jobLog.CreateMaterialID(100, "uniq100", "part100", 100);

      _jobLog.IsMaterialInQueue(100).Should().BeFalse();

      _jobLog
        .RecordAddMaterialToQueue(
          EventLogMaterial.FromLogMat(otherQueueMat),
          "BBBB",
          0,
          "theoper",
          "thereason",
          start.AddHours(-1)
        )
        .Should()
        .BeEquivalentTo(
          new[]
          {
            AddToQueueExpectedEntry(otherQueueMat, 1, "BBBB", 0, start.AddHours(-1), "theoper", "thereason")
          },
          options => options.ComparingByMembers<LogEntry>()
        );

      _jobLog.IsMaterialInQueue(100).Should().BeTrue();

      var expectedLogs = new List<LogEntry>();

      var mat1 = new LogMaterial(1, "uniq1", 15, "part111", 19, "mat1serial", "", "");
      _jobLog.CreateMaterialID(1, "uniq1", "part111", 19);
      var mat2 = new LogMaterial(2, "uniq2", 1, "part2", 22, "mat2serial", "mat2workorder", "");
      _jobLog.CreateMaterialID(2, "uniq2", "part2", 22);
      var mat3 = new LogMaterial(3, "uniq3", 3, "part3", 36, "", "", "");
      _jobLog.CreateMaterialID(3, "uniq3", "part3", 36);
      var mat4 = new LogMaterial(4, "uniq4", 4, "part4", 44, "", "", "");
      _jobLog.CreateMaterialID(4, "uniq4", "part4", 44);

      _jobLog.RecordSerialForMaterialID(EventLogMaterial.FromLogMat(mat1), "mat1serial", start);
      expectedLogs.Add(RecordSerialExpectedEntry(mat1, 2, "mat1serial", start));
      _jobLog.RecordSerialForMaterialID(EventLogMaterial.FromLogMat(mat2), "mat2serial", start);
      expectedLogs.Add(RecordSerialExpectedEntry(mat2, 3, "mat2serial", start));
      _jobLog.RecordWorkorderForMaterialID(EventLogMaterial.FromLogMat(mat2), "mat2workorder", start);
      expectedLogs.Add(RecordWorkorderExpectedEntry(mat2, 4, "mat2workorder", start));

      // add via LogMaterial with position -1
      _jobLog
        .RecordAddMaterialToQueue(EventLogMaterial.FromLogMat(mat1), "AAAA", -1, null, null, start)
        .Should()
        .BeEquivalentTo(
          new[] { AddToQueueExpectedEntry(mat1, 5, "AAAA", 0, start) },
          options => options.ComparingByMembers<LogEntry>()
        );
      expectedLogs.Add(AddToQueueExpectedEntry(mat1, 5, "AAAA", 0, start));

      _jobLog
        .GetMaterialInQueueByUnique("AAAA", "uniq1")
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new QueuedMaterial()
            {
              MaterialID = 1,
              Queue = "AAAA",
              Position = 0,
              Unique = "uniq1",
              PartNameOrCasting = "part111",
              NumProcesses = 19,
              AddTimeUTC = start,
              Serial = "mat1serial",
              NextProcess = 16,
              Paths = ImmutableDictionary<int, int>.Empty
            }
          }
        );
      _jobLog.GetMaterialInQueueByUnique("AAAA", "waeofuihwef").Should().BeEmpty();
      _jobLog.IsMaterialInQueue(mat1.MaterialID).Should().BeTrue();
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().BeNull();
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().BeNull();
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().BeNull();

      //adding with LogMaterial with position -1 and existing queue
      _jobLog
        .RecordAddMaterialToQueue(
          EventLogMaterial.FromLogMat(mat2),
          "AAAA",
          -1,
          null,
          null,
          start.AddMinutes(10)
        )
        .Should()
        .BeEquivalentTo(
          new[] { AddToQueueExpectedEntry(mat2, 6, "AAAA", 1, start.AddMinutes(10)) },
          options => options.ComparingByMembers<LogEntry>()
        );
      expectedLogs.Add(AddToQueueExpectedEntry(mat2, 6, "AAAA", 1, start.AddMinutes(10)));

      _jobLog
        .GetMaterialInQueueByUnique("AAAA", "uniq1")
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new QueuedMaterial()
            {
              MaterialID = 1,
              Queue = "AAAA",
              Position = 0,
              Unique = "uniq1",
              PartNameOrCasting = "part111",
              NumProcesses = 19,
              AddTimeUTC = start,
              Serial = "mat1serial",
              NextProcess = 16,
              Paths = ImmutableDictionary<int, int>.Empty
            },
          }
        );
      _jobLog
        .GetMaterialInQueueByUnique("AAAA", "uniq2")
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new QueuedMaterial()
            {
              MaterialID = 2,
              Queue = "AAAA",
              Position = 1,
              Unique = "uniq2",
              PartNameOrCasting = "part2",
              NumProcesses = 22,
              AddTimeUTC = start.AddMinutes(10),
              Serial = "mat2serial",
              Workorder = "mat2workorder",
              NextProcess = 2,
              Paths = ImmutableDictionary<int, int>.Empty
            }
          }
        );
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().BeNull();
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().BeNull();

      // setting paths
      _jobLog.RecordPathForProcess(mat1.MaterialID, 1, 50);
      _jobLog.RecordPathForProcess(mat1.MaterialID, 2, 52);

      //inserting into queue with LogMaterial
      _jobLog
        .RecordAddMaterialToQueue(
          EventLogMaterial.FromLogMat(mat3),
          "AAAA",
          1,
          "opernnnn",
          "rrrrr",
          start.AddMinutes(20)
        )
        .Should()
        .BeEquivalentTo(
          new[]
          {
            AddToQueueExpectedEntry(mat3, 7, "AAAA", 1, start.AddMinutes(20), "opernnnn", reason: "rrrrr")
          },
          options => options.ComparingByMembers<LogEntry>()
        );
      expectedLogs.Add(
        AddToQueueExpectedEntry(mat3, 7, "AAAA", 1, start.AddMinutes(20), "opernnnn", "rrrrr")
      );

      _jobLog
        .GetMaterialInQueueByUnique("AAAA", "uniq1")
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new QueuedMaterial()
            {
              MaterialID = 1,
              Queue = "AAAA",
              Position = 0,
              Unique = "uniq1",
              PartNameOrCasting = "part111",
              NumProcesses = 19,
              AddTimeUTC = start,
              Serial = "mat1serial",
              NextProcess = 16,
              Paths = ImmutableDictionary<int, int>.Empty.Add(1, 50).Add(2, 52)
            },
          }
        );
      _jobLog
        .GetMaterialInQueueByUnique("AAAA", "uniq3")
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new QueuedMaterial()
            {
              MaterialID = 3,
              Queue = "AAAA",
              Position = 1,
              Unique = "uniq3",
              PartNameOrCasting = "part3",
              NumProcesses = 36,
              AddTimeUTC = start.AddMinutes(20),
              NextProcess = 4,
              Paths = ImmutableDictionary<int, int>.Empty
            },
          }
        );
      _jobLog
        .GetMaterialInQueueByUnique("AAAA", "uniq2")
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new QueuedMaterial()
            {
              MaterialID = 2,
              Queue = "AAAA",
              Position = 2,
              Unique = "uniq2",
              PartNameOrCasting = "part2",
              NumProcesses = 22,
              AddTimeUTC = start.AddMinutes(10),
              Serial = "mat2serial",
              Workorder = "mat2workorder",
              NextProcess = 2,
              Paths = ImmutableDictionary<int, int>.Empty
            }
          }
        );
      _jobLog
        .GetMaterialInAllQueues()
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new QueuedMaterial()
            {
              MaterialID = 1,
              Queue = "AAAA",
              Position = 0,
              Unique = "uniq1",
              PartNameOrCasting = "part111",
              NumProcesses = 19,
              AddTimeUTC = start,
              Serial = "mat1serial",
              NextProcess = 16,
              Paths = ImmutableDictionary<int, int>.Empty.Add(1, 50).Add(2, 52)
            },
            new QueuedMaterial()
            {
              MaterialID = 3,
              Queue = "AAAA",
              Position = 1,
              Unique = "uniq3",
              PartNameOrCasting = "part3",
              NumProcesses = 36,
              AddTimeUTC = start.AddMinutes(20),
              NextProcess = 4,
              Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial()
            {
              MaterialID = 2,
              Queue = "AAAA",
              Position = 2,
              Unique = "uniq2",
              PartNameOrCasting = "part2",
              NumProcesses = 22,
              AddTimeUTC = start.AddMinutes(10),
              Serial = "mat2serial",
              Workorder = "mat2workorder",
              NextProcess = 2,
              Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial()
            {
              MaterialID = 100,
              Queue = "BBBB",
              Position = 0,
              Unique = "uniq100",
              PartNameOrCasting = "part100",
              NumProcesses = 100,
              AddTimeUTC = start.AddHours(-1),
              NextProcess = 101,
              Paths = ImmutableDictionary<int, int>.Empty
            }
          }
        );
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().BeNull();

      //removing from queue with LogMaterial
      _jobLog
        .RecordRemoveMaterialFromAllQueues(EventLogMaterial.FromLogMat(mat3), "operyy", start.AddMinutes(40))
        .Should()
        .BeEquivalentTo(
          new[] { RemoveFromQueueExpectedEntry(mat3, 8, "AAAA", 1, 40 - 20, start.AddMinutes(40), "operyy") },
          options => options.ComparingByMembers<LogEntry>()
        );
      expectedLogs.Add(
        RemoveFromQueueExpectedEntry(mat3, 8, "AAAA", 1, 40 - 20, start.AddMinutes(40), "operyy")
      );

      _jobLog
        .GetMaterialInAllQueues()
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new QueuedMaterial()
            {
              MaterialID = 1,
              Queue = "AAAA",
              Position = 0,
              Unique = "uniq1",
              PartNameOrCasting = "part111",
              NumProcesses = 19,
              AddTimeUTC = start,
              Serial = "mat1serial",
              NextProcess = 16,
              Paths = ImmutableDictionary<int, int>.Empty.Add(1, 50).Add(2, 52)
            },
            new QueuedMaterial()
            {
              MaterialID = 2,
              Queue = "AAAA",
              Position = 1,
              Unique = "uniq2",
              PartNameOrCasting = "part2",
              NumProcesses = 22,
              AddTimeUTC = start.AddMinutes(10),
              Serial = "mat2serial",
              Workorder = "mat2workorder",
              NextProcess = 2,
              Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial()
            {
              MaterialID = 100,
              Queue = "BBBB",
              Position = 0,
              Unique = "uniq100",
              PartNameOrCasting = "part100",
              NumProcesses = 100,
              AddTimeUTC = start.AddHours(-1),
              NextProcess = 101,
              Paths = ImmutableDictionary<int, int>.Empty
            }
          }
        );
      _jobLog.IsMaterialInQueue(mat3.MaterialID).Should().BeFalse();
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().BeNull();

      //add back in with matid only
      _jobLog
        .RecordAddMaterialToQueue(mat3.MaterialID, mat3.Process, "AAAA", 2, null, null, start.AddMinutes(45))
        .Should()
        .BeEquivalentTo(
          new[] { AddToQueueExpectedEntry(mat3, 9, "AAAA", 2, start.AddMinutes(45)) },
          options => options.ComparingByMembers<LogEntry>()
        );
      expectedLogs.Add(AddToQueueExpectedEntry(mat3, 9, "AAAA", 2, start.AddMinutes(45)));

      _jobLog
        .GetMaterialInAllQueues()
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new QueuedMaterial()
            {
              MaterialID = 1,
              Queue = "AAAA",
              Position = 0,
              Unique = "uniq1",
              PartNameOrCasting = "part111",
              NumProcesses = 19,
              AddTimeUTC = start,
              Serial = "mat1serial",
              NextProcess = 16,
              Paths = ImmutableDictionary<int, int>.Empty.Add(1, 50).Add(2, 52)
            },
            new QueuedMaterial()
            {
              MaterialID = 2,
              Queue = "AAAA",
              Position = 1,
              Unique = "uniq2",
              PartNameOrCasting = "part2",
              NumProcesses = 22,
              AddTimeUTC = start.AddMinutes(10),
              Serial = "mat2serial",
              Workorder = "mat2workorder",
              NextProcess = 2,
              Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial()
            {
              MaterialID = 3,
              Queue = "AAAA",
              Position = 2,
              Unique = "uniq3",
              PartNameOrCasting = "part3",
              NumProcesses = 36,
              AddTimeUTC = start.AddMinutes(45),
              NextProcess = 4,
              Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial()
            {
              MaterialID = 100,
              Queue = "BBBB",
              Position = 0,
              Unique = "uniq100",
              PartNameOrCasting = "part100",
              NumProcesses = 100,
              AddTimeUTC = start.AddHours(-1),
              NextProcess = 101,
              Paths = ImmutableDictionary<int, int>.Empty
            }
          }
        );
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().BeNull();

      //move item backwards in queue
      _jobLog
        .RecordAddMaterialToQueue(
          EventLogMaterial.FromLogMat(mat1),
          "AAAA",
          1,
          null,
          null,
          start.AddMinutes(50)
        )
        .Should()
        .BeEquivalentTo(
          new[]
          {
            RemoveFromQueueExpectedEntry(mat1, 10, "AAAA", 0, 50, start.AddMinutes(50)),
            AddToQueueExpectedEntry(mat1, 11, "AAAA", 1, start.AddMinutes(50))
          },
          options => options.ComparingByMembers<LogEntry>()
        );
      expectedLogs.Add(RemoveFromQueueExpectedEntry(mat1, 10, "AAAA", 0, 50, start.AddMinutes(50)));
      expectedLogs.Add(AddToQueueExpectedEntry(mat1, 11, "AAAA", 1, start.AddMinutes(50)));

      _jobLog
        .GetMaterialInAllQueues()
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new QueuedMaterial()
            {
              MaterialID = 2,
              Queue = "AAAA",
              Position = 0,
              Unique = "uniq2",
              PartNameOrCasting = "part2",
              NumProcesses = 22,
              AddTimeUTC = start.AddMinutes(10),
              Serial = "mat2serial",
              Workorder = "mat2workorder",
              NextProcess = 2,
              Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial()
            {
              MaterialID = 1,
              Queue = "AAAA",
              Position = 1,
              Unique = "uniq1",
              PartNameOrCasting = "part111",
              NumProcesses = 19,
              AddTimeUTC = start.AddMinutes(50),
              Serial = "mat1serial",
              NextProcess = 16,
              Paths = ImmutableDictionary<int, int>.Empty.Add(1, 50).Add(2, 52)
            },
            new QueuedMaterial()
            {
              MaterialID = 3,
              Queue = "AAAA",
              Position = 2,
              Unique = "uniq3",
              PartNameOrCasting = "part3",
              NumProcesses = 36,
              AddTimeUTC = start.AddMinutes(45),
              NextProcess = 4,
              Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial()
            {
              MaterialID = 100,
              Queue = "BBBB",
              Position = 0,
              Unique = "uniq100",
              PartNameOrCasting = "part100",
              NumProcesses = 100,
              AddTimeUTC = start.AddHours(-1),
              NextProcess = 101,
              Paths = ImmutableDictionary<int, int>.Empty
            }
          }
        );
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().BeNull();

      //move item forwards in queue
      _jobLog
        .RecordAddMaterialToQueue(
          EventLogMaterial.FromLogMat(mat3),
          "AAAA",
          1,
          null,
          null,
          start.AddMinutes(55)
        )
        .Should()
        .BeEquivalentTo(
          new[]
          {
            RemoveFromQueueExpectedEntry(mat3, 12, "AAAA", 2, 55 - 45, start.AddMinutes(55)),
            AddToQueueExpectedEntry(mat3, 13, "AAAA", 1, start.AddMinutes(55))
          },
          options => options.ComparingByMembers<LogEntry>()
        );
      expectedLogs.Add(RemoveFromQueueExpectedEntry(mat3, 12, "AAAA", 2, 55 - 45, start.AddMinutes(55)));
      expectedLogs.Add(AddToQueueExpectedEntry(mat3, 13, "AAAA", 1, start.AddMinutes(55)));

      _jobLog
        .GetMaterialInAllQueues()
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new QueuedMaterial()
            {
              MaterialID = 2,
              Queue = "AAAA",
              Position = 0,
              Unique = "uniq2",
              PartNameOrCasting = "part2",
              NumProcesses = 22,
              AddTimeUTC = start.AddMinutes(10),
              Serial = "mat2serial",
              Workorder = "mat2workorder",
              NextProcess = 2,
              Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial()
            {
              MaterialID = 3,
              Queue = "AAAA",
              Position = 1,
              Unique = "uniq3",
              PartNameOrCasting = "part3",
              NumProcesses = 36,
              AddTimeUTC = start.AddMinutes(55),
              NextProcess = 4,
              Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial()
            {
              MaterialID = 1,
              Queue = "AAAA",
              Position = 2,
              Unique = "uniq1",
              PartNameOrCasting = "part111",
              NumProcesses = 19,
              AddTimeUTC = start.AddMinutes(50),
              Serial = "mat1serial",
              NextProcess = 16,
              Paths = ImmutableDictionary<int, int>.Empty.Add(1, 50).Add(2, 52)
            },
            new QueuedMaterial()
            {
              MaterialID = 100,
              Queue = "BBBB",
              Position = 0,
              Unique = "uniq100",
              PartNameOrCasting = "part100",
              NumProcesses = 100,
              AddTimeUTC = start.AddHours(-1),
              NextProcess = 101,
              Paths = ImmutableDictionary<int, int>.Empty
            }
          }
        );
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().BeNull();

      //add large position
      _jobLog
        .RecordAddMaterialToQueue(
          EventLogMaterial.FromLogMat(mat4),
          "AAAA",
          500,
          null,
          null,
          start.AddMinutes(58)
        )
        .Should()
        .BeEquivalentTo(
          new[] { AddToQueueExpectedEntry(mat4, 14, "AAAA", 3, start.AddMinutes(58)) },
          options => options.ComparingByMembers<LogEntry>()
        );
      expectedLogs.Add(AddToQueueExpectedEntry(mat4, 14, "AAAA", 3, start.AddMinutes(58)));

      _jobLog
        .GetMaterialInAllQueues()
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new QueuedMaterial()
            {
              MaterialID = 2,
              Queue = "AAAA",
              Position = 0,
              Unique = "uniq2",
              PartNameOrCasting = "part2",
              NumProcesses = 22,
              AddTimeUTC = start.AddMinutes(10),
              Serial = "mat2serial",
              Workorder = "mat2workorder",
              NextProcess = 2,
              Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial()
            {
              MaterialID = 3,
              Queue = "AAAA",
              Position = 1,
              Unique = "uniq3",
              PartNameOrCasting = "part3",
              NumProcesses = 36,
              AddTimeUTC = start.AddMinutes(55),
              NextProcess = 4,
              Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial()
            {
              MaterialID = 1,
              Queue = "AAAA",
              Position = 2,
              Unique = "uniq1",
              PartNameOrCasting = "part111",
              NumProcesses = 19,
              AddTimeUTC = start.AddMinutes(50),
              Serial = "mat1serial",
              NextProcess = 16,
              Paths = ImmutableDictionary<int, int>.Empty.Add(1, 50).Add(2, 52)
            },
            new QueuedMaterial()
            {
              MaterialID = 4,
              Queue = "AAAA",
              Position = 3,
              Unique = "uniq4",
              PartNameOrCasting = "part4",
              NumProcesses = 44,
              AddTimeUTC = start.AddMinutes(58),
              NextProcess = 5,
              Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial()
            {
              MaterialID = 100,
              Queue = "BBBB",
              Position = 0,
              Unique = "uniq100",
              PartNameOrCasting = "part100",
              NumProcesses = 100,
              AddTimeUTC = start.AddHours(-1),
              NextProcess = 101,
              Paths = ImmutableDictionary<int, int>.Empty
            }
          }
        );
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().Be(5);

      _jobLog
        .SignalMaterialForQuarantine(
          EventLogMaterial.FromLogMat(mat1),
          1,
          "QQQ",
          "theoper",
          reason: "a reason",
          timeUTC: start.AddMinutes(59)
        )
        .Should()
        .BeEquivalentTo(
          SignalQuarantineExpectedEntry(mat1, 15, 1, "QQQ", start.AddMinutes(59), "theoper", "a reason"),
          options => options.ComparingByMembers<LogEntry>()
        );
      expectedLogs.Add(
        SignalQuarantineExpectedEntry(mat1, 15, 1, "QQQ", start.AddMinutes(59), "theoper", "a reason")
      );

      // hasn't moved yet
      _jobLog
        .GetMaterialInAllQueues()
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new QueuedMaterial()
            {
              MaterialID = 2,
              Queue = "AAAA",
              Position = 0,
              Unique = "uniq2",
              PartNameOrCasting = "part2",
              NumProcesses = 22,
              AddTimeUTC = start.AddMinutes(10),
              Serial = "mat2serial",
              Workorder = "mat2workorder",
              NextProcess = 2,
              Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial()
            {
              MaterialID = 3,
              Queue = "AAAA",
              Position = 1,
              Unique = "uniq3",
              PartNameOrCasting = "part3",
              NumProcesses = 36,
              AddTimeUTC = start.AddMinutes(55),
              NextProcess = 4,
              Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial()
            {
              MaterialID = 1,
              Queue = "AAAA",
              Position = 2,
              Unique = "uniq1",
              PartNameOrCasting = "part111",
              NumProcesses = 19,
              AddTimeUTC = start.AddMinutes(50),
              Serial = "mat1serial",
              NextProcess = 16,
              Paths = ImmutableDictionary<int, int>.Empty.Add(1, 50).Add(2, 52)
            },
            new QueuedMaterial()
            {
              MaterialID = 4,
              Queue = "AAAA",
              Position = 3,
              Unique = "uniq4",
              PartNameOrCasting = "part4",
              NumProcesses = 44,
              AddTimeUTC = start.AddMinutes(58),
              NextProcess = 5,
              Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial()
            {
              MaterialID = 100,
              Queue = "BBBB",
              Position = 0,
              Unique = "uniq100",
              PartNameOrCasting = "part100",
              NumProcesses = 100,
              AddTimeUTC = start.AddHours(-1),
              NextProcess = 101,
              Paths = ImmutableDictionary<int, int>.Empty
            }
          }
        );
      _jobLog.GetMaterialInQueueByUnique("QQQ", "uniq1").Should().BeEmpty();
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().Be(5);

      //removing from queue with matid
      var mat2proc8 = new LogMaterial(
        mat2.MaterialID,
        mat2.JobUniqueStr,
        1,
        mat2.PartName,
        mat2.NumProcesses,
        mat2.Serial,
        mat2.Workorder,
        mat2.Face
      );
      _jobLog
        .RecordRemoveMaterialFromAllQueues(mat2.MaterialID, 1, null, start.AddMinutes(60))
        .Should()
        .BeEquivalentTo(
          new[] { RemoveFromQueueExpectedEntry(mat2proc8, 16, "AAAA", 0, 60 - 10, start.AddMinutes(60)) },
          options => options.ComparingByMembers<LogEntry>()
        );
      expectedLogs.Add(RemoveFromQueueExpectedEntry(mat2proc8, 16, "AAAA", 0, 60 - 10, start.AddMinutes(60)));

      _jobLog
        .GetMaterialInAllQueues()
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new QueuedMaterial()
            {
              MaterialID = 3,
              Queue = "AAAA",
              Position = 0,
              Unique = "uniq3",
              PartNameOrCasting = "part3",
              NumProcesses = 36,
              AddTimeUTC = start.AddMinutes(55),
              NextProcess = 4,
              Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial()
            {
              MaterialID = 1,
              Queue = "AAAA",
              Position = 1,
              Unique = "uniq1",
              PartNameOrCasting = "part111",
              NumProcesses = 19,
              AddTimeUTC = start.AddMinutes(50),
              Serial = "mat1serial",
              NextProcess = 16,
              Paths = ImmutableDictionary<int, int>.Empty.Add(1, 50).Add(2, 52)
            },
            new QueuedMaterial()
            {
              MaterialID = 4,
              Queue = "AAAA",
              Position = 2,
              Unique = "uniq4",
              PartNameOrCasting = "part4",
              NumProcesses = 44,
              AddTimeUTC = start.AddMinutes(58),
              NextProcess = 5,
              Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial()
            {
              MaterialID = 100,
              Queue = "BBBB",
              Position = 0,
              Unique = "uniq100",
              PartNameOrCasting = "part100",
              NumProcesses = 100,
              AddTimeUTC = start.AddHours(-1),
              NextProcess = 101,
              Paths = ImmutableDictionary<int, int>.Empty
            }
          }
        );
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().Be(5);

      _jobLog
        .GetLogEntries(start, DateTime.UtcNow)
        .Should()
        .BeEquivalentTo(expectedLogs, options => options.ComparingByMembers<LogEntry>());
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
      _jobLog.RecordAddMaterialToQueue(
        new EventLogMaterial()
        {
          MaterialID = mat1.MaterialID,
          Process = 14,
          Face = ""
        },
        "AAAA",
        -1,
        null,
        null,
        start
      );
      expectedLogs.Add(AddToQueueExpectedEntry(SetProcInMat(14)(mat1), 1, "AAAA", 0, start));
      _jobLog.RecordAddMaterialToQueue(
        new EventLogMaterial()
        {
          MaterialID = mat2.MaterialID,
          Process = 0,
          Face = ""
        },
        "AAAA",
        -1,
        null,
        null,
        start
      );
      expectedLogs.Add(AddToQueueExpectedEntry(SetProcInMat(0)(mat2), 2, "AAAA", 1, start));

      _jobLog
        .GetMaterialInAllQueues()
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new QueuedMaterial()
            {
              MaterialID = 1,
              Queue = "AAAA",
              Position = 0,
              Unique = "uniq1",
              PartNameOrCasting = "part111",
              NumProcesses = 19,
              AddTimeUTC = start,
              NextProcess = 15,
              Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial()
            {
              MaterialID = 2,
              Queue = "AAAA",
              Position = 1,
              Unique = "uniq2",
              PartNameOrCasting = "part2",
              NumProcesses = 22,
              AddTimeUTC = start,
              NextProcess = 1,
              Paths = ImmutableDictionary<int, int>.Empty
            }
          }
        );

      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(15);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(1);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().BeNull();
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().BeNull();

      // loading should remove from queue
      var loadEndActual = _jobLog.RecordLoadEnd(
        toLoad: new[]
        {
          new MaterialToLoadOntoPallet()
          {
            LoadStation = 16,
            Elapsed = TimeSpan.FromMinutes(10),
            Faces = ImmutableList.Create(
              new MaterialToLoadOntoFace()
              {
                FaceNum = 1234,
                Process = mat1.Process,
                Path = null,
                ActiveOperationTime = TimeSpan.FromMinutes(20),
                MaterialIDs = ImmutableList.Create(mat1.MaterialID)
              }
            )
          }
        },
        1,
        start.AddMinutes(10)
      );
      loadEndActual
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new LogEntry(
              4,
              new[] { mat1 with { Face = "1234" } },
              1,
              LogType.LoadUnloadCycle,
              "L/U",
              16,
              "LOAD",
              false,
              start.AddMinutes(10),
              "LOAD",
              TimeSpan.FromMinutes(10),
              TimeSpan.FromMinutes(20)
            ),
            RemoveFromQueueExpectedEntry(
              SetProcInMat(mat1.Process - 1)(mat1),
              3,
              "AAAA",
              0,
              10,
              start.AddMinutes(10)
            )
          },
          options => options.ComparingByMembers<LogEntry>()
        );
      expectedLogs.AddRange(loadEndActual);

      _jobLog
        .GetMaterialInAllQueues()
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new QueuedMaterial()
            {
              MaterialID = 2,
              Queue = "AAAA",
              Position = 0,
              Unique = "uniq2",
              PartNameOrCasting = "part2",
              NumProcesses = 22,
              AddTimeUTC = start,
              NextProcess = 1,
              Paths = ImmutableDictionary<int, int>.Empty
            }
          }
        );

      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(1);

      //unloading should add to queue
      var unloadEndActual = _jobLog.RecordUnloadEnd(
        new[] { mat1, mat3, mat4 }.Select(EventLogMaterial.FromLogMat),
        5,
        77,
        start.AddMinutes(30),
        TimeSpan.FromMinutes(52),
        TimeSpan.FromMinutes(23),
        new Dictionary<long, string>() { { 1, "AAAA" }, { 3, "AAAA" } }
      );
      unloadEndActual
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new LogEntry(
              7,
              new[] { mat1, mat3, mat4 },
              5,
              LogType.LoadUnloadCycle,
              "L/U",
              77,
              "UNLOAD",
              false,
              start.AddMinutes(30),
              "UNLOAD",
              TimeSpan.FromMinutes(52),
              TimeSpan.FromMinutes(23)
            ),
            AddToQueueExpectedEntry(mat1, 5, "AAAA", 1, start.AddMinutes(30), reason: "Unloaded"),
            AddToQueueExpectedEntry(mat3, 6, "AAAA", 2, start.AddMinutes(30), reason: "Unloaded"),
          },
          options => options.ComparingByMembers<LogEntry>()
        );
      expectedLogs.AddRange(unloadEndActual);

      _jobLog
        .GetMaterialInAllQueues()
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new QueuedMaterial()
            {
              MaterialID = 2,
              Queue = "AAAA",
              Position = 0,
              Unique = "uniq2",
              PartNameOrCasting = "part2",
              NumProcesses = 22,
              AddTimeUTC = start,
              NextProcess = 1,
              Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial()
            {
              MaterialID = 1,
              Queue = "AAAA",
              Position = 1,
              Unique = "uniq1",
              PartNameOrCasting = "part111",
              NumProcesses = 19,
              AddTimeUTC = start.AddMinutes(30),
              NextProcess = 16,
              Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial()
            {
              MaterialID = 3,
              Queue = "AAAA",
              Position = 2,
              Unique = "uniq3",
              PartNameOrCasting = "part3",
              NumProcesses = 36,
              AddTimeUTC = start.AddMinutes(30),
              NextProcess = 4,
              Paths = ImmutableDictionary<int, int>.Empty
            }
          }
        );

      _jobLog
        .GetLogEntries(start, DateTime.UtcNow)
        .Should()
        .BeEquivalentTo(expectedLogs, options => options.ComparingByMembers<LogEntry>());

      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(1); // unchanged, wasn't unloaded
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).Should().Be(5);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void BulkAddRemoveCastings(bool useSerial, bool existingMats)
    {
      var addTime = DateTime.UtcNow.AddHours(-2);

      long matOffset = 0;
      int posOffset = 0;

      if (existingMats)
      {
        matOffset = 1;
        posOffset = 1;
        _jobLog.AllocateMaterialIDForCasting("castingQ").Should().Be(1);
        _jobLog.RecordAddMaterialToQueue(
          matID: 1,
          process: 0,
          queue: "queueQQ",
          position: 0,
          operatorName: null,
          reason: null,
          addTime
        );
      }

      var matRet = _jobLog.BulkAddNewCastingsInQueue(
        casting: "castingQ",
        qty: 5,
        queue: "queueQQ",
        useSerial ? new[] { "1", "2", "3", "4", "5" } : new string[] { },
        operatorName: "operName",
        reason: "TheReason",
        timeUTC: addTime
      );

      matRet.MaterialIds.Should().BeEquivalentTo(Enumerable.Range(1, 5).Select(i => matOffset + i));

      var expectedLogs = Enumerable
        .Range(1, 5)
        .Select(i =>
        {
          var l = new LogEntry(
            cntr: -1,
            mat: new[]
            {
              new LogMaterial(
                matID: matOffset + i,
                uniq: "",
                proc: 0,
                part: "castingQ",
                numProc: 1,
                serial: useSerial ? i.ToString() : "",
                workorder: "",
                face: ""
              )
            },
            pal: 0,
            ty: LogType.AddToQueue,
            locName: "queueQQ",
            locNum: posOffset + i - 1,
            prog: "TheReason",
            start: false,
            endTime: addTime,
            result: ""
          );
          l %= d => d.ProgramDetails["operator"] = "operName";
          return l;
        })
        .ToList();

      if (useSerial)
      {
        expectedLogs.AddRange(
          Enumerable
            .Range(1, 5)
            .Select(
              i =>
                new LogEntry(
                  cntr: -1,
                  mat: new[]
                  {
                    new LogMaterial(
                      matID: matOffset + i,
                      uniq: "",
                      proc: 0,
                      part: "castingQ",
                      numProc: 1,
                      serial: useSerial ? i.ToString() : "",
                      workorder: "",
                      face: ""
                    )
                  },
                  pal: 0,
                  ty: LogType.PartMark,
                  locName: "Mark",
                  locNum: 1,
                  prog: "MARK",
                  start: false,
                  endTime: addTime,
                  result: i.ToString()
                )
            )
        );
      }

      matRet.Logs
        .Should()
        .BeEquivalentTo(
          expectedLogs,
          options => options.Excluding(o => o.Counter).ComparingByMembers<LogEntry>()
        );

      _jobLog
        .GetRecentLog(-1)
        .Should()
        .BeEquivalentTo(
          expectedLogs.Concat(
            existingMats
              ? new[]
              {
                new LogEntry(
                  cntr: -1,
                  mat: new[]
                  {
                    new LogMaterial(
                      matID: 1,
                      uniq: "",
                      proc: 0,
                      part: "castingQ",
                      numProc: 1,
                      serial: "",
                      workorder: "",
                      face: ""
                    )
                  },
                  pal: 0,
                  ty: LogType.AddToQueue,
                  locName: "queueQQ",
                  locNum: 0,
                  prog: "",
                  start: false,
                  endTime: addTime,
                  result: ""
                )
              }
              : Enumerable.Empty<LogEntry>()
          ),
          options => options.Excluding(o => o.Counter).ComparingByMembers<LogEntry>()
        );

      _jobLog
        .GetMaterialInAllQueues()
        .Should()
        .BeEquivalentTo(
          Enumerable
            .Range(1, existingMats ? 6 : 5)
            .Select(
              i =>
                new QueuedMaterial()
                {
                  MaterialID = i,
                  Queue = "queueQQ",
                  Position = i - 1,
                  Unique = "",
                  PartNameOrCasting = "castingQ",
                  NumProcesses = 1,
                  NextProcess = 1,
                  Serial = useSerial
                    ? (existingMats ? (i == 1 ? null : (i - 1).ToString()) : i.ToString())
                    : null,
                  Paths = ImmutableDictionary<int, int>.Empty,
                  AddTimeUTC = addTime
                }
            )
        );

      _jobLog
        .GetUnallocatedMaterialInQueue("queueQQ", "castingQ")
        .Should()
        .BeEquivalentTo(
          Enumerable
            .Range(1, existingMats ? 6 : 5)
            .Select(
              i =>
                new QueuedMaterial()
                {
                  MaterialID = i,
                  Queue = "queueQQ",
                  Position = i - 1,
                  Unique = "",
                  PartNameOrCasting = "castingQ",
                  NumProcesses = 1,
                  NextProcess = 1,
                  Serial = useSerial
                    ? (existingMats ? (i == 1 ? null : (i - 1).ToString()) : i.ToString())
                    : null,
                  Paths = ImmutableDictionary<int, int>.Empty,
                  AddTimeUTC = addTime
                }
            )
        );
      _jobLog.GetUnallocatedMaterialInQueue("ohuouh", "castingQ").Should().BeEmpty();
      _jobLog.GetUnallocatedMaterialInQueue("queueQQ", "qouhwef").Should().BeEmpty();

      var removeTime = DateTime.UtcNow.AddHours(-1);

      _jobLog
        .BulkRemoveMaterialFromAllQueues(new long[] { 1, 2 }, null, removeTime)
        .Should()
        .BeEquivalentTo(
          (new long[] { 1, 2 }).Select(
            matId =>
              new LogEntry(
                cntr: -1,
                mat: new[]
                {
                  new LogMaterial(
                    matID: matId,
                    uniq: "",
                    proc: 0,
                    part: "castingQ",
                    numProc: 1,
                    serial: useSerial ? (existingMats ? (matId == 2 ? "1" : "") : matId.ToString()) : "",
                    workorder: "",
                    face: ""
                  )
                },
                pal: 0,
                ty: LogType.RemoveFromQueue,
                locName: "queueQQ",
                locNum: 0,
                prog: "",
                start: false,
                endTime: removeTime,
                result: "",
                elapsed: removeTime.Subtract(addTime),
                active: TimeSpan.Zero
              )
          ),
          options => options.Excluding(o => o.Counter).ComparingByMembers<LogEntry>()
        );

      _jobLog
        .GetMaterialInAllQueues()
        .Should()
        .BeEquivalentTo(
          Enumerable
            .Range(3, existingMats ? 4 : 3)
            .Select(
              i =>
                new QueuedMaterial()
                {
                  MaterialID = i,
                  Queue = "queueQQ",
                  Position = i - 3,
                  Unique = "",
                  PartNameOrCasting = "castingQ",
                  NumProcesses = 1,
                  NextProcess = 1,
                  Serial = useSerial
                    ? (existingMats ? (i == 1 ? null : (i - 1).ToString()) : i.ToString())
                    : null,
                  Paths = ImmutableDictionary<int, int>.Empty,
                  AddTimeUTC = addTime
                }
            )
        );
    }

    [Fact]
    public void ReuseMatIDsWhenBulkAdding()
    {
      _jobLog
        .BulkAddNewCastingsInQueue(
          casting: "castingQ",
          qty: 2,
          queue: "queueQQ",
          serials: new[] { "1", "2" },
          operatorName: "theoper"
        )
        .MaterialIds.Should()
        .BeEquivalentTo(new[] { 1, 2 });

      _jobLog.GetMaterialDetails(1).PartName.Should().Be("castingQ");

      //adding again should throw, since they are in the queue
      _jobLog
        .Invoking(
          j =>
            j.BulkAddNewCastingsInQueue(
              casting: "castingQ",
              qty: 2,
              queue: "queueQQ",
              serials: new[] { "1", "2" },
              operatorName: "theoper",
              throwOnExistingSerial: true
            )
        )
        .Should()
        .Throw<Exception>()
        .WithMessage("Serial 1 already exists in the database with MaterialID 1");

      // adding without throwing should create new material ids
      _jobLog
        .BulkAddNewCastingsInQueue(
          casting: "castingQ",
          qty: 2,
          queue: "queueQQ",
          serials: new[] { "1", "2" },
          operatorName: "theoper",
          throwOnExistingSerial: false
        )
        .MaterialIds.Should()
        .BeEquivalentTo(new[] { 3, 4 });

      // now try with a load and machine event
      _jobLog
        .BulkAddNewCastingsInQueue(
          casting: "castingQ",
          qty: 2,
          queue: "queueQQ",
          serials: new[] { "5", "6" },
          operatorName: "theoper"
        )
        .MaterialIds.Should()
        .BeEquivalentTo(new[] { 5, 6 });

      _jobLog.RecordLoadEnd(
        new[]
        {
          new MaterialToLoadOntoPallet()
          {
            LoadStation = 1,
            Faces = ImmutableList.Create(
              new MaterialToLoadOntoFace()
              {
                FaceNum = 1,
                Process = 1,
                Path = 1,
                ActiveOperationTime = TimeSpan.FromMinutes(2),
                MaterialIDs = ImmutableList.Create(5L)
              }
            )
          }
        },
        pallet: 5,
        timeUTC: DateTime.UtcNow
      );

      _jobLog.RecordRemoveMaterialFromAllQueues(matID: 6L, process: 0);

      _jobLog.RecordMachineStart(
        new[]
        {
          new EventLogMaterial()
          {
            MaterialID = 6,
            Process = 1,
            Face = ""
          }
        },
        pallet: 4,
        statName: "MC",
        statNum: 2,
        program: "prog",
        timeUTC: DateTime.UtcNow
      );

      //adding again should throw, since 5 has a load event
      _jobLog
        .Invoking(
          j =>
            j.BulkAddNewCastingsInQueue(
              casting: "castingQ",
              qty: 1,
              queue: "queueQQ",
              serials: new[] { "5" },
              operatorName: "theoper",
              throwOnExistingSerial: true
            )
        )
        .Should()
        .Throw<Exception>()
        .WithMessage("Serial 5 already exists in the database with MaterialID 5");

      //adding again should throw, since 6 has a machine event
      _jobLog
        .Invoking(
          j =>
            j.BulkAddNewCastingsInQueue(
              casting: "castingQ",
              qty: 1,
              queue: "queueQQ",
              serials: new[] { "6" },
              operatorName: "theoper",
              throwOnExistingSerial: true
            )
        )
        .Should()
        .Throw<Exception>()
        .WithMessage("Serial 6 already exists in the database with MaterialID 6");

      // adding without throwing should create new
      _jobLog
        .BulkAddNewCastingsInQueue(
          casting: "casting22",
          qty: 2,
          queue: "queueQQ",
          serials: new[] { "5", "6" },
          operatorName: "theoper",
          throwOnExistingSerial: false
        )
        .MaterialIds.Should()
        .BeEquivalentTo(new[] { 7, 8 });
      _jobLog.GetMaterialDetails(7).PartName.Should().Be("casting22");

      // now adding with no load/machine and not in a queue should reuse
      _jobLog
        .BulkAddNewCastingsInQueue(
          casting: "castingQ",
          qty: 2,
          queue: "queueQQ",
          serials: new[] { "9", "10" },
          operatorName: "theoper"
        )
        .MaterialIds.Should()
        .BeEquivalentTo(new[] { 9, 10 });

      _jobLog.GetMaterialDetails(9).PartName.Should().Be("castingQ");
      _jobLog.BulkRemoveMaterialFromAllQueues(new long[] { 9, 10 });

      // adding serial 9 should be reused
      _jobLog
        .BulkAddNewCastingsInQueue(
          casting: "casting44",
          qty: 1,
          queue: "queueQQ",
          serials: new[] { "9" },
          operatorName: "theoper",
          throwOnExistingSerial: true
        )
        .MaterialIds.Should()
        .BeEquivalentTo(new[] { 9 });

      // the casting should have been updated too
      _jobLog.GetMaterialDetails(9).PartName.Should().Be("casting44");
    }

    [Fact]
    public void AllocateCastingsFromQueues()
    {
      var mat1 = new LogMaterial(
        _jobLog.AllocateMaterialIDForCasting("casting1"),
        "",
        0,
        "casting1",
        1,
        "",
        "",
        ""
      );
      var mat2 = new LogMaterial(
        _jobLog.AllocateMaterialIDForCasting("casting1"),
        "",
        0,
        "casting1",
        1,
        "",
        "",
        ""
      );
      var mat3 = new LogMaterial(
        _jobLog.AllocateMaterialIDForCasting("casting3"),
        "",
        0,
        "casting3",
        1,
        "",
        "",
        ""
      );

      _jobLog.RecordAddMaterialToQueue(EventLogMaterial.FromLogMat(mat1), "queue1", 0, null, null);
      _jobLog.RecordAddMaterialToQueue(EventLogMaterial.FromLogMat(mat2), "queue1", 1, null, null);
      _jobLog.RecordAddMaterialToQueue(EventLogMaterial.FromLogMat(mat3), "queue1", 2, null, null);

      _jobLog
        .GetMaterialDetails(mat1.MaterialID)
        .Should()
        .BeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = mat1.MaterialID,
            JobUnique = null,
            PartName = "casting1",
            NumProcesses = 1,
          }
        );

      _jobLog
        .GetMaterialDetails(mat2.MaterialID)
        .Should()
        .BeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = mat2.MaterialID,
            JobUnique = null,
            PartName = "casting1",
            NumProcesses = 1,
          }
        );

      _jobLog
        .GetMaterialDetails(mat3.MaterialID)
        .Should()
        .BeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = mat3.MaterialID,
            JobUnique = null,
            PartName = "casting3",
            NumProcesses = 1,
          }
        );

      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(1);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(1);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(1);

      _jobLog
        .AllocateCastingsInQueue(
          queue: "queue1",
          casting: "unused",
          unique: "uniqAAA",
          part: "part1",
          proc1Path: 1000,
          numProcesses: 15,
          count: 2
        )
        .Should()
        .BeEquivalentTo(new long[] { });

      _jobLog
        .AllocateCastingsInQueue(
          queue: "queue1",
          casting: "casting1",
          unique: "uniqAAA",
          part: "part1",
          proc1Path: 1234,
          numProcesses: 6312,
          count: 50
        )
        .Should()
        .BeEquivalentTo(new long[] { });

      _jobLog
        .AllocateCastingsInQueue(
          queue: "queue1",
          casting: "casting1",
          unique: "uniqAAA",
          part: "part1",
          proc1Path: 1234,
          numProcesses: 6312,
          count: 2
        )
        .Should()
        .BeEquivalentTo(new[] { mat1.MaterialID, mat2.MaterialID });

      _jobLog
        .GetMaterialDetails(mat1.MaterialID)
        .Should()
        .BeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = mat1.MaterialID,
            JobUnique = "uniqAAA",
            PartName = "part1",
            NumProcesses = 6312,
            Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1234)
          },
          options => options.ComparingByMembers<MaterialDetails>()
        );

      _jobLog
        .GetMaterialDetails(mat2.MaterialID)
        .Should()
        .BeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = mat2.MaterialID,
            JobUnique = "uniqAAA",
            PartName = "part1",
            NumProcesses = 6312,
            Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1234)
          },
          options => options.ComparingByMembers<MaterialDetails>()
        );

      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(1);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(1);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(1);

      _jobLog.MarkCastingsAsUnallocated(new[] { mat1.MaterialID }, casting: "newcasting");

      _jobLog
        .GetMaterialDetails(mat1.MaterialID)
        .Should()
        .BeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = mat1.MaterialID,
            JobUnique = null,
            PartName = "newcasting",
            NumProcesses = 6312,
          }
        );

      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).Should().Be(1);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).Should().Be(1);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).Should().Be(1);
    }

    [Theory]
    [InlineData(true, true, null)]
    [InlineData(true, true, "thecasting")]
    [InlineData(false, true, null)]
    [InlineData(false, true, "thecasting")]
    [InlineData(true, false, null)]
    [InlineData(false, false, null)]
    public void OverrideMatOnPal(bool firstPalletCycle, bool newMatUnassigned, string rawMatName)
    {
      var now = DateTime.UtcNow.AddHours(-5);

      if (!firstPalletCycle)
      {
        var firstMatId = _jobLog.AllocateMaterialID("uniq1", "part1", 2);
        var firstMat = new EventLogMaterial()
        {
          MaterialID = firstMatId,
          Process = 1,
          Face = "12"
        };
        _jobLog.RecordSerialForMaterialID(firstMat, "aaaa", now);
        _jobLog.RecordLoadEnd(
          toLoad: new[]
          {
            new MaterialToLoadOntoPallet()
            {
              LoadStation = 3,
              Elapsed = TimeSpan.FromMinutes(5),
              Faces = ImmutableList.Create(
                new MaterialToLoadOntoFace()
                {
                  FaceNum = 12,
                  Process = firstMat.Process,
                  Path = null,
                  ActiveOperationTime = TimeSpan.FromMinutes(4),
                  MaterialIDs = ImmutableList.Create(firstMat.MaterialID)
                }
              )
            }
          },
          pallet: 5,
          timeUTC: now.AddMinutes(1)
        );
        _jobLog.RecordMachineEnd(
          new[] { firstMat },
          pallet: 5,
          statName: "Mach",
          statNum: 4,
          program: "proggg",
          result: "proggg",
          timeUTC: now.AddMinutes(2),
          elapsed: TimeSpan.FromMinutes(10),
          active: TimeSpan.FromMinutes(11)
        );
        _jobLog.CompletePalletCycle(pal: 5, timeUTC: now.AddMinutes(5), foreignID: "");

        now = now.AddMinutes(5).AddSeconds(1);
      }

      // ------------------------------------------------------
      // Material
      // ------------------------------------------------------
      var initiallyLoadedMatProc0 = new EventLogMaterial()
      {
        MaterialID = _jobLog.AllocateMaterialID("uniq1", "part1", 2),
        Process = 0,
        Face = ""
      };
      var initiallyLoadedMatProc1 = new EventLogMaterial()
      {
        MaterialID = initiallyLoadedMatProc0.MaterialID,
        Process = 1,
        Face = "1"
      };

      var initialMatAddToQueueTime = now;
      _jobLog.RecordAddMaterialToQueue(
        initiallyLoadedMatProc0,
        queue: "rawmat",
        position: -1,
        operatorName: null,
        reason: null,
        timeUTC: now
      );
      _jobLog.RecordSerialForMaterialID(initiallyLoadedMatProc0, "bbbb", now);
      _jobLog.RecordPathForProcess(initiallyLoadedMatProc0.MaterialID, process: 1, path: 5);
      _jobLog.NextProcessForQueuedMaterial(initiallyLoadedMatProc0.MaterialID).Should().Be(1);

      now = now.AddMinutes(1);

      long newMatId;
      if (newMatUnassigned)
      {
        if (!string.IsNullOrEmpty(rawMatName))
        {
          _jobLog.AddJobs(
            new NewJobs()
            {
              Jobs = ImmutableList.Create(
                new Job()
                {
                  UniqueStr = "uniq1",
                  PartName = "part1",
                  Cycles = 10,
                  Processes = ImmutableList.Create(
                    new ProcessInfo()
                    {
                      Paths = ImmutableList.Create(EmptyPath with { Casting = rawMatName })
                    }
                  ),
                  RouteStartUTC = DateTime.MinValue,
                  RouteEndUTC = DateTime.MinValue,
                  Archived = false,
                }
              ),
              ScheduleId = "anotherSchId"
            },
            null,
            true
          );
        }
        newMatId = _jobLog.AllocateMaterialIDForCasting(rawMatName ?? "part1");
      }
      else
      {
        newMatId = _jobLog.AllocateMaterialID("uniq1", "part1", 2);
      }

      var newMatProc0 = new EventLogMaterial()
      {
        MaterialID = newMatId,
        Process = 0,
        Face = ""
      };
      var newMatProc1 = new EventLogMaterial()
      {
        MaterialID = newMatId,
        Process = 1,
        Face = "1"
      };

      var newMatAddToQueueTime = now;
      _jobLog.RecordAddMaterialToQueue(
        newMatProc0,
        queue: "rawmat",
        position: -1,
        operatorName: null,
        reason: null,
        timeUTC: now
      );
      _jobLog.RecordSerialForMaterialID(newMatProc0, "cccc", now);
      _jobLog.NextProcessForQueuedMaterial(newMatProc0.MaterialID).Should().Be(1);

      now = now.AddMinutes(1);

      // ------------------------------------------------------
      // Original Events
      // ------------------------------------------------------

      var origLog = new List<LogEntry>();

      var loadEndOrigEvts = _jobLog.RecordLoadEnd(
        toLoad: new[]
        {
          new MaterialToLoadOntoPallet()
          {
            LoadStation = 2,
            Elapsed = TimeSpan.FromMinutes(4),
            Faces = ImmutableList.Create(
              new MaterialToLoadOntoFace()
              {
                FaceNum = 1,
                Process = initiallyLoadedMatProc1.Process,
                Path = null,
                ActiveOperationTime = TimeSpan.FromMinutes(5),
                MaterialIDs = ImmutableList.Create(initiallyLoadedMatProc1.MaterialID)
              }
            )
          }
        },
        pallet: 5,
        timeUTC: now
      );
      loadEndOrigEvts.Count().Should().Be(2);
      loadEndOrigEvts.First().LogType.Should().Be(LogType.RemoveFromQueue);
      loadEndOrigEvts.First().Material.First().MaterialID.Should().Be(initiallyLoadedMatProc1.MaterialID);
      loadEndOrigEvts.First().Material.First().Process.Should().Be(0);
      loadEndOrigEvts.Last().LogType.Should().Be(LogType.LoadUnloadCycle);
      origLog.Add(loadEndOrigEvts.Last());

      var initialMatRemoveQueueTime = now;

      now = now.AddMinutes(1);

      origLog.Add(
        _jobLog.RecordPalletArriveStocker(
          new[] { initiallyLoadedMatProc1 },
          pallet: 5,
          stockerNum: 5,
          timeUTC: now,
          waitForMachine: false
        )
      );

      now = now.AddMinutes(2);

      origLog.Add(
        _jobLog.RecordPalletDepartStocker(
          new[] { initiallyLoadedMatProc1 },
          pallet: 5,
          stockerNum: 5,
          timeUTC: now,
          waitForMachine: false,
          elapsed: TimeSpan.FromMinutes(2)
        )
      );

      now = now.AddMinutes(1);

      origLog.Add(
        _jobLog.RecordPalletArriveRotaryInbound(
          new[] { initiallyLoadedMatProc1 },
          pallet: 5,
          statName: "Mach",
          statNum: 3,
          timeUTC: now
        )
      );

      now = now.AddMinutes(1);

      origLog.Add(
        _jobLog.RecordPalletDepartRotaryInbound(
          new[] { initiallyLoadedMatProc1 },
          pallet: 5,
          statName: "Mach",
          statNum: 3,
          timeUTC: now,
          elapsed: TimeSpan.FromMinutes(5),
          rotateIntoWorktable: true
        )
      );

      now = now.AddMinutes(1);

      origLog.Add(
        _jobLog.RecordMachineStart(
          new[] { initiallyLoadedMatProc1 },
          pallet: 5,
          statName: "Mach",
          statNum: 3,
          program: "prog11",
          timeUTC: now
        )
      );

      now = now.AddMinutes(1);

      // ------------------------------------------------------
      // Do the swap
      // ------------------------------------------------------

      var result = _jobLog.SwapMaterialInCurrentPalletCycle(
        pallet: 5,
        oldMatId: initiallyLoadedMatProc1.MaterialID,
        newMatId: newMatProc1.MaterialID,
        operatorName: "theoper",
        quarantineQueue: "unused",
        timeUTC: now
      );

      // ------------------------------------------------------
      // Check Mat Details
      // ------------------------------------------------------

      _jobLog
        .GetMaterialDetails(initiallyLoadedMatProc0.MaterialID)
        .Should()
        .BeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = initiallyLoadedMatProc0.MaterialID,
            JobUnique = newMatUnassigned ? null : "uniq1",
            PartName = newMatUnassigned ? (rawMatName ?? "part1") : "part1",
            NumProcesses = 2,
            Workorder = null,
            Serial = "bbbb",
            Paths = newMatUnassigned ? null : ImmutableDictionary<int, int>.Empty.Add(1, 5)
          },
          options => options.ComparingByMembers<MaterialDetails>()
        );

      _jobLog
        .GetMaterialDetails(newMatId)
        .Should()
        .BeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = newMatId,
            JobUnique = "uniq1",
            PartName = "part1",
            NumProcesses = 2,
            Workorder = null,
            Serial = "cccc",
            Paths = ImmutableDictionary<int, int>.Empty.Add(1, 5)
          },
          options => options.ComparingByMembers<MaterialDetails>()
        );

      // ------------------------------------------------------
      // Check Logs
      // ------------------------------------------------------

      var initiallyLoadedLogMatProc0 = new LogMaterial(
        matID: initiallyLoadedMatProc0.MaterialID,
        uniq: newMatUnassigned ? "" : "uniq1",
        part: rawMatName ?? "part1",
        proc: 0,
        numProc: 2,
        serial: "bbbb",
        workorder: "",
        face: ""
      );
      var newLogMatProc0 = new LogMaterial(
        matID: newMatProc1.MaterialID,
        uniq: "uniq1",
        part: "part1",
        proc: 0,
        numProc: 2,
        serial: "cccc",
        workorder: "",
        face: ""
      );

      var expectedSwapMsg = new LogEntry(
        cntr: 0,
        mat: new[] { SetProcInMat(1)(initiallyLoadedLogMatProc0), SetProcInMat(1)(newLogMatProc0) },
        pal: 5,
        ty: LogType.SwapMaterialOnPallet,
        locName: "SwapMatOnPallet",
        locNum: 1,
        prog: "SwapMatOnPallet",
        start: false,
        endTime: now,
        result: "Replace bbbb with cccc on pallet 5"
      );

      var newLog = origLog
        .Select(
          TransformLog(
            initiallyLoadedMatProc1.MaterialID,
            mat =>
              new LogMaterial(
                matID: newMatProc1.MaterialID,
                uniq: "uniq1",
                proc: 1,
                part: "part1",
                numProc: 2,
                serial: "cccc",
                workorder: "",
                face: "1"
              )
          )
        )
        .ToList();

      result.ChangedLogEntries
        .Should()
        .BeEquivalentTo(newLog, options => options.Excluding(e => e.Counter).ComparingByMembers<LogEntry>());

      result.NewLogEntries
        .Should()
        .BeEquivalentTo(
          new[]
          {
            expectedSwapMsg,
            AddToQueueExpectedEntry(
              mat: initiallyLoadedLogMatProc0,
              cntr: 0,
              queue: "rawmat",
              position: 0,
              timeUTC: now,
              operName: "theoper",
              reason: "SwapMaterial"
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
          },
          options => options.Excluding(e => e.Counter).ComparingByMembers<LogEntry>()
        );

      _jobLog.NextProcessForQueuedMaterial(initiallyLoadedLogMatProc0.MaterialID).Should().Be(1);

      _jobLog
        .GetLogForMaterial(initiallyLoadedMatProc0.MaterialID)
        .Should()
        .BeEquivalentTo(
          new[]
          {
            RecordSerialExpectedEntry(
              mat: initiallyLoadedLogMatProc0,
              cntr: 0,
              serial: "bbbb",
              timeUTC: initialMatAddToQueueTime
            ),
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
            expectedSwapMsg,
            AddToQueueExpectedEntry(
              mat: initiallyLoadedLogMatProc0,
              cntr: 0,
              queue: "rawmat",
              position: 0,
              timeUTC: now,
              operName: "theoper",
              reason: "SwapMaterial"
            ),
          },
          options => options.Excluding(e => e.Counter).ComparingByMembers<LogEntry>()
        );

      // log for newMat matches
      _jobLog
        .GetLogForMaterial(newMatProc1.MaterialID)
        .Should()
        .BeEquivalentTo(
          newLog.Concat(
            new[]
            {
              RecordSerialExpectedEntry(
                mat: newLogMatProc0,
                cntr: 0,
                serial: "cccc",
                timeUTC: newMatAddToQueueTime
              ),
              AddToQueueExpectedEntry(
                mat: newLogMatProc0,
                cntr: 0,
                queue: "rawmat",
                position: 1,
                timeUTC: newMatAddToQueueTime
              ),
              expectedSwapMsg,
              RemoveFromQueueExpectedEntry(
                mat: newLogMatProc0,
                cntr: 0,
                queue: "rawmat",
                position: 0,
                elapsedMin: now.Subtract(newMatAddToQueueTime).TotalMinutes,
                timeUTC: now,
                operName: "theoper"
              )
            }
          ),
          options => options.Excluding(c => c.Counter).ComparingByMembers<LogEntry>()
        );

      _jobLog
        .GetLogForMaterial(newMatProc1.MaterialID)
        .Where(
          e =>
            e.LogType != LogType.MachineCycle
            && e.LogType != LogType.LoadUnloadCycle
            && e.LogType != LogType.PalletInStocker
            && e.LogType != LogType.PalletOnRotaryInbound
            && e.LogType != LogType.SwapMaterialOnPallet
        )
        .SelectMany(e => e.Material)
        .Select(m => m.Process)
        .Max()
        .Should()
        .Be(0);
    }

    [Fact]
    public void ErrorsOnBadOverrideMatOnPal()
    {
      var now = DateTime.UtcNow.AddHours(-5);

      _jobLog.AddJobs(
        new NewJobs()
        {
          Jobs = ImmutableList.Create(
            new Job()
            {
              UniqueStr = "uniq1",
              PartName = "part1",
              Cycles = 10,
              Processes = ImmutableList.Create(
                new ProcessInfo() { Paths = ImmutableList.Create(EmptyPath with { Casting = "thecasting" }) }
              ),
              RouteStartUTC = DateTime.MinValue,
              RouteEndUTC = DateTime.MinValue,
              Archived = false,
            }
          ),
          ScheduleId = "aschId"
        },
        null,
        true
      );

      var firstMatId = _jobLog.AllocateMaterialID("uniq1", "part1", 2);
      var firstMatProc0 = new EventLogMaterial()
      {
        MaterialID = firstMatId,
        Process = 0,
        Face = ""
      };
      var firstMat = new EventLogMaterial()
      {
        MaterialID = firstMatId,
        Process = 1,
        Face = "1"
      };
      _jobLog.RecordSerialForMaterialID(firstMatProc0, serial: "aaaa", timeUTC: now);
      _jobLog.RecordLoadEnd(
        toLoad: new[]
        {
          new MaterialToLoadOntoPallet()
          {
            LoadStation = 3,
            Elapsed = TimeSpan.FromMinutes(5),
            Faces = ImmutableList.Create(
              new MaterialToLoadOntoFace()
              {
                FaceNum = 12,
                Process = firstMat.Process,
                Path = null,
                ActiveOperationTime = TimeSpan.FromMinutes(4),
                MaterialIDs = ImmutableList.Create(firstMat.MaterialID)
              }
            )
          }
        },
        pallet: 5,
        timeUTC: now.AddMinutes(1)
      );
      _jobLog.RecordMachineEnd(
        new[] { firstMat },
        pallet: 5,
        statName: "Mach",
        statNum: 4,
        program: "proggg",
        result: "proggg",
        timeUTC: now.AddMinutes(2),
        elapsed: TimeSpan.FromMinutes(10),
        active: TimeSpan.FromMinutes(11)
      );
      _jobLog.RecordPathForProcess(firstMatId, process: 1, path: 10);

      var differentUniqMatId = _jobLog.AllocateMaterialID("uniq2", "part1", 2);

      now = now.AddMinutes(5).AddSeconds(1);

      _jobLog
        .Invoking(
          j =>
            j.SwapMaterialInCurrentPalletCycle(
              pallet: 5,
              oldMatId: 12345,
              newMatId: 98765,
              operatorName: null,
              quarantineQueue: "unusedquarantine"
            )
        )
        .Should()
        .Throw<ConflictRequestException>()
        .WithMessage("Unable to find material");

      _jobLog
        .Invoking(
          j =>
            j.SwapMaterialInCurrentPalletCycle(
              pallet: 5,
              oldMatId: firstMatId,
              newMatId: differentUniqMatId,
              operatorName: null,
              quarantineQueue: "unusedquarantine"
            )
        )
        .Should()
        .Throw<ConflictRequestException>()
        .WithMessage("Overriding material on pallet must use material from the same job");

      var existingPathMatId = _jobLog.AllocateMaterialID("uniq1", "part1", 2);
      _jobLog.RecordPathForProcess(existingPathMatId, process: 1, path: 10);

      _jobLog
        .Invoking(
          j =>
            j.SwapMaterialInCurrentPalletCycle(
              pallet: 5,
              oldMatId: firstMatId,
              newMatId: differentUniqMatId,
              operatorName: null,
              quarantineQueue: "unusedquarantine"
            )
        )
        .Should()
        .Throw<ConflictRequestException>()
        .WithMessage("Overriding material on pallet must use material from the same job");

      var otherCastingMatId = _jobLog.AllocateMaterialIDForCasting("othercasting");
      _jobLog
        .Invoking(
          j =>
            j.SwapMaterialInCurrentPalletCycle(
              pallet: 5,
              oldMatId: firstMatId,
              newMatId: otherCastingMatId,
              operatorName: null,
              quarantineQueue: "unusedquarantine"
            )
        )
        .Should()
        .Throw<ConflictRequestException>()
        .WithMessage("Material swap of unassigned material does not match part name or raw material name");
    }

    [Fact]
    public void InvalidatesCycle()
    {
      var now = DateTime.UtcNow.AddHours(-5);

      // ------------------------------------------------------
      // Material
      // ------------------------------------------------------

      var matProc0 = new EventLogMaterial()
      {
        MaterialID = _jobLog.AllocateMaterialID("uniq1", "part1", 2),
        Process = 0,
        Face = ""
      };
      var matProc1 = new EventLogMaterial()
      {
        MaterialID = matProc0.MaterialID,
        Process = 1,
        Face = "12"
      };

      var initialMatAddToQueueTime = now;
      _jobLog.RecordAddMaterialToQueue(
        matProc0,
        queue: "rawmat",
        position: -1,
        operatorName: null,
        reason: null,
        timeUTC: now
      );
      _jobLog.RecordSerialForMaterialID(matProc0, "bbbb", now);
      _jobLog.RecordPathForProcess(matProc0.MaterialID, process: 1, path: 5);
      _jobLog.NextProcessForQueuedMaterial(matProc0.MaterialID).Should().Be(1);

      now = now.AddMinutes(1);

      // ------------------------------------------------------
      // Original Events
      // ------------------------------------------------------

      var origMatLog = new List<LogEntry>();
      var origPalLog = new List<LogEntry>();

      var loadEndOrigEvts = _jobLog.RecordLoadEnd(
        toLoad: new[]
        {
          new MaterialToLoadOntoPallet()
          {
            LoadStation = 2,
            Elapsed = TimeSpan.FromMinutes(4),
            Faces = ImmutableList.Create(
              new MaterialToLoadOntoFace()
              {
                FaceNum = 12,
                Process = matProc1.Process,
                Path = null,
                ActiveOperationTime = TimeSpan.FromMinutes(5),
                MaterialIDs = ImmutableList.Create(matProc1.MaterialID)
              }
            )
          }
        },
        pallet: 5,
        timeUTC: now
      );
      loadEndOrigEvts.Count().Should().Be(2);
      loadEndOrigEvts.First().LogType.Should().Be(LogType.RemoveFromQueue);
      loadEndOrigEvts.First().Material.First().MaterialID.Should().Be(matProc1.MaterialID);
      loadEndOrigEvts.First().Material.First().Process.Should().Be(0);
      loadEndOrigEvts.Last().LogType.Should().Be(LogType.LoadUnloadCycle);
      origMatLog.Add(loadEndOrigEvts.Last());

      var initialMatRemoveQueueTime = now;

      now = now.AddMinutes(1);

      origPalLog.Add(
        _jobLog.RecordPalletArriveStocker(
          new[] { matProc1 },
          pallet: 5,
          stockerNum: 5,
          timeUTC: now,
          waitForMachine: false
        )
      );

      now = now.AddMinutes(2);

      origPalLog.Add(
        _jobLog.RecordPalletDepartStocker(
          new[] { matProc1 },
          pallet: 5,
          stockerNum: 5,
          timeUTC: now,
          waitForMachine: false,
          elapsed: TimeSpan.FromMinutes(2)
        )
      );

      now = now.AddMinutes(1);

      origPalLog.Add(
        _jobLog.RecordPalletArriveRotaryInbound(
          new[] { matProc1 },
          pallet: 5,
          statName: "Mach",
          statNum: 3,
          timeUTC: now
        )
      );

      now = now.AddMinutes(1);

      origPalLog.Add(
        _jobLog.RecordPalletDepartRotaryInbound(
          new[] { matProc1 },
          pallet: 5,
          statName: "Mach",
          statNum: 3,
          timeUTC: now,
          elapsed: TimeSpan.FromMinutes(5),
          rotateIntoWorktable: true
        )
      );

      now = now.AddMinutes(1);

      origMatLog.Add(
        _jobLog.RecordMachineStart(
          new[] { matProc1 },
          pallet: 5,
          statName: "Mach",
          statNum: 3,
          program: "prog11",
          timeUTC: now
        )
      );

      now = now.AddMinutes(1);

      origMatLog.AddRange(
        _jobLog.RecordAddMaterialToQueue(
          matProc1,
          queue: "xyz",
          position: 0,
          operatorName: "oper",
          reason: "SomeReason",
          timeUTC: now
        )
      );

      now = now.AddMinutes(1);

      _jobLog.NextProcessForQueuedMaterial(matProc0.MaterialID).Should().Be(2);

      // ------------------------------------------------------
      // Invalidate
      // ------------------------------------------------------

      var result = _jobLog.InvalidatePalletCycle(
        matId: matProc1.MaterialID,
        process: 1,
        oldMatPutInQueue: "quarantine",
        operatorName: "theoper",
        timeUTC: now
      );

      // ------------------------------------------------------
      // Check Logs
      // ------------------------------------------------------

      var logMatProc0 = new LogMaterial(
        matID: matProc0.MaterialID,
        uniq: "uniq1",
        part: "part1",
        proc: 0,
        numProc: 2,
        serial: "bbbb",
        workorder: "",
        face: ""
      );

      var expectedInvalidateMsg = new LogEntry(
        cntr: 0,
        mat: new[] { SetProcInMat(proc: 1)(logMatProc0) },
        pal: 0,
        ty: LogType.InvalidateCycle,
        locName: "InvalidateCycle",
        locNum: 1,
        prog: "InvalidateCycle",
        start: false,
        endTime: now,
        result: "Invalidate all events on cycle for pallet 5"
      );
      expectedInvalidateMsg %= e =>
      {
        e.ProgramDetails["EditedCounters"] = string.Join(",", origMatLog.Select(e => e.Counter));
        e.ProgramDetails["operator"] = "theoper";
      };

      var newMatLog = origMatLog
        .Select(RemoveActiveTime())
        .Select(evt =>
        {
          return evt.Produce(e => e.ProgramDetails["PalletCycleInvalidated"] = "1");
        })
        .ToList();

      result
        .Should()
        .BeEquivalentTo(
          new[]
          {
            expectedInvalidateMsg,
            RemoveFromQueueExpectedEntry(
              mat: logMatProc0,
              cntr: 0,
              queue: "xyz",
              position: 0,
              timeUTC: now,
              elapsedMin: 1,
              operName: "theoper"
            ),
            AddToQueueExpectedEntry(
              mat: logMatProc0,
              cntr: 0,
              queue: "quarantine",
              position: 0,
              timeUTC: now,
              operName: "theoper",
              reason: "InvalidateCycle"
            )
          },
          options => options.Excluding(e => e.Counter).ComparingByMembers<LogEntry>()
        );

      // log for initiallyLoadedMatProc matches, and importantly has only process 0 as max
      _jobLog.NextProcessForQueuedMaterial(matProc0.MaterialID).Should().Be(1);

      _jobLog
        .GetLogForMaterial(matProc0.MaterialID)
        .Should()
        .BeEquivalentTo(
          newMatLog
            .Concat(origPalLog)
            .Concat(
              new[]
              {
                RecordSerialExpectedEntry(
                  mat: logMatProc0,
                  cntr: 0,
                  serial: "bbbb",
                  timeUTC: initialMatAddToQueueTime
                ),
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
                RemoveFromQueueExpectedEntry(
                  mat: logMatProc0,
                  cntr: 0,
                  queue: "xyz",
                  position: 0,
                  timeUTC: now,
                  elapsedMin: 1,
                  operName: "theoper"
                ),
                AddToQueueExpectedEntry(
                  mat: logMatProc0,
                  cntr: 0,
                  queue: "quarantine",
                  position: 0,
                  timeUTC: now,
                  operName: "theoper",
                  reason: "InvalidateCycle"
                ),
              }
            ),
          options => options.Excluding(e => e.Counter).ComparingByMembers<LogEntry>()
        );
    }

    #region Helpers
    private LogEntry AddLogEntry(LogEntry l)
    {
      ((Repository)_jobLog).AddLogEntryFromUnitTest(l);
      return l;
    }

    private System.DateTime AddToDB(IList<LogEntry> logs)
    {
      System.DateTime last = default(System.DateTime);

      foreach (var l in logs)
      {
        ((Repository)_jobLog).AddLogEntryFromUnitTest(l);

        if (l.EndTimeUTC > last)
        {
          last = l.EndTimeUTC;
        }
      }

      return last;
    }

    public static long CheckLog(IList<LogEntry> logs, IList<LogEntry> otherLogs, System.DateTime start)
    {
      logs.Where(l => l.EndTimeUTC >= start)
        .Should()
        .BeEquivalentTo(
          otherLogs,
          options => options.Excluding(l => l.Counter).ComparingByMembers<LogEntry>()
        );
      return otherLogs.Select(l => l.Counter).Max();
    }

    private LogEntry RecordSerialExpectedEntry(LogMaterial mat, long cntr, string serial, DateTime timeUTC)
    {
      return new LogEntry(
        cntr: cntr,
        mat: new[] { mat },
        pal: 0,
        ty: LogType.PartMark,
        locName: "Mark",
        locNum: 1,
        prog: "MARK",
        start: false,
        endTime: timeUTC,
        result: serial
      );
    }

    private LogEntry RecordWorkorderExpectedEntry(
      LogMaterial mat,
      long cntr,
      string workorder,
      DateTime timeUTC
    )
    {
      return new LogEntry(
        cntr: cntr,
        mat: new[] { mat },
        pal: 0,
        ty: LogType.OrderAssignment,
        locName: "Order",
        locNum: 1,
        prog: "",
        start: false,
        endTime: timeUTC,
        result: workorder
      );
    }

    private LogEntry AddToQueueExpectedEntry(
      LogMaterial mat,
      long cntr,
      string queue,
      int position,
      DateTime timeUTC,
      string operName = null,
      string reason = null
    )
    {
      var e = new LogEntry(
        cntr: cntr,
        mat: new[] { mat },
        pal: 0,
        ty: LogType.AddToQueue,
        locName: queue,
        locNum: position,
        prog: reason ?? "",
        start: false,
        endTime: timeUTC,
        result: ""
      );
      if (!string.IsNullOrEmpty(operName))
      {
        e %= en => en.ProgramDetails.Add("operator", operName);
      }
      return e;
    }

    private LogEntry SignalQuarantineExpectedEntry(
      LogMaterial mat,
      long cntr,
      int pal,
      string queue,
      DateTime timeUTC,
      string operName = null,
      string reason = null
    )
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
        result: "QuarantineAfterUnload"
      );
      if (!string.IsNullOrEmpty(operName))
      {
        e %= en => en.ProgramDetails.Add("operator", operName);
      }
      if (!string.IsNullOrEmpty(operName))
      {
        e = e with { ProgramDetails = e.ProgramDetails.Add("note", reason) };
      }
      return e;
    }

    private LogEntry RemoveFromQueueExpectedEntry(
      LogMaterial mat,
      long cntr,
      string queue,
      int position,
      double elapsedMin,
      DateTime timeUTC,
      string operName = null
    )
    {
      var e = new LogEntry(
        cntr: cntr,
        mat: new[] { mat },
        pal: 0,
        ty: LogType.RemoveFromQueue,
        locName: queue,
        locNum: position,
        prog: "",
        start: false,
        endTime: timeUTC,
        result: "",
        elapsed: TimeSpan.FromMinutes(elapsedMin),
        active: TimeSpan.Zero
      );
      if (!string.IsNullOrEmpty(operName))
      {
        e %= en => en.ProgramDetails.Add("operator", operName);
      }
      return e;
    }

    public static Func<LogMaterial, LogMaterial> SetUniqInMat(string uniq, int? numProc = null)
    {
      return m =>
        new LogMaterial(
          matID: m.MaterialID,
          uniq: uniq,
          proc: m.Process,
          part: m.PartName,
          numProc: numProc ?? m.NumProcesses,
          serial: m.Serial,
          workorder: m.Workorder,
          face: m.Face
        );
    }

    public static Func<LogMaterial, LogMaterial> SetSerialInMat(string serial)
    {
      return m =>
        new LogMaterial(
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

    public static Func<LogMaterial, LogMaterial> SetWorkorderInMat(string work)
    {
      return m =>
        new LogMaterial(
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

    public static Func<LogMaterial, LogMaterial> SetProcInMat(int proc)
    {
      return m =>
        new LogMaterial(
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

    public static Func<LogEntry, LogEntry> TransformLog(
      long matID,
      Func<LogMaterial, LogMaterial> transformMat
    )
    {
      return copy =>
        copy with
        {
          Material = copy.Material.Select(m => m.MaterialID == matID ? transformMat(m) : m).ToImmutableList()
        };
    }

    private static Func<LogEntry, LogEntry> RemoveActiveTime()
    {
      return copy => copy with { ActiveOperationTime = TimeSpan.Zero };
    }
    #endregion
  }

  public class LogOneSerialPerMaterialSpec : IDisposable
  {
    private RepositoryConfig _repoCfg;
    private IRepository _jobLog;

    public LogOneSerialPerMaterialSpec()
    {
      var settings = new SerialSettings()
      {
        SerialType = SerialType.AssignOneSerialPerMaterial,
        ConvertMaterialIDToSerial = (m) => SerialSettings.ConvertToBase62(m, 10)
      };
      _repoCfg = RepositoryConfig.InitializeSingleThreadedMemoryDB(settings);
      _jobLog = _repoCfg.OpenConnection();
    }

    public void Dispose()
    {
      _repoCfg.CloseMemoryConnection();
    }

    [Fact]
    public void AllocateMatIds()
    {
      var now = DateTime.UtcNow;
      var matId = _jobLog.AllocateMaterialIDAndGenerateSerial(
        unique: "aaa",
        part: "bbb",
        numProc: 202,
        timeUTC: now,
        out var serialLogEntry
      );
      matId.Should().Be(1);

      var expected1 = new LogEntry(
        cntr: 1,
        mat: new[]
        {
          new LogMaterial(
            matID: 1,
            uniq: "aaa",
            proc: 0,
            part: "bbb",
            numProc: 202,
            serial: "0000000001",
            workorder: "",
            face: ""
          )
        },
        pal: 0,
        ty: LogType.PartMark,
        locName: "Mark",
        locNum: 1,
        prog: "MARK",
        start: false,
        endTime: now,
        result: "0000000001"
      );

      serialLogEntry.Should().BeEquivalentTo(expected1);

      _jobLog
        .GetMaterialDetails(matID: 1)
        .Should()
        .BeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = 1,
            JobUnique = "aaa",
            PartName = "bbb",
            NumProcesses = 202,
            Serial = "0000000001",
          }
        );

      _jobLog.GetLogForSerial("0000000001").Should().BeEquivalentTo(new[] { expected1 });

      var mat2 = _jobLog.AllocateMaterialIDWithSerialAndWorkorder(
        unique: "ttt",
        part: "zzz",
        numProc: 202,
        serial: "asdf",
        workorder: "www",
        timeUTC: now.AddSeconds(1),
        newLogEntries: out var newLogEvtsForMat2
      );

      mat2.Should()
        .BeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = 2,
            JobUnique = "ttt",
            PartName = "zzz",
            NumProcesses = 202,
            Serial = "asdf",
            Workorder = "www"
          }
        );

      newLogEvtsForMat2
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new LogEntry(
              cntr: 2,
              mat: new[]
              {
                new LogMaterial(
                  matID: 2,
                  uniq: "ttt",
                  proc: 0,
                  part: "zzz",
                  numProc: 202,
                  serial: "asdf",
                  workorder: "", // NOTE: workorder not filled in yet because serial recorded first
                  face: ""
                )
              },
              pal: 0,
              ty: LogType.PartMark,
              locName: "Mark",
              locNum: 1,
              prog: "MARK",
              start: false,
              endTime: now.AddSeconds(1),
              result: "asdf"
            ),
            new LogEntry(
              cntr: 3,
              mat: new[]
              {
                new LogMaterial(
                  matID: 2,
                  uniq: "ttt",
                  proc: 0,
                  part: "zzz",
                  numProc: 202,
                  serial: "asdf",
                  workorder: "www",
                  face: ""
                )
              },
              pal: 0,
              ty: LogType.OrderAssignment,
              locName: "Order",
              locNum: 1,
              prog: "",
              start: false,
              endTime: now.AddSeconds(1),
              result: "www"
            )
          }
        );

      _jobLog
        .GetLogForSerial("asdf")
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new LogEntry(
              cntr: 2,
              mat: new[]
              {
                new LogMaterial(
                  matID: 2,
                  uniq: "ttt",
                  proc: 0,
                  part: "zzz",
                  numProc: 202,
                  serial: "asdf",
                  workorder: "www",
                  face: ""
                )
              },
              pal: 0,
              ty: LogType.PartMark,
              locName: "Mark",
              locNum: 1,
              prog: "MARK",
              start: false,
              endTime: now.AddSeconds(1),
              result: "asdf"
            ),
            new LogEntry(
              cntr: 3,
              mat: new[]
              {
                new LogMaterial(
                  matID: 2,
                  uniq: "ttt",
                  proc: 0,
                  part: "zzz",
                  numProc: 202,
                  serial: "asdf",
                  workorder: "www",
                  face: ""
                )
              },
              pal: 0,
              ty: LogType.OrderAssignment,
              locName: "Order",
              locNum: 1,
              prog: "",
              start: false,
              endTime: now.AddSeconds(1),
              result: "www"
            )
          }
        );
    }

    [Fact]
    public void PendingLoadOneSerialPerMat()
    {
      _jobLog.PendingLoads(1).Should().BeEmpty();
      _jobLog.AllPendingLoads().Should().BeEmpty();

      var mat1 = new LogMaterial(
        _jobLog.AllocateMaterialID("unique", "part1", 1),
        "unique",
        1,
        "part1",
        1,
        "0000000001",
        "",
        "face1"
      );
      var mat2 = new LogMaterial(
        _jobLog.AllocateMaterialID("unique2", "part2", 2),
        "unique2",
        1,
        "part2",
        2,
        "0000000002",
        "",
        "face1"
      );
      var mat3 = new LogMaterial(
        _jobLog.AllocateMaterialID("unique3", "part3", 3),
        "unique3",
        3,
        "part3",
        3,
        "0000000003",
        "",
        "face3"
      );
      var mat4 = new LogMaterial(
        _jobLog.AllocateMaterialID("unique4", "part4", 4),
        "unique4",
        3,
        "part4",
        4,
        "themat4serial",
        "",
        "face3"
      );
      var mat5 = new LogMaterial(
        _jobLog.AllocateMaterialID("unique5", "part5", 5),
        "unique5",
        5,
        "part5",
        5,
        "0000000005",
        "",
        "555"
      );

      var serial1 = SerialSettings.ConvertToBase62(mat1.MaterialID).PadLeft(10, '0');
      var serial2 = SerialSettings.ConvertToBase62(mat2.MaterialID).PadLeft(10, '0');
      var serial3 = SerialSettings.ConvertToBase62(mat3.MaterialID).PadLeft(10, '0');
      var serial5 = SerialSettings.ConvertToBase62(mat5.MaterialID).PadLeft(10, '0');

      var t = DateTime.UtcNow.AddHours(-1);

      //mat4 already has a serial
      _jobLog.RecordSerialForMaterialID(EventLogMaterial.FromLogMat(mat4), "themat4serial", t.AddMinutes(1));
      var ser4 = new LogEntry(
        0,
        new LogMaterial[] { mat4 },
        0,
        LogType.PartMark,
        "Mark",
        1,
        "MARK",
        false,
        t.AddMinutes(1),
        "themat4serial"
      );

      var log1 = new LogEntry(
        0,
        new LogMaterial[] { mat1, mat2 },
        1,
        LogType.GeneralMessage,
        "ABC",
        1,
        "prog1",
        false,
        t,
        "result1",
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(12)
      );
      var log2 = new LogEntry(
        0,
        new LogMaterial[] { mat1, mat2 },
        2,
        LogType.MachineCycle,
        "MC",
        1,
        "prog2",
        false,
        t.AddMinutes(20),
        "result2",
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(17)
      );

      ((Repository)_jobLog).AddLogEntryFromUnitTest(log1);
      ((Repository)_jobLog).AddLogEntryFromUnitTest(log2);

      _jobLog.AddPendingLoad(1, "key1", 5, TimeSpan.FromMinutes(32), TimeSpan.FromMinutes(38), "for1");
      _jobLog.AddPendingLoad(1, "key2", 7, TimeSpan.FromMinutes(44), TimeSpan.FromMinutes(49), "for2");

      _jobLog
        .PendingLoads(1)
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new PendingLoad()
            {
              Pallet = 1,
              Key = "key1",
              LoadStation = 5,
              Elapsed = TimeSpan.FromMinutes(32),
              ForeignID = "for1",
              ActiveOperationTime = TimeSpan.FromMinutes(38)
            },
            new PendingLoad()
            {
              Pallet = 1,
              Key = "key2",
              LoadStation = 7,
              Elapsed = TimeSpan.FromMinutes(44),
              ForeignID = "for2",
              ActiveOperationTime = TimeSpan.FromMinutes(49)
            }
          }
        );
      _jobLog
        .AllPendingLoads()
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new PendingLoad()
            {
              Pallet = 1,
              Key = "key1",
              LoadStation = 5,
              Elapsed = TimeSpan.FromMinutes(32),
              ForeignID = "for1",
              ActiveOperationTime = TimeSpan.FromMinutes(38)
            },
            new PendingLoad()
            {
              Pallet = 1,
              Key = "key2",
              LoadStation = 7,
              Elapsed = TimeSpan.FromMinutes(44),
              ForeignID = "for2",
              ActiveOperationTime = TimeSpan.FromMinutes(49)
            }
          }
        );

      _jobLog.AddPendingLoad(
        1,
        "key3",
        7,
        TimeSpan.FromMinutes(244),
        TimeSpan.FromMinutes(249),
        "extraforID"
      );

      _jobLog
        .AllPendingLoads()
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new PendingLoad()
            {
              Pallet = 1,
              Key = "key1",
              LoadStation = 5,
              Elapsed = TimeSpan.FromMinutes(32),
              ForeignID = "for1",
              ActiveOperationTime = TimeSpan.FromMinutes(38)
            },
            new PendingLoad()
            {
              Pallet = 1,
              Key = "key2",
              LoadStation = 7,
              Elapsed = TimeSpan.FromMinutes(44),
              ForeignID = "for2",
              ActiveOperationTime = TimeSpan.FromMinutes(49)
            },
            new PendingLoad()
            {
              Pallet = 1,
              Key = "key3",
              LoadStation = 7,
              Elapsed = TimeSpan.FromMinutes(244),
              ForeignID = "extraforID",
              ActiveOperationTime = TimeSpan.FromMinutes(249)
            }
          }
        );

      _jobLog.CancelPendingLoads("extraforID");

      _jobLog
        .AllPendingLoads()
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new PendingLoad()
            {
              Pallet = 1,
              Key = "key1",
              LoadStation = 5,
              Elapsed = TimeSpan.FromMinutes(32),
              ForeignID = "for1",
              ActiveOperationTime = TimeSpan.FromMinutes(38)
            },
            new PendingLoad()
            {
              Pallet = 1,
              Key = "key2",
              LoadStation = 7,
              Elapsed = TimeSpan.FromMinutes(44),
              ForeignID = "for2",
              ActiveOperationTime = TimeSpan.FromMinutes(49)
            }
          }
        );

      var mat = new Dictionary<string, IEnumerable<EventLogMaterial>>();

      var palCycle = new LogEntry(
        0,
        new LogMaterial[] { },
        1,
        LogType.PalletCycle,
        "Pallet Cycle",
        1,
        "",
        false,
        t.AddMinutes(45),
        "PalletCycle",
        TimeSpan.Zero,
        TimeSpan.Zero
      );

      mat["key1"] = new LogMaterial[] { mat1, mat2 }.Select(EventLogMaterial.FromLogMat);

      var nLoad1 = new LogEntry(
        0,
        new LogMaterial[] { mat1, mat2 },
        1,
        LogType.LoadUnloadCycle,
        "L/U",
        5,
        "LOAD",
        false,
        t.AddMinutes(45).AddSeconds(1),
        "LOAD",
        TimeSpan.FromMinutes(32),
        TimeSpan.FromMinutes(38)
      );

      var ser1 = new LogEntry(
        0,
        new LogMaterial[] { mat1 },
        0,
        LogType.PartMark,
        "Mark",
        1,
        "MARK",
        false,
        t.AddMinutes(45).AddSeconds(2),
        serial1
      );

      var ser2 = new LogEntry(
        0,
        new LogMaterial[] { mat2 },
        0,
        LogType.PartMark,
        "Mark",
        1,
        "MARK",
        false,
        t.AddMinutes(45).AddSeconds(2),
        serial2
      );

      mat["key2"] = new LogMaterial[] { mat3, mat4 }.Select(EventLogMaterial.FromLogMat);

      var nLoad2 = new LogEntry(
        0,
        new LogMaterial[] { mat3, mat4 },
        1,
        LogType.LoadUnloadCycle,
        "L/U",
        7,
        "LOAD",
        false,
        t.AddMinutes(45).AddSeconds(1),
        "LOAD",
        TimeSpan.FromMinutes(44),
        TimeSpan.FromMinutes(49)
      );

      var ser3 = new LogEntry(
        0,
        new LogMaterial[] { mat3 },
        0,
        LogType.PartMark,
        "Mark",
        1,
        "MARK",
        false,
        t.AddMinutes(45).AddSeconds(2),
        serial3
      );

      _jobLog.CompletePalletCycle(
        pal: 1,
        timeUTC: t.AddMinutes(45),
        foreignID: "for3",
        matFromPendingLoads: mat,
        additionalLoads: ImmutableList.Create(
          new MaterialToLoadOntoPallet()
          {
            LoadStation = 16,
            Elapsed = TimeSpan.FromMinutes(55),
            Faces = ImmutableList.Create(
              new MaterialToLoadOntoFace()
              {
                FaceNum = 555,
                Process = mat5.Process,
                Path = 88,
                ActiveOperationTime = TimeSpan.FromMinutes(555),
                MaterialIDs = ImmutableList.Create(mat5.MaterialID)
              }
            )
          }
        ),
        generateSerials: true
      );

      var ser5 = new LogEntry(
        0,
        new[] { mat5 },
        0,
        LogType.PartMark,
        "Mark",
        1,
        "MARK",
        false,
        t.AddMinutes(45).AddSeconds(2),
        serial5
      );

      var nLoad5 = new LogEntry(
        0,
        new[] { mat5 },
        1,
        LogType.LoadUnloadCycle,
        "L/U",
        16,
        "LOAD",
        false,
        t.AddMinutes(45).AddSeconds(1),
        "LOAD",
        TimeSpan.FromMinutes(55),
        TimeSpan.FromMinutes(555)
      );

      JobLogTest.CheckLog(
        new LogEntry[] { ser4, log1, log2, palCycle, nLoad1, nLoad2, ser1, ser2, ser3, ser5, nLoad5 },
        _jobLog.GetLogEntries(t.AddMinutes(-10), t.AddHours(1)).ToList(),
        t.AddMinutes(-10)
      );

      _jobLog
        .GetMaterialDetails(mat5.MaterialID)
        .Paths.Should()
        .BeEquivalentTo(new Dictionary<int, int>() { { mat5.Process, 88 } });

      JobLogTest.CheckEqual(palCycle, _jobLog.StationLogByForeignID("for3")[0]);
      _jobLog.MaxForeignID().Should().Be("for3");

      _jobLog.PendingLoads(1).Should().BeEmpty();
    }

    [Fact]
    public void ForeignID()
    {
      var t = DateTime.UtcNow.AddHours(-3);

      var newMat = _jobLog.AllocateMaterialIDAndGenerateSerial(
        unique: "unique3",
        part: "part3",
        numProc: 4,
        timeUTC: t.AddMinutes(3),
        out var serialLogEntry,
        foreignID: "for2"
      );

      var expected = new LogEntry()
      {
        Counter = -1,
        Material = ImmutableList.Create(
          new LogMaterial()
          {
            MaterialID = newMat,
            JobUniqueStr = "unique3",
            Process = 0,
            PartName = "part3",
            NumProcesses = 4,
            Serial = "0000000001",
            Workorder = "",
            Face = ""
          }
        ),
        Pallet = 0,
        LogType = LogType.PartMark,
        Program = "MARK",
        LocationName = "Mark",
        LocationNum = 1,
        StartOfCycle = false,
        EndTimeUTC = t.AddMinutes(3),
        Result = "0000000001",
        ElapsedTime = TimeSpan.FromMinutes(-1),
        ActiveOperationTime = TimeSpan.Zero,
      };

      serialLogEntry.Should().BeEquivalentTo(expected, options => options.Excluding(e => e.Counter));

      _jobLog
        .MostRecentLogEntryForForeignID("for2")
        .Should()
        .BeEquivalentTo(expected, options => options.Excluding(e => e.Counter));
    }
  }

  public class LogOneSerialPerCycleSpec : IDisposable
  {
    private RepositoryConfig _repoCfg;
    private IRepository _jobLog;

    public LogOneSerialPerCycleSpec()
    {
      var settings = new SerialSettings()
      {
        SerialType = SerialType.AssignOneSerialPerCycle,
        ConvertMaterialIDToSerial = (m) => SerialSettings.ConvertToBase62(m, 10)
      };
      _repoCfg = RepositoryConfig.InitializeSingleThreadedMemoryDB(settings);
      _jobLog = _repoCfg.OpenConnection();
    }

    public void Dispose()
    {
      _repoCfg.CloseMemoryConnection();
    }

    [Fact]
    public void PendingLoadOneSerialPerCycle()
    {
      var mat1 = new LogMaterial(
        _jobLog.AllocateMaterialID("unique", "part1", 1),
        "unique",
        1,
        "part1",
        1,
        "0000000001",
        "",
        "face1"
      );
      var mat2 = new LogMaterial(
        _jobLog.AllocateMaterialID("unique2", "part2", 2),
        "unique2",
        1,
        "part2",
        2,
        "0000000001",
        "",
        "face1"
      ); // note mat2 gets same serial as mat1
      var mat3 = new LogMaterial(
        _jobLog.AllocateMaterialID("unique3", "part3", 3),
        "unique3",
        3,
        "part3",
        3,
        "0000000003",
        "",
        "face3"
      );
      var mat4 = new LogMaterial(
        _jobLog.AllocateMaterialID("unique4", "part4", 4),
        "unique4",
        4,
        "part4",
        4,
        "themat4serial",
        "",
        "face4"
      );

      var serial1 = SerialSettings.ConvertToBase62(mat1.MaterialID).PadLeft(10, '0');
      var serial3 = SerialSettings.ConvertToBase62(mat3.MaterialID).PadLeft(10, '0');

      var t = DateTime.UtcNow.AddHours(-1);

      //mat4 already has a serial
      _jobLog.RecordSerialForMaterialID(EventLogMaterial.FromLogMat(mat4), "themat4serial", t.AddMinutes(1));
      var ser4 = new LogEntry(
        0,
        new LogMaterial[] { mat4 },
        0,
        LogType.PartMark,
        "Mark",
        1,
        "MARK",
        false,
        t.AddMinutes(1),
        "themat4serial"
      );

      var log1 = new LogEntry(
        0,
        new LogMaterial[] { mat1, mat2 },
        1,
        LogType.GeneralMessage,
        "ABC",
        1,
        "prog1",
        false,
        t,
        "result1",
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(11)
      );
      var log2 = new LogEntry(
        0,
        new LogMaterial[] { mat1, mat2 },
        2,
        LogType.MachineCycle,
        "MC",
        1,
        "prog2",
        false,
        t.AddMinutes(20),
        "result2",
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(22)
      );

      ((Repository)_jobLog).AddLogEntryFromUnitTest(log1);
      ((Repository)_jobLog).AddLogEntryFromUnitTest(log2);

      _jobLog.AddPendingLoad(1, "key1", 5, TimeSpan.FromMinutes(32), TimeSpan.FromMinutes(38), "for1");
      _jobLog.AddPendingLoad(1, "key2", 7, TimeSpan.FromMinutes(44), TimeSpan.FromMinutes(49), "for2");
      _jobLog.AddPendingLoad(1, "key3", 6, TimeSpan.FromMinutes(55), TimeSpan.FromMinutes(61), "for2.5");

      var mat = new Dictionary<string, IEnumerable<EventLogMaterial>>();

      var palCycle = new LogEntry(
        0,
        new LogMaterial[] { },
        1,
        LogType.PalletCycle,
        "Pallet Cycle",
        1,
        "",
        false,
        t.AddMinutes(45),
        "PalletCycle",
        TimeSpan.Zero,
        TimeSpan.Zero
      );

      mat["key1"] = new LogMaterial[] { mat1, mat2 }.Select(EventLogMaterial.FromLogMat);

      var nLoad1 = new LogEntry(
        0,
        new LogMaterial[] { mat1, mat2 },
        1,
        LogType.LoadUnloadCycle,
        "L/U",
        5,
        "LOAD",
        false,
        t.AddMinutes(45).AddSeconds(1),
        "LOAD",
        TimeSpan.FromMinutes(32),
        TimeSpan.FromMinutes(38)
      );

      var ser1 = new LogEntry(
        0,
        new LogMaterial[] { mat1, mat2 },
        0,
        LogType.PartMark,
        "Mark",
        1,
        "MARK",
        false,
        t.AddMinutes(45).AddSeconds(2),
        serial1
      );

      mat["key2"] = new LogMaterial[] { mat3 }.Select(EventLogMaterial.FromLogMat);

      var nLoad2 = new LogEntry(
        0,
        new LogMaterial[] { mat3 },
        1,
        LogType.LoadUnloadCycle,
        "L/U",
        7,
        "LOAD",
        false,
        t.AddMinutes(45).AddSeconds(1),
        "LOAD",
        TimeSpan.FromMinutes(44),
        TimeSpan.FromMinutes(49)
      );

      var ser3 = new LogEntry(
        0,
        new LogMaterial[] { mat3 },
        0,
        LogType.PartMark,
        "Mark",
        1,
        "MARK",
        false,
        t.AddMinutes(45).AddSeconds(2),
        serial3
      );

      mat["key3"] = new LogMaterial[] { mat4 }.Select(EventLogMaterial.FromLogMat);

      var nLoad3 = new LogEntry(
        0,
        new LogMaterial[] { mat4 },
        1,
        LogType.LoadUnloadCycle,
        "L/U",
        6,
        "LOAD",
        false,
        t.AddMinutes(45).AddSeconds(1),
        "LOAD",
        TimeSpan.FromMinutes(55),
        TimeSpan.FromMinutes(61)
      );

      _jobLog.CompletePalletCycle(
        pal: 1,
        timeUTC: t.AddMinutes(45),
        foreignID: "for3",
        matFromPendingLoads: mat,
        additionalLoads: null,
        generateSerials: true
      );

      JobLogTest.CheckLog(
        new LogEntry[] { ser4, log1, log2, palCycle, nLoad1, nLoad2, nLoad3, ser1, ser3 },
        _jobLog.GetLogEntries(t.AddMinutes(-10), t.AddHours(1)).ToList(),
        t.AddMinutes(-10)
      );

      JobLogTest.CheckEqual(palCycle, _jobLog.StationLogByForeignID("for3")[0]);
      _jobLog.MaxForeignID().Should().Be("for3");

      _jobLog.PendingLoads(1).Should().BeEmpty();
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
      SerialSettings.ConvertFromBase62(SerialSettings.ConvertToBase62(matId)).Should().Be(matId);
    }

    [Fact]
    public void MaterialIDs()
    {
      var logDB = RepositoryConfig
        .InitializeSingleThreadedMemoryDB(
          new SerialSettings()
          {
            StartingMaterialID = SerialSettings.ConvertFromBase62("AbCd12"),
            ConvertMaterialIDToSerial = m => SerialSettings.ConvertToBase62(m)
          },
          _connection,
          createTables: true
        )
        .OpenConnection();
      long m1 = logDB.AllocateMaterialID("U1", "P1", 52);
      long m2 = logDB.AllocateMaterialID("U2", "P2", 66);
      long m3 = logDB.AllocateMaterialID("U3", "P3", 566);
      m1.Should().Be(33_152_428_148);
      m2.Should().Be(33_152_428_149);
      m3.Should().Be(33_152_428_150);

      logDB
        .GetMaterialDetails(m1)
        .Should()
        .BeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = m1,
            JobUnique = "U1",
            PartName = "P1",
            NumProcesses = 52,
          }
        );
    }

    [Fact]
    public void ErrorsTooLarge()
    {
      Action act = () =>
        RepositoryConfig.InitializeSingleThreadedMemoryDB(
          new SerialSettings()
          {
            StartingMaterialID = SerialSettings.ConvertFromBase62("A000000000"),
            ConvertMaterialIDToSerial = m => SerialSettings.ConvertToBase62(m)
          }
        );
      act.Should().Throw<Exception>().WithMessage("Starting Serial is too large");
    }

    [Fact]
    public void AdjustsStartingSerial()
    {
      var logFromCreate = RepositoryConfig
        .InitializeSingleThreadedMemoryDB(
          new SerialSettings()
          {
            StartingMaterialID = SerialSettings.ConvertFromBase62("AbCd12"),
            ConvertMaterialIDToSerial = m => SerialSettings.ConvertToBase62(m)
          },
          _connection,
          createTables: true
        )
        .OpenConnection();

      long m1 = logFromCreate.AllocateMaterialID("U1", "P1", 52);
      m1.Should().Be(33_152_428_148);

      var logFromUpgrade = RepositoryConfig
        .InitializeSingleThreadedMemoryDB(
          new SerialSettings()
          {
            StartingMaterialID = SerialSettings.ConvertFromBase62("B3t24s"),
            ConvertMaterialIDToSerial = m => SerialSettings.ConvertToBase62(m)
          },
          _connection,
          createTables: false
        )
        .OpenConnection();

      long m2 = logFromUpgrade.AllocateMaterialID("U1", "P1", 2);
      long m3 = logFromUpgrade.AllocateMaterialID("U2", "P2", 4);
      m2.Should().Be(33_948_163_268);
      m3.Should().Be(33_948_163_269);
    }

    [Fact]
    public void AvoidsAdjustingSerialBackwards()
    {
      var logFromCreate = RepositoryConfig
        .InitializeSingleThreadedMemoryDB(
          new SerialSettings()
          {
            StartingMaterialID = SerialSettings.ConvertFromBase62("AbCd12"),
            ConvertMaterialIDToSerial = m => SerialSettings.ConvertToBase62(m)
          },
          _connection,
          createTables: true
        )
        .OpenConnection();

      long m1 = logFromCreate.AllocateMaterialID("U1", "P1", 52);
      m1.Should().Be(33_152_428_148);

      var logFromUpgrade = RepositoryConfig
        .InitializeSingleThreadedMemoryDB(
          new SerialSettings()
          {
            StartingMaterialID = SerialSettings.ConvertFromBase62("w53122"),
            ConvertMaterialIDToSerial = m => SerialSettings.ConvertToBase62(m)
          },
          _connection,
          createTables: false
        )
        .OpenConnection();

      long m2 = logFromUpgrade.AllocateMaterialID("U1", "P1", 2);
      m2.Should().Be(33_152_428_149);
    }
  }
}
