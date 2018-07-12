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
using Xunit;
using FluentAssertions;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;
using MazakMachineInterface;

namespace MachineWatchTest
{
  public class QueueSpec : IDisposable
  {
    private JobLogDB _logDB;
		private JobDB _jobDB;
    private MazakQueues _queues;

    public QueueSpec()
    {
      var logConn = BlackMaple.MachineFramework.SqliteExtensions.ConnectMemory();
      logConn.Open();
      _logDB = new JobLogDB(logConn);
      _logDB.CreateTables();

      var jobConn = BlackMaple.MachineFramework.SqliteExtensions.ConnectMemory();
      jobConn.Open();
      _jobDB = new JobDB(jobConn);
      _jobDB.CreateTables();

      _queues = new MazakQueues(_logDB, _jobDB, null, null);
    }

		public void Dispose()
		{
			_logDB.Close();
			_jobDB.Close();
		}

    [Fact]
    public void Empty()
    {
      var trans = _queues.CalculateScheduleChanges(new ReadOnlyDataSet(), new LoadAction[] {});
      trans.Should().BeNull();
    }

    private ReadOnlyDataSet.ScheduleRow AddSchedule(ReadOnlyDataSet read, int schId, string unique, string part, int pri, int complete, int plan)
    {
      return read.Schedule.AddScheduleRow(
        Comment: MazakPart.CreateComment(unique, 1, false),
        CompleteQuantity: complete,
        DueDate: DateTime.UtcNow.AddHours(pri),
        FixForMachine: 1,
        HoldMode: 0,
        MissingFixture: 0,
        MissingProgram: 0,
        MissingTool: 0,
        MixScheduleID: 1,
        PartName: part + ":10:1",
        PlanQuantity: plan,
        Priority: pri,
        ProcessingPriority: 1,
        ScheduleID: schId,
        UpdatedFlag: 1
      );
    }

    private void AddScheduleProcess(ReadOnlyDataSet.ScheduleRow schRow, int proc, int matQty, int exeQty)
    {
      ((ReadOnlyDataSet)schRow.Table.DataSet).ScheduleProcess.AddScheduleProcessRow(
        ID: 1000,
        ProcessBadQuantity: 0,
        ProcessExecuteQuantity: exeQty,
        ProcessMachine: 1,
        ProcessMaterialQuantity: matQty,
        ProcessNumber: proc,
        parentScheduleRowByScheduleRelation: schRow,
        UpdatedFlag: 0
      );
    }

    [Fact]
    public void OneScheduleWithCasting()
    {
      var read = new ReadOnlyDataSet();

      // plan 50, 40 completed, and 5 in process.  So there are 5 remaining.
      var schRow = AddSchedule(read, schId: 10, unique: "uuuu", part: "pppp", pri: 10, plan: 50, complete: 40);
      AddScheduleProcess(schRow, proc: 1, matQty: 0, exeQty: 5);

      var j = new JobPlan("uuuu", 1);
      j.PartName = "pppp";
      j.SetInputQueue(1, 1, "thequeue");
      _jobDB.AddJobs(new NewJobs() {
        Jobs = new List<JobPlan> {j}
      }, null);

      // put 2 castings in queue, plus a different unique and a different process
      var mat1 = _logDB.AllocateMaterialID("uuuu", "pppp", 1);
      var mat2 = _logDB.AllocateMaterialID("uuuu", "pppp", 1);
      var mat3 = _logDB.AllocateMaterialID("other", "pppp", 1);
      var mat4 = _logDB.AllocateMaterialID("uuuu", "pppp", 1);
      _logDB.RecordAddMaterialToQueue(mat1, process: 0, queue: "thequeue", position: 0);
      _logDB.RecordAddMaterialToQueue(mat2, process: 0, queue: "thequeue", position: 1);
      _logDB.RecordAddMaterialToQueue(mat3, process: 0, queue: "thequeue", position: 2);
      _logDB.RecordAddMaterialToQueue(mat4, process: 1, queue: "thequeue", position: 3);

      //put something else at load station
      var action = new LoadAction(true, 1, "pppp", MazakPart.CreateComment("uuuu2", 1, false), 1, 1);

      var trans = _queues.CalculateScheduleChanges(read, new [] {action});

      trans.Schedule_t.Count.Should().Be(1);
      trans.Schedule_t[0].ScheduleID.Should().Be(10);
      trans.ScheduleProcess_t.Count.Should().Be(1);
      trans.ScheduleProcess_t[0].ProcessMaterialQuantity.Should().Be(2); // set the 2 material
    }

    [Fact]
    public void NoChanges()
    {
      var read = new ReadOnlyDataSet();

      // plan 50, 40 completed, and 5 in process, and 2 as material.
      var schRow = AddSchedule(read, schId: 10, unique: "uuuu", part: "pppp", pri: 10, plan: 50, complete: 40);
      AddScheduleProcess(schRow, proc: 1, matQty: 2, exeQty: 5);

      var j = new JobPlan("uuuu", 1);
      j.PartName = "pppp";
      j.SetInputQueue(1, 1, "thequeue");
      _jobDB.AddJobs(new NewJobs() {
        Jobs = new List<JobPlan> {j}
      }, null);

      // put 2 castings in queue which matches the material
      var mat1 = _logDB.AllocateMaterialID("uuuu", "pppp", 1);
      var mat2 = _logDB.AllocateMaterialID("uuuu", "pppp", 1);
      _logDB.RecordAddMaterialToQueue(mat1, process: 0, queue: "thequeue", position: 0);
      _logDB.RecordAddMaterialToQueue(mat2, process: 0, queue: "thequeue", position: 1);

      var trans = _queues.CalculateScheduleChanges(read, new LoadAction[] {});

      trans.Schedule_t.Count.Should().Be(0);
      trans.ScheduleProcess_t.Count.Should().Be(0);
    }

    [Fact]
    public void AllocateCastings()
    {
      var read = new ReadOnlyDataSet();

      // plan 50, 43 completed, and 5 in process.  So there are 2 remaining.
      var schRow = AddSchedule(read, schId: 10, unique: "uuuu", part: "pppp", pri: 10, plan: 50, complete: 43);
      AddScheduleProcess(schRow, proc: 1, matQty: 0, exeQty: 5);

      var j = new JobPlan("uuuu", 1);
      j.PartName = "pppp";
      j.SetInputQueue(1, 1, "thequeue");
      _jobDB.AddJobs(new NewJobs() {
        Jobs = new List<JobPlan> {j}
      }, null);

      // put 3 unassigned castings in queue
      var mat1 = _logDB.AllocateMaterialIDForCasting("pppp", 1);
      var mat2 = _logDB.AllocateMaterialIDForCasting("pppp", 1);
      var mat3 = _logDB.AllocateMaterialIDForCasting("pppp", 1);
      _logDB.RecordAddMaterialToQueue(mat1, process: 0, queue: "thequeue", position: 0);
      _logDB.RecordAddMaterialToQueue(mat2, process: 0, queue: "thequeue", position: 1);
      _logDB.RecordAddMaterialToQueue(mat3, process: 0, queue: "thequeue", position: 2);

      _logDB.GetMaterialInQueue("thequeue").Should().BeEquivalentTo(new [] {
        new JobLogDB.QueuedMaterial() {
          MaterialID = mat1, Queue = "thequeue", Position = 0, Unique = "", PartName = "pppp", NumProcesses = 1},
        new JobLogDB.QueuedMaterial() {
          MaterialID = mat2, Queue = "thequeue", Position = 1, Unique = "", PartName = "pppp", NumProcesses = 1},
        new JobLogDB.QueuedMaterial() {
          MaterialID = mat3, Queue = "thequeue", Position = 2, Unique = "", PartName = "pppp", NumProcesses = 1},
      });

      // should allocate 2 parts to uuuu, leave one unassigned, then update material quantity
      var trans = _queues.CalculateScheduleChanges(read, new LoadAction[] {});

      _logDB.GetMaterialInQueue("thequeue").Should().BeEquivalentTo(new [] {
        new JobLogDB.QueuedMaterial() {
          MaterialID = mat1, Queue = "thequeue", Position = 0, Unique = "uuuu", PartName = "pppp", NumProcesses = 1},
        new JobLogDB.QueuedMaterial() {
          MaterialID = mat2, Queue = "thequeue", Position = 1, Unique = "uuuu", PartName = "pppp", NumProcesses = 1},
        new JobLogDB.QueuedMaterial() {
          MaterialID = mat3, Queue = "thequeue", Position = 2, Unique = "", PartName = "pppp", NumProcesses = 1},
      });

      trans.Schedule_t.Count.Should().Be(1);
      trans.Schedule_t[0].ScheduleID.Should().Be(10);
      trans.ScheduleProcess_t.Count.Should().Be(1);
      trans.ScheduleProcess_t[0].ProcessMaterialQuantity.Should().Be(2); // set the 2 material
    }

    [Fact]
    public void MultipleProcesses()
    {
      var read = new ReadOnlyDataSet();

      // plan 50, 30 completed.  proc1 has 5 in process, zero material.  proc2 has 3 in process, 1 material
      var schRow = AddSchedule(read, schId: 10, unique: "uuuu", part: "pppp", pri: 10, plan: 50, complete: 30);
      AddScheduleProcess(schRow, proc: 1, matQty: 0, exeQty: 5);
      AddScheduleProcess(schRow, proc: 2, matQty: 1, exeQty: 3);

      var j = new JobPlan("uuuu", 2);
      j.PartName = "pppp";
      j.SetInputQueue(1, 1, "castingQ");
      j.SetInputQueue(2, 1, "transQ");
      _jobDB.AddJobs(new NewJobs() {
        Jobs = new List<JobPlan> {j}
      }, null);

      // put 2 castings in queue
      var mat1 = _logDB.AllocateMaterialID("uuuu", "pppp", 2);
      var mat2 = _logDB.AllocateMaterialID("uuuu", "pppp", 2);
      _logDB.RecordAddMaterialToQueue(mat1, process: 0, queue: "castingQ", position: 0);
      _logDB.RecordAddMaterialToQueue(mat2, process: 0, queue: "castingQ", position: 1);

      // put 3 in-proc in queue
      var mat3 = _logDB.AllocateMaterialID("uuuu", "pppp", 2);
      var mat4 = _logDB.AllocateMaterialID("uuuu", "pppp", 2);
      var mat5 = _logDB.AllocateMaterialID("uuuu", "pppp", 2);
      _logDB.RecordAddMaterialToQueue(mat3, process: 1, queue: "transQ", position: 0);
      _logDB.RecordAddMaterialToQueue(mat4, process: 1, queue: "transQ", position: 1);
      _logDB.RecordAddMaterialToQueue(mat5, process: 1, queue: "transQ", position: 2);

      var trans = _queues.CalculateScheduleChanges(read, new LoadAction[] {});

      trans.Schedule_t.Count.Should().Be(1);
      trans.Schedule_t[0].ScheduleID.Should().Be(10);
      trans.ScheduleProcess_t.Count.Should().Be(2);
      trans.ScheduleProcess_t[0].ProcessNumber.Should().Be(1);
      trans.ScheduleProcess_t[0].ProcessMaterialQuantity.Should().Be(2); // set the 2 material
      trans.ScheduleProcess_t[1].ProcessNumber.Should().Be(2);
      trans.ScheduleProcess_t[1].ProcessMaterialQuantity.Should().Be(3); // set the 3 material
    }

    [Fact]
    public void RemoveMat()
    {
      // test removing material when the queue is smaller
      throw new Exception("Removing material when remain = 0 fails");
    }

    [Fact(Skip="pending")]
    public void MultiplePaths()
    {

    }

    [Fact]
    public void SkipsWhenAtLoad()
    {
      var read = new ReadOnlyDataSet();
      var schRow = AddSchedule(read, schId: 10, unique: "uuuu", part: "pppp", pri: 10, plan: 50, complete: 40);
      AddScheduleProcess(schRow, proc: 1, matQty: 0, exeQty: 5);

      var j = new JobPlan("uuuu", 1);
      j.PartName = "pppp";
      j.SetInputQueue(1, 1, "thequeue");
      _jobDB.AddJobs(new NewJobs() {
        Jobs = new List<JobPlan> {j}
      }, null);

      // put 1 castings in queue
      var mat1 = _logDB.AllocateMaterialID("uuuu", "pppp", 1);
      _logDB.RecordAddMaterialToQueue(mat1, process: 0, queue: "thequeue", position: 0);

      //put something else at load station
      var action = new LoadAction(true, 1, "pppp", MazakPart.CreateComment("uuuu", 1, false), 1, 1);

      var trans = _queues.CalculateScheduleChanges(read, new [] {action});
      trans.Should().BeNull();
    }

  }
}