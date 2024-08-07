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
using System.IO;
using System.Linq;
using System.Text.Json;
using BlackMaple.MachineFramework;
using FluentAssertions;
using MazakMachineInterface;
using NSubstitute;
using Xunit;

namespace MachineWatchTest
{
  public sealed class WriteJobsSpec : IDisposable
  {
    private class WriteMock : IWriteData
    {
      public MazakDbType MazakType => MazakDbType.MazakSmooth;
      public MazakWriteData DeleteParts { get; set; }
      public MazakWriteData DeletePallets { get; set; }
      public MazakWriteData DelFixtures { get; set; }
      public MazakWriteData AddFixtures { get; set; }
      public MazakWriteData AddParts { get; set; }
      public MazakWriteData AddSchedules { get; set; }
      public MazakWriteData UpdateSchedules { get; set; }
      public string errorForPrefix;

      public void Save(MazakWriteData data, string prefix)
      {
        if (prefix == "Delete Parts")
        {
          DeleteParts = data;
        }
        else if (prefix == "Delete Pallets")
        {
          DeletePallets = data;
        }
        else if (prefix == "Delete Fixtures")
        {
          DelFixtures = data;
        }
        else if (prefix == "Add Fixtures")
        {
          AddFixtures = data;
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
          Assert.Fail("Unexpected prefix " + prefix);
        }
        if (errorForPrefix == prefix)
        {
          throw new Exception("Sample error");
        }
      }
    }

    private readonly RepositoryConfig _repoCfg;
    private readonly IRepository _jobDB;
    private WriteMock _writeMock;
    private readonly IReadDataAccess _readMock;
    private readonly JsonSerializerOptions jsonSettings;
    private readonly FMSSettings _settings;
    private readonly MazakConfig _mazakCfg;
    private readonly MazakAllData _initialAllData;
    private static readonly DateTime fixtureQueueTime = new(2018, 07, 19, 1, 2, 3, DateTimeKind.Utc);

    public WriteJobsSpec()
    {
      _repoCfg = RepositoryConfig.InitializeMemoryDB(
        new SerialSettings() { ConvertMaterialIDToSerial = (id) => id.ToString() }
      );
      _jobDB = _repoCfg.OpenConnection();

      _writeMock = new WriteMock();

      _readMock = Substitute.For<IReadDataAccess>();
      _readMock.MazakType.Returns(MazakDbType.MazakSmooth);

      _initialAllData = new MazakAllData()
      {
        Schedules = new[]
        {
          // a completed schedule, should be deleted
          new MazakScheduleRow()
          {
            Id = 1,
            PartName = "part1:1:1",
            Comment = "uniq1-Insight",
            PlanQuantity = 15,
            CompleteQuantity = 15,
            Priority = 50,
            Processes =
            {
              new MazakScheduleProcessRow()
              {
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
            Comment = "uniq2-Insight",
            PlanQuantity = 15,
            CompleteQuantity = 10,
            Priority = 50,
            Processes =
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 1,
                FixedMachineFlag = 1,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 3,
                ProcessExecuteQuantity = 2
              }
            }
          },
        },
        Parts = new[]
        {
          // should be deleted, since corresponding schedule is deleted
          new MazakPartRow()
          {
            PartName = "part1:1:1",
            Comment = "uniq1-Insight",
            Processes = new[]
            {
              new MazakPartProcessRow()
              {
                PartName = "part1:1:1",
                ProcessNumber = 1,
                FixQuantity = 5,
                Fixture = "fixtoremove"
              }
            }
          },
          //should be kept, since schedule is kept
          new MazakPartRow()
          {
            PartName = "part2:1:1",
            Comment = "uniq2-Insight",
            Processes = new[]
            {
              new MazakPartProcessRow()
              {
                PartName = "part2:1:1",
                ProcessNumber = 1,
                FixQuantity = 2,
                Fixture = "fixtokeep"
              }
            }
          },
        },
        Fixtures = new[]
        {
          new MazakFixtureRow() { FixtureName = "fixtoremove", Comment = "Insight" },
          new MazakFixtureRow() { FixtureName = "fixtokeep", Comment = "Insight" }
        },
        Pallets = new[]
        {
          new MazakPalletRow() { PalletNumber = 5, Fixture = "fixtoremove" },
          new MazakPalletRow() { PalletNumber = 6, Fixture = "fixtokeep" }
        },
        PalletSubStatuses = Enumerable.Empty<MazakPalletSubStatusRow>(),
        PalletPositions = Enumerable.Empty<MazakPalletPositionRow>(),
        LoadActions = Enumerable.Empty<LoadAction>(),
        MainPrograms = Enumerable.Concat(
          ImmutableList
            .Create("1001", "1002", "1003", "1004", "1005")
            .Select(p => new MazakProgramRow() { MainProgram = p, Comment = "" }),
          new[]
          {
            new MazakProgramRow()
            {
              MainProgram = System.IO.Path.Combine("theprogdir", "rev2", "prog-bbb-1.EIA"),
              Comment = "Insight:2:prog-bbb-1"
            },
            new MazakProgramRow()
            {
              MainProgram = System.IO.Path.Combine("theprogdir", "rev3", "prog-bbb-1.EIA"),
              Comment = "Insight:3:prog-bbb-1"
            }
          }
        )
      };

      //  write jobs calls LoadAllData between creating fixtures and schedules
      _readMock
        .LoadAllData()
        .Returns(
          (context) =>
            new MazakAllData()
            {
              Schedules = Enumerable.Empty<MazakScheduleRow>(),
              Parts = _writeMock.AddParts.Parts,
              Pallets = _writeMock.AddParts.Pallets,
              PalletSubStatuses = Enumerable.Empty<MazakPalletSubStatusRow>(),
              PalletPositions = Enumerable.Empty<MazakPalletPositionRow>(),
              LoadActions = Enumerable.Empty<LoadAction>(),
              MainPrograms = ImmutableList
                .Create("1001", "1002", "1003", "1004", "1005")
                .Select(p => new MazakProgramRow() { MainProgram = p, Comment = "" }),
            }
        );

      _settings = new FMSSettings();
      _settings.Queues["castings"] = new QueueInfo();
      _settings.Queues["queueAAA"] = new QueueInfo();
      _settings.Queues["queueBBB"] = new QueueInfo();
      _settings.Queues["queueCCC"] = new QueueInfo();

      _mazakCfg = new MazakConfig()
      {
        SQLConnectionString = "unused connection string",
        LogCSVPath = "unused log path",
        DBType = MazakDbType.MazakSmooth,
        ProgramDirectory = "theprogdir",
        UseStartingOffsetForDueDate = true
      };

      jsonSettings = new JsonSerializerOptions();
      FMSInsightWebHost.JsonSettings(jsonSettings);
      jsonSettings.WriteIndented = true;
    }

    public void Dispose()
    {
      _jobDB.Dispose();
      _repoCfg.Dispose();
    }

    private void ShouldMatchSnapshot<T>(T val, string snapshot)
    {
      /*
      File.WriteAllText(
        Path.Combine("..", "..", "..", "mazak", "write-snapshots", snapshot),
        JsonSerializer.Serialize(val, jsonSettings)
      );
      */
      var expected = JsonSerializer.Deserialize<T>(
        File.ReadAllText(Path.Combine("..", "..", "..", "mazak", "write-snapshots", snapshot)),
        jsonSettings
      );

      val.Should()
        .BeEquivalentTo(
          expected,
          options =>
            options
              .ComparingByMembers<MazakPartRow>()
              .ComparingByMembers<MazakScheduleRow>()
              .ComparingByMembers<MazakWriteData>()
              .ComparingByMembers<NewMazakProgram>()
              .ComparingByMembers<MazakPartProcessRow>()
              .Using<string>(ctx =>
              {
                if (ctx.Expectation == null)
                {
                  ctx.Subject.Should().BeNull();
                }
                else
                {
                  var path = ctx.Expectation.Split('/');
                  ctx.Subject.Should().Be(System.IO.Path.Combine(path));
                }
              })
              .When(info => info.Path.EndsWith("MainProgram"))
        );
    }

    [Fact]
    public void BasicCreate()
    {
      var completedJob = new Job()
      {
        UniqueStr = "uniq1",
        PartName = "part1",
        RouteStartUTC = DateTime.UtcNow.AddMinutes(-20),
        RouteEndUTC = DateTime.UtcNow,
        Archived = false,
        Processes = [new ProcessInfo() { Paths = [JobLogTest.EmptyPath] }],
        Cycles = 15,
      };
      var inProcJob = new Job()
      {
        UniqueStr = "uniq2",
        PartName = "part2",
        RouteStartUTC = DateTime.UtcNow.AddHours(-4),
        RouteEndUTC = DateTime.UtcNow,
        Archived = false,
        Processes = [new ProcessInfo() { Paths = [JobLogTest.EmptyPath] }],
        Cycles = 15,
      };
      _jobDB.AddJobs(
        new NewJobs() { Jobs = [completedJob, inProcJob], ScheduleId = "thebasicSchId" },
        null,
        addAsCopiedToSystem: true
      );

      _jobDB.LoadUnarchivedJobs().Select(j => j.UniqueStr).Should().BeEquivalentTo(["uniq1", "uniq2"]);

      var newJobs = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
        jsonSettings
      );
      _jobDB.AddJobs(newJobs, expectedPreviousScheduleId: null, addAsCopiedToSystem: false);

      WriteJobs
        .SyncFromDatabase(
          _initialAllData,
          _jobDB,
          _writeMock,
          _readMock,
          _settings,
          _mazakCfg,
          fixtureQueueTime
        )
        .Should()
        .BeTrue();

      ShouldMatchSnapshot(_writeMock.UpdateSchedules, "fixtures-queues-updatesch.json");
      ShouldMatchSnapshot(_writeMock.DeleteParts, "fixtures-queues-delparts.json");
      _writeMock.DeletePallets.Pallets.Should().BeEmpty();
      ShouldMatchSnapshot(_writeMock.AddFixtures, "fixtures-queues-add-fixtures.json");
      ShouldMatchSnapshot(_writeMock.DelFixtures, "fixtures-queues-del-fixtures.json");
      ShouldMatchSnapshot(_writeMock.AddParts, "fixtures-queues-parts.json");
      ShouldMatchSnapshot(_writeMock.AddSchedules, "fixtures-queues-schedules.json");

      var start = newJobs.Jobs.First().RouteStartUTC;
      _jobDB.LoadJobsNotCopiedToSystem(start, start.AddMinutes(1)).Should().BeEmpty();

      // uniq1 was archived
      _jobDB
        .LoadUnarchivedJobs()
        .Select(j => j.UniqueStr)
        .Should()
        .BeEquivalentTo(["uniq2", "aaa-schId1234", "bbb-schId1234", "ccc-schId1234"]);

      // without any decrements
      _jobDB.LoadDecrementsForJob("uniq1").Should().BeEmpty();

      _jobDB
        .LoadUnarchivedJobs()
        .Select(j => (j.UniqueStr, j.CopiedToSystem))
        .Should()
        .BeEquivalentTo(
          [("uniq2", true), ("aaa-schId1234", true), ("bbb-schId1234", true), ("ccc-schId1234", true)]
        );

      WriteJobs
        .SyncFromDatabase(
          _initialAllData,
          _jobDB,
          _writeMock,
          _readMock,
          _settings,
          _mazakCfg,
          fixtureQueueTime
        )
        .Should()
        .BeFalse();
    }

    [Fact]
    public void CreatesPrograms()
    {
      //aaa-1  has prog prog-aaa-1 rev null
      //aaa-2 has prog prog-aaa-2 rev 4
      //bbb-1 has prog prog-bbb-1 rev 3
      //bbb-2 has prog prog-bbb-2 rev null

      //ccc is same as aaa

      var newJobs = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "managed-progs.json")),
        jsonSettings
      );

      _jobDB.AddPrograms(
        new[]
        {
          new NewProgramContent()
          {
            ProgramName = "prog-aaa-1",
            Revision = 7,
            ProgramContent = "prog-aaa-1 content rev 7"
          },
          new NewProgramContent()
          {
            ProgramName = "prog-bbb-1",
            Revision = 3,
            ProgramContent = "prog-bbb-1 content rev 3"
          }
        },
        newJobs.Jobs.First().RouteStartUTC
      );

      _jobDB.AddJobs(newJobs, expectedPreviousScheduleId: null, addAsCopiedToSystem: false);
      WriteJobs.SyncFromDatabase(
        _initialAllData,
        _jobDB,
        _writeMock,
        _readMock,
        _settings,
        _mazakCfg,
        newJobs.Jobs.First().RouteStartUTC
      );

      ShouldMatchSnapshot(_writeMock.UpdateSchedules, "fixtures-queues-updatesch.json");
      ShouldMatchSnapshot(_writeMock.DeleteParts, "fixtures-queues-delparts.json");
      ShouldMatchSnapshot(_writeMock.AddFixtures, "managed-progs-add-fixtures.json");
      ShouldMatchSnapshot(_writeMock.DelFixtures, "managed-progs-del-fixtures.json");
      ShouldMatchSnapshot(_writeMock.AddParts, "managed-progs-parts.json");
      ShouldMatchSnapshot(_writeMock.AddSchedules, "fixtures-queues-schedules.json");
    }

    [Fact]
    public void OnlyDownloadsOneScheduleAtATime()
    {
      var newJ1 = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
        jsonSettings
      );
      _jobDB.AddJobs(newJ1, expectedPreviousScheduleId: null, addAsCopiedToSystem: false);

      var newJ2 = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "singleproc.json")),
        jsonSettings
      ) with
      {
        ScheduleId = "zzzzzzzzzzzzz"
      };
      _jobDB.AddJobs(newJ2, expectedPreviousScheduleId: newJ1.ScheduleId, addAsCopiedToSystem: false);

      WriteJobs
        .SyncFromDatabase(
          _initialAllData,
          _jobDB,
          _writeMock,
          _readMock,
          _settings,
          _mazakCfg,
          fixtureQueueTime
        )
        .Should()
        .BeTrue();

      ShouldMatchSnapshot(_writeMock.UpdateSchedules, "fixtures-queues-updatesch.json");
      ShouldMatchSnapshot(_writeMock.DeleteParts, "fixtures-queues-delparts.json");
      _writeMock.DeletePallets.Pallets.Should().BeEmpty();
      ShouldMatchSnapshot(_writeMock.AddFixtures, "fixtures-queues-add-fixtures.json");
      ShouldMatchSnapshot(_writeMock.DelFixtures, "fixtures-queues-del-fixtures.json");
      ShouldMatchSnapshot(_writeMock.AddParts, "fixtures-queues-parts.json");
      ShouldMatchSnapshot(_writeMock.AddSchedules, "fixtures-queues-schedules.json");
    }

    [Fact]
    public void ErrorDuringPartsPallets()
    {
      _writeMock.errorForPrefix = "Add Parts";

      var newJobs = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
        jsonSettings
      );
      _jobDB.AddJobs(newJobs, expectedPreviousScheduleId: null, addAsCopiedToSystem: false);

      FluentActions
        .Invoking(
          () =>
            WriteJobs.SyncFromDatabase(
              _initialAllData,
              _jobDB,
              _writeMock,
              _readMock,
              _settings,
              _mazakCfg,
              fixtureQueueTime
            )
        )
        .Should()
        .Throw<Exception>()
        .WithMessage("Sample error");

      ShouldMatchSnapshot(_writeMock.UpdateSchedules, "fixtures-queues-updatesch.json");
      ShouldMatchSnapshot(_writeMock.DeleteParts, "fixtures-queues-delparts.json");
      ShouldMatchSnapshot(_writeMock.AddFixtures, "fixtures-queues-add-fixtures.json");
      ShouldMatchSnapshot(_writeMock.DelFixtures, "fixtures-queues-del-fixtures.json");
      ShouldMatchSnapshot(_writeMock.AddParts, "fixtures-queues-parts.json");
      _writeMock.AddSchedules.Should().BeNull();

      var start = newJobs.Jobs.First().RouteStartUTC;
      _jobDB
        .LoadJobsNotCopiedToSystem(start, start.AddMinutes(1))
        .Select(j => j.UniqueStr)
        .Should()
        .BeEquivalentTo(["aaa-schId1234", "bbb-schId1234", "ccc-schId1234"]);
    }

    [Fact]
    public void ErrorDuringSchedule()
    {
      _writeMock.errorForPrefix = "Add Schedules";

      var newJobs = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
        jsonSettings
      );
      _jobDB.AddJobs(newJobs, expectedPreviousScheduleId: null, addAsCopiedToSystem: false);

      FluentActions
        .Invoking(
          () =>
            WriteJobs.SyncFromDatabase(
              _initialAllData,
              _jobDB,
              _writeMock,
              _readMock,
              _settings,
              _mazakCfg,
              fixtureQueueTime
            )
        )
        .Should()
        .Throw<Exception>()
        .WithMessage("Sample error");

      ShouldMatchSnapshot(_writeMock.UpdateSchedules, "fixtures-queues-updatesch.json");
      ShouldMatchSnapshot(_writeMock.DeleteParts, "fixtures-queues-delparts.json");
      ShouldMatchSnapshot(_writeMock.AddFixtures, "fixtures-queues-add-fixtures.json");
      ShouldMatchSnapshot(_writeMock.DelFixtures, "fixtures-queues-del-fixtures.json");
      ShouldMatchSnapshot(_writeMock.AddParts, "fixtures-queues-parts.json");
      ShouldMatchSnapshot(_writeMock.AddSchedules, "fixtures-queues-schedules.json");

      var start = newJobs.Jobs.First().RouteStartUTC;
      _jobDB
        .LoadJobsNotCopiedToSystem(start, start.AddMinutes(1))
        .Select(j => j.UniqueStr)
        .Should()
        .BeEquivalentTo(newJobs.Jobs.Select(j => j.UniqueStr));

      //try again still with error
      _writeMock.AddSchedules = null;
      FluentActions
        .Invoking(
          () =>
            WriteJobs.SyncFromDatabase(
              _initialAllData,
              _jobDB,
              _writeMock,
              _readMock,
              _settings,
              _mazakCfg,
              fixtureQueueTime
            )
        )
        .Should()
        .Throw<Exception>()
        .WithMessage("Sample error");

      ShouldMatchSnapshot(_writeMock.AddSchedules, "fixtures-queues-schedules.json");
      _jobDB
        .LoadJobsNotCopiedToSystem(start, start.AddMinutes(1))
        .Select(j => j.UniqueStr)
        .Should()
        .BeEquivalentTo(newJobs.Jobs.Select(j => j.UniqueStr));

      //finally succeed without error
      _writeMock.errorForPrefix = null;
      WriteJobs.SyncFromDatabase(
        _initialAllData,
        _jobDB,
        _writeMock,
        _readMock,
        _settings,
        _mazakCfg,
        fixtureQueueTime
      );
      ShouldMatchSnapshot(_writeMock.AddSchedules, "fixtures-queues-schedules.json");

      _jobDB.LoadJobsNotCopiedToSystem(start, start.AddMinutes(1)).Should().BeEmpty();
    }

    [Fact]
    public void ResumesADownloadInterruptedDuringSchedules()
    {
      _writeMock.errorForPrefix = "Add Schedules";

      var newJobs = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
        jsonSettings
      );
      _jobDB.AddJobs(newJobs, expectedPreviousScheduleId: null, addAsCopiedToSystem: false);

      FluentActions
        .Invoking(
          () =>
            WriteJobs.SyncFromDatabase(
              _initialAllData,
              _jobDB,
              _writeMock,
              _readMock,
              _settings,
              _mazakCfg,
              fixtureQueueTime
            )
        )
        .Should()
        .Throw<Exception>()
        .WithMessage("Sample error");

      // Now with the parts and only the aaa schedule
      var allParts = _writeMock.AddParts.Parts.ToList();
      var aaaSch = _writeMock.AddSchedules.Schedules.Where(s => s.PartName.StartsWith("aaa")).ToList();
      var bbbAndCCCSch = _writeMock.AddSchedules.Schedules.Where(s => !s.PartName.StartsWith("aaa")).ToList();
      _writeMock = new WriteMock();

      WriteJobs.SyncFromDatabase(
        new MazakAllData()
        {
          Schedules = aaaSch,
          Parts = allParts,
          Pallets = [],
          Fixtures = [],
        },
        _jobDB,
        _writeMock,
        _readMock,
        _settings,
        _mazakCfg,
        fixtureQueueTime
      );

      _writeMock.AddFixtures.Should().BeNull();
      _writeMock.AddParts.Should().BeNull();
      _writeMock.DelFixtures.Should().BeNull();
      _writeMock.DeleteParts.Should().BeNull();
      _writeMock.DeletePallets.Should().BeNull();
      _writeMock.UpdateSchedules.Should().BeNull();
      // adds only bbb and ccc
      _writeMock
        .AddSchedules.Schedules.Should()
        .BeEquivalentTo(bbbAndCCCSch.Select(s => s with { Priority = s.Priority + 1 }));

      _jobDB
        .LoadJobsNotCopiedToSystem(
          newJobs.Jobs.First().RouteStartUTC,
          newJobs.Jobs.First().RouteStartUTC.AddMinutes(1)
        )
        .Should()
        .BeEmpty();
    }

    [Fact]
    public void SplitsWrites()
    {
      //Arrange
      var rng = new Random();

      int cnt = rng.Next(15, 25);
      var schs = new List<MazakScheduleRow>();
      for (int i = 0; i < cnt; i++)
      {
        schs.Add(new MazakScheduleRow() { Id = i });
      }

      cnt = rng.Next(15, 25);
      var parts = new List<MazakPartRow>();
      for (int i = 0; i < cnt; i++)
      {
        parts.Add(new MazakPartRow() { PartName = "Part" + i.ToString() });
      }

      cnt = rng.Next(15, 25);
      var pals = new List<MazakPalletRow>();
      for (int i = 0; i < cnt; i++)
      {
        pals.Add(new MazakPalletRow() { PalletNumber = i });
      }

      cnt = rng.Next(15, 25);
      var fixtures = new List<MazakFixtureRow>();
      for (int i = 0; i < cnt; i++)
      {
        fixtures.Add(new MazakFixtureRow() { FixtureName = "fix" + i.ToString() });
      }

      cnt = rng.Next(15, 25);
      var progs = new List<NewMazakProgram>();
      for (int i = 0; i < cnt; i++)
      {
        progs.Add(new NewMazakProgram() { ProgramName = "prog " + i.ToString() });
      }

      var orig = new MazakWriteData()
      {
        Schedules = schs,
        Parts = parts,
        Pallets = pals,
        Fixtures = fixtures,
        Programs = progs
      };

      //act
      var chunks = OpenDatabaseKitTransactionDB.SplitWriteData(orig);

      // check

      chunks.SelectMany(c => c.Schedules).Should().BeEquivalentTo(orig.Schedules);
      chunks.SelectMany(c => c.Parts).Should().BeEquivalentTo(orig.Parts);
      chunks.SelectMany(c => c.Pallets).Should().BeEquivalentTo(orig.Pallets);
      chunks.SelectMany(c => c.Fixtures).Should().BeEquivalentTo(orig.Fixtures);
      chunks.SelectMany(c => c.Programs).Should().BeEquivalentTo(orig.Programs);

      foreach (var chunk in chunks)
      {
        (
          chunk.Schedules.Count
          + chunk.Parts.Count
          + chunk.Pallets.Count
          + chunk.Fixtures.Count
          + chunk.Programs.Count
        )
          .Should()
          .BeLessOrEqualTo(20);
      }
    }

    [Fact]
    public void CorrectPriorityForPalletSubsets()
    {
      // If the starting times are equal but there is a pallet subset, the priorty
      // should be split

      var newJobs = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "pallet-subset.json")),
        jsonSettings
      );

      _jobDB.AddJobs(newJobs, expectedPreviousScheduleId: null, addAsCopiedToSystem: false);
      WriteJobs
        .SyncFromDatabase(
          _initialAllData,
          _jobDB,
          _writeMock,
          _readMock,
          _settings,
          _mazakCfg,
          new DateTime(2024, 6, 18, 22, 0, 0, DateTimeKind.Utc)
        )
        .Should()
        .BeTrue();

      ShouldMatchSnapshot(_writeMock.UpdateSchedules, "pallet-subset-updatesch.json");
      ShouldMatchSnapshot(_writeMock.DeleteParts, "pallet-subset-delparts.json");
      _writeMock.DeletePallets.Pallets.Should().BeEmpty();
      ShouldMatchSnapshot(_writeMock.AddFixtures, "pallet-subset-add-fixtures.json");
      ShouldMatchSnapshot(_writeMock.DelFixtures, "pallet-subset-del-fixtures.json");
      ShouldMatchSnapshot(_writeMock.AddParts, "pallet-subset-parts.json");
      ShouldMatchSnapshot(_writeMock.AddSchedules, "pallet-subset-schedules.json");

      // The schedule snapshot should have checked this, but do it here too since it was the
      // bug which triggered this test case
      _writeMock.AddSchedules.Schedules.DistinctBy(p => p.Priority).Should().HaveCount(2);
    }
  }
}
