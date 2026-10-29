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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.FMSInsight.Tests;
using BlackMaple.MachineFramework;
using MazakMachineInterface;
using NSubstitute;
using Shouldly;

namespace BlackMaple.FMSInsight.Mazak.Tests
{
  public class DecrementSpec : IDisposable
  {
    private RepositoryConfig _repoCfg;

    private IMazakDB _write;

    private IList<MazakScheduleRow> GetSchRows()
    {
      var wr =
        _write
          .ReceivedCalls()
          .LastOrDefault(c => c.GetMethodInfo().Name == "Save")
          ?.GetArguments()[0] as MazakWriteData;

      if (wr != null)
      {
        wr.Pallets.ShouldBeEmpty();
        wr.Parts.ShouldBeEmpty();
        wr.Fixtures.ShouldBeEmpty();
      }

      return wr?.Schedules;
    }

    public DecrementSpec()
    {
      _repoCfg = RepositoryConfig.InitializeMemoryDB(
        new SerialSettings() { ConvertMaterialIDToSerial = (id) => id.ToString() },
        Guid.NewGuid()
      );

      _write = Substitute.For<IMazakDB>();
    }

    public void Dispose()
    {
      _repoCfg.Dispose();
    }

    [Test]
    public void SinglePathSingleProc()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      // plan 50, completed 30, 5 in proc and 15 not yet started
      var st = new MazakCurrentStatus()
      {
        Schedules = new[]
        {
          new MazakScheduleRow()
          {
            Id = 15,
            Comment = "uuuu-Insight",
            PartName = "pppp:1",
            PlanQuantity = 50,
            CompleteQuantity = 30,
            Processes = new List<MazakScheduleProcessRow>
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 15,
                FixQuantity = 1,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 15,
                ProcessExecuteQuantity = 5,
              },
            },
          },
        },
      };

      var j = new Job
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Cycles = 50,
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
        ),
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
      };
      _jobDB.AddJobs(
        new NewJobs() { Jobs = ImmutableList.Create(j), ScheduleId = "sch222" },
        null,
        addAsCopiedToSystem: true
      );

      var now = DateTime.UtcNow;
      DecrementPlanQty.Decrement(_write, _jobDB, st, now);

      var schR = GetSchRows();
      schR.Count.ShouldBe(1);
      var sch = schR[0];
      sch.Id.ShouldBe(15);
      sch.PlanQuantity.ShouldBe(35);
      sch.Processes.ShouldBe(
        new[]
        {
          new MazakScheduleProcessRow()
          {
            MazakScheduleRowId = 15,
            FixQuantity = 1,
            ProcessNumber = 1,
            ProcessMaterialQuantity = 15,
            ProcessExecuteQuantity = 5,
          },
        }
      );

      _jobDB
        .LoadDecrementsForJob("uuuu")
        .ShouldBe(
          new[]
          {
            new DecrementQuantity()
            {
              DecrementId = 0,
              TimeUTC = now,
              Quantity = 50 - 35,
            },
          }
        );
    }

    [Test]
    public void SplitSchedulesDecrementAsSingleJob()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      var st = new MazakCurrentStatus()
      {
        Schedules = new[]
        {
          new MazakScheduleRow()
          {
            Id = 15,
            Comment = "uuuu-1-1-InsightS",
            PartName = "pppp:1",
            PlanQuantity = 50,
            CompleteQuantity = 3,
            Processes = new List<MazakScheduleProcessRow>
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 15,
                FixQuantity = 1,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 0,
                ProcessExecuteQuantity = 0,
              },
            },
          },
          new MazakScheduleRow()
          {
            Id = 16,
            Comment = "uuuu-2-1-InsightS",
            PartName = "pppp:2",
            PlanQuantity = 50,
            CompleteQuantity = 2,
            Processes = new List<MazakScheduleProcessRow>
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 16,
                FixQuantity = 1,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 1,
                ProcessExecuteQuantity = 0,
              },
            },
          },
        },
        LoadActions = new[]
        {
          new LoadAction()
          {
            Comment = "uuuu-2-1-InsightS",
            LoadEvent = true,
            LoadStation = 1,
            Part = "pppp",
            Process = 1,
            Qty = 4,
          },
        },
      };

      var job = new Job
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Cycles = 50,
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) },
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
        ),
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
      };
      _jobDB.AddJobs(
        new NewJobs() { Jobs = ImmutableList.Create(job), ScheduleId = "splitDecrement" },
        null,
        addAsCopiedToSystem: true
      );

      var now = DateTime.UtcNow;
      DecrementPlanQty.Decrement(_write, _jobDB, st, now);

      var schRows = GetSchRows();
      schRows.Count.ShouldBe(2);
      schRows.Select(s => (s.Id, s.PlanQuantity)).OrderBy(s => s.Id).ShouldBe([(15, 3), (16, 3)]);

      _jobDB
        .LoadDecrementsForJob("uuuu")
        .ShouldBe(
          new[]
          {
            new DecrementQuantity()
            {
              DecrementId = 0,
              TimeUTC = now,
              Quantity = 47,
            },
          }
        );
    }

    [Test]
    public void SplitSchedulesUseExistingPlanQtyWhenProc1ScheduleMissing()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      var st = new MazakCurrentStatus()
      {
        Schedules = new[]
        {
          new MazakScheduleRow()
          {
            Id = 16,
            Comment = "uuuu-2-1-InsightS",
            PartName = "pppp:2",
            PlanQuantity = 30,
            CompleteQuantity = 10,
            Processes = new List<MazakScheduleProcessRow>
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 16,
                FixQuantity = 1,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 1,
                ProcessExecuteQuantity = 0,
              },
            },
          },
        },
      };

      var job = new Job
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Cycles = 50,
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) },
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
        ),
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
      };
      _jobDB.AddJobs(
        new NewJobs()
        {
          Jobs = ImmutableList.Create(job),
          ScheduleId = "splitDecrementMissingProc1",
        },
        null,
        addAsCopiedToSystem: true
      );

      var now = DateTime.UtcNow;
      DecrementPlanQty.Decrement(_write, _jobDB, st, now);

      GetSchRows().ShouldBeNull();

      _jobDB
        .LoadDecrementsForJob("uuuu")
        .ShouldBe(
          new[]
          {
            new DecrementQuantity()
            {
              DecrementId = 0,
              TimeUTC = now,
              Quantity = 20,
            },
          }
        );
    }

    [Test]
    public void IgnoresManualSchedule()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      // plan 50, completed 30, 5 in proc and 15 not yet started
      var st = new MazakCurrentStatus()
      {
        Schedules = new[]
        {
          new MazakScheduleRow()
          {
            Id = 15,
            Comment = "uuuu-Insight",
            PartName = "pppp:1",
            PlanQuantity = 50,
            CompleteQuantity = 30,
            Processes = new List<MazakScheduleProcessRow>
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 15,
                FixQuantity = 1,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 15,
                ProcessExecuteQuantity = 5,
              },
            },
          },
        },
      };

      var j = new Job
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Cycles = 50,
        ManuallyCreated = true,
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
        ),
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
      };
      _jobDB.AddJobs(
        new NewJobs() { Jobs = ImmutableList.Create(j), ScheduleId = "anschedule" },
        null,
        addAsCopiedToSystem: true
      );

      DecrementPlanQty.Decrement(_write, _jobDB, st);

      GetSchRows().ShouldBeNull();
      _jobDB.LoadDecrementsForJob("uuuu").ShouldBeEmpty();
    }

    [Test]
    public void IgnoresAlreadyExistingDecrement()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      // plan 50, completed 30, 5 in proc and 15 not yet started
      var st = new MazakCurrentStatus()
      {
        Schedules = new[]
        {
          new MazakScheduleRow()
          {
            Id = 15,
            Comment = "uuuu-Insight",
            PartName = "pppp:1",
            PlanQuantity = 50,
            CompleteQuantity = 30,
            Processes = new List<MazakScheduleProcessRow>
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 15,
                FixQuantity = 1,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 15,
                ProcessExecuteQuantity = 5,
              },
            },
          },
        },
      };

      var j = new Job
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Cycles = 50,
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
        ),
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
      };
      _jobDB.AddJobs(
        new NewJobs() { Jobs = ImmutableList.Create(j), ScheduleId = "sch56666" },
        null,
        addAsCopiedToSystem: true
      );

      var now = DateTime.UtcNow.AddHours(-1);
      _jobDB.AddNewDecrement(
        new[]
        {
          new NewDecrementQuantity()
          {
            JobUnique = "uuuu",
            Part = "pppp",
            Quantity = 3,
          },
        },
        now
      );

      DecrementPlanQty.Decrement(_write, _jobDB, st);

      GetSchRows().ShouldBeNull();
      _jobDB
        .LoadDecrementsForJob("uuuu")
        .ShouldBe(
          new[]
          {
            new DecrementQuantity()
            {
              DecrementId = 0,
              TimeUTC = now,
              Quantity = 3,
            },
          }
        );
    }

    [Test]
    public void LoadInProcess()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      // plan 50, completed 30, 5 in proc and 15 not yet started.  BUT, one is being loaded at the load station
      var st = new MazakCurrentStatus()
      {
        Schedules = new[]
        {
          new MazakScheduleRow()
          {
            Id = 15,
            Comment = "uuuu-Insight",
            PartName = "pppp:1",
            PlanQuantity = 50,
            CompleteQuantity = 30,
            Processes = new List<MazakScheduleProcessRow>
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 15,
                FixQuantity = 1,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 15,
                ProcessExecuteQuantity = 5,
              },
            },
          },
        },
        LoadActions = new[]
        {
          new LoadAction()
          {
            LoadStation = 1,
            LoadEvent = true, // load
            Comment = "uuuu-Insight",
            Part = "pppp",
            Process = 1,
            Qty = 1,
          },
          new LoadAction()
          {
            LoadStation = 1,
            LoadEvent = false, // unload, should be ignored
            Comment = "uuuu-Insight",
            Part = "pppp",
            Process = 1,
            Qty = 1,
          },
          new LoadAction()
          {
            LoadStation = 2,
            LoadEvent = true, // load of different part
            Comment = "uuuu2-Insight",
            Part = "pppp",
            Process = 1,
            Qty = 1,
          },
        },
      };

      var j = new Job
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Cycles = 50,
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
        ),
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
      };
      _jobDB.AddJobs(
        new NewJobs() { Jobs = ImmutableList.Create(j), ScheduleId = "sch3333" },
        null,
        addAsCopiedToSystem: true
      );

      var now = DateTime.UtcNow;
      DecrementPlanQty.Decrement(_write, _jobDB, st, now);

      var schR = GetSchRows();
      schR.Count.ShouldBe(1);
      schR[0].PlanQuantity.ShouldBe(36);

      _jobDB
        .LoadDecrementsForJob("uuuu")
        .ShouldBe(
          new[]
          {
            new DecrementQuantity()
            {
              DecrementId = 0,
              TimeUTC = now,
              Quantity = 50 - 36,
            },
          }
        );
    }

    [Test]
    public void ContinuePreviousDecrement()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      // plan 50, completed 30, 5 in proc and 15 not yet started
      // a previous decrement already reduced the plan quantity to 35
      var st = new MazakCurrentStatus()
      {
        Schedules = new[]
        {
          new MazakScheduleRow()
          {
            Id = 15,
            Comment = "uuuu-Insight",
            PartName = "pppp:1",
            PlanQuantity = 35,
            CompleteQuantity = 30,
            Processes = new List<MazakScheduleProcessRow>
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 15,
                FixQuantity = 1,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 15,
                ProcessExecuteQuantity = 5,
              },
            },
          },
        },
      };

      var j = new Job
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Cycles = 50,
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
        ),
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
      };
      _jobDB.AddJobs(
        new NewJobs() { Jobs = ImmutableList.Create(j), ScheduleId = "aschId" },
        null,
        addAsCopiedToSystem: true
      );

      var now = DateTime.UtcNow;
      DecrementPlanQty.Decrement(_write, _jobDB, st, now);

      GetSchRows().ShouldBeNull();

      _jobDB
        .LoadDecrementsForJob("uuuu")
        .ShouldBe(
          new[]
          {
            new DecrementQuantity()
            {
              DecrementId = 0,
              TimeUTC = now,
              Quantity = 50 - 35,
            },
          }
        );
    }

    [Test]
    public async Task RefusesPendingJobsUntilDownloadIsConfirmed()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      // uuuu plan 50, completed 30, 5 in proc and 15 not yet started
      // vvvv not copied so not returned from LoadSchedulesAndLoadActions
      var st = new MazakCurrentStatus()
      {
        Schedules = new[]
        {
          new MazakScheduleRow()
          {
            Id = 15,
            Comment = "uuuu-Insight",
            PartName = "pppp:1",
            PlanQuantity = 50,
            CompleteQuantity = 30,
            Processes = new List<MazakScheduleProcessRow>
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 15,
                FixQuantity = 1,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 15,
                ProcessExecuteQuantity = 5,
              },
            },
          },
        },
      };

      var now = DateTime.UtcNow;

      var uuuu = new Job()
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Cycles = 50,
        RouteStartUTC = now.AddHours(-12),
        RouteEndUTC = now.AddHours(12),
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
        ),
        Archived = false,
      };
      var vvvv = new Job()
      {
        UniqueStr = "vvvv",
        PartName = "oooo",
        Cycles = 4 + 7,
        RouteStartUTC = now.AddHours(-12),
        RouteEndUTC = now.AddHours(12),
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
        ),
        Archived = false,
      };
      _jobDB.AddJobs(
        new NewJobs() { Jobs = ImmutableList.Create(uuuu, vvvv), ScheduleId = "sch44444" },
        null,
        addAsCopiedToSystem: false
      );
      _jobDB.MarkJobCopiedToSystem("uuuu");

      _jobDB
        .LoadJobsNotCopiedToSystem(now.AddHours(-12), now.AddHours(12), includeDecremented: false)
        .Select(j => j.UniqueStr)
        .ShouldBe(new[] { "vvvv" });

      await Assert
        .That(() => DecrementPlanQty.Decrement(_write, _jobDB, st, now))
        .Throws<ConflictRequestException>();
      _write.DidNotReceive().Save(Arg.Any<MazakWriteData>());
      await Assert.That(_jobDB.LoadDecrementQuantitiesAfter(-1).Count).IsEqualTo(0);

      // Ordinary synchronization must finish the download before this operation can proceed.
      _jobDB.MarkJobCopiedToSystem("vvvv");
      st = st with
      {
        Schedules = st
          .Schedules.Append(
            new MazakScheduleRow
            {
              Id = 16,
              Comment = "vvvv-Insight",
              PartName = "oooo:1",
              PlanQuantity = 11,
              Processes = [],
            }
          )
          .ToImmutableList(),
      };
      await Assert.That(DecrementPlanQty.Decrement(_write, _jobDB, st, now)).IsTrue();

      var schR = GetSchRows();
      schR.Count.ShouldBe(2);
      var sch = schR[0];
      sch.Id.ShouldBe(15);
      sch.PlanQuantity.ShouldBe(35);
      sch.Processes.ShouldBe(
        new List<MazakScheduleProcessRow>
        {
          new MazakScheduleProcessRow()
          {
            MazakScheduleRowId = 15,
            FixQuantity = 1,
            ProcessNumber = 1,
            ProcessMaterialQuantity = 15,
            ProcessExecuteQuantity = 5,
          },
        }
      );

      _jobDB
        .LoadDecrementsForJob("uuuu")
        .ShouldBe(
          new[]
          {
            new DecrementQuantity()
            {
              DecrementId = 0,
              TimeUTC = now,
              Quantity = 50 - 35,
            },
          }
        );
      _jobDB
        .LoadDecrementsForJob("vvvv")
        .ShouldBe(
          new[]
          {
            new DecrementQuantity()
            {
              DecrementId = 0,
              TimeUTC = now,
              Quantity = 4 + 7,
            },
          }
        );

      _jobDB
        .LoadDecrementQuantitiesAfter(now.AddHours(-12))
        .ShouldBe(
          new[]
          {
            new JobAndDecrementQuantity()
            {
              DecrementId = 0,
              JobUnique = "uuuu",
              Part = "pppp",
              Quantity = 50 - 35,
              TimeUTC = now,
            },
            new JobAndDecrementQuantity()
            {
              DecrementId = 0,
              JobUnique = "vvvv",
              Part = "oooo",
              Quantity = 4 + 7,
              TimeUTC = now,
            },
          }
        );

      _jobDB
        .LoadJobsNotCopiedToSystem(now.AddHours(-12), now.AddHours(12), includeDecremented: false)
        .ShouldBeEmpty();
    }

    private static Job PendingJob(string unique) =>
      new()
      {
        UniqueStr = unique,
        PartName = "part",
        Cycles = 3,
        RouteStartUTC = DateTime.UtcNow.AddHours(-1),
        RouteEndUTC = DateTime.UtcNow.AddHours(1),
        Processes = [new ProcessInfo { Paths = [JobLogTest.EmptyPath] }],
        Archived = false,
      };

    private static MazakScheduleRow Schedule(string unique, int committed = 0) =>
      new()
      {
        Id = 1,
        Comment = unique + "-Insight",
        PartName = "part:1",
        PlanQuantity = 3,
        Processes =
        [
          new MazakScheduleProcessRow
          {
            MazakScheduleRowId = 1,
            ProcessNumber = 1,
            ProcessExecuteQuantity = committed,
          },
        ],
      };

    [Test]
    [Arguments(false, 0, -30)]
    [Arguments(false, 0, 0)]
    [Arguments(false, 0, 30)]
    [Arguments(true, 0, 0)]
    [Arguments(true, 2, 0)]
    [Arguments(true, 3, 0)]
    public async Task RefusesUnconfirmedDownloadsBeforeAnyWrite(
      bool installed,
      int committed,
      int routeDaysFromNow
    )
    {
      using var db = _repoCfg.OpenConnection();
      var job = PendingJob("unconfirmed") with
      {
        RouteStartUTC = DateTime.UtcNow.AddDays(routeDaysFromNow).AddHours(-1),
        RouteEndUTC = DateTime.UtcNow.AddDays(routeDaysFromNow).AddHours(1),
      };
      db.AddJobs(new NewJobs { ScheduleId = "schedule", Jobs = [job] }, null, false);
      var state = new MazakCurrentStatus
      {
        Schedules = installed ? [Schedule(job.UniqueStr, committed)] : [],
      };

      await Assert
        .That(() => DecrementPlanQty.Decrement(_write, db, state))
        .Throws<ConflictRequestException>();
      _write.DidNotReceive().Save(Arg.Any<MazakWriteData>());
      await Assert.That(db.LoadDecrementQuantitiesAfter(-1).Count).IsEqualTo(0);
      await Assert.That(db.LoadJob(job.UniqueStr).CopiedToSystem).IsFalse();

      if (installed)
      {
        db.MarkJobCopiedToSystem(job.UniqueStr);
        await Assert.That(DecrementPlanQty.Decrement(_write, db, state)).IsTrue();
        await Assert
          .That(db.LoadDecrementsForJob(job.UniqueStr).Sum(d => d.Quantity))
          .IsEqualTo(3 - committed);
      }
    }

    [Test]
    [Arguments("manual")]
    [Arguments("archived")]
    [Arguments("decremented")]
    public async Task IneligibleUnconfirmedJobsDoNotBlockBackout(string excluded)
    {
      using var db = _repoCfg.OpenConnection();
      var ignored = PendingJob("ignored") with
      {
        ManuallyCreated = excluded == "manual",
        Archived = excluded == "archived",
      };
      db.AddJobs(
        new NewJobs { ScheduleId = "schedule", Jobs = [ignored, PendingJob("eligible")] },
        null,
        false
      );
      db.MarkJobCopiedToSystem("eligible");
      if (excluded == "decremented")
        db.AddNewDecrement([
          new NewDecrementQuantity
          {
            JobUnique = "ignored",
            Part = "part",
            Quantity = 3,
          },
        ]);
      var previous = db.LoadDecrementsForJob("ignored");
      var state = new MazakCurrentStatus
      {
        Schedules = [Schedule("ignored"), Schedule("eligible") with { Id = 2 }],
      };

      await Assert.That(DecrementPlanQty.Decrement(_write, db, state)).IsTrue();
      await Assert.That(GetSchRows().Select(s => s.Id)).IsEquivalentTo([2]);
      await Assert.That(db.LoadDecrementsForJob("ignored")).IsEquivalentTo(previous);
      await Assert.That(db.LoadDecrementsForJob("eligible").Single().Quantity).IsEqualTo(3);
    }

    [Test]
    public async Task RefusesBackoutUntilMazakEventsSettle()
    {
      using var db = _repoCfg.OpenConnection();
      db.AddJobs(new NewJobs { ScheduleId = "schedule", Jobs = [PendingJob("job")] }, null, true);
      using var sync = new MazakSync(
        _write,
        new FMSSettings(),
        new MazakConfig { DBType = MazakDbType.MazakSmooth }
      );
      var state = new MazakState
      {
        AllData = new MazakAllDataAndLogs { Schedules = [Schedule("job")] },
        CurrentStatus = new CurrentStatus
        {
          Revision = 0,
          TimeOfCurrentStatusUTC = DateTime.UtcNow,
          Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
          Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
          Material = [],
          Queues = ImmutableDictionary<string, QueueInfo>.Empty,
          Alarms = [],
        },
        StateUpdated = true,
        TimeUntilNextRefresh = TimeSpan.Zero,
        StoppedBecauseRecentLogEvent = true,
      };
      await Assert.That(() => sync.DecrementJobs(db, state)).Throws<ConflictRequestException>();
      _write.DidNotReceive().Save(Arg.Any<MazakWriteData>());
      await Assert.That(db.LoadDecrementQuantitiesAfter(-1).Count).IsEqualTo(0);

      await Assert
        .That(sync.DecrementJobs(db, state with { StoppedBecauseRecentLogEvent = false }))
        .IsTrue();
      await Assert.That(db.LoadDecrementsForJob("job").Single().Quantity).IsEqualTo(3);
    }
  }
}
