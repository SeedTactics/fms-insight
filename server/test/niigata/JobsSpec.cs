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

namespace BlackMaple.FMSInsight.Niigata.Tests
{
  public class NiigataJobSpec : IDisposable
  {
    private JobDB _jobDB;
    private JobLogDB _logDB;
    private NiigataJobs _jobs;
    private ISyncPallets _syncMock;

    public NiigataJobSpec()
    {
      var logConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
      logConn.Open();
      _logDB = new JobLogDB(new FMSSettings(), logConn);
      _logDB.CreateTables(firstSerialOnEmpty: null);

      var jobConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
      jobConn.Open();
      _jobDB = new JobDB(jobConn);
      _jobDB.CreateTables();

      _syncMock = Substitute.For<ISyncPallets>();

      var settings = new FMSSettings();
      settings.Queues.Add("q1", new QueueSize());

      _jobs = new NiigataJobs(_jobDB, _logDB, settings, _syncMock);
    }

    void IDisposable.Dispose()
    {
      _logDB.Close();
      _jobDB.Close();
    }

    [Fact]
    public void LoadsCurrentSt()
    {
      var mat1 = _logDB.AllocateMaterialID("u1", "p1", 1);
      _logDB.RecordSerialForMaterialID(mat1, 1, "serial1");
      _logDB.RecordWorkorderForMaterialID(mat1, 1, "work1");

      var mat2 = _logDB.AllocateMaterialID("u1", "p1", 1);
      _logDB.RecordSerialForMaterialID(mat2, 1, "serial2");
      _logDB.RecordWorkorderForMaterialID(mat2, 1, "work2");
      _logDB.ForceInspection(mat2, "insp2");

      var mat3 = _logDB.AllocateMaterialID("u2", "p2", 1);
      _logDB.RecordSerialForMaterialID(mat3, 2, "serial3");

      var mat4 = _logDB.AllocateMaterialID("u1", "p1", 1);
      _logDB.RecordSerialForMaterialID(mat4, 1, "serial4");
      _logDB.RecordWorkorderForMaterialID(mat4, 1, "work4");
      _logDB.ForceInspection(mat4, "insp4");


      //job with 1 completed part
      var j = new JobPlan("u1", 1);
      j.PartName = "p1";
      j.SetPlannedCyclesOnFirstProcess(1, 10);
      j.AddProcessOnPallet(1, 1, "pal1");
      j.ScheduleId = "sch1";
      _logDB.RecordUnloadEnd(
        mats: new[] { new JobLogDB.EventLogMaterial() {
          MaterialID = mat1,
          Process = 1,
          Face = "f1"
        }},
        pallet: "pal1",
        lulNum: 2,
        timeUTC: DateTime.UtcNow,
        elapsed: TimeSpan.FromMinutes(10),
        active: TimeSpan.Zero
      );
      _jobDB.AddJobs(new NewJobs()
      {
        Jobs = new List<JobPlan> { j }
      }, null);

      // two pallets with some material
      var pal1 = new PalletAndMaterial()
      {
        Pallet = new PalletStatus
        {
          Master = new PalletMaster()
          {
            PalletNum = 1,
            Skip = true
          },
          Loc = new MachineOrWashLoc()
          {
            Station = 3,
            Position = MachineOrWashLoc.Rotary.Worktable
          },
        },
        Material = new List<InProcessMaterial> {
          new InProcessMaterial() {
            MaterialID = mat2,
            JobUnique = "u1",
            PartName = "p1",
            Process = 1,
            Path = 1,
            Serial = "serial2",
            WorkorderId = "work2",
            SignaledInspections = new List<string> {"insp2"},
            Location = new InProcessMaterialLocation() {
              Type = InProcessMaterialLocation.LocType.OnPallet,
              Pallet = "1"
            },
            Action = new InProcessMaterialAction() {
              Type = InProcessMaterialAction.ActionType.Waiting
            }
          }
        }
      };
      var pal2 = new PalletAndMaterial()
      {
        Pallet = new PalletStatus()
        {
          Master = new PalletMaster()
          {
            PalletNum = 2,
            Skip = false,
            Alarm = true
          },
          Loc = new LoadUnloadLoc()
          {
            LoadStation = 2,
          },
        },
        Material = new List<InProcessMaterial> {
          new InProcessMaterial() {
            MaterialID = mat3,
            JobUnique = "u2",
            PartName = "p2",
            Process = 2,
            Path = 1,
            Serial = "serial3",
            WorkorderId = null,
            SignaledInspections = new List<string>(),
            Location = new InProcessMaterialLocation() {
              Type = InProcessMaterialLocation.LocType.OnPallet,
              Pallet = "2"
            },
            Action = new InProcessMaterialAction() {
              Type = InProcessMaterialAction.ActionType.Loading,
              LoadOntoFace = 1,
              LoadOntoPallet = "2",
              ProcessAfterLoad = 2,
              PathAfterLoad = 1
            }
          }
        }
      };

      // two material in queues
      _logDB.RecordAddMaterialToQueue(new JobLogDB.EventLogMaterial()
      {
        MaterialID = mat3,
        Process = 1,
        Face = null
      }, "q1", 0);
      _logDB.RecordAddMaterialToQueue(new JobLogDB.EventLogMaterial()
      {
        MaterialID = mat4,
        Process = 1,
        Face = null
      }, "q1", 1);


      var expectedSt = new CurrentStatus();
      var expectedJob = new InProcessJob(j);
      expectedJob.SetCompleted(1, 1, 1);
      expectedSt.Jobs.Add("u1", expectedJob);
      expectedSt.Material.Add(pal1.Material[0]);
      expectedSt.Material.Add(pal2.Material[0]);
      expectedSt.Material.Add(new InProcessMaterial()
      {
        MaterialID = mat4,
        Process = 1,
        JobUnique = "u1",
        PartName = "p1",
        Path = 1,
        Serial = "serial4",
        WorkorderId = "work4",
        SignaledInspections = new List<string> { "insp4" },
        Location = new InProcessMaterialLocation()
        {
          Type = InProcessMaterialLocation.LocType.InQueue,
          CurrentQueue = "q1",
          QueuePosition = 1,
        },
        Action = new InProcessMaterialAction()
        {
          Type = InProcessMaterialAction.ActionType.Waiting
        }
      });
      expectedSt.Pallets.Add("1", new MachineWatchInterface.PalletStatus()
      {
        Pallet = "1",
        FixtureOnPallet = "",
        OnHold = true,
        CurrentPalletLocation = new PalletLocation(PalletLocationEnum.Machine, "MC", 3),
        NumFaces = 1
      });
      expectedSt.Pallets.Add("2", new MachineWatchInterface.PalletStatus()
      {
        Pallet = "2",
        FixtureOnPallet = "",
        OnHold = false,
        CurrentPalletLocation = new PalletLocation(PalletLocationEnum.LoadUnload, "L/U", 2),
        NumFaces = 1
      });
      expectedSt.Alarms.Add("Pallet 2 has alarm");
      expectedSt.QueueSizes.Add("q1", new QueueSize());

      using (var monitor = _jobs.Monitor())
      {
        _syncMock.OnPalletsChanged += Raise.Event<Action<IList<PalletAndMaterial>>>(new List<PalletAndMaterial> { pal1, pal2 });

        _jobs.GetCurrentStatus().Should().BeEquivalentTo(expectedSt, config =>
          config.Excluding(c => c.TimeOfCurrentStatusUTC)
        );

        monitor.Should().Raise("OnNewCurrentStatus").First().Parameters.First().Should().BeEquivalentTo(expectedSt, config =>
          config.Excluding(c => c.TimeOfCurrentStatusUTC)
        );
      }
    }

    [Fact]
    public void AddsBasicJobs()
    {
      //add a completed job
      var completedJob = new JobPlan("old", 2);
      completedJob.PartName = "oldpart";
      completedJob.SetPlannedCyclesOnFirstProcess(1, 3);
      // record 3 completed parts
      for (int i = 0; i < 3; i++)
      {
        var mat = _logDB.AllocateMaterialID("old", "oldpart", 2);
        _logDB.RecordSerialForMaterialID(mat, 1, "serial1");
        _logDB.RecordUnloadEnd(
          mats: new[] { new JobLogDB.EventLogMaterial() { MaterialID = mat, Process = 2, Face = "1" } },
          pallet: "p1",
          lulNum: i,
          timeUTC: DateTime.UtcNow,
          elapsed: TimeSpan.FromMinutes(i),
          active: TimeSpan.FromHours(i)
        );
      }
      _jobDB.AddJobs(new NewJobs() { ScheduleId = "old", Jobs = new List<JobPlan> { completedJob } }, null);

      //some new jobs
      var newJob1 = new JobPlan("uu1", 2);
      newJob1.PartName = "p1";
      newJob1.SetPlannedCyclesOnFirstProcess(1, 10);
      var newJob2 = new JobPlan("uu2", 1);
      newJob2.PartName = "p2";
      newJob2.AddProcessOnPallet(process: 1, path: 1, pallet: "4");

      var newJobs = new NewJobs()
      {
        ScheduleId = "abcd",
        ArchiveCompletedJobs = true,
        Jobs = new List<JobPlan> { newJob1, newJob2 }
      };


      _jobs.AddJobs(newJobs, null);

      _jobDB.LoadUnarchivedJobs().Jobs.Should().BeEquivalentTo(new[] { newJob1, newJob2 });
      _jobDB.LoadJob("old").Archived.Should().BeTrue();

      _syncMock.Received().JobsOrQueuesChanged();
    }

    [Fact]
    public void Decrement()
    {
      var now = DateTime.UtcNow.AddHours(-1);

      _jobDB.AddNewDecrement(new[] {
        new JobDB.NewDecrementQuantity()
        {
          JobUnique = "u1",
          Part = "p1",
          Quantity = 10
        },
        new JobDB.NewDecrementQuantity()
        {
          JobUnique = "u2",
          Part = "p2",
          Quantity = 5
        }
      }, now);

      var expected = new[] {
        new JobAndDecrementQuantity()
        {
          DecrementId = 0,
          JobUnique = "u1",
          Part = "p1",
          Quantity = 10,
          TimeUTC = now
        },
        new JobAndDecrementQuantity()
        {
          DecrementId = 0,
          JobUnique = "u2",
          Part = "p2",
          Quantity = 5,
          TimeUTC = now
        }
      };

      _jobs.DecrementJobQuantites(-1).Should().BeEquivalentTo(expected);
      _syncMock.Received().DecrementPlannedButNotStartedQty();

      _syncMock.ClearReceivedCalls();

      _jobs.DecrementJobQuantites(DateTime.UtcNow.AddHours(-5)).Should().BeEquivalentTo(expected);
      _syncMock.Received().DecrementPlannedButNotStartedQty();
    }

    [Fact]
    public void UnallocatedQueues()
    {
      //add a casting
      _jobs.AddUnallocatedCastingToQueue("p1", "q1", position: 0, serial: "aaa");
      _logDB.GetMaterialDetails(1).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = 1,
        PartName = "p1",
        NumProcesses = 1,
        Serial = "aaa"
      });

      _logDB.GetMaterialInQueue("q1").Should().BeEquivalentTo(new[] {
          new JobLogDB.QueuedMaterial()
          { MaterialID = 1, NumProcesses = 1, PartName = "p1", Position = 0, Queue = "q1", Unique = ""}
        });

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();

      _jobs.RemoveMaterialFromAllQueues(1);
      _logDB.GetMaterialInQueue("q1").Should().BeEmpty();

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();
    }

    [Fact]
    public void AllocatedQueues()
    {
      var job = new JobPlan("uuu1", 2);
      job.PartName = "p1";
      _jobDB.AddJobs(new NewJobs() { ScheduleId = "abcd", Jobs = new List<JobPlan> { job } }, null);

      //add an allocated material
      _jobs.AddUnprocessedMaterialToQueue("uuu1", 1, "q1", 0, "aaa");
      _logDB.GetMaterialDetails(1).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = 1,
        PartName = "p1",
        NumProcesses = 2,
        Serial = "aaa",
        JobUnique = "uuu1"
      });

      _logDB.GetMaterialInQueue("q1").Should().BeEquivalentTo(new[] {
          new JobLogDB.QueuedMaterial()
          { MaterialID = 1, NumProcesses = 2, PartName = "p1", Position = 0, Queue = "q1", Unique = "uuu1"}
        });

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();

      //remove it
      _jobs.RemoveMaterialFromAllQueues(1);
      _logDB.GetMaterialInQueue("q1").Should().BeEmpty();

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();

      //add it back in
      _jobs.SetMaterialInQueue(1, "q1", 0);
      _logDB.GetMaterialInQueue("q1").Should().BeEquivalentTo(new[] {
          new JobLogDB.QueuedMaterial()
          { MaterialID = 1, NumProcesses = 2, PartName = "p1", Position = 0, Queue = "q1", Unique = "uuu1"}
        });

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();

    }
  }
}