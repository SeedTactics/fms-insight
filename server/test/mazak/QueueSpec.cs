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

      _queues = new MazakQueues(_logDB, _jobDB, null, null, null, startThread: false);
    }

		public void Dispose()
		{
			_logDB.Close();
			_jobDB.Close();
		}

    [Fact]
    public void Empty()
    {
      var trans = _queues.CalculateScheduleChanges(new ReadOnlyDataSet(), new LoadAction[] {}, out bool foundAtLoad);
      trans.Should().BeNull();
      foundAtLoad.Should().BeFalse();
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

      // put 2 material in queue
      var mat1 = _logDB.AllocateMaterialID("uuuu", "pppp", 1);
      var mat2 = _logDB.AllocateMaterialID("uuuu", "pppp", 1);
      _logDB.RecordAddMaterialToQueue(mat1, process: 0, queue: "thequeue", position: 0);
      _logDB.RecordAddMaterialToQueue(mat2, process: 0, queue: "thequeue", position: 1);

      var trans = _queues.CalculateScheduleChanges(read, new LoadAction[] {}, out bool foundAtLoad);
      foundAtLoad.Should().BeFalse();

      trans.Schedule_t.Count.Should().Be(1);
      trans.Schedule_t[0].ScheduleID.Should().Be(10);
      trans.ScheduleProcess_t.Count.Should().Be(1);
      trans.ScheduleProcess_t[0].ProcessMaterialQuantity.Should().Be(2); // set the 2 material
    }
  }
}