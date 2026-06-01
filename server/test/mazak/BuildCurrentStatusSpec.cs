/* Copyright (c) 2019, John Lenz

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
using MazakMachineInterface;
using Shouldly;
using VerifyTUnit;

namespace BlackMaple.FMSInsight.Mazak.Tests
{
  public class BuildCurrentStatusSpec : IDisposable
  {
    private RepositoryConfig _repoCfg;
    private JsonSerializerOptions jsonSettings;
    private FMSSettings _settings;
    private MazakConfig _mazakCfg;

    public BuildCurrentStatusSpec()
    {
      _repoCfg = RepositoryConfig.InitializeMemoryDB(
        new SerialSettings() { ConvertMaterialIDToSerial = (id) => id.ToString() }
      );

      _settings = new FMSSettings();
      _settings.Queues["castings"] = new QueueInfo();
      _settings.Queues["queueAAA"] = new QueueInfo();
      _settings.Queues["queueBBB"] = new QueueInfo();
      _settings.Queues["queueCCC"] = new QueueInfo();

      _mazakCfg = new MazakConfig() { DBType = MazakDbType.MazakSmooth, MachineNumbers = null };

      jsonSettings = new JsonSerializerOptions();
      FMSInsightWebHost.JsonSettings(jsonSettings);
      jsonSettings.WriteIndented = true;
    }

    public void Dispose()
    {
      _repoCfg.Dispose();
    }

    [Test]
    public void TestPalletNumberTranslation()
    {
      var cfg = new MazakConfig() { DBType = MazakDbType.MazakSmooth, StartingPalletNumber = 1 };
      cfg.TranslatePalletNumber(1).ShouldBe(1);
      cfg.InversePalletNumber(1).ShouldBe(1);

      cfg = cfg with { StartingPalletNumber = 101 };
      cfg.TranslatePalletNumber(1).ShouldBe(101);
      cfg.TranslatePalletNumber(3).ShouldBe(103);
      cfg.InversePalletNumber(101).ShouldBe(1);
      cfg.InversePalletNumber(103).ShouldBe(3);
    }

    [Test]
    public void TestLoadStationNumberTranslation()
    {
      var cfg = new MazakConfig() { DBType = MazakDbType.MazakSmooth, StartingLoadStationNumber = 1 };
      cfg.TranslateLoadStationNumber(1).ShouldBe(1);
      cfg.InverseLoadStationNumber(1).ShouldBe(1);

      cfg = cfg with { StartingLoadStationNumber = 201 };
      cfg.TranslateLoadStationNumber(1).ShouldBe(201);
      cfg.TranslateLoadStationNumber(2).ShouldBe(202);
      cfg.InverseLoadStationNumber(201).ShouldBe(1);
      cfg.InverseLoadStationNumber(202).ShouldBe(2);
    }

    /*
    [Fact]
    public void CreateSnapshot()
    {
      var scenario = "pathsgroups-load";
      var jobsFile = "path-groups.json";

      var newJobs = JsonConvert.DeserializeObject<NewJobs>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "sample-newjobs", jobsFile)),
          jsonSettings
      );
      _jobDB.AddJobs(newJobs, null);

      var connStr = "Server=172.16.11.6;User Id=mazakpmc;Password=Fms-978";
      var open = new OpenDatabaseKitReadDB(connStr, MazakDbType.MazakSmooth, null);
      var smooth = new SmoothReadOnlyDB(connStr, open);

      var all = smooth.LoadAllData();

      File.WriteAllText(
        Path.Combine("..", "..", "..", "mazak", "read-snapshots", scenario + ".data.json"),
        JsonConvert.SerializeObject(all, jsonSettings)
      );

      var logDb = _emptyLog;
      var existingLogPath =
        Path.Combine("..", "..", "..", "mazak", "read-snapshots", scenario + ".log.db");
      if (File.Exists(existingLogPath))
      {
        logDb = new JobLogDB();
        logDb.Open(existingLogPath);
      }

      var status = BuildCurrentStatus.Build(_jobDB, logDb, _settings, MazakDbType.MazakSmooth, all,
          new DateTime(2018, 7, 19, 20, 42, 3, DateTimeKind.Utc));

      File.WriteAllText(
        Path.Combine("..", "..", "..", "mazak", "read-snapshots", scenario + ".status.json"),
        JsonConvert.SerializeObject(status, jsonSettings)
      );
    }
    */

    [Test]
    [Arguments("basic-no-material")]
    [Arguments("basic-load-material")]
    [Arguments("basic-cutting")]
    [Arguments("basic-load-queue")]
    [Arguments("basic-unload-queues")]
    [Arguments("multiface-inital-load")]
    [Arguments("multiface-transfer-faces")]
    [Arguments("multiface-transfer-faces-and-unload")]
    [Arguments("multiface-transfer-user-jobs")]
    public async Task StatusSnapshot(string scenario)
    {
      IRepository repository;
      var existingLogPath = Path.Combine("..", "..", "..", "mazak", "read-snapshots", scenario + ".log.db");
      string _tempLogFile = System.IO.Path.GetTempFileName();
      if (File.Exists(existingLogPath))
      {
        System.IO.File.Copy(existingLogPath, _tempLogFile, overwrite: true);
        repository = RepositoryConfig
          .InitializeEventDatabase(
            new SerialSettings() { ConvertMaterialIDToSerial = (id) => id.ToString() },
            _tempLogFile
          )
          .OpenConnection();
      }
      else
      {
        repository = _repoCfg.OpenConnection();
      }

      /*
      Symlinks not supported on Windows
      var newJobs = JsonConvert.DeserializeObject<NewJobs>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "mazak", "read-snapshots", scenario + ".jobs.json")),
          jsonSettings
      );
      */
      NewJobs newJobs = null;
      if (scenario.Contains("basic"))
      {
        newJobs = JsonSerializer.Deserialize<NewJobs>(
          File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
          jsonSettings
        );
      }
      else if (scenario.Contains("multiface"))
      {
        newJobs = JsonSerializer.Deserialize<NewJobs>(
          File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "multi-face.json")),
          jsonSettings
        );
      }
      repository.AddJobs(newJobs, null, addAsCopiedToSystem: true);

      var allData = JsonSerializer.Deserialize<MazakAllData>(
        File.ReadAllText(Path.Combine("..", "..", "..", "mazak", "read-snapshots", scenario + ".data.json")),
        jsonSettings
      );

      CurrentStatus status;
      try
      {
        status = BuildCurrentStatus.Build(
          repository,
          _settings,
          _mazakCfg,
          allData,
          machineGroupName: "MC",
          null,
          new DateTime(2018, 7, 19, 20, 42, 3, DateTimeKind.Utc)
        );
      }
      finally
      {
        repository.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_tempLogFile);
      }

      await Verifier
        .Verify(status)
        .UseDirectory("read-snapshots")
        .UseParameters(scenario)
        .DontScrubDateTimes()
        .IgnoreMember<CurrentStatus>(c => c.TimeOfCurrentStatusUTC);
    }

    [Test]
    public async Task PendingLoad()
    {
      using var _memoryLog = _repoCfg.OpenConnection();
      NewJobs newJobs = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
        jsonSettings
      );
      _memoryLog.AddJobs(newJobs, null, addAsCopiedToSystem: true);

      var allData = JsonSerializer.Deserialize<MazakAllData>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "mazak", "read-snapshots", "basic-after-load.data.json")
        ),
        jsonSettings
      );

      var matId = _memoryLog.AllocateMaterialID("aaa-schId1234", "aaa", 2);
      _memoryLog.RecordAddMaterialToQueue(
        new EventLogMaterial()
        {
          MaterialID = matId,
          Process = 0,
          Face = 0,
        },
        "castings",
        -1,
        operatorName: null,
        reason: "TheQueueReason"
      );

      var status = BuildCurrentStatus.Build(
        _memoryLog,
        _settings,
        _mazakCfg,
        allData,
        machineGroupName: "MC",
        palletWithUnprocessedUnloads: 1,
        new DateTime(2018, 7, 19, 20, 42, 3, DateTimeKind.Utc)
      );

      await Verifier
        .Verify(status)
        .UseDirectory("read-snapshots")
        .DontScrubDateTimes()
        .IgnoreMember<CurrentStatus>(c => c.TimeOfCurrentStatusUTC);
    }

    [Test]
    public async Task SignalForQuarantine()
    {
      IRepository repository;
      bool close = false;
      var existingLogPath = Path.Combine(
        "..",
        "..",
        "..",
        "mazak",
        "read-snapshots",
        "basic-unload-queues.log.db"
      );
      string _tempLogFile = System.IO.Path.GetTempFileName();
      System.IO.File.Copy(existingLogPath, _tempLogFile, overwrite: true);
      repository = RepositoryConfig
        .InitializeEventDatabase(
          new SerialSettings() { ConvertMaterialIDToSerial = (id) => id.ToString() },
          _tempLogFile
        )
        .OpenConnection();
      close = true;

      var newJobs = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
        jsonSettings
      );
      repository.AddJobs(newJobs, null, addAsCopiedToSystem: true);

      var allData = JsonSerializer.Deserialize<MazakAllData>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "mazak", "read-snapshots", "basic-unload-queues.data.json")
        ),
        jsonSettings
      );

      // additionally record a signal for quarantine for the unloading material

      repository.SignalMaterialForQuarantine(
        new EventLogMaterial()
        {
          MaterialID = 2426324,
          Process = 1,
          Face = 1,
        },
        pallet: 5,
        queue: "QuarantineQ",
        operatorName: "theoper",
        reason: "a reason",
        timeUTC: new DateTime(2018, 7, 19, 20, 40, 19, DateTimeKind.Utc)
      );

      CurrentStatus status;
      try
      {
        status = BuildCurrentStatus.Build(
          repository,
          _settings,
          _mazakCfg,
          allData,
          machineGroupName: "MC",
          null,
          new DateTime(2018, 7, 19, 20, 42, 3, DateTimeKind.Utc)
        );
      }
      finally
      {
        if (close)
          repository.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_tempLogFile);
      }

      await Verifier
        .Verify(status)
        .UseDirectory("read-snapshots")
        .DontScrubDateTimes()
        .IgnoreMember<CurrentStatus>(c => c.TimeOfCurrentStatusUTC);
    }

    [Test]
    public async Task SignalForQuarantineOnlyMarksMatchingMaterial()
    {
      var existingLogPath = Path.Combine(
        "..",
        "..",
        "..",
        "mazak",
        "read-snapshots",
        "basic-unload-queues.log.db"
      );
      var tempLogFile = Path.GetTempFileName();
      File.Copy(existingLogPath, tempLogFile, overwrite: true);
      var repository = RepositoryConfig
        .InitializeEventDatabase(
          new SerialSettings() { ConvertMaterialIDToSerial = (id) => id.ToString() },
          tempLogFile
        )
        .OpenConnection();

      var newJobs = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
        jsonSettings
      );
      repository.AddJobs(newJobs, null, addAsCopiedToSystem: true);

      var allData = JsonSerializer.Deserialize<MazakAllData>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "mazak", "read-snapshots", "basic-unload-queues.data.json")
        ),
        jsonSettings
      );

      repository.SignalMaterialForQuarantine(
        new EventLogMaterial()
        {
          MaterialID = 2426324,
          Process = 1,
          Face = 1,
        },
        pallet: 5,
        queue: "QuarantineQ",
        operatorName: "theoper",
        reason: "a reason",
        timeUTC: new DateTime(2018, 7, 19, 20, 40, 19, DateTimeKind.Utc)
      );

      try
      {
        var status = BuildCurrentStatus.Build(
          repository,
          _settings,
          _mazakCfg,
          allData,
          machineGroupName: "MC",
          null,
          new DateTime(2018, 7, 19, 20, 42, 3, DateTimeKind.Utc)
        );

        var quarantined = status.Material.Where(m => m.QuarantineAfterUnload == true).ToList();

        await Assert.That(quarantined.Select(m => m.MaterialID)).IsEquivalentTo([2426324L]);
        await Assert.That(quarantined[0].Action.UnloadIntoQueue).IsEqualTo("QuarantineQ");
      }
      finally
      {
        repository.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(tempLogFile);
      }
    }

    [Test]
    public async Task WithMachineNumbers()
    {
      using var repository = _repoCfg.OpenConnection();

      var newJobs = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
        jsonSettings
      );
      repository.AddJobs(newJobs, null, addAsCopiedToSystem: true);

      var allData = JsonSerializer.Deserialize<MazakAllData>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "mazak", "read-snapshots", "basic-cutting.data.json")
        ),
        jsonSettings
      );

      var status = BuildCurrentStatus.Build(
        repository,
        _settings,
        _mazakCfg with
        {
          MachineNumbers = [101, 102, 103, 104],
        },
        allData,
        machineGroupName: "MC",
        null,
        new DateTime(2018, 7, 19, 20, 42, 3, DateTimeKind.Utc)
      );

      await Verifier
        .Verify(status)
        .DontScrubDateTimes()
        .UseDirectory("read-snapshots")
        .IgnoreMember<CurrentStatus>(c => c.TimeOfCurrentStatusUTC);
    }

    [Test]
    public async Task WithPalletNumbers()
    {
      using var repository = _repoCfg.OpenConnection();

      var newJobs = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
        jsonSettings
      );
      repository.AddJobs(newJobs, null, addAsCopiedToSystem: true);

      var allData = JsonSerializer.Deserialize<MazakAllData>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "mazak", "read-snapshots", "basic-cutting.data.json")
        ),
        jsonSettings
      );

      var status = BuildCurrentStatus.Build(
        repository,
        _settings,
        _mazakCfg with
        {
          StartingPalletNumber = 101,
        },
        allData,
        machineGroupName: "MC",
        null,
        new DateTime(2018, 7, 19, 20, 42, 3, DateTimeKind.Utc)
      );

      await Verifier
        .Verify(status)
        .DontScrubDateTimes()
        .UseDirectory("read-snapshots")
        .IgnoreMember<CurrentStatus>(c => c.TimeOfCurrentStatusUTC);
    }

    [Test]
    public async Task WithLoadStationNumbers()
    {
      using var repository = _repoCfg.OpenConnection();

      var newJobs = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
        jsonSettings
      );
      repository.AddJobs(newJobs, null, addAsCopiedToSystem: true);

      var allData = JsonSerializer.Deserialize<MazakAllData>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "mazak", "read-snapshots", "basic-cutting.data.json")
        ),
        jsonSettings
      );

      var status = BuildCurrentStatus.Build(
        repository,
        _settings,
        _mazakCfg with
        {
          StartingLoadStationNumber = 201,
        },
        allData,
        machineGroupName: "MC",
        null,
        new DateTime(2018, 7, 19, 20, 42, 3, DateTimeKind.Utc)
      );

      await Verifier
        .Verify(status)
        .DontScrubDateTimes()
        .UseDirectory("read-snapshots")
        .IgnoreMember<CurrentStatus>(c => c.TimeOfCurrentStatusUTC);
    }

    [Test]
    public async Task ProvisionalWorkorder()
    {
      using var repo = _repoCfg.OpenConnection();

      var newJobs = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
        jsonSettings
      );

      newJobs = newJobs with
      {
        Jobs = newJobs
          .Jobs.Select(j =>
            j.UniqueStr == "ccc-schId1234" ? j with { ProvisionalWorkorderId = "provWorkCCC" } : j
          )
          .ToImmutableList(),
      };
      repo.AddJobs(newJobs, null, addAsCopiedToSystem: true);

      var allData = JsonSerializer.Deserialize<MazakAllData>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "mazak", "read-snapshots", "basic-load-material.data.json")
        ),
        jsonSettings
      );

      CurrentStatus status;
      try
      {
        status = BuildCurrentStatus.Build(
          repo,
          _settings,
          _mazakCfg,
          allData,
          machineGroupName: "MC",
          null,
          new DateTime(2025, 8, 18, 16, 12, 3, DateTimeKind.Utc)
        );
      }
      finally
      {
        repo.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
      }

      var loadingMat = status.Material.Where(m =>
        m.Action.Type == InProcessMaterialAction.ActionType.Loading
      );
      await Assert.That(loadingMat.Count()).IsEqualTo(2);
      await Assert.That(loadingMat.Select(m => m.WorkorderId)).IsEquivalentTo(["provWorkCCC", "provWorkCCC"]);
    }

    [Test]
    public async Task SplitScheduleUsesRepositoryProcessCount()
    {
      using var repository = _repoCfg.OpenConnection();
      _settings.Queues["split-in-1"] = new QueueInfo();
      _settings.Queues["split-out-1"] = new QueueInfo();
      _settings.Queues["split-in-2"] = new QueueInfo();
      _settings.Queues["split-out-2"] = new QueueInfo();

      var splitJob = new Job()
      {
        UniqueStr = "split-uniq",
        PartName = "SplitPart",
        Cycles = 12,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes =
        [
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                PalletNums = [1],
                Load = [1],
                Unload = [1],
                Fixture = "Fix1",
                Face = 1,
                PartsPerPallet = 1,
                ExpectedLoadTime = TimeSpan.Zero,
                ExpectedUnloadTime = TimeSpan.Zero,
                InputQueue = "split-in-1",
                OutputQueue = "split-out-1",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "machine",
                    Stations = [1],
                    Program = "prog1",
                    ExpectedCycleTime = TimeSpan.Zero,
                  },
                ],
              },
            ],
          },
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                PalletNums = [2],
                Load = [1],
                Unload = [1],
                Fixture = "Fix2",
                Face = 1,
                PartsPerPallet = 1,
                ExpectedLoadTime = TimeSpan.Zero,
                ExpectedUnloadTime = TimeSpan.Zero,
                InputQueue = "split-in-2",
                OutputQueue = "split-out-2",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "machine",
                    Stations = [1],
                    Program = "prog2",
                    ExpectedCycleTime = TimeSpan.Zero,
                  },
                ],
              },
            ],
          },
        ],
      };

      repository.AddJobs(
        new NewJobs()
        {
          ScheduleId = "sch",
          Jobs = [splitJob],
          ExtraParts = [],
        },
        null,
        addAsCopiedToSystem: true
      );

      var allData = new MazakAllData()
      {
        Parts =
        [
          new MazakPartRow()
          {
            PartName = "SplitPart:1:1",
            Comment = "split-uniq-1-1-InsightS",
            Processes =
            [
              new MazakPartProcessRow()
              {
                PartName = "SplitPart:1:1",
                ProcessNumber = 1,
                Fixture = "Fix1",
                MainProgram = "prog1",
                FixLDS = "1",
                RemoveLDS = "1",
                CutMc = "1",
                FixQuantity = 1,
              },
            ],
          },
          new MazakPartRow()
          {
            PartName = "SplitPart:1:2",
            Comment = "split-uniq-2-1-InsightS",
            Processes =
            [
              new MazakPartProcessRow()
              {
                PartName = "SplitPart:1:2",
                ProcessNumber = 1,
                Fixture = "Fix2",
                MainProgram = "prog2",
                FixLDS = "1",
                RemoveLDS = "1",
                CutMc = "1",
                FixQuantity = 1,
              },
            ],
          },
        ],
        Schedules =
        [
          new MazakScheduleRow()
          {
            Id = 9,
            PartName = "SplitPart:1:1",
            Comment = "split-uniq-1-1-InsightS",
            PlanQuantity = 12,
            CompleteQuantity = 0,
            Priority = 1,
            DueDate = new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc),
            Processes =
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 9,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 0,
                ProcessExecuteQuantity = 0,
                ProcessBadQuantity = 0,
                FixQuantity = 1,
              },
            },
          },
          new MazakScheduleRow()
          {
            Id = 10,
            PartName = "SplitPart:1:2",
            Comment = "split-uniq-2-1-InsightS",
            PlanQuantity = 12,
            CompleteQuantity = 0,
            Priority = 2,
            DueDate = new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc),
            Processes =
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 10,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 0,
                ProcessExecuteQuantity = 0,
                ProcessBadQuantity = 0,
                FixQuantity = 1,
              },
            },
          },
        ],
        LoadActions = [],
        Pallets = [],
        PalletSubStatuses = [],
        PalletStatuses = [],
        PalletPositions = [],
        Alarms = [],
        MainPrograms = [],
        Fixtures = [],
      };

      var status = BuildCurrentStatus.Build(
        repository,
        _settings,
        _mazakCfg,
        allData,
        machineGroupName: "MC",
        null,
        new DateTime(2026, 4, 28, 12, 0, 0, DateTimeKind.Utc)
      );

      await Assert.That(status.Jobs.ContainsKey("split-uniq")).IsTrue();
      await Assert.That(status.Jobs["split-uniq"].Processes.Count).IsEqualTo(2);
      await Assert.That(status.Jobs["split-uniq"].Completed.Count).IsEqualTo(2);
    }

    [Test]
    public async Task SplitScheduleUsesRepositoryProcessCountWhenOnlyLaterProcessExists()
    {
      using var repository = _repoCfg.OpenConnection();
      _settings.Queues["split-late-in-1"] = new QueueInfo();
      _settings.Queues["split-late-out-1"] = new QueueInfo();
      _settings.Queues["split-late-in-2"] = new QueueInfo();
      _settings.Queues["split-late-out-2"] = new QueueInfo();

      var splitJob = new Job()
      {
        UniqueStr = "split-late-uniq",
        PartName = "SplitLatePart",
        Cycles = 12,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes =
        [
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                PalletNums = [1],
                Load = [1],
                Unload = [1],
                Fixture = "FixLate1",
                Face = 1,
                PartsPerPallet = 1,
                ExpectedLoadTime = TimeSpan.Zero,
                ExpectedUnloadTime = TimeSpan.Zero,
                InputQueue = "split-late-in-1",
                OutputQueue = "split-late-out-1",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "machine",
                    Stations = [1],
                    Program = "progLate1",
                    ExpectedCycleTime = TimeSpan.Zero,
                  },
                ],
              },
            ],
          },
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                PalletNums = [2],
                Load = [1],
                Unload = [1],
                Fixture = "FixLate2",
                Face = 1,
                PartsPerPallet = 1,
                ExpectedLoadTime = TimeSpan.Zero,
                ExpectedUnloadTime = TimeSpan.Zero,
                InputQueue = "split-late-in-2",
                OutputQueue = "split-late-out-2",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "machine",
                    Stations = [2],
                    Program = "progLate2",
                    ExpectedCycleTime = TimeSpan.Zero,
                  },
                ],
              },
            ],
          },
        ],
      };

      repository.AddJobs(
        new NewJobs()
        {
          ScheduleId = "sch-late",
          Jobs = [splitJob],
          ExtraParts = [],
        },
        null,
        addAsCopiedToSystem: true
      );

      var allData = new MazakAllData()
      {
        Parts =
        [
          new MazakPartRow()
          {
            PartName = "SplitLatePart:1:2",
            Comment = "split-late-uniq-2-1-InsightS",
            Processes =
            [
              new MazakPartProcessRow()
              {
                PartName = "SplitLatePart:1:2",
                ProcessNumber = 1,
                Fixture = "FixLate2",
                MainProgram = "progLate2",
                FixLDS = "1",
                RemoveLDS = "1",
                CutMc = "2",
                FixQuantity = 1,
              },
            ],
          },
        ],
        Schedules =
        [
          new MazakScheduleRow()
          {
            Id = 12,
            PartName = "SplitLatePart:1:2",
            Comment = "split-late-uniq-2-1-InsightS",
            PlanQuantity = 12,
            CompleteQuantity = 4,
            Priority = 2,
            DueDate = new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc),
            Processes =
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 12,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 1,
                ProcessExecuteQuantity = 0,
                ProcessBadQuantity = 0,
                FixQuantity = 1,
              },
            },
          },
        ],
        LoadActions = [],
        Pallets = [],
        PalletSubStatuses = [],
        PalletStatuses = [],
        PalletPositions = [],
        Alarms = [],
        MainPrograms = [],
        Fixtures = [],
      };

      var status = BuildCurrentStatus.Build(
        repository,
        _settings,
        _mazakCfg,
        allData,
        machineGroupName: "MC",
        null,
        new DateTime(2026, 4, 28, 12, 0, 0, DateTimeKind.Utc)
      );

      await Assert.That(status.Jobs.ContainsKey("split-late-uniq")).IsTrue();
      await Assert.That(status.Jobs["split-late-uniq"].Processes.Count).IsEqualTo(2);
      await Assert.That(status.Jobs["split-late-uniq"].Cycles).IsEqualTo(12);
      await Assert.That(status.Jobs["split-late-uniq"].Completed[0][0]).IsEqualTo(0);
      await Assert.That(status.Jobs["split-late-uniq"].Completed[1][0]).IsEqualTo(4);
      await Assert.That(status.Jobs["split-late-uniq"].RemainingToStart).IsEqualTo(7);
    }

    [Test]
    public async Task IgnoresSplitScheduleWhenCommentProcessExceedsRepositoryJob()
    {
      using var repository = _repoCfg.OpenConnection();
      _settings.Queues["split-downgrade-in-1"] = new QueueInfo();
      _settings.Queues["split-downgrade-out-1"] = new QueueInfo();

      var downgradedJob = new Job()
      {
        UniqueStr = "split-downgrade-uniq",
        PartName = "SplitDowngradePart",
        Cycles = 12,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes =
        [
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                PalletNums = [1],
                Load = [1],
                Unload = [1],
                Fixture = "FixDown1",
                Face = 1,
                PartsPerPallet = 1,
                ExpectedLoadTime = TimeSpan.Zero,
                ExpectedUnloadTime = TimeSpan.Zero,
                InputQueue = "split-downgrade-in-1",
                OutputQueue = "split-downgrade-out-1",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "machine",
                    Stations = [1],
                    Program = "progDown1",
                    ExpectedCycleTime = TimeSpan.Zero,
                  },
                ],
              },
            ],
          },
        ],
      };

      repository.AddJobs(
        new NewJobs()
        {
          ScheduleId = "sch-downgrade",
          Jobs = [downgradedJob],
          ExtraParts = [],
        },
        null,
        addAsCopiedToSystem: true
      );

      var allData = new MazakAllData()
      {
        Parts =
        [
          new MazakPartRow()
          {
            PartName = "SplitDowngradePart:1:2",
            Comment = "split-downgrade-uniq-2-1-InsightS",
            Processes =
            [
              new MazakPartProcessRow()
              {
                PartName = "SplitDowngradePart:1:2",
                ProcessNumber = 1,
                Fixture = "FixDown2",
                MainProgram = "progDown2",
                FixLDS = "1",
                RemoveLDS = "1",
                CutMc = "2",
                FixQuantity = 1,
              },
            ],
          },
        ],
        Schedules =
        [
          new MazakScheduleRow()
          {
            Id = 13,
            PartName = "SplitDowngradePart:1:2",
            Comment = "split-downgrade-uniq-2-1-InsightS",
            PlanQuantity = 12,
            CompleteQuantity = 4,
            Priority = 2,
            DueDate = new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc),
            Processes =
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 13,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 1,
                ProcessExecuteQuantity = 0,
                ProcessBadQuantity = 0,
                FixQuantity = 1,
              },
            },
          },
        ],
        LoadActions = [],
        Pallets = [],
        PalletSubStatuses = [],
        PalletStatuses = [],
        PalletPositions = [],
        Alarms = [],
        MainPrograms = [],
        Fixtures = [],
      };

      var status = BuildCurrentStatus.Build(
        repository,
        _settings,
        _mazakCfg,
        allData,
        machineGroupName: "MC",
        null,
        new DateTime(2026, 4, 28, 12, 0, 0, DateTimeKind.Utc)
      );

      await Assert.That(status.Jobs.ContainsKey("split-downgrade-uniq")).IsFalse();
    }

    [Test]
    public async Task SplitScheduleCountsAreCorrect()
    {
      // Verifies that Cycles, Completed, and RemainingToStart are not double-counted
      // when two split schedules share the same job unique string.
      //
      // Setup:
      //   proc-1 schedule: PlanQty=12, CompleteQty=5, ExeQty=1  (5 left proc-1, 1 running proc-1)
      //   proc-2 schedule: PlanQty=12, CompleteQty=3, ExeQty=0  (3 fully done)
      //
      // Expected:
      //   Cycles = 12  (not 24)
      //   Completed[0] = 5  (pieces that finished proc-1)
      //   Completed[1] = 3  (pieces fully done)
      //   RemainingToStart = 12 - (5 + 1) = 6  (not double-counted)

      using var repository = _repoCfg.OpenConnection();
      _settings.Queues["sq-in-1"] = new QueueInfo();
      _settings.Queues["sq-out-1"] = new QueueInfo();
      _settings.Queues["sq-in-2"] = new QueueInfo();
      _settings.Queues["sq-out-2"] = new QueueInfo();

      var splitJob = new Job()
      {
        UniqueStr = "split-cnt-uniq",
        PartName = "SplitCntPart",
        Cycles = 12,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes =
        [
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                PalletNums = [1],
                Load = [1],
                Unload = [1],
                Fixture = "FixA",
                Face = 1,
                PartsPerPallet = 1,
                ExpectedLoadTime = TimeSpan.Zero,
                ExpectedUnloadTime = TimeSpan.Zero,
                InputQueue = "sq-in-1",
                OutputQueue = "sq-out-1",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "machine",
                    Stations = [1],
                    Program = "p1",
                    ExpectedCycleTime = TimeSpan.Zero,
                  },
                ],
              },
            ],
          },
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                PalletNums = [2],
                Load = [1],
                Unload = [1],
                Fixture = "FixB",
                Face = 1,
                PartsPerPallet = 1,
                ExpectedLoadTime = TimeSpan.Zero,
                ExpectedUnloadTime = TimeSpan.Zero,
                InputQueue = "sq-in-2",
                OutputQueue = "sq-out-2",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "machine",
                    Stations = [2],
                    Program = "p2",
                    ExpectedCycleTime = TimeSpan.Zero,
                  },
                ],
              },
            ],
          },
        ],
      };

      repository.AddJobs(
        new NewJobs()
        {
          ScheduleId = "sch-cnt",
          Jobs = [splitJob],
          ExtraParts = [],
        },
        null,
        addAsCopiedToSystem: true
      );

      var allData = new MazakAllData()
      {
        Parts =
        [
          new MazakPartRow()
          {
            PartName = "SplitCntPart:1:1",
            Comment = "split-cnt-uniq-1-1-InsightS",
            Processes =
            [
              new MazakPartProcessRow()
              {
                PartName = "SplitCntPart:1:1",
                ProcessNumber = 1,
                Fixture = "FixA",
                MainProgram = "p1",
                FixLDS = "1",
                RemoveLDS = "1",
                CutMc = "1",
                FixQuantity = 1,
              },
            ],
          },
          new MazakPartRow()
          {
            PartName = "SplitCntPart:1:2",
            Comment = "split-cnt-uniq-2-1-InsightS",
            Processes =
            [
              new MazakPartProcessRow()
              {
                PartName = "SplitCntPart:1:2",
                ProcessNumber = 1,
                Fixture = "FixB",
                MainProgram = "p2",
                FixLDS = "1",
                RemoveLDS = "1",
                CutMc = "2",
                FixQuantity = 1,
              },
            ],
          },
        ],
        Schedules =
        [
          // proc-1 schedule: 5 completed, 1 executing
          new MazakScheduleRow()
          {
            Id = 20,
            PartName = "SplitCntPart:1:1",
            Comment = "split-cnt-uniq-1-1-InsightS",
            PlanQuantity = 12,
            CompleteQuantity = 5,
            Priority = 1,
            DueDate = new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc),
            Processes =
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 20,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 0,
                ProcessExecuteQuantity = 1,
                ProcessBadQuantity = 0,
                FixQuantity = 1,
              },
            },
          },
          // proc-2 schedule: 3 completed, 0 executing
          new MazakScheduleRow()
          {
            Id = 21,
            PartName = "SplitCntPart:1:2",
            Comment = "split-cnt-uniq-2-1-InsightS",
            PlanQuantity = 12,
            CompleteQuantity = 3,
            Priority = 2,
            DueDate = new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc),
            Processes =
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 21,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 2, // 2 in queue waiting for proc-2
                ProcessExecuteQuantity = 0,
                ProcessBadQuantity = 0,
                FixQuantity = 1,
              },
            },
          },
        ],
        LoadActions = [],
        Pallets = [],
        PalletSubStatuses = [],
        PalletStatuses = [],
        PalletPositions = [],
        Alarms = [],
        MainPrograms = [],
        Fixtures = [],
      };

      var status = BuildCurrentStatus.Build(
        repository,
        _settings,
        _mazakCfg,
        allData,
        machineGroupName: "MC",
        null,
        new DateTime(2026, 4, 28, 12, 0, 0, DateTimeKind.Utc)
      );

      var j = status.Jobs["split-cnt-uniq"];

      // Cycles must not be doubled (both split schedules have PlanQuantity=12)
      await Assert.That(j.Cycles).IsEqualTo(12);

      // Completed[0] = proc-1 CompleteQty = 5 (pieces that left proc-1)
      // Completed[1] = proc-2 CompleteQty = 3 (fully done)
      await Assert.That(j.Completed[0][0]).IsEqualTo(5);
      await Assert.That(j.Completed[1][0]).IsEqualTo(3);

      // Started = proc1.CompleteQty + proc1.ExeQty = 5 + 1 = 6
      // proc2 counts are NOT added (would double-count pieces already in proc1.CompleteQty)
      await Assert.That(j.RemainingToStart).IsEqualTo(6);
    }

    [Test]
    public async Task SplitScheduleHoldPersistsIfAnyScheduleHeld()
    {
      using var repository = _repoCfg.OpenConnection();
      _settings.Queues["hold-in-1"] = new QueueInfo();
      _settings.Queues["hold-out-1"] = new QueueInfo();
      _settings.Queues["hold-in-2"] = new QueueInfo();
      _settings.Queues["hold-out-2"] = new QueueInfo();

      var splitJob = new Job()
      {
        UniqueStr = "split-hold-uniq",
        PartName = "SplitHoldPart",
        Cycles = 4,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes =
        [
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                PalletNums = [1],
                Load = [1],
                Unload = [1],
                Fixture = "FixH1",
                Face = 1,
                PartsPerPallet = 1,
                ExpectedLoadTime = TimeSpan.Zero,
                ExpectedUnloadTime = TimeSpan.Zero,
                InputQueue = "hold-in-1",
                OutputQueue = "hold-out-1",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "machine",
                    Stations = [1],
                    Program = "hold-p1",
                    ExpectedCycleTime = TimeSpan.Zero,
                  },
                ],
              },
            ],
          },
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                PalletNums = [2],
                Load = [1],
                Unload = [1],
                Fixture = "FixH2",
                Face = 1,
                PartsPerPallet = 1,
                ExpectedLoadTime = TimeSpan.Zero,
                ExpectedUnloadTime = TimeSpan.Zero,
                InputQueue = "hold-in-2",
                OutputQueue = "hold-out-2",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "machine",
                    Stations = [2],
                    Program = "hold-p2",
                    ExpectedCycleTime = TimeSpan.Zero,
                  },
                ],
              },
            ],
          },
        ],
      };

      repository.AddJobs(
        new NewJobs()
        {
          ScheduleId = "split-hold",
          Jobs = [splitJob],
          ExtraParts = [],
        },
        null,
        addAsCopiedToSystem: true
      );

      var allData = new MazakAllData()
      {
        Parts =
        [
          new MazakPartRow()
          {
            PartName = "SplitHoldPart:1:1",
            Comment = "split-hold-uniq-1-1-InsightS",
            Processes =
            [
              new MazakPartProcessRow()
              {
                PartName = "SplitHoldPart:1:1",
                ProcessNumber = 1,
                Fixture = "FixH1",
                MainProgram = "hold-p1",
                FixLDS = "1",
                RemoveLDS = "1",
                CutMc = "1",
                FixQuantity = 1,
              },
            ],
          },
          new MazakPartRow()
          {
            PartName = "SplitHoldPart:1:2",
            Comment = "split-hold-uniq-2-1-InsightS",
            Processes =
            [
              new MazakPartProcessRow()
              {
                PartName = "SplitHoldPart:1:2",
                ProcessNumber = 1,
                Fixture = "FixH2",
                MainProgram = "hold-p2",
                FixLDS = "1",
                RemoveLDS = "1",
                CutMc = "2",
                FixQuantity = 1,
              },
            ],
          },
        ],
        Schedules =
        [
          new MazakScheduleRow()
          {
            Id = 30,
            PartName = "SplitHoldPart:1:1",
            Comment = "split-hold-uniq-1-1-InsightS",
            PlanQuantity = 4,
            CompleteQuantity = 0,
            Priority = 1,
            DueDate = new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc),
            HoldMode = (int)MazakMachineInterface.HoldPattern.HoldMode.FullHold,
            Processes =
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 30,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 0,
                ProcessExecuteQuantity = 0,
                ProcessBadQuantity = 0,
                FixQuantity = 1,
              },
            },
          },
          new MazakScheduleRow()
          {
            Id = 31,
            PartName = "SplitHoldPart:1:2",
            Comment = "split-hold-uniq-2-1-InsightS",
            PlanQuantity = 4,
            CompleteQuantity = 0,
            Priority = 2,
            DueDate = new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc),
            HoldMode = (int)MazakMachineInterface.HoldPattern.HoldMode.NoHold,
            Processes =
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 31,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 0,
                ProcessExecuteQuantity = 0,
                ProcessBadQuantity = 0,
                FixQuantity = 1,
              },
            },
          },
        ],
        LoadActions = [],
        Pallets = [],
        PalletSubStatuses = [],
        PalletStatuses = [],
        PalletPositions = [],
        Alarms = [],
        MainPrograms = [],
        Fixtures = [],
      };

      var status = BuildCurrentStatus.Build(
        repository,
        _settings,
        _mazakCfg,
        allData,
        machineGroupName: "MC",
        null,
        new DateTime(2026, 4, 28, 12, 0, 0, DateTimeKind.Utc)
      );

      await Assert.That(status.Jobs["split-hold-uniq"].HoldJob?.UserHold == true).IsTrue();
    }

    [Test]
    public async Task SplitScheduleProc2PalletMaterialHasCorrectFmsProcess()
    {
      // Regression: when split proc-2 material is physically on a pallet, the Mazak
      // PartProcessNumber is 1 (only one Mazak process per split schedule), but the FMS
      // job process must be 2.  Before the fix, InProcessMaterial.Process was reported as 1.
      using var repository = _repoCfg.OpenConnection();
      _settings.Queues["sp2-in-1"] = new QueueInfo();
      _settings.Queues["sp2-in-2"] = new QueueInfo();

      var splitJob = new Job()
      {
        UniqueStr = "sp2-uniq",
        PartName = "SP2Part",
        Cycles = 5,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes =
        [
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                PalletNums = [1],
                Load = [1],
                Unload = [1],
                Fixture = "FixA",
                Face = 1,
                PartsPerPallet = 1,
                ExpectedLoadTime = TimeSpan.Zero,
                ExpectedUnloadTime = TimeSpan.Zero,
                InputQueue = "sp2-in-1",
                OutputQueue = "sp2-in-2",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "MC",
                    Stations = [1],
                    Program = "sp2-p1",
                    ExpectedCycleTime = TimeSpan.FromMinutes(10),
                  },
                ],
              },
            ],
          },
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                PalletNums = [2],
                Load = [1],
                Unload = [1],
                Fixture = "FixB",
                Face = 1,
                PartsPerPallet = 1,
                ExpectedLoadTime = TimeSpan.Zero,
                ExpectedUnloadTime = TimeSpan.Zero,
                InputQueue = "sp2-in-2",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "MC",
                    Stations = [2],
                    Program = "sp2-p2",
                    ExpectedCycleTime = TimeSpan.FromMinutes(20),
                  },
                ],
              },
            ],
          },
        ],
      };

      repository.AddJobs(
        new NewJobs()
        {
          ScheduleId = "sp2-sch",
          Jobs = [splitJob],
          ExtraParts = [],
        },
        null,
        addAsCopiedToSystem: true
      );

      var allData = new MazakAllData()
      {
        Parts =
        [
          new MazakPartRow()
          {
            PartName = "SP2Part:1:1:1",
            Comment = "sp2-uniq-1-1-InsightS",
            Processes =
            [
              new MazakPartProcessRow()
              {
                PartName = "SP2Part:1:1:1",
                ProcessNumber = 1,
                Fixture = "FixA",
                MainProgram = "sp2-p1",
                FixLDS = "1",
                RemoveLDS = "1",
                CutMc = "1",
                FixQuantity = 1,
              },
            ],
          },
          new MazakPartRow()
          {
            PartName = "SP2Part:1:2:2",
            Comment = "sp2-uniq-2-1-InsightS",
            Processes =
            [
              new MazakPartProcessRow()
              {
                PartName = "SP2Part:1:2:2",
                ProcessNumber = 1,
                Fixture = "FixB",
                MainProgram = "sp2-p2",
                FixLDS = "1",
                RemoveLDS = "1",
                CutMc = "2",
                FixQuantity = 1,
              },
            ],
          },
        ],
        Schedules =
        [
          new MazakScheduleRow()
          {
            Id = 40,
            PartName = "SP2Part:1:1:1",
            Comment = "sp2-uniq-1-1-InsightS",
            PlanQuantity = 5,
            CompleteQuantity = 1,
            Priority = 1,
            DueDate = new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc),
            Processes =
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 40,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 0,
                ProcessExecuteQuantity = 0,
                ProcessBadQuantity = 0,
                FixQuantity = 1,
              },
            },
          },
          new MazakScheduleRow()
          {
            Id = 41,
            PartName = "SP2Part:1:2:2",
            Comment = "sp2-uniq-2-1-InsightS",
            PlanQuantity = 5,
            CompleteQuantity = 0,
            Priority = 2,
            DueDate = new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc),
            Processes =
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 41,
                ProcessNumber = 1,
                // 1 piece currently executing proc-2 on pallet 2
                ProcessMaterialQuantity = 0,
                ProcessExecuteQuantity = 1,
                ProcessBadQuantity = 0,
                FixQuantity = 1,
              },
            },
          },
        ],
        LoadActions = [],
        Pallets =
        [
          new MazakPalletRow()
          {
            PalletNumber = 2,
            Fixture = "FixB",
            FixtureGroupV2 = 0,
          },
        ],
        PalletSubStatuses =
        [
          new MazakPalletSubStatusRow()
          {
            PalletNumber = 2,
            // proc-2 split schedule ID
            ScheduleID = 41,
            // Mazak process number is always 1 for a single-process split schedule
            PartProcessNumber = 1,
            FixQuantity = 1,
          },
        ],
        PalletStatuses = [],
        PalletPositions = [],
        Alarms = [],
        MainPrograms = [],
        Fixtures = [],
      };

      var status = BuildCurrentStatus.Build(
        repository,
        _settings,
        _mazakCfg,
        allData,
        machineGroupName: "MC",
        null,
        new DateTime(2026, 4, 28, 12, 0, 0, DateTimeKind.Utc)
      );

      // The pallet-2 material should be reported at FMS process 2 (not Mazak process 1)
      var palMaterial = status.Material.Where(m => m.Location.PalletNum == 2).ToList();
      await Assert.That(palMaterial.Count).IsEqualTo(1);
      await Assert.That(palMaterial[0].Process).IsEqualTo(2);
      await Assert.That(palMaterial[0].JobUnique).IsEqualTo("sp2-uniq");
    }

    [Test]
    public async Task SplitScheduleLoadToProc2ShowsLoadingAction()
    {
      using var repository = _repoCfg.OpenConnection();
      _settings.Queues["sp2load-in-1"] = new QueueInfo();
      _settings.Queues["sp2load-in-2"] = new QueueInfo();

      var splitJob = new Job()
      {
        UniqueStr = "sp2load-uniq",
        PartName = "SP2LoadPart",
        Cycles = 5,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes =
        [
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                PalletNums = [1],
                Load = [1],
                Unload = [1],
                Fixture = "FixA",
                Face = 1,
                PartsPerPallet = 1,
                ExpectedLoadTime = TimeSpan.Zero,
                ExpectedUnloadTime = TimeSpan.Zero,
                InputQueue = "sp2load-in-1",
                OutputQueue = "sp2load-in-2",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "MC",
                    Stations = [1],
                    Program = "sp2load-p1",
                    ExpectedCycleTime = TimeSpan.FromMinutes(10),
                  },
                ],
              },
            ],
          },
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                PalletNums = [2],
                Load = [1],
                Unload = [1],
                Fixture = "FixB",
                Face = 1,
                PartsPerPallet = 1,
                ExpectedLoadTime = TimeSpan.Zero,
                ExpectedUnloadTime = TimeSpan.Zero,
                InputQueue = "sp2load-in-2",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "MC",
                    Stations = [2],
                    Program = "sp2load-p2",
                    ExpectedCycleTime = TimeSpan.FromMinutes(20),
                  },
                ],
              },
            ],
          },
        ],
      };

      repository.AddJobs(
        new NewJobs()
        {
          ScheduleId = "sp2load-sch",
          Jobs = [splitJob],
          ExtraParts = [],
        },
        null,
        addAsCopiedToSystem: true
      );

      var allData = new MazakAllData()
      {
        Parts =
        [
          new MazakPartRow()
          {
            PartName = "SP2LoadPart:1:1:1",
            Comment = "sp2load-uniq-1-1-InsightS",
            Processes =
            [
              new MazakPartProcessRow()
              {
                PartName = "SP2LoadPart:1:1:1",
                ProcessNumber = 1,
                Fixture = "FixA",
                MainProgram = "sp2load-p1",
                FixLDS = "1",
                RemoveLDS = "1",
                CutMc = "1",
                FixQuantity = 1,
              },
            ],
          },
          new MazakPartRow()
          {
            PartName = "SP2LoadPart:1:2:2",
            Comment = "sp2load-uniq-2-1-InsightS",
            Processes =
            [
              new MazakPartProcessRow()
              {
                PartName = "SP2LoadPart:1:2:2",
                ProcessNumber = 1,
                Fixture = "FixB",
                MainProgram = "sp2load-p2",
                FixLDS = "1",
                RemoveLDS = "1",
                CutMc = "2",
                FixQuantity = 1,
              },
            ],
          },
        ],
        Schedules =
        [
          new MazakScheduleRow()
          {
            Id = 50,
            PartName = "SP2LoadPart:1:1:1",
            Comment = "sp2load-uniq-1-1-InsightS",
            PlanQuantity = 5,
            CompleteQuantity = 1,
            Priority = 1,
            DueDate = new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc),
            Processes =
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 50,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 0,
                ProcessExecuteQuantity = 0,
                ProcessBadQuantity = 0,
                FixQuantity = 1,
              },
            },
          },
          new MazakScheduleRow()
          {
            Id = 51,
            PartName = "SP2LoadPart:1:2:2",
            Comment = "sp2load-uniq-2-1-InsightS",
            PlanQuantity = 5,
            CompleteQuantity = 0,
            Priority = 2,
            DueDate = new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc),
            Processes =
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 51,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 1,
                ProcessExecuteQuantity = 0,
                ProcessBadQuantity = 0,
                FixQuantity = 1,
              },
            },
          },
        ],
        LoadActions =
        [
          new LoadAction()
          {
            Comment = "sp2load-uniq-2-1-InsightS",
            LoadEvent = true,
            LoadStation = 1,
            Part = "SP2LoadPart",
            Process = 1,
            Qty = 1,
          },
        ],
        Pallets =
        [
          new MazakPalletRow()
          {
            PalletNumber = 2,
            Fixture = "FixB",
            FixtureGroupV2 = 0,
          },
        ],
        PalletSubStatuses =
        [
          new MazakPalletSubStatusRow()
          {
            PalletNumber = 2,
            ScheduleID = 51,
            PartProcessNumber = 1,
            FixQuantity = 1,
          },
        ],
        PalletStatuses = [],
        PalletPositions =
        [
          new MazakPalletPositionRow()
          {
            PalletNumber = 2,
            PalletPosition = "LS011",
          },
        ],
        Alarms = [],
        MainPrograms = [],
        Fixtures = [],
      };

      var status = BuildCurrentStatus.Build(
        repository,
        _settings,
        _mazakCfg,
        allData,
        machineGroupName: "MC",
        null,
        new DateTime(2026, 4, 28, 12, 0, 0, DateTimeKind.Utc)
      );

      var loadMaterial = status.Material.Where(m => m.Location.PalletNum == 2).ToList();
      await Assert.That(loadMaterial.Count).IsEqualTo(1);
      await Assert.That(loadMaterial[0].Process).IsEqualTo(2);
      await Assert.That(loadMaterial[0].Action.Type).IsEqualTo(InProcessMaterialAction.ActionType.Loading);
      await Assert.That(loadMaterial[0].Action.ProcessAfterLoad).IsEqualTo(2);
    }

    [Test]
    public async Task SplitScheduleLoadToProc3ShowsLoadingAction()
    {
      using var repository = _repoCfg.OpenConnection();
      _settings.Queues["sp3load-in-1"] = new QueueInfo();
      _settings.Queues["sp3load-in-2"] = new QueueInfo();
      _settings.Queues["sp3load-in-3"] = new QueueInfo();

      var splitJob = new Job()
      {
        UniqueStr = "sp3load-uniq",
        PartName = "SP3LoadPart",
        Cycles = 5,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes =
        [
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                PalletNums = [1],
                Load = [1],
                Unload = [1],
                Fixture = "FixA",
                Face = 1,
                PartsPerPallet = 1,
                ExpectedLoadTime = TimeSpan.Zero,
                ExpectedUnloadTime = TimeSpan.Zero,
                InputQueue = "sp3load-in-1",
                OutputQueue = "sp3load-in-2",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "MC",
                    Stations = [1],
                    Program = "sp3load-p1",
                    ExpectedCycleTime = TimeSpan.FromMinutes(10),
                  },
                ],
              },
            ],
          },
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                PalletNums = [2],
                Load = [1],
                Unload = [1],
                Fixture = "FixB",
                Face = 1,
                PartsPerPallet = 1,
                ExpectedLoadTime = TimeSpan.Zero,
                ExpectedUnloadTime = TimeSpan.Zero,
                InputQueue = "sp3load-in-2",
                OutputQueue = "sp3load-in-3",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "MC",
                    Stations = [2],
                    Program = "sp3load-p2",
                    ExpectedCycleTime = TimeSpan.FromMinutes(15),
                  },
                ],
              },
            ],
          },
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                PalletNums = [3],
                Load = [1],
                Unload = [1],
                Fixture = "FixC",
                Face = 1,
                PartsPerPallet = 1,
                ExpectedLoadTime = TimeSpan.Zero,
                ExpectedUnloadTime = TimeSpan.Zero,
                InputQueue = "sp3load-in-3",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "MC",
                    Stations = [3],
                    Program = "sp3load-p3",
                    ExpectedCycleTime = TimeSpan.FromMinutes(20),
                  },
                ],
              },
            ],
          },
        ],
      };

      repository.AddJobs(
        new NewJobs()
        {
          ScheduleId = "sp3load-sch",
          Jobs = [splitJob],
          ExtraParts = [],
        },
        null,
        addAsCopiedToSystem: true
      );

      var allData = new MazakAllData()
      {
        Parts =
        [
          new MazakPartRow()
          {
            PartName = "SP3LoadPart:1:1:1",
            Comment = "sp3load-uniq-1-1-InsightS",
            Processes =
            [
              new MazakPartProcessRow()
              {
                PartName = "SP3LoadPart:1:1:1",
                ProcessNumber = 1,
                Fixture = "FixA",
                MainProgram = "sp3load-p1",
                FixLDS = "1",
                RemoveLDS = "1",
                CutMc = "1",
                FixQuantity = 1,
              },
            ],
          },
          new MazakPartRow()
          {
            PartName = "SP3LoadPart:1:2:2",
            Comment = "sp3load-uniq-2-1-InsightS",
            Processes =
            [
              new MazakPartProcessRow()
              {
                PartName = "SP3LoadPart:1:2:2",
                ProcessNumber = 1,
                Fixture = "FixB",
                MainProgram = "sp3load-p2",
                FixLDS = "1",
                RemoveLDS = "1",
                CutMc = "2",
                FixQuantity = 1,
              },
            ],
          },
          new MazakPartRow()
          {
            PartName = "SP3LoadPart:1:3:3",
            Comment = "sp3load-uniq-3-1-InsightS",
            Processes =
            [
              new MazakPartProcessRow()
              {
                PartName = "SP3LoadPart:1:3:3",
                ProcessNumber = 1,
                Fixture = "FixC",
                MainProgram = "sp3load-p3",
                FixLDS = "1",
                RemoveLDS = "1",
                CutMc = "3",
                FixQuantity = 1,
              },
            ],
          },
        ],
        Schedules =
        [
          new MazakScheduleRow()
          {
            Id = 60,
            PartName = "SP3LoadPart:1:1:1",
            Comment = "sp3load-uniq-1-1-InsightS",
            PlanQuantity = 5,
            CompleteQuantity = 5,
            Priority = 1,
            DueDate = new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc),
            Processes =
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 60,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 0,
                ProcessExecuteQuantity = 0,
                ProcessBadQuantity = 0,
                FixQuantity = 1,
              },
            },
          },
          new MazakScheduleRow()
          {
            Id = 61,
            PartName = "SP3LoadPart:1:2:2",
            Comment = "sp3load-uniq-2-1-InsightS",
            PlanQuantity = 5,
            CompleteQuantity = 1,
            Priority = 2,
            DueDate = new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc),
            Processes =
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 61,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 0,
                ProcessExecuteQuantity = 0,
                ProcessBadQuantity = 0,
                FixQuantity = 1,
              },
            },
          },
          new MazakScheduleRow()
          {
            Id = 62,
            PartName = "SP3LoadPart:1:3:3",
            Comment = "sp3load-uniq-3-1-InsightS",
            PlanQuantity = 5,
            CompleteQuantity = 0,
            Priority = 3,
            DueDate = new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc),
            Processes =
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 62,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 1,
                ProcessExecuteQuantity = 0,
                ProcessBadQuantity = 0,
                FixQuantity = 1,
              },
            },
          },
        ],
        LoadActions =
        [
          new LoadAction()
          {
            Comment = "sp3load-uniq-3-1-InsightS",
            LoadEvent = true,
            LoadStation = 1,
            Part = "SP3LoadPart",
            Process = 1,
            Qty = 1,
          },
        ],
        Pallets =
        [
          new MazakPalletRow()
          {
            PalletNumber = 3,
            Fixture = "FixC",
            FixtureGroupV2 = 0,
          },
        ],
        PalletSubStatuses =
        [
          new MazakPalletSubStatusRow()
          {
            PalletNumber = 3,
            ScheduleID = 62,
            PartProcessNumber = 1,
            FixQuantity = 1,
          },
        ],
        PalletStatuses = [],
        PalletPositions =
        [
          new MazakPalletPositionRow()
          {
            PalletNumber = 3,
            PalletPosition = "LS011",
          },
        ],
        Alarms = [],
        MainPrograms = [],
        Fixtures = [],
      };

      var status = BuildCurrentStatus.Build(
        repository,
        _settings,
        _mazakCfg,
        allData,
        machineGroupName: "MC",
        null,
        new DateTime(2026, 4, 28, 12, 0, 0, DateTimeKind.Utc)
      );

      var loadMaterial = status.Material.Where(m => m.Location.PalletNum == 3).ToList();
      await Assert.That(loadMaterial.Count).IsEqualTo(1);
      await Assert.That(loadMaterial[0].Process).IsEqualTo(3);
      await Assert.That(loadMaterial[0].Action.Type).IsEqualTo(InProcessMaterialAction.ActionType.Loading);
      await Assert.That(loadMaterial[0].Action.ProcessAfterLoad).IsEqualTo(3);
      await Assert.That(loadMaterial[0].Action.LoadOntoFace).IsEqualTo(3);
    }
  }
}
