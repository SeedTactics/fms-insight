/* Copyright (c) 2021, John Lenz

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
using BlackMaple.MachineFramework;
using Xunit;
using FluentAssertions;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Germinate;

namespace MachineWatchTest
{
  public class EventDBUpgradeSpec : IDisposable
  {
    private readonly IRepository _log;
    private string _tempLogFile;
    private string _tempJobFile;

    public EventDBUpgradeSpec()
    {
      _tempLogFile = System.IO.Path.GetTempFileName();
      System.IO.File.Copy("log.v17.db", _tempLogFile, overwrite: true);
      _tempJobFile = System.IO.Path.GetTempFileName();
      System.IO.File.Copy("job.v16.db", _tempJobFile, overwrite: true);
      _log = RepositoryConfig
        .InitializeEventDatabase(new SerialSettings(), _tempLogFile, null, _tempJobFile)
        .OpenConnection();
    }

    public void Dispose()
    {
      _log.Dispose();
      Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
      if (!string.IsNullOrEmpty(_tempLogFile) && System.IO.File.Exists(_tempLogFile))
        System.IO.File.Delete(_tempLogFile);
      if (!string.IsNullOrEmpty(_tempJobFile) && System.IO.File.Exists(_tempJobFile))
        System.IO.File.Delete(_tempJobFile);
    }

    [Fact]
    public void ConvertsMaterialFromV17()
    {
      // existing v17 file has the following data in it
      var now = new DateTime(2018, 7, 12, 5, 6, 7, DateTimeKind.Utc);

      var mat1_1 = new LogMaterial(1, "uuu1", 1, "part1", 2, "serial1", "work1", face: "A");
      var mat1_2 = new LogMaterial(1, "uuu1", 2, "part1", 2, "serial1", "work1", face: "B");
      var mat2_1 = new LogMaterial(2, "uuu1", 1, "part1", 2, "serial2", "", face: "C");
      var mat2_2 = new LogMaterial(2, "uuu1", 1, "part1", 2, "serial2", "", face: "D");
      var mat3 = new LogMaterial(3, "uuu2", 1, "part2", 1, "", "work3", face: "E");

      _log.GetLogEntries(now, now.AddDays(1))
        .ToList()
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new LogEntry(
              cntr: -1,
              mat: new[] { mat1_1, mat2_1 },
              pal: "3",
              ty: LogType.MachineCycle,
              locName: "MC",
              locNum: 1,
              prog: "proggg",
              start: false,
              endTime: now,
              result: "result",
              endOfRoute: false
            ),
            new LogEntry(
              cntr: -1,
              mat: new[] { mat1_2, mat2_2 },
              pal: "5",
              ty: LogType.MachineCycle,
              locName: "MC",
              locNum: 1,
              prog: "proggg2",
              start: false,
              endTime: now.AddMinutes(10),
              result: "result2",
              endOfRoute: false
            ),
            new LogEntry(
              cntr: -1,
              mat: new[] { mat1_1 },
              pal: "",
              ty: LogType.PartMark,
              locName: "Mark",
              locNum: 1,
              prog: "MARK",
              start: false,
              endTime: now.AddMinutes(20),
              result: "serial1",
              endOfRoute: false
            ),
            new LogEntry(
              cntr: -1,
              mat: new[] { mat1_1 },
              pal: "",
              ty: LogType.OrderAssignment,
              locName: "Order",
              locNum: 1,
              prog: "",
              start: false,
              endTime: now.AddMinutes(30),
              result: "work1",
              endOfRoute: false
            ),
            new LogEntry(
              cntr: -1,
              mat: new[] { mat2_2 },
              pal: "",
              ty: LogType.PartMark,
              locName: "Mark",
              locNum: 1,
              prog: "MARK",
              start: false,
              endTime: now.AddMinutes(40),
              result: "serial2",
              endOfRoute: false
            ),
            new LogEntry(
              cntr: -1,
              mat: new[] { mat3 },
              pal: "1",
              ty: LogType.LoadUnloadCycle,
              locName: "L/U",
              locNum: 5,
              prog: "LOAD",
              start: false,
              endTime: now.AddMinutes(50),
              result: "LOAD",
              endOfRoute: false
            ),
            new LogEntry(
              cntr: -1,
              mat: new[] { mat3 },
              pal: "",
              ty: LogType.OrderAssignment,
              locName: "Order",
              locNum: 1,
              prog: "",
              start: false,
              endTime: now.AddMinutes(60),
              result: "work3",
              endOfRoute: false
            ),
          },
          options => options.Excluding(x => x.Counter).ComparingByMembers<LogEntry>()
        );

      _log.GetMaterialDetails(1)
        .Should()
        .BeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = 1,
            JobUnique = "uuu1",
            PartName = "part1",
            NumProcesses = 2,
            Workorder = "work1",
            Serial = "serial1",
          }
        );
      _log.GetMaterialDetails(2)
        .Should()
        .BeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = 2,
            JobUnique = "uuu1",
            PartName = "part1",
            NumProcesses = 2,
            Workorder = null,
            Serial = "serial2",
          }
        );
      _log.GetMaterialDetails(3)
        .Should()
        .BeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = 3,
            JobUnique = "uuu2",
            PartName = "part2",
            NumProcesses = 1,
            Workorder = "work3",
            Serial = null,
          }
        );
    }

    [Fact]
    public void QueueTablesCorrectlyCreated()
    {
      var now = new DateTime(2018, 7, 12, 5, 6, 7, DateTimeKind.Utc);
      var matId = _log.AllocateMaterialID("uuu5", "part5", 1);
      var mat = new LogMaterial(matId, "uuu5", 1, "part5", 1, "", "", "");

      _log.RecordAddMaterialToQueue(
        EventLogMaterial.FromLogMat(mat),
        "queue",
        5,
        null,
        null,
        now.AddHours(2)
      );

      _log.GetMaterialInAllQueues()
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new QueuedMaterial()
            {
              MaterialID = matId,
              Queue = "queue",
              Position = 0,
              Unique = "uuu5",
              PartNameOrCasting = "part5",
              NumProcesses = 1,
              NextProcess = 2,
              Paths = ImmutableDictionary<int, int>.Empty,
              AddTimeUTC = now.AddHours(2)
            }
          }
        );
    }

    [Fact]
    public void ConvertsJobFromV16()
    {
      var expected = CreateJob();
      var actual = _log.LoadJob("Unique1");

      actual.Processes[0].Should().BeEquivalentTo(expected.Processes[0]);
      actual.Should().BeEquivalentTo(expected);

      _log.LoadDecrementsForJob("Unique1")
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new DecrementQuantity()
            {
              DecrementId = 12,
              TimeUTC = new DateTime(2020, 10, 22, 4, 5, 6, DateTimeKind.Utc),
              Quantity = 123
            }
          }
        );
    }

    [Fact]
    public void CreatesNewJob()
    {
      var old = CreateJob();
      var newJob = old with
      {
        UniqueStr = "mynewunique",
        Decrements = ImmutableList<DecrementQuantity>.Empty,
        Processes = old.Processes.ConvertAll(
          p => p with { Paths = p.Paths.ConvertAll(path => path with { Inspections = null }) }
        ),
      };

      _log.AddJobs(
        new NewJobs() { ScheduleId = newJob.ScheduleId + "newSch", Jobs = ImmutableList.Create<Job>(newJob) },
        null,
        addAsCopiedToSystem: true
      );

      var actual = _log.LoadJob("mynewunique");

      actual.Should().BeEquivalentTo(newJob with { ScheduleId = newJob.ScheduleId + "newSch" });

      var now = DateTime.UtcNow;
      _log.AddNewDecrement(
        new[]
        {
          new NewDecrementQuantity()
          {
            JobUnique = "mynewunique",
            Part = "thepart",
            Quantity = 88
          }
        },
        now
      );

      _log.LoadDecrementsForJob("mynewunique")
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new DecrementQuantity()
            {
              DecrementId = 13, // existing old job had decrement id 12
              TimeUTC = now,
              Quantity = 88
            }
          }
        );
    }

    private HistoricJob CreateJob()
    {
      var routeStart = DateTime.Parse("2019-10-22 20:24 GMT").ToUniversalTime();
      return new HistoricJob()
      {
        UniqueStr = "Unique1",
        PartName = "Job1",
        Cycles = 178,
        CopiedToSystem = true,
        Decrements = ImmutableList.Create(
          new DecrementQuantity()
          {
            DecrementId = 12,
            TimeUTC = new DateTime(2020, 10, 22, 4, 5, 6, DateTimeKind.Utc),
            Quantity = 123
          }
        ),
        RouteStartUTC = routeStart,
        RouteEndUTC = routeStart.AddHours(100),
        Archived = false,
        ScheduleId = "Job1tag1245",
        Comment = "Hello there",
        BookingIds = ImmutableList.Create("booking1", "booking2", "booking3"),
        HoldJob = new HoldPattern()
        {
          UserHold = true,
          ReasonForUserHold = "test string",
          HoldUnholdPatternStartUTC = routeStart,
          HoldUnholdPatternRepeats = true,
          HoldUnholdPattern = ImmutableList.Create(
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(18),
            TimeSpan.FromMinutes(125)
          )
        },
        Processes = ImmutableList.Create(
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(
              new ProcPathInfo()
              {
                PartsPerPallet = 10,
                InputQueue = "in11",
                SimulatedStartingUTC = DateTime.Parse("1/5/2011 11:34 PM GMT").ToUniversalTime(),
                SimulatedAverageFlowTime = TimeSpan.FromMinutes(0.5),
                SimulatedProduction = ImmutableList<SimulatedProduction>.Empty,
                Pallets = ImmutableList.Create("Pal2", "Pal5"),
                Fixture = "Fix1",
                Face = 1,
                Load = ImmutableList.Create(35, 64),
                ExpectedLoadTime = TimeSpan.FromSeconds(100),
                Unload = ImmutableList.Create(75, 234),
                ExpectedUnloadTime = TimeSpan.FromSeconds(13),
                Stops = ImmutableList.Create(
                  new MachiningStop()
                  {
                    StationGroup = "Machine",
                    Stations = ImmutableList.Create(12, 23),
                    Program = "Emily",
                    ExpectedCycleTime = TimeSpan.FromHours(1.2),
                    Tools = ImmutableDictionary<string, TimeSpan>.Empty
                      .Add("tool1", TimeSpan.FromMinutes(30))
                      .Add("tool2", TimeSpan.FromMinutes(35)),
                  }
                ),
                Inspections = ImmutableList.Create(
                  new PathInspection()
                  {
                    InspectionType = "Insp1",
                    Counter = "counter1",
                    MaxVal = 53,
                    RandomFreq = -1,
                    TimeInterval = TimeSpan.FromMinutes(100)
                  },
                  new PathInspection()
                  {
                    InspectionType = "Insp3",
                    Counter = "abcdef",
                    MaxVal = 175,
                    RandomFreq = -1,
                    TimeInterval = TimeSpan.FromMinutes(121)
                  }
                ),
                HoldMachining = new HoldPattern()
                {
                  UserHold = false,
                  ReasonForUserHold = "reason for user hold",
                  HoldUnholdPatternStartUTC = DateTime.Parse("2010/5/4 12:32 AM GMT").ToUniversalTime(),
                  HoldUnholdPatternRepeats = false,
                  HoldUnholdPattern = ImmutableList.Create(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(53))
                },
                HoldLoadUnload = new HoldPattern()
                {
                  UserHold = true,
                  ReasonForUserHold = "abcdef",
                  HoldUnholdPatternStartUTC = DateTime.Parse("2010/12/2 9:32 PM GMT").ToUniversalTime(),
                  HoldUnholdPatternRepeats = true,
                  HoldUnholdPattern = ImmutableList.Create(TimeSpan.FromMinutes(63), TimeSpan.FromMinutes(7))
                },
              },
              new ProcPathInfo()
              {
                PartsPerPallet = 15,
                OutputQueue = "out12",
                SimulatedStartingUTC = DateTime.Parse("2/10/2011 12:45 AM GMT").ToUniversalTime(),
                SimulatedAverageFlowTime = TimeSpan.FromMinutes(1.5),
                SimulatedProduction = ImmutableList<SimulatedProduction>.Empty,
                Pallets = ImmutableList.Create("Pal4", "Pal35"),
                Fixture = "ABC",
                Face = 4,
                Load = ImmutableList.Create(785, 15),
                ExpectedLoadTime = TimeSpan.FromMinutes(53),
                Unload = ImmutableList.Create(53),
                ExpectedUnloadTime = TimeSpan.FromMinutes(12),
                Stops = ImmutableList.Create(
                  new MachiningStop()
                  {
                    StationGroup = "Other Machine",
                    Stations = ImmutableList.Create(23, 12),
                    Program = "awef",
                    ExpectedCycleTime = TimeSpan.FromHours(2.8),
                    Tools = ImmutableDictionary<string, TimeSpan>.Empty
                      .Add("tool1", TimeSpan.FromMinutes(9))
                      .Add("tool33", TimeSpan.FromMinutes(42)),
                  }
                ),
                Inspections = ImmutableList.Create(
                  new PathInspection()
                  {
                    InspectionType = "Insp1",
                    Counter = "counter1",
                    MaxVal = 53,
                    RandomFreq = -1,
                    TimeInterval = TimeSpan.FromMinutes(100)
                  },
                  new PathInspection()
                  {
                    InspectionType = "Insp3",
                    Counter = "abcdef",
                    MaxVal = 175,
                    RandomFreq = -1,
                    TimeInterval = TimeSpan.FromMinutes(121)
                  }
                ),
                HoldMachining = new HoldPattern()
                {
                  UserHold = true,
                  ReasonForUserHold = "another reason for user hold",
                  HoldUnholdPatternStartUTC = DateTime.Parse("2010/5/12 11:12 PM GMT").ToUniversalTime(),
                  HoldUnholdPatternRepeats = true,
                  HoldUnholdPattern = ImmutableList.Create(TimeSpan.FromMinutes(84), TimeSpan.FromMinutes(1))
                },
                HoldLoadUnload = new HoldPattern()
                {
                  UserHold = false,
                  ReasonForUserHold = "agr",
                  HoldUnholdPatternStartUTC = DateTime.Parse("2010/6/1 8:12 PM GMT").ToUniversalTime(),
                  HoldUnholdPatternRepeats = false,
                  HoldUnholdPattern = ImmutableList.Create(
                    TimeSpan.FromMinutes(174),
                    TimeSpan.FromMinutes(83)
                  )
                },
              }
            )
          },
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(
              new ProcPathInfo()
              {
                PartsPerPallet = 20,
                InputQueue = "in21",
                SimulatedStartingUTC = DateTime.Parse("3/14/2011 2:03 AM GMT").ToUniversalTime(),
                SimulatedAverageFlowTime = TimeSpan.FromMinutes(2.5),
                SimulatedProduction = ImmutableList<SimulatedProduction>.Empty,
                Pallets = ImmutableList.Create("Pal12", "Pal64"),
                Fixture = "Fix123",
                Face = 6,
                Load = ImmutableList.Create(647, 474),
                ExpectedLoadTime = TimeSpan.FromHours(52),
                Unload = ImmutableList.Create(563),
                ExpectedUnloadTime = TimeSpan.FromHours(63),
                Stops = ImmutableList.Create(
                  new MachiningStop()
                  {
                    StationGroup = "Test",
                    Stations = ImmutableList.Create(64, 323),
                    Program = "Goodbye",
                    ExpectedCycleTime = TimeSpan.FromHours(6.3),
                    Tools = ImmutableDictionary<string, TimeSpan>.Empty
                      .Add("tool2", TimeSpan.FromMinutes(12))
                      .Add("tool44", TimeSpan.FromMinutes(99)),
                  },
                  new MachiningStop()
                  {
                    StationGroup = "Test",
                    Stations = ImmutableList.Create(245, 36),
                    Program = "dduuude",
                  }
                ),
                Inspections = ImmutableList.Create(
                  new PathInspection()
                  {
                    InspectionType = "Insp2",
                    Counter = "counter1",
                    MaxVal = 12,
                    RandomFreq = -1,
                    TimeInterval = TimeSpan.FromMinutes(64)
                  },
                  new PathInspection()
                  {
                    InspectionType = "Insp4",
                    Counter = "counter2",
                    RandomFreq = 16.12,
                    MaxVal = -1,
                    TimeInterval = TimeSpan.FromMinutes(33)
                  },
                  new PathInspection()
                  {
                    InspectionType = "Insp5",
                    Counter = "counter3",
                    RandomFreq = 0.544,
                    MaxVal = -1,
                    TimeInterval = TimeSpan.FromMinutes(44)
                  }
                ),
                HoldMachining = new HoldPattern()
                {
                  UserHold = false,
                  ReasonForUserHold = "oh my reason for user hold",
                  HoldUnholdPatternStartUTC = DateTime.Parse("2010/9/1 6:30 PM GMT").ToUniversalTime(),
                  HoldUnholdPatternRepeats = true,
                  HoldUnholdPattern = ImmutableList.Create(
                    TimeSpan.FromMinutes(532),
                    TimeSpan.FromMinutes(64)
                  )
                },
                HoldLoadUnload = new HoldPattern()
                {
                  HoldUnholdPatternStartUTC = DateTime.Parse("2000-01-01")
                },
              },
              new ProcPathInfo()
              {
                PartsPerPallet = 22,
                SimulatedStartingUTC = DateTime.Parse("4/20/2011 3:22 PM GMT").ToUniversalTime(),
                SimulatedAverageFlowTime = TimeSpan.FromMinutes(3.5),
                SimulatedProduction = ImmutableList<SimulatedProduction>.Empty,
                Pallets = ImmutableList.Create("Hi", "Pal2"),
                // has non-integer face so should be ignored
                Load = ImmutableList.Create(785, 53),
                ExpectedLoadTime = TimeSpan.FromSeconds(98),
                Unload = ImmutableList.Create(2, 12),
                ExpectedUnloadTime = TimeSpan.FromSeconds(73),
                Stops = ImmutableList.Create(
                  new MachiningStop()
                  {
                    StationGroup = "Test",
                    Stations = ImmutableList.Create(32, 64),
                    Program = "wefq",
                  },
                  new MachiningStop()
                  {
                    StationGroup = "Test",
                    Stations = ImmutableList.Create(23, 53),
                    Program = "so cool",
                  }
                ),
                Inspections = ImmutableList.Create(
                  new PathInspection()
                  {
                    InspectionType = "Insp2",
                    Counter = "counter1",
                    MaxVal = 12,
                    RandomFreq = -1,
                    TimeInterval = TimeSpan.FromMinutes(64)
                  },
                  new PathInspection()
                  {
                    InspectionType = "Insp4",
                    Counter = "counter2",
                    RandomFreq = 16.12,
                    MaxVal = -1,
                    TimeInterval = TimeSpan.FromMinutes(33)
                  },
                  new PathInspection()
                  {
                    InspectionType = "Insp5",
                    Counter = "counter3",
                    RandomFreq = 0.544,
                    MaxVal = -1,
                    TimeInterval = TimeSpan.FromMinutes(44)
                  }
                ),
                HoldMachining = new HoldPattern()
                {
                  HoldUnholdPatternStartUTC = DateTime.Parse("2000-01-01")
                },
                HoldLoadUnload = new HoldPattern()
                {
                  HoldUnholdPatternStartUTC = DateTime.Parse("2000-01-01")
                },
              },
              new ProcPathInfo()
              {
                PartsPerPallet = 23,
                OutputQueue = "out23",
                SimulatedStartingUTC = DateTime.Parse("5/22/2011 4:18 AM GMT").ToUniversalTime(),
                SimulatedAverageFlowTime = TimeSpan.FromMinutes(4.5),
                SimulatedProduction = ImmutableList<SimulatedProduction>.Empty,
                Pallets = ImmutableList.Create("Pal5", "OMG"),
                Fixture = "Fix17",
                Face = 7,
                Load = ImmutableList.Create(15),
                ExpectedLoadTime = TimeSpan.FromSeconds(35),
                Unload = ImmutableList.Create(32),
                ExpectedUnloadTime = TimeSpan.FromSeconds(532),
                Stops = ImmutableList<MachiningStop>.Empty,
                Inspections = ImmutableList.Create(
                  new PathInspection()
                  {
                    InspectionType = "Insp2",
                    Counter = "counter1",
                    MaxVal = 12,
                    RandomFreq = -1,
                    TimeInterval = TimeSpan.FromMinutes(64)
                  },
                  new PathInspection()
                  {
                    InspectionType = "Insp4",
                    Counter = "counter2",
                    RandomFreq = 16.12,
                    MaxVal = -1,
                    TimeInterval = TimeSpan.FromMinutes(33)
                  },
                  new PathInspection()
                  {
                    InspectionType = "Insp5",
                    Counter = "counter3",
                    RandomFreq = 0.544,
                    MaxVal = -1,
                    TimeInterval = TimeSpan.FromMinutes(44)
                  }
                ),
                HoldMachining = new HoldPattern()
                {
                  HoldUnholdPatternStartUTC = DateTime.Parse("2000-01-01")
                },
                HoldLoadUnload = new HoldPattern()
                {
                  UserHold = true,
                  ReasonForUserHold = "erhagsad",
                  HoldUnholdPatternStartUTC = DateTime.Parse("2010/11/5 2:30 PM GMT").ToUniversalTime(),
                  HoldUnholdPatternRepeats = false,
                  HoldUnholdPattern = ImmutableList.Create(TimeSpan.FromMinutes(32), TimeSpan.FromMinutes(64))
                },
              }
            )
          }
        )
      };
    }

    /*
    public void CreateV17()
    {
      var now = new DateTime(2018, 7, 12, 5, 6, 7, DateTimeKind.Utc);

      var m1 = _log.AllocateMaterialID("uuu1");
      var m2 = _log.AllocateMaterialID("uuu1");
      var m3 = _log.AllocateMaterialID("uuu2");

      var mat1_1 = new LogMaterial(m1, "uuu1", 1, "part1", 2, face: "A");
      var mat1_2 = new LogMaterial(m1, "uuu1", 2, "part1", 2, face: "B");
      var mat2_1 = new LogMaterial(m2, "uuu1", 1, "part1", 2, face: "C");
      var mat2_2 = new LogMaterial(m2, "uuu1", 1, "part1", 2, face: "D");
      var mat3 = new LogMaterial(m3, "uuu2", 1, "part2", 1, face: "E");

      var log1 = new LogEntry(
        cntr: -1,
        mat: new [] {mat1_1, mat2_1},
        pal: "3",
        ty: LogType.MachineCycle,
        locName: "MC",
        locNum: 1,
        prog: "proggg",
        start: false,
        endTime: now,
        result: "result",
        endOfRoute: false
      );
      _log.AddLogEntry(log1);

      var log2 = new LogEntry(
        cntr: -1,
        mat: new [] {mat1_2, mat2_2},
        pal: "5",
        ty: LogType.MachineCycle,
        locName: "MC",
        locNum: 1,
        prog: "proggg2",
        start: false,
        endTime: now.AddMinutes(10),
        result: "result2",
        endOfRoute: false
      );
      _log.AddLogEntry(log2);

      _log.RecordSerialForMaterialID(mat1_1, "serial1", now.AddMinutes(20));
      _log.RecordWorkorderForMaterialID(mat1_1, "work1", now.AddMinutes(30));
      _log.RecordSerialForMaterialID(mat2_2, "serial2", now.AddMinutes(40));

      var log3 = new LogEntry(
        cntr: -1,
        mat: new [] {mat3},
        pal: "1",
        ty: LogType.LoadUnloadCycle,
        locName: "L/U",
        locNum: 5,
        prog: "LOAD",
        start: false,
        endTime: now.AddMinutes(50),
        result: "LOAD",
        endOfRoute: false
      );
      _log.AddLogEntry(log3);

      _log.RecordWorkorderForMaterialID(mat3, "work3", now.AddMinutes(60));
    }*/

    /*
    public void CreateV16()
    {
      var job1 = CreateJob();
      _jobs.AddJobs(new NewJobs() { Jobs = new List<JobPlan> { job1 } }, null);
    }
    */
  }
}
