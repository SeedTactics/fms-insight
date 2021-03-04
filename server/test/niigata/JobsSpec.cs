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
using System.Collections.Immutable;
using Germinate;
using MachineWatchTest;

namespace BlackMaple.FMSInsight.Niigata.Tests
{
  public class NiigataJobSpec : IDisposable
  {
    private RepositoryConfig _repoCfg;
    private IRepository _logDB;
    private NiigataJobs _jobs;
    private ISyncPallets _syncMock;
    private Action<NewJobs> _onNewJobs;
    private Action<EditMaterialInLogEvents> _onEditMatInLog;

    public NiigataJobSpec()
    {
      _repoCfg = RepositoryConfig.InitializeSingleThreadedMemoryDB(new FMSSettings());
      _logDB = _repoCfg.OpenConnection();

      _syncMock = Substitute.For<ISyncPallets>();

      var settings = new FMSSettings();
      settings.Queues.Add("q1", new QueueSize());
      settings.Queues.Add("q2", new QueueSize());

      _onNewJobs = Substitute.For<Action<NewJobs>>();
      _onEditMatInLog = Substitute.For<Action<EditMaterialInLogEvents>>();
      _jobs = new NiigataJobs(_repoCfg, settings, _syncMock, null, false, false, true, _onNewJobs, _onEditMatInLog);
    }

    void IDisposable.Dispose()
    {
      _repoCfg.CloseMemoryConnection();
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
      j.SetPlannedCyclesOnFirstProcess(1, 70);
      j.AddProcessOnPallet(1, 1, "pal1");
      j.RouteStartingTimeUTC = new DateTime(2020, 04, 19, 13, 18, 0, DateTimeKind.Utc);
      j.SetSimulatedStartingTimeUTC(1, 1, new DateTime(2020, 04, 19, 20, 0, 0, DateTimeKind.Utc));
      j.ScheduleId = "sch1";
      _logDB.RecordUnloadEnd(
        mats: new[] { new EventLogMaterial() {
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
      var job1Decrements = ImmutableList.Create(
        new DecrementQuantity()
        {
          DecrementId = 0,
          Proc1Path = 1,
          TimeUTC = new DateTime(2020, 04, 19, 13, 18, 0, DateTimeKind.Utc),
          Quantity = 30
        }
      );

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
                SignaledInspections = ImmutableList.Create("insp2"),
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
            AlarmCode = PalletAlarmCode.RoutingFault
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
            Job = j2,
            Mat =
            new InProcessMaterial() {
              MaterialID = mat3,
              JobUnique = "u2",
              PartName = "p2",
              Process = 2,
              Path = 5,
              Serial = "serial3",
              WorkorderId = null,
              SignaledInspections = ImmutableList<string>.Empty,
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

      var queuedMat = new InProcessMaterialAndJob
      {
        Job = j,
        Mat = new InProcessMaterial()
        {
          MaterialID = mat4,
          Process = 1,
          JobUnique = "u1",
          PartName = "p1",
          Path = 1,
          Serial = "serial4",
          WorkorderId = "work4",
          SignaledInspections = ImmutableList.Create("insp4"),
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
        }
      };

      // two material in queues
      _logDB.RecordAddMaterialToQueue(new EventLogMaterial()
      {
        MaterialID = mat3,
        Process = 1,
        Face = null
      }, "q1", 0, operatorName: null, reason: "TestReason");
      _logDB.RecordAddMaterialToQueue(new EventLogMaterial()
      {
        MaterialID = mat4,
        Process = 1,
        Face = null
      }, "q1", 1, operatorName: null, reason: "TestReason2");

      var status = new NiigataStatus()
      {
        Machines = new Dictionary<int, MachineStatus> {
          {2, new MachineStatus() {
            MachineNumber = 2,
            Alarm = true
          }}
        },
        Alarm = true,
        TimeOfStatusUTC = DateTime.UtcNow
      };


      var expectedSt = (new CurrentStatus()).Produce(st =>
      {
        st.TimeOfCurrentStatusUTC = status.TimeOfStatusUTC;
        var expectedJob = j.ToHistoricJob().CloneToDerived<ActiveJob, Job>() with
        {
          ScheduleId = j.ScheduleId,
          CopiedToSystem = true,
          CyclesOnFirstProcess = ImmutableList.Create(70 - 30),
          Completed = ImmutableList.Create(ImmutableList.Create(1)),
          Precedence = ImmutableList.Create(ImmutableList.Create(3L)), // has last precedence
          Decrements = job1Decrements,
          AssignedWorkorders = ImmutableList.Create("work1", "work2", "work4")
        };
        st.Jobs.Add("u1", expectedJob);

        var expectedJob2 = j2.ToHistoricJob().CloneToDerived<ActiveJob, Job>() with
        {
          ScheduleId = j2.ScheduleId,
          CopiedToSystem = true,
          CyclesOnFirstProcess = ImmutableList.Create(0),
          Completed = ImmutableList.Create(ImmutableList.Create(0)),
          Precedence = ImmutableList.Create(ImmutableList.Create(2L)), // has middle precedence
          Decrements = null,
          AssignedWorkorders = null,
        };
        st.Jobs.Add("u2", expectedJob2);

        var expectedJob3 = j3.ToHistoricJob().CloneToDerived<ActiveJob, Job>() with
        {
          ScheduleId = j3.ScheduleId,
          CopiedToSystem = true,
          CyclesOnFirstProcess = ImmutableList.Create(0),
          Completed = ImmutableList.Create(ImmutableList.Create(0)),
          Precedence = ImmutableList.Create(ImmutableList.Create(1L)), // has first precedence
          Decrements = null,
          AssignedWorkorders = null
        };
        st.Jobs.Add("u3", expectedJob3);

        st.Material.Add(pal1.Material[0].Mat);
        st.Material.Add(pal2.Material[0].Mat);
        st.Material.Add(queuedMat.Mat);
        st.Pallets.Add("1", new MachineWatchInterface.PalletStatus()
        {
          Pallet = "1",
          FixtureOnPallet = "",
          OnHold = true,
          CurrentPalletLocation = new PalletLocation(PalletLocationEnum.Machine, "MC", 3),
          NumFaces = 1
        });
        st.Pallets.Add("2", new MachineWatchInterface.PalletStatus()
        {
          Pallet = "2",
          FixtureOnPallet = "",
          OnHold = false,
          CurrentPalletLocation = new PalletLocation(PalletLocationEnum.LoadUnload, "L/U", 2),
          NumFaces = 1
        });
        st.Alarms.Add("Pallet 2 has routing fault");
        st.Alarms.Add("Machine 2 has an alarm");
        st.Alarms.Add("ICC has an alarm");
        st.QueueSizes.Add("q1", new QueueSize());
        st.QueueSizes.Add("q2", new QueueSize());
      });

      _syncMock.CurrentCellState().Returns(new CellState()
      {
        Status = status,
        Pallets = new List<PalletAndMaterial> { pal1, pal2 },
        QueuedMaterial = new List<InProcessMaterialAndJob> { queuedMat },
        UnarchivedJobs = new List<HistoricJob> { j.ToHistoricJob() with { Decrements = job1Decrements }, j2.ToHistoricJob(), j3.ToHistoricJob() },
        UnarchivedLegacyJobs = new List<JobPlan> { j, j2, j3 }
      });

      ((IJobControl)_jobs).GetCurrentStatus().Should().BeEquivalentTo(expectedSt,
        options => options
          .ComparingByMembers<CurrentStatus>()
          .ComparingByMembers<ActiveJob>()
          .ComparingByMembers<ProcessInfo>()
          .ComparingByMembers<ProcPathInfo>()
          .ComparingByMembers<MachiningStop>()
          .ComparingByMembers<InProcessMaterial>()
          .ComparingByMembers<PalletStatus>()
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
      toKeepJob.PathInspections(1, 1).Add(new PathInspection()
      {
        InspectionType = "insp",
        Counter = "cntr",
        MaxVal = 10,
      });
      _logDB.AddJobs(new NewJobs() { ScheduleId = "old", Jobs = ImmutableList.Create<Job>(completedJob.ToHistoricJob(), toKeepJob.ToHistoricJob()) }, null, addAsCopiedToSystem: true);

      _syncMock.CurrentCellState().Returns(new CellState()
      {
        Pallets = new List<PalletAndMaterial>(),
        QueuedMaterial = new List<InProcessMaterialAndJob>(),
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
      newJob1.PathInspections(1, 1).Add(new PathInspection()
      {
        InspectionType = "insp",
        Counter = "cntr1",
        MaxVal = 11,
      });
      newJob1.PathInspections(2, 1).Add(new PathInspection()
      {
        InspectionType = "insp",
        Counter = "cntr2",
        MaxVal = 22,
      });
      var newJob2 = new JobPlan("uu2", 1);
      newJob2.PartName = "p2";
      newJob2.AddProcessOnPallet(process: 1, path: 1, pallet: "4");
      newJob2.AddLoadStation(1, 1, 1);
      newJob2.AddUnloadStation(1, 1, 2);
      newJob2.SetFixtureFace(1, 1, "fix1", 2);
      newJob2.PathInspections(1, 1).Add(new PathInspection()
      {
        InspectionType = "insp",
        Counter = "cntr3",
        MaxVal = 33,
      });

      var newJobs = new NewJobs()
      {
        ScheduleId = "abcd",
        Jobs = ImmutableList.Create<Job>(newJob1.ToHistoricJob(), newJob2.ToHistoricJob())
      };

      ((IJobControl)_jobs).AddJobs(newJobs, null);

      _logDB.LoadUnarchivedJobs().Select(j => j.UniqueStr).Should().BeEquivalentTo(new[] { toKeepJob.UniqueStr, newJob1.UniqueStr, newJob2.UniqueStr });
      _logDB.LoadJob("old").Archived.Should().BeTrue();

      _syncMock.Received().JobsOrQueuesChanged();
    }

    [Fact]
    public void Decrement()
    {
      var now = DateTime.UtcNow.AddHours(-1);

      _logDB.AddNewDecrement(new[] {
        new NewDecrementQuantity()
        {
          JobUnique = "u1",
          Proc1Path = 1,
          Part = "p1",
          Quantity = 10
        },
        new NewDecrementQuantity()
        {
          JobUnique = "u2",
          Proc1Path = 2,
          Part = "p2",
          Quantity = 5
        }
      }, now);

      var expected = new[] {
        new JobAndDecrementQuantity()
        {
          DecrementId = 0,
          JobUnique = "u1",
          Proc1Path = 1,
          Part = "p1",
          Quantity = 10,
          TimeUTC = now
        },
        new JobAndDecrementQuantity()
        {
          DecrementId = 0,
          Proc1Path = 2,
          JobUnique = "u2",
          Part = "p2",
          Quantity = 5,
          TimeUTC = now
        }
      };

      ((IJobControl)_jobs).DecrementJobQuantites(-1).Should().BeEquivalentTo(expected);
      _syncMock.Received().DecrementPlannedButNotStartedQty(Arg.Any<IRepository>());

      _syncMock.ClearReceivedCalls();

      ((IJobControl)_jobs).DecrementJobQuantites(DateTime.UtcNow.AddHours(-5)).Should().BeEquivalentTo(expected);
      _syncMock.Received().DecrementPlannedButNotStartedQty(Arg.Any<IRepository>());
    }

    [Fact]
    public void UnallocatedQueues()
    {
      //add a casting
      ((IJobControl)_jobs).AddUnallocatedCastingToQueue(casting: "c1", qty: 2, queue: "q1", serial: new[] { "aaa" }, operatorName: "theoper")
        .Should().BeEquivalentTo(new[] {
          QueuedMat(matId: 1, job: null, part: "c1", proc: 0, path: 1, serial: "aaa", queue: "q1", pos: 0).Mat,
          QueuedMat(matId: 2, job: null, part: "c1", proc: 0, path: 1, serial: "", queue: "q1", pos: 1).Mat,
        }, options => options.ComparingByMembers<InProcessMaterial>());
      _logDB.GetMaterialDetails(1).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = 1,
        PartName = "c1",
        NumProcesses = 1,
        Serial = "aaa",
      });
      _logDB.GetMaterialDetails(2).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = 2,
        PartName = "c1",
        NumProcesses = 1,
        Serial = null,
      });

      var mats = _logDB.GetMaterialInAllQueues().ToList();
      mats[0].AddTimeUTC.Value.Should().BeCloseTo(DateTime.UtcNow, precision: 4000);
      mats.Should().BeEquivalentTo(new[] {
         new QueuedMaterial()
          { MaterialID = 1, NumProcesses = 1, PartNameOrCasting = "c1", Position = 0, Queue = "q1", Unique = "", AddTimeUTC = mats[0].AddTimeUTC },
         new QueuedMaterial()
          { MaterialID = 2, NumProcesses = 1, PartNameOrCasting = "c1", Position = 1, Queue = "q1", Unique = "", AddTimeUTC = mats[1].AddTimeUTC }
        });

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();

      ((IJobControl)_jobs).RemoveMaterialFromAllQueues(new List<long> { 1 }, "theoper");
      mats = _logDB.GetMaterialInAllQueues().ToList();
      mats.Should().BeEquivalentTo(new[] {
          new QueuedMaterial()
            { MaterialID = 2, NumProcesses = 1, PartNameOrCasting = "c1", Position = 0, Queue = "q1", Unique = "", AddTimeUTC = mats[0].AddTimeUTC}
        }, options => options.Using<DateTime?>(ctx => ctx.Subject.Value.Should().BeCloseTo(ctx.Expectation.Value, 4000)).WhenTypeIs<DateTime?>());

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();

      var mat1 = new LogMaterial(matID: 1, uniq: "", proc: 0, part: "c1", numProc: 1, serial: "aaa", workorder: "", face: "");
      var mat2 = new LogMaterial(matID: 2, uniq: "", proc: 0, part: "c1", numProc: 1, serial: "", workorder: "", face: "");

      _logDB.GetLogForMaterial(materialID: 1).Should().BeEquivalentTo(new[] {
        MarkExpectedEntry(mat1, cntr: 1, serial: "aaa"),
        AddToQueueExpectedEntry(mat1, cntr: 2, queue: "q1", position: 0, operName: "theoper", reason: "SetByOperator"),
        RemoveFromQueueExpectedEntry(mat1, cntr: 4, queue: "q1", position: 0, elapsedMin: 0, operName: "theoper"),
      }, options => options
        .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: 4000))
        .WhenTypeIs<DateTime>()
        .Using<TimeSpan>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: 4000))
        .WhenTypeIs<TimeSpan>()
        .ComparingByMembers<LogEntry>()
      );

      _logDB.GetLogForMaterial(materialID: 2).Should().BeEquivalentTo(new[] {
        AddToQueueExpectedEntry(mat2, cntr: 3, queue: "q1", position: 1, operName: "theoper", reason: "SetByOperator"),
      }, options => options
        .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: 4000))
        .WhenTypeIs<DateTime>()
        .Using<TimeSpan>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: 4000))
        .WhenTypeIs<TimeSpan>()
        .ComparingByMembers<LogEntry>()
      );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void UnprocessedMaterial(int lastCompletedProcess)
    {
      var job = new JobPlan("uuu1", 2, new[] { 2, 2 });
      job.PartName = "p1";
      job.SetPathGroup(1, 1, 50);
      job.SetPathGroup(1, 2, 60);
      _logDB.AddJobs(new NewJobs() { ScheduleId = "abcd", Jobs = ImmutableList.Create<Job>(job.ToHistoricJob()) }, null, addAsCopiedToSystem: true);

      //add an allocated material
      ((IJobControl)_jobs).AddUnprocessedMaterialToQueue("uuu1", lastCompletedProcess: lastCompletedProcess, pathGroup: 60, queue: "q1", position: 0, serial: "aaa", operatorName: "theoper")
        .Should().BeEquivalentTo(
          QueuedMat(matId: 1, job: job, part: "p1", proc: lastCompletedProcess, path: 2, serial: "aaa", queue: "q1", pos: 0).Mat,
          options => options.ComparingByMembers<InProcessMaterial>()
        );
      _logDB.GetMaterialDetails(1).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = 1,
        PartName = "p1",
        NumProcesses = 2,
        Serial = "aaa",
        JobUnique = "uuu1",
        Paths = ImmutableDictionary<int, int>.Empty.Add(1, 2)
      }, options => options.ComparingByMembers<MaterialDetails>());

      var mats = _logDB.GetMaterialInAllQueues().ToList();
      mats[0].AddTimeUTC.Value.Should().BeCloseTo(DateTime.UtcNow, precision: 4000);
      mats.Should().BeEquivalentTo(new[] {
          new QueuedMaterial()
          { MaterialID = 1, NumProcesses = 2, PartNameOrCasting = "p1", Position = 0, Queue = "q1", Unique = "uuu1", AddTimeUTC = mats[0].AddTimeUTC}
        });

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();

      //remove it
      ((IJobControl)_jobs).RemoveMaterialFromAllQueues(new List<long> { 1 }, "myoper");
      _logDB.GetMaterialInAllQueues().Should().BeEmpty();

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();

      //add it back in
      ((IJobControl)_jobs).SetMaterialInQueue(1, "q1", 0, "theoper");
      mats = _logDB.GetMaterialInAllQueues().ToList();
      mats.Should().BeEquivalentTo(new[] {
          new QueuedMaterial()
          { MaterialID = 1, NumProcesses = 2, PartNameOrCasting = "p1", Position = 0, Queue = "q1", Unique = "uuu1", AddTimeUTC = mats[0].AddTimeUTC}
        });

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();

      mats = _logDB.GetMaterialInAllQueues().ToList();
      mats[0].AddTimeUTC.Value.Should().BeCloseTo(DateTime.UtcNow, precision: 4000);
      mats.Should().BeEquivalentTo(new[] {
          new QueuedMaterial()
          { MaterialID = 1, NumProcesses = 2, PartNameOrCasting = "p1", Position = 0, Queue = "q1", Unique = "uuu1", AddTimeUTC = mats[0].AddTimeUTC}
        });

      var logMat = new LogMaterial(matID: 1, uniq: "uuu1", proc: lastCompletedProcess, part: "p1", numProc: 2, serial: "aaa", workorder: "", face: "");

      _logDB.GetLogForMaterial(materialID: 1).Should().BeEquivalentTo(new[] {
        MarkExpectedEntry(logMat, cntr: 1, serial: "aaa"),
        AddToQueueExpectedEntry(logMat, cntr: 2, queue: "q1", position: 0, operName: "theoper", reason: "SetByOperator"),
        RemoveFromQueueExpectedEntry(logMat, cntr: 3, queue: "q1", position: 0, elapsedMin: 0, operName: "myoper"),
        AddToQueueExpectedEntry(logMat, cntr: 4, queue: "q1", position: 0, operName: "theoper", reason: "SetByOperator"),
      }, options => options
        .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: 4000))
        .WhenTypeIs<DateTime>()
        .Using<TimeSpan>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: 4000))
        .WhenTypeIs<TimeSpan>()
        .ComparingByMembers<LogEntry>()
      );
    }

    [Fact]
    public void QuarantinesMatOnPallet()
    {

      var job = new JobPlan("uuu1", 2, new[] { 2, 2 });
      job.PartName = "p1";
      job.SetPathGroup(1, 1, 50);
      job.SetPathGroup(1, 2, 60);
      _logDB.AddJobs(new NewJobs() { ScheduleId = "abcd", Jobs = ImmutableList.Create<Job>(job.ToHistoricJob()) }, null, addAsCopiedToSystem: true);

      _logDB.AllocateMaterialID("uuu1", "p1", 2).Should().Be(1);

      _syncMock.CurrentCellState().Returns(
        new CellState()
        {
          Pallets = new List<PalletAndMaterial>() {
            new PalletAndMaterial() {
              Material = new List<InProcessMaterialAndJob>() {
                new InProcessMaterialAndJob() {
                  Mat = MatOnPal(matId: 1, uniq: "uuu1", part: "p1", proc: 1, path: 2, serial: "aaa", pal: "4")
                }
              }
            }
          },
          QueuedMaterial = new List<InProcessMaterialAndJob>()
        }
      );

      ((IJobControl)_jobs).SignalMaterialForQuarantine(1, "q1", "theoper");

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();

      var logMat = new LogMaterial(matID: 1, uniq: "uuu1", proc: 1, part: "p1", numProc: 2, serial: "", workorder: "", face: "");

      _logDB.GetLogForMaterial(materialID: 1).Should().BeEquivalentTo(new[] {
        SignalQuarantineExpectedEntry(logMat, cntr: 1, pal: "4", queue: "q1", operName: "theoper")
      }, options => options
        .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: 4000))
        .WhenTypeIs<DateTime>()
        .ComparingByMembers<LogEntry>()
      );
    }

    [Fact]
    public void QuarantinesMatInQueue()
    {
      var job = new JobPlan("uuu1", 2, new[] { 2, 2 });
      job.PartName = "p1";
      job.SetPathGroup(1, 1, 50);
      job.SetPathGroup(1, 2, 60);
      _logDB.AddJobs(new NewJobs() { ScheduleId = "abcd", Jobs = ImmutableList.Create<Job>(job.ToHistoricJob()) }, null, addAsCopiedToSystem: true);

      _logDB.AllocateMaterialID("uuu1", "p1", 2).Should().Be(1);
      _logDB.RecordSerialForMaterialID(materialID: 1, proc: 1, serial: "aaa");
      _logDB.RecordLoadStart(new[] { new EventLogMaterial() { MaterialID = 1, Process = 1, Face = "" }
          }, "3", 2, DateTime.UtcNow);

      ((IJobControl)_jobs).SetMaterialInQueue(materialId: 1, queue: "q1", position: -1);

      _syncMock.CurrentCellState().Returns(
            new CellState()
            {
              Pallets = new List<PalletAndMaterial>(),
              QueuedMaterial = new List<InProcessMaterialAndJob>() {
            QueuedMat(matId: 1, job: job, part: "p1", proc: 1, path: 2, serial: "aaa", queue: "q1", pos: 0)
              }
            }
          );

      ((IJobControl)_jobs).SignalMaterialForQuarantine(1, "q2", "theoper");

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();

      var logMat = new LogMaterial(matID: 1, uniq: "uuu1", proc: 1, part: "p1", numProc: 2, serial: "aaa", workorder: "", face: "");

      _logDB.GetLogForMaterial(materialID: 1).Should().BeEquivalentTo(new[] {
        MarkExpectedEntry(logMat, cntr: 1, serial: "aaa"),
        LoadStartExpectedEntry(logMat, cntr: 2, pal: "3", lul: 2),
        AddToQueueExpectedEntry(logMat, cntr: 3, queue: "q1", position: 0, operName: null, reason: "SetByOperator"),
        RemoveFromQueueExpectedEntry(logMat, cntr: 4, queue: "q1", position: 0, elapsedMin: 0, operName: "theoper"),
        AddToQueueExpectedEntry(logMat, cntr: 5, queue: "q2", position: 0, operName: "theoper", reason: "SetByOperator")
      }, options => options
        .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: 4000))
        .WhenTypeIs<DateTime>()
        .Using<TimeSpan>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: 4000))
        .WhenTypeIs<TimeSpan>()
        .ComparingByMembers<LogEntry>()
      );
    }

    private LogEntry MarkExpectedEntry(LogMaterial mat, long cntr, string serial, DateTime? timeUTC = null)
    {
      var e = new LogEntry(
          cntr: cntr,
          mat: new[] { mat },
          pal: "",
          ty: LogType.PartMark,
          locName: "Mark",
          locNum: 1,
          prog: "MARK",
          start: false,
          endTime: timeUTC ?? DateTime.UtcNow,
          result: serial,
          endOfRoute: false);
      return e;
    }

    private LogEntry LoadStartExpectedEntry(LogMaterial mat, long cntr, string pal, int lul, DateTime? timeUTC = null)
    {
      var e = new LogEntry(
          cntr: cntr,
          mat: new[] { mat },
          pal: pal,
          ty: LogType.LoadUnloadCycle,
          locName: "L/U",
          locNum: lul,
          prog: "LOAD",
          start: true,
          endTime: timeUTC ?? DateTime.UtcNow,
          result: "LOAD",
          endOfRoute: false);
      return e;
    }

    private LogEntry AddToQueueExpectedEntry(LogMaterial mat, long cntr, string queue, int position, DateTime? timeUTC = null, string operName = null, string reason = null)
    {
      var e = new LogEntry(
          cntr: cntr,
          mat: new[] { mat },
          pal: "",
          ty: LogType.AddToQueue,
          locName: queue,
          locNum: position,
          prog: reason ?? "",
          start: false,
          endTime: timeUTC ?? DateTime.UtcNow,
          result: "",
          endOfRoute: false);
      if (!string.IsNullOrEmpty(operName))
      {
        e %= en => en.ProgramDetails.Add("operator", operName);
      }
      return e;
    }

    private LogEntry SignalQuarantineExpectedEntry(LogMaterial mat, long cntr, string pal, string queue, DateTime? timeUTC = null, string operName = null)
    {
      var e = new LogEntry(
          cntr: cntr,
          mat: new[] { mat },
          pal: pal,
          ty: LogType.SignalQuarantine,
          locName: queue,
          locNum: -1,
          prog: "QuarantineAfterUnload",
          start: false,
          endTime: timeUTC ?? DateTime.UtcNow,
          result: "QuarantineAfterUnload",
          endOfRoute: false);
      if (!string.IsNullOrEmpty(operName))
      {
        e %= en => en.ProgramDetails.Add("operator", operName);
      }
      return e;
    }

    private LogEntry RemoveFromQueueExpectedEntry(LogMaterial mat, long cntr, string queue, int position, int elapsedMin, DateTime? timeUTC = null, string operName = null)
    {
      var e = new LogEntry(
          cntr: cntr,
          mat: new[] { mat },
          pal: "",
          ty: LogType.RemoveFromQueue,
          locName: queue,
          locNum: position,
          prog: "",
          start: false,
          endTime: timeUTC ?? DateTime.UtcNow,
          result: "",
          endOfRoute: false,
          elapsed: TimeSpan.FromMinutes(elapsedMin),
          active: TimeSpan.Zero);
      if (!string.IsNullOrEmpty(operName))
      {
        e %= en => en.ProgramDetails.Add("operator", operName);
      }
      return e;
    }

    private InProcessMaterialAndJob QueuedMat(long matId, JobPlan job, string part, int proc, int path, string serial, string queue, int pos)
    {
      return new InProcessMaterialAndJob()
      {
        Job = job,
        Mat = new InProcessMaterial()
        {
          MaterialID = matId,
          JobUnique = job?.UniqueStr,
          PartName = part,
          Process = proc,
          Path = path,
          Serial = serial,
          Location = new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.InQueue,
            CurrentQueue = queue,
            QueuePosition = pos,
          },
          Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Waiting
          }
        }
      };
    }

    private InProcessMaterial MatOnPal(long matId, string uniq, string part, int proc, int path, string serial, string pal)
    {
      return new InProcessMaterial()
      {
        MaterialID = matId,
        JobUnique = uniq,
        PartName = part,
        Process = proc,
        Path = path,
        Serial = serial,
        Location = new InProcessMaterialLocation()
        {
          Type = InProcessMaterialLocation.LocType.OnPallet,
          Pallet = pal,
        },
        Action = new InProcessMaterialAction()
        {
          Type = InProcessMaterialAction.ActionType.Waiting
        }
      };
    }
  }
}