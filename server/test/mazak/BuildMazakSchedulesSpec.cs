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
using System.Collections.Generic;
using BlackMaple.MachineWatchInterface;
using BlackMaple.MachineFramework;
using MazakMachineInterface;
using Xunit;
using FluentAssertions;

namespace MachineWatchTest
{
  public class BuildMazakSchedulesSpec
  {
    [Fact]
    public void DeleteCompletedSchedules()
    {
      var schedules = new MazakSchedules()
      {
        Schedules = new[] {
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
          }
        }
      };

      var (actions, tokeep) = BuildMazakSchedules.RemoveCompletedSchedules(schedules);

      schedules.Schedules.First().Command = MazakWriteCommand.Delete;

      actions.Schedules.Should().BeEquivalentTo(new[] { schedules.Schedules.First() });
      actions.Parts.Should().BeEmpty();
      actions.Fixtures.Should().BeEmpty();
      actions.Pallets.Should().BeEmpty();

      tokeep.Should().BeEquivalentTo(new[] { "part2:1:1" });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddsSchedules(bool conflictByFixture)
    {
      //basic job
      var job1 = new JobPlan("uniq1", 2, new int[] { 2, 2 });
      job1.PartName = "part1";
      job1.RouteStartingTimeUTC = new DateTime(2020, 04, 14, 13, 43, 00, DateTimeKind.Local).ToUniversalTime();
      job1.SetPathGroup(1, 1, 1);
      job1.SetPathGroup(1, 2, 2);
      job1.SetPathGroup(2, 1, 1);
      job1.SetPathGroup(2, 2, 2);
      job1.SetSimulatedStartingTimeUTC(1, 1, new DateTime(2018, 1, 9, 8, 7, 6, DateTimeKind.Local).ToUniversalTime());
      job1.SetSimulatedStartingTimeUTC(1, 2, new DateTime(2018, 1, 2, 3, 4, 5, DateTimeKind.Local).ToUniversalTime());
      job1.SetPlannedCyclesOnFirstProcess(1, 51);
      job1.SetPlannedCyclesOnFirstProcess(2, 41);
      if (conflictByFixture)
      {
        job1.SetFixtureFace(1, 1, "fixA", 1); // paths conflict with each other
        job1.SetFixtureFace(1, 2, "fixA", 1);
        job1.SetFixtureFace(2, 1, "fixA", 2);
        job1.SetFixtureFace(2, 2, "fixA", 2);
      }
      else
      {
        job1.AddProcessOnPallet(1, 1, "palA"); // paths conflict with each other
        job1.AddProcessOnPallet(1, 2, "palA");
        job1.AddProcessOnPallet(2, 1, "palA");
        job1.AddProcessOnPallet(2, 2, "palA");
      }

      //one with an input queue
      var job2 = new JobPlan("uniq2", 2, new int[] { 2, 2 });
      job2.PartName = "part2";
      job2.RouteStartingTimeUTC = job1.RouteStartingTimeUTC;
      job2.SetPathGroup(1, 1, 1);
      job2.SetPathGroup(1, 2, 2);
      job2.SetPathGroup(2, 1, 1);
      job2.SetPathGroup(2, 2, 2);
      job2.SetSimulatedStartingTimeUTC(1, 1, new DateTime(2018, 2, 9, 8, 7, 6, DateTimeKind.Local).ToUniversalTime());
      job2.SetSimulatedStartingTimeUTC(1, 2, new DateTime(2018, 2, 2, 3, 4, 5, DateTimeKind.Local).ToUniversalTime());
      job2.SetPlannedCyclesOnFirstProcess(1, 12);
      job2.SetPlannedCyclesOnFirstProcess(2, 42);
      job2.SetInputQueue(1, 1, "aaa");
      job2.SetInputQueue(1, 2, "bbb");
      if (conflictByFixture)
      {
        job2.SetFixtureFace(1, 1, "fixA", 2); // conflicts with both job1 paths
        job2.SetFixtureFace(2, 2, "fixB", 2); // no conflicts
      }
      else
      {
        job2.AddProcessOnPallet(1, 1, "palA"); // conflict with both paths
        job2.AddProcessOnPallet(2, 2, "palB"); // no conflict
      }

      //two schedule which already exists, one with same route starting, one with different
      var job3 = new JobPlan("uniq3", 2, new int[] { 1, 1 });
      job3.PartName = "part3";
      job3.RouteStartingTimeUTC = job1.RouteStartingTimeUTC;
      job3.SetSimulatedStartingTimeUTC(1, 1, new DateTime(2018, 3, 9, 8, 7, 6, DateTimeKind.Local).ToUniversalTime());
      job3.SetPlannedCyclesOnFirstProcess(1, 23);

      var job4 = new JobPlan("uniq4", 2, new int[] { 1, 1 });
      job4.PartName = "part4";
      job4.RouteStartingTimeUTC = new DateTime(2020, 01, 01, 5, 2, 3, DateTimeKind.Utc);
      job4.SetSimulatedStartingTimeUTC(1, 1, new DateTime(2018, 3, 9, 8, 7, 7, DateTimeKind.Local).ToUniversalTime());
      job4.SetPlannedCyclesOnFirstProcess(1, 3);

      // all the parts, plus a schedule for uniq3
      var curData = new MazakSchedulesPartsPallets()
      {
        Parts = new[] {
          new MazakPartRow() {
            PartName = "part1:6:1",
            Comment = MazakPart.CreateComment("uniq1", new[] {1, 1}, false)
          },
          new MazakPartRow() {
            PartName = "part1:6:2",
            Comment = MazakPart.CreateComment("uniq1", new[] {2, 2}, false)
          },
          new MazakPartRow() {
            PartName = "part2:6:1",
            Comment = MazakPart.CreateComment("uniq2", new[] {1, 1}, false)
          },
          new MazakPartRow() {
            PartName = "part2:6:2",
            Comment = MazakPart.CreateComment("uniq2", new[] {2, 2}, false)
          },
          new MazakPartRow() {
            PartName = "part3:6:1",
            Comment = MazakPart.CreateComment("uniq3", new[] {1, 1}, false)
          }
        },
        Schedules = new[] {
          new MazakScheduleRow() {
            Id = 1,
            PartName = "part3:6:1",
            Comment = MazakPart.CreateComment("uniq3", new[] {1, 1}, false),
            DueDate = new DateTime(2020, 04, 14, 0, 0, 0, DateTimeKind.Local),
            Priority = 17
          },
          new MazakScheduleRow() {
            Id = 0,
            PartName = "part4:6:1",
            Comment = MazakPart.CreateComment("uniq4", new[] {1, 1}, false),
            DueDate = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Local),
            Priority = 50 // should be ignored since duedate is not the same
          }
        }
      };

      var actions = BuildMazakSchedules.AddSchedules(curData, new[] { job1, job2, job3 }, true);
      actions.Parts.Should().BeEmpty();
      actions.Fixtures.Should().BeEmpty();
      actions.Pallets.Should().BeEmpty();

      actions.Schedules.Should().BeEquivalentTo(new[]
      {
				//uniq1
				new MazakScheduleRow() {
          Command = MazakWriteCommand.Add,
          Id = 2,
          PartName = "part1:6:1",
          Comment = MazakPart.CreateComment("uniq1", new[] {1, 1}, false),
          PlanQuantity = 51,
          Priority = 18 + 1, // max existing is 17 so start at 18, plus one earlier conflict
          DueDate = new DateTime(2020, 4, 14, 0, 0, 0, DateTimeKind.Local),
          Processes = {
            new MazakScheduleProcessRow() {
              MazakScheduleRowId = 2,
              ProcessNumber = 1,
              ProcessMaterialQuantity = 51
            },
            new MazakScheduleProcessRow() {
              MazakScheduleRowId = 2,
              ProcessNumber = 2,
              ProcessMaterialQuantity = 0
            },
          }
        },
        new MazakScheduleRow() {
          Command = MazakWriteCommand.Add,
          Id = 3,
          PartName = "part1:6:2",
          Comment = MazakPart.CreateComment("uniq1", new[] {2, 2}, false),
          PlanQuantity = 41,
          Priority = 18, // max existing is 17 so start at 18
          DueDate = new DateTime(2020, 4, 14, 0, 0, 0, DateTimeKind.Local),
          Processes = {
            new MazakScheduleProcessRow() {
              MazakScheduleRowId = 3,
              ProcessNumber = 1,
              ProcessMaterialQuantity = 41
            },
            new MazakScheduleProcessRow() {
              MazakScheduleRowId = 3,
              ProcessNumber = 2,
              ProcessMaterialQuantity = 0
            },
          }
        },

				//uniq2
				new MazakScheduleRow() {
          Command = MazakWriteCommand.Add,
          Id = 4,
          PartName = "part2:6:1",
          Comment = MazakPart.CreateComment("uniq2", new[] {1, 1}, false),
          PlanQuantity = 12,
          Priority = 18 + 2, // conflicts with 2 earlier
          DueDate = new DateTime(2020, 4, 14, 0, 0, 0, DateTimeKind.Local),
          Processes = {
            new MazakScheduleProcessRow() {
              MazakScheduleRowId = 4,
              ProcessNumber = 1,
              ProcessMaterialQuantity = 0 // no material, input queue
						},
            new MazakScheduleProcessRow() {
              MazakScheduleRowId = 4,
              ProcessNumber = 2,
              ProcessMaterialQuantity = 0
            },
          }
        },
        new MazakScheduleRow() {
          Command = MazakWriteCommand.Add,
          Id = 5,
          PartName = "part2:6:2",
          Comment = MazakPart.CreateComment("uniq2", new[] {2, 2}, false),
          PlanQuantity = 42,
          Priority = 18,
          DueDate = new DateTime(2020, 4, 14, 0, 0, 0, DateTimeKind.Local),
          Processes = {
            new MazakScheduleProcessRow() {
              MazakScheduleRowId = 5,
              ProcessNumber = 1,
              ProcessMaterialQuantity = 0 //no material, input queue
						},
            new MazakScheduleProcessRow() {
              MazakScheduleRowId = 5,
              ProcessNumber = 2,
              ProcessMaterialQuantity = 0
            },
          }
        }
      });
    }
  }
}