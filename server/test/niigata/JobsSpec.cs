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
using BlackMaple.MachineWatchInterface;
using NSubstitute;

namespace BlackMaple.FMSInsight.Niigata.Tests
{
  public class NiigataJobSpec : IDisposable
  {
    private JobDB _jobDB;
    private EventLogDB _logDB;
    private NiigataJobs _jobs;
    private ISyncPallets _syncMock;
    private Action<NewJobs> _onNewJobs;

    public NiigataJobSpec()
    {
      var logCfg = EventLogDB.Config.InitializeSingleThreadedMemoryDB(new FMSSettings());
      _logDB = logCfg.OpenConnection();

      var jobDbCfg = JobDB.Config.InitializeSingleThreadedMemoryDB();
      _jobDB = jobDbCfg.OpenConnection();

      _syncMock = Substitute.For<ISyncPallets>();

      var settings = new FMSSettings();
      settings.Queues.Add("q1", new QueueSize());

      _onNewJobs = Substitute.For<Action<NewJobs>>();
      _jobs = new NiigataJobs(jobDbCfg, logCfg, settings, _syncMock, null, _onNewJobs);
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
      _logDB.RecordPathForProcess(mat3, 2, 5);
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
      j.RouteStartingTimeUTC = new DateTime(2020, 04, 19, 13, 18, 0, DateTimeKind.Utc);
      j.SetSimulatedStartingTimeUTC(1, 1, new DateTime(2020, 04, 19, 20, 0, 0, DateTimeKind.Utc));
      j.ScheduleId = "sch1";
      _logDB.RecordUnloadEnd(
        mats: new[] { new EventLogDB.EventLogMaterial() {
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
      _jobDB.AddNewDecrement(new[] {
        new JobDB.NewDecrementQuantity() {
          JobUnique = "u1",
          Part = "p1",
          Quantity = 30
        }
      }, new DateTime(2020, 04, 19, 13, 18, 0, DateTimeKind.Utc));

      // a second job with the same  route starting time and earlier simulated starting time
      var j2 = new JobPlan("u2", 1);
      j2.PartName = "p1";
      j2.RouteStartingTimeUTC = new DateTime(2020, 04, 19, 13, 18, 0, DateTimeKind.Utc);
      j2.SetSimulatedStartingTimeUTC(1, 1, new DateTime(2020, 04, 19, 17, 0, 0, DateTimeKind.Utc));

      // a third job with earlier route starting time but later simulated starting time
      var j3 = new JobPlan("u3", 1);
      j3.PartName = "p1";
      j3.RouteStartingTimeUTC = new DateTime(2020, 04, 19, 10, 18, 0, DateTimeKind.Utc);
      j3.SetSimulatedStartingTimeUTC(1, 1, new DateTime(2020, 04, 19, 22, 0, 0, DateTimeKind.Utc));


      // two pallets with some material
      var pal1 = new PalletAndMaterial()
      {
        Status = new PalletStatus
        {
          Master = new PalletMaster()
          {
            PalletNum = 1,
            Skip = true
          },
          Tracking = new TrackingInfo()
          {
            Alarm = false
          },
          CurStation = NiigataStationNum.Machine(3, null),
        },
        CurrentOrLoadingFaces = new List<PalletFace> {
          new PalletFace() {
            Job = j,
            Process = 1,
            Path = 1,
            Face = 1,
            FaceIsMissingMaterial = false
          }
        },
        Material = new List<InProcessMaterialAndJob> {
            new InProcessMaterialAndJob() {
              Job = j,
              Mat = new InProcessMaterial() {
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
        },
      };
      var pal2 = new PalletAndMaterial()
      {
        Status = new PalletStatus()
        {
          Master = new PalletMaster()
          {
            PalletNum = 2,
            Skip = false,
          },
          Tracking = new TrackingInfo()
          {
            Alarm = true,
            AlarmCode = 40
          },
          CurStation = NiigataStationNum.LoadStation(2)
        },
        CurrentOrLoadingFaces = new List<PalletFace> {
          new PalletFace() {
            Job = j,
            Process = 2,
            Path = 5,
            Face = 1,
            FaceIsMissingMaterial = false
          }
        },
        Material = new List<InProcessMaterialAndJob> {
          new InProcessMaterialAndJob() {
            Job = j,
            Mat =
            new InProcessMaterial() {
              MaterialID = mat3,
              JobUnique = "u2",
              PartName = "p2",
              Process = 2,
              Path = 5,
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
        },
      };

      var queuedMat = new InProcessMaterial
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
      };

      // two material in queues
      _logDB.RecordAddMaterialToQueue(new EventLogDB.EventLogMaterial()
      {
        MaterialID = mat3,
        Process = 1,
        Face = null
      }, "q1", 0);
      _logDB.RecordAddMaterialToQueue(new EventLogDB.EventLogMaterial()
      {
        MaterialID = mat4,
        Process = 1,
        Face = null
      }, "q1", 1);

      var status = new NiigataStatus()
      {
        Machines = new Dictionary<int, MachineStatus> {
          {2, new MachineStatus() {
            MachineNumber = 2,
            Alarm = true
          }}
        },
        Alarm = true
      };


      var expectedSt = new CurrentStatus();
      var expectedJob = new InProcessJob(j);
      expectedJob.SetCompleted(1, 1, 1);
      expectedJob.Decrements.Add(new DecrementQuantity()
      {
        DecrementId = 0,
        TimeUTC = new DateTime(2020, 04, 19, 13, 18, 0, DateTimeKind.Utc),
        Quantity = 30
      });
      expectedJob.SetPrecedence(1, 1, 2); // has last precedence
      expectedSt.Jobs.Add("u1", expectedJob);

      var expectedJob2 = new InProcessJob(j2);
      expectedJob2.SetPrecedence(1, 1, 1); // has middle precedence
      expectedSt.Jobs.Add("u2", expectedJob2);

      var expectedJob3 = new InProcessJob(j3);
      expectedJob2.SetPrecedence(1, 1, 0); // has first precedence
      expectedSt.Jobs.Add("u3", expectedJob3);

      expectedSt.Material.Add(pal1.Material[0].Mat);
      expectedSt.Material.Add(pal2.Material[0].Mat);
      expectedSt.Material.Add(queuedMat);
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
      expectedSt.Alarms.Add("Pallet 2 has alarm 40");
      expectedSt.Alarms.Add("Machine 2 has an alarm");
      expectedSt.Alarms.Add("ICC has an alarm");
      expectedSt.QueueSizes.Add("q1", new QueueSize());

      _syncMock.CurrentCellState().Returns(new CellState()
      {
        Status = status,
        Pallets = new List<PalletAndMaterial> { pal1, pal2 },
        QueuedMaterial = new List<InProcessMaterial> { queuedMat },
        UnarchivedJobs = new List<JobPlan> { j, j2, j3 }
      });

      ((IJobControl)_jobs).GetCurrentStatus().Should().BeEquivalentTo(expectedSt, config =>
        config.Excluding(c => c.TimeOfCurrentStatusUTC)
      );
    }

    [Fact]
    public void AddsBasicJobs()
    {
      //add some existing jobs
      var completedJob = new JobPlan("old", 2);
      completedJob.PartName = "oldpart";
      var toKeepJob = new JobPlan("tokeep", 1);
      toKeepJob.PartName = "tokeep";
      _jobDB.AddJobs(new NewJobs() { ScheduleId = "old", Jobs = new List<JobPlan> { completedJob, toKeepJob } }, null);

      _syncMock.CurrentCellState().Returns(new CellState()
      {
        Pallets = new List<PalletAndMaterial>(),
        QueuedMaterial = new List<InProcessMaterial>(),
        JobQtyRemainingOnProc1 = new Dictionary<(string uniq, int proc1path), int>() {
          {(uniq: "old", proc1path: 1), 0},
          {(uniq: "tokeep", proc1path: 1), 5}
        }
      });

      //some new jobs
      var newJob1 = new JobPlan("uu1", 2);
      newJob1.PartName = "p1";
      newJob1.SetPlannedCyclesOnFirstProcess(1, 10);
      newJob1.AddLoadStation(1, 1, 1);
      newJob1.AddLoadStation(2, 1, 1);
      newJob1.AddUnloadStation(1, 1, 2);
      newJob1.AddUnloadStation(2, 1, 1);
      newJob1.AddProcessOnPallet(1, 1, "5");
      newJob1.AddProcessOnPallet(2, 1, "6");
      newJob1.SetFixtureFace(1, 1, "fix1", 2);
      newJob1.SetFixtureFace(2, 1, "fix1", 2);
      var newJob2 = new JobPlan("uu2", 1);
      newJob2.PartName = "p2";
      newJob2.AddProcessOnPallet(process: 1, path: 1, pallet: "4");
      newJob2.AddLoadStation(1, 1, 1);
      newJob2.AddUnloadStation(1, 1, 2);
      newJob2.SetFixtureFace(1, 1, "fix1", 2);

      var newJobs = new NewJobs()
      {
        ScheduleId = "abcd",
        Jobs = new List<JobPlan> { newJob1, newJob2 }
      };

      ((IJobControl)_jobs).AddJobs(newJobs, null);

      _jobDB.LoadUnarchivedJobs().Should().BeEquivalentTo(new[] { newJob1, newJob2, toKeepJob });
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

      ((IJobControl)_jobs).DecrementJobQuantites(-1).Should().BeEquivalentTo(expected);
      _syncMock.Received().DecrementPlannedButNotStartedQty(Arg.Any<JobDB>(), Arg.Any<EventLogDB>());

      _syncMock.ClearReceivedCalls();

      ((IJobControl)_jobs).DecrementJobQuantites(DateTime.UtcNow.AddHours(-5)).Should().BeEquivalentTo(expected);
      _syncMock.Received().DecrementPlannedButNotStartedQty(Arg.Any<JobDB>(), Arg.Any<EventLogDB>());
    }

    [Fact]
    public void UnallocatedQueues()
    {
      //add a casting
      ((IJobControl)_jobs).AddUnallocatedCastingToQueue(casting: "c1", qty: 2, queue: "q1", position: 0, serial: new[] { "aaa" }, operatorName: "theoper");
      _logDB.GetMaterialDetails(1).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = 1,
        PartName = "c1",
        NumProcesses = 1,
        Serial = "aaa",
        Paths = new Dictionary<int, int>()
      });
      _logDB.GetMaterialDetails(2).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = 2,
        PartName = "c1",
        NumProcesses = 1,
        Serial = null,
        Paths = new Dictionary<int, int>()
      });

      var mats = _logDB.GetMaterialInQueue("q1").ToList();
      mats[0].AddTimeUTC.Value.Should().BeCloseTo(DateTime.UtcNow, precision: 4000);
      mats.Should().BeEquivalentTo(new[] {
         new EventLogDB.QueuedMaterial()
          { MaterialID = 1, NumProcesses = 1, PartNameOrCasting = "c1", Position = 0, Queue = "q1", Unique = "", AddTimeUTC = mats[0].AddTimeUTC },
         new EventLogDB.QueuedMaterial()
          { MaterialID = 2, NumProcesses = 1, PartNameOrCasting = "c1", Position = 1, Queue = "q1", Unique = "", AddTimeUTC = mats[1].AddTimeUTC }
        });

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();

      ((IJobControl)_jobs).RemoveMaterialFromAllQueues(new List<long> { 1 }, "theoper");
      mats = _logDB.GetMaterialInQueue("q1").ToList();
      mats.Should().BeEquivalentTo(new[] {
          new EventLogDB.QueuedMaterial()
            { MaterialID = 2, NumProcesses = 1, PartNameOrCasting = "c1", Position = 0, Queue = "q1", Unique = "", AddTimeUTC = mats[0].AddTimeUTC}
        }, options => options.Using<DateTime?>(ctx => ctx.Subject.Value.Should().BeCloseTo(ctx.Expectation.Value, 4000)).WhenTypeIs<DateTime?>());

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();
    }

    [Fact]
    public void AllocatedQueues()
    {
      var job = new JobPlan("uuu1", 2, new[] { 2, 2 });
      job.PartName = "p1";
      job.SetPathGroup(1, 1, 50);
      job.SetPathGroup(1, 2, 60);
      _jobDB.AddJobs(new NewJobs() { ScheduleId = "abcd", Jobs = new List<JobPlan> { job } }, null);

      //add an allocated material
      ((IJobControl)_jobs).AddUnprocessedMaterialToQueue("uuu1", lastCompletedProcess: 1, pathGroup: 60, queue: "q1", position: 0, serial: "aaa", operatorName: "theoper");
      _logDB.GetMaterialDetails(1).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = 1,
        PartName = "p1",
        NumProcesses = 2,
        Serial = "aaa",
        JobUnique = "uuu1",
        Paths = new Dictionary<int, int>() { { 1, 2 } }
      });

      var mats = _logDB.GetMaterialInQueue("q1").ToList();
      mats[0].AddTimeUTC.Value.Should().BeCloseTo(DateTime.UtcNow, precision: 4000);
      mats.Should().BeEquivalentTo(new[] {
          new EventLogDB.QueuedMaterial()
          { MaterialID = 1, NumProcesses = 2, PartNameOrCasting = "p1", Position = 0, Queue = "q1", Unique = "uuu1", AddTimeUTC = mats[0].AddTimeUTC}
        });

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();

      //remove it
      ((IJobControl)_jobs).RemoveMaterialFromAllQueues(new List<long> { 1 }, "myoper");
      _logDB.GetMaterialInQueue("q1").Should().BeEmpty();

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();

      //add it back in
      ((IJobControl)_jobs).SetMaterialInQueue(1, "q1", 0, "theoper");
      mats = _logDB.GetMaterialInQueue("q1").ToList();
      mats.Should().BeEquivalentTo(new[] {
          new EventLogDB.QueuedMaterial()
          { MaterialID = 1, NumProcesses = 2, PartNameOrCasting = "p1", Position = 0, Queue = "q1", Unique = "uuu1", AddTimeUTC = mats[0].AddTimeUTC}
        });

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();

    }
  }
}