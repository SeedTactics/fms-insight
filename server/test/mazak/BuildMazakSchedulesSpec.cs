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
using System.Linq;
using AutoFixture;
using BlackMaple.FMSInsight.Tests;
using BlackMaple.MachineFramework;
using MazakMachineInterface;
using Shouldly;

namespace BlackMaple.FMSInsight.Mazak.Tests
{
  public class BuildMazakSchedulesSpec
  {
    [Test]
    public void DeleteCompletedSchedules()
    {
      var schedules = new MazakCurrentStatus()
      {
        Schedules = new[]
        {
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
                ProcessNumber = 1,
              },
            },
          },
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
                ProcessExecuteQuantity = 2,
              },
            },
          },
        },
      };

      var (actions, tokeep) = BuildMazakSchedules.RemoveCompletedSchedules(schedules);

      actions
        .Schedules.ToList()
        .ShouldBeEquivalentTo(
          new List<MazakScheduleRow>
          {
            schedules.Schedules.First() with
            {
              Command = MazakWriteCommand.Delete,
            },
          }
        );
      actions.Parts.ShouldBeEmpty();
      actions.Fixtures.ShouldBeEmpty();
      actions.Pallets.ShouldBeEmpty();

      tokeep.ShouldBe(new[] { "part2:1:1" });
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void AddsSchedules(bool conflictByFixture)
    {
      //basic job
      var uniq1 = new HistoricJob()
      {
        UniqueStr = "uniq1",
        PartName = "part1",
        RouteStartUTC = new DateTime(2020, 04, 14, 13, 43, 00, DateTimeKind.Local).ToUniversalTime(),
        Cycles = 51,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        CopiedToSystem = true,
        Processes = new[]
        {
          new ProcessInfo()
          {
            Paths = new[]
            {
              JobLogTest.EmptyPath with
              {
                SimulatedStartingUTC = new DateTime(
                  2018,
                  1,
                  9,
                  8,
                  7,
                  6,
                  DateTimeKind.Local
                ).ToUniversalTime(),
                Fixture = conflictByFixture ? "fixA" : null,
                Face = 1,
                PalletNums = conflictByFixture ? [] : [1],
              },
            }.ToImmutableList(),
          },
          new ProcessInfo()
          {
            Paths = new[]
            {
              JobLogTest.EmptyPath with
              {
                Fixture = conflictByFixture ? "fixA" : null,
                Face = 2,
                PalletNums = conflictByFixture ? [] : [1],
              },
            }.ToImmutableList(),
          },
        }.ToImmutableList(),
      };

      var uniq2 = new HistoricJob()
      {
        UniqueStr = "uniq2",
        PartName = "part1",
        RouteStartUTC = uniq1.RouteStartUTC,
        Cycles = 41,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        CopiedToSystem = true,
        Processes = new[]
        {
          new ProcessInfo()
          {
            Paths = new[]
            {
              JobLogTest.EmptyPath with
              {
                SimulatedStartingUTC = new DateTime(
                  2018,
                  1,
                  2,
                  3,
                  4,
                  5,
                  DateTimeKind.Local
                ).ToUniversalTime(),
                Fixture = conflictByFixture ? "fixA" : null,
                Face = 1,
                PalletNums = conflictByFixture ? [] : [1],
              },
            }.ToImmutableList(),
          },
          new ProcessInfo()
          {
            Paths = new[]
            {
              JobLogTest.EmptyPath with
              {
                Fixture = conflictByFixture ? "fixA" : null,
                Face = 2,
                PalletNums = conflictByFixture ? [] : [1],
              },
            }.ToImmutableList(),
          },
        }.ToImmutableList(),
      };

      //two with an input queue
      var uniq3 = new HistoricJob()
      {
        UniqueStr = "uniq3",
        PartName = "part2",
        RouteStartUTC = uniq1.RouteStartUTC,
        Cycles = 12,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        CopiedToSystem = true,
        Processes = new[]
        {
          new ProcessInfo()
          {
            Paths = new[]
            {
              JobLogTest.EmptyPath with
              {
                SimulatedStartingUTC = new DateTime(
                  2018,
                  2,
                  9,
                  8,
                  7,
                  6,
                  DateTimeKind.Local
                ).ToUniversalTime(),
                // conflicts with both uniq1 and uniq2
                Fixture = conflictByFixture ? "fixA" : null,
                Face = 2,
                PalletNums = conflictByFixture ? [] : [1],
                InputQueue = "aaa",
              },
            }.ToImmutableList(),
          },
          new ProcessInfo()
          {
            Paths = new[]
            {
              JobLogTest.EmptyPath with
              {
                Fixture = null,
                Face = 1,
                PalletNums = [],
              },
            }.ToImmutableList(),
          },
        }.ToImmutableList(),
      };

      var uniq4 = new HistoricJob()
      {
        UniqueStr = "uniq4",
        PartName = "part2",
        RouteStartUTC = uniq1.RouteStartUTC,
        Cycles = 42,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        CopiedToSystem = true,
        Processes = new[]
        {
          new ProcessInfo()
          {
            Paths = new[]
            {
              JobLogTest.EmptyPath with
              {
                SimulatedStartingUTC = new DateTime(
                  2018,
                  2,
                  2,
                  3,
                  4,
                  5,
                  DateTimeKind.Local
                ).ToUniversalTime(),
                Fixture = null,
                Face = 1,
                PalletNums = [],
                InputQueue = "bbb",
              },
            }.ToImmutableList(),
          },
          new ProcessInfo()
          {
            Paths = new[]
            {
              JobLogTest.EmptyPath with
              {
                // no conflicts
                Fixture = conflictByFixture ? "fixB" : null,
                Face = 2,
                PalletNums = conflictByFixture ? [] : [2],
              },
            }.ToImmutableList(),
          },
        }.ToImmutableList(),
      };

      //two schedule which already exists, one with same route starting, one with different
      var uniq5 = new HistoricJob()
      {
        UniqueStr = "uniq5",
        PartName = "part3",
        RouteStartUTC = uniq1.RouteStartUTC,
        Cycles = 23,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        CopiedToSystem = true,
        Processes = new[]
        {
          new ProcessInfo()
          {
            Paths = new[]
            {
              JobLogTest.EmptyPath with
              {
                SimulatedStartingUTC = new DateTime(
                  2018,
                  3,
                  9,
                  8,
                  7,
                  6,
                  DateTimeKind.Local
                ).ToUniversalTime(),
              },
            }.ToImmutableList(),
          },
          new ProcessInfo() { Paths = new[] { JobLogTest.EmptyPath }.ToImmutableList() },
        }.ToImmutableList(),
      };

      // all the parts, plus a schedule for uniq5
      var curData = new MazakAllData()
      {
        Parts = new[]
        {
          new MazakPartRow() { PartName = "part1:6:1", Comment = "uniq1-Insight" },
          new MazakPartRow() { PartName = "part1:6:2", Comment = "uniq2-Insight" },
          new MazakPartRow() { PartName = "part2:6:1", Comment = "uniq3-Path1-1" }, // old versions of Insight included -Path, check for backwards compatibility
          new MazakPartRow() { PartName = "part2:6:2", Comment = "uniq4-Insight" },
          new MazakPartRow() { PartName = "part3:6:1", Comment = "uniq5-Insight" },
        },
        Schedules = new[]
        {
          new MazakScheduleRow()
          {
            Id = 1,
            PartName = "part3:6:1",
            Comment = "uniq5-Insight",
            DueDate = new DateTime(2020, 04, 14, 0, 0, 0, DateTimeKind.Local),
            Priority = 17,
          },
        },
      };

      var actions = BuildMazakSchedules.AddSchedules(
        curData,
        new[] { uniq1, uniq2, uniq3, uniq4, uniq5 },
        new MazakConfig() { DBType = MazakDbType.MazakSmooth, UseStartingOffsetForDueDate = true }
      );
      actions.Parts.ShouldBeEmpty();
      actions.Fixtures.ShouldBeEmpty();
      actions.Pallets.ShouldBeEmpty();

      actions
        .Schedules.OrderBy(s => s.Id)
        .ToList()
        .ShouldBeEquivalentTo(
          new List<MazakScheduleRow>
          {
            new MazakScheduleRow()
            {
              Command = MazakWriteCommand.Add,
              Id = 2,
              PartName = "part1:6:1",
              Comment = "uniq1-Insight",
              PlanQuantity = 51,
              Priority = 18 + 1, // max existing is 17 so start at 18, plus one earlier conflict
              DueDate = new DateTime(2020, 4, 14, 0, 0, 0, DateTimeKind.Local),
              Processes =
              {
                new MazakScheduleProcessRow()
                {
                  MazakScheduleRowId = 2,
                  ProcessNumber = 1,
                  ProcessMaterialQuantity = 51,
                },
                new MazakScheduleProcessRow()
                {
                  MazakScheduleRowId = 2,
                  ProcessNumber = 2,
                  ProcessMaterialQuantity = 0,
                },
              },
            },
            new MazakScheduleRow()
            {
              Command = MazakWriteCommand.Add,
              Id = 3,
              PartName = "part1:6:2",
              Comment = "uniq2-Insight",
              PlanQuantity = 41,
              Priority = 18, // max existing is 17 so start at 18
              DueDate = new DateTime(2020, 4, 14, 0, 0, 0, DateTimeKind.Local),
              Processes =
              {
                new MazakScheduleProcessRow()
                {
                  MazakScheduleRowId = 3,
                  ProcessNumber = 1,
                  ProcessMaterialQuantity = 41,
                },
                new MazakScheduleProcessRow()
                {
                  MazakScheduleRowId = 3,
                  ProcessNumber = 2,
                  ProcessMaterialQuantity = 0,
                },
              },
            },
            new MazakScheduleRow()
            {
              Command = MazakWriteCommand.Add,
              Id = 4,
              PartName = "part2:6:1",
              Comment = "uniq3-Path1-1",
              PlanQuantity = 12,
              Priority = 18 + 2, // conflicts with 2 earlier
              DueDate = new DateTime(2020, 4, 14, 0, 0, 0, DateTimeKind.Local),
              Processes =
              {
                new MazakScheduleProcessRow()
                {
                  MazakScheduleRowId = 4,
                  ProcessNumber = 1,
                  ProcessMaterialQuantity =
                    0 // no material, input queue
                  ,
                },
                new MazakScheduleProcessRow()
                {
                  MazakScheduleRowId = 4,
                  ProcessNumber = 2,
                  ProcessMaterialQuantity = 0,
                },
              },
            },
            new MazakScheduleRow()
            {
              Command = MazakWriteCommand.Add,
              Id = 5,
              PartName = "part2:6:2",
              Comment = "uniq4-Insight",
              PlanQuantity = 42,
              Priority = 18,
              DueDate = new DateTime(2020, 4, 14, 0, 0, 0, DateTimeKind.Local),
              Processes =
              {
                new MazakScheduleProcessRow()
                {
                  MazakScheduleRowId = 5,
                  ProcessNumber = 1,
                  ProcessMaterialQuantity =
                    0 //no material, input queue
                  ,
                },
                new MazakScheduleProcessRow()
                {
                  MazakScheduleRowId = 5,
                  ProcessNumber = 2,
                  ProcessMaterialQuantity = 0,
                },
              },
            },
          }
        );
    }

    [Test]
    public void CorrectPriorityWithPerPalletStartTimes()
    {
      var baseTime = new DateTime(2026, 1, 17, 14, 0, 0, DateTimeKind.Utc);

      var jobA = new HistoricJob()
      {
        UniqueStr = "uniqA",
        PartName = "partA",
        RouteStartUTC = baseTime,
        RouteEndUTC = DateTime.MinValue,
        Cycles = 5,
        Archived = false,
        CopiedToSystem = true,
        Processes = new[]
        {
          new ProcessInfo()
          {
            Paths = new[]
            {
              JobLogTest.EmptyPath with
              {
                PalletNums = [1, 2, 3, 4],
                SimulatedStartTimePerPallet = ImmutableDictionary<int, DateTime>
                  .Empty.Add(1, baseTime.AddHours(7))
                  .Add(2, baseTime.AddHours(7))
                  .Add(3, baseTime)
                  .Add(4, baseTime),
                SimulatedStartingUTC = baseTime,
              },
            }.ToImmutableList(),
          },
        }.ToImmutableList(),
      };

      var jobB = new HistoricJob()
      {
        UniqueStr = "uniqB",
        PartName = "partB",
        RouteStartUTC = baseTime,
        RouteEndUTC = DateTime.MinValue,
        Cycles = 5,
        Archived = false,
        CopiedToSystem = true,
        Processes = new[]
        {
          new ProcessInfo()
          {
            Paths = new[]
            {
              JobLogTest.EmptyPath with
              {
                PalletNums = [1, 2],
                SimulatedStartTimePerPallet = ImmutableDictionary<int, DateTime>
                  .Empty.Add(1, baseTime.AddHours(6))
                  .Add(2, baseTime.AddHours(6)),
                SimulatedStartingUTC = baseTime.AddHours(6),
              },
            }.ToImmutableList(),
          },
        }.ToImmutableList(),
      };

      var curData = new MazakAllData()
      {
        Parts = new[]
        {
          new MazakPartRow() { PartName = "partA:1:1", Comment = "uniqA-Insight" },
          new MazakPartRow() { PartName = "partB:1:1", Comment = "uniqB-Insight" },
        },
        Schedules = Array.Empty<MazakScheduleRow>(),
      };

      var actions = BuildMazakSchedules.AddSchedules(
        curData,
        new[] { jobA, jobB },
        new MazakConfig() { DBType = MazakDbType.MazakSmooth, UseStartingOffsetForDueDate = true }
      );

      var schA = actions.Schedules.Single(s => s.Comment == "uniqA-Insight");
      var schB = actions.Schedules.Single(s => s.Comment == "uniqB-Insight");

      schB.Priority.ShouldBeLessThan(schA.Priority);
    }

    [Test]
    [MatrixDataSource]
    public void UsesZeroQtyForProc1(
      [Matrix(true, false)] bool hasInputQueue,
      [Matrix(true, false)] bool shouldSyncProc1
    )
    {
      var cfg = new MazakConfig()
      {
        DBType = MazakDbType.MazakSmooth,
        SynchronizeRawMaterialInQueues = shouldSyncProc1,
      };

      var fix = new Fixture();
      fix.Customizations.Add(new ImmutableSpecimenBuilder());

      var job = fix.Build<Job>()
        .With(j => j.Cycles, 30)
        .With(
          j => j.Processes,
          [
            new ProcessInfo()
            {
              Paths =
              [
                fix.Build<ProcPathInfo>()
                  .With(p => p.InputQueue, hasInputQueue ? "inputQ" : "")
                  .With(x => x.OutputQueue, "outQ")
                  .Create(),
              ],
            },
            new ProcessInfo()
            {
              Paths = [fix.Build<ProcPathInfo>().With(p => p.InputQueue, "outQ").Create()],
            },
          ]
        )
        .Create();

      var actions = BuildMazakSchedules.AddSchedules(
        new MazakAllData()
        {
          Schedules = [],
          Parts =
          [
            new MazakPartRow() { PartName = job.PartName + ":1", Comment = job.UniqueStr + "-Insight" },
          ],
        },
        [job],
        cfg
      );

      actions.Schedules.Count.ShouldBe(1);
      actions.Schedules[0].PlanQuantity.ShouldBe(30);
      actions.Schedules[0].Processes.Count.ShouldBe(2);
      actions
        .Schedules[0]
        .Processes[0]
        .ProcessMaterialQuantity.ShouldBe(hasInputQueue && shouldSyncProc1 ? 0 : 30);
    }
  }
}
