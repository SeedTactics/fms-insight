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
using Xunit;
using FluentAssertions;
using BlackMaple.MachineFramework;
using MazakMachineInterface;
using System.Collections.Immutable;

namespace MachineWatchTest
{
  public class QueueSpec : IDisposable
  {
    private RepositoryConfig _repoCfg;
    private IRepository _logDB;

    private readonly DateTime _now;

    public QueueSpec()
    {
      _repoCfg = RepositoryConfig.InitializeSingleThreadedMemoryDB(new FMSSettings());
      _logDB = _repoCfg.OpenConnection();

      _now = DateTime.UtcNow.AddHours(1);
    }

    public void Dispose()
    {
      _repoCfg.CloseMemoryConnection();
    }

    private class TestMazakData
    {
      public List<MazakScheduleRow> Schedules { get; } = new List<MazakScheduleRow>();
      public List<LoadAction> LoadActions { get; } = new List<LoadAction>();

      public MazakCurrentStatus ToData()
      {
        return new MazakCurrentStatus()
        {
          Schedules = Schedules,
          LoadActions = LoadActions
        };
      }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Empty(bool waitAll)
    {
      var queues = new MazakQueues(null, waitForAllCastings: waitAll);
      var trans = queues.CalculateScheduleChanges(_logDB, new TestMazakData().ToData());
      trans.Should().BeNull();
    }

    private MazakScheduleRow AddSchedule(TestMazakData read, int schId, string unique, string part, int pri, int numProc, int complete, int plan, int partIdx = 1, DateTime? dueDate = null)
    {
      var row = new MazakScheduleRow()
      {
        Comment = MazakPart.CreateComment(unique, Enumerable.Repeat(1, numProc), false),
        CompleteQuantity = complete,
        DueDate = dueDate ?? DateTime.Today,
        FixForMachine = 1,
        HoldMode = 0,
        MissingFixture = 0,
        MissingProgram = 0,
        MissingTool = 0,
        MixScheduleID = 1,
        PartName = part + ":10:" + partIdx.ToString(),
        PlanQuantity = plan,
        Priority = pri,
        ProcessingPriority = 1,
        Id = schId,
      };
      read.Schedules.Add(row);
      return row;
    }

    private void AddScheduleProcess(MazakScheduleRow schRow, int proc, int matQty, int exeQty, int fixQty = 1)
    {
      schRow.Processes.Add(new MazakScheduleProcessRow()
      {
        MazakScheduleRowId = schRow.Id,
        ProcessBadQuantity = 0,
        ProcessExecuteQuantity = exeQty,
        ProcessMachine = 1,
        FixQuantity = fixQty,
        ProcessMaterialQuantity = matQty,
        ProcessNumber = proc,
      });
    }

    private long AddCasting(string casting, string queue)
    {
      // Same as RoutingInfo.AddUnallocatedCastingToQueue
      var mat = _logDB.AllocateMaterialIDForCasting(casting);
      _logDB.RecordAddMaterialToQueue(mat, 0, queue, position: -1, timeUTC: _now, operatorName: null, reason: "TestAddCasting");
      return mat;
    }

    private long AddAssigned(string uniq, string part, int numProc, int lastProc, int path, string queue)
    {
      // Same as RoutingInfo.AddUnprocessedMaterialToQueue
      var mat = _logDB.AllocateMaterialID(uniq, part, numProc);
      _logDB.RecordPathForProcess(mat, Math.Max(1, lastProc), path);
      _logDB.RecordAddMaterialToQueue(mat, lastProc, queue, position: -1, timeUTC: _now, operatorName: null, reason: "TestAddAssigned");
      return mat;
    }

    [Fact]
    public void AddAssignedMaterialToQueueNoWaitForAll()
    {
      var queues = new MazakQueues(null, waitForAllCastings: false);
      var read = new TestMazakData();

      // plan 50, 40 completed, and 5 in process.  So there are 5 remaining.
      var schRow = AddSchedule(read, schId: 10, unique: "uuuu", part: "pppp", numProc: 2, pri: 10, plan: 50, complete: 40);
      AddScheduleProcess(schRow, proc: 1, matQty: 0, exeQty: 5);
      AddScheduleProcess(schRow, proc: 2, matQty: 0, exeQty: 0);

      var j = new Job()
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo() { InputQueue = "thequeue" }) },
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo() { InputQueue = "thequeue" }) }
        )
      };
      _logDB.AddJobs(new NewJobs()
      {
        Jobs = ImmutableList.Create<Job>(j),
        ScheduleId = "sch11",
      }, null, addAsCopiedToSystem: true);

      var trans = queues.CalculateScheduleChanges(_logDB, read.ToData());
      trans.Schedules.Should().BeEmpty();

      // put 2 castings in queue, plus a different unique and a different process
      AddAssigned(uniq: "uuuu", part: "pppp", numProc: 1, lastProc: 0, path: 1, queue: "thequeue");
      AddAssigned(uniq: "uuuu", part: "pppp", numProc: 1, lastProc: 0, path: 1, queue: "thequeue");
      AddAssigned(uniq: "xxxx", part: "pppp", numProc: 1, lastProc: 0, path: 1, queue: "thequeue");
      AddAssigned(uniq: "uuuu", part: "pppp", numProc: 1, lastProc: 1, path: 1, queue: "thequeue");

      //put something else at load station
      var action = new LoadAction(true, 1, "pppp", MazakPart.CreateComment("yyyy", new[] { 1 }, false), 1, 1);

      trans = queues.CalculateScheduleChanges(_logDB, read.ToData());

      trans.Schedules.Count.Should().Be(1);
      trans.Schedules[0].Id.Should().Be(10);
      trans.Schedules[0].Priority.Should().Be(10);
      trans.Schedules[0].Processes.Count.Should().Be(2);
      trans.Schedules[0].Processes[0].ProcessNumber.Should().Be(1);
      trans.Schedules[0].Processes[0].ProcessMaterialQuantity.Should().Be(2); // set the 2 material
      trans.Schedules[0].Processes[1].ProcessNumber.Should().Be(2);
      trans.Schedules[0].Processes[1].ProcessMaterialQuantity.Should().Be(1); // set the 1 material
    }

    [Theory]
    [InlineData(null)]
    [InlineData("casting")]
    public void AddAssignedToQueueNoWaitForAll(string casting)
    {
      var queues = new MazakQueues(null, waitForAllCastings: false);
      var read = new TestMazakData();

      // plan 50, 40 completed, and 5 machining.  1 in proc on mat 1 and 2 in proc on mat 2
      var schRow = AddSchedule(read, schId: 10, unique: "uuuu", part: "pppp", numProc: 2, pri: 10, plan: 50, complete: 40);
      AddScheduleProcess(schRow, proc: 1, matQty: 1, exeQty: 5);
      AddScheduleProcess(schRow, proc: 2, matQty: 2, exeQty: 0);

      var j = new Job()
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo() { InputQueue = "thequeue", Casting = casting }) },
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo() { InputQueue = "thequeue" }) }
        )
      };
      if (casting == null)
      {
        casting = "pppp";
      }
      _logDB.AddJobs(new NewJobs()
      {
        Jobs = ImmutableList.Create<Job>(j),
        ScheduleId = "sch22",
      }, null, addAsCopiedToSystem: true);

      // add the material which matches the schedule
      AddAssigned(uniq: "uuuu", part: "pppp", numProc: 1, lastProc: 0, path: 1, queue: "thequeue");
      AddAssigned(uniq: "uuuu", part: "pppp", numProc: 1, lastProc: 1, path: 1, queue: "thequeue");
      AddAssigned(uniq: "uuuu", part: "pppp", numProc: 1, lastProc: 1, path: 1, queue: "thequeue");

      // and some extra stuff
      AddAssigned(uniq: "xxxx", part: "pppp", numProc: 1, lastProc: 0, path: 1, queue: "thequeue");
      AddAssigned(uniq: "xxxx", part: "pppp", numProc: 1, lastProc: 1, path: 1, queue: "thequeue");
      AddCasting(casting, "thequeue");
      AddCasting(casting, "thequeue");

      var trans = queues.CalculateScheduleChanges(_logDB, read.ToData());
      trans.Schedules.Should().BeEmpty();

      // add 1 more for each proc 1 and 2
      AddAssigned(uniq: "uuuu", part: "pppp", numProc: 1, lastProc: 0, path: 1, queue: "thequeue");
      AddAssigned(uniq: "uuuu", part: "pppp", numProc: 1, lastProc: 1, path: 1, queue: "thequeue");

      trans = queues.CalculateScheduleChanges(_logDB, read.ToData());

      trans.Schedules.Count.Should().Be(1);
      trans.Schedules[0].Priority.Should().Be(10);
      trans.Schedules[0].Processes.Count.Should().Be(2);
      trans.Schedules[0].Processes[0].ProcessNumber.Should().Be(1);
      trans.Schedules[0].Processes[0].ProcessMaterialQuantity.Should().Be(2);
      trans.Schedules[0].Processes[1].ProcessNumber.Should().Be(2);
      trans.Schedules[0].Processes[1].ProcessMaterialQuantity.Should().Be(3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("casting")]
    public void AddAssignedToQueueWaitForAll(string casting)
    {
      var queues = new MazakQueues(null, waitForAllCastings: true);
      var read = new TestMazakData();

      // plan 50, 40 completed, and 5 machining.  1 in proc on mat 1 and 2 in proc on mat 2
      var schRow = AddSchedule(read, schId: 10, unique: "uuuu", part: "pppp", numProc: 2, pri: 10, plan: 50, complete: 40);
      AddScheduleProcess(schRow, proc: 1, matQty: 1, exeQty: 5);
      AddScheduleProcess(schRow, proc: 2, matQty: 2, exeQty: 0);

      var j = new Job()
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo() { InputQueue = "thequeue", Casting = casting }) },
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo() { InputQueue = "thequeue" }) }
        )
      };
      if (casting == null)
      {
        casting = "pppp";
      }
      _logDB.AddJobs(new NewJobs()
      {
        Jobs = ImmutableList.Create<Job>(j),
        ScheduleId = "sch33"
      }, null, addAsCopiedToSystem: true);

      // add the material which matches the schedule
      AddAssigned(uniq: "uuuu", part: "pppp", numProc: 1, lastProc: 0, path: 1, queue: "thequeue");
      AddAssigned(uniq: "uuuu", part: "pppp", numProc: 1, lastProc: 1, path: 1, queue: "thequeue");
      AddAssigned(uniq: "uuuu", part: "pppp", numProc: 1, lastProc: 1, path: 1, queue: "thequeue");

      // and some extra stuff
      AddAssigned(uniq: "xxxx", part: "pppp", numProc: 1, lastProc: 0, path: 1, queue: "thequeue");
      AddAssigned(uniq: "xxxx", part: "pppp", numProc: 1, lastProc: 1, path: 1, queue: "thequeue");
      AddCasting(casting, "thequeue");
      AddCasting(casting, "thequeue");

      var trans = queues.CalculateScheduleChanges(_logDB, read.ToData());
      trans.Schedules.Should().BeEmpty();

      // add 1 more for each proc 1 and 2.
      // proc 2 should be added, while the mat from proc1 is removed to match the mazak schedule
      var matIdProc1 = AddAssigned(uniq: "uuuu", part: "pppp", numProc: 1, lastProc: 0, path: 1, queue: "thequeue");
      AddAssigned(uniq: "uuuu", part: "pppp", numProc: 1, lastProc: 1, path: 1, queue: "thequeue");

      _logDB.GetMaterialInQueueByUnique("thequeue", "uuuu").Should().Contain(m => m.MaterialID == matIdProc1);
      _logDB.IsMaterialInQueue(matIdProc1).Should().BeTrue();

      trans = queues.CalculateScheduleChanges(_logDB, read.ToData());

      trans.Schedules.Count.Should().Be(1);
      trans.Schedules[0].Priority.Should().Be(10);
      trans.Schedules[0].Processes.Count.Should().Be(2);
      trans.Schedules[0].Processes[0].ProcessNumber.Should().Be(1);
      trans.Schedules[0].Processes[0].ProcessMaterialQuantity.Should().Be(1);
      trans.Schedules[0].Processes[1].ProcessNumber.Should().Be(2);
      trans.Schedules[0].Processes[1].ProcessMaterialQuantity.Should().Be(3);

      _logDB.GetMaterialInQueueByUnique("thequeue", "uuuu").Should().NotContain(m => m.MaterialID == matIdProc1);
      _logDB.IsMaterialInQueue(matIdProc1).Should().BeFalse();
    }

    [Fact]
    public void RemoveMatFromQueueNoWaitForAll()
    {
      var queues = new MazakQueues(null, waitForAllCastings: false);
      var read = new TestMazakData();

      // plan 50, 30 completed.  proc1 has 5 in process, 2 material.  proc2 has 3 in process, 4 material
      var schRow = AddSchedule(read, schId: 10, unique: "uuuu", part: "pppp", numProc: 1, pri: 10, plan: 50, complete: 30);
      AddScheduleProcess(schRow, proc: 1, matQty: 2, exeQty: 5);
      AddScheduleProcess(schRow, proc: 2, matQty: 4, exeQty: 3);

      var j = new Job()
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo() { InputQueue = "castingQ" }) },
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo() { InputQueue = "transQ" }) }
        )
      };
      _logDB.AddJobs(new NewJobs()
      {
        Jobs = ImmutableList.Create<Job>(j),
        ScheduleId = "sch44"
      }, null, addAsCopiedToSystem: true);

      // put 2 allocated casting in queue
      var proc1Mat = Enumerable.Range(0, 2).Select(i =>
        AddAssigned(uniq: "uuuu", part: "pppp", numProc: 2, lastProc: 0, path: 1, queue: "castingQ")
      ).ToList();

      // put 4 in-proc in queue
      var proc2Mat = Enumerable.Range(0, 4).Select(i =>
        AddAssigned(uniq: "uuuu", part: "pppp", numProc: 2, lastProc: 1, path: 1, queue: "transQ")
      ).ToList();

      // some extra stuff
      AddAssigned(uniq: "xxxx", part: "pppp", numProc: 1, lastProc: 0, path: 1, queue: "castingQ");
      AddAssigned(uniq: "xxxx", part: "pppp", numProc: 1, lastProc: 1, path: 1, queue: "transQ");

      var trans = queues.CalculateScheduleChanges(_logDB, read.ToData());
      trans.Schedules.Should().BeEmpty();

      // now remove one from process 1 and one from process 2
      _logDB.RecordRemoveMaterialFromAllQueues(proc1Mat[0], 1);
      _logDB.RecordRemoveMaterialFromAllQueues(proc2Mat[0], 2);

      trans = queues.CalculateScheduleChanges(_logDB, read.ToData());

      trans.Schedules.Count.Should().Be(1);
      trans.Schedules[0].Id.Should().Be(10);
      trans.Schedules[0].Priority.Should().Be(10);
      trans.Schedules[0].Processes.Count.Should().Be(2);
      trans.Schedules[0].Processes[0].ProcessNumber.Should().Be(1);
      trans.Schedules[0].Processes[0].ProcessMaterialQuantity.Should().Be(1); // sets the material to 1
      trans.Schedules[0].Processes[1].ProcessNumber.Should().Be(2);
      trans.Schedules[0].Processes[1].ProcessMaterialQuantity.Should().Be(3); // set the material back to 3
    }

    [Fact]
    public void RemoveMatFromQueueWaitForAll()
    {
      var queues = new MazakQueues(null, waitForAllCastings: true);
      var read = new TestMazakData();

      // plan 50, 30 completed.  proc1 has 5 in process, 2 material.  proc2 has 3 in process, 4 material
      var schRow = AddSchedule(read, schId: 10, unique: "uuuu", part: "pppp", numProc: 1, pri: 10, plan: 50, complete: 30);
      AddScheduleProcess(schRow, proc: 1, matQty: 2, exeQty: 5);
      AddScheduleProcess(schRow, proc: 2, matQty: 4, exeQty: 3);

      var j = new Job()
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo() { InputQueue = "castingQ" }) },
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo() { InputQueue = "transQ" }) }
        )
      };
      _logDB.AddJobs(new NewJobs()
      {
        Jobs = ImmutableList.Create<Job>(j),
        ScheduleId = "sch55",
      }, null, addAsCopiedToSystem: true);

      // put 2 allocated casting in queue
      var proc1Mat = Enumerable.Range(0, 2).Select(i =>
        AddAssigned(uniq: "uuuu", part: "pppp", numProc: 2, lastProc: 0, path: 1, queue: "castingQ")
      ).ToList();

      // put 4 in-proc in queue
      var proc2Mat = Enumerable.Range(0, 4).Select(i =>
        AddAssigned(uniq: "uuuu", part: "pppp", numProc: 2, lastProc: 1, path: 1, queue: "transQ")
      ).ToList();

      // some extra stuff
      var xxxId1 = AddAssigned(uniq: "xxxx", part: "pppp", numProc: 1, lastProc: 0, path: 1, queue: "castingQ");
      var xxxId2 = AddAssigned(uniq: "xxxx", part: "pppp", numProc: 1, lastProc: 1, path: 1, queue: "transQ");

      var trans = queues.CalculateScheduleChanges(_logDB, read.ToData());
      trans.Schedules.Should().BeEmpty();

      // now remove one from process 1 and one from process 2
      _logDB.RecordRemoveMaterialFromAllQueues(proc1Mat[0], 1);
      _logDB.RecordRemoveMaterialFromAllQueues(proc2Mat[0], 2);

      var expectedTransQ = proc2Mat.Skip(1).Select((matId, idx) =>
          new QueuedMaterial()
          {
            MaterialID = matId,
            Queue = "transQ",
            Position = idx,
            Unique = "uuuu",
            PartNameOrCasting = "pppp",
            NumProcesses = 2,
            AddTimeUTC = _now,
            NextProcess = 2,
            Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
          }
      ).Append(
          new QueuedMaterial()
          {
            MaterialID = xxxId2,
            Queue = "transQ",
            Position = 3,
            Unique = "xxxx",
            PartNameOrCasting = "pppp",
            NumProcesses = 1,
            AddTimeUTC = _now,
            NextProcess = 2,
            Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
          }
      ).ToList();

      _logDB.GetMaterialInAllQueues().Should().BeEquivalentTo(new[] {
          new QueuedMaterial() {
            MaterialID = proc1Mat[1], Queue = "castingQ", Position = 0, Unique = "uuuu", PartNameOrCasting = "pppp", NumProcesses = 2, AddTimeUTC = _now,
            NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
          },
          new QueuedMaterial() {
            MaterialID = xxxId1, Queue = "castingQ", Position = 1, Unique = "xxxx", PartNameOrCasting = "pppp", NumProcesses = 1, AddTimeUTC = _now,
            NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
          }
      }.Concat(expectedTransQ));

      trans = queues.CalculateScheduleChanges(_logDB, read.ToData());

      // adds an extra material with id xxxId2 + 1
      var actual = _logDB.GetMaterialInAllQueues();
      actual.Should().BeEquivalentTo(new[] {
          new QueuedMaterial() {
            MaterialID = proc1Mat[1], Queue = "castingQ", Position = 0, Unique = "uuuu", PartNameOrCasting = "pppp", NumProcesses = 2, AddTimeUTC = _now,
            NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
          },
          new QueuedMaterial() {
            MaterialID = xxxId1, Queue = "castingQ", Position = 1, Unique = "xxxx", PartNameOrCasting = "pppp", NumProcesses = 1, AddTimeUTC = _now,
            NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
          },
          new QueuedMaterial() {
            MaterialID = xxxId2 + 1, Queue = "castingQ", Position = 2, Unique = "uuuu", PartNameOrCasting = "pppp", NumProcesses = 2, AddTimeUTC = actual.Last(m => m.Queue == "castingQ").AddTimeUTC,
            NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
          }
      }.Concat(expectedTransQ));

      trans.Schedules.Count.Should().Be(1);
      trans.Schedules[0].Id.Should().Be(10);
      trans.Schedules[0].Priority.Should().Be(10);
      trans.Schedules[0].Processes.Count.Should().Be(2);
      trans.Schedules[0].Processes[0].ProcessNumber.Should().Be(1);
      trans.Schedules[0].Processes[0].ProcessMaterialQuantity.Should().Be(2); // unchanged quantity of 2
      trans.Schedules[0].Processes[1].ProcessNumber.Should().Be(2);
      trans.Schedules[0].Processes[1].ProcessMaterialQuantity.Should().Be(3); // set the material back to 3

      read.Schedules[0].Processes[1] = read.Schedules[0].Processes[1] with { ProcessMaterialQuantity = 3 };

      trans = queues.CalculateScheduleChanges(_logDB, read.ToData());
      trans.Schedules.Should().BeEmpty();

      // no extra material a second time
      actual = _logDB.GetMaterialInAllQueues();
      actual.Should().BeEquivalentTo(new[] {
          new QueuedMaterial() {
            MaterialID = proc1Mat[1], Queue = "castingQ", Position = 0, Unique = "uuuu", PartNameOrCasting = "pppp", NumProcesses = 2, AddTimeUTC = _now,
            NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
          },
          new QueuedMaterial() {
            MaterialID = xxxId1, Queue = "castingQ", Position = 1, Unique = "xxxx", PartNameOrCasting = "pppp", NumProcesses = 1, AddTimeUTC = _now,
            NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
          },
          new QueuedMaterial() {
            MaterialID = xxxId2 + 1, Queue = "castingQ", Position = 2, Unique = "uuuu", PartNameOrCasting = "pppp", NumProcesses = 2, AddTimeUTC = actual.Last(m => m.Queue == "castingQ").AddTimeUTC,
            NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
          }
      }.Concat(expectedTransQ));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("mycasting")]
    public void AddCastingsByFixedQuantity(string casting)
    {
      var queues = new MazakQueues(null, waitForAllCastings: false);
      var read = new TestMazakData();

      // plan 50, 43 completed, and 5 in process.  So there are 2 remaining.
      var schRow = AddSchedule(read, schId: 10, unique: "uuuu", part: "pppp", numProc: 1, pri: 10, plan: 50, complete: 43);
      AddScheduleProcess(schRow, proc: 1, matQty: 0, exeQty: 5);

      var j = new Job()
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo() { InputQueue = "thequeue", Casting = casting }) }
        )
      };
      if (casting == null)
      {
        casting = "pppp";
      }
      _logDB.AddJobs(new NewJobs()
      {
        Jobs = ImmutableList.Create<Job>(j),
        ScheduleId = "sch66"
      }, null, addAsCopiedToSystem: true);

      // put a different casting
      var mat0 = AddCasting("unused", "thequeue");

      var trans = queues.CalculateScheduleChanges(_logDB, read.ToData());
      trans.Schedules.Should().BeEmpty();

      // put 3 unassigned castings in queue
      var mat1 = AddCasting(casting, "thequeue");
      var mat2 = AddCasting(casting, "thequeue");
      var mat3 = AddCasting(casting, "thequeue");

      _logDB.GetMaterialInAllQueues().Should().BeEquivalentTo(new[] {
            new QueuedMaterial() {
              MaterialID = mat0, Queue = "thequeue", Position = 0, Unique = "", PartNameOrCasting = "unused", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
              MaterialID = mat1, Queue = "thequeue", Position = 1, Unique = "", PartNameOrCasting = casting, NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
              MaterialID = mat2, Queue = "thequeue", Position = 2, Unique = "", PartNameOrCasting = casting, NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
              MaterialID = mat3, Queue = "thequeue", Position = 3, Unique = "", PartNameOrCasting = casting, NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
          });

      // should allocate 1 parts to uuuu since that is the fixed quantity
      trans = queues.CalculateScheduleChanges(_logDB, read.ToData());

      _logDB.GetMaterialInAllQueues().Should().BeEquivalentTo(new[] {
            new QueuedMaterial() {
              MaterialID = mat0, Queue = "thequeue", Position = 0, Unique = "", PartNameOrCasting = "unused", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
              MaterialID = mat1, Queue = "thequeue", Position = 1, Unique = "uuuu", PartNameOrCasting = "pppp", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
            new QueuedMaterial() {
              MaterialID = mat2, Queue = "thequeue", Position = 2, Unique = "", PartNameOrCasting = casting, NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
              MaterialID = mat3, Queue = "thequeue", Position = 3, Unique = "", PartNameOrCasting = casting, NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
          });
      _logDB.GetMaterialDetails(mat1).Paths.Should().BeEquivalentTo(new Dictionary<int, int>() { { 1, 1 } });

      trans.Schedules.Count.Should().Be(1);
      trans.Schedules[0].Priority.Should().Be(10);
      trans.Schedules[0].Id.Should().Be(10);
      trans.Schedules[0].Processes.Count.Should().Be(1);
      trans.Schedules[0].Processes[0].ProcessMaterialQuantity.Should().Be(1); // set the material
    }

    [Theory]
    [InlineData(null)]
    [InlineData("mycasting")]
    public void AddCastingsByPlannedQuantity(string casting)
    {
      var queues = new MazakQueues(null, waitForAllCastings: true);
      var read = new TestMazakData();

      // plan 50, 43 completed, and 5 in process.  So there are 2 remaining.
      var schRow = AddSchedule(read, schId: 10, unique: "uuuu", part: "pppp", numProc: 1, pri: 10, plan: 50, complete: 43);
      AddScheduleProcess(schRow, proc: 1, matQty: 0, exeQty: 5);

      var j = new Job()
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo() { InputQueue = "thequeue", Casting = casting }) }
        )
      };
      if (casting == null)
      {
        casting = "pppp";
      }
      _logDB.AddJobs(new NewJobs()
      {
        Jobs = ImmutableList.Create<Job>(j),
        ScheduleId = "sch77"
      }, null, addAsCopiedToSystem: true);


      // put 1 unassigned castings in queue
      var mat1 = AddCasting("unused", "thequeue");
      var mat2 = AddCasting(casting, "thequeue");

      // should not be enough, need two to fill out planned quantity
      var trans = queues.CalculateScheduleChanges(_logDB, read.ToData());
      trans.Schedules.Should().BeEmpty();

      // now add two more
      var mat3 = AddCasting(casting, "thequeue");
      var mat4 = AddCasting(casting, "thequeue");

      _logDB.GetMaterialInAllQueues().Should().BeEquivalentTo(new[] {
            new QueuedMaterial() {
              MaterialID = mat1, Queue = "thequeue", Position = 0, Unique = "", PartNameOrCasting = "unused", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
              MaterialID = mat2, Queue = "thequeue", Position = 1, Unique = "", PartNameOrCasting = casting, NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
              MaterialID = mat3, Queue = "thequeue", Position = 2, Unique = "", PartNameOrCasting = casting, NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
              MaterialID = mat4, Queue = "thequeue", Position = 3, Unique = "", PartNameOrCasting = casting, NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
          });

      // should allocate 2 parts to uuuu since that is the remaining planned
      trans = queues.CalculateScheduleChanges(_logDB, read.ToData());

      _logDB.GetMaterialInAllQueues().Should().BeEquivalentTo(new[] {
            new QueuedMaterial() {
              MaterialID = mat1, Queue = "thequeue", Position = 0, Unique = "", PartNameOrCasting = "unused", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
              MaterialID = mat2, Queue = "thequeue", Position = 1, Unique = "uuuu", PartNameOrCasting = "pppp", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
            new QueuedMaterial() {
              MaterialID = mat3, Queue = "thequeue", Position = 2, Unique = "uuuu", PartNameOrCasting = "pppp", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
            new QueuedMaterial() {
              MaterialID = mat4, Queue = "thequeue", Position = 3, Unique = "", PartNameOrCasting = casting, NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
          });
      _logDB.GetMaterialDetails(mat2).Paths.Should().BeEquivalentTo(new Dictionary<int, int>() { { 1, 1 } });
      _logDB.GetMaterialDetails(mat3).Paths.Should().BeEquivalentTo(new Dictionary<int, int>() { { 1, 1 } });

      trans.Schedules.Count.Should().Be(1);
      trans.Schedules[0].Priority.Should().Be(10);
      trans.Schedules[0].Id.Should().Be(10);
      trans.Schedules[0].Processes.Count.Should().Be(1);
      trans.Schedules[0].Processes[0].ProcessMaterialQuantity.Should().Be(2); // set the material
    }

    [Fact]
    public void AllocateCastingsAndMatQtyChanges()
    {
      var queues = new MazakQueues(null, waitForAllCastings: false);
      var read = new TestMazakData();

      // plan 50, 43 completed, and 3 in process, 0 material in mazak, but 2 assigned in queue.
      // So there are 2 remaining unallocated castings.
      var schRow = AddSchedule(read, schId: 10, unique: "uuuu", part: "pppp", numProc: 1, pri: 10, plan: 50, complete: 43);
      AddScheduleProcess(schRow, proc: 1, matQty: 0, exeQty: 3);

      var j = new Job()
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo() { InputQueue = "thequeue", Casting = "casting" }) }
        )
      };
      _logDB.AddJobs(new NewJobs()
      {
        Jobs = ImmutableList.Create<Job>(j),
        ScheduleId = "sch88"
      }, null, addAsCopiedToSystem: true);

      var trans = queues.CalculateScheduleChanges(_logDB, read.ToData());
      trans.Schedules.Should().BeEmpty();

      // put 2 assigned castings and three castings
      var mat1 = AddAssigned(uniq: "uuuu", part: "pppp", numProc: 1, lastProc: 0, path: 1, queue: "thequeue");
      var mat2 = AddAssigned(uniq: "uuuu", part: "pppp", numProc: 1, lastProc: 0, path: 1, queue: "thequeue");
      var mat3 = AddCasting("casting", "thequeue");
      var mat4 = AddCasting("casting", "thequeue");
      var mat5 = AddCasting("casting", "thequeue");

      _logDB.GetMaterialInAllQueues().Should().BeEquivalentTo(new[] {
            new QueuedMaterial() {
              MaterialID = mat1, Queue = "thequeue", Position = 0, Unique = "uuuu", PartNameOrCasting = "pppp", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
            new QueuedMaterial() {
              MaterialID = mat2, Queue = "thequeue", Position = 1, Unique = "uuuu", PartNameOrCasting = "pppp", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
            new QueuedMaterial() {
              MaterialID = mat3, Queue = "thequeue", Position = 2, Unique = "", PartNameOrCasting = "casting", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
              MaterialID = mat4, Queue = "thequeue", Position = 3, Unique = "", PartNameOrCasting = "casting", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
              MaterialID = mat5, Queue = "thequeue", Position = 4, Unique = "", PartNameOrCasting = "casting", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
          });

      // should allocate nothing because material is not zero, just update the process material quantity
      trans = queues.CalculateScheduleChanges(_logDB, read.ToData());

      _logDB.GetMaterialInAllQueues().Should().BeEquivalentTo(new[] {
            new QueuedMaterial() {
              MaterialID = mat1, Queue = "thequeue", Position = 0, Unique = "uuuu", PartNameOrCasting = "pppp", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
            new QueuedMaterial() {
              MaterialID = mat2, Queue = "thequeue", Position = 1, Unique = "uuuu", PartNameOrCasting = "pppp", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
            new QueuedMaterial() {
              MaterialID = mat3, Queue = "thequeue", Position = 2, Unique = "", PartNameOrCasting = "casting", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
              MaterialID = mat4, Queue = "thequeue", Position = 3, Unique = "", PartNameOrCasting = "casting", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
              MaterialID = mat5, Queue = "thequeue", Position = 4, Unique = "", PartNameOrCasting = "casting", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
          });

      trans.Schedules.Count.Should().Be(1);
      trans.Schedules[0].Priority.Should().Be(10);
      trans.Schedules[0].Id.Should().Be(10);
      trans.Schedules[0].Processes.Count.Should().Be(1);
      trans.Schedules[0].Processes[0].ProcessMaterialQuantity.Should().Be(2); // set the 2 already allocated
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IgnoreAllocateWhenNoRemaining(bool waitAll)
    {
      var queues = new MazakQueues(null, waitForAllCastings: waitAll);
      var read = new TestMazakData();

      // plan 50, 45 completed, and 5 in process.  So there are none remaining.
      var schRow = AddSchedule(read, schId: 10, unique: "uuuu", part: "pppp", numProc: 1, pri: 10, plan: 50, complete: 45);
      AddScheduleProcess(schRow, proc: 1, matQty: 0, exeQty: 5);

      var j = new Job()
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo() { InputQueue = "thequeue", Casting = "cccc" }) }
        )
      };
      _logDB.AddJobs(new NewJobs()
      {
        Jobs = ImmutableList.Create<Job>(j),
        ScheduleId = "sch99"
      }, null, addAsCopiedToSystem: true);

      // put 3 unassigned castings in queue
      var mat1 = AddCasting("cccc", "thequeue");
      var mat2 = AddCasting("cccc", "thequeue");
      var mat3 = AddCasting("cccc", "thequeue");

      _logDB.GetMaterialInAllQueues().Should().BeEquivalentTo(new[] {
            new QueuedMaterial() {
                  MaterialID = mat1, Queue = "thequeue", Position = 0, Unique = "", PartNameOrCasting = "cccc", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
                  MaterialID = mat2, Queue = "thequeue", Position = 1, Unique = "", PartNameOrCasting = "cccc", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
                  MaterialID = mat3, Queue = "thequeue", Position = 2, Unique = "", PartNameOrCasting = "cccc", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
          });

      // should allocate no parts and leave schedule unchanged.
      var trans = queues.CalculateScheduleChanges(_logDB, read.ToData());

      _logDB.GetMaterialInAllQueues().Should().BeEquivalentTo(new[] {
                new QueuedMaterial() {
                  MaterialID = mat1, Queue = "thequeue", Position = 0, Unique = "", PartNameOrCasting = "cccc", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
                new QueuedMaterial() {
                  MaterialID = mat2, Queue = "thequeue", Position = 1, Unique = "", PartNameOrCasting = "cccc", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
                new QueuedMaterial() {
                  MaterialID = mat3, Queue = "thequeue", Position = 2, Unique = "", PartNameOrCasting = "cccc", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
              });

      trans.Schedules.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DecrementJobWithoutQueue(bool waitAll)
    {
      var queues = new MazakQueues(null, waitForAllCastings: waitAll);
      var read = new TestMazakData();

      // this scenario comes from a job decrement.  At time of decrement, plan 50, 40 completed, 5 in proc, and 5 material
      // decrement reduces planned quantity to 45 but leaves material.  Queue code should clear it.
      var schRow = AddSchedule(read, schId: 10, unique: "uuuu", part: "pppp", numProc: 1, pri: 10, plan: 45, complete: 40);
      AddScheduleProcess(schRow, proc: 1, matQty: 5, exeQty: 5);

      var j = new Job()
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo() { }) }
        )
      };
      _logDB.AddJobs(new NewJobs()
      {
        Jobs = ImmutableList.Create<Job>(j),
        ScheduleId = "sch011"
      }, null, addAsCopiedToSystem: true);

      var trans = queues.CalculateScheduleChanges(_logDB, read.ToData());

      trans.Schedules.Count.Should().Be(1);
      trans.Schedules[0].Priority.Should().Be(10);
      trans.Schedules[0].Id.Should().Be(10);
      trans.Schedules[0].Processes.Count.Should().Be(1);
      trans.Schedules[0].Processes[0].ProcessMaterialQuantity.Should().Be(0); // clear the material
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(true, "mycasting")]
    [InlineData(false, null)]
    [InlineData(false, "mycasting")]
    public void DecrementJobWithQueue(bool waitAll, string casting)
    {
      var queues = new MazakQueues(null, waitForAllCastings: waitAll);
      var read = new TestMazakData();

      // this scenario occurs after a decrement.  The decrement sets the planned quantity to equal
      // the completed and in-process, but some material in the queue might be assigned to this job unique.
      // The queues code should unallocate or remove the queued material so it can be assigned to a new job.
      var schRow = AddSchedule(read, schId: 10, unique: "uuuu", part: "pppp", numProc: 1, pri: 10, plan: 46, complete: 43);
      AddScheduleProcess(schRow, proc: 1, matQty: 0, exeQty: 3);

      var j = new Job()
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo() { InputQueue = "thequeue", Casting = casting }) }
        )
      };
      if (casting == null)
      {
        casting = "pppp";
      }
      _logDB.AddJobs(new NewJobs()
      {
        Jobs = ImmutableList.Create<Job>(j),
        ScheduleId = "sch012",
      }, null, addAsCopiedToSystem: true);

      // put 2 assigned castings in queue
      var mat1 = AddAssigned(uniq: "uuuu", part: "pppp", numProc: 1, lastProc: 0, path: 1, queue: "thequeue");
      var mat2 = AddAssigned(uniq: "uuuu", part: "pppp", numProc: 1, lastProc: 0, path: 1, queue: "thequeue");

      _logDB.GetMaterialInAllQueues().Should().BeEquivalentTo(new[] {
                new QueuedMaterial() {
                  MaterialID = mat1, Queue = "thequeue", Position = 0, Unique = "uuuu", PartNameOrCasting = "pppp", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
                new QueuedMaterial() {
                  MaterialID = mat2, Queue = "thequeue", Position = 1, Unique = "uuuu", PartNameOrCasting = "pppp", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
              });

      // should not touch the schedule but unallocate or remove the two entries
      var trans = queues.CalculateScheduleChanges(_logDB, read.ToData());

      trans.Schedules.Should().BeEmpty();

      if (waitAll)
      {
        // when waiting for all, material is removed
        _logDB.GetMaterialInAllQueues().Should().BeEmpty();
      }
      else
      {
        // otherwise, material is just unallocated
        _logDB.GetMaterialInAllQueues().Should().BeEquivalentTo(new[] {
                  new QueuedMaterial() {
                    MaterialID = mat1, Queue = "thequeue", Position = 0, Unique = "", PartNameOrCasting = casting, NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
                  new QueuedMaterial() {
                    MaterialID = mat2, Queue = "thequeue", Position = 1, Unique = "", PartNameOrCasting = casting, NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
                });
      }
    }

    private void CreateMultiPathJobs(string casting = "mycasting", bool matchingPallets = false, bool matchingFixtures = false)
    {
      var j1 = new Job()
      {
        UniqueStr = "uuuu1",
        PartName = "pppp",
        Processes = ImmutableList.Create(
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(new ProcPathInfo()
            {
              InputQueue = "castingQ",
              Casting = casting,
              Pallets = matchingPallets ? ImmutableList.Create("1", "3") : ImmutableList.Create("1"),
              Fixture = matchingFixtures ? "fixA" : null,
              Face = matchingFixtures ? 10 : null,
            })
          },
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(new ProcPathInfo()
            {
              InputQueue = "transQ",
              Pallets = ImmutableList.Create("2"),
            })
          }
        )
      };

      var j2 = new Job()
      {
        UniqueStr = "uuuu2",
        PartName = "pppp",
        Processes = ImmutableList.Create(
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(new ProcPathInfo()
            {
              InputQueue = "castingQ",
              Casting = casting,
              Pallets = ImmutableList.Create("3"),
              Fixture = matchingFixtures ? "fixA" : null,
              Face = matchingFixtures ? 10 : null,
            })
          },
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(new ProcPathInfo()
            {
              InputQueue = "transQ",
              Pallets = ImmutableList.Create("4"),
            })
          }
        )
      };

      _logDB.AddJobs(new NewJobs()
      {
        Jobs = ImmutableList.Create<Job>(j1, j2),
        ScheduleId = "sch013"
      }, null, addAsCopiedToSystem: true);
    }

    [Fact]
    public void MultiplePathsAddMaterial()
    {
      var queues = new MazakQueues(null, waitForAllCastings: false);
      var read = new TestMazakData();

      // path 1-2
      //   - plan 70
      //   - complete 10
      //   - proc1: 6 in execution, 0 material in mazak
      //   - proc2: 3 in execution, 1 material in mazak
      var schRow1 = AddSchedule(read,
        schId: 10, unique: "uuuu1", part: "pppp", numProc: 2, pri: 10, plan: 70, complete: 10,
        partIdx: 1); // paths are twisted
      AddScheduleProcess(schRow1, proc: 1, matQty: 0, exeQty: 6, fixQty: 2);
      AddScheduleProcess(schRow1, proc: 2, matQty: 1, exeQty: 3);
      AddAssigned(uniq: "uuuu1", part: "pppp", numProc: 2, lastProc: 1, path: 1, queue: "transQ");

      //path 2-1
      //   - plan 80
      //   - complete 5
      //   - proc1: 4 in execution, 0 material in mazak
      //   - proc2: 3 in execution, 1 material in mazak
      var schRow2 = AddSchedule(read,
        schId: 11, unique: "uuuu2", part: "pppp", numProc: 2, pri: 10, plan: 80, complete: 5,
        partIdx: 2); // paths are twisted
      AddScheduleProcess(schRow2, proc: 1, matQty: 1, exeQty: 4, fixQty: 2);
      AddScheduleProcess(schRow2, proc: 2, matQty: 0, exeQty: 3);
      AddAssigned(uniq: "uuuu2", part: "pppp", numProc: 2, lastProc: 0, path: 1, queue: "castingQ");

      CreateMultiPathJobs();

      AddAssigned(uniq: "xxxx", part: "pppp", numProc: 2, lastProc: 1, path: 1, queue: "transQ");

      var trans = queues.CalculateScheduleChanges(_logDB, read.ToData());
      trans.Schedules.Should().BeEmpty();

      // add 2 more to uuuu1 proc 1
      for (int i = 0; i < 2; i++)
        AddAssigned(uniq: "uuuu1", part: "pppp", numProc: 2, lastProc: 0, path: 1, queue: "castingQ");

      // add 5 more to uuuu1 proc 2
      for (int i = 0; i < 5; i++)
        AddAssigned(uniq: "uuuu1", part: "pppp", numProc: 2, lastProc: 1, path: 1, queue: "transQ");

      // add 10 more to uuuu 2 proc 1
      for (int i = 0; i < 10; i++)
        AddAssigned(uniq: "uuuu2", part: "pppp", numProc: 2, lastProc: 0, path: 1, queue: "castingQ");

      // add 15 more to uuuu 2 proc 2
      for (int i = 0; i < 15; i++)
        AddAssigned(uniq: "uuuu2", part: "pppp", numProc: 2, lastProc: 1, path: 1, queue: "transQ");


      trans = queues.CalculateScheduleChanges(_logDB, read.ToData());

      trans.Schedules.Count.Should().Be(2);
      trans.Schedules.Select(s => s.Id).Should().BeEquivalentTo(new[] { 10, 11 });
      trans.Schedules[0].Priority.Should().Be(10);
      var path1Rows = trans.Schedules[0].Processes;
      path1Rows.Count().Should().Be(2);
      path1Rows[0].ProcessNumber.Should().Be(1);
      path1Rows[0].MazakScheduleRowId.Should().Be(10);
      path1Rows[0].ProcessMaterialQuantity.Should().Be(0 + 2);
      path1Rows[1].ProcessNumber.Should().Be(2);
      path1Rows[1].MazakScheduleRowId.Should().Be(10);
      path1Rows[1].ProcessMaterialQuantity.Should().Be(1 + 5);

      trans.Schedules[1].Priority.Should().Be(10);
      var path2Rows = trans.Schedules[1].Processes;
      path2Rows.Count().Should().Be(2);
      path2Rows[0].ProcessNumber.Should().Be(1);
      path2Rows[0].MazakScheduleRowId.Should().Be(11);
      path2Rows[0].ProcessMaterialQuantity.Should().Be(1 + 10);
      path2Rows[1].ProcessNumber.Should().Be(2);
      path2Rows[1].MazakScheduleRowId.Should().Be(11);
      path2Rows[1].ProcessMaterialQuantity.Should().Be(0 + 15);
    }

    [Fact]
    public void MultiplePathsRemoveMaterial()
    {
      var queues = new MazakQueues(null, waitForAllCastings: false);
      var read = new TestMazakData();

      // path 1
      //   - plan 30
      //   - complete 10
      //   - proc1: 6 in execution, 4 material in mazak
      //   - proc2: 3 in execution, 7 material in mazak
      var schRow1 = AddSchedule(read,
        schId: 10, unique: "uuuu1", part: "pppp", numProc: 2, pri: 10, plan: 30, complete: 10,
        partIdx: 1); // paths are twisted
      AddScheduleProcess(schRow1, proc: 1, matQty: 4, exeQty: 6, fixQty: 2);
      AddScheduleProcess(schRow1, proc: 2, matQty: 7, exeQty: 3);

      var proc1path1 = Enumerable.Range(0, 4).Select(i =>
        AddAssigned(uniq: "uuuu1", part: "pppp", numProc: 2, lastProc: 0, path: 1, queue: "castingQ")
      ).ToList();
      var proc2path1 = Enumerable.Range(0, 7).Select(i =>
        AddAssigned(uniq: "uuuu1", part: "pppp", numProc: 2, lastProc: 1, path: 1, queue: "transQ")
      ).ToList();

      //path 2
      //   - plan 50
      //   - complete 5
      //   - proc1: 4 in execution, 2 material in mazak
      //   - proc2: 3 in execution, 9 material in mazak
      var schRow2 = AddSchedule(read,
        schId: 11, unique: "uuuu2", part: "pppp", numProc: 2, pri: 10, plan: 50, complete: 5,
        partIdx: 2); // paths are twisted
      AddScheduleProcess(schRow2, proc: 1, matQty: 2, exeQty: 4, fixQty: 2);
      AddScheduleProcess(schRow2, proc: 2, matQty: 9, exeQty: 3);

      var proc1path2 = Enumerable.Range(0, 2).Select(i =>
        AddAssigned(uniq: "uuuu2", part: "pppp", numProc: 2, lastProc: 0, path: 1, queue: "castingQ")
      ).ToList();
      var proc2path2 = Enumerable.Range(0, 9).Select(i =>
        AddAssigned(uniq: "uuuu2", part: "pppp", numProc: 2, lastProc: 1, path: 1, queue: "transQ")
      ).ToList();

      CreateMultiPathJobs();

      var trans = queues.CalculateScheduleChanges(_logDB, read.ToData());
      trans.Schedules.Should().BeEmpty();

      // now remove some material
      _logDB.RecordRemoveMaterialFromAllQueues(proc1path1[0], 1);
      _logDB.RecordRemoveMaterialFromAllQueues(proc2path2[0], 2);
      _logDB.RecordRemoveMaterialFromAllQueues(proc2path2[1], 2);

      trans = queues.CalculateScheduleChanges(_logDB, read.ToData());

      trans.Schedules.Count.Should().Be(2);
      trans.Schedules.Select(s => s.Id).Should().BeEquivalentTo(new[] { 10, 11 });

      trans = queues.CalculateScheduleChanges(_logDB, read.ToData());

      trans.Schedules.Count.Should().Be(2);
      trans.Schedules.Select(s => s.Id).Should().BeEquivalentTo(new[] { 10, 11 });
      trans.Schedules[0].Priority.Should().Be(10);
      var path1Rows = trans.Schedules[0].Processes;
      path1Rows.Count().Should().Be(2);
      path1Rows[0].MazakScheduleRowId.Should().Be(10);
      path1Rows[0].ProcessNumber.Should().Be(1);
      path1Rows[0].ProcessMaterialQuantity.Should().Be(4 - 1);
      path1Rows[1].ProcessNumber.Should().Be(2);
      path1Rows[1].MazakScheduleRowId.Should().Be(10);
      path1Rows[1].ProcessMaterialQuantity.Should().Be(7); // unchanged

      trans.Schedules[1].Priority.Should().Be(10);
      var path2Rows = trans.Schedules[1].Processes;
      path2Rows.Count().Should().Be(2);
      path2Rows[0].MazakScheduleRowId.Should().Be(11);
      path2Rows[0].ProcessNumber.Should().Be(1);
      path2Rows[0].ProcessMaterialQuantity.Should().Be(2); // unchanged
      path2Rows[1].ProcessNumber.Should().Be(2);
      path2Rows[1].MazakScheduleRowId.Should().Be(11);
      path2Rows[1].ProcessMaterialQuantity.Should().Be(9 - 2); // remove two
    }

    [Theory]
    [InlineData(null)]
    [InlineData("mycasting")]
    public void AddCastingsByFixedQuantityMultiPath(string casting)
    {
      var queues = new MazakQueues(null, waitForAllCastings: false);
      var read = new TestMazakData();

      // path 1
      //   - plan 30
      //   - complete 10
      //   - proc1: 6 in execution, 0 material in mazak
      //   - proc2: 3 in execution, 3 material in mazak
      var schRow1 = AddSchedule(read,
        schId: 10, unique: "uuuu1", part: "pppp", numProc: 2, pri: 10, plan: 30, complete: 10,
        partIdx: 1);
      AddScheduleProcess(schRow1, proc: 1, matQty: 0, exeQty: 6, fixQty: 2);
      AddScheduleProcess(schRow1, proc: 2, matQty: 3, exeQty: 3);

      var proc2path1 = Enumerable.Range(0, 3).Select(i =>
        AddAssigned(uniq: "uuuu1", part: "pppp", numProc: 2, lastProc: 1, path: 1, queue: "transQ")
      ).ToList();

      //path 2
      //   - plan 20
      //   - complete 5
      //   - proc1: 2 in execution, 0 material in mazak
      //   - proc2: 3 in execution, 6 material in mazak
      //   - thus 20 - 5 - 2 - 3 - 6 = 4 not yet assigned
      var schRow2 = AddSchedule(read,
        schId: 11, unique: "uuuu2", part: "pppp", numProc: 2, pri: 8, plan: 20, complete: 5,
        partIdx: 2);
      AddScheduleProcess(schRow2, proc: 1, matQty: 0, exeQty: 2, fixQty: 2);
      AddScheduleProcess(schRow2, proc: 2, matQty: 6, exeQty: 3);

      var proc2path2 = Enumerable.Range(0, 6).Select(i =>
        AddAssigned(uniq: "uuuu2", part: "pppp", numProc: 2, lastProc: 1, path: 1, queue: "transQ")
      ).ToList();

      CreateMultiPathJobs(casting);
      if (casting == null) casting = "pppp";


      // put a different casting
      var mat0 = AddCasting("unused", "castingQ");

      var trans = queues.CalculateScheduleChanges(_logDB, read.ToData());
      trans.Schedules.Should().BeEmpty();

      // put 3 unassigned castings in queue
      var mat1 = AddCasting(casting, "castingQ");
      var mat2 = AddCasting(casting, "castingQ");
      var mat3 = AddCasting(casting, "castingQ");

      _logDB.GetMaterialInAllQueues().Where(m => m.Queue == "castingQ").Should().BeEquivalentTo(new[] {
            new QueuedMaterial() {
              MaterialID = mat0, Queue = "castingQ", Position = 0, Unique = "", PartNameOrCasting = "unused", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
              MaterialID = mat1, Queue = "castingQ", Position = 1, Unique = "", PartNameOrCasting = casting, NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
              MaterialID = mat2, Queue = "castingQ", Position = 2, Unique = "", PartNameOrCasting = casting, NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
              MaterialID = mat3, Queue = "castingQ", Position = 3, Unique = "", PartNameOrCasting = casting, NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
          });

      // should allocate 2 (fixqty) parts to path 2 (lowest priority)
      trans = queues.CalculateScheduleChanges(_logDB, read.ToData());

      _logDB.GetMaterialInAllQueues().Where(m => m.Queue == "castingQ").Should().BeEquivalentTo(new[] {
            new QueuedMaterial() {
              MaterialID = mat0, Queue = "castingQ", Position = 0, Unique = "", PartNameOrCasting = "unused", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
              MaterialID = mat1, Queue = "castingQ", Position = 1, Unique = "uuuu2", PartNameOrCasting = "pppp", NumProcesses = 2, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
            new QueuedMaterial() {
              MaterialID = mat2, Queue = "castingQ", Position = 2, Unique = "uuuu2", PartNameOrCasting = "pppp", NumProcesses = 2, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
            new QueuedMaterial() {
              MaterialID = mat3, Queue = "castingQ", Position = 3, Unique = "", PartNameOrCasting = casting, NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
          });
      _logDB.GetMaterialDetails(mat1).Paths.Should().BeEquivalentTo(new Dictionary<int, int>() { { 1, 1 } });
      _logDB.GetMaterialDetails(mat2).Paths.Should().BeEquivalentTo(new Dictionary<int, int>() { { 1, 1 } });

      trans.Schedules.Count.Should().Be(1);
      trans.Schedules[0].Priority.Should().Be(8);
      var path2Rows = trans.Schedules[0].Processes;
      path2Rows.Count().Should().Be(2);
      path2Rows[0].MazakScheduleRowId.Should().Be(11);
      path2Rows[0].ProcessNumber.Should().Be(1);
      path2Rows[0].ProcessMaterialQuantity.Should().Be(2); // new parts
      path2Rows[1].ProcessNumber.Should().Be(2);
      path2Rows[1].MazakScheduleRowId.Should().Be(11);
      path2Rows[1].ProcessMaterialQuantity.Should().Be(6); // unchanged

      // now add some more castings.  Should add fixqty (2) to first path since second path already has some material
      var mat4 = AddCasting(casting, "castingQ");
      var mat5 = AddCasting(casting, "castingQ");

      trans = queues.CalculateScheduleChanges(_logDB, read.ToData());

      _logDB.GetMaterialInAllQueues().Where(m => m.Queue == "castingQ").Should().BeEquivalentTo(new[] {
            new QueuedMaterial() {
              MaterialID = mat0, Queue = "castingQ", Position = 0, Unique = "", PartNameOrCasting = "unused", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
              MaterialID = mat1, Queue = "castingQ", Position = 1, Unique = "uuuu2", PartNameOrCasting = "pppp", NumProcesses = 2, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
            new QueuedMaterial() {
              MaterialID = mat2, Queue = "castingQ", Position = 2, Unique = "uuuu2", PartNameOrCasting = "pppp", NumProcesses = 2, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
            new QueuedMaterial() {
              MaterialID = mat3, Queue = "castingQ", Position = 3, Unique = "uuuu1", PartNameOrCasting = "pppp", NumProcesses = 2, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
            new QueuedMaterial() {
              MaterialID = mat4, Queue = "castingQ", Position = 4, Unique = "uuuu1", PartNameOrCasting = "pppp", NumProcesses = 2, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
            new QueuedMaterial() {
              MaterialID = mat5, Queue = "castingQ", Position = 5, Unique = "", PartNameOrCasting = casting, NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
          });
      _logDB.GetMaterialDetails(mat3).Paths.Should().BeEquivalentTo(new Dictionary<int, int>() { { 1, 1 } });
      _logDB.GetMaterialDetails(mat4).Paths.Should().BeEquivalentTo(new Dictionary<int, int>() { { 1, 1 } });

      trans.Schedules.Count.Should().Be(2);
      trans.Schedules.Select(s => s.Id).Should().BeEquivalentTo(new[] { 10, 11 });
      trans.Schedules[0].Priority.Should().Be(8);
      var path1Rows = trans.Schedules[1].Processes;
      path1Rows.Count().Should().Be(2);
      path1Rows[0].MazakScheduleRowId.Should().Be(10);
      path1Rows[0].ProcessNumber.Should().Be(1);
      path1Rows[0].ProcessMaterialQuantity.Should().Be(2); // 2 new parts
      path1Rows[1].ProcessNumber.Should().Be(2);
      path1Rows[1].MazakScheduleRowId.Should().Be(10);
      path1Rows[1].ProcessMaterialQuantity.Should().Be(3); // unchanged

      trans.Schedules[1].Priority.Should().Be(10);
      path2Rows = trans.Schedules[0].Processes;
      path2Rows.Count().Should().Be(2);
      path2Rows[0].MazakScheduleRowId.Should().Be(11);
      path2Rows[0].ProcessNumber.Should().Be(1);
      path2Rows[0].ProcessMaterialQuantity.Should().Be(2); // set to 2 existing allocated
      path2Rows[1].ProcessNumber.Should().Be(2);
      path2Rows[1].MazakScheduleRowId.Should().Be(11);
      path2Rows[1].ProcessMaterialQuantity.Should().Be(6); // unchanged
    }

    [Theory]
    [InlineData(null)]
    [InlineData("mycasting")]
    public void AddCastingsByFullScheduleMultiPath(string casting)
    {
      var queues = new MazakQueues(null, waitForAllCastings: true);
      var read = new TestMazakData();

      // path 1
      //   - plan 24
      //   - complete 10
      //   - proc1: 6 in execution, 0 material in mazak
      //   - proc2: 3 in execution, 3 material in mazak
      //     24 - 10 - 6 - 3 - 3 = 2 remaining
      var schRow1 = AddSchedule(read,
        schId: 10, unique: "uuuu1", part: "pppp", numProc: 2, pri: 10, plan: 24, complete: 10,
        partIdx: 1); // paths are twisted
      AddScheduleProcess(schRow1, proc: 1, matQty: 0, exeQty: 6, fixQty: 2);
      AddScheduleProcess(schRow1, proc: 2, matQty: 3, exeQty: 3);

      var proc2path2 = Enumerable.Range(0, 3).Select(i =>
        AddAssigned(uniq: "uuuu1", part: "pppp", numProc: 2, lastProc: 1, path: 1, queue: "transQ")
      ).ToList();

      //path 2
      //   - plan 20
      //   - complete 5
      //   - proc1: 2 in execution, 0 material in mazak
      //   - proc2: 3 in execution, 6 material in mazak
      //   - thus 20 - 5 - 2 - 3 - 6 = 4 not yet assigned
      var schRow2 = AddSchedule(read,
        schId: 11, unique: "uuuu2", part: "pppp", numProc: 2, pri: 8, plan: 20, complete: 5,
        partIdx: 2);
      AddScheduleProcess(schRow2, proc: 1, matQty: 0, exeQty: 2, fixQty: 2);
      AddScheduleProcess(schRow2, proc: 2, matQty: 6, exeQty: 3);

      var proc2path1 = Enumerable.Range(0, 6).Select(i =>
        AddAssigned(uniq: "uuuu2", part: "pppp", numProc: 2, lastProc: 1, path: 1, queue: "transQ")
      ).ToList();

      CreateMultiPathJobs(casting);
      if (casting == null) casting = "pppp";


      // put a different casting
      var mat0 = AddCasting("unused", "castingQ");

      var trans = queues.CalculateScheduleChanges(_logDB, read.ToData());
      trans.Schedules.Should().BeEmpty();

      // put 3 unassigned castings in queue which is not enough for path 2 (but is enough for path 1)
      var mat1 = AddCasting(casting, "castingQ");
      var mat2 = AddCasting(casting, "castingQ");
      var mat3 = AddCasting(casting, "castingQ");

      _logDB.GetMaterialInAllQueues().Where(m => m.Queue == "castingQ").Should().BeEquivalentTo(new[] {
            new QueuedMaterial() {
              MaterialID = mat0, Queue = "castingQ", Position = 0, Unique = "", PartNameOrCasting = "unused", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
              MaterialID = mat1, Queue = "castingQ", Position = 1, Unique = "", PartNameOrCasting = casting, NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
              MaterialID = mat2, Queue = "castingQ", Position = 2, Unique = "", PartNameOrCasting = casting, NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
              MaterialID = mat3, Queue = "castingQ", Position = 3, Unique = "", PartNameOrCasting = casting, NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
          });

      trans = queues.CalculateScheduleChanges(_logDB, read.ToData());
      trans.Schedules.Should().BeEmpty();

      // two more, which gives enough for path 2
      var mat4 = AddCasting(casting, "castingQ");
      var mat5 = AddCasting(casting, "castingQ");

      trans = queues.CalculateScheduleChanges(_logDB, read.ToData());

      _logDB.GetMaterialInAllQueues().Where(m => m.Queue == "castingQ").Should().BeEquivalentTo(new[] {
            new QueuedMaterial() {
              MaterialID = mat0, Queue = "castingQ", Position = 0, Unique = "", PartNameOrCasting = "unused", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
            new QueuedMaterial() {
              MaterialID = mat1, Queue = "castingQ", Position = 1, Unique = "uuuu2", PartNameOrCasting = "pppp", NumProcesses = 2, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
            new QueuedMaterial() {
              MaterialID = mat2, Queue = "castingQ", Position = 2, Unique = "uuuu2", PartNameOrCasting = "pppp", NumProcesses = 2, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
            new QueuedMaterial() {
              MaterialID = mat3, Queue = "castingQ", Position = 3, Unique = "uuuu2", PartNameOrCasting = "pppp", NumProcesses = 2, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
            new QueuedMaterial() {
              MaterialID = mat4, Queue = "castingQ", Position = 4, Unique = "uuuu2", PartNameOrCasting = "pppp", NumProcesses = 2, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
            new QueuedMaterial() {
              MaterialID = mat5, Queue = "castingQ", Position = 5, Unique = "", PartNameOrCasting = casting, NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
          });
      _logDB.GetMaterialDetails(mat1).Paths.Should().BeEquivalentTo(new Dictionary<int, int>() { { 1, 1 } });
      _logDB.GetMaterialDetails(mat2).Paths.Should().BeEquivalentTo(new Dictionary<int, int>() { { 1, 1 } });
      _logDB.GetMaterialDetails(mat3).Paths.Should().BeEquivalentTo(new Dictionary<int, int>() { { 1, 1 } });
      _logDB.GetMaterialDetails(mat4).Paths.Should().BeEquivalentTo(new Dictionary<int, int>() { { 1, 1 } });

      trans.Schedules.Count.Should().Be(1);
      trans.Schedules[0].Priority.Should().Be(8); // the schedules use different pallets, so priority should not be increased
      var path2Rows = trans.Schedules[0].Processes;
      path2Rows.Count().Should().Be(2);
      path2Rows[0].MazakScheduleRowId.Should().Be(11);
      path2Rows[0].ProcessNumber.Should().Be(1);
      path2Rows[0].ProcessMaterialQuantity.Should().Be(4); // new parts
      path2Rows[1].ProcessNumber.Should().Be(2);
      path2Rows[1].MazakScheduleRowId.Should().Be(11);
      path2Rows[1].ProcessMaterialQuantity.Should().Be(6); // unchanged
    }

    [Theory]
    [InlineData(null, true, false)]
    [InlineData(null, false, true)]
    [InlineData("mycasting", true, false)]
    [InlineData("mycasting", false, true)]
    public void AdjustsPriorityWhenAddingCastings(string casting, bool matchPallet, bool matchFixture)
    {
      var queues = new MazakQueues(null, waitForAllCastings: true);
      var read = new TestMazakData();

      // path 1
      //   - plan 24
      //   - complete 10
      //   - proc1: 6 in execution, 0 material in mazak
      //   - proc2: 3 in execution, 3 material in mazak
      //     24 - 10 - 6 - 3 - 3 = 2 remaining
      var schRow1 = AddSchedule(read,
        schId: 10, unique: "uuuu1", part: "pppp", numProc: 2, pri: 10, plan: 24, complete: 10,
        partIdx: 1,
        dueDate: new DateTime(2020, 04, 23)
      );
      AddScheduleProcess(schRow1, proc: 1, matQty: 0, exeQty: 6, fixQty: 2);
      AddScheduleProcess(schRow1, proc: 2, matQty: 3, exeQty: 3);

      var proc2path1 = Enumerable.Range(0, 3).Select(i =>
        AddAssigned(uniq: "uuuu1", part: "pppp", numProc: 2, lastProc: 1, path: 1, queue: "transQ")
      ).ToList();

      //path 2
      //   - plan 20
      //   - complete 5
      //   - proc1: 2 in execution, 0 material in mazak
      //   - proc2: 3 in execution, 6 material in mazak
      //   - thus 20 - 5 - 2 - 3 - 6 = 4 not yet assigned
      var schRow2 = AddSchedule(read,
        schId: 11, unique: "uuuu2", part: "pppp", numProc: 2, pri: 8, plan: 20, complete: 5,
        partIdx: 2, // paths are twisted
        dueDate: new DateTime(2020, 04, 22)
      );
      AddScheduleProcess(schRow2, proc: 1, matQty: 0, exeQty: 2, fixQty: 2);
      AddScheduleProcess(schRow2, proc: 2, matQty: 6, exeQty: 3);

      var proc2path2 = Enumerable.Range(0, 6).Select(i =>
        AddAssigned(uniq: "uuuu2", part: "pppp", numProc: 2, lastProc: 1, path: 1, queue: "transQ")
      ).ToList();

      CreateMultiPathJobs(casting, matchPallet, matchFixture);
      if (casting == null) casting = "pppp";

      var mat1 = AddCasting(casting, "castingQ");
      var mat2 = AddCasting(casting, "castingQ");
      var mat3 = AddCasting(casting, "castingQ");
      var mat4 = AddCasting(casting, "castingQ");
      var mat5 = AddCasting(casting, "castingQ");

      var trans = queues.CalculateScheduleChanges(_logDB, read.ToData());

      trans.Schedules.Count.Should().Be(1);
      trans.Schedules[0].Priority.Should().Be(11); // schedule has priority increased from 8 to 11

      var path2Rows = trans.Schedules[0].Processes;
      path2Rows.Count().Should().Be(2);
      path2Rows[0].MazakScheduleRowId.Should().Be(11);
      path2Rows[0].ProcessNumber.Should().Be(1);
      path2Rows[0].ProcessMaterialQuantity.Should().Be(4); // new parts
      path2Rows[1].ProcessNumber.Should().Be(2);
      path2Rows[1].MazakScheduleRowId.Should().Be(11);
      path2Rows[1].ProcessMaterialQuantity.Should().Be(6); // unchanged
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SkipsWhenAtLoad(bool waitAll)
    {
      var queues = new MazakQueues(null, waitForAllCastings: waitAll);
      var read = new TestMazakData();
      var schRow = AddSchedule(read, schId: 10, unique: "uuuu", part: "pppp", numProc: 1, pri: 10, plan: 50, complete: 40);
      AddScheduleProcess(schRow, proc: 1, matQty: 0, exeQty: 5);

      var j = new Job()
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo() { InputQueue = "thequeue" }) }
        )
      };
      _logDB.AddJobs(new NewJobs()
      {
        Jobs = ImmutableList.Create<Job>(j),
        ScheduleId = "sch014"
      }, null, addAsCopiedToSystem: true);

      // put 1 castings in queue
      var matId = AddAssigned(uniq: "uuuu", part: "pppp", numProc: 1, lastProc: 0, path: 1, queue: "thequeue");

      //put something else at load station
      var action = new LoadAction(true, 1, "pppp", MazakPart.CreateComment("uuuu", new[] { 1 }, false), 1, 1);
      read.LoadActions.Add(action);

      var trans = queues.CalculateScheduleChanges(_logDB, read.ToData());
      trans.Should().BeNull();

      _logDB.GetMaterialInAllQueues().Should().BeEquivalentTo(new[] {
        new QueuedMaterial() {
              MaterialID = matId, Queue = "thequeue", Position = 0, Unique = "uuuu", PartNameOrCasting = "pppp", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
      });

      read.LoadActions.Clear();

      trans = queues.CalculateScheduleChanges(_logDB, read.ToData());

      if (waitAll)
      {
        // wait all removes the material
        trans.Schedules.Should().BeEmpty();
        _logDB.GetMaterialInAllQueues().Should().BeEmpty();
      }
      else
      {
        // not wait all sets the job
        _logDB.GetMaterialInAllQueues().Should().BeEquivalentTo(new[] {
          new QueuedMaterial() {
                MaterialID = matId, Queue = "thequeue", Position = 0, Unique = "uuuu", PartNameOrCasting = "pppp", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
        });
        trans.Schedules.Count.Should().Be(1);
        trans.Schedules[0].Priority.Should().Be(10);
        trans.Schedules[0].Id.Should().Be(10);
        trans.Schedules[0].Processes.Count.Should().Be(1);
        trans.Schedules[0].Processes[0].ProcessNumber.Should().Be(1);
        trans.Schedules[0].Processes[0].ProcessMaterialQuantity.Should().Be(1);
      }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SkipsWhenExistsPendingLoad(bool waitAll)
    {
      var queues = new MazakQueues(null, waitForAllCastings: waitAll);
      var read = new TestMazakData();
      var schRow = AddSchedule(read, schId: 10, unique: "uuuu", part: "pppp", numProc: 1, pri: 10, plan: 50, complete: 40);
      AddScheduleProcess(schRow, proc: 1, matQty: 0, exeQty: 5);

      var j = new Job()
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo() { InputQueue = "thequeue" }) }
        )
      };
      _logDB.AddJobs(new NewJobs()
      {
        Jobs = ImmutableList.Create<Job>(j),
        ScheduleId = "sch015"
      }, null, addAsCopiedToSystem: true);

      // put 1 castings in queue
      var matId = AddAssigned(uniq: "uuuu", part: "pppp", numProc: 1, lastProc: 0, path: 1, queue: "thequeue");

      //add a pending load
      _logDB.AddPendingLoad("pal1", "pppp:10:1,unused", load: 5, elapsed: TimeSpan.FromMinutes(2), active: TimeSpan.FromMinutes(3), foreignID: null);

      var trans = queues.CalculateScheduleChanges(_logDB, read.ToData());
      trans.Should().BeNull();

      _logDB.CompletePalletCycle("pal1", DateTime.UtcNow, "",
        new Dictionary<string, IEnumerable<EventLogMaterial>>() {
          {"pppp:10:1,unused", new EventLogMaterial[] {}}
        }, false
      );

      _logDB.GetMaterialInAllQueues().Should().BeEquivalentTo(new[] {
        new QueuedMaterial() {
              MaterialID = matId, Queue = "thequeue", Position = 0, Unique = "uuuu", PartNameOrCasting = "pppp", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
      });


      trans = queues.CalculateScheduleChanges(_logDB, read.ToData());

      if (waitAll)
      {
        // wait all removes the material
        trans.Schedules.Should().BeEmpty();
        _logDB.GetMaterialInAllQueues().Should().BeEmpty();
      }
      else
      {
        // not wait all sets the job
        _logDB.GetMaterialInAllQueues().Should().BeEquivalentTo(new[] {
          new QueuedMaterial() {
                MaterialID = matId, Queue = "thequeue", Position = 0, Unique = "uuuu", PartNameOrCasting = "pppp", NumProcesses = 1, AddTimeUTC = _now,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
        });
        trans.Schedules.Count.Should().Be(1);
        trans.Schedules[0].Priority.Should().Be(10);
        trans.Schedules[0].Id.Should().Be(10);
        trans.Schedules[0].Processes.Count.Should().Be(1);
        trans.Schedules[0].Processes[0].ProcessNumber.Should().Be(1);
        trans.Schedules[0].Processes[0].ProcessMaterialQuantity.Should().Be(1);
      }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("mycasting")]
    public void AllocateToMultipleSchedulesByPriority(string casting)
    {
      var queues = new MazakQueues(null, waitForAllCastings: false);
      var read = new TestMazakData();
      var schRow1 = AddSchedule(read, schId: 10, unique: "uuu1", part: "pppp", numProc: 1, pri: 10, plan: 15, complete: 0);
      AddScheduleProcess(schRow1, proc: 1, matQty: 0, exeQty: 0);

      //sch2 has lower priority so should be allocated to first
      var schRow2 = AddSchedule(read, schId: 11, unique: "uuu2", part: "pppp", numProc: 1, pri: 8, plan: 50, complete: 40);
      AddScheduleProcess(schRow2, proc: 1, matQty: 0, exeQty: 5);

      var j1 = new Job()
      {
        UniqueStr = "uuu1",
        PartName = "pppp",
        RouteStartUTC = DateTime.UtcNow.AddHours(-2),
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo() { InputQueue = "thequeue", Casting = casting }) }
        )
      };
      var j2 = new Job()
      {
        UniqueStr = "uuu2",
        PartName = "pppp",
        RouteStartUTC = DateTime.UtcNow.AddHours(-5),
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo() { InputQueue = "thequeue", Casting = casting }) }
        )
      };
      if (casting == null)
      {
        casting = "pppp";
      }
      _logDB.AddJobs(new NewJobs()
      {
        Jobs = ImmutableList.Create<Job>(j1, j2),
        ScheduleId = "sch016"
      }, null, addAsCopiedToSystem: true);


      //put something at the load station for uuu2
      var action = new LoadAction(true, 1, "pppp", MazakPart.CreateComment("uuu2", new[] { 1 }, false), 1, 1);
      read.LoadActions.Add(action);

      var mat1 = AddCasting(casting, "thequeue");

      _logDB.GetMaterialInAllQueues().Should().BeEquivalentTo(new[] {
                    new QueuedMaterial() {
                      MaterialID = mat1, Queue = "thequeue", Position = 0, Unique = "", PartNameOrCasting = casting, NumProcesses = 1, AddTimeUTC = _now,
                      NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
                  });

      var trans = queues.CalculateScheduleChanges(_logDB, read.ToData());
      trans.Schedules.Should().BeEmpty();

      _logDB.GetMaterialInAllQueues().Should().BeEquivalentTo(new[] {
                    new QueuedMaterial() {
                      MaterialID = mat1, Queue = "thequeue", Position = 0, Unique = "", PartNameOrCasting = casting, NumProcesses = 1, AddTimeUTC = _now,
                      NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            },
                  });

      // now remove the action
      read.LoadActions.Clear();
      trans = queues.CalculateScheduleChanges(_logDB, read.ToData());

      _logDB.GetMaterialInAllQueues().Should().BeEquivalentTo(new[] {
                    new QueuedMaterial() {
                      MaterialID = mat1, Queue = "thequeue", Position = 0, Unique = "uuu2", PartNameOrCasting = "pppp", NumProcesses = 1, AddTimeUTC = _now,
                      NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1)
            },
                  });

      trans.Schedules.Count.Should().Be(1);
      trans.Schedules[0].Priority.Should().Be(8);
      trans.Schedules[0].Id.Should().Be(11);
      trans.Schedules[0].Processes.Count.Should().Be(1);
      trans.Schedules[0].Processes[0].ProcessMaterialQuantity.Should().Be(1);
    }

  }
}