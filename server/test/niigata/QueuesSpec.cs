
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
using NSubstitute;
using System.Collections.Immutable;
using Germinate;

namespace BlackMaple.FMSInsight.Niigata.Tests
{
  public class NiigataQueueSpec : IDisposable
  {
    private RepositoryConfig _repoCfg;
    private IRepository _logDB;
    private NiigataQueues _jobs;
    private ISyncPallets _syncMock;

    public NiigataQueueSpec()
    {
      _repoCfg = RepositoryConfig.InitializeSingleThreadedMemoryDB(new SerialSettings());
      _logDB = _repoCfg.OpenConnection();

      _syncMock = Substitute.For<ISyncPallets>();

      var settings = new FMSSettings();
      settings.Queues.Add("q1", new QueueSize());
      settings.Queues.Add("q2", new QueueSize());

      _jobs = new NiigataQueues(_repoCfg, settings, _syncMock);
    }

    void IDisposable.Dispose()
    {
      _repoCfg.CloseMemoryConnection();
    }

    [Fact]
    public void UnallocatedQueues()
    {
      //add a casting
      _jobs.AddUnallocatedCastingToQueue(casting: "c1", qty: 2, queue: "q1", serial: new[] { "aaa" }, operatorName: "theoper")
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
      mats[0].AddTimeUTC.Value.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(4));
      mats.Should().BeEquivalentTo(new[] {
         new QueuedMaterial()
          {
            MaterialID = 1, NumProcesses = 1, PartNameOrCasting = "c1", Position = 0, Queue = "q1", Unique = "", AddTimeUTC = mats[0].AddTimeUTC,
            Serial = "aaa", NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
          },
         new QueuedMaterial()
          {
            MaterialID = 2, NumProcesses = 1, PartNameOrCasting = "c1", Position = 1, Queue = "q1", Unique = "", AddTimeUTC = mats[1].AddTimeUTC,
            NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
           }
        });

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();

      _jobs.RemoveMaterialFromAllQueues(new List<long> { 1 }, "theoper");
      mats = _logDB.GetMaterialInAllQueues().ToList();
      mats.Should().BeEquivalentTo(new[] {
          new QueuedMaterial()
            {
              MaterialID = 2, NumProcesses = 1, PartNameOrCasting = "c1", Position = 0, Queue = "q1", Unique = "", AddTimeUTC = mats[0].AddTimeUTC,
              NextProcess = 1, Paths = ImmutableDictionary<int, int>.Empty
            }
        }, options => options.Using<DateTime?>(ctx => ctx.Subject.Value.Should().BeCloseTo(ctx.Expectation.Value, TimeSpan.FromSeconds(4))).WhenTypeIs<DateTime?>());

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();

      var mat1 = new LogMaterial(matID: 1, uniq: "", proc: 0, part: "c1", numProc: 1, serial: "aaa", workorder: "", face: "");
      var mat2 = new LogMaterial(matID: 2, uniq: "", proc: 0, part: "c1", numProc: 1, serial: "", workorder: "", face: "");

      _logDB.GetLogForMaterial(materialID: 1).Should().BeEquivalentTo(new[] {
        MarkExpectedEntry(mat1, cntr: 1, serial: "aaa"),
        AddToQueueExpectedEntry(mat1, cntr: 2, queue: "q1", position: 0, operName: "theoper", reason: "SetByOperator"),
        RemoveFromQueueExpectedEntry(mat1, cntr: 4, queue: "q1", position: 0, elapsedMin: 0, operName: "theoper"),
      }, options => options
        .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4)))
        .WhenTypeIs<DateTime>()
        .Using<TimeSpan>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4)))
        .WhenTypeIs<TimeSpan>()
        .ComparingByMembers<LogEntry>()
      );

      _logDB.GetLogForMaterial(materialID: 2).Should().BeEquivalentTo(new[] {
        AddToQueueExpectedEntry(mat2, cntr: 3, queue: "q1", position: 1, operName: "theoper", reason: "SetByOperator"),
      }, options => options
        .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4)))
        .WhenTypeIs<DateTime>()
        .Using<TimeSpan>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4)))
        .WhenTypeIs<TimeSpan>()
        .ComparingByMembers<LogEntry>()
      );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void UnprocessedMaterial(int lastCompletedProcess)
    {
      var job = new Job()
      {
        UniqueStr = "uuu1",
        PartName = "p1",
        Cycles = 0,
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo()) },
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo()) }
        )
      };
      _logDB.AddJobs(new NewJobs() { ScheduleId = "abcd", Jobs = ImmutableList.Create<Job>(job) }, null, addAsCopiedToSystem: true);

      //add an allocated material
      _jobs.AddUnprocessedMaterialToQueue("uuu1", process: lastCompletedProcess, queue: "q1", position: 0, serial: "aaa", operatorName: "theoper")
        .Should().BeEquivalentTo(
          QueuedMat(matId: 1, job: job, part: "p1", proc: lastCompletedProcess, path: 1, serial: "aaa", queue: "q1", pos: 0).Mat,
          options => options.ComparingByMembers<InProcessMaterial>()
        );
      _logDB.GetMaterialDetails(1).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = 1,
        PartName = "p1",
        NumProcesses = 2,
        Serial = "aaa",
        JobUnique = "uuu1",
      }, options => options.ComparingByMembers<MaterialDetails>());

      var mats = _logDB.GetMaterialInAllQueues().ToList();
      mats[0].AddTimeUTC.Value.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(4));
      mats.Should().BeEquivalentTo(new[] {
          new QueuedMaterial()
          {
            MaterialID = 1, NumProcesses = 2, PartNameOrCasting = "p1", Position = 0, Queue = "q1", Unique = "uuu1", AddTimeUTC = mats[0].AddTimeUTC,
            Serial = "aaa", NextProcess = lastCompletedProcess + 1, Paths = ImmutableDictionary<int, int>.Empty
          }
        });

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();

      //remove it
      _jobs.RemoveMaterialFromAllQueues(new List<long> { 1 }, "myoper");
      _logDB.GetMaterialInAllQueues().Should().BeEmpty();

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();

      //add it back in
      _jobs.SetMaterialInQueue(1, "q1", 0, "theoper");
      mats = _logDB.GetMaterialInAllQueues().ToList();
      mats.Should().BeEquivalentTo(new[] {
          new QueuedMaterial()
          {
            MaterialID = 1, NumProcesses = 2, PartNameOrCasting = "p1", Position = 0, Queue = "q1", Unique = "uuu1", AddTimeUTC = mats[0].AddTimeUTC,
            Serial = "aaa", NextProcess = lastCompletedProcess + 1, Paths = ImmutableDictionary<int, int>.Empty
          }
        });

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();

      mats = _logDB.GetMaterialInAllQueues().ToList();
      mats[0].AddTimeUTC.Value.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(4));
      mats.Should().BeEquivalentTo(new[] {
          new QueuedMaterial()
          {
            MaterialID = 1, NumProcesses = 2, PartNameOrCasting = "p1", Position = 0, Queue = "q1", Unique = "uuu1", AddTimeUTC = mats[0].AddTimeUTC,
            Serial = "aaa", NextProcess = lastCompletedProcess + 1, Paths = ImmutableDictionary<int, int>.Empty
          }
        });

      var logMat = new LogMaterial(matID: 1, uniq: "uuu1", proc: lastCompletedProcess, part: "p1", numProc: 2, serial: "aaa", workorder: "", face: "");

      _logDB.GetLogForMaterial(materialID: 1).Should().BeEquivalentTo(new[] {
        MarkExpectedEntry(logMat, cntr: 1, serial: "aaa"),
        AddToQueueExpectedEntry(logMat, cntr: 2, queue: "q1", position: 0, operName: "theoper", reason: "SetByOperator"),
        RemoveFromQueueExpectedEntry(logMat, cntr: 3, queue: "q1", position: 0, elapsedMin: 0, operName: "myoper"),
        AddToQueueExpectedEntry(logMat, cntr: 4, queue: "q1", position: 0, operName: "theoper", reason: "SetByOperator"),
      }, options => options
        .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4)))
        .WhenTypeIs<DateTime>()
        .Using<TimeSpan>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4)))
        .WhenTypeIs<TimeSpan>()
        .ComparingByMembers<LogEntry>()
      );
    }

    [Fact]
    public void QuarantinesMatOnPallet()
    {
      var job = new Job()
      {
        UniqueStr = "uuu1",
        PartName = "p1",
        Processes = ImmutableList.Create(new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo()) })
      };

      _logDB.AddJobs(new NewJobs() { ScheduleId = "abcd", Jobs = ImmutableList.Create(job) }, null, addAsCopiedToSystem: true);

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

      _jobs.SignalMaterialForQuarantine(1, "q1", "theoper");

      _syncMock.Received().JobsOrQueuesChanged();
      _syncMock.ClearReceivedCalls();

      var logMat = new LogMaterial(matID: 1, uniq: "uuu1", proc: 1, part: "p1", numProc: 2, serial: "", workorder: "", face: "");

      _logDB.GetLogForMaterial(materialID: 1).Should().BeEquivalentTo(new[] {
        SignalQuarantineExpectedEntry(logMat, cntr: 1, pal: "4", queue: "q1", operName: "theoper")
      }, options => options
        .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4)))
        .WhenTypeIs<DateTime>()
        .ComparingByMembers<LogEntry>()
      );
    }

    [Fact]
    public void QuarantinesMatInQueue()
    {
      var job = new Job()
      {
        UniqueStr = "uuu1",
        PartName = "p1",
        Cycles = 5,
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo()) },
          new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo()) }
        )
      };
      _logDB.AddJobs(new NewJobs() { ScheduleId = "abcd", Jobs = ImmutableList.Create<Job>(job) }, null, addAsCopiedToSystem: true);

      _logDB.AllocateMaterialID("uuu1", "p1", 2).Should().Be(1);
      _logDB.RecordSerialForMaterialID(materialID: 1, proc: 1, serial: "aaa");
      _logDB.RecordLoadStart(new[] { new EventLogMaterial() { MaterialID = 1, Process = 1, Face = "" }
          }, "3", 2, DateTime.UtcNow);

      _jobs.SetMaterialInQueue(materialId: 1, queue: "q1", position: -1, operatorName: null);

      _syncMock.CurrentCellState().Returns(
            new CellState()
            {
              Pallets = new List<PalletAndMaterial>(),
              QueuedMaterial = new List<InProcessMaterialAndJob>() {
            QueuedMat(matId: 1, job: job, part: "p1", proc: 1, path: 2, serial: "aaa", queue: "q1", pos: 0)
              }
            }
          );

      _jobs.SignalMaterialForQuarantine(1, "q2", "theoper");

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
        .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4)))
        .WhenTypeIs<DateTime>()
        .Using<TimeSpan>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4)))
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

    private InProcessMaterialAndJob QueuedMat(long matId, Job job, string part, int proc, int path, string serial, string queue, int pos)
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