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
using System.Linq;
using System.IO;
using System.Collections.Generic;
using Xunit;
using FluentAssertions;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;
using MazakMachineInterface;
using NSubstitute;
using Newtonsoft.Json;

namespace MachineWatchTest
{
  public class WriteJobsSpec : IDisposable
  {
    private class WriteMock : IWriteData
    {
      public MazakDbType MazakType => MazakDbType.MazakSmooth;
      public MazakWriteData DeletePartsPals { get; set; }
      public MazakWriteData Fixtures { get; set; }
      public MazakWriteData AddParts { get; set; }
      public MazakWriteData AddSchedules { get; set; }
      public MazakWriteData UpdateSchedules { get; set; }
      public string errorForPrefix;

      public void Save(MazakWriteData data, string prefix)
      {
        if (prefix == "Delete Parts Pallets")
        {
          DeletePartsPals = data;
        }
        else if (prefix == "Fixtures")
        {
          Fixtures = data;
        }
        else if (prefix == "Add Parts")
        {
          AddParts = data;
        }
        else if (prefix == "Add Schedules")
        {
          AddSchedules = data;
        }
        else if (prefix == "Update schedules")
        {
          UpdateSchedules = data;
        }
        else
        {
          Assert.True(false, "Unexpected prefix " + prefix);
        }
        if (errorForPrefix == prefix)
        {
          throw new Exception("Sample error");
        }
      }
    }

    private JobLogDB _logDB;
    private JobDB _jobDB;
    private IWriteJobs _writeJobs;
    private WriteMock _writeMock;
    private IReadDataAccess _readMock;
    private JsonSerializerSettings jsonSettings;
    private FMSSettings _settings;

    public WriteJobsSpec()
    {
      var logConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
      logConn.Open();
      _logDB = new JobLogDB(new FMSSettings(), logConn);
      _logDB.CreateTables(firstSerialOnEmpty: null);

      var jobConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
      jobConn.Open();
      _jobDB = new JobDB(jobConn);
      _jobDB.CreateTables();

      _writeMock = new WriteMock();

      _readMock = Substitute.For<IReadDataAccess>();
      _readMock.MazakType.Returns(MazakDbType.MazakSmooth);
      _readMock.LoadAllData().Returns(new MazakAllData()
      {
        Schedules = new[] {
          // a completed schedule, should be deleted
					new MazakScheduleRow()
          {
            Id = 1,
            PartName = "part1:1:1",
            Comment = MazakPart.CreateComment("uniq1", new [] {1}, false),
            PlanQuantity = 15,
            CompleteQuantity = 15,
            Priority = 50,
            Processes = {
              new MazakScheduleProcessRow() {
                MazakScheduleRowId = 1,
                FixedMachineFlag = 1,
                ProcessNumber = 1
              }
            }
          },
          // a non-completed schedule, should be untouched
					new MazakScheduleRow()
          {
            Id = 2,
            PartName = "part2:1:1",
            Comment = MazakPart.CreateComment("uniq2", new [] {1}, false),
            PlanQuantity = 15,
            CompleteQuantity = 10,
            Priority = 50,
            Processes = {
              new MazakScheduleProcessRow() {
                MazakScheduleRowId = 1,
                FixedMachineFlag = 1,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 3,
                ProcessExecuteQuantity = 2
              }
            }
          },
        },
        Parts = new[] {
          // should be deleted, since corresponding schedule is deleted
					new MazakPartRow() {
            PartName = "part1:1:1",
            Comment = MazakPart.CreateComment("uniq1", new[] {1}, false),
            Processes = {
              new MazakPartProcessRow() {
                PartName = "part1:1:1",
                ProcessNumber = 1,
                FixQuantity = 5,
                Fixture = "fixtoremove"
              }
            }
          },
          //should be kept, since schedule is kept
					new MazakPartRow() {
            PartName = "part2:1:1",
            Comment = MazakPart.CreateComment("uniq2", new[] {1}, false),
            Processes = {
              new MazakPartProcessRow() {
                PartName = "part2:1:1",
                ProcessNumber = 1,
                FixQuantity = 2,
                Fixture = "fixtokeep"
              }
            }
          },
        },
        Fixtures = new[] {
          new MazakFixtureRow() { FixtureName = "fixtoremove", Comment = "Insight" },
          new MazakFixtureRow() { FixtureName = "fixtokeep", Comment = "Insight"}
        },
        Pallets = new[] {
          new MazakPalletRow() { PalletNumber = 5, Fixture = "fixtoremove"},
          new MazakPalletRow() { PalletNumber = 6, Fixture = "fixtokeep"}
        },
        PalletSubStatuses = Enumerable.Empty<MazakPalletSubStatusRow>(),
        PalletPositions = Enumerable.Empty<MazakPalletPositionRow>(),
        LoadActions = Enumerable.Empty<LoadAction>(),
        MainPrograms =
          Enumerable.Concat(
            (new[] { "1001", "1002", "1003", "1004", "1005" }).Select(p => new MazakProgramRow() { MainProgram = p, Comment = "" }),
            new[] {
              new MazakProgramRow()
              {
                MainProgram = System.IO.Path.Combine("theprogdir", "prog-bbb-1_rev2.EIA"),
                Comment = "Insight:2:prog-bbb-1"
              },
              new MazakProgramRow()
              {
                MainProgram = System.IO.Path.Combine("theprogdir", "prog-bbb-1_rev3.EIA"),
                Comment = "Insight:3:prog-bbb-1"
              }
            }
          )
      });
      _readMock.LoadSchedulesPartsPallets().Returns(x => new MazakSchedulesPartsPallets()
      {
        Schedules = Enumerable.Empty<MazakScheduleRow>(),
        Parts = _writeMock.AddParts.Parts,
        Pallets = _writeMock.AddParts.Pallets,
        PalletSubStatuses = Enumerable.Empty<MazakPalletSubStatusRow>(),
        PalletPositions = Enumerable.Empty<MazakPalletPositionRow>(),
        LoadActions = Enumerable.Empty<LoadAction>(),
        MainPrograms = (new[] {
          "1001", "1002", "1003", "1004", "1005"
        }).Select(p => new MazakProgramRow() { MainProgram = p, Comment = "" }),
      });

      _settings = new FMSSettings();
      _settings.Queues["castings"] = new QueueSize();
      _settings.Queues["queueAAA"] = new QueueSize();
      _settings.Queues["queueBBB"] = new QueueSize();
      _settings.Queues["queueCCC"] = new QueueSize();


      _writeJobs = new WriteJobs(
        _writeMock,
        _readMock,
        Substitute.For<IHoldManagement>(),
        _jobDB,
        _logDB,
        _settings,
        check: false,
        useStarting: true,
        progDir: "theprogdir");

      jsonSettings = new JsonSerializerSettings();
      jsonSettings.Converters.Add(new BlackMaple.MachineFramework.TimespanConverter());
      jsonSettings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
      jsonSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
      jsonSettings.Formatting = Formatting.Indented;
    }

    public void Dispose()
    {
      _logDB.Close();
      _jobDB.Close();
    }

    private void ShouldMatchSnapshot<T>(T val, string snapshot, Action<T> adjust = null)
    {
      /*
      File.WriteAllText(
          Path.Combine("..", "..", "..", "mazak", "write-snapshots", snapshot),
          JsonConvert.SerializeObject(val, jsonSettings)
      );
      */
      var expected = JsonConvert.DeserializeObject<T>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "mazak", "write-snapshots", snapshot)),
          jsonSettings
      );

      if (adjust != null)
        adjust(expected);

      val.Should().BeEquivalentTo(expected);
    }

    private static void AdjustProgramPath(MazakWriteData w)
    {
      // snapshots contain forward slash since they were made on linux
      if (System.IO.Path.DirectorySeparatorChar == '/') return;

      foreach (var part in w.Parts)
      {
        foreach (var proc in part.Processes)
        {
          if (!proc.MainProgram.Contains('/')) continue;
          var path = proc.MainProgram.Split('/');
          proc.MainProgram = System.IO.Path.Combine(path);
        }
      }
      foreach (var prog in w.Programs)
      {
        if (!prog.MainProgram.Contains('/')) continue;
        var path = prog.MainProgram.Split('/');
        prog.MainProgram = System.IO.Path.Combine(path);
      }
    }

    [Fact]
    public void RejectsMismatchedPreviousSchedule()
    {
      var newJobs = JsonConvert.DeserializeObject<NewJobs>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
          jsonSettings
      );
      _jobDB.AddJobs(newJobs, null);

      var newJobsMultiFace = JsonConvert.DeserializeObject<NewJobs>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "sample-newjobs", "multi-face.json")),
        jsonSettings
      );

      _writeJobs.Invoking(x => x.AddJobs(newJobsMultiFace, "xxxx"))
        .Should()
        .Throw<BadRequestException>()
        .WithMessage(
        "Expected previous schedule ID does not match current schedule ID.  Another user may have already created a schedule."
      );
    }

    [Fact]
    public void BasicCreate()
    {
      var completedJob = new JobPlan("uniq1", 1);
      completedJob.PartName = "part1";
      completedJob.SetPlannedCyclesOnFirstProcess(1, 15);
      var inProcJob = new JobPlan("uniq2", 1);
      inProcJob.PartName = "part2";
      inProcJob.SetPlannedCyclesOnFirstProcess(1, 15);
      _jobDB.AddJobs(new NewJobs() { Jobs = new List<JobPlan> { completedJob, inProcJob } }, null);

      _jobDB.LoadUnarchivedJobs().Jobs.Select(j => j.UniqueStr).Should().BeEquivalentTo(
        new[] { "uniq1", "uniq2" }
      );

      var newJobs = JsonConvert.DeserializeObject<NewJobs>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
        jsonSettings
      );
      _writeJobs.AddJobs(newJobs, null);


      ShouldMatchSnapshot(_writeMock.UpdateSchedules, "fixtures-queues-updatesch.json");
      ShouldMatchSnapshot(_writeMock.DeletePartsPals, "fixtures-queues-delparts.json");
      ShouldMatchSnapshot(_writeMock.Fixtures, "fixtures-queues-fixtures.json", AdjustProgramPath);
      ShouldMatchSnapshot(_writeMock.AddParts, "fixtures-queues-parts.json");
      ShouldMatchSnapshot(_writeMock.AddSchedules, "fixtures-queues-schedules.json");

      var start = newJobs.Jobs.First().RouteStartingTimeUTC;
      _jobDB.LoadJobsNotCopiedToSystem(start, start.AddMinutes(1)).Jobs.Should().BeEmpty();

      // uniq1 was archived
      _jobDB.LoadUnarchivedJobs().Jobs.Select(j => j.UniqueStr).Should().BeEquivalentTo(
        new[] { "uniq2", "aaa-schId1234", "bbb-schId1234", "ccc-schId1234" }
      );

      // without any decrements
      _jobDB.LoadDecrementsForJob("uniq1").Should().BeEmpty();
    }

    [Fact]
    public void MultiPathGroups()
    {
      // mazak has 15 planned quantity, set job to have 20
      var completedJob = new JobPlan("uniq1", 1);
      completedJob.PartName = "part1";
      completedJob.SetPlannedCyclesOnFirstProcess(1, 20);
      var inProcJob = new JobPlan("uniq2", 1);
      inProcJob.PartName = "part2";
      inProcJob.SetPlannedCyclesOnFirstProcess(1, 20);
      _jobDB.AddJobs(new NewJobs() { Jobs = new List<JobPlan> { completedJob, inProcJob } }, null);

      _jobDB.LoadUnarchivedJobs().Jobs.Select(j => j.UniqueStr).Should().BeEquivalentTo(
        new[] { "uniq1", "uniq2" }
      );

      var newJobs = JsonConvert.DeserializeObject<NewJobs>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "sample-newjobs", "path-groups.json")),
        jsonSettings
      );

      _writeJobs.AddJobs(newJobs, null);

      ShouldMatchSnapshot(_writeMock.UpdateSchedules, "path-groups-updatesch.json");
      ShouldMatchSnapshot(_writeMock.DeletePartsPals, "path-groups-delparts.json");
      ShouldMatchSnapshot(_writeMock.Fixtures, "path-groups-fixtures.json", AdjustProgramPath);
      ShouldMatchSnapshot(_writeMock.AddParts, "path-groups-parts.json");
      ShouldMatchSnapshot(_writeMock.AddSchedules, "path-groups-schedules.json");

      _jobDB.LoadUnarchivedJobs().Jobs.Select(j => j.UniqueStr).Should().BeEquivalentTo(
        new[] { "uniq2", "part1-schId1234", "part2-schId1234", "part3-schId1234" }
      );

      // should have a decrement of 20 - 15
      _jobDB.LoadDecrementsForJob("uniq1").Should().BeEquivalentTo(
        new[] {new DecrementQuantity()
        {
          DecrementId = 0,
          TimeUTC = DateTime.UtcNow,
          Quantity = 5
        }},
        options => options
          .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: 20000)) // 20 secs
          .WhenTypeIs<DateTime>()
      );
    }

    [Fact]
    public void CreatesPrograms()
    {
      //aaa-1  has prog prog-aaa-1 rev null
      //aaa-2 has prog prog-aaa-2 rev 4
      //bbb-1 has prog prog-bbb-1 rev 3
      //bbb-2 has prog prog-bbb-2 rev null

      //ccc is same as aaa

      var newJobs = JsonConvert.DeserializeObject<NewJobs>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "sample-newjobs", "managed-progs.json")),
        jsonSettings
      );

      _jobDB.AddPrograms(new[] {
        new ProgramEntry() {
          ProgramName = "prog-aaa-1",
          Revision = 7,
          ProgramContent = "prog-aaa-1 content rev 7"
        },
        new ProgramEntry() {
          ProgramName = "prog-bbb-1",
          Revision = 3,
          ProgramContent = "prog-bbb-1 content rev 3"
        }
      }, newJobs.Jobs.First().RouteStartingTimeUTC);

      _writeJobs.AddJobs(newJobs, null);

      ShouldMatchSnapshot(_writeMock.UpdateSchedules, "fixtures-queues-updatesch.json");
      ShouldMatchSnapshot(_writeMock.DeletePartsPals, "fixtures-queues-delparts.json");
      ShouldMatchSnapshot(_writeMock.Fixtures, "managed-progs-fixtures.json", AdjustProgramPath);
      ShouldMatchSnapshot(_writeMock.AddParts, "managed-progs-parts.json", AdjustProgramPath);
      ShouldMatchSnapshot(_writeMock.AddSchedules, "fixtures-queues-schedules.json");
    }

    [Fact]
    public void ErrorDuringPartsPallets()
    {
      _writeMock.errorForPrefix = "Add Parts";

      var newJobs = JsonConvert.DeserializeObject<NewJobs>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
          jsonSettings
      );
      _writeJobs.Invoking(x => x.AddJobs(newJobs, null))
        .Should()
        .Throw<Exception>()
        .WithMessage("Sample error");

      ShouldMatchSnapshot(_writeMock.UpdateSchedules, "fixtures-queues-updatesch.json");
      ShouldMatchSnapshot(_writeMock.DeletePartsPals, "fixtures-queues-delparts.json");
      ShouldMatchSnapshot(_writeMock.Fixtures, "fixtures-queues-fixtures.json", AdjustProgramPath);
      ShouldMatchSnapshot(_writeMock.AddParts, "fixtures-queues-parts.json");
      _writeMock.AddSchedules.Should().BeNull();

      var start = newJobs.Jobs.First().RouteStartingTimeUTC;
      _jobDB.LoadJobsNotCopiedToSystem(start, start.AddMinutes(1)).Jobs.Should().BeEmpty();
    }

    [Fact]
    public void ErrorDuringSchedule()
    {
      _writeMock.errorForPrefix = "Add Schedules";

      var newJobs = JsonConvert.DeserializeObject<NewJobs>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
          jsonSettings
      );
      _writeJobs.Invoking(x => x.AddJobs(newJobs, null))
        .Should()
        .Throw<Exception>()
        .WithMessage("Sample error");

      ShouldMatchSnapshot(_writeMock.UpdateSchedules, "fixtures-queues-updatesch.json");
      ShouldMatchSnapshot(_writeMock.DeletePartsPals, "fixtures-queues-delparts.json");
      ShouldMatchSnapshot(_writeMock.Fixtures, "fixtures-queues-fixtures.json", AdjustProgramPath);
      ShouldMatchSnapshot(_writeMock.AddParts, "fixtures-queues-parts.json");
      ShouldMatchSnapshot(_writeMock.AddSchedules, "fixtures-queues-schedules.json");

      var start = newJobs.Jobs.First().RouteStartingTimeUTC;
      _jobDB.LoadJobsNotCopiedToSystem(start, start.AddMinutes(1)).Jobs
        .Should().BeEquivalentTo(
          newJobs.Jobs,
          options => options
            .Excluding(j => j.Comment)
            .Excluding(j => j.HoldEntireJob));

      //try again still with error
      _writeMock.AddSchedules = null;
      _writeJobs.Invoking(x => x.RecopyJobsToMazak(start))
        .Should()
        .Throw<Exception>()
        .WithMessage("Sample error");

      ShouldMatchSnapshot(_writeMock.AddSchedules, "fixtures-queues-schedules.json");
      _jobDB.LoadJobsNotCopiedToSystem(start, start.AddMinutes(1)).Jobs
        .Should().BeEquivalentTo(
          newJobs.Jobs,
          options => options
            .Excluding(j => j.Comment)
            .Excluding(j => j.HoldEntireJob));

      //finally succeed without error
      _writeMock.errorForPrefix = null;
      _writeJobs.RecopyJobsToMazak(start);
      ShouldMatchSnapshot(_writeMock.AddSchedules, "fixtures-queues-schedules.json");

      _jobDB.LoadJobsNotCopiedToSystem(start, start.AddMinutes(1)).Jobs.Should().BeEmpty();
    }
  }
}