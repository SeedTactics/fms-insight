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
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using FluentAssertions;
using MazakMachineInterface;
using NSubstitute;
using Xunit;

namespace MachineWatchTest;

public sealed class MazakSyncSpec : IDisposable
{
  private readonly MazakSync _sync;
  private readonly IReadDataAccess _read;
  private readonly IWriteData _write;
  private readonly string _tempDir;
  private readonly FMSSettings _fmsSt;
  private readonly RepositoryConfig repo;

  public MazakSyncSpec()
  {
    _read = Substitute.For<IReadDataAccess>();
    _write = Substitute.For<IWriteData>();
    repo = RepositoryConfig.InitializeMemoryDB(
      new SerialSettings() { ConvertMaterialIDToSerial = m => SerialSettings.ConvertToBase62(m) }
    );
    _tempDir = Path.Combine(Path.GetTempPath(), "MazakSyncSpec" + Path.GetRandomFileName());
    System.IO.Directory.CreateDirectory(_tempDir);

    _fmsSt = new FMSSettings() { QuarantineQueue = "quarantine" };
    _fmsSt.Queues.Add("castings", new QueueInfo());
    _fmsSt.Queues.Add("queueAAA", new QueueInfo());
    _fmsSt.Queues.Add("queueBBB", new QueueInfo());
    _fmsSt.Queues.Add("queueCCC", new QueueInfo());

    _sync = new MazakSync(
      _read,
      _write,
      _fmsSt,
      new MazakConfig()
      {
        DBType = MazakDbType.MazakSmooth,
        SQLConnectionString = "unused sql string",
        LogCSVPath = _tempDir,
        ProgramDirectory = "not used",
        LoadCSVPath = "not used",
      }
    );
  }

  public void Dispose()
  {
    _sync.Dispose();
    Directory.Delete(_tempDir, true);
    repo.Dispose();
  }

  [Fact]
  public async Task RaisesEventOnNewLogMessage()
  {
    var complete = new TaskCompletionSource<bool>();

    _sync.NewCellState += () => complete.SetResult(true);

    File.WriteAllLines(Path.Combine(_tempDir, "alog.csv"), ["2024,6,11,4,5,6,501,2,part,job,1,,2,,"]);

    if (await Task.WhenAny(complete.Task, Task.Delay(5000)) != complete.Task)
    {
      throw new Exception("Timeout waiting for event");
    }
  }

  [Fact]
  public void CheckJobsSuccess()
  {
    var jsonSettings = new JsonSerializerOptions();
    FMSInsightWebHost.JsonSettings(jsonSettings);
    _read
      .LoadAllData()
      .Returns(
        new MazakAllData()
        {
          MainPrograms =
          [
            new MazakProgramRow() { MainProgram = "1001", Comment = "" },
            new MazakProgramRow() { MainProgram = "1002", Comment = "" },
            new MazakProgramRow() { MainProgram = "1003", Comment = "" },
            new MazakProgramRow() { MainProgram = "1004", Comment = "" },
          ],
          Fixtures = [],
          Pallets = [],
          Parts = [],
          Schedules = [],
        }
      );

    var newJ = JsonSerializer.Deserialize<NewJobs>(
      File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
      jsonSettings
    );

    using var db = repo.OpenConnection();

    _sync.CheckNewJobs(db, newJ).Should().BeEmpty();
  }

  [Fact]
  public void CheckFailMissingProgram()
  {
    var jsonSettings = new JsonSerializerOptions();
    FMSInsightWebHost.JsonSettings(jsonSettings);
    _read
      .LoadAllData()
      .Returns(
        new MazakAllData()
        {
          MainPrograms =
          [
            new MazakProgramRow() { MainProgram = "1001", Comment = "" },
            // no 1002
            new MazakProgramRow() { MainProgram = "1003", Comment = "" },
            new MazakProgramRow() { MainProgram = "1004", Comment = "" },
          ],
          Fixtures = [],
          Pallets = [],
          Parts = [],
          Schedules = [],
        }
      );

    var newJ = JsonSerializer.Deserialize<NewJobs>(
      File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
      jsonSettings
    );

    using var db = repo.OpenConnection();

    _sync
      .CheckNewJobs(db, newJ)
      .Should()
      .BeEquivalentTo(
        [
          "Part aaa program 1002 does not exist in the cell controller.",
          "Part bbb program 1002 does not exist in the cell controller.",
        ]
      );
  }

  [Fact]
  public void MissingQueue()
  {
    _fmsSt.Queues.Remove("queueAAA");

    var jsonSettings = new JsonSerializerOptions();
    FMSInsightWebHost.JsonSettings(jsonSettings);
    _read
      .LoadAllData()
      .Returns(
        new MazakAllData()
        {
          MainPrograms =
          [
            new MazakProgramRow() { MainProgram = "1001", Comment = "" },
            new MazakProgramRow() { MainProgram = "1002", Comment = "" },
            new MazakProgramRow() { MainProgram = "1003", Comment = "" },
            new MazakProgramRow() { MainProgram = "1004", Comment = "" },
          ],
          Fixtures = [],
          Pallets = [],
          Parts = [],
          Schedules = [],
        }
      );

    var newJ = JsonSerializer.Deserialize<NewJobs>(
      File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
      jsonSettings
    );

    using var db = repo.OpenConnection();

    _sync
      .CheckNewJobs(db, newJ)
      .Should()
      .BeEquivalentTo(
        [
          " Job aaa-schId1234 has an output queue queueAAA which is not configured as a queue in FMS Insight. Non-final processes must have a configured local queue, not an external queue",
          " Job aaa-schId1234 has an input queue queueAAA which is not configured as a local queue in FMS Insight. All input queues must be local queues, not an external queue.",
        ]
      );
  }

  [Fact]
  public void LoadsEvents()
  {
    using var db = repo.OpenConnection();

    File.WriteAllLines(
      Path.Combine(_tempDir, "111loadstart.csv"),
      ["2024,6,11,4,5,6,501,,12,,1,6,4,prog,,,,"]
    );
    File.WriteAllLines(Path.Combine(_tempDir, "222loadend.csv"), ["2024,6,11,4,5,9,502,,12,,1,6,4,prog,,,,"]);

    var allData = new MazakAllData()
    {
      MainPrograms = [],
      Fixtures = [],
      Pallets = [],
      Parts = [],
      PalletPositions = [],
      Schedules = [],
      LoadActions = [],
    };
    _read.LoadAllData().Returns(allData);

    _sync
      .CalculateCellState(db)
      .Should()
      .BeEquivalentTo(
        new MazakState()
        {
          CurrentStatus = new CurrentStatus()
          {
            TimeOfCurrentStatusUTC = DateTime.UtcNow,
            Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
            Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
            Material = [],
            Alarms = [],
            Workorders = null,
            Queues = _fmsSt.Queues.ToImmutableDictionary(kv => kv.Key, kv => kv.Value),
          },
          AllData = allData,
          StoppedBecauseRecentMachineEnd = false,
          StateUpdated = true,
          TimeUntilNextRefresh = TimeSpan.FromMinutes(2),
        },
        options =>
          options
            .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, TimeSpan.FromSeconds(2)))
            .When(info => info.Path.EndsWith("TimeOfCurrentStatusUTC"))
      );

    db.MaxForeignID().Should().BeEquivalentTo("222loadend.csv");
    Directory.GetFiles(_tempDir, "*.csv").Should().BeEmpty();
  }

  [Fact]
  public void StopsProcessingOnRecentMachineEnd()
  {
    var now = DateTime.UtcNow.ToLocalTime();

    using var db = repo.OpenConnection();

    var dateFmt = "yyyy,MM,dd,HH,mm,ss";

    File.WriteAllLines(
      Path.Combine(_tempDir, "111loadstart.csv"),
      [now.AddMinutes(-1).ToString(dateFmt) + ",501,,12,,1,6,4,prog,,,,"]
    );
    File.WriteAllLines(
      Path.Combine(_tempDir, "222machineend.csv"),
      [now.ToString(dateFmt) + ",442,,3,,1,6,2,prog,,,,"]
    );
    File.WriteAllLines(
      Path.Combine(_tempDir, "333loadend.csv"),
      [now.AddSeconds(10).ToString(dateFmt) + ",501,,12,,1,6,4,prog,,,,"]
    );

    _read
      .LoadAllData()
      .Returns(
        new MazakAllData()
        {
          MainPrograms = [],
          Fixtures = [],
          Pallets = [],
          Parts = [],
          Schedules = [],
          LoadActions = [],
        }
      );

    var st = _sync.CalculateCellState(db);
    st.StoppedBecauseRecentMachineEnd.Should().BeTrue();
    st.TimeUntilNextRefresh.Should().Be(TimeSpan.FromSeconds(15));

    db.MaxForeignID().Should().BeEquivalentTo("111loadstart.csv");

    Directory
      .GetFiles(_tempDir, "*.csv")
      .Should()
      .BeEquivalentTo(
        [Path.Combine(_tempDir, "222machineend.csv"), Path.Combine(_tempDir, "333loadend.csv")]
      );
  }

  [Fact]
  public void QuarantinesMaterial()
  {
    using var db = repo.OpenConnection();

    var now = DateTime.UtcNow;

    var mat = db.AllocateMaterialID("uuuu", "pppp", 1);
    db.RecordLoadEnd(
      [
        new()
        {
          LoadStation = 2,
          Faces =
          [
            new MaterialToLoadOntoFace()
            {
              MaterialIDs = [mat],
              FaceNum = 1,
              Process = 1,
              Path = 1,
              ActiveOperationTime = TimeSpan.FromMinutes(1),
            },
          ],
        },
      ],
      pallet: 4,
      timeUTC: now
    );

    var allData = new MazakAllData()
    {
      MainPrograms = [],
      Fixtures = [],
      Pallets = [],
      Parts = [],
      PalletPositions = [new MazakPalletPositionRow() { PalletNumber = 4, PalletPosition = "S4" }],
      PalletSubStatuses = [],
      Schedules = [],
      LoadActions = [],
    };
    _read.LoadAllData().Returns(allData);

    _sync
      .CalculateCellState(db)
      .Should()
      .BeEquivalentTo(
        new MazakState()
        {
          CurrentStatus = new CurrentStatus()
          {
            TimeOfCurrentStatusUTC = DateTime.UtcNow,
            Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
            Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
            Material =
            [
              new InProcessMaterial()
              {
                MaterialID = mat,
                JobUnique = "uuuu",
                PartName = "pppp",
                Path = 1,
                Process = 1,
                Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
                SignaledInspections = [],
                Location = new InProcessMaterialLocation()
                {
                  Type = InProcessMaterialLocation.LocType.InQueue,
                  CurrentQueue = "quarantine",
                  QueuePosition = 0,
                },
              },
            ],
            Alarms = [],
            Workorders = null,
            Queues = _fmsSt.Queues.ToImmutableDictionary(kv => kv.Key, kv => kv.Value),
          },
          AllData = allData,
          StoppedBecauseRecentMachineEnd = false,
          StateUpdated = true,
          TimeUntilNextRefresh = TimeSpan.FromMinutes(2),
        },
        options =>
          options
            .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, TimeSpan.FromSeconds(2)))
            .When(info => info.Path.EndsWith("TimeOfCurrentStatusUTC"))
      );
  }

  [Fact]
  public void DownloadsNewJobs()
  {
    var jsonSettings = new JsonSerializerOptions();
    FMSInsightWebHost.JsonSettings(jsonSettings);

    using var db = repo.OpenConnection();
    var newJobs = JsonSerializer.Deserialize<NewJobs>(
      File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
      jsonSettings
    );
    db.AddJobs(newJobs, expectedPreviousScheduleId: null, addAsCopiedToSystem: false);

    var st = new MazakState()
    {
      StateUpdated = false,
      TimeUntilNextRefresh = TimeSpan.FromMinutes(1),
      StoppedBecauseRecentMachineEnd = false,
      CurrentStatus = new()
      {
        TimeOfCurrentStatusUTC = new(2018, 07, 19, 1, 2, 3, DateTimeKind.Utc),
        Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
        Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
        Queues = _fmsSt.Queues.ToImmutableDictionary(kv => kv.Key, kv => kv.Value),
        Material = [],
        Alarms = [],
      },
      AllData = new MazakAllData()
      {
        MainPrograms = ImmutableList
          .Create("1001", "1002", "1003", "1004", "1005")
          .Select(p => new MazakProgramRow() { MainProgram = p, Comment = "" }),
        Fixtures = [],
        Pallets = [],
        Parts = [],
        Schedules = [],
        LoadActions = [],
      },
    };

    // Before writing schedules, we refresh the database
    var parts = JsonSerializer.Deserialize<MazakWriteData>(
      File.ReadAllText(
        Path.Combine("..", "..", "..", "mazak", "write-snapshots", "fixtures-queues-parts.json")
      ),
      jsonSettings
    );
    MazakWriteData writeData = null;
    _write.WhenForAnyArgs(x => x.Save(default, default)).Do((ctx) => writeData = ctx.Arg<MazakWriteData>());

    _read
      .LoadAllData()
      .Returns(
        (context) =>
          new MazakAllData()
          {
            Schedules = writeData?.Schedules ?? [],
            Parts = parts.Parts,
            Pallets = parts.Pallets,
            PalletSubStatuses = Enumerable.Empty<MazakPalletSubStatusRow>(),
            PalletPositions = Enumerable.Empty<MazakPalletPositionRow>(),
            LoadActions = Enumerable.Empty<LoadAction>(),
            MainPrograms = ImmutableList
              .Create("1001", "1002", "1003", "1004", "1005")
              .Select(p => new MazakProgramRow() { MainProgram = p, Comment = "" }),
          }
      );

    _sync.ApplyActions(db, st).Should().BeTrue();

    _write
      .ReceivedCalls()
      .Where(c => c.GetMethodInfo().Name == "Save")
      .Select(c => c.GetArguments()[1] as string)
      .Should()
      .BeEquivalentTo(["Delete Pallets", "Delete Fixtures", "Add Fixtures", "Add Parts", "Add Schedules"]);
    // more detailed tests are in the write data tests

    _write.ClearReceivedCalls();

    _sync.ApplyActions(db, st).Should().BeFalse();

    _write.ReceivedCalls().Should().BeEmpty();
  }

  [Fact]
  public void CalculatesQueueChanges()
  {
    using var db = repo.OpenConnection();

    var st = new MazakState()
    {
      StateUpdated = true,
      TimeUntilNextRefresh = TimeSpan.FromMinutes(1),
      StoppedBecauseRecentMachineEnd = false,
      CurrentStatus = new()
      {
        TimeOfCurrentStatusUTC = DateTime.UtcNow,
        Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
        Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
        Queues = _fmsSt.Queues.ToImmutableDictionary(kv => kv.Key, kv => kv.Value),
        Material = [],
        Alarms = [],
      },
      AllData = new MazakAllData()
      {
        Schedules =
        [
          new MazakScheduleRow()
          {
            Comment = "uuuu-Insight",
            DueDate = DateTime.Today,
            FixForMachine = 1,
            HoldMode = 0,
            MissingFixture = 0,
            MissingProgram = 0,
            MissingTool = 0,
            MixScheduleID = 1,
            PartName = "pppp:10:1",
            Priority = 10,
            ProcessingPriority = 1,
            Id = 10,

            // plan 50, 40 completed, and 5 in process.  So there are 5 remaining.
            PlanQuantity = 50,
            CompleteQuantity = 40,
            Processes =
            [
              new MazakScheduleProcessRow()
              {
                ProcessNumber = 1,
                ProcessMaterialQuantity = 0,
                ProcessExecuteQuantity = 5,
              },
              new MazakScheduleProcessRow()
              {
                ProcessNumber = 2,
                ProcessMaterialQuantity = 0,
                ProcessExecuteQuantity = 0,
              },
            ],
          },
        ],
        LoadActions = [],
      },
    };

    var j = new Job()
    {
      UniqueStr = "uuuu",
      PartName = "pppp",
      Cycles = 0,
      RouteStartUTC = DateTime.MinValue,
      RouteEndUTC = DateTime.MinValue,
      Archived = false,
      Processes =
      [
        new ProcessInfo() { Paths = [JobLogTest.EmptyPath with { InputQueue = "thequeue" }] },
        new ProcessInfo() { Paths = [JobLogTest.EmptyPath with { InputQueue = "thequeue" }] },
      ],
    };
    db.AddJobs(new NewJobs() { Jobs = [j], ScheduleId = "sch11" }, null, addAsCopiedToSystem: true);

    // put 2 castings in queue, plus a different unique and a different process
    db.RecordAddMaterialToQueue(
      db.AllocateMaterialID("uuuu", "pppp", 1),
      process: 0,
      queue: "thequeue",
      position: -1,
      operatorName: "TestOper",
      reason: "TestSuite"
    );

    _sync.ApplyActions(db, st).Should().BeTrue();

    _write.ReceivedCalls().Should().HaveCount(1);
    _write.Received().Save(Arg.Any<MazakWriteData>(), Arg.Is("Setting material from queues"));

    var trans = _write.ReceivedCalls().Select(c => c.GetArguments()[0] as MazakWriteData).First();
    trans.Schedules.Count.Should().Be(1);
    trans.Schedules[0].Id.Should().Be(10);
    trans.Schedules[0].Priority.Should().Be(10);
    trans.Schedules[0].Processes.Count.Should().Be(2);
    trans.Schedules[0].Processes[0].ProcessNumber.Should().Be(1);
    trans.Schedules[0].Processes[0].ProcessMaterialQuantity.Should().Be(1); // set the 1 material
    trans.Schedules[0].Processes[1].ProcessNumber.Should().Be(2);
    trans.Schedules[0].Processes[1].ProcessMaterialQuantity.Should().Be(0); // sets no material
  }
}
