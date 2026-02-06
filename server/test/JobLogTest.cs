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
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using AutoFixture;
using BlackMaple.MachineFramework;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace BlackMaple.FMSInsight.Tests
{
  public static class MkLogMat
  {
    public static LogMaterial Mk(
      long matID,
      string uniq,
      int proc,
      string part,
      int numProc,
      string serial,
      string workorder,
      string face
    )
    {
      // A remnant from before required properties and record intializers
      // All new code should just directly intialize LogMaterial as a record
      return new LogMaterial()
      {
        MaterialID = matID,
        JobUniqueStr = uniq,
        PartName = part,
        Process = proc,
        Path = null,
        NumProcesses = numProc,
        Face = face == "" ? 0 : int.Parse(face),
        Serial = serial,
        Workorder = workorder,
      };
    }
  }

  public class JobLogTest : IDisposable
  {
    public static readonly ProcPathInfo EmptyPath = new ProcPathInfo()
    {
      PalletNums = [],
      Load = [],
      Unload = [],
      ExpectedLoadTime = TimeSpan.Zero,
      ExpectedUnloadTime = TimeSpan.Zero,
      Stops = ImmutableList<MachiningStop>.Empty,
      SimulatedStartingUTC = DateTime.MinValue,
      SimulatedAverageFlowTime = TimeSpan.Zero,
      PartsPerPallet = 1,
    };

    private RepositoryConfig _repoCfg;
    private Fixture _fixture;

    public JobLogTest()
    {
      _repoCfg = RepositoryConfig.InitializeMemoryDB(null);
      _fixture = new Fixture();
      _fixture.Customizations.Add(new ImmutableSpecimenBuilder());
      _fixture.Customizations.Add(new InjectNullValuesForNullableTypesSpecimenBuilder());
      _fixture.Customizations.Add(new DateOnlySpecimenBuilder());
    }

    void IDisposable.Dispose()
    {
      _repoCfg.Dispose();
    }

    [Test]
    public void MaterialIDs()
    {
      using var _jobLog = _repoCfg.OpenConnection();
      long m1 = _jobLog.AllocateMaterialID("U1", "P1", 52);
      long m2 = _jobLog.AllocateMaterialIDAndGenerateSerial(
        "U2",
        "P2",
        66,
        DateTime.UtcNow,
        out var serialLogEntry
      );
      // no serial is actually generated since the setting is disabled
      serialLogEntry.ShouldBeNull();
      long m3 = _jobLog.AllocateMaterialID("U3", "P3", 566);
      m1.ShouldBe(1);
      m2.ShouldBe(2);
      m3.ShouldBe(3);

      _jobLog.RecordPathForProcess(m1, 1, 60);
      _jobLog.RecordPathForProcess(m1, 2, 88);
      _jobLog.RecordPathForProcess(m2, 6, 5);
      _jobLog.RecordPathForProcess(m2, 6, 10);

      _jobLog
        .GetMaterialDetails(m1)
        .ShouldBeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = m1,
            JobUnique = "U1",
            PartName = "P1",
            NumProcesses = 52,
            Paths = ImmutableDictionary<int, int>.Empty.Add(1, 60).Add(2, 88),
          }
        );

      _jobLog
        .GetMaterialDetails(m2)
        .ShouldBeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = m2,
            JobUnique = "U2",
            PartName = "P2",
            NumProcesses = 66,
            Paths = ImmutableDictionary<int, int>.Empty.Add(6, 10),
          }
        );

      _jobLog
        .GetMaterialDetails(m3)
        .ShouldBeEquivalentTo(
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
        .ShouldBeEquivalentTo(
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
        .ShouldBeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = m4,
            JobUnique = "U4",
            PartName = "P4444",
            NumProcesses = 77,
          }
        );

      _jobLog.GetWorkordersForUnique("U1").ShouldBeEmpty();

      _jobLog.RecordWorkorderForMaterialID(m1, 1, "work1");
      _jobLog.RecordWorkorderForMaterialID(m2, 1, "work2");
      _jobLog.RecordWorkorderForMaterialID(m3, 1, "work1");

      _jobLog
        .GetMaterialForWorkorder("work1")
        .ShouldBeEquivalentTo(
          new List<MaterialDetails>
          {
            new MaterialDetails
            {
              MaterialID = m1,
              JobUnique = "U1",
              PartName = "P1",
              NumProcesses = 52,
              Workorder = "work1",
              Paths = ImmutableDictionary<int, int>.Empty.Add(1, 60).Add(2, 88),
            },
            new MaterialDetails()
            {
              MaterialID = m3,
              JobUnique = "U3",
              PartName = "P3",
              NumProcesses = 566,
              Workorder = "work1",
            },
          }
        );
      _jobLog.CountMaterialForWorkorder("work1").ShouldBe(2);
      _jobLog.CountMaterialForWorkorder("work1", "P1").ShouldBe(1);
      _jobLog.CountMaterialForWorkorder("work1", "P2").ShouldBe(0);
      _jobLog.CountMaterialForWorkorder("work1", "P3").ShouldBe(1);
      _jobLog.CountMaterialForWorkorder("unused").ShouldBe(0);

      _jobLog.GetWorkordersForUnique("U1").ShouldBeEquivalentTo(ImmutableSortedSet.Create("work1"));
      _jobLog.GetWorkordersForUnique("unused").ShouldBeEmpty();

      _jobLog
        .GetMaterialForJobUnique("U1")
        .ShouldBeEquivalentTo(
          new List<MaterialDetails>
          {
            new MaterialDetails
            {
              MaterialID = m1,
              JobUnique = "U1",
              PartName = "P1",
              NumProcesses = 52,
              Workorder = "work1",
              Paths = ImmutableDictionary<int, int>.Empty.Add(1, 60).Add(2, 88),
            },
          }
        );

      _jobLog.GetMaterialForJobUnique("unused").ShouldBeEmpty();
      _jobLog.CountMaterialForJobUnique("U1").ShouldBe(1);
      _jobLog.CountMaterialForJobUnique("unused").ShouldBe(0);

      _jobLog.CreateMaterialID(matID: 555, unique: "55555", part: "part5", numProc: 12);
      _jobLog
        .GetMaterialDetails(555)
        .ShouldBeEquivalentTo(
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
        .ShouldBeEquivalentTo(
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
        .ShouldBeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = 666,
            JobUnique = null,
            PartName = "abc",
            NumProcesses = 32,
          }
        );
    }

    [Test]
    public void AddLog()
    {
      using var _jobLog = _repoCfg.OpenConnection();
      _jobLog.MaxLogDate().ShouldBe(DateTime.MinValue);

      System.DateTime start = DateTime.UtcNow.AddHours(-10);

      List<LogEntry> logs = new List<LogEntry>();
      var logsForMat1 = new List<LogEntry>();
      var logsForMat2 = new List<LogEntry>();

      LogMaterial mat1 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("grgaegr", "pp2", 23),
        "grgaegr",
        7,
        "pp2",
        23,
        "",
        "",
        "22"
      );
      LogMaterial mat19 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("unique", "pp1", 53),
        "unique",
        2,
        "pp1",
        53,
        "",
        "",
        "55"
      );
      LogMaterial mat20 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("unique", "pp1", 53),
        "unique",
        1,
        "pp1",
        53,
        "",
        "",
        "22"
      );

      var loadStartActualCycle = _jobLog.RecordLoadStart(
        mats: new[] { mat1, mat19 }.Select(EventLogMaterial.FromLogMat),
        pallet: 55,
        lulNum: 2,
        timeUTC: start.AddHours(1)
      );
      loadStartActualCycle.ShouldBeEquivalentTo(
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
        )
      );
      logs.Add(loadStartActualCycle);
      logsForMat1.Add(loadStartActualCycle);

      var mat2 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("ahre", "gewoiweg", 13),
        "ahre",
        1,
        "gewoiweg",
        13,
        "",
        "",
        "22"
      );
      var mat15 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("qghr4e", "ppp532", 14),
        "qghr4e",
        1,
        "ppp532",
        14,
        "",
        "",
        "22"
      );
      var matLoc2Face1 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("loc2", "face1", 14),
        "loc2",
        3,
        "face1",
        14,
        "",
        "",
        "1"
      );
      var matLoc2Face2 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("loc2", "face2", 14),
        "loc2",
        4,
        "face2",
        14,
        "",
        "",
        "2"
      );

      var mat300 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("loc2", "face2", 14),
        "loc2",
        4,
        "face2",
        14,
        "",
        "",
        "2"
      );

      var mat400 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("loc2", "face2", 14),
        "loc2",
        4,
        "face2",
        14,
        "",
        "",
        "2"
      );

      var loadEndActualCycle = _jobLog.RecordLoadUnloadComplete(
        toLoad: new[]
        {
          new MaterialToLoadOntoFace()
          {
            FaceNum = 22,
            Process = mat2.Process,
            Path = null,
            MaterialIDs = [mat2.MaterialID, mat15.MaterialID],
            ActiveOperationTime = TimeSpan.FromMinutes(111),
          },
          new MaterialToLoadOntoFace()
          {
            FaceNum = matLoc2Face1.Face,
            Process = matLoc2Face1.Process,
            Path = 33,
            MaterialIDs = [matLoc2Face1.MaterialID],
            ActiveOperationTime = TimeSpan.FromMinutes(222),
          },
          new MaterialToLoadOntoFace()
          {
            FaceNum = matLoc2Face2.Face,
            Process = matLoc2Face2.Process,
            Path = 44,
            MaterialIDs = [matLoc2Face2.MaterialID],
            ActiveOperationTime = TimeSpan.FromMinutes(333),
          },
        },
        previouslyLoaded: [EventLogMaterial.FromLogMat(mat300)],
        toUnload:
        [
          new MaterialToUnloadFromFace()
          {
            MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>
              .Empty.Add(mat20.MaterialID, null)
              .Add(mat19.MaterialID, null),
            FaceNum = mat20.Face,
            Process = mat20.Process,
            ActiveOperationTime = TimeSpan.FromMinutes(123),
          },
          new MaterialToUnloadFromFace()
          {
            MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(
              mat1.MaterialID,
              null
            ),
            FaceNum = mat1.Face,
            Process = mat1.Process,
            ActiveOperationTime = TimeSpan.FromMinutes(211),
          },
        ],
        previouslyUnloaded: [EventLogMaterial.FromLogMat(mat400)],
        lulNum: 111,
        totalElapsed: TimeSpan.FromMinutes(40000),
        pallet: 1234,
        timeUTC: start.AddHours(3),
        externalQueues: null
      );
      loadEndActualCycle.EventsShouldBe(
        new[]
        {
          new LogEntry(
            -1,
            new LogMaterial[] { mat19 with { Face = 22, Process = 1 }, mat20 },
            1234,
            LogType.LoadUnloadCycle,
            "L/U",
            111,
            "UNLOAD",
            false,
            start.AddHours(3),
            "UNLOAD",
            TimeSpan.FromMinutes(123 * 40000.0 / (111 + 222 + 333 + 123 + 211)),
            TimeSpan.FromMinutes(123)
          ),
          new LogEntry(
            -1,
            new LogMaterial[] { mat1 },
            1234,
            LogType.LoadUnloadCycle,
            "L/U",
            111,
            "UNLOAD",
            false,
            start.AddHours(3),
            "UNLOAD",
            TimeSpan.FromMinutes(211 * 40000.0 / (111 + 222 + 333 + 123 + 211)),
            TimeSpan.FromMinutes(211)
          ),
          new LogEntry()
          {
            Counter = -1,
            Material = [mat20, mat19 with { Face = 22, Process = 1 }, mat1, mat400],
            Pallet = 1234,
            LogType = LogType.PalletCycle,
            LocationName = "Pallet Cycle",
            LocationNum = 1,
            Program = "",
            StartOfCycle = false,
            EndTimeUTC = start.AddHours(3),
            Result = "PalletCycle",
            ElapsedTime = TimeSpan.Zero,
            ActiveOperationTime = TimeSpan.Zero,
          },
          new LogEntry()
          {
            Counter = -1,
            Material =
            [
              mat2,
              mat15,
              matLoc2Face1 with
              {
                Path = 33,
              },
              matLoc2Face2 with
              {
                Path = 44,
              },
              mat300,
            ],
            Pallet = 1234,
            LogType = LogType.PalletCycle,
            LocationName = "Pallet Cycle",
            LocationNum = 1,
            Program = "",
            StartOfCycle = true,
            EndTimeUTC = start.AddHours(3),
            Result = "PalletCycle",
            ElapsedTime = TimeSpan.Zero,
            ActiveOperationTime = TimeSpan.Zero,
          },
          new LogEntry(
            -1,
            new LogMaterial[] { mat2, mat15 },
            1234,
            LogType.LoadUnloadCycle,
            "L/U",
            111,
            "LOAD",
            false,
            start.AddHours(3).AddSeconds(1),
            "LOAD",
            TimeSpan.FromMinutes(111 * 40000.0 / (111 + 222 + 333 + 123 + 211)),
            TimeSpan.FromMinutes(111)
          ),
          new LogEntry(
            -1,
            new LogMaterial[] { matLoc2Face1 with { Path = 33 } },
            1234,
            LogType.LoadUnloadCycle,
            "L/U",
            111,
            "LOAD",
            false,
            start.AddHours(3).AddSeconds(1),
            "LOAD",
            TimeSpan.FromMinutes(222 * 40000.0 / (111 + 222 + 333 + 123 + 211)),
            TimeSpan.FromMinutes(222)
          ),
          new LogEntry(
            -1,
            new LogMaterial[] { matLoc2Face2 with { Path = 44 } },
            1234,
            LogType.LoadUnloadCycle,
            "L/U",
            111,
            "LOAD",
            false,
            start.AddHours(3).AddSeconds(1),
            "LOAD",
            TimeSpan.FromMinutes(333 * 40000.0 / (111 + 222 + 333 + 123 + 211)),
            TimeSpan.FromMinutes(333)
          ),
        }
      );
      logs.AddRange(loadEndActualCycle);
      logsForMat1.Add(loadEndActualCycle.Skip(1).First());
      logsForMat1.AddRange(
        loadEndActualCycle.Where(e => e.LogType == LogType.PalletCycle && !e.StartOfCycle)
      );
      logsForMat2.Add(loadEndActualCycle.Skip(4).First());
      logsForMat2.AddRange(loadEndActualCycle.Where(e => e.LogType == LogType.PalletCycle && e.StartOfCycle));
      _jobLog.ToolPocketSnapshotForCycle(loadEndActualCycle.First().Counter).ShouldBeEmpty();
      _jobLog
        .GetMaterialDetails(matLoc2Face1.MaterialID)
        .Paths.ShouldBeEquivalentTo(ImmutableDictionary<int, int>.Empty.Add(matLoc2Face1.Process, 33));
      _jobLog
        .GetMaterialDetails(matLoc2Face2.MaterialID)
        .Paths.ShouldBeEquivalentTo(ImmutableDictionary<int, int>.Empty.Add(matLoc2Face2.Process, 44));

      var arriveStocker = _jobLog.RecordPalletArriveStocker(
        mats: new[] { mat2 }.Select(EventLogMaterial.FromLogMat),
        pallet: 4455,
        stockerNum: 23,
        waitForMachine: true,
        timeUTC: start.AddHours(4)
      );
      arriveStocker.ShouldBeEquivalentTo(
        new LogEntry(
          arriveStocker.Counter,
          mat: new[] { mat2 },
          pal: 4455,
          ty: LogType.PalletInStocker,
          locName: "Stocker",
          locNum: 23,
          prog: "Arrive",
          start: true,
          endTime: start.AddHours(4),
          result: "WaitForMachine"
        )
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
      departStocker.ShouldBeEquivalentTo(
        new LogEntry(
          departStocker.Counter,
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
        )
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
      arriveInbound.ShouldBeEquivalentTo(
        new LogEntry(
          arriveInbound.Counter,
          mat: new[] { mat15 },
          pal: 6578,
          ty: LogType.PalletOnRotaryInbound,
          locName: "thestat",
          locNum: 77,
          prog: "Arrive",
          start: true,
          endTime: start.AddHours(4).AddMinutes(20),
          result: "Arrive"
        )
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
      departInbound.ShouldBeEquivalentTo(
        new LogEntry(
          departInbound.Counter,
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
        )
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
      machineStartActualCycle.ShouldBeEquivalentTo(
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
        )
      );
      logs.Add(machineStartActualCycle);
      _jobLog
        .ToolPocketSnapshotForCycle(machineStartActualCycle.Counter)
        .OrderBy(e => e.Pocket)
        .ToList()
        .ShouldBeEquivalentTo(machineStartPockets.OrderBy(e => e.Pocket).ToList());

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
        Tools = machineEndUsage.ToImmutableList(),
      };
      machineEndActualCycle.ShouldBeEquivalentTo(machineEndExpectedCycle);
      logs.Add(machineEndActualCycle);
      logsForMat2.Add(machineEndActualCycle);
      // start snapshot deleted
      _jobLog.ToolPocketSnapshotForCycle(machineStartActualCycle.Counter).ShouldBeEmpty();

      var unloadStartActualCycle = _jobLog.RecordUnloadStart(
        mats: new[] { mat15, mat19 }.Select(EventLogMaterial.FromLogMat),
        pallet: 66,
        lulNum: 87,
        timeUTC: start.AddHours(6).AddMinutes(10)
      );
      unloadStartActualCycle.ShouldBeEquivalentTo(
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
        )
      );
      logs.Add(unloadStartActualCycle);

      var unloadEndActualCycle = _jobLog.RecordLoadUnloadComplete(
        toLoad: null,
        toUnload:
        [
          new MaterialToUnloadFromFace()
          {
            MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>
              .Empty.Add(mat2.MaterialID, null)
              .Add(mat20.MaterialID, null),
            FaceNum = mat2.Face,
            Process = mat2.Process,
            ActiveOperationTime = TimeSpan.FromSeconds(55),
          },
        ],
        previouslyLoaded: null,
        previouslyUnloaded: null,
        pallet: 3,
        lulNum: 14,
        timeUTC: start.AddHours(7),
        totalElapsed: TimeSpan.FromMinutes(152),
        externalQueues: null
      );
      unloadEndActualCycle.EventsShouldBe(
        new[]
        {
          new LogEntry(
            -1,
            new LogMaterial[] { mat2, mat20 },
            3,
            LogType.LoadUnloadCycle,
            "L/U",
            14,
            "UNLOAD",
            false,
            start.AddHours(7),
            "UNLOAD",
            TimeSpan.FromMinutes(152),
            TimeSpan.FromSeconds(55)
          ),
          new LogEntry()
          {
            Counter = -1,
            Material = [mat2, mat20],
            Pallet = 3,
            LogType = LogType.PalletCycle,
            LocationName = "Pallet Cycle",
            LocationNum = 1,
            Program = "",
            StartOfCycle = false,
            EndTimeUTC = start.AddHours(7),
            Result = "PalletCycle",
            ElapsedTime = TimeSpan.Zero,
            ActiveOperationTime = TimeSpan.Zero,
          },
        }
      );
      logs.AddRange(unloadEndActualCycle);
      logsForMat2.AddRange(unloadEndActualCycle);

      // ----- check loading of logs -----

      _jobLog.MaxLogDate().ShouldBe(start.AddHours(7));

      _jobLog.GetLogEntries(start, DateTime.UtcNow).EventsShouldBe(logs);

      _jobLog
        .GetLogEntries(start.AddHours(5), DateTime.UtcNow)
        .EventsShouldBe(logs.Where(e => e.EndTimeUTC >= start.AddHours(5)));

      _jobLog
        .GetRecentLog(loadEndActualCycle.Last().Counter)
        .EventsShouldBe(logs.Where(e => e.EndTimeUTC >= start.AddHours(4)));

      _jobLog
        .GetRecentLog(unloadStartActualCycle.Counter, unloadStartActualCycle.EndTimeUTC)
        .EventsShouldBe(logs.Where(e => e.EndTimeUTC >= start.AddHours(6.5)));

      Should
        .Throw<ConflictRequestException>(() =>
          _jobLog.GetRecentLog(unloadStartActualCycle.Counter, new DateTime(2000, 2, 3, 4, 5, 6)).ToList()
        )
        .Message.ShouldBe("Counter " + unloadStartActualCycle.Counter.ToString() + " has different end time");

      _jobLog.GetRecentLog(unloadEndActualCycle.Last().Counter).ShouldBeEmpty();

      foreach (var c in logs)
      {
        _jobLog
          .CycleExists(c.EndTimeUTC, c.Pallet, c.LogType, c.LocationName, c.LocationNum)
          .ShouldBeTrue("Checking " + c.EndTimeUTC.ToString());
      }

      _jobLog.CycleExists(DateTime.Parse("4/6/2011"), 123, LogType.MachineCycle, "MC", 3).ShouldBeFalse();

      _jobLog.GetLogForMaterial(1).EventsShouldBe(logsForMat1);
      _jobLog.GetLogForMaterial(new[] { 1, mat2.MaterialID }).EventsShouldBe(logsForMat1.Concat(logsForMat2));
      _jobLog.GetLogForMaterial(18).ShouldBeEmpty();
      _jobLog.GetLogForMaterial([18, 19]).ShouldBeEmpty();

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
      _jobLog.GetLogForSerial("ser1").EventsShouldBe(logsForMat1);
      _jobLog.GetLogForSerial("ser2").ShouldBeEmpty();

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
      _jobLog.GetLogForWorkorder("work1").EventsShouldBe(logsForMat1.Append(finalize));
      _jobLog.GetLogForWorkorder("work2").ShouldBeEmpty();

      _jobLog.GetLogForJobUnique(mat1.JobUniqueStr).EventsShouldBe(logsForMat1);
      _jobLog.GetLogForJobUnique("sofusadouf").ShouldBeEmpty();

      //inspection, wash, and general
      var inspDecision = _jobLog.StoreInspectionDecision(
        matID: mat2.MaterialID,
        proc: 2,
        insp: new PathInspection()
        {
          InspectionType = "insptype_mat2",
          Counter = "thecounter_mat2",
          MaxVal = 1000,
          RandomFreq = 0,
          TimeInterval = TimeSpan.Zero,
        },
        inspect: true,
        utcNow: start.AddHours(8).AddMinutes(10)
      );
      var expectedInspDecision = new LogEntry(
        inspDecision.Counter,
        [mat2 with { Face = 0, Process = 2 }],
        0,
        LogType.Inspection,
        "Inspect",
        1,
        "thecounter_mat2",
        false,
        inspDecision.EndTimeUTC,
        "True"
      ) with
      {
        ProgramDetails = ImmutableDictionary<string, string>
          .Empty.Add("InspectionType", "insptype_mat2")
          .Add(
            "ActualPath",
            "[{\"MaterialID\":4,\"Process\":1,\"Pallet\":3,\"LoadStation\":111,\"Stops\":[{\"StationName\":\"xxx\",\"StationNum\":177}],\"UnloadStation\":14}]"
          ),
      };
      logsForMat2.Add(expectedInspDecision);
      inspDecision.ShouldBeEquivalentTo(expectedInspDecision);

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
      expectedInspLog = expectedInspLog with
      {
        ProgramDetails = ImmutableDictionary<string, string>.Empty.Add("a", "aaa").Add("b", "bbb"),
      };
      logsForMat1.Add(expectedInspLog);

      var washLog = _jobLog.RecordCloseoutCompleted(
        EventLogMaterial.FromLogMat(mat1),
        7,
        "Closety",
        success: true,
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
      expectedWashLog = expectedWashLog with
      {
        ProgramDetails = ImmutableDictionary<string, string>.Empty.Add("z", "zzz").Add("y", "yyy"),
      };
      logsForMat1.Add(expectedWashLog);

      var failedCloseout = _jobLog.RecordCloseoutCompleted(
        materialID: mat1.MaterialID,
        process: 12,
        locNum: 4,
        closeoutType: "TheType",
        success: false,
        new Dictionary<string, string> { { "a", "aaa" } },
        TimeSpan.FromMinutes(22),
        TimeSpan.FromMinutes(4)
      );
      var expectedFailedCloseout = new LogEntry(
        -1,
        new LogMaterial[] { mat1 with { Face = 0, Process = 12 } },
        0,
        LogType.CloseOut,
        "CloseOut",
        4,
        "TheType",
        false,
        failedCloseout.EndTimeUTC,
        "Failed",
        TimeSpan.FromMinutes(22),
        TimeSpan.FromMinutes(4)
      );
      expectedFailedCloseout = expectedFailedCloseout with
      {
        ProgramDetails = ImmutableDictionary<string, string>.Empty.Add("a", "aaa"),
      };
      logsForMat1.Add(expectedFailedCloseout);

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
      expectedGeneralLog = expectedGeneralLog with
      {
        ProgramDetails = ImmutableDictionary<string, string>.Empty.SetItem("extra1", "value1"),
      };
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
          MkLogMat.Mk(
            mat1.MaterialID,
            mat1.JobUniqueStr,
            mat1.Process,
            mat1.PartName,
            mat1.NumProcesses,
            mat1.Serial,
            mat1.Workorder,
            ""
          ),
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
      expectedNotesLog = expectedNotesLog with
      {
        ProgramDetails = ImmutableDictionary<string, string>
          .Empty.SetItem("note", "The notes content")
          .SetItem("operator", "Opername"),
      };
      logsForMat1.Add(expectedNotesLog);

      _jobLog.GetLogForJobUnique(mat1.JobUniqueStr).EventsShouldBe(logsForMat1);
    }

    [Test]
    public void LookupByPallet()
    {
      using var _jobLog = _repoCfg.OpenConnection();

      _jobLog.CurrentPalletLog(123).ShouldBeEmpty();
      _jobLog.CurrentPalletLog(4).ShouldBeEmpty();

      var pal1Initial = new List<LogEntry>();
      var pal1Cycle = new List<LogEntry>();
      var pal2Cycle = new List<LogEntry>();

      var mat1 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("unique", "part1", 2),
        "unique",
        1,
        "part1",
        2,
        "",
        "",
        "24"
      );
      var mat2 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("unique2", "part2", 2),
        "unique2",
        2,
        "part2",
        2,
        "",
        "",
        "33"
      );

      DateTime pal1InitialTime = DateTime.UtcNow.AddHours(-4);

      // *********** Add load cycle on pal1
      // load start won't be on the cycle created by RecordLoadUnloadComplete
      var loadStart = _jobLog.RecordLoadStart(
        mats: new[] { mat1, mat2 }.Select(EventLogMaterial.FromLogMat),
        pallet: 1,
        lulNum: 2,
        timeUTC: pal1InitialTime
      );
      var initialLoad = _jobLog.RecordLoadUnloadComplete(
        toLoad:
        [
          new MaterialToLoadOntoFace()
          {
            MaterialIDs = [mat1.MaterialID, mat2.MaterialID],
            Process = mat1.Process,
            Path = mat1.Path,
            FaceNum = mat1.Face,
            ActiveOperationTime = TimeSpan.FromSeconds(22),
          },
        ],
        toUnload: null,
        previouslyLoaded: null,
        previouslyUnloaded: null,
        pallet: 1,
        lulNum: 2,
        totalElapsed: TimeSpan.FromMinutes(10),
        timeUTC: pal1InitialTime.AddMinutes(5),
        externalQueues: null
      );
      var initialPalCycle = initialLoad.First(e => e.LogType == LogType.PalletCycle);
      pal1Initial.AddRange(initialLoad.Where(e => e.LogType != LogType.PalletCycle));

      // *********** Add machine cycle on pal1
      pal1Initial.Add(
        _jobLog.RecordMachineStart(
          mats: new[] { mat1, mat2 }.Select(EventLogMaterial.FromLogMat),
          pallet: 1,
          statName: "MC",
          statNum: 2,
          program: "prog1",
          timeUTC: pal1InitialTime.AddMinutes(10)
        )
      ); //end of route
      pal1Initial.Add(
        _jobLog.RecordMachineEnd(
          mats: new[] { mat1, mat2 }.Select(EventLogMaterial.FromLogMat),
          pallet: 1,
          statName: "MC",
          statNum: 2,
          program: "prog1",
          timeUTC: pal1InitialTime.AddMinutes(20),
          result: "result",
          elapsed: TimeSpan.FromMinutes(10),
          active: TimeSpan.Zero
        )
      ); //end of route
      // ***********  End of Route for pal1

      _jobLog.CurrentPalletLog(1).EventsShouldBe(pal1Initial);
      _jobLog
        .GetLogForMaterial(mat1.MaterialID, includeInvalidatedCycles: false)
        .EventsShouldBe([loadStart, initialPalCycle, .. pal1Initial]);
      _jobLog
        .GetLogForMaterial([mat1.MaterialID, mat2.MaterialID], includeInvalidatedCycles: false)
        .EventsShouldBe([loadStart, initialPalCycle, .. pal1Initial]);
      _jobLog.CurrentPalletLog(2).ShouldBeEmpty();

      var pal1CycleEvt = _jobLog
        .RecordEmptyPallet(pallet: 1, timeUTC: pal1InitialTime.AddMinutes(25))
        .ShouldHaveSingleItem();

      pal1CycleEvt.ShouldBeEquivalentTo(
        new LogEntry(
          pal1CycleEvt.Counter,
          new LogMaterial[] { },
          1,
          LogType.PalletCycle,
          "Pallet Cycle",
          1,
          "",
          true,
          pal1InitialTime.AddMinutes(25),
          "PalletCycle",
          TimeSpan.Zero,
          TimeSpan.Zero
        )
      );

      _jobLog
        .GetLogEntries(DateTime.UtcNow.AddHours(-10), DateTime.UtcNow)
        .EventsShouldBe([loadStart, initialPalCycle, .. pal1Initial, pal1CycleEvt]);
      _jobLog.CurrentPalletLog(1).ShouldBeEmpty();
      _jobLog.CurrentPalletLog(1, includeLastPalletCycleEvt: true).EventsShouldBe([pal1CycleEvt]);
      _jobLog.CurrentPalletLog(2).ShouldBeEmpty();

      DateTime pal2CycleTime = DateTime.UtcNow.AddHours(-3);

      // *********** Add pal2 load event
      pal2Cycle.Add(
        _jobLog.RecordLoadStart(
          mats: new[] { mat1, mat2 }.Select(EventLogMaterial.FromLogMat),
          pallet: 2,
          lulNum: 2,
          timeUTC: pal2CycleTime
        )
      );
      pal2Cycle.Add(
        _jobLog.RecordMachineEnd(
          mats: new[] { mat1, mat2 }.Select(EventLogMaterial.FromLogMat),
          pallet: 2,
          statName: "MC",
          statNum: 2,
          program: "prog1",
          result: "result",
          timeUTC: pal2CycleTime.AddMinutes(10),
          elapsed: TimeSpan.FromMinutes(10),
          active: TimeSpan.Zero
        )
      );

      _jobLog.CurrentPalletLog(1).ShouldBeEmpty();
      _jobLog.CurrentPalletLog(2).EventsShouldBe(pal2Cycle);

      DateTime pal1CycleTime = DateTime.UtcNow.AddHours(-2);

      // ********** Add pal1 load event
      var pal1LoadComp = _jobLog.RecordLoadUnloadComplete(
        toLoad:
        [
          new MaterialToLoadOntoFace()
          {
            MaterialIDs = [mat1.MaterialID, mat2.MaterialID],
            Process = mat1.Process,
            Path = mat1.Path,
            FaceNum = mat1.Face,
            ActiveOperationTime = TimeSpan.FromSeconds(22),
          },
        ],
        toUnload: [],
        previouslyLoaded: null,
        previouslyUnloaded: null,
        pallet: 1,
        lulNum: 2,
        timeUTC: pal1CycleTime.AddMinutes(15),
        totalElapsed: TimeSpan.FromMinutes(10),
        externalQueues: null
      );

      var pal1PalletCycle = pal1LoadComp.First(e => e.LogType == LogType.PalletCycle);
      pal1Cycle.Add(pal1LoadComp.First(e => e.LogType != LogType.PalletCycle));

      // *********** Add pal1 start of machining
      pal1Cycle.Add(
        _jobLog.RecordMachineStart(
          mats: new[] { mat1, mat2 }.Select(EventLogMaterial.FromLogMat),
          pallet: 1,
          statName: "MC",
          statNum: 4,
          program: "prog1",
          timeUTC: pal1CycleTime.AddMinutes(20)
        )
      ); //end of route

      _jobLog.CurrentPalletLog(1).EventsShouldBe(pal1Cycle);
      _jobLog.CurrentPalletLog(2).EventsShouldBe(pal2Cycle);

      //********  Complete the pal1 machining
      pal1Cycle.Add(
        _jobLog.RecordMachineEnd(
          new[] { mat1, mat2 }.Select(EventLogMaterial.FromLogMat),
          pallet: 1,
          statName: "MC",
          statNum: 4,
          program: "prog1",
          result: "result",
          timeUTC: pal1CycleTime.AddMinutes(30),
          elapsed: TimeSpan.FromMinutes(30),
          active: TimeSpan.Zero
        )
      );

      _jobLog.CurrentPalletLog(1).EventsShouldBe(pal1Cycle);
      _jobLog.CurrentPalletLog(2).EventsShouldBe(pal2Cycle);
      _jobLog
        .GetLogForMaterial(mat1.MaterialID, includeInvalidatedCycles: false)
        .EventsShouldBe([
          loadStart,
          initialPalCycle,
          .. pal1Initial,
          pal1PalletCycle,
          .. pal1Cycle,
          .. pal2Cycle,
        ]);
      _jobLog
        .GetLogForMaterial([mat1.MaterialID, mat2.MaterialID], includeInvalidatedCycles: false)
        .EventsShouldBe([
          loadStart,
          initialPalCycle,
          .. pal1Initial,
          pal1PalletCycle,
          .. pal1Cycle,
          .. pal2Cycle,
        ]);

      //********  Ignores invalidated
      var invalidated = _jobLog.RecordMachineStart(
        new[] { mat1, mat2 }.Select(EventLogMaterial.FromLogMat),
        pallet: 1,
        statName: "OtherMC",
        statNum: 100,
        program: "prog22",
        timeUTC: pal1CycleTime.AddMinutes(31),
        extraData: new Dictionary<string, string> { { "PalletCycleInvalidated", "1" } }
      );

      // invalidated not added to pal1Cycle
      _jobLog.CurrentPalletLog(1).EventsShouldBe(pal1Cycle);
      _jobLog
        .GetLogForMaterial(mat1.MaterialID, includeInvalidatedCycles: false)
        .EventsShouldBe([
          loadStart,
          initialPalCycle,
          .. pal1Initial,
          pal1PalletCycle,
          .. pal1Cycle,
          .. pal2Cycle,
        ]);
      _jobLog
        .GetLogForMaterial([mat1.MaterialID, mat2.MaterialID], includeInvalidatedCycles: false)
        .EventsShouldBe([
          loadStart,
          initialPalCycle,
          .. pal1Initial,
          pal1PalletCycle,
          .. pal1Cycle,
          .. pal2Cycle,
        ]);

      _jobLog.RecordEmptyPallet(pallet: 1, timeUTC: pal1CycleTime.AddMinutes(40));
      pal1Cycle.Add(
        new LogEntry(
          0,
          new LogMaterial[] { },
          1,
          LogType.PalletCycle,
          "Pallet Cycle",
          1,
          "",
          true,
          pal1CycleTime.AddMinutes(40),
          "PalletCycle",
          TimeSpan.Zero,
          TimeSpan.Zero
        )
      );

      _jobLog.CurrentPalletLog(1).ShouldBeEmpty();

      // add invalidated when loading all entries
      _jobLog
        .GetLogEntries(pal1CycleTime.AddMinutes(-5), DateTime.UtcNow)
        .EventsShouldBe([
          .. pal1LoadComp.Where(e => e.LogType == LogType.PalletCycle),
          .. pal1Cycle,
          invalidated,
        ]);

      _jobLog.CurrentPalletLog(2).EventsShouldBe(pal2Cycle);
    }

    [Test]
    public void ForeignID()
    {
      using var _jobLog = _repoCfg.OpenConnection();

      var mat1ID = _jobLog.AllocateMaterialID("unique", "part1", 2);
      var mat2ID = _jobLog.AllocateMaterialID("unique2", "part2", 2);

      _jobLog.MaxForeignID().ShouldBe("");
      _jobLog.MostRecentLogEntryForForeignID("for1").ShouldBeNull();
      _jobLog.MostRecentLogEntryLessOrEqualToForeignID("for1").ShouldBeNull();

      var log1 = _jobLog.RecordGeneralMessage(
        mat: new()
        {
          MaterialID = mat1ID,
          Process = 1,
          Face = 0,
        },
        program: "general prog",
        result: "general result",
        foreignId: "for1"
      );

      _jobLog.MaxForeignID().ShouldBe("for1");

      var log2 = _jobLog.RecordMachineEnd(
        mats:
        [
          new()
          {
            MaterialID = mat1ID,
            Process = 1,
            Face = 0,
          },
          new()
          {
            MaterialID = mat2ID,
            Process = 1,
            Face = 0,
          },
        ],
        pallet: 4,
        statName: "MC",
        statNum: 42,
        program: "prog",
        result: "result",
        timeUTC: DateTime.UtcNow,
        elapsed: TimeSpan.FromMinutes(15),
        active: TimeSpan.FromMinutes(16),
        foreignId: "for2"
      );

      _jobLog.MaxForeignID().ShouldBe("for2");

      //add one without a foreign id

      var log3 = _jobLog.RecordPalletDepartStocker(
        mats:
        [
          new()
          {
            MaterialID = mat2ID,
            Process = 1,
            Face = 2,
          },
        ],
        pallet: 4,
        stockerNum: 4,
        timeUTC: DateTime.UtcNow,
        waitForMachine: true,
        elapsed: TimeSpan.FromMinutes(10)
      );

      _jobLog.MaxForeignID().ShouldBe("for2");

      _jobLog.MostRecentLogEntryForForeignID("for1").ShouldBeEquivalentTo(log1);
      _jobLog.MostRecentLogEntryLessOrEqualToForeignID("for1").ShouldBeEquivalentTo(log1);
      _jobLog.MostRecentLogEntryLessOrEqualToForeignID("for11234").ShouldBeEquivalentTo(log1);

      _jobLog.MostRecentLogEntryForForeignID("for2").ShouldBeEquivalentTo(log2);

      _jobLog.MostRecentLogEntryForForeignID("abwgtweg").ShouldBeNull();

      _jobLog.ForeignIDForCounter(log1.Counter).ShouldBe("for1");
      _jobLog.ForeignIDForCounter(log2.Counter).ShouldBe("for2");
      _jobLog.ForeignIDForCounter(log3.Counter + 100).ShouldBe("");

      // add another log with same foreign id
      var expectedFor1Serial = _jobLog.RecordSerialForMaterialID(
        new()
        {
          MaterialID = mat1ID,
          Process = 2,
          Face = 1,
        },
        "ser1",
        DateTime.UtcNow.AddHours(-2),
        foreignID: "for1"
      );

      // added 2 hours in the past, but still most recent since recent is checking counters, not time
      _jobLog.MostRecentLogEntryForForeignID("for1").ShouldBeEquivalentTo(expectedFor1Serial);
    }

    [Test]
    public void OriginalMessage()
    {
      using var _jobLog = _repoCfg.OpenConnection();
      var mat1 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("uniqqqq", "pppart66", 5),
        "uniqqqq",
        1,
        "pppart66",
        5,
        "",
        "",
        "41"
      );
      var mat2 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("uuuuuniq", "part5", 2),
        "uuuuuniq",
        2,
        "part5",
        2,
        "",
        "",
        "2"
      );

      _jobLog.MaxForeignID().ShouldBe("");

      var log1 = _jobLog.RecordGeneralMessage(
        mats: [EventLogMaterial.FromLogMat(mat1), EventLogMaterial.FromLogMat(mat2)],
        pallet: 16,
        program: "program125",
        result: "result66",
        foreignId: "foreign1",
        originalMessage: "the original message"
      );

      _jobLog.MaxForeignID().ShouldBe("foreign1");

      _jobLog.MostRecentLogEntryForForeignID("foreign1").ShouldBeEquivalentTo(log1);

      _jobLog.OriginalMessageByForeignID("foreign1").ShouldBe("the original message");
      _jobLog.OriginalMessageByForeignID("abc").ShouldBe("");
    }

    [Test]
    public void WorkorderSummary()
    {
      using var _jobLog = _repoCfg.OpenConnection();
      var t = DateTime.UtcNow.AddHours(-1);

      var earlierWork = _fixture.Create<Workorder>() with { WorkorderId = "earlierwork" };
      var work1part1 = _fixture.Create<Workorder>() with { WorkorderId = "work1", Part = "part1" };
      var work1part2 = _fixture.Create<Workorder>() with { WorkorderId = "work1", Part = "part2" };
      var work2 = _fixture.Create<Workorder>() with { WorkorderId = "work2", Part = "part3" };
      var work1part1sim = _fixture
        .Build<WorkorderSimFilled>()
        .With(w => w.WorkorderId, "work1")
        .With(w => w.Part, "part1")
        .Create();
      var work1part2sim = _fixture
        .Build<WorkorderSimFilled>()
        .With(w => w.WorkorderId, "work1")
        .With(w => w.Part, "part2")
        .Create();
      var work2sim = _fixture
        .Build<WorkorderSimFilled>()
        .With(w => w.WorkorderId, "work2")
        .With(w => w.Part, "part3")
        .Create();

      _jobLog.AddJobs(
        new NewJobs()
        {
          ScheduleId = "aaaa",
          Jobs = [],
          CurrentUnfilledWorkorders = [earlierWork],
        },
        null,
        true
      );

      _jobLog.AddJobs(
        new NewJobs()
        {
          ScheduleId = "cccc",
          Jobs = [],
          CurrentUnfilledWorkorders = [work1part1, work1part2, work2],
          SimWorkordersFilled = [work1part1sim, work1part2sim, work2sim],
        },
        null,
        true
      );

      //one material across two processes
      var mat1 = _jobLog.AllocateMaterialID("uniq1", "part1", 2);
      var mat1_proc1 = MkLogMat.Mk(mat1, "uniq1", 1, "part1", 2, "", "", "");
      var mat1_proc2 = MkLogMat.Mk(mat1, "uniq1", 2, "part1", 2, "", "", "");
      var mat2_proc1 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("uniq1", "part1", 2),
        "uniq1",
        1,
        "part1",
        2,
        "",
        "",
        ""
      );
      var mat7 = MkLogMat.Mk(
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
      _jobLog.RecordMachineEnd(
        mats: [EventLogMaterial.FromLogMat(mat1_proc1)],
        pallet: 1,
        statName: "MC",
        statNum: 5,
        program: "prog1",
        result: "",
        timeUTC: t.AddMinutes(5),
        elapsed: TimeSpan.FromMinutes(10),
        active: TimeSpan.FromMinutes(20)
      );
      _jobLog.RecordMachineEnd(
        mats: [EventLogMaterial.FromLogMat(mat1_proc2)],
        pallet: 1,
        statName: "MC",
        statNum: 5,
        program: "prog2",
        timeUTC: t.AddMinutes(6),
        result: "",
        elapsed: TimeSpan.FromMinutes(30),
        active: TimeSpan.FromMinutes(40)
      );
      _jobLog.RecordPartialLoadUnload(
        toLoad:
        [
          new MaterialToLoadOntoFace()
          {
            MaterialIDs = [mat7.MaterialID],
            Process = mat7.Process,
            Path = mat7.Path,
            FaceNum = mat7.Face,
            ActiveOperationTime = TimeSpan.FromMinutes(30),
          },
        ],
        toUnload:
        [
          new MaterialToUnloadFromFace()
          {
            MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>
              .Empty.Add(mat1_proc2.MaterialID, null)
              .Add(mat2_proc1.MaterialID, null),
            Process = 2,
            FaceNum = mat1_proc2.Face,
            ActiveOperationTime = TimeSpan.FromMinutes(60),
          },
        ],
        //mat2_proc1 should be ignored since it isn't final process
        pallet: 1,
        lulNum: 5,
        timeUTC: t.AddMinutes(7),
        totalElapsed: TimeSpan.FromMinutes(75),
        externalQueues: null
      );

      //four materials on the same pallet but different workorders
      var mat3 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("uniq2", "part1", 1),
        "uniq2",
        1,
        "part1",
        1,
        "",
        "",
        ""
      );
      var mat4 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("uniq2", "part2", 1),
        "uniq2",
        1,
        "part2",
        1,
        "",
        "",
        ""
      );
      var mat5 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("uniq2", "part3", 1),
        "uniq2",
        1,
        "part3",
        1,
        "",
        "",
        ""
      );
      var mat6 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("uniq2", "part3", 1),
        "uniq2",
        1,
        "part3",
        1,
        "",
        "",
        ""
      );

      _jobLog.RecordMachineEnd(
        mats: new[] { mat3, mat4, mat5, mat6 }.Select(EventLogMaterial.FromLogMat),
        pallet: 1,
        statName: "MC",
        statNum: 5,
        program: "progdouble",
        timeUTC: t.AddMinutes(15),
        result: "",
        elapsed: TimeSpan.FromMinutes(3),
        active: TimeSpan.FromMinutes(4)
      );
      _jobLog.RecordLoadUnloadComplete(
        toLoad: null,
        toUnload:
        [
          new MaterialToUnloadFromFace()
          {
            MaterialIDToDestination = new[] { mat3, mat4, mat5, mat6 }.ToImmutableDictionary(
              m => m.MaterialID,
              _ => (UnloadDestination)null
            ),
            Process = mat3.Process,
            FaceNum = mat3.Face,
            ActiveOperationTime = TimeSpan.FromMinutes(6),
          },
        ],
        previouslyLoaded: null,
        previouslyUnloaded: null,
        pallet: 1,
        lulNum: 5,
        timeUTC: t.AddMinutes(17),
        totalElapsed: TimeSpan.FromMinutes(5),
        externalQueues: null
      );

      //now record serial and workorder
      _jobLog.RecordSerialForMaterialID(EventLogMaterial.FromLogMat(mat1_proc2), "serial1", t.AddHours(1));
      _jobLog.RecordSerialForMaterialID(EventLogMaterial.FromLogMat(mat2_proc1), "serial2", t.AddHours(2));
      _jobLog.RecordSerialForMaterialID(EventLogMaterial.FromLogMat(mat3), "serial3", t.AddHours(3));
      _jobLog.RecordSerialForMaterialID(EventLogMaterial.FromLogMat(mat4), "serial4", t.AddHours(4));
      _jobLog.RecordSerialForMaterialID(EventLogMaterial.FromLogMat(mat5), "serial5", t.AddHours(5));
      _jobLog.RecordSerialForMaterialID(EventLogMaterial.FromLogMat(mat6), "serial6", t.AddHours(6));
      _jobLog.GetMaterialDetails(mat1_proc2.MaterialID).Serial.ShouldBe("serial1");
      _jobLog
        .GetMaterialDetailsForSerial("serial1")
        .ShouldBeEquivalentTo(
          new List<MaterialDetails>
          {
            new MaterialDetails()
            {
              MaterialID = mat1_proc2.MaterialID,
              JobUnique = "uniq1",
              PartName = "part1",
              NumProcesses = 2,
              Serial = "serial1",
            },
          }
        );
      _jobLog.GetMaterialDetailsForSerial("waoheufweiuf").ShouldBeEmpty();

      _jobLog.RecordWorkorderForMaterialID(EventLogMaterial.FromLogMat(mat1_proc2), "work1");
      _jobLog.RecordWorkorderForMaterialID(EventLogMaterial.FromLogMat(mat3), "work1");
      _jobLog.RecordWorkorderForMaterialID(EventLogMaterial.FromLogMat(mat4), "work1");
      _jobLog.RecordWorkorderForMaterialID(EventLogMaterial.FromLogMat(mat5), "work2");
      _jobLog.RecordWorkorderForMaterialID(EventLogMaterial.FromLogMat(mat6), "work2");
      _jobLog.GetMaterialDetails(mat5.MaterialID).Workorder.ShouldBe("work2");

      // mat1 is closed out, mat5 failed closeout
      _jobLog.RecordCloseoutCompleted(
        EventLogMaterial.FromLogMat(mat1_proc2),
        7,
        "Closety",
        success: true,
        new Dictionary<string, string>(),
        TimeSpan.FromMinutes(44),
        TimeSpan.FromMinutes(9)
      );
      _jobLog.RecordCloseoutCompleted(
        EventLogMaterial.FromLogMat(mat5),
        7,
        "Closety",
        success: false,
        new Dictionary<string, string>(),
        TimeSpan.FromMinutes(44),
        TimeSpan.FromMinutes(9)
      );

      // failed inspections for mat3
      _jobLog.RecordInspectionCompleted(
        EventLogMaterial.FromLogMat(mat3),
        5,
        "insptype1",
        false,
        new Dictionary<string, string> { { "a", "aaa" }, { "b", "bbb" } },
        TimeSpan.FromMinutes(100),
        TimeSpan.FromMinutes(5)
      );

      // quarantine. mat5 and mat6 are quarantined, but mat6 has other events so that overrides it
      _jobLog.SignalMaterialForQuarantine(
        EventLogMaterial.FromLogMat(mat5),
        pallet: 3,
        queue: "quarantine",
        operatorName: "oper",
        reason: "thereason",
        t.AddMinutes(10)
      );
      _jobLog.RecordAddMaterialToQueue(
        EventLogMaterial.FromLogMat(mat6),
        queue: "qqq",
        position: -1,
        operatorName: "oper",
        reason: "Quarantine",
        timeUTC: t.AddMinutes(11)
      );
      _jobLog.RecordLoadStart(
        mats: new[] { mat6 }.Select(EventLogMaterial.FromLogMat),
        pallet: 3,
        lulNum: 5,
        timeUTC: t.AddMinutes(12)
      );

      double c2Cnt = 4; //number of material on cycle 2

      var expectedActiveWorks = ImmutableList.Create(
        new ActiveWorkorder()
        {
          WorkorderId = "work1",
          Part = work1part1.Part,
          PlannedQuantity = work1part1.Quantity,
          DueDate = work1part1.DueDate,
          Priority = work1part1.Priority,
          CompletedQuantity = 2, // mat1 and mat3
          Material =
          [
            new WorkorderMaterial()
            {
              MaterialID = mat1_proc2.MaterialID,
              Serial = "serial1",
              Quarantined = false,
              InspectionFailed = false,
              Closeout = WorkorderSerialCloseout.ClosedOut,
            },
            new WorkorderMaterial()
            {
              MaterialID = mat3.MaterialID,
              Serial = "serial3",
              Quarantined = false,
              InspectionFailed = true,
              Closeout = WorkorderSerialCloseout.None,
            },
          ],
          Comments = null,
          ElapsedStationTime = ImmutableDictionary<string, TimeSpan>
            .Empty.Add("MC", TimeSpan.FromMinutes(10 + 30 + 3 * 1 / c2Cnt)) //10 + 30 from mat1, 3*1/4 for mat3
            .Add(
              "L/U",
              TimeSpan.FromMinutes(50 / 2 + 5 * 1 / c2Cnt)
            ) //50/2 from mat1_proc2, and 5*1/4 for mat3
          ,
          ActiveStationTime = ImmutableDictionary<string, TimeSpan>
            .Empty.Add("MC", TimeSpan.FromMinutes(20 + 40 + 4 * 1 / c2Cnt)) //20 + 40 from mat1, 4*1/4 for mat3
            .Add("L/U", TimeSpan.FromMinutes(60 / 2 + 6 * 1 / c2Cnt)), //60/2 from mat1_proc2, and 6*1/4 for mat3
          SimulatedStart = work1part1sim.Started,
          SimulatedFilled = work1part1sim.Filled,
        },
        new ActiveWorkorder()
        {
          WorkorderId = "work1",
          Part = work1part2.Part,
          PlannedQuantity = work1part2.Quantity,
          DueDate = work1part2.DueDate,
          Priority = work1part2.Priority,
          CompletedQuantity = 1,
          Material =
          [
            new WorkorderMaterial()
            {
              MaterialID = mat4.MaterialID,
              Serial = "serial4",
              Quarantined = false,
              InspectionFailed = false,
              Closeout = WorkorderSerialCloseout.None,
            },
          ],
          Comments = null,
          ElapsedStationTime = ImmutableDictionary<string, TimeSpan>
            .Empty.Add("MC", TimeSpan.FromMinutes(3 * 1 / c2Cnt))
            .Add("L/U", TimeSpan.FromMinutes(5 * 1 / c2Cnt)),
          ActiveStationTime = ImmutableDictionary<string, TimeSpan>
            .Empty.Add("MC", TimeSpan.FromMinutes(4 * 1 / c2Cnt))
            .Add("L/U", TimeSpan.FromMinutes(6 * 1 / c2Cnt)),
          SimulatedStart = work1part2sim.Started,
          SimulatedFilled = work1part2sim.Filled,
        },
        new ActiveWorkorder()
        {
          WorkorderId = "work2",
          Part = work2.Part,
          PlannedQuantity = work2.Quantity,
          DueDate = work2.DueDate,
          Priority = work2.Priority,
          CompletedQuantity = 2,
          Material =
          [
            new WorkorderMaterial()
            {
              MaterialID = mat5.MaterialID,
              Serial = "serial5",
              Quarantined = true,
              InspectionFailed = false,
              Closeout = WorkorderSerialCloseout.CloseOutFailed,
            },
            new WorkorderMaterial()
            {
              MaterialID = mat6.MaterialID,
              Serial = "serial6",
              Quarantined = false,
              InspectionFailed = false,
              Closeout = WorkorderSerialCloseout.None,
            },
          ],
          Comments = null,
          ElapsedStationTime = ImmutableDictionary<string, TimeSpan>
            .Empty.Add("MC", TimeSpan.FromMinutes(3 * 2 / c2Cnt))
            .Add("L/U", TimeSpan.FromMinutes(5 * 2 / c2Cnt)),
          ActiveStationTime = ImmutableDictionary<string, TimeSpan>
            .Empty.Add("MC", TimeSpan.FromMinutes(4 * 2 / c2Cnt))
            .Add("L/U", TimeSpan.FromMinutes(6 * 2 / c2Cnt)),
          SimulatedStart = work2sim.Started,
          SimulatedFilled = work2sim.Filled,
        }
      );

      _jobLog.GetActiveWorkorders().ShouldBeEquivalentTo(expectedActiveWorks);

      _jobLog
        .GetActiveWorkorder(workorder: "work1")
        .ShouldBeEquivalentTo(ImmutableList.Create(expectedActiveWorks[0], expectedActiveWorks[1]));

      _jobLog.GetActiveWorkorder(workorder: "asdoiuhwe").ShouldBeEmpty();

      //---- test comments
      var finalizedEntry = _jobLog.RecordWorkorderComment(
        "work1",
        comment: "work1ccc",
        operName: "oper1",
        timeUTC: t.AddHours(222)
      );
      finalizedEntry.Pallet.ShouldBe(0);
      finalizedEntry.Result.ShouldBe("work1");
      finalizedEntry.LogType.ShouldBe(LogType.WorkorderComment);
      finalizedEntry.ProgramDetails.ShouldBeEquivalentTo(
        ImmutableDictionary<string, string>.Empty.Add("Comment", "work1ccc").Add("Operator", "oper1")
      );
      var expectedComment1 = new WorkorderComment() { Comment = "work1ccc", TimeUTC = t.AddHours(222) };

      _jobLog
        .GetActiveWorkorders()
        .ShouldBeEquivalentTo(
          ImmutableList.Create(
            expectedActiveWorks[0] with
            {
              Comments = ImmutableList.Create(expectedComment1),
            },
            expectedActiveWorks[1] with
            {
              Comments = ImmutableList.Create(expectedComment1),
            },
            expectedActiveWorks[2]
          )
        );

      // add a second comment
      _jobLog.RecordWorkorderComment("work1", comment: "work1ddd", operName: null, timeUTC: t.AddHours(333));
      var expectedComment2 = new WorkorderComment() { Comment = "work1ddd", TimeUTC = t.AddHours(333) };

      _jobLog
        .GetActiveWorkorders()
        .ShouldBeEquivalentTo(
          ImmutableList.Create(
            expectedActiveWorks[0] with
            {
              Comments = ImmutableList.Create(expectedComment1, expectedComment2),
            },
            expectedActiveWorks[1] with
            {
              Comments = ImmutableList.Create(expectedComment1, expectedComment2),
            },
            expectedActiveWorks[2]
          )
        );

      // new jobs with empty workorders, should replace with empty
      _jobLog.AddJobs(
        new NewJobs()
        {
          ScheduleId = "dddd",
          Jobs = ImmutableList.Create(_fixture.Create<Job>()),
          CurrentUnfilledWorkorders = [],
        },
        null,
        true
      );

      _jobLog.GetActiveWorkorders().ShouldBeEmpty();

      // still loads even if archived
      _jobLog
        .GetActiveWorkorder(workorder: "work2")
        .ShouldBeEquivalentTo(ImmutableList.Create(expectedActiveWorks[2]));
    }

    [Test]
    public void LoadCompletedParts()
    {
      using var _jobLog = _repoCfg.OpenConnection();
      var old = DateTime.UtcNow.AddHours(-50);
      var recent = DateTime.UtcNow.AddHours(-1);

      //material
      var mat1 = _jobLog.AllocateMaterialID("uniq1", "part1", 2);
      var mat1_proc1 = MkLogMat.Mk(mat1, "uniq1", 1, "part1", 2, "", "", "");
      var mat1_proc2 = MkLogMat.Mk(mat1, "uniq1", 2, "part1", 2, "", "", "");
      var mat2 = _jobLog.AllocateMaterialID("uniq1", "part1", 2);
      var mat2_proc1 = MkLogMat.Mk(mat2, "uniq1", 1, "part1", 2, "", "", "");
      var mat2_proc2 = MkLogMat.Mk(mat2, "uniq1", 2, "part1", 2, "", "", "");

      var mat3 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("uniq1", "part1", 1),
        "uniq1",
        1,
        "part1",
        1,
        "",
        "",
        ""
      );
      var mat4 = MkLogMat.Mk(
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
      var mat1_proc1old = _jobLog.RecordMachineEnd(
        mats: [EventLogMaterial.FromLogMat(mat1_proc1)],
        pallet: 1,
        statName: "MC",
        statNum: 5,
        program: "prog1",
        timeUTC: old.AddMinutes(5),
        result: "",
        elapsed: TimeSpan.FromMinutes(10),
        active: TimeSpan.FromMinutes(20)
      );
      var mat1_proc1load = _jobLog.RecordLoadUnloadComplete(
        toLoad:
        [
          new MaterialToLoadOntoFace()
          {
            MaterialIDs = [mat1_proc1.MaterialID],
            Process = mat1_proc1.Process,
            Path = mat1_proc1.Path,
            FaceNum = mat1_proc1.Face,
            ActiveOperationTime = TimeSpan.FromMinutes(21),
          },
        ],
        toUnload: null,
        previouslyLoaded: null,
        previouslyUnloaded: null,
        lulNum: 5,
        pallet: 1,
        timeUTC: old.AddMinutes(6),
        totalElapsed: TimeSpan.FromMinutes(11),
        externalQueues: null
      );
      var mat1_proc2old = _jobLog.RecordMachineEnd(
        mats: [EventLogMaterial.FromLogMat(mat1_proc2)],
        pallet: 1,
        statName: "MC",
        statNum: 5,
        program: "prog2",
        timeUTC: old.AddMinutes(7),
        result: "",
        elapsed: TimeSpan.FromMinutes(12),
        active: TimeSpan.FromMinutes(22)
      );
      var mat1_proc2complete = _jobLog.RecordLoadUnloadComplete(
        toLoad: null,
        toUnload:
        [
          new MaterialToUnloadFromFace()
          {
            MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(
              mat1_proc2.MaterialID,
              null
            ),
            Process = mat1_proc2.Process,
            FaceNum = mat1_proc2.Face,
            ActiveOperationTime = TimeSpan.FromMinutes(40),
          },
        ],
        previouslyLoaded: null,
        previouslyUnloaded: null,
        pallet: 1,
        lulNum: 5,
        timeUTC: recent.AddMinutes(4),
        totalElapsed: TimeSpan.FromMinutes(30),
        externalQueues: null
      );

      //mat2 has everything in recent, (including proc1 complete) but no complete on proc2
      _jobLog.RecordMachineEnd(
        mats: [EventLogMaterial.FromLogMat(mat2_proc1)],
        pallet: 1,
        statName: "MC",
        statNum: 5,
        program: "mach2",
        timeUTC: recent.AddMinutes(5),
        result: "",
        elapsed: TimeSpan.FromMinutes(50),
        active: TimeSpan.FromMinutes(60)
      );
      var mat2_proc1complete = _jobLog.RecordLoadUnloadComplete(
        toLoad: null,
        toUnload:
        [
          new MaterialToUnloadFromFace()
          {
            MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(
              mat2_proc1.MaterialID,
              null
            ),
            Process = 1,
            FaceNum = mat2_proc1.Face,
            ActiveOperationTime = TimeSpan.FromMinutes(61),
          },
        ],
        previouslyLoaded: null,
        previouslyUnloaded: null,
        pallet: 1,
        lulNum: 5,
        timeUTC: recent.AddMinutes(6),
        totalElapsed: TimeSpan.FromMinutes(51),
        externalQueues: null
      );
      _jobLog.RecordMachineEnd(
        mats: [EventLogMaterial.FromLogMat(mat2_proc2)],
        pallet: 1,
        statName: "MC",
        statNum: 5,
        program: "mach2",
        timeUTC: old.AddMinutes(7),
        result: "",
        elapsed: TimeSpan.FromMinutes(52),
        active: TimeSpan.FromMinutes(62)
      );

      //mat3 has everything in old
      _jobLog.RecordMachineEnd(
        mats: [EventLogMaterial.FromLogMat(mat3)],
        pallet: 1,
        statName: "MC",
        statNum: 5,
        program: "prog3",
        timeUTC: old.AddMinutes(20),
        result: "",
        elapsed: TimeSpan.FromMinutes(70),
        active: TimeSpan.FromMinutes(80)
      );
      var mat3_complete = _jobLog.RecordLoadUnloadComplete(
        toLoad: null,
        toUnload:
        [
          new MaterialToUnloadFromFace()
          {
            MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(
              mat3.MaterialID,
              null
            ),
            Process = mat3.Process,
            FaceNum = mat3.Face,
            ActiveOperationTime = TimeSpan.FromMinutes(81),
          },
        ],
        previouslyLoaded: null,
        previouslyUnloaded: null,
        pallet: 1,
        lulNum: 5,
        timeUTC: old.AddMinutes(25),
        totalElapsed: TimeSpan.FromMinutes(71),
        externalQueues: null
      );

      //mat4 has everything in new
      var mat4recent = _jobLog.RecordMachineEnd(
        mats: [EventLogMaterial.FromLogMat(mat4)],
        pallet: 1,
        statName: "MC",
        statNum: 5,
        program: "prog44",
        timeUTC: recent.AddMinutes(40),
        result: "",
        elapsed: TimeSpan.FromMinutes(90),
        active: TimeSpan.FromMinutes(100)
      );
      var mat4complete = _jobLog.RecordLoadUnloadComplete(
        toLoad: null,
        toUnload:
        [
          new MaterialToUnloadFromFace()
          {
            MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(
              mat4.MaterialID,
              null
            ),
            Process = mat4.Process,
            FaceNum = mat4.Face,
            ActiveOperationTime = TimeSpan.FromMinutes(101),
          },
        ],
        previouslyLoaded: null,
        previouslyUnloaded: null,
        pallet: 1,
        lulNum: 5,
        timeUTC: recent.AddMinutes(45),
        totalElapsed: TimeSpan.FromMinutes(91),
        externalQueues: null
      );

      _jobLog
        .GetLogOfAllCompletedParts(recent.AddHours(-4), recent.AddHours(4))
        .EventsShouldBe(
          (LogEntry[])
            [
              mat1_proc1old,
              .. mat1_proc1load,
              mat1_proc2old,
              .. mat1_proc2complete,
              mat4recent,
              .. mat4complete,
            ]
        );

      _jobLog
        .CompletedUnloadsSince(counter: -1)
        .EventsShouldBe([
          mat1_proc2complete.First(e => e.LogType == LogType.LoadUnloadCycle && e.Result == "UNLOAD"),
          mat2_proc1complete.First(e => e.LogType == LogType.LoadUnloadCycle && e.Result == "UNLOAD"),
          mat3_complete.First(e => e.LogType == LogType.LoadUnloadCycle && e.Result == "UNLOAD"),
          mat4complete.First(e => e.LogType == LogType.LoadUnloadCycle && e.Result == "UNLOAD"),
        ]);

      _jobLog
        .CompletedUnloadsSince(
          counter: mat2_proc1complete.First(e => e.LogType == LogType.LoadUnloadCycle).Counter
        )
        .EventsShouldBe([
          mat3_complete.First(e => e.LogType == LogType.LoadUnloadCycle && e.Result == "UNLOAD"),
          mat4complete.First(e => e.LogType == LogType.LoadUnloadCycle && e.Result == "UNLOAD"),
        ]);
    }

    [Test]
    public void Queues()
    {
      using var _jobLog = _repoCfg.OpenConnection();
      var start = DateTime.UtcNow.AddHours(-10);

      var otherQueueMat = MkLogMat.Mk(100, "uniq100", 100, "part100", 100, "", "", "");
      _jobLog.CreateMaterialID(100, "uniq100", "part100", 100);

      _jobLog.IsMaterialInQueue(100).ShouldBeFalse();

      _jobLog
        .RecordAddMaterialToQueue(
          EventLogMaterial.FromLogMat(otherQueueMat),
          "BBBB",
          0,
          "theoper",
          "thereason",
          start.AddHours(-1)
        )
        .ShouldHaveSingleItem()
        .ShouldBeEquivalentTo(
          AddToQueueExpectedEntry(otherQueueMat, 1, "BBBB", 0, start.AddHours(-1), "theoper", "thereason")
        );

      _jobLog.IsMaterialInQueue(100).ShouldBeTrue();

      var expectedLogs = new List<LogEntry>();

      var mat1 = MkLogMat.Mk(1, "uniq1", 15, "part111", 19, "mat1serial", "", "");
      _jobLog.CreateMaterialID(1, "uniq1", "part111", 19);
      var mat2 = MkLogMat.Mk(2, "uniq2", 1, "part2", 22, "mat2serial", "mat2workorder", "");
      _jobLog.CreateMaterialID(2, "uniq2", "part2", 22);
      var mat3 = MkLogMat.Mk(3, "uniq3", 3, "part3", 36, "", "", "");
      _jobLog.CreateMaterialID(3, "uniq3", "part3", 36);
      var mat4 = MkLogMat.Mk(4, "uniq4", 4, "part4", 44, "", "", "");
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
        .ShouldHaveSingleItem()
        .ShouldBeEquivalentTo(AddToQueueExpectedEntry(mat1, 5, "AAAA", 0, start));
      expectedLogs.Add(AddToQueueExpectedEntry(mat1, 5, "AAAA", 0, start));

      _jobLog
        .GetMaterialInQueueByUnique("AAAA", "uniq1")
        .ShouldBeEquivalentTo(
          new List<QueuedMaterial>
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
              Paths = ImmutableDictionary<int, int>.Empty,
            },
          }
        );
      _jobLog.GetMaterialInQueueByUnique("AAAA", "waeofuihwef").ShouldBeEmpty();
      _jobLog.IsMaterialInQueue(mat1.MaterialID).ShouldBeTrue();
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).ShouldBe(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).ShouldBeNull();
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).ShouldBeNull();
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).ShouldBeNull();

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
        .ShouldBeEquivalentTo(
          new List<LogEntry> { AddToQueueExpectedEntry(mat2, 6, "AAAA", 1, start.AddMinutes(10)) }
        );
      expectedLogs.Add(AddToQueueExpectedEntry(mat2, 6, "AAAA", 1, start.AddMinutes(10)));

      _jobLog
        .GetMaterialInQueueByUnique("AAAA", "uniq1")
        .ShouldBeEquivalentTo(
          new List<QueuedMaterial>
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
              Paths = ImmutableDictionary<int, int>.Empty,
            },
          }
        );
      _jobLog
        .GetMaterialInQueueByUnique("AAAA", "uniq2")
        .ShouldBeEquivalentTo(
          new List<QueuedMaterial>
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
              Paths = ImmutableDictionary<int, int>.Empty,
            },
          }
        );
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).ShouldBe(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).ShouldBe(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).ShouldBeNull();
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).ShouldBeNull();

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
        .ShouldBeEquivalentTo(
          new List<LogEntry>
          {
            AddToQueueExpectedEntry(mat3, 7, "AAAA", 1, start.AddMinutes(20), "opernnnn", reason: "rrrrr"),
          }
        );
      expectedLogs.Add(
        AddToQueueExpectedEntry(mat3, 7, "AAAA", 1, start.AddMinutes(20), "opernnnn", "rrrrr")
      );

      _jobLog
        .GetMaterialInQueueByUnique("AAAA", "uniq1")
        .ShouldBeEquivalentTo(
          new List<QueuedMaterial>
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
              Paths = ImmutableDictionary<int, int>.Empty.Add(1, 50).Add(2, 52),
            },
          }
        );
      _jobLog
        .GetMaterialInQueueByUnique("AAAA", "uniq3")
        .ShouldBeEquivalentTo(
          new List<QueuedMaterial>
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
              Paths = ImmutableDictionary<int, int>.Empty,
            },
          }
        );
      _jobLog
        .GetMaterialInQueueByUnique("AAAA", "uniq2")
        .ShouldBeEquivalentTo(
          new List<QueuedMaterial>
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
              Paths = ImmutableDictionary<int, int>.Empty,
            },
          }
        );
      _jobLog
        .GetMaterialInAllQueues()
        .ShouldBeEquivalentTo(
          new List<QueuedMaterial>
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
              Paths = ImmutableDictionary<int, int>.Empty.Add(1, 50).Add(2, 52),
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
              Paths = ImmutableDictionary<int, int>.Empty,
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
              Paths = ImmutableDictionary<int, int>.Empty,
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
              Paths = ImmutableDictionary<int, int>.Empty,
            },
          }
        );
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).ShouldBe(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).ShouldBe(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).ShouldBe(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).ShouldBeNull();

      //removing from queue with LogMaterial
      _jobLog
        .RecordRemoveMaterialFromAllQueues(EventLogMaterial.FromLogMat(mat3), "operyy", start.AddMinutes(40))
        .ShouldBeEquivalentTo(
          new List<LogEntry>
          {
            RemoveFromQueueExpectedEntry(mat3, 8, "AAAA", 1, 40 - 20, "", start.AddMinutes(40), "operyy"),
          }
        );
      expectedLogs.Add(
        RemoveFromQueueExpectedEntry(mat3, 8, "AAAA", 1, 40 - 20, "", start.AddMinutes(40), "operyy")
      );

      _jobLog
        .GetMaterialInAllQueues()
        .ShouldBeEquivalentTo(
          new List<QueuedMaterial>
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
              Paths = ImmutableDictionary<int, int>.Empty.Add(1, 50).Add(2, 52),
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
              Paths = ImmutableDictionary<int, int>.Empty,
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
              Paths = ImmutableDictionary<int, int>.Empty,
            },
          }
        );
      _jobLog.IsMaterialInQueue(mat3.MaterialID).ShouldBeFalse();
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).ShouldBe(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).ShouldBe(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).ShouldBe(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).ShouldBeNull();

      //add back in with matid only
      _jobLog
        .RecordAddMaterialToQueue(mat3.MaterialID, mat3.Process, "AAAA", 2, null, null, start.AddMinutes(45))
        .ShouldBeEquivalentTo(
          new List<LogEntry> { AddToQueueExpectedEntry(mat3, 9, "AAAA", 2, start.AddMinutes(45)) }
        );
      expectedLogs.Add(AddToQueueExpectedEntry(mat3, 9, "AAAA", 2, start.AddMinutes(45)));

      _jobLog
        .GetMaterialInAllQueues()
        .ShouldBeEquivalentTo(
          new List<QueuedMaterial>
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
              Paths = ImmutableDictionary<int, int>.Empty.Add(1, 50).Add(2, 52),
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
              Paths = ImmutableDictionary<int, int>.Empty,
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
              Paths = ImmutableDictionary<int, int>.Empty,
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
              Paths = ImmutableDictionary<int, int>.Empty,
            },
          }
        );
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).ShouldBe(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).ShouldBe(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).ShouldBe(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).ShouldBeNull();

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
        .ShouldBeEquivalentTo(
          new List<LogEntry>
          {
            RemoveFromQueueExpectedEntry(mat1, 10, "AAAA", 0, 50, "MovingInQueue", start.AddMinutes(50)),
            AddToQueueExpectedEntry(mat1, 11, "AAAA", 1, start.AddMinutes(50)),
          }
        );
      expectedLogs.Add(
        RemoveFromQueueExpectedEntry(mat1, 10, "AAAA", 0, 50, "MovingInQueue", start.AddMinutes(50))
      );
      expectedLogs.Add(AddToQueueExpectedEntry(mat1, 11, "AAAA", 1, start.AddMinutes(50)));

      _jobLog
        .GetMaterialInAllQueues()
        .ShouldBeEquivalentTo(
          new List<QueuedMaterial>
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
              Paths = ImmutableDictionary<int, int>.Empty,
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
              Paths = ImmutableDictionary<int, int>.Empty.Add(1, 50).Add(2, 52),
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
              Paths = ImmutableDictionary<int, int>.Empty,
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
              Paths = ImmutableDictionary<int, int>.Empty,
            },
          }
        );
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).ShouldBe(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).ShouldBe(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).ShouldBe(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).ShouldBeNull();

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
        .ShouldBeEquivalentTo(
          new List<LogEntry>
          {
            RemoveFromQueueExpectedEntry(mat3, 12, "AAAA", 2, 55 - 45, "MovingInQueue", start.AddMinutes(55)),
            AddToQueueExpectedEntry(mat3, 13, "AAAA", 1, start.AddMinutes(55)),
          }
        );
      expectedLogs.Add(
        RemoveFromQueueExpectedEntry(mat3, 12, "AAAA", 2, 55 - 45, "MovingInQueue", start.AddMinutes(55))
      );
      expectedLogs.Add(AddToQueueExpectedEntry(mat3, 13, "AAAA", 1, start.AddMinutes(55)));

      _jobLog
        .GetMaterialInAllQueues()
        .ShouldBeEquivalentTo(
          new List<QueuedMaterial>
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
              Paths = ImmutableDictionary<int, int>.Empty,
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
              Paths = ImmutableDictionary<int, int>.Empty,
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
              Paths = ImmutableDictionary<int, int>.Empty.Add(1, 50).Add(2, 52),
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
              Paths = ImmutableDictionary<int, int>.Empty,
            },
          }
        );
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).ShouldBe(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).ShouldBe(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).ShouldBe(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).ShouldBeNull();

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
        .ShouldBeEquivalentTo(
          new List<LogEntry> { AddToQueueExpectedEntry(mat4, 14, "AAAA", 3, start.AddMinutes(58)) }
        );
      expectedLogs.Add(AddToQueueExpectedEntry(mat4, 14, "AAAA", 3, start.AddMinutes(58)));

      _jobLog
        .GetMaterialInAllQueues()
        .ShouldBeEquivalentTo(
          new List<QueuedMaterial>
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
              Paths = ImmutableDictionary<int, int>.Empty,
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
              Paths = ImmutableDictionary<int, int>.Empty,
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
              Paths = ImmutableDictionary<int, int>.Empty.Add(1, 50).Add(2, 52),
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
              Paths = ImmutableDictionary<int, int>.Empty,
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
              Paths = ImmutableDictionary<int, int>.Empty,
            },
          }
        );
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).ShouldBe(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).ShouldBe(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).ShouldBe(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).ShouldBe(5);

      _jobLog
        .SignalMaterialForQuarantine(
          EventLogMaterial.FromLogMat(mat1),
          1,
          "QQQ",
          "theoper",
          reason: "a reason",
          timeUTC: start.AddMinutes(59)
        )
        .ShouldBeEquivalentTo(
          SignalQuarantineExpectedEntry(mat1, 15, 1, "QQQ", start.AddMinutes(59), "theoper", "a reason")
        );
      expectedLogs.Add(
        SignalQuarantineExpectedEntry(mat1, 15, 1, "QQQ", start.AddMinutes(59), "theoper", "a reason")
      );

      // hasn't moved yet
      _jobLog
        .GetMaterialInAllQueues()
        .ShouldBeEquivalentTo(
          new List<QueuedMaterial>
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
              Paths = ImmutableDictionary<int, int>.Empty,
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
              Paths = ImmutableDictionary<int, int>.Empty,
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
              Paths = ImmutableDictionary<int, int>.Empty.Add(1, 50).Add(2, 52),
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
              Paths = ImmutableDictionary<int, int>.Empty,
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
              Paths = ImmutableDictionary<int, int>.Empty,
            },
          }
        );
      _jobLog.GetMaterialInQueueByUnique("QQQ", "uniq1").ShouldBeEmpty();
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).ShouldBe(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).ShouldBe(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).ShouldBe(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).ShouldBe(5);

      //removing from queue with matid
      var mat2proc8 = mat2 with
      {
        Process = 1,
      };
      _jobLog
        .RecordRemoveMaterialFromAllQueues(mat2.MaterialID, 1, null, start.AddMinutes(60))
        .ShouldBeEquivalentTo(
          new List<LogEntry>
          {
            RemoveFromQueueExpectedEntry(mat2proc8, 16, "AAAA", 0, 60 - 10, "", start.AddMinutes(60)),
          }
        );
      expectedLogs.Add(
        RemoveFromQueueExpectedEntry(mat2proc8, 16, "AAAA", 0, 60 - 10, "", start.AddMinutes(60))
      );

      _jobLog
        .GetMaterialInAllQueues()
        .ShouldBeEquivalentTo(
          new List<QueuedMaterial>
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
              Paths = ImmutableDictionary<int, int>.Empty,
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
              Paths = ImmutableDictionary<int, int>.Empty.Add(1, 50).Add(2, 52),
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
              Paths = ImmutableDictionary<int, int>.Empty,
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
              Paths = ImmutableDictionary<int, int>.Empty,
            },
          }
        );
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).ShouldBe(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).ShouldBe(2);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).ShouldBe(4);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).ShouldBe(5);

      _jobLog.GetLogEntries(start, DateTime.UtcNow).EventsShouldBe(expectedLogs);
    }

    [Test]
    public void LoadUnloadIntoQueues()
    {
      using var _jobLog = _repoCfg.OpenConnection();
      var start = DateTime.UtcNow.AddHours(-10);
      var expectedLogs = new List<LogEntry>();

      var mat1 = MkLogMat.Mk(1, "uniq1", 15, "part111", 19, "", "", "");
      _jobLog.CreateMaterialID(1, "uniq1", "part111", 19);
      var mat2 = MkLogMat.Mk(2, "uniq2", 1, "part2", 22, "", "", "");
      _jobLog.CreateMaterialID(2, "uniq2", "part2", 22);
      var mat3 = MkLogMat.Mk(3, "uniq3", 3, "part3", 36, "", "", "");
      _jobLog.CreateMaterialID(3, "uniq3", "part3", 36);
      var mat4 = MkLogMat.Mk(4, "uniq4", 4, "part4", 47, "", "", "");
      _jobLog.CreateMaterialID(4, "uniq4", "part4", 47);

      // add two material into queue 1
      _jobLog.RecordAddMaterialToQueue(
        new EventLogMaterial()
        {
          MaterialID = mat1.MaterialID,
          Process = 14,
          Face = 0,
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
          Face = 0,
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
        .ShouldBeEquivalentTo(
          new List<QueuedMaterial>
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
              Paths = ImmutableDictionary<int, int>.Empty,
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
              Paths = ImmutableDictionary<int, int>.Empty,
            },
          }
        );

      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).ShouldBe(15);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).ShouldBe(1);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).ShouldBeNull();
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).ShouldBeNull();

      // loading should remove from queue
      var loadEndActual = _jobLog.RecordLoadUnloadComplete(
        toLoad: new[]
        {
          new MaterialToLoadOntoFace()
          {
            FaceNum = 1234,
            Process = mat1.Process,
            Path = null,
            ActiveOperationTime = TimeSpan.FromMinutes(20),
            MaterialIDs = ImmutableList.Create(mat1.MaterialID),
          },
        },
        previouslyLoaded: null,
        previouslyUnloaded: null,
        toUnload: null,
        pallet: 1,
        lulNum: 16,
        totalElapsed: TimeSpan.FromMinutes(10),
        timeUTC: start.AddMinutes(10),
        externalQueues: null
      );
      loadEndActual.EventsShouldBe(
        new List<LogEntry>
        {
          new LogEntry()
          {
            Counter = 3,
            Material = [mat1 with { Face = 1234 }],
            Pallet = 1,
            LogType = LogType.PalletCycle,
            LocationName = "Pallet Cycle",
            LocationNum = 1,
            Program = "",
            Result = "PalletCycle",
            StartOfCycle = true,
            EndTimeUTC = start.AddMinutes(10),
            ElapsedTime = TimeSpan.Zero,
            ActiveOperationTime = TimeSpan.Zero,
          },
          new LogEntry(
            5,
            new[] { mat1 with { Face = 1234 } },
            1,
            LogType.LoadUnloadCycle,
            "L/U",
            16,
            "LOAD",
            false,
            start.AddMinutes(10).AddSeconds(1),
            "LOAD",
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(20)
          ),
          RemoveFromQueueExpectedEntry(
            SetProcInMat(mat1.Process - 1)(mat1),
            4,
            "AAAA",
            0,
            10,
            "LoadedToPallet",
            start.AddMinutes(10)
          ),
        }
      );
      expectedLogs.AddRange(loadEndActual);

      _jobLog
        .GetMaterialInAllQueues()
        .ShouldBeEquivalentTo(
          new List<QueuedMaterial>
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
              Paths = ImmutableDictionary<int, int>.Empty,
            },
          }
        );

      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).ShouldBe(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).ShouldBe(1);

      //unloading should add to queue
      var unloadEndActual = _jobLog.RecordLoadUnloadComplete(
        toLoad: null,
        toUnload:
        [
          new MaterialToUnloadFromFace()
          {
            MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>
              .Empty.Add(mat1.MaterialID, new UnloadDestination() { Queue = "AAAA" })
              .Add(mat3.MaterialID, new UnloadDestination() { Queue = "AAAA" })
              .Add(mat4.MaterialID, null),
            FaceNum = mat1.Face,
            Process = mat1.Process,
            ActiveOperationTime = TimeSpan.FromMinutes(23),
          },
        ],
        previouslyLoaded: null,
        previouslyUnloaded: null,
        pallet: 5,
        lulNum: 77,
        timeUTC: start.AddMinutes(30),
        totalElapsed: TimeSpan.FromMinutes(52),
        externalQueues: null
      );
      unloadEndActual.EventsShouldBe(
        new List<LogEntry>
        {
          new LogEntry(
            8,
            new[] { mat1, mat3 with { Process = mat1.Process }, mat4 with { Process = mat1.Process } },
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
          AddToQueueExpectedEntry(mat1, 6, "AAAA", 1, start.AddMinutes(30), reason: "Unloaded"),
          AddToQueueExpectedEntry(
            mat3 with
            {
              Process = mat1.Process,
            },
            7,
            "AAAA",
            2,
            start.AddMinutes(30),
            reason: "Unloaded"
          ),
          new LogEntry()
          {
            Counter = 9,
            LogType = LogType.PalletCycle,
            Material = [mat1, mat3 with { Process = mat1.Process }, mat4 with { Process = mat1.Process }],
            Pallet = 5,
            LocationName = "Pallet Cycle",
            LocationNum = 1,
            Program = "",
            Result = "PalletCycle",
            StartOfCycle = false,
            EndTimeUTC = start.AddMinutes(30),
            ElapsedTime = TimeSpan.Zero,
            ActiveOperationTime = TimeSpan.Zero,
          },
        }
      );
      expectedLogs.AddRange(unloadEndActual);

      _jobLog
        .GetMaterialInAllQueues()
        .ShouldBeEquivalentTo(
          new List<QueuedMaterial>
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
              Paths = ImmutableDictionary<int, int>.Empty,
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
              Paths = ImmutableDictionary<int, int>.Empty,
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
              NextProcess = mat1.Process + 1,
              Paths = ImmutableDictionary<int, int>.Empty,
            },
          }
        );

      _jobLog.GetLogEntries(start, DateTime.UtcNow).EventsShouldBe(expectedLogs);

      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).ShouldBe(16);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).ShouldBe(1); // unchanged, wasn't unloaded
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).ShouldBe(16);
      _jobLog.NextProcessForQueuedMaterial(mat4.MaterialID).ShouldBe(16);
    }

    [Test]
    [Arguments(true, true, true)]
    [Arguments(true, true, false)]
    [Arguments(true, false, true)]
    [Arguments(true, false, false)]
    [Arguments(false, true, true)]
    [Arguments(false, true, false)]
    [Arguments(false, false, true)]
    [Arguments(false, false, false)]
    public void BulkAddRemoveCastings(bool useSerial, bool existingMats, bool useWorkorder)
    {
      using var _jobLog = _repoCfg.OpenConnection();
      var addTime = DateTime.UtcNow.AddHours(-2);
      var workorder = useWorkorder ? "TheWork" : "";

      long matOffset = 0;
      int posOffset = 0;

      if (existingMats)
      {
        matOffset = 1;
        posOffset = 1;
        _jobLog.AllocateMaterialIDForCasting("castingQ").ShouldBe(1);
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
        workorder: workorder,
        reason: "TheReason",
        timeUTC: addTime
      );

      matRet
        .MaterialIds.OrderBy(m => m)
        .ToList()
        .ShouldBeEquivalentTo(Enumerable.Range(1, 5).Select(i => matOffset + i).ToList());

      var expectedLogs = Enumerable
        .Range(1, 5)
        .Select(i =>
        {
          var l = new LogEntry(
            cntr: -1,
            mat: new[]
            {
              MkLogMat.Mk(
                matID: matOffset + i,
                uniq: "",
                proc: 0,
                part: "castingQ",
                numProc: 1,
                serial: useSerial ? i.ToString() : "",
                workorder: workorder,
                face: ""
              ),
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
          l = l with
          {
            ProgramDetails = (l.ProgramDetails ?? ImmutableDictionary<string, string>.Empty).Add(
              "operator",
              "operName"
            ),
          };
          return l;
        })
        .ToList();

      if (useSerial)
      {
        expectedLogs.AddRange(
          Enumerable
            .Range(1, 5)
            .Select(i => new LogEntry(
              cntr: -1,
              mat: new[]
              {
                MkLogMat.Mk(
                  matID: matOffset + i,
                  uniq: "",
                  proc: 0,
                  part: "castingQ",
                  numProc: 1,
                  serial: useSerial ? i.ToString() : "",
                  workorder: workorder,
                  face: ""
                ),
              },
              pal: 0,
              ty: LogType.PartMark,
              locName: "Mark",
              locNum: 1,
              prog: "MARK",
              start: false,
              endTime: addTime,
              result: i.ToString()
            ))
        );
      }

      if (useWorkorder)
      {
        expectedLogs.AddRange(
          Enumerable
            .Range(1, 5)
            .Select(i => new LogEntry(
              cntr: -1,
              mat: new[]
              {
                MkLogMat.Mk(
                  matID: matOffset + i,
                  uniq: "",
                  proc: 0,
                  part: "castingQ",
                  numProc: 1,
                  serial: useSerial ? i.ToString() : "",
                  workorder: workorder,
                  face: ""
                ),
              },
              pal: 0,
              ty: LogType.OrderAssignment,
              locName: "Order",
              locNum: 1,
              prog: "",
              start: false,
              endTime: addTime,
              result: workorder
            ))
        );
      }

      matRet.Logs.EventsShouldBe(expectedLogs);

      _jobLog
        .GetRecentLog(-1)
        .EventsShouldBe(
          expectedLogs.Concat(
            existingMats
              ? new[]
              {
                new LogEntry(
                  cntr: -1,
                  mat: new[]
                  {
                    MkLogMat.Mk(
                      matID: 1,
                      uniq: "",
                      proc: 0,
                      part: "castingQ",
                      numProc: 1,
                      serial: "",
                      workorder: "",
                      face: ""
                    ),
                  },
                  pal: 0,
                  ty: LogType.AddToQueue,
                  locName: "queueQQ",
                  locNum: 0,
                  prog: "",
                  start: false,
                  endTime: addTime,
                  result: ""
                ),
              }
              : Enumerable.Empty<LogEntry>()
          )
        );

      _jobLog
        .GetMaterialInAllQueues()
        .ShouldBeEquivalentTo(
          Enumerable
            .Range(1, existingMats ? 6 : 5)
            .Select(i => new QueuedMaterial()
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
              Workorder =
                existingMats && i == 1 ? null
                : useWorkorder ? workorder
                : null,
              Paths = ImmutableDictionary<int, int>.Empty,
              AddTimeUTC = addTime,
            })
            .ToList()
        );

      _jobLog
        .GetUnallocatedMaterialInQueue("queueQQ", "castingQ")
        .ShouldBeEquivalentTo(
          Enumerable
            .Range(1, existingMats ? 6 : 5)
            .Select(i => new QueuedMaterial()
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
              Workorder =
                existingMats && i == 1 ? null
                : useWorkorder ? workorder
                : null,
              Paths = ImmutableDictionary<int, int>.Empty,
              AddTimeUTC = addTime,
            })
            .ToList()
        );
      _jobLog.GetUnallocatedMaterialInQueue("ohuouh", "castingQ").ShouldBeEmpty();
      _jobLog.GetUnallocatedMaterialInQueue("queueQQ", "qouhwef").ShouldBeEmpty();

      var removeTime = DateTime.UtcNow.AddHours(-1);

      _jobLog
        .BulkRemoveMaterialFromAllQueues(new long[] { 1, 2 }, null, reason: "reason11", removeTime)
        .EventsShouldBe(
          (new long[] { 1, 2 }).Select(matId => new LogEntry(
            cntr: -1,
            mat: new[]
            {
              MkLogMat.Mk(
                matID: matId,
                uniq: "",
                proc: 0,
                part: "castingQ",
                numProc: 1,
                serial: useSerial ? (existingMats ? (matId == 2 ? "1" : "") : matId.ToString()) : "",
                workorder: existingMats && matId == 1 ? "" : workorder,
                face: ""
              ),
            },
            pal: 0,
            ty: LogType.RemoveFromQueue,
            locName: "queueQQ",
            locNum: 0,
            prog: "reason11",
            start: false,
            endTime: removeTime,
            result: "",
            elapsed: removeTime.Subtract(addTime),
            active: TimeSpan.Zero
          ))
        );

      _jobLog
        .GetMaterialInAllQueues()
        .ShouldBeEquivalentTo(
          Enumerable
            .Range(3, existingMats ? 4 : 3)
            .Select(i => new QueuedMaterial()
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
              Workorder =
                existingMats && i == 1 ? null
                : useWorkorder ? workorder
                : null,
              Paths = ImmutableDictionary<int, int>.Empty,
              AddTimeUTC = addTime,
            })
            .ToList()
        );
    }

    [Test]
    public void ReuseMatIDsWhenBulkAdding()
    {
      using var _jobLog = _repoCfg.OpenConnection();
      _jobLog
        .BulkAddNewCastingsInQueue(
          casting: "castingQ",
          qty: 2,
          queue: "queueQQ",
          serials: new[] { "1", "2" },
          workorder: "work1",
          operatorName: "theoper"
        )
        .MaterialIds.OrderBy(m => m)
        .ToArray()
        .ShouldBeEquivalentTo(new long[] { 1, 2 });

      _jobLog.GetMaterialDetails(1).PartName.ShouldBe("castingQ");
      _jobLog.GetMaterialDetails(1).Workorder.ShouldBe("work1");

      //adding again should throw, since they are in the queue
      Should
        .Throw<Exception>(() =>
          _jobLog.BulkAddNewCastingsInQueue(
            casting: "castingQ",
            qty: 2,
            queue: "queueQQ",
            serials: new[] { "1", "2" },
            workorder: "work5455",
            operatorName: "theoper",
            throwOnExistingSerial: true
          )
        )
        .Message.ShouldBe("Serial 1 already exists in the database with MaterialID 1");

      // adding without throwing should create new material ids
      _jobLog
        .BulkAddNewCastingsInQueue(
          casting: "castingQ",
          qty: 2,
          queue: "queueQQ",
          serials: new[] { "1", "2" },
          workorder: "work2",
          operatorName: "theoper",
          throwOnExistingSerial: false
        )
        .MaterialIds.OrderBy(m => m)
        .ToArray()
        .ShouldBeEquivalentTo(new long[] { 3, 4 });

      // now try with a load and machine event
      _jobLog
        .BulkAddNewCastingsInQueue(
          casting: "castingQ",
          qty: 2,
          queue: "queueQQ",
          serials: new[] { "5", "6" },
          workorder: null,
          operatorName: "theoper"
        )
        .MaterialIds.OrderBy(m => m)
        .ToArray()
        .ShouldBeEquivalentTo(new long[] { 5, 6 });

      _jobLog.RecordLoadUnloadComplete(
        toLoad:
        [
          new MaterialToLoadOntoFace()
          {
            FaceNum = 1,
            Process = 1,
            Path = 1,
            ActiveOperationTime = TimeSpan.FromMinutes(2),
            MaterialIDs = ImmutableList.Create(5L),
          },
        ],
        previouslyLoaded: null,
        previouslyUnloaded: null,
        toUnload: null,
        pallet: 5,
        lulNum: 1,
        timeUTC: DateTime.UtcNow,
        totalElapsed: TimeSpan.FromMinutes(2),
        externalQueues: null
      );

      _jobLog.RecordRemoveMaterialFromAllQueues(matID: 6L, process: 0);

      _jobLog.RecordMachineStart(
        new[]
        {
          new EventLogMaterial()
          {
            MaterialID = 6,
            Process = 1,
            Face = 12,
          },
        },
        pallet: 4,
        statName: "MC",
        statNum: 2,
        program: "prog",
        timeUTC: DateTime.UtcNow
      );

      //adding again should throw, since 5 has a load event
      Should
        .Throw<Exception>(() =>
          _jobLog.BulkAddNewCastingsInQueue(
            casting: "castingQ",
            qty: 1,
            queue: "queueQQ",
            serials: new[] { "5" },
            workorder: null,
            operatorName: "theoper",
            throwOnExistingSerial: true
          )
        )
        .Message.ShouldBe("Serial 5 already exists in the database with MaterialID 5");

      //adding again should throw, since 6 has a machine event
      Should
        .Throw<Exception>(() =>
          _jobLog.BulkAddNewCastingsInQueue(
            casting: "castingQ",
            qty: 1,
            queue: "queueQQ",
            serials: new[] { "6" },
            workorder: "work4",
            operatorName: "theoper",
            throwOnExistingSerial: true
          )
        )
        .Message.ShouldBe("Serial 6 already exists in the database with MaterialID 6");

      // adding without throwing should create new
      _jobLog
        .BulkAddNewCastingsInQueue(
          casting: "casting22",
          qty: 2,
          queue: "queueQQ",
          serials: new[] { "5", "6" },
          workorder: "work77",
          operatorName: "theoper",
          throwOnExistingSerial: false
        )
        .MaterialIds.OrderBy(m => m)
        .ToArray()
        .ShouldBeEquivalentTo(new long[] { 7, 8 });
      _jobLog.GetMaterialDetails(7).PartName.ShouldBe("casting22");
      _jobLog.GetMaterialDetails(7).Workorder.ShouldBe("work77");

      // now adding with no load/machine and not in a queue should reuse
      _jobLog
        .BulkAddNewCastingsInQueue(
          casting: "castingQ",
          qty: 2,
          queue: "queueQQ",
          serials: new[] { "9", "10" },
          workorder: "work12",
          operatorName: "theoper"
        )
        .MaterialIds.OrderBy(m => m)
        .ToArray()
        .ShouldBeEquivalentTo(new long[] { 9, 10 });

      _jobLog.GetMaterialDetails(9).PartName.ShouldBe("castingQ");
      _jobLog.GetMaterialDetails(9).Workorder.ShouldBe("work12");
      _jobLog.BulkRemoveMaterialFromAllQueues(new long[] { 9, 10 });

      // adding serial 9 should be reused
      _jobLog
        .BulkAddNewCastingsInQueue(
          casting: "casting44",
          qty: 1,
          queue: "queueQQ",
          serials: new[] { "9" },
          workorder: "updatedwork",
          operatorName: "theoper",
          throwOnExistingSerial: true
        )
        .MaterialIds.OrderBy(m => m)
        .ToArray()
        .ShouldBeEquivalentTo(new long[] { 9 });

      // the casting should have been updated too
      _jobLog.GetMaterialDetails(9).PartName.ShouldBe("casting44");
      _jobLog.GetMaterialDetails(9).Workorder.ShouldBe("updatedwork");
    }

    [Test]
    public void AllocateCastingsFromQueues()
    {
      using var _jobLog = _repoCfg.OpenConnection();
      var mat1 = MkLogMat.Mk(
        _jobLog.AllocateMaterialIDForCasting("casting1"),
        "",
        0,
        "casting1",
        1,
        "",
        "",
        ""
      );
      var mat2 = MkLogMat.Mk(
        _jobLog.AllocateMaterialIDForCasting("casting1"),
        "",
        0,
        "casting1",
        1,
        "",
        "",
        ""
      );
      var mat3 = MkLogMat.Mk(
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
        .ShouldBeEquivalentTo(
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
        .ShouldBeEquivalentTo(
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
        .ShouldBeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = mat3.MaterialID,
            JobUnique = null,
            PartName = "casting3",
            NumProcesses = 1,
          }
        );

      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).ShouldBe(1);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).ShouldBe(1);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).ShouldBe(1);

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
        .ShouldBeEmpty();

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
        .ShouldBeEmpty();

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
        .ShouldBe(new[] { mat1.MaterialID, mat2.MaterialID });

      _jobLog
        .GetMaterialDetails(mat1.MaterialID)
        .ShouldBeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = mat1.MaterialID,
            JobUnique = "uniqAAA",
            PartName = "part1",
            NumProcesses = 6312,
            Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1234),
          }
        );

      _jobLog
        .GetMaterialDetails(mat2.MaterialID)
        .ShouldBeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = mat2.MaterialID,
            JobUnique = "uniqAAA",
            PartName = "part1",
            NumProcesses = 6312,
            Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1234),
          }
        );

      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).ShouldBe(1);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).ShouldBe(1);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).ShouldBe(1);

      _jobLog.MarkCastingsAsUnallocated(new[] { mat1.MaterialID }, casting: "newcasting");

      _jobLog
        .GetMaterialDetails(mat1.MaterialID)
        .ShouldBeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = mat1.MaterialID,
            JobUnique = null,
            PartName = "newcasting",
            NumProcesses = 6312,
          }
        );

      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).ShouldBe(1);
      _jobLog.NextProcessForQueuedMaterial(mat2.MaterialID).ShouldBe(1);
      _jobLog.NextProcessForQueuedMaterial(mat3.MaterialID).ShouldBe(1);
    }

    [Test]
    [Arguments(true, true, null)]
    [Arguments(true, true, "thecasting")]
    [Arguments(false, true, null)]
    [Arguments(false, true, "thecasting")]
    [Arguments(true, false, null)]
    [Arguments(false, false, null)]
    public void OverrideMatOnPal(bool firstPalletCycle, bool newMatUnassigned, string rawMatName)
    {
      using var _jobLog = _repoCfg.OpenConnection();
      var now = DateTime.UtcNow.AddHours(-5);

      if (!firstPalletCycle)
      {
        var firstMatId = _jobLog.AllocateMaterialID("uniq1", "part1", 2);
        var firstMat = new EventLogMaterial()
        {
          MaterialID = firstMatId,
          Process = 1,
          Face = 12,
        };
        _jobLog.RecordSerialForMaterialID(firstMat, "aaaa", now);
        _jobLog.RecordLoadUnloadComplete(
          toLoad:
          [
            new MaterialToLoadOntoFace()
            {
              FaceNum = 12,
              Process = firstMat.Process,
              Path = null,
              ActiveOperationTime = TimeSpan.FromMinutes(4),
              MaterialIDs = ImmutableList.Create(firstMat.MaterialID),
            },
          ],
          previouslyLoaded: null,
          previouslyUnloaded: null,
          toUnload: null,
          lulNum: 3,
          pallet: 5,
          timeUTC: now.AddMinutes(1),
          totalElapsed: TimeSpan.FromMinutes(5),
          externalQueues: null
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

        now = now.AddMinutes(5).AddSeconds(1);
      }

      // ------------------------------------------------------
      // Material
      // ------------------------------------------------------
      var initiallyLoadedMatProc0 = new EventLogMaterial()
      {
        MaterialID = _jobLog.AllocateMaterialID("uniq1", "part1", 2),
        Process = 0,
        Face = 0,
      };
      var initiallyLoadedMatProc1 = new EventLogMaterial()
      {
        MaterialID = initiallyLoadedMatProc0.MaterialID,
        Process = 1,
        Face = 1,
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
      _jobLog.NextProcessForQueuedMaterial(initiallyLoadedMatProc0.MaterialID).ShouldBe(1);

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
                      Paths = ImmutableList.Create(EmptyPath with { Casting = rawMatName }),
                    }
                  ),
                  RouteStartUTC = DateTime.MinValue,
                  RouteEndUTC = DateTime.MinValue,
                  Archived = false,
                }
              ),
              ScheduleId = "anotherSchId",
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
        Face = 0,
      };
      var newMatProc1 = new EventLogMaterial()
      {
        MaterialID = newMatId,
        Process = 1,
        Face = 1,
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
      _jobLog.NextProcessForQueuedMaterial(newMatProc0.MaterialID).ShouldBe(1);

      now = now.AddMinutes(1);

      // ------------------------------------------------------
      // Original Events
      // ------------------------------------------------------

      var origLog = new List<LogEntry>();

      var loadEndOrigEvts = _jobLog.RecordLoadUnloadComplete(
        toLoad:
        [
          new MaterialToLoadOntoFace()
          {
            FaceNum = 1,
            Process = initiallyLoadedMatProc1.Process,
            Path = null,
            ActiveOperationTime = TimeSpan.FromMinutes(5),
            MaterialIDs = ImmutableList.Create(initiallyLoadedMatProc1.MaterialID),
          },
        ],
        previouslyLoaded: null,
        previouslyUnloaded: null,
        toUnload: null,
        lulNum: 2,
        totalElapsed: TimeSpan.FromMinutes(4),
        pallet: 5,
        timeUTC: now,
        externalQueues: null
      );
      loadEndOrigEvts.Count().ShouldBe(3);
      loadEndOrigEvts.First().LogType.ShouldBe(LogType.PalletCycle);
      loadEndOrigEvts.Skip(1).First().LogType.ShouldBe(LogType.RemoveFromQueue);
      loadEndOrigEvts
        .Skip(1)
        .First()
        .Material.First()
        .MaterialID.ShouldBe(initiallyLoadedMatProc1.MaterialID);
      loadEndOrigEvts.Skip(1).First().Material.First().Process.ShouldBe(0);
      loadEndOrigEvts.Last().LogType.ShouldBe(LogType.LoadUnloadCycle);
      origLog.AddRange(loadEndOrigEvts.Where(e => e.LogType != LogType.RemoveFromQueue));

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
        .ShouldBeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = initiallyLoadedMatProc0.MaterialID,
            JobUnique = newMatUnassigned ? null : "uniq1",
            PartName = newMatUnassigned ? (rawMatName ?? "part1") : "part1",
            NumProcesses = 2,
            Workorder = null,
            Serial = "bbbb",
            Paths = newMatUnassigned ? null : ImmutableDictionary<int, int>.Empty.Add(1, 5),
          }
        );

      _jobLog
        .GetMaterialDetails(newMatId)
        .ShouldBeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = newMatId,
            JobUnique = "uniq1",
            PartName = "part1",
            NumProcesses = 2,
            Workorder = null,
            Serial = "cccc",
            Paths = ImmutableDictionary<int, int>.Empty.Add(1, 5),
          }
        );

      // ------------------------------------------------------
      // Check Logs
      // ------------------------------------------------------

      var initiallyLoadedLogMatProc0 = MkLogMat.Mk(
        matID: initiallyLoadedMatProc0.MaterialID,
        uniq: newMatUnassigned ? "" : "uniq1",
        part: rawMatName ?? "part1",
        proc: 0,
        numProc: 2,
        serial: "bbbb",
        workorder: "",
        face: ""
      );
      var newLogMatProc0 = MkLogMat.Mk(
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
        mat: new[]
        {
          initiallyLoadedLogMatProc0 with
          {
            Process = 1,
            Path = newMatUnassigned ? null : 5,
          },
          newLogMatProc0 with
          {
            Process = 1,
            Path = 5,
          },
        },
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
            mat => new LogMaterial()
            {
              MaterialID = newMatProc1.MaterialID,
              JobUniqueStr = "uniq1",
              Process = 1,
              Path = 5,
              PartName = "part1",
              NumProcesses = 2,
              Serial = "cccc",
              Workorder = "",
              Face = 1,
            }
          )
        )
        .ToList();

      result.ChangedLogEntries.EventsShouldBe(newLog);

      result.NewLogEntries.EventsShouldBe(
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
            reason: "SwapMaterial",
            timeUTC: now,
            operName: "theoper"
          ),
        }
      );

      _jobLog.NextProcessForQueuedMaterial(initiallyLoadedLogMatProc0.MaterialID).ShouldBe(1);

      _jobLog
        .GetLogForMaterial(initiallyLoadedMatProc0.MaterialID)
        .EventsShouldBe(
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
              reason: "LoadedToPallet",
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
          }
        );

      // log for newMat matches
      _jobLog
        .GetLogForMaterial(newMatProc1.MaterialID)
        .EventsShouldBe(
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
                reason: "SwapMaterial",
                operName: "theoper"
              ),
            }
          )
        );

      _jobLog
        .GetLogForMaterial(newMatProc1.MaterialID)
        .Where(e =>
          e.LogType != LogType.MachineCycle
          && e.LogType != LogType.LoadUnloadCycle
          && e.LogType != LogType.PalletInStocker
          && e.LogType != LogType.PalletOnRotaryInbound
          && e.LogType != LogType.SwapMaterialOnPallet
          && e.LogType != LogType.PalletCycle
        )
        .SelectMany(e => e.Material)
        .Select(m => m.Process)
        .Max()
        .ShouldBe(0);
    }

    [Test]
    public void ErrorsOnBadOverrideMatOnPal()
    {
      using var _jobLog = _repoCfg.OpenConnection();
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
          ScheduleId = "aschId",
        },
        null,
        true
      );

      var firstMatId = _jobLog.AllocateMaterialID("uniq1", "part1", 2);
      var firstMatProc0 = new EventLogMaterial()
      {
        MaterialID = firstMatId,
        Process = 0,
        Face = 0,
      };
      var firstMat = new EventLogMaterial()
      {
        MaterialID = firstMatId,
        Process = 1,
        Face = 1,
      };
      _jobLog.RecordSerialForMaterialID(firstMatProc0, serial: "aaaa", timeUTC: now);
      _jobLog.RecordLoadUnloadComplete(
        toLoad:
        [
          new MaterialToLoadOntoFace()
          {
            FaceNum = 12,
            Process = firstMat.Process,
            Path = null,
            ActiveOperationTime = TimeSpan.FromMinutes(4),
            MaterialIDs = ImmutableList.Create(firstMat.MaterialID),
          },
        ],
        previouslyLoaded: null,
        previouslyUnloaded: null,
        toUnload: null,
        lulNum: 3,
        totalElapsed: TimeSpan.FromMinutes(4),
        pallet: 5,
        timeUTC: now.AddMinutes(1),
        externalQueues: null
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

      Should
        .Throw<ConflictRequestException>(() =>
          _jobLog.SwapMaterialInCurrentPalletCycle(
            pallet: 5,
            oldMatId: 12345,
            newMatId: 98765,
            operatorName: null,
            quarantineQueue: "unusedquarantine"
          )
        )
        .Message.ShouldBe("Unable to find material");

      Should
        .Throw<ConflictRequestException>(() =>
          _jobLog.SwapMaterialInCurrentPalletCycle(
            pallet: 5,
            oldMatId: firstMatId,
            newMatId: differentUniqMatId,
            operatorName: null,
            quarantineQueue: "unusedquarantine"
          )
        )
        .Message.ShouldBe("Overriding material on pallet must use material from the same job");

      var existingPathMatId = _jobLog.AllocateMaterialID("uniq1", "part1", 2);
      _jobLog.RecordPathForProcess(existingPathMatId, process: 1, path: 10);

      Should
        .Throw<ConflictRequestException>(() =>
          _jobLog.SwapMaterialInCurrentPalletCycle(
            pallet: 5,
            oldMatId: firstMatId,
            newMatId: differentUniqMatId,
            operatorName: null,
            quarantineQueue: "unusedquarantine"
          )
        )
        .Message.ShouldBe("Overriding material on pallet must use material from the same job");

      var otherCastingMatId = _jobLog.AllocateMaterialIDForCasting("othercasting");

      Should
        .Throw<ConflictRequestException>(() =>
          _jobLog.SwapMaterialInCurrentPalletCycle(
            pallet: 5,
            oldMatId: firstMatId,
            newMatId: otherCastingMatId,
            operatorName: null,
            quarantineQueue: "unusedquarantine"
          )
        )
        .Message.ShouldBe(
          "Material swap of unassigned material does not match part name or raw material name"
        );
    }

    [Test]
    public void InvalidatesCycle()
    {
      using var _jobLog = _repoCfg.OpenConnection();
      var now = DateTime.UtcNow.AddHours(-5);

      // ------------------------------------------------------
      // Material
      // ------------------------------------------------------

      var matProc0 = new EventLogMaterial()
      {
        MaterialID = _jobLog.AllocateMaterialID("uniq1", "part1", 2),
        Process = 0,
        Face = 0,
      };
      var matProc1 = new EventLogMaterial()
      {
        MaterialID = matProc0.MaterialID,
        Process = 1,
        Face = 12,
      };

      var matProc2 = new EventLogMaterial()
      {
        MaterialID = matProc0.MaterialID,
        Process = 2,
        Face = 0,
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
      _jobLog.NextProcessForQueuedMaterial(matProc0.MaterialID).ShouldBe(1);

      now = now.AddMinutes(1);

      var logMatProc0 = MkLogMat.Mk(
        matID: matProc0.MaterialID,
        uniq: "uniq1",
        part: "part1",
        proc: 0,
        numProc: 2,
        serial: "bbbb",
        workorder: "",
        face: ""
      );

      var proc0Evts = new[]
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
          reason: "LoadedToPallet",
          timeUTC: now,
          elapsedMin: now.Subtract(initialMatAddToQueueTime).TotalMinutes
        ),
      };

      // ------------------------------------------------------
      // Original Events
      // ------------------------------------------------------

      var origMatLog = new List<LogEntry>();
      var origPalLog = new List<LogEntry>();

      var loadEndOrigEvts = _jobLog.RecordLoadUnloadComplete(
        toLoad:
        [
          new MaterialToLoadOntoFace()
          {
            FaceNum = 12,
            Process = matProc1.Process,
            Path = null,
            ActiveOperationTime = TimeSpan.FromMinutes(5),
            MaterialIDs = ImmutableList.Create(matProc1.MaterialID),
          },
        ],
        previouslyLoaded: null,
        previouslyUnloaded: null,
        toUnload: null,
        pallet: 5,
        lulNum: 2,
        totalElapsed: TimeSpan.FromMinutes(4),
        timeUTC: now,
        externalQueues: null
      );
      loadEndOrigEvts.Count().ShouldBe(3);
      loadEndOrigEvts.First().LogType.ShouldBe(LogType.PalletCycle);
      loadEndOrigEvts.Skip(1).First().LogType.ShouldBe(LogType.RemoveFromQueue);
      loadEndOrigEvts.Skip(1).First().Material.First().MaterialID.ShouldBe(matProc1.MaterialID);
      loadEndOrigEvts.Skip(1).First().Material.First().Process.ShouldBe(0);
      loadEndOrigEvts.Last().LogType.ShouldBe(LogType.LoadUnloadCycle);
      origMatLog.Add(loadEndOrigEvts.Last());
      origPalLog.Add(loadEndOrigEvts.First(l => l.LogType == LogType.PalletCycle));

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

      origMatLog.Add(
        _jobLog.RecordMachineStart(
          new[] { matProc2 },
          pallet: 5,
          statName: "Mach",
          statNum: 3,
          program: "prog22",
          timeUTC: now
        )
      );

      now = now.AddMinutes(1);

      _jobLog.NextProcessForQueuedMaterial(matProc0.MaterialID).ShouldBe(3);

      // ------------------------------------------------------
      // Invalidate
      // ------------------------------------------------------

      var result = _jobLog.InvalidatePalletCycle(
        matId: matProc1.MaterialID,
        process: 1,
        operatorName: "theoper",
        timeUTC: now
      );

      // ------------------------------------------------------
      // Check Logs
      // ------------------------------------------------------

      var expectedInvalidateMsg = new LogEntry(
        cntr: 0,
        mat: [logMatProc0 with { Process = 1, Path = null }, logMatProc0 with { Process = 2, Path = null }],
        pal: 0,
        ty: LogType.InvalidateCycle,
        locName: "InvalidateCycle",
        locNum: 1,
        prog: "InvalidateCycle",
        start: false,
        endTime: now,
        result: "Invalidate all events on cycles"
      );
      expectedInvalidateMsg = expectedInvalidateMsg with
      {
        ProgramDetails = ImmutableDictionary<string, string>
          .Empty.Add("EditedCounters", string.Join(",", origMatLog.Select(e => e.Counter)))
          .Add("operator", "theoper"),
      };

      var expectedReaddToQueue = new LogEntry(
        cntr: 0,
        mat: [logMatProc0], // was process 1, adding back as process 0
        pal: 0,
        ty: LogType.AddToQueue,
        locName: "xyz",
        locNum: 0,
        prog: "Invalidating",
        start: false,
        endTime: now,
        result: ""
      );
      expectedReaddToQueue = expectedReaddToQueue with
      {
        ProgramDetails = ImmutableDictionary<string, string>.Empty.Add("operator", "theoper"),
      };

      result.EventsShouldBe(new[] { expectedInvalidateMsg, expectedReaddToQueue });

      var newMatLog = origMatLog
        .Select(RemoveActiveTime())
        .Select(evt =>
        {
          return evt with
          {
            Material = evt.Material.Select(m => m with { Path = null }).ToImmutableList(),
            ProgramDetails = (evt.ProgramDetails ?? ImmutableDictionary<string, string>.Empty).Add(
              "PalletCycleInvalidated",
              "1"
            ),
          };
        })
        .ToList();

      var newPalLog = origPalLog.Select(evt =>
        evt with
        {
          Material = evt.Material.Select(m => m with { Path = null }).ToImmutableList(),
        }
      );

      logMatProc0 = logMatProc0 with { Path = null };

      // log for initiallyLoadedMatProc matches, and importantly has only process 0 as max
      _jobLog.NextProcessForQueuedMaterial(matProc0.MaterialID).ShouldBe(1);

      _jobLog
        .GetLogForMaterial(matProc0.MaterialID)
        .EventsShouldBe(
          newMatLog
            .Concat(newPalLog)
            .Concat(proc0Evts)
            .Concat(new[] { expectedInvalidateMsg, expectedReaddToQueue })
        );
    }

    [Test]
    public void InvalidatesAndChangesToCasting()
    {
      using var db = _repoCfg.OpenConnection();
      var now = DateTime.UtcNow.AddHours(-4);

      // start with allocated to uniq1
      var matId = db.AllocateMaterialID("uniq1", "part1", numProc: 3);
      var matProc1 = new EventLogMaterial()
      {
        MaterialID = matId,
        Process = 1,
        Face = 1,
      };

      var matProc2 = new EventLogMaterial()
      {
        MaterialID = matId,
        Process = 2,
        Face = 4,
      };

      var noChangingLog = new List<LogEntry>();
      var logToInvalidate = new List<LogEntry>();

      db.RecordPathForProcess(matProc1.MaterialID, process: 1, path: 2);

      noChangingLog.AddRange(
        db.RecordSerialForMaterialID(matProc1.MaterialID, proc: 0, serial: "ser1", timeUTC: now)
      );

      logToInvalidate.Add(
        TransformAllMat(SetPathInMat(path: 2))(
          db.RecordAddMaterialToQueue(
              matProc1,
              queue: "rawmat",
              position: -1,
              operatorName: null,
              reason: null,
              timeUTC: now
            )
            .First()
        )
      );

      now = now.AddMinutes(1);

      logToInvalidate.AddRange(
        db.RecordRemoveMaterialFromAllQueues(matProc1, timeUTC: now)
          .Select(TransformAllMat(SetPathInMat(path: 2)))
      );

      noChangingLog.Add(
        TransformAllMat(SetPathInMat(path: 2))(
          db.RecordPalletArriveRotaryInbound(
            [matProc1],
            pallet: 5,
            statName: "Mach",
            statNum: 3,
            timeUTC: now.AddMinutes(1)
          )
        )
      );

      logToInvalidate.Add(
        TransformAllMat(SetPathInMat(path: 2))(
          db.RecordMachineEnd(
            [matProc1],
            pallet: 5,
            statName: "Mach",
            statNum: 3,
            program: "prog1",
            result: "prog1",
            timeUTC: now.AddMinutes(2),
            elapsed: TimeSpan.FromMinutes(10),
            active: TimeSpan.FromMinutes(11)
          )
        )
      );

      // now a few for matProc2
      db.RecordPathForProcess(matProc1.MaterialID, process: 2, path: 3);
      noChangingLog.Add(
        TransformAllMat(SetPathInMat(path: 3))(
          db.RecordPalletArriveStocker(
            [matProc2],
            pallet: 5,
            stockerNum: 5,
            timeUTC: now.AddMinutes(3),
            waitForMachine: false
          )
        )
      );

      logToInvalidate.Add(
        TransformAllMat(SetPathInMat(path: 3))(
          db.RecordMachineStart(
            [matProc2],
            pallet: 5,
            statName: "Mach",
            statNum: 3,
            program: "prog2",
            timeUTC: now.AddMinutes(4)
          )
        )
      );

      // check details
      db.GetMaterialDetails(matProc1.MaterialID)
        .ShouldBeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = matProc1.MaterialID,
            JobUnique = "uniq1",
            PartName = "part1",
            NumProcesses = 3,
            Workorder = null,
            Serial = "ser1",
            Paths = ImmutableDictionary<int, int>.Empty.Add(1, 2).Add(2, 3),
          }
        );

      db.GetLogForMaterial(matProc1.MaterialID).EventsShouldBe(noChangingLog.Concat(logToInvalidate));

      // now invalidate
      var expectedInvalidate = new LogEntry()
      {
        Counter = 0,
        Material = new[] { 1, 2 }
          .Select(proc =>
            MkLogMat.Mk(
              matID: matProc1.MaterialID,
              uniq: "",
              part: "thecasting",
              proc: proc,
              numProc: 1,
              serial: "ser1",
              workorder: "",
              face: ""
            ) with
            {
              Path = null,
            }
          )
          .ToImmutableList(),
        Pallet = 0,
        LogType = LogType.InvalidateCycle,
        LocationName = "InvalidateCycle",
        LocationNum = 1,
        Program = "InvalidateCycle",
        StartOfCycle = false,
        EndTimeUTC = now.AddMinutes(5),
        ElapsedTime = TimeSpan.FromMinutes(-1),
        ActiveOperationTime = TimeSpan.Zero,
        Result = "Invalidate all events on cycles",
        ProgramDetails = ImmutableDictionary<string, string>
          .Empty.Add("EditedCounters", string.Join(",", logToInvalidate.Select(e => e.Counter)))
          .Add("operator", "theoper"),
      };

      db.InvalidateAndChangeAssignment(
          matId: matProc1.MaterialID,
          operatorName: "theoper",
          changeJobUniqueTo: null,
          changePartNameTo: "thecasting",
          changeNumProcessesTo: 1,
          timeUTC: now.AddMinutes(5)
        )
        .EventsShouldBe([expectedInvalidate]);

      db.GetMaterialDetails(matProc1.MaterialID)
        .ShouldBeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = matProc1.MaterialID,
            JobUnique = null,
            PartName = "thecasting",
            NumProcesses = 1,
            Workorder = null,
            Serial = "ser1",
            Paths = null,
          }
        );

      db.NextProcessForQueuedMaterial(matProc1.MaterialID).ShouldBeNull();

      db.GetLogForMaterial(matProc1.MaterialID)
        .EventsShouldBe(
          noChangingLog
            .Concat(
              logToInvalidate.Select(e =>
                e with
                {
                  ActiveOperationTime = TimeSpan.Zero,
                  ProgramDetails = (e.ProgramDetails ?? ImmutableDictionary<string, string>.Empty).Add(
                    "PalletCycleInvalidated",
                    "1"
                  ),
                }
              )
            )
            .Select(e =>
              e with
              {
                Material = e
                  .Material.Select(m =>
                    m with
                    {
                      JobUniqueStr = "",
                      PartName = "thecasting",
                      NumProcesses = 1,
                      Path = null,
                    }
                  )
                  .ToImmutableList(),
              }
            )
            .Append(expectedInvalidate)
        );
    }

    [Test]
    public void InvalidatesAndChangesToJob()
    {
      using var db = _repoCfg.OpenConnection();
      var now = DateTime.UtcNow.AddHours(-3);

      var matId = db.AllocateMaterialID("uniqqq", "parttt", numProc: 3);
      var matProc0 = new EventLogMaterial()
      {
        MaterialID = matId,
        Process = 0,
        Face = 0,
      };

      var serial = db.RecordSerialForMaterialID(matProc0.MaterialID, proc: 0, serial: "ser111", timeUTC: now);

      // just added to queue, nothing else yet

      var addToQueue = db.RecordAddMaterialToQueue(
        matProc0,
        queue: "rawmat",
        position: -1,
        operatorName: null,
        reason: null,
        timeUTC: now
      );

      db.GetMaterialDetails(matId)
        .ShouldBeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = matId,
            JobUnique = "uniqqq",
            PartName = "parttt",
            NumProcesses = 3,
            Workorder = null,
            Serial = "ser111",
            Paths = null,
          }
        );

      db.NextProcessForQueuedMaterial(matId).ShouldBe(1);

      // invalidate
      var expectedInvalidate = new LogEntry()
      {
        Counter = 0,
        Material = new[]
        {
          MkLogMat.Mk(
            matID: matProc0.MaterialID,
            uniq: "ZZZuniq",
            part: "ZZZpart",
            proc: 0,
            numProc: 2,
            serial: "ser111",
            workorder: "",
            face: ""
          ),
        }.ToImmutableList(),
        Pallet = 0,
        LogType = LogType.InvalidateCycle,
        LocationName = "InvalidateCycle",
        LocationNum = 1,
        Program = "InvalidateCycle",
        StartOfCycle = false,
        EndTimeUTC = now.AddMinutes(5),
        ElapsedTime = TimeSpan.FromMinutes(-1),
        ActiveOperationTime = TimeSpan.Zero,
        Result = "Invalidate all events on cycles",
        ProgramDetails = ImmutableDictionary<string, string>
          .Empty.Add("EditedCounters", string.Join(",", addToQueue.First().Counter))
          .Add("operator", "theoper"),
      };

      var expectedReaddToQueue = new LogEntry()
      {
        Counter = 0,
        Material = new[]
        {
          MkLogMat.Mk(
            matID: matProc0.MaterialID,
            uniq: "ZZZuniq",
            part: "ZZZpart",
            proc: 0,
            numProc: 2,
            serial: "ser111",
            workorder: "",
            face: ""
          ),
        }.ToImmutableList(),
        Pallet = 0,
        LogType = LogType.AddToQueue,
        LocationName = "rawmat",
        LocationNum = 0,
        Program = "ChangedJob",
        StartOfCycle = false,
        EndTimeUTC = now.AddMinutes(5),
        ElapsedTime = TimeSpan.FromMinutes(-1),
        ActiveOperationTime = TimeSpan.Zero,
        Result = "",
        ProgramDetails = ImmutableDictionary<string, string>.Empty.Add("operator", "theoper"),
      };

      db.InvalidateAndChangeAssignment(
          matId: matProc0.MaterialID,
          operatorName: "theoper",
          changeJobUniqueTo: "ZZZuniq",
          changePartNameTo: "ZZZpart",
          changeNumProcessesTo: 2,
          timeUTC: now.AddMinutes(5)
        )
        .EventsShouldBe([expectedInvalidate, expectedReaddToQueue]);

      db.GetMaterialDetails(matId)
        .ShouldBeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = matId,
            JobUnique = "ZZZuniq",
            PartName = "ZZZpart",
            NumProcesses = 2,
            Workorder = null,
            Serial = "ser111",
            Paths = null,
          }
        );

      db.NextProcessForQueuedMaterial(matId).ShouldBe(1);

      db.GetLogForMaterial(matId)
        .EventsShouldBe(
          (
            (List<LogEntry>)
              [
                addToQueue.First() with
                {
                  ActiveOperationTime = TimeSpan.Zero,
                  ProgramDetails = (
                    addToQueue.First().ProgramDetails ?? ImmutableDictionary<string, string>.Empty
                  ).Add("PalletCycleInvalidated", "1"),
                },
                .. addToQueue.Skip(1),
                serial,
                expectedInvalidate,
                expectedReaddToQueue,
              ]
          ).Select(e =>
            e with
            {
              Material = e
                .Material.Select(m =>
                  m with
                  {
                    JobUniqueStr = "ZZZuniq",
                    PartName = "ZZZpart",
                    NumProcesses = 2,
                  }
                )
                .ToImmutableList(),
            }
          )
        );

      db.GetMaterialInAllQueues()
        .ShouldBe([
          new QueuedMaterial()
          {
            MaterialID = matId,
            Unique = "ZZZuniq",
            PartNameOrCasting = "ZZZpart",
            NumProcesses = 2,
            Serial = "ser111",
            Queue = "rawmat",
            Position = 0,
            Paths = ImmutableDictionary<int, int>.Empty,
            AddTimeUTC = now,
            NextProcess = 1,
          },
        ]);
    }

    [Test]
    public void RecordsRebookings()
    {
      var now = DateTime.UtcNow.AddHours(-5);

      using var db = _repoCfg.OpenConnection();

      var booking1 = _fixture.Create<string>();
      var note = _fixture.Create<string>();
      var pri = _fixture.Create<int>();

      var expectedLog = new LogEntry()
      {
        Counter = 1,
        Material = [],
        Pallet = 0,
        LogType = LogType.Rebooking,
        LocationName = "Rebooking",
        LocationNum = pri,
        Program = "part1",
        StartOfCycle = false,
        EndTimeUTC = now.AddMinutes(1),
        ElapsedTime = TimeSpan.Zero,
        ActiveOperationTime = TimeSpan.Zero,
        Result = booking1,
        ProgramDetails = ImmutableDictionary<string, string>
          .Empty.Add("Notes", note)
          .Add("Workorder", "work1")
          .Add("Quantity", "1"),
      };

      var expectedR = new Rebooking()
      {
        BookingId = booking1,
        PartName = "part1",
        Quantity = 1,
        Priority = pri,
        Workorder = "work1",
        Notes = note,
        TimeUTC = now.AddMinutes(1),
      };

      db.CreateRebooking(
          bookingId: booking1,
          partName: "part1",
          workorder: "work1",
          qty: 1,
          notes: note,
          priority: pri,
          timeUTC: now.AddMinutes(1)
        )
        .ShouldBeEquivalentTo(expectedLog);

      db.LookupRebooking(booking1).ShouldBeEquivalentTo(expectedR);
      db.LookupRebooking("notfound").ShouldBeNull();
      db.LoadUnscheduledRebookings().ShouldBeEquivalentTo(ImmutableList.Create(expectedR));
      db.LoadMostRecentSchedule().UnscheduledRebookings.ShouldBeEquivalentTo(ImmutableList.Create(expectedR));

      // record another one, not from mat
      var booking2 = _fixture.Create<string>();
      var part2 = _fixture.Create<string>();
      var pri2 = _fixture.Create<int>();
      var note2 = _fixture.Create<string>();
      var work2 = _fixture.Create<string>();

      var expectedLog2 = new LogEntry()
      {
        Counter = 2,
        Material = [],
        Pallet = 0,
        LogType = LogType.Rebooking,
        LocationName = "Rebooking",
        LocationNum = pri2,
        Program = part2,
        StartOfCycle = false,
        EndTimeUTC = now.AddMinutes(2),
        ElapsedTime = TimeSpan.Zero,
        ActiveOperationTime = TimeSpan.Zero,
        Result = booking2,
        ProgramDetails = ImmutableDictionary<string, string>
          .Empty.Add("Notes", note2)
          .Add("Workorder", work2)
          .Add("Quantity", "2"),
      };

      var expectedR2 = new Rebooking()
      {
        BookingId = booking2,
        PartName = part2,
        Quantity = 2,
        Priority = pri2,
        Workorder = work2,
        Notes = note2,
        TimeUTC = now.AddMinutes(2),
      };

      db.CreateRebooking(
          bookingId: booking2,
          partName: part2,
          qty: 2,
          notes: note2,
          priority: pri2,
          workorder: work2,
          timeUTC: now.AddMinutes(2)
        )
        .ShouldBeEquivalentTo(expectedLog2);

      db.LoadUnscheduledRebookings().ShouldBeEquivalentTo(ImmutableList.Create(expectedR, expectedR2));
      db.LoadMostRecentSchedule()
        .UnscheduledRebookings.ShouldBeEquivalentTo(ImmutableList.Create(expectedR, expectedR2));

      // now cancel the first one

      var expectedCancel = new LogEntry()
      {
        Counter = 3,
        Material = [],
        Pallet = 0,
        LogType = LogType.CancelRebooking,
        LocationName = "CancelRebooking",
        LocationNum = 1,
        Program = "",
        StartOfCycle = false,
        EndTimeUTC = now.AddMinutes(3),
        ElapsedTime = TimeSpan.Zero,
        ActiveOperationTime = TimeSpan.Zero,
        Result = booking1,
      };

      db.CancelRebooking(bookingId: booking1, timeUTC: now.AddMinutes(3))
        .ShouldBeEquivalentTo(expectedCancel);

      db.LoadUnscheduledRebookings().ShouldBeEquivalentTo(ImmutableList.Create(expectedR2));
      db.LoadMostRecentSchedule()
        .UnscheduledRebookings.ShouldBeEquivalentTo(ImmutableList.Create(expectedR2));
    }

    [Test]
    public async Task ExternalQueues()
    {
      using var server = WireMockServer.Start();

      server
        .Given(Request.Create().WithPath(path => path.StartsWith("/api/v1/jobs/casting/")))
        .RespondWith(Response.Create().WithStatusCode(200));

      using var db = _repoCfg.OpenConnection();
      var fix = new Fixture();
      var partName = fix.Create<string>();
      var serial = fix.Create<string>();

      var now = DateTime.UtcNow.AddHours(-5);

      var mat1 = db.AllocateMaterialID(unique: "uuu1", part: partName, numProc: 2);
      db.RecordSerialForMaterialID(
        new EventLogMaterial()
        {
          MaterialID = mat1,
          Face = 0,
          Process = 0,
        },
        serial,
        now
      );

      db.RecordLoadUnloadComplete(
          toLoad: [],
          toUnload:
          [
            new MaterialToUnloadFromFace()
            {
              MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(
                mat1,
                new UnloadDestination() { Queue = "queue1" }
              ),
              FaceNum = 1,
              Process = 1,
              ActiveOperationTime = TimeSpan.FromMinutes(5),
            },
          ],
          previouslyLoaded: null,
          previouslyUnloaded: null,
          lulNum: 10,
          pallet: 20,
          totalElapsed: TimeSpan.FromMinutes(6),
          timeUTC: now.AddMinutes(2),
          externalQueues: new Dictionary<string, string> { { "queue1", server.Urls[0] } }
        )
        .EventsShouldBe([
          new LogEntry()
          {
            Counter = 3,
            Material =
            [
              new LogMaterial()
              {
                MaterialID = mat1,
                Process = 1,
                Face = 1,
                JobUniqueStr = "uuu1",
                NumProcesses = 2,
                PartName = partName,
                Serial = serial,
                Workorder = "",
              },
            ],
            LogType = LogType.PalletCycle,
            LocationName = "Pallet Cycle",
            LocationNum = 1,
            Pallet = 20,
            Program = "",
            StartOfCycle = false,
            EndTimeUTC = now.AddMinutes(2),
            ElapsedTime = TimeSpan.Zero,
            ActiveOperationTime = TimeSpan.Zero,
            Result = "PalletCycle",
          },
          new LogEntry()
          {
            Counter = 2,
            Material =
            [
              new LogMaterial()
              {
                MaterialID = mat1,
                Process = 1,
                Face = 1,
                JobUniqueStr = "uuu1",
                NumProcesses = 2,
                PartName = partName,
                Serial = serial,
                Workorder = "",
              },
            ],
            Pallet = 20,
            LogType = LogType.LoadUnloadCycle,
            LocationName = "L/U",
            LocationNum = 10,
            Program = "UNLOAD",
            StartOfCycle = false,
            EndTimeUTC = now.AddMinutes(2),
            ElapsedTime = TimeSpan.FromMinutes(6),
            ActiveOperationTime = TimeSpan.FromMinutes(5),
            Result = "UNLOAD",
          },
        ]);

      // The sends to external queues happen on a new thread so need to wait
      int numWaits = 0;
      while (server.LogEntries.Count() < 1 && numWaits < 30)
      {
        await Task.Delay(100, TestContext.Current.Execution.CancellationToken);
        numWaits++;
      }

      server.LogEntries.ShouldHaveSingleItem();
      var req = server.LogEntries.First().RequestMessage;

      req.Path.ShouldBe("/api/v1/jobs/casting/" + partName);
      req.Query.ShouldHaveSingleItem();
      req.Query["queue"].ShouldBe(["queue1"]);
      req.Body.ShouldBe("[\"" + serial + "\"]");

      // Now test PartialLoadUnload

      server.ResetLogEntries();

      var mat2 = db.AllocateMaterialID(unique: "uuu1", part: partName, numProc: 2);
      var mat3 = db.AllocateMaterialID(unique: "uuu1", part: partName, numProc: 2);
      var mat4 = db.AllocateMaterialID(unique: "uuu1", part: partName, numProc: 2);
      var serial2 = fix.Create<string>();
      var serial3 = fix.Create<string>();
      db.RecordSerialForMaterialID(
        new EventLogMaterial()
        {
          MaterialID = mat2,
          Face = 0,
          Process = 0,
        },
        serial2,
        now
      );
      db.RecordSerialForMaterialID(
        new EventLogMaterial()
        {
          MaterialID = mat3,
          Face = 0,
          Process = 0,
        },
        serial3,
        now
      );

      db.RecordPartialLoadUnload(
          toLoad:
          [
            new MaterialToLoadOntoFace()
            {
              FaceNum = 1,
              Process = 1,
              Path = 7,
              ActiveOperationTime = TimeSpan.FromMinutes(3),
              MaterialIDs = [mat4],
            },
          ],
          toUnload:
          [
            new MaterialToUnloadFromFace()
            {
              MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(
                mat2,
                new UnloadDestination() { Queue = "queue1" }
              ),
              FaceNum = 3,
              Process = 2,
              ActiveOperationTime = TimeSpan.FromMinutes(4),
            },
            new MaterialToUnloadFromFace()
            {
              MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(
                mat3,
                new UnloadDestination() { Queue = "queue1" }
              ),
              FaceNum = 1,
              Process = 1,
              ActiveOperationTime = TimeSpan.FromMinutes(5),
            },
          ],
          lulNum: 10,
          pallet: 20,
          timeUTC: now.AddMinutes(3),
          totalElapsed: TimeSpan.FromMinutes(24),
          externalQueues: new Dictionary<string, string> { { "queue1", server.Urls[0] } }
        )
        .EventsShouldBe([
          new LogEntry()
          {
            Counter = 6,
            Material =
            [
              new LogMaterial()
              {
                MaterialID = mat2,
                Process = 2,
                Face = 3,
                JobUniqueStr = "uuu1",
                NumProcesses = 2,
                PartName = partName,
                Serial = serial2,
                Workorder = "",
              },
            ],
            Pallet = 20,
            LogType = LogType.LoadUnloadCycle,
            LocationName = "L/U",
            LocationNum = 10,
            Program = "UNLOAD",
            StartOfCycle = false,
            EndTimeUTC = now.AddMinutes(3),
            ElapsedTime = TimeSpan.FromMinutes(24 * 4 / (3 + 4 + 5)),
            ActiveOperationTime = TimeSpan.FromMinutes(4),
            Result = "UNLOAD",
          },
          new LogEntry()
          {
            Counter = 7,
            Material =
            [
              new LogMaterial()
              {
                MaterialID = mat3,
                Process = 1,
                Face = 1,
                JobUniqueStr = "uuu1",
                NumProcesses = 2,
                PartName = partName,
                Serial = serial3,
                Workorder = "",
              },
            ],
            Pallet = 20,
            LogType = LogType.LoadUnloadCycle,
            LocationName = "L/U",
            LocationNum = 10,
            Program = "UNLOAD",
            StartOfCycle = false,
            EndTimeUTC = now.AddMinutes(3),
            ElapsedTime = TimeSpan.FromMinutes(24 * 5 / (3 + 4 + 5)),
            ActiveOperationTime = TimeSpan.FromMinutes(5),
            Result = "UNLOAD",
          },
          new LogEntry()
          {
            Counter = 8,
            Material =
            [
              new LogMaterial()
              {
                MaterialID = mat4,
                Process = 1,
                Face = 1,
                Path = 7,
                JobUniqueStr = "uuu1",
                NumProcesses = 2,
                PartName = partName,
                Serial = "",
                Workorder = "",
              },
            ],
            Pallet = 20,
            LogType = LogType.LoadUnloadCycle,
            LocationName = "L/U",
            LocationNum = 10,
            Program = "LOAD",
            StartOfCycle = false,
            EndTimeUTC = now.AddMinutes(3).AddSeconds(1),
            ElapsedTime = TimeSpan.FromMinutes(24 * 3 / (3 + 4 + 5)),
            ActiveOperationTime = TimeSpan.FromMinutes(3),
            Result = "LOAD",
          },
        ]);

      db.GetMaterialDetails(mat4).Paths.ShouldBeEquivalentTo(ImmutableDictionary<int, int>.Empty.Add(1, 7));

      // The sends to external queues happen on a new thread so need to wait
      numWaits = 0;
      while (server.LogEntries.Count() < 2 && numWaits < 30)
      {
        await Task.Delay(100, TestContext.Current.Execution.CancellationToken);
        numWaits++;
      }

      server.LogEntries.Count.ShouldBe(2);

      req = server.LogEntries.First().RequestMessage;
      req.Path.ShouldBe("/api/v1/jobs/casting/" + partName);
      req.Query.ShouldHaveSingleItem();
      req.Query["queue"].ShouldBe(["queue1"]);
      req.Body.ShouldBe("[\"" + serial2 + "\"]");

      req = server.LogEntries.Last().RequestMessage;
      req.Path.ShouldBe("/api/v1/jobs/casting/" + partName);
      req.Query.ShouldHaveSingleItem();
      req.Query["queue"].ShouldBe(["queue1"]);
      req.Body.ShouldBe("[\"" + serial3 + "\"]");
    }

    #region Helpers
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
        e = e with { ProgramDetails = ImmutableDictionary<string, string>.Empty.Add("operator", operName) };
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
        e = e with
        {
          ProgramDetails = ImmutableDictionary<string, string>
            .Empty.Add("operator", operName)
            .Add("note", reason),
        };
      }
      return e;
    }

    private LogEntry RemoveFromQueueExpectedEntry(
      LogMaterial mat,
      long cntr,
      string queue,
      int position,
      double elapsedMin,
      string reason,
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
        prog: reason ?? "",
        start: false,
        endTime: timeUTC,
        result: "",
        elapsed: TimeSpan.FromMinutes(elapsedMin),
        active: TimeSpan.Zero
      );
      if (!string.IsNullOrEmpty(operName))
      {
        e = e with { ProgramDetails = ImmutableDictionary<string, string>.Empty.Add("operator", operName) };
      }
      return e;
    }

    public static Func<LogMaterial, LogMaterial> SetUniqInMat(string uniq, int? numProc = null)
    {
      return m => m with { JobUniqueStr = uniq, NumProcesses = numProc ?? m.NumProcesses };
    }

    public static Func<LogMaterial, LogMaterial> SetSerialInMat(string serial)
    {
      return m => m with { Serial = serial };
    }

    public static Func<LogMaterial, LogMaterial> SetWorkorderInMat(string work)
    {
      return m => m with { Workorder = work };
    }

    public static Func<LogMaterial, LogMaterial> SetProcInMat(int proc)
    {
      return m => m with { Process = proc };
    }

    public static Func<LogMaterial, LogMaterial> SetPathInMat(int? path)
    {
      return m => m with { Path = path };
    }

    public static Func<LogEntry, LogEntry> TransformLog(
      long matID,
      Func<LogMaterial, LogMaterial> transformMat
    )
    {
      return copy =>
        copy with
        {
          Material = copy.Material.Select(m => m.MaterialID == matID ? transformMat(m) : m).ToImmutableList(),
        };
    }

    public static Func<LogEntry, LogEntry> TransformAllMat(Func<LogMaterial, LogMaterial> transformMat)
    {
      return copy => copy with { Material = copy.Material.Select(m => transformMat(m)).ToImmutableList() };
    }

    private static Func<LogEntry, LogEntry> RemoveActiveTime()
    {
      return copy => copy with { ActiveOperationTime = TimeSpan.Zero };
    }
    #endregion

    #region Basket Tests

    [Test]
    public void BasketLogEvents()
    {
      using var _jobLog = _repoCfg.OpenConnection();

      var mat1 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("unique1", "part1", 2),
        "unique1",
        1,
        "part1",
        2,
        "",
        "",
        ""
      );
      var mat2 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("unique2", "part2", 2),
        "unique2",
        1,
        "part2",
        2,
        "",
        "",
        ""
      );

      var start = DateTime.UtcNow.AddHours(-2);

      // Test BasketLoadUnload with Program = "LOAD"
      var loadToBasket = _jobLog.RecordBasketLoadBegin(
        mats: new[] { mat1, mat2 }.Select(EventLogMaterial.FromLogMat),
        basketId: 5,
        lulNum: 10,
        timeUTC: start
      );

      loadToBasket.ShouldBeEquivalentTo(
        new LogEntry(
          loadToBasket.Counter,
          new[] { mat1, mat2 },
          5, // Pallet = basketId
          LogType.BasketLoadUnload,
          "L/U",
          10, // LocationNum = lulNum
          "LOAD",
          true, // StartOfCycle = true for standalone calls
          start,
          "LOAD",
          TimeSpan.Zero,
          TimeSpan.Zero
        )
      );

      // Test BasketLoadUnload with Program = "UNLOAD"
      var unloadFromBasket = _jobLog.RecordBasketUnloadBegin(
        mats: new[] { mat1, mat2 }.Select(EventLogMaterial.FromLogMat),
        basketId: 5,
        lulNum: 10,
        timeUTC: start.AddMinutes(10)
      );

      unloadFromBasket.ShouldBeEquivalentTo(
        new LogEntry(
          unloadFromBasket.Counter,
          new[] { mat1, mat2 },
          5, // Pallet = basketId
          LogType.BasketLoadUnload,
          "L/U",
          10, // LocationNum = lulNum
          "UNLOAD",
          true, // StartOfCycle = true for standalone calls
          start.AddMinutes(10),
          "UNLOAD",
          TimeSpan.Zero,
          TimeSpan.Zero
        )
      );

      // Test BasketInLocation arrive
      var basketArrive = _jobLog.RecordBasketArriveLocation(
        mats: new[] { mat1 }.Select(EventLogMaterial.FromLogMat),
        basketId: 3,
        locationName: "Tower1",
        locationPosition: 5,
        timeUTC: start.AddMinutes(20)
      );

      basketArrive.Counter.ShouldBeGreaterThan(0);
      basketArrive.Material.ShouldBe(new[] { mat1 });
      basketArrive.Pallet.ShouldBe(3); // basketId
      basketArrive.LogType.ShouldBe(LogType.BasketInLocation);
      basketArrive.LocationName.ShouldBe("Tower1");
      basketArrive.LocationNum.ShouldBe(5); // locationPosition
      basketArrive.Program.ShouldBe("Arrive");
      basketArrive.StartOfCycle.ShouldBe(true);
      basketArrive.EndTimeUTC.ShouldBe(start.AddMinutes(20));
      basketArrive.Result.ShouldBe("");
      basketArrive.ElapsedTime.ShouldBe(TimeSpan.Zero);
      basketArrive.ActiveOperationTime.ShouldBe(TimeSpan.Zero);

      // Test BasketInLocation depart
      var basketDepart = _jobLog.RecordBasketDepartLocation(
        mats: new[] { mat1 }.Select(EventLogMaterial.FromLogMat),
        basketId: 3,
        locationName: "Tower1",
        locationPosition: 5,
        timeUTC: start.AddMinutes(30),
        elapsed: TimeSpan.FromMinutes(10)
      );

      basketDepart.Counter.ShouldBeGreaterThan(0);
      basketDepart.Material.ShouldBe(new[] { mat1 });
      basketDepart.Pallet.ShouldBe(3); // basketId
      basketDepart.LogType.ShouldBe(LogType.BasketInLocation);
      basketDepart.LocationName.ShouldBe("Tower1");
      basketDepart.LocationNum.ShouldBe(5); // locationPosition
      basketDepart.Program.ShouldBe("Depart");
      basketDepart.StartOfCycle.ShouldBe(false);
      basketDepart.EndTimeUTC.ShouldBe(start.AddMinutes(30));
      basketDepart.Result.ShouldBe("");
      basketDepart.ElapsedTime.ShouldBe(TimeSpan.FromMinutes(10));
      basketDepart.ActiveOperationTime.ShouldBe(TimeSpan.Zero);
    }

    [Test]
    public void BasketRepositoryMethods()
    {
      using var _jobLog = _repoCfg.OpenConnection();

      // Test empty basket
      _jobLog.CurrentBasketLog(1).ShouldBeEmpty();
      _jobLog.CurrentBasketLog(2).ShouldBeEmpty();

      var mat1 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("unique1", "part1", 2),
        "unique1",
        1,
        "part1",
        2,
        "",
        "",
        ""
      );
      var mat2 = MkLogMat.Mk(
        _jobLog.AllocateMaterialID("unique2", "part2", 2),
        "unique2",
        1,
        "part2",
        2,
        "",
        "",
        ""
      );

      var start = DateTime.UtcNow.AddHours(-5);
      var basket1Events = new List<LogEntry>();

      // Add material to queues first so we can load them onto baskets
      _jobLog.RecordAddMaterialToQueue(
        new EventLogMaterial()
        {
          MaterialID = mat1.MaterialID,
          Process = 0,
          Face = 0,
        },
        "QUEUE1",
        -1,
        null,
        null,
        start.AddMinutes(-10)
      );
      _jobLog.RecordAddMaterialToQueue(
        new EventLogMaterial()
        {
          MaterialID = mat2.MaterialID,
          Process = 0,
          Face = 0,
        },
        "QUEUE1",
        -1,
        null,
        null,
        start.AddMinutes(-10)
      );

      // Load material onto basket 1 using RecordBasketOnlyLoadUnload
      // This creates both BasketLoadUnload and BasketCycle events
      var loadLogs = _jobLog.RecordBasketOnlyLoadUnload(
        toLoad: new MaterialToLoadOntoBasket()
        {
          MaterialIDs = [mat1.MaterialID, mat2.MaterialID],
          Process = 1,
          ActiveOperationTime = TimeSpan.Zero,
        },
        previouslyLoaded: null,
        toUnload: null,
        lulNum: 5,
        basketId: 1,
        totalElapsed: TimeSpan.Zero,
        timeUTC: start,
        externalQueues: null
      );
      // Add only the BasketLoadUnload events (not the cycle) to basket1Events
      basket1Events.AddRange(loadLogs.Where(e => e.LogType == LogType.BasketLoadUnload));

      // Also test BasketInLocation, but it's not included in CurrentBasketLog
      var arriveEvt = _jobLog.RecordBasketArriveLocation(
        mats: new[] { mat1, mat2 }.Select(EventLogMaterial.FromLogMat),
        basketId: 1,
        locationName: "Tower1",
        locationPosition: 3,
        timeUTC: start.AddMinutes(5)
      );
      basket1Events.Add(arriveEvt);

      // CurrentBasketLog should return events since last basket cycle (the cycle start is not included by default)
      var currentLog = _jobLog.CurrentBasketLog(1);
      currentLog.EventsShouldBe(basket1Events);
      _jobLog.CurrentBasketLog(2).ShouldBeEmpty();

      // Use RecordBasketOnlyLoadUnload to create basket cycle end (unload from basket to queue)
      // This will emit both BasketLoadUnload UNLOAD and BasketCycle end events
      var cycleTime = start.AddMinutes(20);
      var cycleLogs = _jobLog.RecordBasketOnlyLoadUnload(
        toLoad: null,
        previouslyLoaded: null,
        toUnload: new MaterialToUnloadFromBasket()
        {
          MaterialIDToQueue = new Dictionary<long, string>()
          {
            [mat1.MaterialID] = "QUEUE1",
            [mat2.MaterialID] = "QUEUE1",
          }.ToImmutableDictionary(),
          Process = 1,
          ActiveOperationTime = TimeSpan.Zero,
        },
        lulNum: 5,
        basketId: 1,
        totalElapsed: TimeSpan.Zero,
        timeUTC: cycleTime,
        externalQueues: null
      );

      // After basket cycle, CurrentBasketLog should be empty
      _jobLog.CurrentBasketLog(1).ShouldBeEmpty();

      // Test includeLastCycleEvt parameter
      var logsWithCycle = _jobLog.CurrentBasketLog(1, includeLastCycleEvt: true);
      logsWithCycle.Count.ShouldBe(1);
      logsWithCycle[0].LogType.ShouldBe(LogType.BasketCycle);

      // Add new events after cycle
      var basket1NewEvents = new List<LogEntry>();
      var loadEvt2 = _jobLog.RecordBasketLoadBegin(
        mats: new[] { mat1 }.Select(EventLogMaterial.FromLogMat),
        basketId: 1,
        lulNum: 5,
        timeUTC: start.AddMinutes(30)
      );
      basket1NewEvents.Add(loadEvt2);

      _jobLog.CurrentBasketLog(1).EventsShouldBe(basket1NewEvents);

      // Test multiple baskets tracked independently
      var basket2Events = new List<LogEntry>();
      var basket2Load = _jobLog.RecordBasketLoadBegin(
        mats: new[] { mat2 }.Select(EventLogMaterial.FromLogMat),
        basketId: 2,
        lulNum: 8,
        timeUTC: start.AddMinutes(40)
      );
      basket2Events.Add(basket2Load);

      _jobLog.CurrentBasketLog(1).EventsShouldBe(basket1NewEvents);
      _jobLog.CurrentBasketLog(2).EventsShouldBe(basket2Events);
    }

    [Test]
    public void LoadUnloadBetweenPalletAndBasket()
    {
      using var _jobLog = _repoCfg.OpenConnection();
      var start = DateTime.UtcNow.AddHours(-10);

      var mat1 = MkLogMat.Mk(1, "uniq1", 1, "part1", 2, "", "", "");
      _jobLog.CreateMaterialID(1, "uniq1", "part1", 2);
      var mat2 = MkLogMat.Mk(2, "uniq2", 1, "part2", 2, "", "", "");
      _jobLog.CreateMaterialID(2, "uniq2", "part2", 2);

      // First, load material onto basket from queue
      _jobLog.RecordAddMaterialToQueue(
        new EventLogMaterial()
        {
          MaterialID = mat1.MaterialID,
          Process = 0,
          Face = 0,
        },
        "QUEUE1",
        -1,
        null,
        null,
        start
      );
      _jobLog.RecordAddMaterialToQueue(
        new EventLogMaterial()
        {
          MaterialID = mat2.MaterialID,
          Process = 0,
          Face = 0,
        },
        "QUEUE1",
        -1,
        null,
        null,
        start
      );

      // Load from queue to pallet, then unload to basket
      var loadToPallet = _jobLog.RecordLoadUnloadComplete(
        toLoad:
        [
          new MaterialToLoadOntoFace()
          {
            MaterialIDs = [mat1.MaterialID, mat2.MaterialID],
            Process = 1,
            Path = null,
            FaceNum = 1,
            ActiveOperationTime = TimeSpan.FromMinutes(10),
          },
        ],
        toUnload: null,
        previouslyLoaded: null,
        previouslyUnloaded: null,
        pallet: 1,
        lulNum: 5,
        totalElapsed: TimeSpan.FromMinutes(5),
        timeUTC: start.AddMinutes(10),
        externalQueues: null
      );

      // Now unload from pallet to basket
      // Pass completedBaskets to emit basket cycle events for basket 3
      var unloadToBasket = _jobLog.RecordLoadUnloadComplete(
        toLoad: null,
        toUnload:
        [
          new MaterialToUnloadFromFace()
          {
            MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>
              .Empty.Add(mat1.MaterialID, new UnloadDestination() { BasketId = 3 })
              .Add(mat2.MaterialID, new UnloadDestination() { BasketId = 3 }),
            FaceNum = 1,
            Process = 1,
            ActiveOperationTime = TimeSpan.FromMinutes(15),
          },
        ],
        previouslyLoaded: null,
        previouslyUnloaded: null,
        pallet: 1,
        lulNum: 5,
        totalElapsed: TimeSpan.FromMinutes(20),
        timeUTC: start.AddMinutes(30),
        externalQueues: null,
        completedBaskets: new Dictionary<int, IEnumerable<EventLogMaterial>>
        {
          [3] = new[]
          {
            new EventLogMaterial()
            {
              MaterialID = mat1.MaterialID,
              Process = 1,
              Face = 0,
            },
            new EventLogMaterial()
            {
              MaterialID = mat2.MaterialID,
              Process = 1,
              Face = 0,
            },
          },
        }
      );

      // Should have LoadUnloadCycle for pallet unload
      var palletUnload = unloadToBasket.FirstOrDefault(e =>
        e.LogType == LogType.LoadUnloadCycle && e.Program == "UNLOAD"
      );
      palletUnload.ShouldNotBeNull();
      palletUnload.Material.Count.ShouldBe(2);
      palletUnload.Material[0].MaterialID.ShouldBe(mat1.MaterialID);
      palletUnload.Material[1].MaterialID.ShouldBe(mat2.MaterialID);

      // Should have BasketLoadUnload for basket load
      var basketLoad = unloadToBasket.FirstOrDefault(e =>
        e.LogType == LogType.BasketLoadUnload && e.Program == "LOAD"
      );
      basketLoad.ShouldNotBeNull();
      basketLoad.Material.Count.ShouldBe(2);
      basketLoad.Material[0].MaterialID.ShouldBe(mat1.MaterialID);
      basketLoad.Material[1].MaterialID.ShouldBe(mat2.MaterialID);

      // Should have PalletCycle end
      var palletCycleEnd = unloadToBasket.FirstOrDefault(e =>
        e.LogType == LogType.PalletCycle && e.StartOfCycle == false
      );
      palletCycleEnd.ShouldNotBeNull();

      // Should have BasketCycle start
      var basketCycleStart = unloadToBasket.FirstOrDefault(e =>
        e.LogType == LogType.BasketCycle && e.StartOfCycle == true
      );
      basketCycleStart.ShouldNotBeNull();
      basketCycleStart.Material.Count.ShouldBe(2);
      basketCycleStart.Material[0].MaterialID.ShouldBe(mat1.MaterialID);
      basketCycleStart.Material[1].MaterialID.ShouldBe(mat2.MaterialID);

      // Now load from basket back to pallet
      // Pass completedBaskets with empty material to emit basket cycle end for basket 3
      var loadFromBasket = _jobLog.RecordLoadUnloadComplete(
        toLoad:
        [
          new MaterialToLoadOntoFace()
          {
            MaterialIDs = [mat1.MaterialID, mat2.MaterialID],
            Process = 2,
            Path = null,
            FaceNum = 2,
            ActiveOperationTime = TimeSpan.FromMinutes(12),
          },
        ],
        toUnload: null,
        previouslyLoaded: null,
        previouslyUnloaded: null,
        pallet: 2,
        lulNum: 6,
        totalElapsed: TimeSpan.FromMinutes(8),
        timeUTC: start.AddMinutes(50),
        externalQueues: null,
        completedBaskets: new Dictionary<int, IEnumerable<EventLogMaterial>>
        {
          [3] = [], // Basket 3 is now empty
        }
      );

      // Should have BasketLoadUnload for basket unload
      var basketUnload = loadFromBasket.FirstOrDefault(e =>
        e.LogType == LogType.BasketLoadUnload && e.Program == "UNLOAD"
      );
      basketUnload.ShouldNotBeNull();
      basketUnload.Material.Count.ShouldBe(2);
      basketUnload.Material[0].MaterialID.ShouldBe(mat1.MaterialID);
      basketUnload.Material[1].MaterialID.ShouldBe(mat2.MaterialID);

      // Should have LoadUnloadCycle for pallet load
      var palletLoad = loadFromBasket.FirstOrDefault(e =>
        e.LogType == LogType.LoadUnloadCycle && e.Program == "LOAD"
      );
      palletLoad.ShouldNotBeNull();
      palletLoad.Material.Count.ShouldBe(2);
      palletLoad.Material[0].MaterialID.ShouldBe(mat1.MaterialID);
      palletLoad.Material[0].Face.ShouldBe(2);
      palletLoad.Material[1].MaterialID.ShouldBe(mat2.MaterialID);
      palletLoad.Material[1].Face.ShouldBe(2);

      // Should have BasketCycle end
      var basketCycleEnd = loadFromBasket.FirstOrDefault(e =>
        e.LogType == LogType.BasketCycle && e.StartOfCycle == false
      );
      basketCycleEnd.ShouldNotBeNull();
      basketCycleEnd.Material.Count.ShouldBe(2);
      basketCycleEnd.Material[0].MaterialID.ShouldBe(mat1.MaterialID);
      basketCycleEnd.Material[1].MaterialID.ShouldBe(mat2.MaterialID);

      // Should have PalletCycle start
      var palletCycleStart = loadFromBasket.FirstOrDefault(e =>
        e.LogType == LogType.PalletCycle && e.StartOfCycle == true
      );
      palletCycleStart.ShouldNotBeNull();
    }

    [Test]
    public void LoadUnloadBetweenBasketAndQueue()
    {
      using var _jobLog = _repoCfg.OpenConnection();
      var start = DateTime.UtcNow.AddHours(-10);

      var mat1 = MkLogMat.Mk(1, "uniq1", 1, "part1", 2, "", "", "");
      _jobLog.CreateMaterialID(1, "uniq1", "part1", 2);
      var mat2 = MkLogMat.Mk(2, "uniq2", 1, "part2", 2, "", "", "");
      _jobLog.CreateMaterialID(2, "uniq2", "part2", 2);

      // Add material to queue
      _jobLog.RecordAddMaterialToQueue(
        new EventLogMaterial()
        {
          MaterialID = mat1.MaterialID,
          Process = 0,
          Face = 0,
        },
        "QUEUE1",
        -1,
        null,
        null,
        start
      );
      _jobLog.RecordAddMaterialToQueue(
        new EventLogMaterial()
        {
          MaterialID = mat2.MaterialID,
          Process = 0,
          Face = 0,
        },
        "QUEUE1",
        -1,
        null,
        null,
        start
      );

      // Load from queue to basket using RecordBasketOnlyLoadUnload
      var loadLogs = _jobLog.RecordBasketOnlyLoadUnload(
        toLoad: new MaterialToLoadOntoBasket()
        {
          MaterialIDs = [mat1.MaterialID],
          Process = 1,
          ActiveOperationTime = TimeSpan.FromMinutes(5),
        },
        previouslyLoaded: null,
        toUnload: null,
        lulNum: 10,
        basketId: 5,
        totalElapsed: TimeSpan.FromMinutes(2),
        timeUTC: start.AddMinutes(10),
        externalQueues: null
      );

      // Should have created basket cycle start and load events
      loadLogs.Count().ShouldBeGreaterThan(0);
      loadLogs.Any(e => e.LogType == LogType.BasketCycle && e.StartOfCycle).ShouldBeTrue();
      loadLogs.Any(e => e.LogType == LogType.BasketLoadUnload && e.Program == "LOAD").ShouldBeTrue();

      // Verify that basket events have path = null (not recorded for baskets)
      var basketLoadEvent = loadLogs.First(e => e.LogType == LogType.BasketLoadUnload && e.Program == "LOAD");
      basketLoadEvent.Material[0].Path.ShouldBeNull();

      // Material should not be in queue now
      var queuedMats = _jobLog.GetMaterialInAllQueues();
      queuedMats.Count(m => m.MaterialID == mat1.MaterialID).ShouldBe(0);

      // Now unload from basket back to queue using RecordBasketOnlyLoadUnload
      var unloadLogs = _jobLog.RecordBasketOnlyLoadUnload(
        toLoad: null,
        previouslyLoaded: null,
        toUnload: new MaterialToUnloadFromBasket()
        {
          MaterialIDToQueue = new Dictionary<long, string>()
          {
            [mat1.MaterialID] = "QUEUE2",
          }.ToImmutableDictionary(),
          Process = 1,
          ActiveOperationTime = TimeSpan.FromMinutes(5),
        },
        lulNum: 10,
        basketId: 5,
        totalElapsed: TimeSpan.FromMinutes(2),
        timeUTC: start.AddMinutes(30),
        externalQueues: null
      );

      // Should have created basket cycle end and unload events
      unloadLogs.Count().ShouldBeGreaterThan(0);
      unloadLogs.Any(e => e.LogType == LogType.BasketCycle && !e.StartOfCycle).ShouldBeTrue();
      unloadLogs.Any(e => e.LogType == LogType.BasketLoadUnload && e.Program == "UNLOAD").ShouldBeTrue();

      // Verify that basket events have path = null (not recorded for baskets)
      var basketUnloadEvent = unloadLogs.First(e =>
        e.LogType == LogType.BasketLoadUnload && e.Program == "UNLOAD"
      );
      basketUnloadEvent.Material[0].Path.ShouldBeNull();

      // Should have added to queue
      var queuedMats2 = _jobLog.GetMaterialInAllQueues().ToList();
      queuedMats2.Count(m => m.MaterialID == mat1.MaterialID && m.Queue == "QUEUE2").ShouldBe(1);
    }

    [Test]
    public void BasketOnlySimultaneousLoadUnload()
    {
      using var _jobLog = _repoCfg.OpenConnection();
      var start = DateTime.UtcNow.AddHours(-10);

      var mat1 = MkLogMat.Mk(1, "uniq1", 1, "part1", 2, "", "", "");
      _jobLog.CreateMaterialID(1, "uniq1", "part1", 2);
      var mat2 = MkLogMat.Mk(2, "uniq2", 1, "part2", 2, "", "", "");
      _jobLog.CreateMaterialID(2, "uniq2", "part2", 2);
      var mat3 = MkLogMat.Mk(3, "uniq3", 1, "part3", 2, "", "", "");
      _jobLog.CreateMaterialID(3, "uniq3", "part3", 2);

      // Add mat1 and mat2 to queue
      _jobLog.RecordAddMaterialToQueue(
        new EventLogMaterial()
        {
          MaterialID = mat1.MaterialID,
          Process = 0,
          Face = 0,
        },
        "QUEUE1",
        -1,
        null,
        null,
        start
      );
      _jobLog.RecordAddMaterialToQueue(
        new EventLogMaterial()
        {
          MaterialID = mat2.MaterialID,
          Process = 0,
          Face = 0,
        },
        "QUEUE1",
        -1,
        null,
        null,
        start
      );

      // Load mat3 onto basket 5 first
      _jobLog.RecordBasketOnlyLoadUnload(
        toLoad: new MaterialToLoadOntoBasket()
        {
          MaterialIDs = [mat3.MaterialID],
          Process = 1,
          ActiveOperationTime = TimeSpan.FromMinutes(3),
        },
        previouslyLoaded: null,
        toUnload: null,
        lulNum: 10,
        basketId: 5,
        totalElapsed: TimeSpan.FromMinutes(2),
        timeUTC: start.AddMinutes(5),
        externalQueues: null
      );

      // Now do a simultaneous load and unload operation:
      // - Load mat1 and mat2 from queue onto basket 5
      // - Unload mat3 from basket 5 to queue
      var logs = _jobLog.RecordBasketOnlyLoadUnload(
        toLoad: new MaterialToLoadOntoBasket()
        {
          MaterialIDs = [mat1.MaterialID, mat2.MaterialID],
          Process = 1,
          ActiveOperationTime = TimeSpan.FromMinutes(5),
        },
        previouslyLoaded:
        [
          new EventLogMaterial()
          {
            MaterialID = mat3.MaterialID,
            Process = 1,
            Face = 0,
          },
        ],
        toUnload: new MaterialToUnloadFromBasket()
        {
          MaterialIDToQueue = new Dictionary<long, string>()
          {
            [mat3.MaterialID] = "QUEUE2",
          }.ToImmutableDictionary(),
          Process = 1,
          ActiveOperationTime = TimeSpan.FromMinutes(3),
        },
        lulNum: 10,
        basketId: 5,
        totalElapsed: TimeSpan.FromMinutes(3),
        timeUTC: start.AddMinutes(10),
        externalQueues: null
      );

      // Verify events created
      logs.Count().ShouldBeGreaterThan(0);

      // Should have basket cycle end for basket 5 (mat3 unloaded)
      logs.Count(e => e.LogType == LogType.BasketCycle && e.Pallet == 5 && !e.StartOfCycle).ShouldBe(1);

      // Should have unload event for basket 5
      logs.Count(e => e.LogType == LogType.BasketLoadUnload && e.Pallet == 5 && e.Program == "UNLOAD")
        .ShouldBe(1);

      // Should have basket cycle start for basket 5 (mat1, mat2 loaded; mat3 previously loaded)
      var cycleStart = logs.FirstOrDefault(e =>
        e.LogType == LogType.BasketCycle && e.Pallet == 5 && e.StartOfCycle
      );
      cycleStart.ShouldNotBeNull();
      cycleStart.Material.Count.ShouldBe(3); // mat1, mat2, mat3 (previously loaded)

      // mat3 should be in QUEUE2 now
      var queuedMats = _jobLog.GetMaterialInAllQueues().ToList();
      queuedMats.Count(m => m.MaterialID == mat3.MaterialID && m.Queue == "QUEUE2").ShouldBe(1);

      // Should have load event for basket 5 (mat1, mat2)
      logs.Count(e => e.LogType == LogType.BasketLoadUnload && e.Pallet == 5 && e.Program == "LOAD")
        .ShouldBe(1);

      // Verify queue state
      var queued = _jobLog.GetMaterialInAllQueues();
      queued.Count(m => m.MaterialID == mat1.MaterialID).ShouldBe(0);
      queued.Count(m => m.MaterialID == mat2.MaterialID).ShouldBe(0);

      // Verify CurrentBasketLog
      var basket5Log = _jobLog.CurrentBasketLog(5);
      basket5Log.Count.ShouldBeGreaterThan(0);
      basket5Log.Any(e => e.LogType == LogType.BasketLoadUnload).ShouldBeTrue();
    }

    [Test]
    public void BasketCycleInvalidation()
    {
      using var _jobLog = _repoCfg.OpenConnection();
      var start = DateTime.UtcNow.AddHours(-5);

      var mat1 = MkLogMat.Mk(1, "uniq1", 1, "part1", 2, "", "", "");
      _jobLog.CreateMaterialID(1, "uniq1", "part1", 2);

      // Add material to queue
      _jobLog.RecordAddMaterialToQueue(
        new EventLogMaterial()
        {
          MaterialID = mat1.MaterialID,
          Process = 0,
          Face = 0,
        },
        "QUEUE1",
        -1,
        null,
        null,
        start
      );

      // Load from queue to basket using RecordBasketOnlyLoadUnload
      _jobLog.RecordBasketOnlyLoadUnload(
        toLoad: new MaterialToLoadOntoBasket()
        {
          MaterialIDs = [mat1.MaterialID],
          Process = 1,
          ActiveOperationTime = TimeSpan.Zero,
        },
        previouslyLoaded: null,
        toUnload: null,
        lulNum: 10,
        basketId: 5,
        totalElapsed: TimeSpan.FromMinutes(2),
        timeUTC: start.AddMinutes(10),
        externalQueues: null
      );

      // Material should not be in queue now
      _jobLog.GetMaterialInAllQueues().ShouldBeEmpty();
      // NextProcessForQueuedMaterial returns the material's NEXT process from log events
      // After loading to process 1, it should return 2 (process + 1)
      _jobLog.NextProcessForQueuedMaterial(mat1.MaterialID).ShouldBe(2);

      // Get log before invalidation
      var logBeforeInvalidate = _jobLog.GetLogForMaterial(mat1.MaterialID, includeInvalidatedCycles: false);
      var basketEventsBeforeInvalidateCount = logBeforeInvalidate.Count(e =>
        e.LogType == LogType.BasketLoadUnload || e.LogType == LogType.BasketCycle
      );
      basketEventsBeforeInvalidateCount.ShouldBeGreaterThan(0);

      // Invalidate the cycle
      var invalidateResults = _jobLog.InvalidatePalletCycle(
        matId: mat1.MaterialID,
        process: 1,
        operatorName: "operator"
      );
      invalidateResults.ShouldNotBeNull();

      // After invalidation, basket events should be excluded from queries that don't include invalidated cycles
      var logAfterInvalidate = _jobLog.GetLogForMaterial(mat1.MaterialID, includeInvalidatedCycles: false);
      var basketEventsAfterInvalidate = logAfterInvalidate.Count(e =>
        e.LogType == LogType.BasketLoadUnload || e.LogType == LogType.BasketCycle
      );
      basketEventsAfterInvalidate.ShouldBe(0);

      // But should be included when we explicitly ask for invalidated cycles
      var logWithInvalidated = _jobLog.GetLogForMaterial(mat1.MaterialID, includeInvalidatedCycles: true);
      var invalidatedBasketEvents = logWithInvalidated.Count(e =>
        (e.LogType == LogType.BasketLoadUnload || e.LogType == LogType.BasketCycle)
        && e.ProgramDetails.ContainsKey("PalletCycleInvalidated")
      );
      invalidatedBasketEvents.ShouldBeGreaterThan(0);

      // Material should NOT be returned to basket (can't put it back), should go to quarantine queue
      var queuedMats = _jobLog.GetMaterialInAllQueues();
      if (queuedMats.Any())
      {
        // Should be in quarantine queue
        queuedMats.ShouldHaveSingleItem().Queue.ShouldContain("quarantine", Case.Insensitive);
      }

      // Should have an InvalidateCycle event
      var invalidateEvt = logWithInvalidated.FirstOrDefault(e => e.LogType == LogType.InvalidateCycle);
      invalidateEvt.ShouldNotBeNull();
    }

    [Test]
    public void CurrentBasketLogHandlesInvalidation()
    {
      var start = new DateTime(2018, 01, 15, 17, 30, 0, DateTimeKind.Utc);
      using var _jobLog = _repoCfg.OpenConnection();

      // Create material and load it onto a basket
      var m1 = _jobLog.AllocateMaterialID("U1", "Part1", 1);

      _jobLog.RecordBasketOnlyLoadUnload(
        toLoad: new MaterialToLoadOntoBasket()
        {
          MaterialIDs = [m1],
          Process = 1,
          ActiveOperationTime = TimeSpan.Zero,
        },
        previouslyLoaded: null,
        toUnload: null,
        lulNum: 1,
        basketId: 55,
        totalElapsed: TimeSpan.FromMinutes(1),
        timeUTC: start,
        externalQueues: null
      );

      // Verify CurrentBasketLog returns events
      var logBefore = _jobLog.CurrentBasketLog(55);
      logBefore.ShouldNotBeEmpty();

      // Invalidate the cycle
      _jobLog.InvalidatePalletCycle(m1, 1, "test-operator");

      // Verify CurrentBasketLog is now empty
      var logAfter = _jobLog.CurrentBasketLog(55);
      logAfter.ShouldBeEmpty();
    }

    [Test]
    public void BasketLoadEventUses1SecondDelay()
    {
      var start = new DateTime(2018, 01, 15, 17, 30, 0, DateTimeKind.Utc);
      using var _jobLog = _repoCfg.OpenConnection();

      var mat1 = MkLogMat.Mk(1, "uniq1", 1, "part1", 2, "", "", "");
      _jobLog.CreateMaterialID(1, "uniq1", "part1", 2);

      // Add material to queue
      _jobLog.RecordAddMaterialToQueue(
        new EventLogMaterial()
        {
          MaterialID = mat1.MaterialID,
          Process = 0,
          Face = 0,
        },
        "QUEUE1",
        -1,
        null,
        null,
        start
      );

      // Load from queue to pallet, then unload to basket using RecordLoadUnloadComplete
      var loadCompleteLogs = _jobLog.RecordLoadUnloadComplete(
        toLoad:
        [
          new MaterialToLoadOntoFace()
          {
            MaterialIDs = [mat1.MaterialID],
            Process = 1,
            Path = null,
            FaceNum = 1,
            ActiveOperationTime = TimeSpan.FromMinutes(10),
          },
        ],
        toUnload:
        [
          new MaterialToUnloadFromFace()
          {
            MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(
              mat1.MaterialID,
              new UnloadDestination() { BasketId = 3 }
            ),
            FaceNum = 1,
            Process = 1,
            ActiveOperationTime = TimeSpan.FromMinutes(15),
          },
        ],
        previouslyLoaded: null,
        previouslyUnloaded: null,
        pallet: 1,
        lulNum: 5,
        totalElapsed: TimeSpan.FromMinutes(20),
        timeUTC: start.AddMinutes(30),
        externalQueues: null,
        completedBaskets: new Dictionary<int, IEnumerable<EventLogMaterial>>
        {
          [3] = new[]
          {
            new EventLogMaterial()
            {
              MaterialID = mat1.MaterialID,
              Process = 1,
              Face = 0,
            },
          },
        }
      );

      // Find the Basket LOAD event
      var basketLoad = loadCompleteLogs.FirstOrDefault(e =>
        e.LogType == LogType.BasketLoadUnload && e.Program == "LOAD"
      );
      basketLoad.ShouldNotBeNull();

      // Basket LOAD should be 1 second after the base time
      basketLoad.EndTimeUTC.ShouldBe(start.AddMinutes(30).AddSeconds(1));

      // Find the Pallet LOAD event (from first toLoad)
      var palletLoad = loadCompleteLogs.FirstOrDefault(e =>
        e.LogType == LogType.LoadUnloadCycle && e.Program == "LOAD"
      );
      palletLoad.ShouldNotBeNull();

      // Pallet LOAD should also be 1 second after the base time
      palletLoad.EndTimeUTC.ShouldBe(start.AddMinutes(30).AddSeconds(1));
    }

    [Test]
    public void RecordBasketCycleEndIgnoresInvalidatedStart()
    {
      var start = new DateTime(2018, 01, 15, 17, 30, 0, DateTimeKind.Utc);
      using var _jobLog = _repoCfg.OpenConnection();

      var m1 = _jobLog.AllocateMaterialID("U1", "Part1", 1);

      // Load material onto basket to create a basket cycle START
      _jobLog.RecordBasketOnlyLoadUnload(
        toLoad: new MaterialToLoadOntoBasket()
        {
          MaterialIDs = [m1],
          Process = 1,
          ActiveOperationTime = TimeSpan.Zero,
        },
        previouslyLoaded: null,
        toUnload: null,
        lulNum: 1,
        basketId: 55,
        totalElapsed: TimeSpan.FromMinutes(1),
        timeUTC: start,
        externalQueues: null
      );

      // Verify basket cycle START was created
      var logBefore = _jobLog.GetLogForMaterial(m1);
      var basketCycleStart = logBefore.FirstOrDefault(e =>
        e.LogType == LogType.BasketCycle && e.StartOfCycle
      );
      basketCycleStart.ShouldNotBeNull();

      // Invalidate the cycle
      _jobLog.InvalidatePalletCycle(m1, 1, "test-operator");

      // Now try to create a basket cycle END by calling RecordEmptyBasket
      // This internally calls RecordBasketCycleEnd, which should NOT emit an end
      // because the START is invalidated
      var emptyLogs = _jobLog.RecordEmptyBasket(basketId: 55, timeUTC: start.AddMinutes(10), basketEnd: true);

      // Should not have created a basket cycle END
      emptyLogs.Any(e => e.LogType == LogType.BasketCycle && !e.StartOfCycle).ShouldBeFalse();
    }

    [Test]
    public void RecordLoadEndUsesBasketCycleStartAndIgnoresInvalidated()
    {
      var start = new DateTime(2018, 01, 15, 17, 30, 0, DateTimeKind.Utc);
      using var _jobLog = _repoCfg.OpenConnection();

      var mat1 = MkLogMat.Mk(1, "uniq1", 1, "part1", 2, "", "", "");
      _jobLog.CreateMaterialID(1, "uniq1", "part1", 2);

      // Load material onto basket to create a basket cycle START
      _jobLog.RecordBasketOnlyLoadUnload(
        toLoad: new MaterialToLoadOntoBasket()
        {
          MaterialIDs = [mat1.MaterialID],
          Process = 1,
          ActiveOperationTime = TimeSpan.Zero,
        },
        previouslyLoaded: null,
        toUnload: null,
        lulNum: 1,
        basketId: 55,
        totalElapsed: TimeSpan.FromMinutes(1),
        timeUTC: start,
        externalQueues: null
      );

      // Verify basket cycle START was created
      var logBefore = _jobLog.GetLogForMaterial(mat1.MaterialID);
      var basketCycleStart = logBefore.FirstOrDefault(e =>
        e.LogType == LogType.BasketCycle && e.StartOfCycle
      );
      basketCycleStart.ShouldNotBeNull();

      // Now load from basket to pallet - should detect material is on basket
      var loadLogs = _jobLog.RecordLoadUnloadComplete(
        toLoad:
        [
          new MaterialToLoadOntoFace()
          {
            MaterialIDs = [mat1.MaterialID],
            Process = 2,
            Path = null,
            FaceNum = 1,
            ActiveOperationTime = TimeSpan.FromMinutes(5),
          },
        ],
        toUnload: null,
        previouslyLoaded: null,
        previouslyUnloaded: null,
        pallet: 1,
        lulNum: 5,
        totalElapsed: TimeSpan.FromMinutes(3),
        timeUTC: start.AddMinutes(10),
        externalQueues: null,
        completedBaskets: new Dictionary<int, IEnumerable<EventLogMaterial>>
        {
          [55] = [], // Basket is now empty
        }
      );

      // Should have created a basket UNLOAD event
      var basketUnload = loadLogs.FirstOrDefault(e =>
        e.LogType == LogType.BasketLoadUnload && e.Program == "UNLOAD"
      );
      basketUnload.ShouldNotBeNull();
      basketUnload.Pallet.ShouldBe(55);

      // Now test with invalidated basket cycle START
      var mat2 = MkLogMat.Mk(2, "uniq2", 1, "part2", 2, "", "", "");
      _jobLog.CreateMaterialID(2, "uniq2", "part2", 2);

      // Load material onto basket
      _jobLog.RecordBasketOnlyLoadUnload(
        toLoad: new MaterialToLoadOntoBasket()
        {
          MaterialIDs = [mat2.MaterialID],
          Process = 1,
          ActiveOperationTime = TimeSpan.Zero,
        },
        previouslyLoaded: null,
        toUnload: null,
        lulNum: 1,
        basketId: 66,
        totalElapsed: TimeSpan.FromMinutes(1),
        timeUTC: start.AddMinutes(20),
        externalQueues: null
      );

      // Invalidate the basket cycle
      _jobLog.InvalidatePalletCycle(mat2.MaterialID, 1, "test-operator");

      // Now try to load from basket to pallet - should NOT detect material on basket
      // because the basket cycle START is invalidated
      var loadLogs2 = _jobLog.RecordLoadUnloadComplete(
        toLoad:
        [
          new MaterialToLoadOntoFace()
          {
            MaterialIDs = [mat2.MaterialID],
            Process = 2,
            Path = null,
            FaceNum = 1,
            ActiveOperationTime = TimeSpan.FromMinutes(5),
          },
        ],
        toUnload: null,
        previouslyLoaded: null,
        previouslyUnloaded: null,
        pallet: 2,
        lulNum: 5,
        totalElapsed: TimeSpan.FromMinutes(3),
        timeUTC: start.AddMinutes(30),
        externalQueues: null
      );

      // Should NOT have created a basket UNLOAD event
      var basketUnload2 = loadLogs2.FirstOrDefault(e =>
        e.LogType == LogType.BasketLoadUnload && e.Program == "UNLOAD"
      );
      basketUnload2.ShouldBeNull();
    }

    [Test]
    public void InvalidatePalletCycleDetectsBasketCycleStart()
    {
      var start = new DateTime(2018, 01, 15, 17, 30, 0, DateTimeKind.Utc);
      using var _jobLog = _repoCfg.OpenConnection();

      var mat1 = MkLogMat.Mk(1, "uniq1", 1, "part1", 2, "", "", "");
      _jobLog.CreateMaterialID(1, "uniq1", "part1", 2);

      // Add material to queue first
      _jobLog.RecordAddMaterialToQueue(
        new EventLogMaterial()
        {
          MaterialID = mat1.MaterialID,
          Process = 0,
          Face = 0,
        },
        "QUEUE1",
        -1,
        null,
        null,
        start
      );

      // Load material onto basket from queue - creates basket cycle START
      _jobLog.RecordBasketOnlyLoadUnload(
        toLoad: new MaterialToLoadOntoBasket()
        {
          MaterialIDs = [mat1.MaterialID],
          Process = 1,
          ActiveOperationTime = TimeSpan.Zero,
        },
        previouslyLoaded: null,
        toUnload: null,
        lulNum: 1,
        basketId: 55,
        totalElapsed: TimeSpan.FromMinutes(1),
        timeUTC: start.AddMinutes(5),
        externalQueues: null
      );

      // Verify basket cycle START was created
      var logBefore = _jobLog.GetLogForMaterial(mat1.MaterialID);
      var basketCycleStart = logBefore.FirstOrDefault(e =>
        e.LogType == LogType.BasketCycle && e.StartOfCycle
      );
      basketCycleStart.ShouldNotBeNull();

      // Invalidate the cycle - should detect wasOnBasket=true because we're invalidating a basket cycle START
      _jobLog.InvalidatePalletCycle(mat1.MaterialID, 1, "test-operator");

      // Material should NOT be returned to the basket
      // It should NOT be in QUEUE1 either (since wasOnBasket=true)
      var queuedMats = _jobLog.GetMaterialInAllQueues();
      queuedMats.Any(m => m.Queue == "QUEUE1" && m.MaterialID == mat1.MaterialID).ShouldBeFalse();

      // Should have an InvalidateCycle event
      var logAfter = _jobLog.GetLogForMaterial(mat1.MaterialID, includeInvalidatedCycles: true);
      var invalidateEvt = logAfter.FirstOrDefault(e => e.LogType == LogType.InvalidateCycle);
      invalidateEvt.ShouldNotBeNull();
    }

    #endregion
  }

  public class LogOneSerialPerMaterialSpec : IDisposable
  {
    private RepositoryConfig _repoCfg;

    public LogOneSerialPerMaterialSpec()
    {
      var settings = new SerialSettings()
      {
        ConvertMaterialIDToSerial = (m) => SerialSettings.ConvertToBase62(m, 10),
      };
      _repoCfg = RepositoryConfig.InitializeMemoryDB(settings);
    }

    void IDisposable.Dispose()
    {
      _repoCfg.Dispose();
    }

    [Test]
    public void AllocateMatIds()
    {
      using var _jobLog = _repoCfg.OpenConnection();
      var now = DateTime.UtcNow;
      var matId = _jobLog.AllocateMaterialIDAndGenerateSerial(
        unique: "aaa",
        part: "bbb",
        numProc: 202,
        timeUTC: now,
        out var serialLogEntry
      );
      matId.ShouldBe(1);

      var expected1 = new LogEntry(
        cntr: 1,
        mat: new[]
        {
          MkLogMat.Mk(
            matID: 1,
            uniq: "aaa",
            proc: 0,
            part: "bbb",
            numProc: 202,
            serial: "0000000001",
            workorder: "",
            face: ""
          ),
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

      serialLogEntry.ShouldBeEquivalentTo(expected1);

      _jobLog
        .GetMaterialDetails(matID: 1)
        .ShouldBeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = 1,
            JobUnique = "aaa",
            PartName = "bbb",
            NumProcesses = 202,
            Serial = "0000000001",
          }
        );

      _jobLog.GetLogForSerial("0000000001").ShouldHaveSingleItem().ShouldBeEquivalentTo(expected1);

      var mat2 = _jobLog.AllocateMaterialIDWithSerialAndWorkorder(
        unique: "ttt",
        part: "zzz",
        numProc: 202,
        serial: "asdf",
        workorder: "www",
        timeUTC: now.AddSeconds(1),
        newLogEntries: out var newLogEvtsForMat2
      );

      mat2.ShouldBeEquivalentTo(
        new MaterialDetails()
        {
          MaterialID = 2,
          JobUnique = "ttt",
          PartName = "zzz",
          NumProcesses = 202,
          Serial = "asdf",
          Workorder = "www",
        }
      );

      newLogEvtsForMat2.EventsShouldBe(
        new[]
        {
          new LogEntry(
            cntr: 2,
            mat: new[]
            {
              MkLogMat.Mk(
                matID: 2,
                uniq: "ttt",
                proc: 0,
                part: "zzz",
                numProc: 202,
                serial: "asdf",
                workorder: "", // NOTE: workorder not filled in yet because serial recorded first
                face: ""
              ),
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
              MkLogMat.Mk(
                matID: 2,
                uniq: "ttt",
                proc: 0,
                part: "zzz",
                numProc: 202,
                serial: "asdf",
                workorder: "www",
                face: ""
              ),
            },
            pal: 0,
            ty: LogType.OrderAssignment,
            locName: "Order",
            locNum: 1,
            prog: "",
            start: false,
            endTime: now.AddSeconds(1),
            result: "www"
          ),
        }
      );

      _jobLog
        .GetLogForSerial("asdf")
        .EventsShouldBe(
          new[]
          {
            new LogEntry(
              cntr: 2,
              mat: new[]
              {
                MkLogMat.Mk(
                  matID: 2,
                  uniq: "ttt",
                  proc: 0,
                  part: "zzz",
                  numProc: 202,
                  serial: "asdf",
                  workorder: "www",
                  face: ""
                ),
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
                MkLogMat.Mk(
                  matID: 2,
                  uniq: "ttt",
                  proc: 0,
                  part: "zzz",
                  numProc: 202,
                  serial: "asdf",
                  workorder: "www",
                  face: ""
                ),
              },
              pal: 0,
              ty: LogType.OrderAssignment,
              locName: "Order",
              locNum: 1,
              prog: "",
              start: false,
              endTime: now.AddSeconds(1),
              result: "www"
            ),
          }
        );
    }

    [Test]
    public void ForeignID()
    {
      using var _jobLog = _repoCfg.OpenConnection();
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
        Counter = serialLogEntry.Counter,
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
            Face = 0,
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

      serialLogEntry.ShouldBeEquivalentTo(expected);

      _jobLog.MostRecentLogEntryForForeignID("for2").ShouldBeEquivalentTo(expected);
    }
  }

  public class LogStartingMaterialIDSpec
  {
    [Test]
    public void ConvertSerials()
    {
      var fixture = new Fixture();
      var matId = fixture.Create<long>();
      SerialSettings.ConvertFromBase62(SerialSettings.ConvertToBase62(matId)).ShouldBe(matId);
    }

    [Test]
    public void MaterialIDs()
    {
      using var repoCfg = RepositoryConfig.InitializeMemoryDB(
        new SerialSettings()
        {
          StartingMaterialID = SerialSettings.ConvertFromBase62("AbCd12"),
          ConvertMaterialIDToSerial = m => SerialSettings.ConvertToBase62(m),
        }
      );
      using var logDB = repoCfg.OpenConnection();
      long m1 = logDB.AllocateMaterialID("U1", "P1", 52);
      long m2 = logDB.AllocateMaterialID("U2", "P2", 66);
      long m3 = logDB.AllocateMaterialID("U3", "P3", 566);
      m1.ShouldBe(33_152_428_148);
      m2.ShouldBe(33_152_428_149);
      m3.ShouldBe(33_152_428_150);

      logDB
        .GetMaterialDetails(m1)
        .ShouldBeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = m1,
            JobUnique = "U1",
            PartName = "P1",
            NumProcesses = 52,
          }
        );
    }

    [Test]
    public void ErrorsTooLarge()
    {
      Should
        .Throw<Exception>(() =>
          RepositoryConfig.InitializeMemoryDB(
            new SerialSettings()
            {
              StartingMaterialID = SerialSettings.ConvertFromBase62("A000000000"),
              ConvertMaterialIDToSerial = m => SerialSettings.ConvertToBase62(m),
            }
          )
        )
        .Message.ShouldBe("Starting Serial is too large");
    }

    [Test]
    public void AdjustsStartingSerial()
    {
      using var repoCfg = RepositoryConfig.InitializeMemoryDB(
        new SerialSettings()
        {
          StartingMaterialID = SerialSettings.ConvertFromBase62("AbCd12"),
          ConvertMaterialIDToSerial = m => SerialSettings.ConvertToBase62(m),
        }
      );
      using var logFromCreate = repoCfg.OpenConnection();

      long m1 = logFromCreate.AllocateMaterialID("U1", "P1", 52);
      m1.ShouldBe(33_152_428_148);
    }

    [Test]
    public void AdjustsStartingSerial2()
    {
      using var repoCfg = RepositoryConfig.InitializeMemoryDB(
        new SerialSettings()
        {
          StartingMaterialID = SerialSettings.ConvertFromBase62("B3t24s"),
          ConvertMaterialIDToSerial = m => SerialSettings.ConvertToBase62(m),
        }
      );
      using var logFromUpgrade = repoCfg.OpenConnection();

      long m2 = logFromUpgrade.AllocateMaterialID("U1", "P1", 2);
      long m3 = logFromUpgrade.AllocateMaterialID("U2", "P2", 4);
      m2.ShouldBe(33_948_163_268);
      m3.ShouldBe(33_948_163_269);
    }

    [Test]
    public void AvoidsAdjustingSerialBackwards()
    {
      var guid = new Guid();
      using var repo1 = RepositoryConfig.InitializeMemoryDB(
        new SerialSettings()
        {
          StartingMaterialID = SerialSettings.ConvertFromBase62("AbCd12"),
          ConvertMaterialIDToSerial = m => SerialSettings.ConvertToBase62(m),
        },
        guid,
        createTables: true
      );

      using (var db = repo1.OpenConnection())
      {
        long m1 = db.AllocateMaterialID("U1", "P1", 52);
        m1.ShouldBe(33_152_428_148);
      }

      // repo2 uses the same guid so will be the same database, sharing it with repo1
      using var repo2 = RepositoryConfig.InitializeMemoryDB(
        new SerialSettings()
        {
          StartingMaterialID = SerialSettings.ConvertFromBase62("w53122"),
          ConvertMaterialIDToSerial = m => SerialSettings.ConvertToBase62(m),
        },
        guid,
        createTables: false
      );

      using (var db = repo2.OpenConnection())
      {
        long m2 = db.AllocateMaterialID("U1", "P1", 2);
        m2.ShouldBe(33_152_428_149);
      }
    }
  }
}
