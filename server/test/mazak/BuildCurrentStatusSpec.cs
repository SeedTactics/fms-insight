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
    public async Task ProvisionalWorkorder()
    {
      using var repo = _repoCfg.OpenConnection();

      var newJobs = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
        jsonSettings
      );

      newJobs = newJobs with { Jobs = newJobs.Jobs.Select(j => j.UniqueStr == "ccc-schId1234" ? j with { ProvisionalWorkorderId = "provWorkCCC" } : j).ToImmutableList() };
      repo.AddJobs(newJobs, null, addAsCopiedToSystem: true);

      var allData = JsonSerializer.Deserialize<MazakAllData>(
        File.ReadAllText(Path.Combine("..", "..", "..", "mazak", "read-snapshots", "basic-load-material.data.json")),
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

      var loadingMat = status.Material.Where(m => m.Action.Type == InProcessMaterialAction.ActionType.Loading);
      await Assert.That(loadingMat.Count()).IsEqualTo(2);
      await Assert.That(loadingMat.Select(m => m.WorkorderId)).IsEquivalentTo(["provWorkCCC", "provWorkCCC"]);
    }
  }
}
