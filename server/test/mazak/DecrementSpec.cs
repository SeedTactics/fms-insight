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

namespace MachineWatchTest
{
  public class DecrementSpec : IDisposable
  {
    private JobDB _jobDB;
    private DecrementPlanQty _decr;

    private class WriteMock : IWriteData
    {
      public MazakDbType MazakType => MazakDbType.MazakSmooth;
      public IList<MazakScheduleRow> Schedules { get; private set; }
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
      _jobDB = JobDB.Config.InitializeSingleThreadedMemoryDB().OpenConnection();

      _write = new WriteMock();

      _read = Substitute.For<IReadDataAccess>();
      _read.MazakType.Returns(MazakDbType.MazakSmooth);

      _decr = new DecrementPlanQty(_write, _read);
    }
    public void Dispose()
    {
      _jobDB.Close();
    }

    [Fact]
    public void SinglePathSingleProc()
    {
      // plan 50, completed 30, 5 in proc and 15 not yet started
      _read.LoadSchedulesAndLoadActions().Returns(new MazakSchedulesAndLoadActions()
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

      var j = new JobPlan("uuuu", 1);
      j.PartName = "pppp";
      j.SetPlannedCyclesOnFirstProcess(path: 1, numCycles: 50);
      _jobDB.AddJobs(new NewJobs()
      {
        Jobs = new List<JobPlan> { j }
      }, null);

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
      _read.LoadSchedulesAndLoadActions().Returns(new MazakSchedulesAndLoadActions()
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

      var j = new JobPlan("uuuu", 1);
      j.PartName = "pppp";
      j.SetPlannedCyclesOnFirstProcess(path: 1, numCycles: 50);
      _jobDB.AddJobs(new NewJobs()
      {
        Jobs = new List<JobPlan> { j }
      }, null);

      _decr.Decrement(_jobDB);

      _write.Schedules.Should().BeNull();
      _jobDB.LoadDecrementsForJob("uuuu").Should().BeEmpty();
    }

    [Fact]
    public void IgnoresAlreadyExistingDecrement()
    {
      // plan 50, completed 30, 5 in proc and 15 not yet started
      _read.LoadSchedulesAndLoadActions().Returns(new MazakSchedulesAndLoadActions()
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

      var j = new JobPlan("uuuu", 1);
      j.PartName = "pppp";
      j.SetPlannedCyclesOnFirstProcess(path: 1, numCycles: 50);
      _jobDB.AddJobs(new NewJobs()
      {
        Jobs = new List<JobPlan> { j }
      }, null);

      var now = DateTime.UtcNow.AddHours(-1);
      _jobDB.AddNewDecrement(new[] {
        new JobDB.NewDecrementQuantity() {
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
      _read.LoadSchedulesAndLoadActions().Returns(new MazakSchedulesAndLoadActions()
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

      var j = new JobPlan("uuuu", 1);
      j.PartName = "pppp";
      j.SetPlannedCyclesOnFirstProcess(path: 1, numCycles: 50);
      _jobDB.AddJobs(new NewJobs()
      {
        Jobs = new List<JobPlan> { j }
      }, null);

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
      _read.LoadSchedulesAndLoadActions().Returns(new MazakSchedulesAndLoadActions()
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

      var j = new JobPlan("uuuu", 1);
      j.PartName = "pppp";
      j.SetPlannedCyclesOnFirstProcess(path: 1, numCycles: 50);
      _jobDB.AddJobs(new NewJobs()
      {
        Jobs = new List<JobPlan> { j }
      }, null);

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
    public void MultplePathsAndProcs()
    {
      // path 1: plan 50, complete 30, 5 in-proc #1, 3 in-proc #2, 2 material proc #2, 0 material proc1 (has input queue).  10 un-started parts
      // path 2: plan 25, complete 3, 2 in-proc #1, 4 in-proc #2, 3 material proc #2, 25 - 3 - 2 - 4 - 3 = 13 un-started parts
      _read.LoadSchedulesAndLoadActions().Returns(new MazakSchedulesAndLoadActions()
      {
        Schedules = new[] {
          new MazakScheduleRow()
          {
            Id = 15,
            Comment = MazakPart.CreateComment("uuuu", new[] {1, 2}, false),
            PartName = "pppp:1",
            PlanQuantity = 50,
            CompleteQuantity = 30,
            Processes = new List<MazakScheduleProcessRow> {
              new MazakScheduleProcessRow() {
                MazakScheduleRowId = 15,
                FixQuantity = 1,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 0,
                ProcessExecuteQuantity = 5
              },
              new MazakScheduleProcessRow() {
                MazakScheduleRowId = 15,
                FixQuantity = 1,
                ProcessNumber = 2,
                ProcessMaterialQuantity = 2,
                ProcessExecuteQuantity = 3
              }
            }
          },
          new MazakScheduleRow()
          {
            Id = 16,
            Comment = MazakPart.CreateComment("uuuu", new[] {2, 1}, false),
            PartName = "pppp:1",
            PlanQuantity = 25,
            CompleteQuantity = 3,
            Processes = new List<MazakScheduleProcessRow> {
              new MazakScheduleProcessRow() {
                MazakScheduleRowId = 16,
                FixQuantity = 1,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 0,
                ProcessExecuteQuantity = 2
              },
              new MazakScheduleProcessRow() {
                MazakScheduleRowId = 16,
                FixQuantity = 1,
                ProcessNumber = 2,
                ProcessMaterialQuantity = 3,
                ProcessExecuteQuantity = 4
              }
            }
          }
        }
      });

      var j = new JobPlan("uuuu", 2, new[] { 2, 2 });
      j.PartName = "pppp";
      j.SetPlannedCyclesOnFirstProcess(path: 1, numCycles: 50);
      j.SetPlannedCyclesOnFirstProcess(path: 2, numCycles: 25);
      j.SetPathGroup(process: 1, path: 1, pgroup: 1);
      j.SetPathGroup(process: 2, path: 2, pgroup: 1);
      j.SetPathGroup(process: 1, path: 2, pgroup: 2);
      j.SetPathGroup(process: 2, path: 1, pgroup: 2);
      j.SetInputQueue(process: 1, path: 1, queue: "castings");
      j.SetInputQueue(process: 1, path: 2, queue: "castings");
      _jobDB.AddJobs(new NewJobs()
      {
        Jobs = new List<JobPlan> { j }
      }, null);

      var now = DateTime.UtcNow;
      _decr.Decrement(_jobDB, now);

      _write.Schedules.Count.Should().Be(2);
      _write.Schedules[0].Id.Should().Be(15);
      _write.Schedules[0].PlanQuantity.Should().Be(50 - 10);
      _write.Schedules[1].Id.Should().Be(16);
      _write.Schedules[1].PlanQuantity.Should().Be(25 - 13);

      _jobDB.LoadDecrementsForJob("uuuu").Should().BeEquivalentTo(new[] {
        new DecrementQuantity() {
          DecrementId = 0,
          TimeUTC = now,
          Quantity = 10 + 13
        }
      });
    }

    [Fact]
    public void IncludesNotCopiedJobs()
    {
      // uuuu plan 50, completed 30, 5 in proc and 15 not yet started
      // vvvv not copied so not returned from LoadSchedulesAndLoadActions
      _read.LoadSchedulesAndLoadActions().Returns(new MazakSchedulesAndLoadActions()
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
      uuuu.SetPlannedCyclesOnFirstProcess(path: 1, numCycles: 50);
      uuuu.RouteStartingTimeUTC = now.AddHours(-12);
      uuuu.RouteEndingTimeUTC = now.AddHours(12);
      uuuu.JobCopiedToSystem = true;
      var vvvv = new JobPlan("vvvv", 1, new[] { 2 });
      vvvv.PartName = "oooo";
      vvvv.JobCopiedToSystem = false;
      vvvv.SetPlannedCyclesOnFirstProcess(path: 1, numCycles: 4);
      vvvv.SetPlannedCyclesOnFirstProcess(path: 2, numCycles: 7);
      vvvv.RouteStartingTimeUTC = now.AddHours(-12);
      vvvv.RouteEndingTimeUTC = now.AddHours(12);
      _jobDB.AddJobs(new NewJobs()
      {
        Jobs = new List<JobPlan> { uuuu, vvvv }
      }, null);

      _jobDB.LoadJobsNotCopiedToSystem(now.AddHours(-12), now.AddHours(12), includeDecremented: false).Jobs.Select(j => j.UniqueStr)
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
        }
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
        }
      });

      _jobDB.LoadJobsNotCopiedToSystem(now.AddHours(-12), now.AddHours(12), includeDecremented: false).Jobs
        .Should().BeEmpty();

    }

  }

}