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
using AutoFixture;
using BlackMaple.MachineFramework;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

#nullable enable

namespace BlackMaple.FMSInsight.Makino.Tests;

public sealed class LogBuilderSpec : IDisposable
{
  private readonly RepositoryConfig _repo;
  private readonly IMakinoDB _makinoDB;
  private readonly Fixture _fixture = new();
  private readonly List<LogEntry> _expectedLog = [];

  public LogBuilderSpec()
  {
    var serialSettings = new SerialSettings()
    {
      SerialType = SerialType.AssignOneSerialPerMaterial,
      ConvertMaterialIDToSerial = (m) => SerialSettings.ConvertToBase62(m, 10)
    };
    _repo = RepositoryConfig.InitializeMemoryDB(serialSettings);

    _makinoDB = Substitute.For<IMakinoDB>();
    _makinoDB.LoadCurrentInfo(null).ThrowsForAnyArgs(new Exception("Should not be called"));
    _makinoDB
      .QueryLoadUnloadResults(default, default)
      .ThrowsForAnyArgs(new Exception("Query Load not configured"));
    _makinoDB
      .QueryMachineResults(default, default)
      .ThrowsForAnyArgs(new Exception("Query machine not configured"));
    _makinoDB
      .QueryCommonValues(default)
      .ThrowsForAnyArgs(new Exception("Query common values not configured"));

    _makinoDB
      .Devices()
      .Returns(
        new Dictionary<int, PalletLocation>()
        {
          {
            1,
            new PalletLocation()
            {
              Location = PalletLocationEnum.LoadUnload,
              Num = 1,
              StationGroup = "L/U"
            }
          },
          {
            2,
            new PalletLocation()
            {
              Location = PalletLocationEnum.LoadUnload,
              Num = 2,
              StationGroup = "L/U"
            }
          },
          {
            3,
            new PalletLocation()
            {
              Location = PalletLocationEnum.Machine,
              Num = 1,
              StationGroup = "Mach"
            }
          },
          {
            4,
            new PalletLocation()
            {
              Location = PalletLocationEnum.Machine,
              Num = 2,
              StationGroup = "Mach"
            }
          },
        }
      );
  }

  void IDisposable.Dispose()
  {
    _repo.CloseMemoryConnection();
  }

  private record TestMat
  {
    public required string OrderName { get; init; }
    public required string PartName { get; init; }
    public required string Revision { get; init; }
    public required int Process { get; init; }

    public required int Quantity { get; init; }

    public required int PalletID { get; init; }
    public required int FixtureNum { get; init; }
    public required string FixtureName { get; init; }
    public required string FixtureComment { get; init; }

    public required long StartingMatID { get; init; }
  }

  private TestMat MkMat(int palId, int fixNum, long matId, int qty = 1)
  {
    return new TestMat()
    {
      OrderName = "order" + _fixture.Create<string>(),
      PartName = "part" + _fixture.Create<string>(),
      Revision = "rev" + _fixture.Create<string>(),
      Process = 1,
      Quantity = qty,
      PalletID = palId,
      FixtureNum = fixNum,
      FixtureName = "fix" + _fixture.Create<string>(),
      FixtureComment = "comment" + _fixture.Create<string>(),
      StartingMatID = matId
    };
  }

  private MachineResults Mach(
    DateTime start,
    int elapsedMin,
    int device,
    TestMat mat,
    string program,
    int spindleTime
  )
  {
    var machr = new MachineResults()
    {
      StartDateTimeLocal = start.ToLocalTime(),
      EndDateTimeLocal = start.ToLocalTime() + TimeSpan.FromMinutes(elapsedMin),
      StartDateTimeUTC = start.ToUniversalTime(),
      EndDateTimeUTC = start.ToUniversalTime() + TimeSpan.FromMinutes(elapsedMin),
      DeviceID = device,
      PalletID = mat.PalletID,
      FixtureNumber = mat.FixtureNum,
      FixtureName = mat.FixtureName,
      FixtureComment = mat.FixtureComment,
      OrderName = mat.OrderName,
      PartName = mat.PartName,
      Revision = mat.Revision,
      ProcessNum = mat.Process,
      JobNum = 1,
      ProcessName = "unused process name",
      Program = program,
      SpindleTimeSeconds = spindleTime,
      OperQuantities = mat.Quantity == 1 ? [1] : [1, mat.Quantity - 1]
    };

    _expectedLog.Add(
      new LogEntry()
      {
        Counter = 0,
        Material = Enumerable
          .Range(0, mat.Quantity)
          .Select(i => new LogMaterial()
          {
            MaterialID = mat.StartingMatID + i,
            JobUniqueStr = mat.OrderName,
            Serial = SerialSettings.ConvertToBase62(mat.StartingMatID + i, 10),
            Workorder = "",
            NumProcesses = 1,
            Face = 1,
            PartName = mat.PartName,
            Process = mat.Process,
            Path = null
          })
          .ToImmutableList(),
        LogType = LogType.MachineCycle,
        StartOfCycle = false,
        EndTimeUTC = start.ToUniversalTime() + TimeSpan.FromMinutes(elapsedMin),
        LocationName = "MC",
        LocationNum = device == 3 ? 1 : 2,
        Pallet = mat.PalletID,
        Program = program,
        Result = "",
        ElapsedTime = TimeSpan.FromMinutes(elapsedMin),
        ActiveOperationTime = TimeSpan.FromSeconds(spindleTime),
      }
    );

    return machr;
  }

  private WorkSetResults Load(
    DateTime start,
    int elapsedMin,
    int device,
    TestMat? loadMat,
    TestMat? unloadMat,
    int palCycleMin
  )
  {
    var workr = new WorkSetResults()
    {
      StartDateTimeUTC = start.ToUniversalTime(),
      EndDateTimeUTC = start.ToUniversalTime() + TimeSpan.FromMinutes(elapsedMin),
      DeviceID = device,
      PalletID = loadMat?.PalletID ?? unloadMat?.PalletID ?? 0,
      FixtureNumber = loadMat?.FixtureNum ?? unloadMat?.FixtureNum ?? 0,
      FixtureName = loadMat?.FixtureName ?? unloadMat?.FixtureName ?? "",
      FixtureComment = loadMat?.FixtureComment ?? unloadMat?.FixtureComment ?? "",
      UnloadOrderName = unloadMat?.OrderName,
      LoadOrderName = loadMat?.OrderName,
      UnloadPartName = unloadMat?.PartName,
      UnloadRevision = unloadMat?.Revision,
      UnloadProcessNum = unloadMat?.Process ?? 0,
      UnloadJobNum = 3,
      LoadPartName = loadMat?.PartName,
      LoadRevision = loadMat?.Revision,
      LoadProcessNum = loadMat?.Process ?? 0,
      LoadJobNum = 1,
      UnloadProcessName = "unused unload process name",
      LoadProcessName = "unused load process name",
      LoadQuantities =
        loadMat == null
          ? [0]
          : loadMat.Quantity == 1
            ? [1]
            : [1, loadMat.Quantity - 1],
      UnloadNormalQuantities =
        unloadMat == null
          ? [0]
          : unloadMat.Quantity == 1
            ? [1]
            : [1, unloadMat.Quantity - 1],
      UnloadScrapQuantities = [],
      UnloadOutProcQuantities = [],
      Remachine = false
    };

    foreach (var mat in new[] { loadMat, unloadMat })
    {
      if (mat == null)
        continue;
      _expectedLog.Add(
        new LogEntry()
        {
          Counter = 0,
          Material = Enumerable
            .Range(0, mat.Quantity)
            .Select(i => new LogMaterial()
            {
              MaterialID = mat.StartingMatID + i,
              JobUniqueStr = mat.OrderName,
              Serial = SerialSettings.ConvertToBase62(mat.StartingMatID + i, 10),
              Workorder = "",
              NumProcesses = 1,
              Face = 1,
              PartName = mat.PartName,
              Process = mat.Process,
              Path = null
            })
            .ToImmutableList(),
          LogType = LogType.LoadUnloadCycle,
          StartOfCycle = false,
          EndTimeUTC =
            start.ToUniversalTime()
            + TimeSpan.FromMinutes(elapsedMin)
            + (mat == loadMat ? TimeSpan.FromSeconds(1) : TimeSpan.Zero),
          LocationName = "L/U",
          LocationNum = device,
          Pallet = loadMat?.PalletID ?? unloadMat!.PalletID,
          Program = loadMat == mat ? "LOAD" : "UNLOAD",
          Result = loadMat == mat ? "LOAD" : "UNLOAD",
          ElapsedTime = TimeSpan.FromMinutes(elapsedMin) / 2,
          ActiveOperationTime = TimeSpan.FromMinutes(elapsedMin) / 2,
        }
      );
    }

    if (loadMat != null)
    {
      for (int i = 0; i < loadMat.Quantity; i++)
      {
        _expectedLog.Add(
          new LogEntry()
          {
            Counter = 0,
            Material =
            [
              new LogMaterial()
              {
                MaterialID = loadMat.StartingMatID + i,
                JobUniqueStr = loadMat.OrderName,
                Serial = SerialSettings.ConvertToBase62(loadMat.StartingMatID + i, 10),
                Workorder = "",
                NumProcesses = 1,
                Face = 0,
                PartName = loadMat.PartName,
                Process = 0,
                Path = null
              }
            ],
            LogType = LogType.PartMark,
            StartOfCycle = false,
            EndTimeUTC = start.ToUniversalTime() + TimeSpan.FromMinutes(elapsedMin) + TimeSpan.FromSeconds(1),
            LocationName = "Mark",
            LocationNum = 1,
            Pallet = 0,
            Program = "MARK",
            Result = SerialSettings.ConvertToBase62(loadMat.StartingMatID + i, 10),
            ElapsedTime = TimeSpan.FromMinutes(-1),
            ActiveOperationTime = TimeSpan.Zero
          }
        );
      }
    }

    _expectedLog.Add(
      new LogEntry()
      {
        Counter = 0,
        Material = [],
        LogType = LogType.PalletCycle,
        StartOfCycle = false,
        EndTimeUTC = start.ToUniversalTime() + TimeSpan.FromMinutes(elapsedMin),
        LocationName = "Pallet Cycle",
        LocationNum = 1,
        Pallet = loadMat?.PalletID ?? unloadMat!.PalletID,
        Program = "",
        Result = "PalletCycle",
        ElapsedTime = TimeSpan.FromMinutes(palCycleMin),
        ActiveOperationTime = TimeSpan.Zero,
      }
    );

    return workr;
  }

  [Fact]
  public void NoLogOnEmpty()
  {
    using var db = _repo.OpenConnection();

    var lastDate = DateTime.UtcNow.AddDays(-30);

    _makinoDB
      .QueryLoadUnloadResults(
        lastDate,
        Arg.Is<DateTime>(x =>
          Math.Abs(x.Subtract(DateTime.UtcNow.AddMinutes(1)).Ticks) < TimeSpan.FromSeconds(2).Ticks
        )
      )
      .Returns([]);
    _makinoDB
      .QueryMachineResults(
        lastDate,
        Arg.Is<DateTime>(x =>
          Math.Abs(x.Subtract(DateTime.UtcNow.AddMinutes(1)).Ticks) < TimeSpan.FromSeconds(2).Ticks
        )
      )
      .Returns([]);

    new LogBuilder(_makinoDB, db).CheckLogs(lastDate).Should().BeFalse();

    db.MaxLogDate().Should().Be(DateTime.MinValue);
  }

  [Fact]
  public void SingleCycle()
  {
    var mat = MkMat(palId: 2, fixNum: 4, matId: 1);

    var now = DateTime.UtcNow;
    var start = now.AddHours(-2);

    _makinoDB
      .QueryLoadUnloadResults(now.AddDays(-30), Arg.Any<DateTime>())
      .Returns(
        [
          Load(start, elapsedMin: 10, device: 1, loadMat: mat, unloadMat: null, palCycleMin: 0),
          Load(start.AddMinutes(30), elapsedMin: 5, device: 2, loadMat: null, unloadMat: mat, palCycleMin: 25)
        ]
      );

    List<MachineResults> machs =
    [
      Mach(start.AddMinutes(15), elapsedMin: 11, device: 3, mat: mat, program: "prog1", spindleTime: 5)
    ];

    _makinoDB.QueryMachineResults(now.AddDays(-30), Arg.Any<DateTime>()).Returns(machs);

    _makinoDB.QueryCommonValues(Arg.Is(machs[0])).Returns([]);

    using var db = _repo.OpenConnection();

    new LogBuilder(_makinoDB, db).CheckLogs(now.AddDays(-30)).Should().BeTrue();

    db.GetLogEntries(start, now)
      .Should()
      .BeEquivalentTo(_expectedLog, options => options.Excluding(x => x.Counter));
  }

  [Fact]
  public void MultiplePallets()
  {
    var mat1 = MkMat(palId: 2, fixNum: 4, matId: 1);
    var mat2 = MkMat(palId: 3, fixNum: 5, matId: 2);

    var now = DateTime.UtcNow;
    var start = now.AddHours(-5);

    _makinoDB
      .QueryLoadUnloadResults(now.AddDays(-30), Arg.Any<DateTime>())
      .Returns(
        [
          Load(start, elapsedMin: 10, device: 1, loadMat: mat1, unloadMat: null, palCycleMin: 0),
          Load(
            start.AddMinutes(30),
            elapsedMin: 5,
            device: 2,
            loadMat: mat2,
            unloadMat: null,
            palCycleMin: 0
          ),
          Load(
            start.AddMinutes(60),
            elapsedMin: 10,
            device: 1,
            loadMat: null,
            unloadMat: mat2,
            palCycleMin: 35
          ),
          Load(
            start.AddMinutes(90),
            elapsedMin: 5,
            device: 2,
            loadMat: null,
            unloadMat: mat1,
            palCycleMin: 85
          )
        ]
      );

    _makinoDB
      .QueryMachineResults(now.AddDays(-30), Arg.Any<DateTime>())
      .Returns(
        [
          Mach(start.AddMinutes(15), elapsedMin: 11, device: 3, mat: mat1, program: "prog1", spindleTime: 5),
          Mach(start.AddMinutes(45), elapsedMin: 6, device: 4, mat: mat2, program: "prog2", spindleTime: 3)
        ]
      );

    _makinoDB.QueryCommonValues(Arg.Any<MachineResults>()).Returns([]);

    using var db = _repo.OpenConnection();

    new LogBuilder(_makinoDB, db).CheckLogs(now.AddDays(-30)).Should().BeTrue();
    db.GetLogEntries(start, now)
      .Should()
      .BeEquivalentTo(_expectedLog, options => options.Excluding(x => x.Counter));
  }

  [Fact]
  public void MultiplePartsOnPallet()
  {
    var mat1 = MkMat(palId: 2, fixNum: 4, matId: 1, qty: 2);
    var mat2 = MkMat(palId: 2, fixNum: 4, matId: 3, qty: 2);
    var mat3 = MkMat(palId: 2, fixNum: 4, matId: 5, qty: 2);

    var now = DateTime.UtcNow;
    var start = now.AddHours(-5);

    _makinoDB
      .QueryLoadUnloadResults(now.AddDays(-30), Arg.Any<DateTime>())
      .Returns(
        [
          // load 1
          Load(start, elapsedMin: 10, device: 1, loadMat: mat1, unloadMat: null, palCycleMin: 0),
          // unload 1, load 2
          Load(
            start.AddMinutes(30),
            elapsedMin: 5,
            device: 2,
            loadMat: mat2,
            unloadMat: mat1,
            palCycleMin: 25
          ),
          // unload 2, load 3
          Load(
            start.AddMinutes(60),
            elapsedMin: 10,
            device: 1,
            loadMat: mat3,
            unloadMat: mat2,
            palCycleMin: 35
          ),
          // unload 3
          Load(
            start.AddMinutes(90),
            elapsedMin: 5,
            device: 2,
            loadMat: null,
            unloadMat: mat3,
            palCycleMin: 25
          ),
        ]
      );

    _makinoDB
      .QueryMachineResults(now.AddDays(-30), Arg.Any<DateTime>())
      .Returns(
        [
          Mach(start.AddMinutes(15), elapsedMin: 11, device: 3, mat: mat1, program: "prog1", spindleTime: 5),
          Mach(start.AddMinutes(45), elapsedMin: 6, device: 4, mat: mat2, program: "prog2", spindleTime: 3),
          Mach(start.AddMinutes(75), elapsedMin: 11, device: 3, mat: mat3, program: "prog3", spindleTime: 5)
        ]
      );

    _makinoDB.QueryCommonValues(Arg.Any<MachineResults>()).Returns([]);

    using var db = _repo.OpenConnection();

    new LogBuilder(_makinoDB, db).CheckLogs(now.AddDays(-30)).Should().BeTrue();

    db.GetLogEntries(start, now)
      .Should()
      .BeEquivalentTo(_expectedLog, options => options.Excluding(x => x.Counter));
  }

  [Fact]
  public void RecordsCommonValues()
  {
    var mat = MkMat(palId: 2, fixNum: 4, matId: 1);

    var now = DateTime.UtcNow;
    var start = now.AddHours(-2);

    _makinoDB
      .QueryLoadUnloadResults(now.AddDays(-30), Arg.Any<DateTime>())
      .Returns(
        [
          Load(start, elapsedMin: 10, device: 1, loadMat: mat, unloadMat: null, palCycleMin: 0),
          Load(start.AddMinutes(30), elapsedMin: 5, device: 2, loadMat: null, unloadMat: mat, palCycleMin: 25)
        ]
      );

    List<MachineResults> machs =
    [
      Mach(start.AddMinutes(15), elapsedMin: 5, device: 3, mat: mat, program: "prog1", spindleTime: 5)
    ];

    _makinoDB.QueryMachineResults(now.AddDays(-30), Arg.Any<DateTime>()).Returns(machs);

    _makinoDB
      .QueryCommonValues(Arg.Is(machs[0]))
      .Returns(
        [
          new CommonValue()
          {
            ParentMachineResults = machs[0],
            ExecDateTimeUTC = machs[0].StartDateTimeUTC,
            Number = 23,
            Value = "common value 23"
          },
          new CommonValue()
          {
            ParentMachineResults = machs[0],
            ExecDateTimeUTC = machs[0].StartDateTimeUTC,
            Number = 24,
            Value = "common value 24"
          }
        ]
      );

    var mcIdx = _expectedLog.FindIndex(x => x.LogType == LogType.MachineCycle);

    _expectedLog[mcIdx] = _expectedLog[mcIdx] with
    {
      ProgramDetails = ImmutableDictionary<string, string>
        .Empty.Add("23", "common value 23")
        .Add("24", "common value 24")
    };

    using var db = _repo.OpenConnection();

    new LogBuilder(_makinoDB, db).CheckLogs(now.AddDays(-30)).Should().BeTrue();

    db.GetLogEntries(start, now)
      .Should()
      .BeEquivalentTo(_expectedLog, options => options.Excluding(x => x.Counter));
  }

  [Fact]
  public void SignalsInspection()
  {
    using var db = _repo.OpenConnection();
    var mat = MkMat(palId: 2, fixNum: 4, matId: 1);

    db.AddJobs(
      new NewJobs()
      {
        ScheduleId = "abc",
        Jobs =
        [
          new Job()
          {
            UniqueStr = mat.OrderName,
            RouteStartUTC = DateTime.UtcNow,
            RouteEndUTC = DateTime.UtcNow.AddHours(1),
            Archived = false,
            PartName = mat.PartName,
            Cycles = 10,
            Processes =
            [
              new ProcessInfo()
              {
                Paths =
                [
                  new ProcPathInfo()
                  {
                    PalletNums = [2],
                    Load = [1, 2],
                    Unload = [1, 2],
                    ExpectedLoadTime = TimeSpan.FromMinutes(10),
                    ExpectedUnloadTime = TimeSpan.FromMinutes(10),
                    SimulatedAverageFlowTime = TimeSpan.FromMinutes(10),
                    SimulatedProduction = [],
                    SimulatedStartingUTC = DateTime.UtcNow,
                    PartsPerPallet = 1,
                    Stops =
                    [
                      new MachiningStop()
                      {
                        StationGroup = "Mach",
                        Stations = [3],
                        ExpectedCycleTime = TimeSpan.FromMinutes(5),
                      }
                    ],
                    Inspections =
                    [
                      new PathInspection()
                      {
                        InspectionType = "CMM",
                        Counter = $"CMM,{mat.PartName},%stat1,1%",
                        MaxVal = 10,
                        RandomFreq = 0,
                        TimeInterval = TimeSpan.Zero
                      }
                    ]
                  }
                ]
              }
            ]
          }
        ]
      },
      expectedPreviousScheduleId: null,
      addAsCopiedToSystem: true
    );

    var now = DateTime.UtcNow;
    var start = now.AddHours(-2);

    _makinoDB
      .QueryLoadUnloadResults(now.AddDays(-30), Arg.Any<DateTime>())
      .Returns(
        [
          Load(start, elapsedMin: 10, device: 2, loadMat: mat, unloadMat: null, palCycleMin: 0),
          Load(start.AddMinutes(30), elapsedMin: 5, device: 2, loadMat: null, unloadMat: mat, palCycleMin: 25)
        ]
      );

    _makinoDB
      .QueryMachineResults(now.AddDays(-30), Arg.Any<DateTime>())
      .Returns(
        [Mach(start.AddMinutes(15), elapsedMin: 5, device: 3, mat: mat, program: "prog1", spindleTime: 5)]
      );

    _makinoDB.QueryCommonValues(Arg.Any<MachineResults>()).Returns([]);

    _expectedLog.Add(
      new LogEntry()
      {
        Counter = 0,
        Material =
        [
          new LogMaterial()
          {
            MaterialID = mat.StartingMatID,
            JobUniqueStr = mat.OrderName,
            Serial = SerialSettings.ConvertToBase62(mat.StartingMatID, 10),
            Workorder = "",
            NumProcesses = 1,
            Face = 0,
            PartName = mat.PartName,
            Process = mat.Process,
            Path = null
          }
        ],
        LogType = LogType.Inspection,
        StartOfCycle = false,
        EndTimeUTC = start.ToUniversalTime() + TimeSpan.FromMinutes(15 + 5),
        LocationName = "Inspect",
        LocationNum = 1,
        Pallet = 0,
        Program = $"CMM,{mat.PartName},1",
        Result = "False",
        ElapsedTime = TimeSpan.FromMinutes(-1),
        ActiveOperationTime = TimeSpan.Zero,
        ProgramDetails = ImmutableDictionary<string, string>
          .Empty.Add(
            "ActualPath",
            """
            [{"MaterialID":1,"Process":1,"Pallet":2,"LoadStation":2,"Stops":[{"StationName":"MC","StationNum":1}],"UnloadStation":-1}]
            """
          )
          .Add("InspectionType", "CMM")
      }
    );

    new LogBuilder(_makinoDB, db).CheckLogs(now.AddDays(-30)).Should().BeTrue();

    db.GetLogEntries(start, now)
      .Should()
      .BeEquivalentTo(_expectedLog, options => options.Excluding(x => x.Counter));

    db.LookupInspectionDecisions(mat.StartingMatID)
      .Should()
      .BeEquivalentTo(
        [
          new Decision()
          {
            Counter = $"CMM,{mat.PartName},1",
            CreateUTC = start.ToUniversalTime() + TimeSpan.FromMinutes(15 + 5),
            Forced = false,
            InspType = "CMM",
            Inspect = false,
            MaterialID = mat.StartingMatID,
          }
        ]
      );
  }
}
