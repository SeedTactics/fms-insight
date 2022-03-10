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
using System.Collections.Generic;
using Xunit;
using FluentAssertions;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;
using NSubstitute;
using MazakMachineInterface;
using System.Collections.Immutable;

namespace MachineWatchTest
{
  public class DecrementSpec : IDisposable
  {
    private RepositoryConfig _repoCfg;
    private IRepository _jobDB;
    private DecrementPlanQty _decr;

    private class WriteMock : IWriteData
    {
      public MazakDbType MazakType => MazakDbType.MazakSmooth;
      public IReadOnlyList<MazakScheduleRow> Schedules { get; private set; }
      public void Save(MazakWriteData data, string prefix)
      {
        Schedules = data.Schedules;
        data.Pallets.Should().BeEmpty();
        data.Parts.Should().BeEmpty();
        data.Fixtures.Should().BeEmpty();
      }
    }
    private WriteMock _write;
    private IReadDataAccess _read;

    public DecrementSpec()
    {
      _repoCfg = RepositoryConfig.InitializeSingleThreadedMemoryDB(new FMSSettings());
      _jobDB = _repoCfg.OpenConnection();

      _write = new WriteMock();

      _read = Substitute.For<IReadDataAccess>();
      _read.MazakType.Returns(MazakDbType.MazakSmooth);

      _decr = new DecrementPlanQty(_write, _read);
    }
    public void Dispose()
    {
      _repoCfg.CloseMemoryConnection();
    }

    [Fact]
    public void SinglePathSingleProc()
    {
      // plan 50, completed 30, 5 in proc and 15 not yet started
      _read.LoadStatusAndTools().Returns(new MazakCurrentStatusAndTools()
      {
        Schedules = new[] {
    new MazakScheduleRow()
    {
      Id = 15,
      Comment = MazakPart.CreateComment("uuuu", new[] {1}, false),
      PartName = "pppp:1",
      PlanQuantity = 50,
      CompleteQuantity = 30,
      Processes = new List<MazakScheduleProcessRow> {
        new MazakScheduleProcessRow() {
    MazakScheduleRowId = 15,
    FixQuantity = 1,
    ProcessNumber = 1,
    ProcessMaterialQuantity = 15,
    ProcessExecuteQuantity = 5
        }
      }
    }
  }
      });

      var j = new Job
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Cycles = 50,
        Processes = ImmutableList.Create(new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo()) })
      };
      _jobDB.AddJobs(new NewJobs()
      {
        Jobs = ImmutableList.Create(j)
      }, null, addAsCopiedToSystem: true);

      var now = DateTime.UtcNow;
      _decr.Decrement(_jobDB, now);

      _write.Schedules.Count.Should().Be(1);
      var sch = _write.Schedules[0];
      sch.Id.Should().Be(15);
      sch.PlanQuantity.Should().Be(35);
      sch.Processes.Should().BeEmpty();

      _jobDB.LoadDecrementsForJob("uuuu").Should().BeEquivalentTo(new[] {
  new DecrementQuantity() {
    DecrementId = 0,
    TimeUTC = now,
    Quantity = 50 - 35
  }
      });
    }

    [Fact]
    public void IgnoresManualSchedule()
    {
      // plan 50, completed 30, 5 in proc and 15 not yet started
      _read.LoadStatusAndTools().Returns(new MazakCurrentStatusAndTools()
      {
        Schedules = new[] {
    new MazakScheduleRow()
    {
      Id = 15,
      Comment = MazakPart.CreateComment("uuuu", new[] {1}, manual: true),
      PartName = "pppp:1",
      PlanQuantity = 50,
      CompleteQuantity = 30,
      Processes = new List<MazakScheduleProcessRow> {
        new MazakScheduleProcessRow() {
    MazakScheduleRowId = 15,
    FixQuantity = 1,
    ProcessNumber = 1,
    ProcessMaterialQuantity = 15,
    ProcessExecuteQuantity = 5
        }
      }
    }
  }
      });

      var j = new Job
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Cycles = 50,
        Processes = ImmutableList.Create(new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo()) })
      };
      _jobDB.AddJobs(new NewJobs()
      {
        Jobs = ImmutableList.Create(j)
      }, null, addAsCopiedToSystem: true);

      _decr.Decrement(_jobDB);

      _write.Schedules.Should().BeNull();
      _jobDB.LoadDecrementsForJob("uuuu").Should().BeEmpty();
    }

    [Fact]
    public void IgnoresAlreadyExistingDecrement()
    {
      // plan 50, completed 30, 5 in proc and 15 not yet started
      _read.LoadStatusAndTools().Returns(new MazakCurrentStatusAndTools()
      {
        Schedules = new[] {
    new MazakScheduleRow()
    {
      Id = 15,
      Comment = MazakPart.CreateComment("uuuu", new[] {1}, manual: false),
      PartName = "pppp:1",
      PlanQuantity = 50,
      CompleteQuantity = 30,
      Processes = new List<MazakScheduleProcessRow> {
        new MazakScheduleProcessRow() {
    MazakScheduleRowId = 15,
    FixQuantity = 1,
    ProcessNumber = 1,
    ProcessMaterialQuantity = 15,
    ProcessExecuteQuantity = 5
        }
      }
    }
  }
      });

      var j = new Job
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Cycles = 50,
        Processes = ImmutableList.Create(new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo()) })
      };
      _jobDB.AddJobs(new NewJobs()
      {
        Jobs = ImmutableList.Create(j)
      }, null, addAsCopiedToSystem: true);

      var now = DateTime.UtcNow.AddHours(-1);
      _jobDB.AddNewDecrement(new[] {
  new NewDecrementQuantity() {
    JobUnique = "uuuu", Part = "pppp", Quantity = 3
  }
      }, now);

      _decr.Decrement(_jobDB);

      _write.Schedules.Should().BeNull();
      _jobDB.LoadDecrementsForJob("uuuu").Should().BeEquivalentTo(new[] {
  new DecrementQuantity() {
    DecrementId = 0,
    TimeUTC = now,
    Quantity = 3
  }
      });
    }

    [Fact]
    public void LoadInProcess()
    {
      // plan 50, completed 30, 5 in proc and 15 not yet started.  BUT, one is being loaded at the load station
      _read.LoadStatusAndTools().Returns(new MazakCurrentStatusAndTools()
      {
        Schedules = new[] {
    new MazakScheduleRow()
    {
      Id = 15,
      Comment = MazakPart.CreateComment("uuuu", new[] {1}, false),
      PartName = "pppp:1",
      PlanQuantity = 50,
      CompleteQuantity = 30,
      Processes = new List<MazakScheduleProcessRow> {
        new MazakScheduleProcessRow() {
    MazakScheduleRowId = 15,
    FixQuantity = 1,
    ProcessNumber = 1,
    ProcessMaterialQuantity = 15,
    ProcessExecuteQuantity = 5
        }
      }
    }
  },
        LoadActions = new[] {
    new LoadAction() {
      LoadStation = 1,
      LoadEvent = true, // load
            Unique = "uuuu",
      Part = "pppp",
      Process = 1,
      Path = 1,
      Qty = 1
    },
    new LoadAction() {
      LoadStation = 1,
      LoadEvent = false, // unload, should be ignored
            Unique = "uuuu",
      Part = "pppp",
      Process = 1,
      Path = 1,
      Qty = 1
    },
    new LoadAction() {
      LoadStation = 2,
      LoadEvent = true, // load of different part
            Unique = "uuuu2",
      Part = "pppp",
      Process = 1,
      Path = 1,
      Qty = 1
    }
  }
      });

      var j = new Job
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Cycles = 50,
        Processes = ImmutableList.Create(new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo()) })
      };
      _jobDB.AddJobs(new NewJobs()
      {
        Jobs = ImmutableList.Create(j)
      }, null, addAsCopiedToSystem: true);

      var now = DateTime.UtcNow;
      _decr.Decrement(_jobDB, now);

      _write.Schedules.Count.Should().Be(1);
      _write.Schedules[0].PlanQuantity.Should().Be(36);

      _jobDB.LoadDecrementsForJob("uuuu").Should().BeEquivalentTo(new[] {
  new DecrementQuantity() {
    DecrementId = 0,
    TimeUTC = now,
    Quantity = 50 - 36
  }
      });
    }

    [Fact]
    public void ContinuePreviousDecrement()
    {
      // plan 50, completed 30, 5 in proc and 15 not yet started
      // a previous decrement already reduced the plan quantity to 35
      _read.LoadStatusAndTools().Returns(new MazakCurrentStatusAndTools()
      {
        Schedules = new[] {
    new MazakScheduleRow()
    {
      Id = 15,
      Comment = MazakPart.CreateComment("uuuu", new[] {1}, false),
      PartName = "pppp:1",
      PlanQuantity = 35,
      CompleteQuantity = 30,
      Processes = new List<MazakScheduleProcessRow> {
        new MazakScheduleProcessRow() {
    MazakScheduleRowId = 15,
    FixQuantity = 1,
    ProcessNumber = 1,
    ProcessMaterialQuantity = 15,
    ProcessExecuteQuantity = 5
        }
      }
    }
  }
      });

      var j = new Job
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Cycles = 50,
        Processes = ImmutableList.Create(new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo()) })
      };
      _jobDB.AddJobs(new NewJobs()
      {
        Jobs = ImmutableList.Create(j)
      }, null, addAsCopiedToSystem: true);

      var now = DateTime.UtcNow;
      _decr.Decrement(_jobDB, now);

      _write.Schedules.Should().BeNull();

      _jobDB.LoadDecrementsForJob("uuuu").Should().BeEquivalentTo(new[] {
  new DecrementQuantity() {
    DecrementId = 0,
    TimeUTC = now,
    Quantity = 50 - 35
  }
      });
    }

    [Fact]
    public void IncludesNotCopiedJobs()
    {
      // uuuu plan 50, completed 30, 5 in proc and 15 not yet started
      // vvvv not copied so not returned from LoadSchedulesAndLoadActions
      _read.LoadStatusAndTools().Returns(new MazakCurrentStatusAndTools()
      {
        Schedules = new[] {
    new MazakScheduleRow()
    {
      Id = 15,
      Comment = MazakPart.CreateComment("uuuu", new[] {1}, false),
      PartName = "pppp:1",
      PlanQuantity = 50,
      CompleteQuantity = 30,
      Processes = new List<MazakScheduleProcessRow> {
        new MazakScheduleProcessRow() {
    MazakScheduleRowId = 15,
    FixQuantity = 1,
    ProcessNumber = 1,
    ProcessMaterialQuantity = 15,
    ProcessExecuteQuantity = 5
        }
      }
    }
  }
      });

      var now = DateTime.UtcNow;

      var uuuu = new JobPlan("uuuu", 1);
      uuuu.PartName = "pppp";
      uuuu.Cycles = 50;
      uuuu.RouteStartingTimeUTC = now.AddHours(-12);
      uuuu.RouteEndingTimeUTC = now.AddHours(12);
      var vvvv = new JobPlan("vvvv", 1, new[] { 2 });
      vvvv.PartName = "oooo";
      vvvv.Cycles = 4 + 7;
      vvvv.RouteStartingTimeUTC = now.AddHours(-12);
      vvvv.RouteEndingTimeUTC = now.AddHours(12);
      _jobDB.AddJobs(new NewJobs()
      {
        Jobs = ImmutableList.Create((Job)uuuu.ToHistoricJob(), vvvv.ToHistoricJob())
      }, null, addAsCopiedToSystem: false);
      _jobDB.MarkJobCopiedToSystem("uuuu");

      _jobDB.LoadJobsNotCopiedToSystem(now.AddHours(-12), now.AddHours(12), includeDecremented: false).Select(j => j.UniqueStr)
        .Should().BeEquivalentTo(new[] { "vvvv" });

      _decr.Decrement(_jobDB, now);

      _write.Schedules.Count.Should().Be(1);
      var sch = _write.Schedules[0];
      sch.Id.Should().Be(15);
      sch.PlanQuantity.Should().Be(35);
      sch.Processes.Should().BeEmpty();

      _jobDB.LoadDecrementsForJob("uuuu").Should().BeEquivalentTo(new[] {
  new DecrementQuantity() {
    DecrementId = 0,
    TimeUTC = now,
    Quantity = 50 - 35
  }
      });
      _jobDB.LoadDecrementsForJob("vvvv").Should().BeEquivalentTo(new[] {
  new DecrementQuantity() {
    DecrementId = 0,
    TimeUTC = now,
    Quantity = 4 + 7
  },
      });

      _jobDB.LoadDecrementQuantitiesAfter(now.AddHours(-12)).Should().BeEquivalentTo(new[] {
  new JobAndDecrementQuantity() {
    DecrementId = 0,
    JobUnique = "uuuu",
    Part = "pppp",
    Quantity = 50 - 35,
    TimeUTC = now
  },
  new JobAndDecrementQuantity() {
    DecrementId = 0,
    JobUnique = "vvvv",
    Part = "oooo",
    Quantity = 4 + 7,
    TimeUTC = now
  },
      });

      _jobDB.LoadJobsNotCopiedToSystem(now.AddHours(-12), now.AddHours(12), includeDecremented: false)
        .Should().BeEmpty();

    }

  }

}