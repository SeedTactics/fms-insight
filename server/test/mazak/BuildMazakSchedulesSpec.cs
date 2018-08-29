/* Copyright (c) 2018, John Lenz

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
    public void DeleteCompletedAndDecrementSchedules()
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

      var (actions, tokeep) = BuildMazakSchedules.RemoveCompletedAndDecrementSchedules(schedules, DecrementPriorityOnDownload: true);

      schedules.Schedules.First().Command = MazakWriteCommand.Delete;
      schedules.Schedules.Skip(1).First().Command = MazakWriteCommand.ScheduleSafeEdit;
      schedules.Schedules.Skip(1).First().Priority = 49;
      schedules.Schedules.Skip(1).First().Processes.Clear();

      actions.Schedules.Should().BeEquivalentTo(schedules.Schedules);
      actions.Parts.Should().BeEmpty();
      actions.Fixtures.Should().BeEmpty();
      actions.Pallets.Should().BeEmpty();

      tokeep.Should().BeEquivalentTo(new[] { "part2:1:1" });
    }

    [Fact]
    public void AddsSchedules()
    {
      //basic job
      var job1 = new JobPlan("uniq1", 2, new int[] { 2, 2 });
      job1.PartName = "part1";
      job1.SetPathGroup(1, 1, 1);
      job1.SetPathGroup(1, 2, 2);
      job1.SetPathGroup(2, 1, 1);
      job1.SetPathGroup(2, 2, 2);
      job1.SetSimulatedStartingTimeUTC(1, 1, new DateTime(2018, 1, 9, 8, 7, 6, DateTimeKind.Utc));
      job1.SetSimulatedStartingTimeUTC(1, 2, new DateTime(2018, 1, 2, 3, 4, 5, DateTimeKind.Utc));
      job1.SetPlannedCyclesOnFirstProcess(1, 51);
      job1.SetPlannedCyclesOnFirstProcess(2, 41);

      //one with an input queue
      var job2 = new JobPlan("uniq2", 2, new int[] { 2, 2 });
      job2.PartName = "part2";
      job2.SetPathGroup(1, 1, 1);
      job2.SetPathGroup(1, 2, 2);
      job2.SetPathGroup(2, 1, 1);
      job2.SetPathGroup(2, 2, 2);
      job2.SetSimulatedStartingTimeUTC(1, 1, new DateTime(2018, 2, 9, 8, 7, 6, DateTimeKind.Utc));
      job2.SetSimulatedStartingTimeUTC(1, 2, new DateTime(2018, 2, 2, 3, 4, 5, DateTimeKind.Utc));
      job2.SetPlannedCyclesOnFirstProcess(1, 12);
      job2.SetPlannedCyclesOnFirstProcess(2, 42);
      job2.SetInputQueue(1, 1, "aaa");
      job2.SetInputQueue(1, 2, "bbb");

      //a schedule which already exists
      var job3 = new JobPlan("uniq3", 2, new int[] { 1, 1 });
      job3.PartName = "part3";
      job3.SetSimulatedStartingTimeUTC(1, 1, new DateTime(2018, 3, 9, 8, 7, 6, DateTimeKind.Utc));
      job3.SetPlannedCyclesOnFirstProcess(1, 23);

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
            Comment = MazakPart.CreateComment("uniq3", new[] {1, 1}, false)
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
          Priority = 75,
          DueDate = new DateTime(2018, 1, 9, 8, 7, 6, DateTimeKind.Utc),
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
          Priority = 75,
          DueDate = new DateTime(2018, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddSeconds(5),
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
          Priority = 75,
          DueDate = new DateTime(2018, 2, 9, 8, 7, 6, DateTimeKind.Utc).AddSeconds(10),
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
          Priority = 75,
          DueDate = new DateTime(2018, 2, 2, 3, 4, 5, DateTimeKind.Utc).AddSeconds(15),
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