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
using System.Linq;
using System.IO;
using System.Collections.Generic;
using Xunit;
using FluentAssertions;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;
using MazakMachineInterface;
using Newtonsoft.Json;
using NSubstitute;

namespace MachineWatchTest
{
  public class BuildCurrentStatusSpec : IDisposable
  {
    private RepositoryConfig _repoCfg;
    private IRepository _memoryLog;
    private JsonSerializerSettings jsonSettings;
    private FMSSettings _settings;
    private IMachineGroupName _machGroupName;

    private IQueueSyncFault queueSyncFault;

    public BuildCurrentStatusSpec()
    {
      _repoCfg = RepositoryConfig.InitializeSingleThreadedMemoryDB(new FMSSettings());
      _memoryLog = _repoCfg.OpenConnection();

      _settings = new FMSSettings();
      _settings.Queues["castings"] = new QueueSize();
      _settings.Queues["queueAAA"] = new QueueSize();
      _settings.Queues["queueBBB"] = new QueueSize();
      _settings.Queues["queueCCC"] = new QueueSize();

      jsonSettings = new JsonSerializerSettings();
      jsonSettings.Converters.Add(new BlackMaple.MachineFramework.TimespanConverter());
      jsonSettings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
      jsonSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
      jsonSettings.Formatting = Formatting.Indented;
      jsonSettings.ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor;

      queueSyncFault = Substitute.For<IQueueSyncFault>();
      queueSyncFault.CurrentQueueMismatch.Returns(false);

      _machGroupName = Substitute.For<IMachineGroupName>();
      _machGroupName.MachineGroupName.Returns("MC");
    }

    public void Dispose()
    {
      _repoCfg.CloseMemoryConnection();
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

    [Theory]
    [InlineData("basic-no-material")]
    [InlineData("basic-load-material")]
    [InlineData("basic-cutting")]
    [InlineData("basic-load-queue")]
    [InlineData("basic-unload-queues")]
    [InlineData("multiface-inital-load")]
    [InlineData("multiface-transfer-faces")]
    [InlineData("multiface-transfer-faces-and-unload")]
    [InlineData("multiface-transfer-user-jobs")]
    [InlineData("pathgroups-load")]
    public void StatusSnapshot(string scenario)
    {

      IRepository repository;
      bool close = false;
      var existingLogPath =
        Path.Combine("..", "..", "..", "mazak", "read-snapshots", scenario + ".log.db");
      string _tempLogFile = System.IO.Path.GetTempFileName();
      if (File.Exists(existingLogPath))
      {
        System.IO.File.Copy(existingLogPath, _tempLogFile, overwrite: true);
        repository = RepositoryConfig.InitializeEventDatabase(new FMSSettings(), _tempLogFile).OpenConnection();
        close = true;
      }
      else
      {
        repository = _memoryLog;
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
        newJobs = JsonConvert.DeserializeObject<NewJobs>(
          File.ReadAllText(
            Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
          jsonSettings
        );
      }
      else if (scenario.Contains("multiface"))
      {
        newJobs = JsonConvert.DeserializeObject<NewJobs>(
          File.ReadAllText(
            Path.Combine("..", "..", "..", "sample-newjobs", "multi-face.json")),
          jsonSettings
        );
      }
      else if (scenario.Contains("pathgroups"))
      {
        newJobs = JsonConvert.DeserializeObject<NewJobs>(
          File.ReadAllText(
            Path.Combine("..", "..", "..", "sample-newjobs", "path-groups.json")),
          jsonSettings
        );

      }
      repository.AddJobs(newJobs, null, addAsCopiedToSystem: true);

      var allData = JsonConvert.DeserializeObject<MazakAllData>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "mazak", "read-snapshots", scenario + ".data.json")),
          jsonSettings
      );

      if (scenario == "basic-no-material")
      {
        queueSyncFault.CurrentQueueMismatch.Returns(true);
      }

      CurrentStatus status;
      try
      {
        status = BuildCurrentStatus.Build(repository, _settings, _machGroupName, queueSyncFault, MazakDbType.MazakSmooth, allData,
          new DateTime(2018, 7, 19, 20, 42, 3, DateTimeKind.Utc));
      }
      finally
      {
        if (close) repository.Dispose();
        File.Delete(_tempLogFile);
      }

      /*
      File.WriteAllText(
        Path.Combine("..", "..", "..", "mazak", "read-snapshots", scenario + ".status.json"),
        JsonConvert.SerializeObject(status, jsonSettings)
      );
      */

      var expectedStatus = JsonConvert.DeserializeObject<CurrentStatus>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "mazak", "read-snapshots", scenario + ".status.json")),
          jsonSettings
      );

      status.Should().BeEquivalentTo(expectedStatus, options =>
        options.Excluding(c => c.TimeOfCurrentStatusUTC)
          .ComparingByMembers<CurrentStatus>()
          .ComparingByMembers<ActiveJob>()
          .ComparingByMembers<ProcessInfo>()
          .ComparingByMembers<ProcPathInfo>()
          .ComparingByMembers<MachiningStop>()
          .ComparingByMembers<InProcessMaterial>()
          .ComparingByMembers<PalletStatus>()
      );
    }

    [Fact]
    public void PendingLoad()
    {
      NewJobs newJobs = JsonConvert.DeserializeObject<NewJobs>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
        jsonSettings
      );
      _memoryLog.AddJobs(newJobs, null, addAsCopiedToSystem: true);

      var allData = JsonConvert.DeserializeObject<MazakAllData>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "mazak", "read-snapshots", "basic-after-load.data.json")),
          jsonSettings
      );

      _memoryLog.AddPendingLoad("1", "thekey", 1, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), "foreignid");

      var matId = _memoryLog.AllocateMaterialID("aaa-schId1234", "aaa", 2);
      _memoryLog.RecordAddMaterialToQueue(new EventLogMaterial() { MaterialID = matId, Process = 0, Face = "" }, "castings", -1,
        operatorName: null, reason: "TheQueueReason"
      );

      var status = BuildCurrentStatus.Build(_memoryLog, _settings, _machGroupName, queueSyncFault, MazakDbType.MazakSmooth, allData,
          new DateTime(2018, 7, 19, 20, 42, 3, DateTimeKind.Utc));

      var expectedStatus = JsonConvert.DeserializeObject<CurrentStatus>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "mazak", "read-snapshots", "basic-after-load.status.json")),
          jsonSettings
      );

      status.Should().BeEquivalentTo(expectedStatus, options =>
        options.Excluding(c => c.TimeOfCurrentStatusUTC)
          .ComparingByMembers<CurrentStatus>()
          .ComparingByMembers<ActiveJob>()
          .ComparingByMembers<ProcessInfo>()
          .ComparingByMembers<ProcPathInfo>()
          .ComparingByMembers<MachiningStop>()
          .ComparingByMembers<InProcessMaterial>()
          .ComparingByMembers<PalletStatus>()
      );
    }

  }
}