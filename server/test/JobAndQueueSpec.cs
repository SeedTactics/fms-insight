/* Copyright (c) 2022, John Lenz

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
using System.Threading.Tasks;
using AutoFixture;

namespace MachineWatchTest;

public class JobAndQueueSpec : ISynchronizeCellState<JobAndQueueSpec.MockCellState>, IDisposable
{
  public record MockCellState : ICellState
  {
    public long Uniq { get; init; }
    public CurrentStatus CurrentStatus { get; init; }
    public bool PalletStateUpdated { get; init; }
  }

  private readonly RepositoryConfig _repo;
  private readonly ICheckJobsValid _checkJobsMock;
  private readonly JobsAndQueuesFromDb<MockCellState> _jq;
  private readonly Fixture _fixture;

  public JobAndQueueSpec(Xunit.Abstractions.ITestOutputHelper output)
  {
    _repo = RepositoryConfig.InitializeSingleThreadedMemoryDB(new SerialSettings());
    _fixture = new Fixture();
    _fixture.Customizations.Add(new ImmutableSpecimenBuilder());

    _checkJobsMock = Substitute.For<ICheckJobsValid>();

    var settings = new FMSSettings();
    settings.Queues.Add("q1", new QueueSize());
    settings.Queues.Add("q2", new QueueSize());

    _jq = new JobsAndQueuesFromDb<MockCellState>(
      _repo,
      settings,
      OnNewCurrentStatus,
      this,
      _checkJobsMock,
      refreshStateInterval: TimeSpan.FromHours(50)
    );

    _jq.StartThread();
  }

  void IDisposable.Dispose()
  {
    _repo?.CloseMemoryConnection();
    _jq?.Dispose();
  }

  #region Sync Cell State
  private MockCellState _curSt;
  private bool _executeActions;
  public event Action NewCellState;

  MockCellState ISynchronizeCellState<MockCellState>.CalculateCellState(IRepository db)
  {
    return _curSt;
  }

  bool ISynchronizeCellState<MockCellState>.ApplyActions(IRepository db, MockCellState st)
  {
    st.Should().Be(_curSt);
    if (_executeActions)
    {
      _executeActions = false;
      _curSt = _curSt with
      {
        Uniq = _curSt.Uniq + 1,
        CurrentStatus = new CurrentStatus()
        {
          TimeOfCurrentStatusUTC = DateTime.UtcNow,
          Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
          Pallets = ImmutableDictionary<string, PalletStatus>.Empty,
          Material = ImmutableList<InProcessMaterial>.Empty,
          Alarms = ImmutableList<string>.Empty,
          QueueSizes = ImmutableDictionary<string, QueueSize>.Empty
        }
      };
      return true;
    }
    else
    {
      return false;
    }
  }

  private System.Collections.Concurrent.ConcurrentQueue<
    TaskCompletionSource<CurrentStatus>
  > _newCellStateTcs = new();

  private void OnNewCurrentStatus(CurrentStatus st)
  {
    while (_newCellStateTcs.TryDequeue(out var tcs))
    {
      tcs.SetResult(st);
    }
  }

  private async Task SetCurrentState(bool palStateUpdated, bool executeAction, CurrentStatus curSt = null)
  {
    curSt =
      curSt
      ?? new CurrentStatus()
      {
        TimeOfCurrentStatusUTC = DateTime.UtcNow,
        Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
        Pallets = ImmutableDictionary<string, PalletStatus>.Empty,
        Material = ImmutableList<InProcessMaterial>.Empty,
        Alarms = ImmutableList<string>.Empty,
        QueueSizes = ImmutableDictionary<string, QueueSize>.Empty
      };
    _curSt = new MockCellState()
    {
      Uniq = 0,
      CurrentStatus = curSt,
      PalletStateUpdated = palStateUpdated
    };
    _executeActions = executeAction;

    // wait for NewCurrentStatus after raising NewCellState event
    var tcs = new TaskCompletionSource<CurrentStatus>();
    _newCellStateTcs.Enqueue(tcs);
    NewCellState?.Invoke();
    (await tcs.Task).Should().Be(curSt);
  }

  private Task CreateTaskToWaitForNewCellState()
  {
    var st = _curSt.CurrentStatus;
    var tcs = new TaskCompletionSource<CurrentStatus>();
    _newCellStateTcs.Enqueue(tcs);
    return tcs.Task.ContinueWith(s => s.Result.Should().Be(st));
  }
  #endregion

  #region Jobs

  private Job RandJob()
  {
    var job = _fixture.Create<Job>();
    return job with
    {
      RouteEndUTC = job.RouteStartUTC.AddHours(1),
      Processes = job.Processes
        .Select(
          (proc, procIdx) =>
            new ProcessInfo()
            {
              Paths = proc.Paths
                .Select(path => path with { Casting = procIdx == 0 ? path.Casting : null })
                .ToImmutableList()
            }
        )
        .ToImmutableList()
    };
  }

  [Fact]
  public async Task AddsBasicJobs()
  {
    using var db = _repo.OpenConnection();

    //add some existing jobs
    var completedJob = RandJob() with
    {
      Cycles = 3,
      Archived = false
    };
    var toKeepJob = RandJob() with { Cycles = 10, Archived = false };

    var completedActive = completedJob.CloneToDerived<ActiveJob, Job>() with
    {
      Cycles = 4,
      RemainingToStart = 0
    };
    var toKeepActive = toKeepJob.CloneToDerived<ActiveJob, Job>() with { Cycles = 10, RemainingToStart = 5 };

    db.AddJobs(
      new NewJobs() { ScheduleId = "old", Jobs = ImmutableList.Create<Job>(completedJob, toKeepJob) },
      null,
      addAsCopiedToSystem: true
    );

    await SetCurrentState(
      palStateUpdated: false,
      executeAction: false,
      curSt: new CurrentStatus()
      {
        TimeOfCurrentStatusUTC = DateTime.UtcNow,
        Jobs = ImmutableDictionary<string, ActiveJob>.Empty
          .Add(completedActive.UniqueStr, completedActive)
          .Add(toKeepJob.UniqueStr, toKeepActive),
        Pallets = ImmutableDictionary<string, PalletStatus>.Empty,
        Material = ImmutableList<InProcessMaterial>.Empty,
        Alarms = ImmutableList<string>.Empty,
        QueueSizes = ImmutableDictionary<string, QueueSize>.Empty
      }
    );

    //some new jobs
    var newJob1 = RandJob() with
    {
      Archived = false
    };
    var newJob2 = RandJob() with { Archived = false };
    var newJobs = new NewJobs()
    {
      ScheduleId = "abcd",
      Jobs = ImmutableList.Create<Job>(newJob1, newJob2),
      Programs = ImmutableList.Create(
        new NewProgramContent() { ProgramName = "prog1", ProgramContent = "content1" },
        new NewProgramContent() { ProgramName = "prog2", ProgramContent = "content2" }
      )
    };

    var newStatusTask = CreateTaskToWaitForNewCellState();

    _checkJobsMock.CheckNewJobs(Arg.Any<IRepository>(), Arg.Any<NewJobs>()).Returns(new string[] { });

    ((IJobControl)_jq).AddJobs(newJobs, null, false);

    await newStatusTask;

    db.LoadUnarchivedJobs()
      .Select(j => j.UniqueStr)
      .Should()
      .BeEquivalentTo(new[] { toKeepJob.UniqueStr, newJob1.UniqueStr, newJob2.UniqueStr });
    db.LoadJob(completedJob.UniqueStr).Archived.Should().BeTrue();
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task Decrement(bool byDate)
  {
    var now = DateTime.UtcNow;
    using var db = _repo.OpenConnection();

    var j1 = RandJob() with { UniqueStr = "u1", Cycles = 12, };
    var j1Active = j1.CloneToDerived<ActiveJob, Job>() with { RemainingToStart = 7 };

    var j2 = RandJob() with { UniqueStr = "u2", Cycles = 22, };
    var j2Active = j2.CloneToDerived<ActiveJob, Job>() with { RemainingToStart = 0 };

    db.AddJobs(
      new NewJobs() { ScheduleId = "old", Jobs = ImmutableList.Create<Job>(j1, j2) },
      null,
      addAsCopiedToSystem: true
    );

    await SetCurrentState(
      palStateUpdated: false,
      executeAction: false,
      curSt: new CurrentStatus()
      {
        TimeOfCurrentStatusUTC = now,
        Jobs = ImmutableDictionary<string, ActiveJob>.Empty
          .Add(j1.UniqueStr, j1Active)
          .Add(j2.UniqueStr, j2Active),
        Pallets = ImmutableDictionary<string, PalletStatus>.Empty,
        Material = ImmutableList<InProcessMaterial>.Empty,
        Alarms = ImmutableList<string>.Empty,
        QueueSizes = ImmutableDictionary<string, QueueSize>.Empty
      }
    );

    var newStatusTask = CreateTaskToWaitForNewCellState();

    IEnumerable<JobAndDecrementQuantity> decrs;
    if (byDate)
    {
      decrs = ((IJobControl)_jq).DecrementJobQuantites(DateTime.UtcNow.AddHours(-5));
    }
    else
    {
      decrs = ((IJobControl)_jq).DecrementJobQuantites(-1);
    }

    decrs
      .Should()
      .BeEquivalentTo(
        new[]
        {
          new JobAndDecrementQuantity()
          {
            DecrementId = 0,
            JobUnique = j1.UniqueStr,
            TimeUTC = now,
            Part = j1.PartName,
            Quantity = 7,
          }
        }
      );

    await newStatusTask;

    db.LoadDecrementsForJob("u1")
      .Should()
      .BeEquivalentTo(
        ImmutableList.Create(
          new[]
          {
            new DecrementQuantity()
            {
              DecrementId = 0,
              TimeUTC = now,
              Quantity = 7
            }
          }
        )
      );

    db.LoadDecrementsForJob("u2").Should().BeEmpty();
  }
  #endregion


  #region Queues
  [Fact]
  public async Task UnallocatedQueues()
  {
    using var db = _repo.OpenConnection();

    await SetCurrentState(palStateUpdated: false, executeAction: false);
    var newStatusTask = CreateTaskToWaitForNewCellState();

    //add a casting
    _jq.AddUnallocatedCastingToQueue(
        casting: "c1",
        qty: 2,
        queue: "q1",
        serial: new[] { "aaa" },
        operatorName: "theoper"
      )
      .Should()
      .BeEquivalentTo(
        new[]
        {
          QueuedMat(matId: 1, job: null, part: "c1", proc: 0, path: 1, serial: "aaa", queue: "q1", pos: 0),
          QueuedMat(matId: 2, job: null, part: "c1", proc: 0, path: 1, serial: "", queue: "q1", pos: 1),
        }
      );
    db.GetMaterialDetails(1)
      .Should()
      .BeEquivalentTo(
        new MaterialDetails()
        {
          MaterialID = 1,
          PartName = "c1",
          NumProcesses = 1,
          Serial = "aaa",
        }
      );
    db.GetMaterialDetails(2)
      .Should()
      .BeEquivalentTo(
        new MaterialDetails()
        {
          MaterialID = 2,
          PartName = "c1",
          NumProcesses = 1,
          Serial = null,
        }
      );

    var mats = db.GetMaterialInAllQueues().ToList();
    mats[0].AddTimeUTC.Value.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(4));
    mats.Should()
      .BeEquivalentTo(
        new[]
        {
          new QueuedMaterial()
          {
            MaterialID = 1,
            NumProcesses = 1,
            PartNameOrCasting = "c1",
            Position = 0,
            Queue = "q1",
            Unique = "",
            AddTimeUTC = mats[0].AddTimeUTC,
            Serial = "aaa",
            NextProcess = 1,
            Paths = ImmutableDictionary<int, int>.Empty
          },
          new QueuedMaterial()
          {
            MaterialID = 2,
            NumProcesses = 1,
            PartNameOrCasting = "c1",
            Position = 1,
            Queue = "q1",
            Unique = "",
            AddTimeUTC = mats[1].AddTimeUTC,
            NextProcess = 1,
            Paths = ImmutableDictionary<int, int>.Empty
          }
        }
      );

    await newStatusTask;

    newStatusTask = CreateTaskToWaitForNewCellState();

    _jq.RemoveMaterialFromAllQueues(new List<long> { 1 }, "theoper");
    mats = db.GetMaterialInAllQueues().ToList();
    mats.Should()
      .BeEquivalentTo(
        new[]
        {
          new QueuedMaterial()
          {
            MaterialID = 2,
            NumProcesses = 1,
            PartNameOrCasting = "c1",
            Position = 0,
            Queue = "q1",
            Unique = "",
            AddTimeUTC = mats[0].AddTimeUTC,
            NextProcess = 1,
            Paths = ImmutableDictionary<int, int>.Empty
          }
        },
        options =>
          options
            .Using<DateTime?>(
              ctx => ctx.Subject.Value.Should().BeCloseTo(ctx.Expectation.Value, TimeSpan.FromSeconds(4))
            )
            .WhenTypeIs<DateTime?>()
      );

    await newStatusTask;

    var mat1 = new LogMaterial(
      matID: 1,
      uniq: "",
      proc: 0,
      part: "c1",
      numProc: 1,
      serial: "aaa",
      workorder: "",
      face: ""
    );
    var mat2 = new LogMaterial(
      matID: 2,
      uniq: "",
      proc: 0,
      part: "c1",
      numProc: 1,
      serial: "",
      workorder: "",
      face: ""
    );

    db.GetLogForMaterial(materialID: 1)
      .Should()
      .BeEquivalentTo(
        new[]
        {
          MarkExpectedEntry(mat1, cntr: 1, serial: "aaa"),
          AddToQueueExpectedEntry(
            mat1,
            cntr: 2,
            queue: "q1",
            position: 0,
            operName: "theoper",
            reason: "SetByOperator"
          ),
          RemoveFromQueueExpectedEntry(
            mat1,
            cntr: 4,
            queue: "q1",
            position: 0,
            elapsedMin: 0,
            operName: "theoper"
          ),
        },
        options =>
          options
            .Using<DateTime>(
              ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4))
            )
            .WhenTypeIs<DateTime>()
            .Using<TimeSpan>(
              ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4))
            )
            .WhenTypeIs<TimeSpan>()
            .ComparingByMembers<LogEntry>()
      );

    db.GetLogForMaterial(materialID: 2)
      .Should()
      .BeEquivalentTo(
        new[]
        {
          AddToQueueExpectedEntry(
            mat2,
            cntr: 3,
            queue: "q1",
            position: 1,
            operName: "theoper",
            reason: "SetByOperator"
          ),
        },
        options =>
          options
            .Using<DateTime>(
              ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4))
            )
            .WhenTypeIs<DateTime>()
            .Using<TimeSpan>(
              ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4))
            )
            .WhenTypeIs<TimeSpan>()
            .ComparingByMembers<LogEntry>()
      );
  }

  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  public async Task UnprocessedMaterial(int lastCompletedProcess)
  {
    using var db = _repo.OpenConnection();

    var job = new Job()
    {
      UniqueStr = "uuu1",
      PartName = "p1",
      Cycles = 0,
      Processes = ImmutableList.Create(
        new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) },
        new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
      ),
      RouteStartUTC = DateTime.MinValue,
      RouteEndUTC = DateTime.MinValue,
      Archived = false,
    };
    db.AddJobs(
      new NewJobs() { ScheduleId = "abcd", Jobs = ImmutableList.Create<Job>(job) },
      null,
      addAsCopiedToSystem: true
    );

    await SetCurrentState(palStateUpdated: false, executeAction: false);
    var newStatusTask = CreateTaskToWaitForNewCellState();

    //add an allocated material
    _jq.AddUnprocessedMaterialToQueue(
        "uuu1",
        process: lastCompletedProcess,
        queue: "q1",
        position: 0,
        serial: "aaa",
        operatorName: "theoper"
      )
      .Should()
      .BeEquivalentTo(
        QueuedMat(
          matId: 1,
          job: job,
          part: "p1",
          proc: lastCompletedProcess,
          path: 1,
          serial: "aaa",
          queue: "q1",
          pos: 0
        ),
        options => options.ComparingByMembers<InProcessMaterial>()
      );
    db.GetMaterialDetails(1)
      .Should()
      .BeEquivalentTo(
        new MaterialDetails()
        {
          MaterialID = 1,
          PartName = "p1",
          NumProcesses = 2,
          Serial = "aaa",
          JobUnique = "uuu1",
        },
        options => options.ComparingByMembers<MaterialDetails>()
      );

    var mats = db.GetMaterialInAllQueues().ToList();
    mats[0].AddTimeUTC.Value.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(4));
    mats.Should()
      .BeEquivalentTo(
        new[]
        {
          new QueuedMaterial()
          {
            MaterialID = 1,
            NumProcesses = 2,
            PartNameOrCasting = "p1",
            Position = 0,
            Queue = "q1",
            Unique = "uuu1",
            AddTimeUTC = mats[0].AddTimeUTC,
            Serial = "aaa",
            NextProcess = lastCompletedProcess + 1,
            Paths = ImmutableDictionary<int, int>.Empty
          }
        }
      );

    await newStatusTask;

    newStatusTask = CreateTaskToWaitForNewCellState();

    //remove it
    _jq.RemoveMaterialFromAllQueues(new List<long> { 1 }, "myoper");
    db.GetMaterialInAllQueues().Should().BeEmpty();

    await newStatusTask;

    newStatusTask = CreateTaskToWaitForNewCellState();

    //add it back in
    _jq.SetMaterialInQueue(1, "q1", 0, "theoper");
    mats = db.GetMaterialInAllQueues().ToList();
    mats.Should()
      .BeEquivalentTo(
        new[]
        {
          new QueuedMaterial()
          {
            MaterialID = 1,
            NumProcesses = 2,
            PartNameOrCasting = "p1",
            Position = 0,
            Queue = "q1",
            Unique = "uuu1",
            AddTimeUTC = mats[0].AddTimeUTC,
            Serial = "aaa",
            NextProcess = lastCompletedProcess + 1,
            Paths = ImmutableDictionary<int, int>.Empty
          }
        }
      );

    await newStatusTask;

    mats = db.GetMaterialInAllQueues().ToList();
    mats[0].AddTimeUTC.Value.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(4));
    mats.Should()
      .BeEquivalentTo(
        new[]
        {
          new QueuedMaterial()
          {
            MaterialID = 1,
            NumProcesses = 2,
            PartNameOrCasting = "p1",
            Position = 0,
            Queue = "q1",
            Unique = "uuu1",
            AddTimeUTC = mats[0].AddTimeUTC,
            Serial = "aaa",
            NextProcess = lastCompletedProcess + 1,
            Paths = ImmutableDictionary<int, int>.Empty
          }
        }
      );

    var logMat = new LogMaterial(
      matID: 1,
      uniq: "uuu1",
      proc: lastCompletedProcess,
      part: "p1",
      numProc: 2,
      serial: "aaa",
      workorder: "",
      face: ""
    );

    db.GetLogForMaterial(materialID: 1)
      .Should()
      .BeEquivalentTo(
        new[]
        {
          MarkExpectedEntry(logMat, cntr: 1, serial: "aaa"),
          AddToQueueExpectedEntry(
            logMat,
            cntr: 2,
            queue: "q1",
            position: 0,
            operName: "theoper",
            reason: "SetByOperator"
          ),
          RemoveFromQueueExpectedEntry(
            logMat,
            cntr: 3,
            queue: "q1",
            position: 0,
            elapsedMin: 0,
            operName: "myoper"
          ),
          AddToQueueExpectedEntry(
            logMat,
            cntr: 4,
            queue: "q1",
            position: 0,
            operName: "theoper",
            reason: "SetByOperator"
          ),
        },
        options =>
          options
            .Using<DateTime>(
              ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4))
            )
            .WhenTypeIs<DateTime>()
            .Using<TimeSpan>(
              ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4))
            )
            .WhenTypeIs<TimeSpan>()
            .ComparingByMembers<LogEntry>()
      );
  }

  [Fact]
  public async Task QuarantinesMatOnPallet()
  {
    using var db = _repo.OpenConnection();

    var job = new Job()
    {
      UniqueStr = "uuu1",
      PartName = "p1",
      Processes = ImmutableList.Create(
        new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
      ),
      RouteStartUTC = DateTime.MinValue,
      RouteEndUTC = DateTime.MinValue,
      Archived = false,
      Cycles = 0
    };

    db.AddJobs(
      new NewJobs() { ScheduleId = "abcd", Jobs = ImmutableList.Create(job) },
      null,
      addAsCopiedToSystem: true
    );

    db.AllocateMaterialID("uuu1", "p1", 2).Should().Be(1);

    await SetCurrentState(
      palStateUpdated: false,
      executeAction: false,
      curSt: new CurrentStatus()
      {
        TimeOfCurrentStatusUTC = DateTime.UtcNow,
        Material = ImmutableList.Create(
          MatOnPal(matId: 1, uniq: "uuu1", part: "p1", proc: 1, path: 2, serial: "aaa", pal: "4")
        ),
        Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
        Pallets = ImmutableDictionary<string, PalletStatus>.Empty,
        Alarms = ImmutableList<string>.Empty,
        QueueSizes = ImmutableDictionary<string, QueueSize>.Empty
      }
    );

    var newStatusTask = CreateTaskToWaitForNewCellState();

    _jq.SignalMaterialForQuarantine(1, "q1", "theoper");

    await newStatusTask;

    var logMat = new LogMaterial(
      matID: 1,
      uniq: "uuu1",
      proc: 1,
      part: "p1",
      numProc: 2,
      serial: "",
      workorder: "",
      face: ""
    );

    db.GetLogForMaterial(materialID: 1)
      .Should()
      .BeEquivalentTo(
        new[] { SignalQuarantineExpectedEntry(logMat, cntr: 1, pal: "4", queue: "q1", operName: "theoper") },
        options =>
          options
            .Using<DateTime>(
              ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4))
            )
            .WhenTypeIs<DateTime>()
            .ComparingByMembers<LogEntry>()
      );
  }

  [Fact]
  public async Task QuarantinesMatInQueue()
  {
    using var db = _repo.OpenConnection();

    var job = new Job()
    {
      UniqueStr = "uuu1",
      PartName = "p1",
      Cycles = 5,
      Processes = ImmutableList.Create(
        new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) },
        new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
      ),
      RouteStartUTC = DateTime.MinValue,
      RouteEndUTC = DateTime.MinValue,
      Archived = false,
    };
    db.AddJobs(
      new NewJobs() { ScheduleId = "abcd", Jobs = ImmutableList.Create<Job>(job) },
      null,
      addAsCopiedToSystem: true
    );

    db.AllocateMaterialID("uuu1", "p1", 2).Should().Be(1);
    db.RecordSerialForMaterialID(materialID: 1, proc: 1, serial: "aaa");
    db.RecordLoadStart(
      new[]
      {
        new EventLogMaterial()
        {
          MaterialID = 1,
          Process = 1,
          Face = ""
        }
      },
      "3",
      2,
      DateTime.UtcNow
    );

    _jq.SetMaterialInQueue(materialId: 1, queue: "q1", position: -1, operatorName: null);

    await SetCurrentState(
      palStateUpdated: false,
      executeAction: false,
      curSt: new CurrentStatus()
      {
        TimeOfCurrentStatusUTC = DateTime.UtcNow,
        Material = ImmutableList.Create(
          QueuedMat(matId: 1, job: job, part: "p1", proc: 1, path: 2, serial: "aaa", queue: "q1", pos: 0)
        ),
        Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
        Pallets = ImmutableDictionary<string, PalletStatus>.Empty,
        Alarms = ImmutableList<string>.Empty,
        QueueSizes = ImmutableDictionary<string, QueueSize>.Empty
      }
    );

    var newStatusTask = CreateTaskToWaitForNewCellState();

    _jq.SignalMaterialForQuarantine(1, "q2", "theoper");

    await newStatusTask;

    var logMat = new LogMaterial(
      matID: 1,
      uniq: "uuu1",
      proc: 1,
      part: "p1",
      numProc: 2,
      serial: "aaa",
      workorder: "",
      face: ""
    );

    db.GetLogForMaterial(materialID: 1)
      .Should()
      .BeEquivalentTo(
        new[]
        {
          MarkExpectedEntry(logMat, cntr: 1, serial: "aaa"),
          LoadStartExpectedEntry(logMat, cntr: 2, pal: "3", lul: 2),
          AddToQueueExpectedEntry(
            logMat,
            cntr: 3,
            queue: "q1",
            position: 0,
            operName: null,
            reason: "SetByOperator"
          ),
          RemoveFromQueueExpectedEntry(
            logMat,
            cntr: 4,
            queue: "q1",
            position: 0,
            elapsedMin: 0,
            operName: "theoper"
          ),
          AddToQueueExpectedEntry(
            logMat,
            cntr: 5,
            queue: "q2",
            position: 0,
            operName: "theoper",
            reason: "SetByOperator"
          )
        },
        options =>
          options
            .Using<DateTime>(
              ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4))
            )
            .WhenTypeIs<DateTime>()
            .Using<TimeSpan>(
              ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4))
            )
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
      result: serial
    );
    return e;
  }

  private LogEntry LoadStartExpectedEntry(
    LogMaterial mat,
    long cntr,
    string pal,
    int lul,
    DateTime? timeUTC = null
  )
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
      result: "LOAD"
    );
    return e;
  }

  private LogEntry AddToQueueExpectedEntry(
    LogMaterial mat,
    long cntr,
    string queue,
    int position,
    DateTime? timeUTC = null,
    string operName = null,
    string reason = null
  )
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
      result: ""
    );
    if (!string.IsNullOrEmpty(operName))
    {
      e %= en => en.ProgramDetails.Add("operator", operName);
    }
    return e;
  }

  private LogEntry SignalQuarantineExpectedEntry(
    LogMaterial mat,
    long cntr,
    string pal,
    string queue,
    DateTime? timeUTC = null,
    string operName = null
  )
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
      result: "QuarantineAfterUnload"
    );
    if (!string.IsNullOrEmpty(operName))
    {
      e %= en => en.ProgramDetails.Add("operator", operName);
    }
    return e;
  }

  private LogEntry RemoveFromQueueExpectedEntry(
    LogMaterial mat,
    long cntr,
    string queue,
    int position,
    int elapsedMin,
    DateTime? timeUTC = null,
    string operName = null
  )
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
      elapsed: TimeSpan.FromMinutes(elapsedMin),
      active: TimeSpan.Zero
    );
    if (!string.IsNullOrEmpty(operName))
    {
      e %= en => en.ProgramDetails.Add("operator", operName);
    }
    return e;
  }

  private InProcessMaterial QueuedMat(
    long matId,
    Job job,
    string part,
    int proc,
    int path,
    string serial,
    string queue,
    int pos
  )
  {
    return new InProcessMaterial()
    {
      MaterialID = matId,
      JobUnique = job?.UniqueStr,
      PartName = part,
      Process = proc,
      Path = path,
      Serial = serial,
      SignaledInspections = ImmutableList<string>.Empty,
      Location = new InProcessMaterialLocation()
      {
        Type = InProcessMaterialLocation.LocType.InQueue,
        CurrentQueue = queue,
        QueuePosition = pos,
      },
      Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting }
    };
  }

  private InProcessMaterial MatOnPal(
    long matId,
    string uniq,
    string part,
    int proc,
    int path,
    string serial,
    string pal
  )
  {
    return new InProcessMaterial()
    {
      MaterialID = matId,
      JobUnique = uniq,
      PartName = part,
      Process = proc,
      Path = path,
      Serial = serial,
      SignaledInspections = ImmutableList<string>.Empty,
      Location = new InProcessMaterialLocation()
      {
        Type = InProcessMaterialLocation.LocType.OnPallet,
        Pallet = pal,
      },
      Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting }
    };
  }
  #endregion
}
