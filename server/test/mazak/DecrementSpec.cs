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
using BlackMaple.FMSInsight.Tests;
using BlackMaple.MachineFramework;
using MazakMachineInterface;
using NSubstitute;
using Shouldly;
using Xunit;

namespace BlackMaple.FMSInsight.Mazak.Tests
{
  public class DecrementSpec : IDisposable
  {
    private RepositoryConfig _repoCfg;

    private IMazakDB _write;

    private IList<MazakScheduleRow> GetSchRows()
    {
      var wr =
        _write.ReceivedCalls().LastOrDefault(c => c.GetMethodInfo().Name == "Save")?.GetArguments()[0]
        as MazakWriteData;

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
        new SerialSettings() { ConvertMaterialIDToSerial = (id) => id.ToString() }
      );

      _write = Substitute.For<IMazakDB>();
    }

    public void Dispose()
    {
      _repoCfg.Dispose();
    }

    [Fact]
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
            Comment = "uuuu-Path1-1", // old previous versions of Insight added path information into the comment, use here to test backwards compatbility
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public void IncludesNotCopiedJobs()
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

      DecrementPlanQty.Decrement(_write, _jobDB, st, now);

      var schR = GetSchRows();
      schR.Count.ShouldBe(1);
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
  }
}
