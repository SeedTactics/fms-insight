/* Copyright (c) 2024, John Lenz

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
using System.Threading.Tasks;
using AutoFixture;
using BlackMaple.MachineFramework;
using FluentAssertions;
using Xunit;

namespace MachineWatchTest;

public sealed class JobAndQueueSpec : ISynchronizeCellState<JobAndQueueSpec.MockCellState>, IDisposable
{
  public record MockCellState : ICellState
  {
    public long Uniq { get; init; }
    public CurrentStatus CurrentStatus { get; init; }
    public bool StateUpdated { get; init; }
    public TimeSpan TimeUntilNextRefresh => TimeSpan.FromHours(50);
  }

  private readonly RepositoryConfig _repo;
  private FMSSettings _settings;
  private JobsAndQueuesFromDb<MockCellState> _jq;
  private readonly Fixture _fixture;

  public JobAndQueueSpec()
  {
    _repo = RepositoryConfig.InitializeMemoryDB(null);
    _fixture = new Fixture();
    _fixture.Customizations.Add(new ImmutableSpecimenBuilder());

    _settings = new FMSSettings() { };
    _settings.Queues.Add("q1", new QueueInfo());
    _settings.Queues.Add("q2", new QueueInfo());
  }

  void IDisposable.Dispose()
  {
    _repo?.Dispose();
    if (_jq != null)
    {
      _jq?.Dispose();
    }
  }

  #region Sync Cell State
  private MockCellState _curSt;
  private string _calculateStateError = null;
  private bool _executeActions;
  private string _executeActionError = null;
  public event Action NewCellState;
  private bool _expectsDecrement = false;

  public bool AllowQuarantineToCancelLoad { get; private set; } = false;
  public bool AddJobsAsCopiedToSystem => true;

  private async Task StartSyncThread()
  {
    var newCellSt = CreateTaskToWaitForNewCellState();
    _jq = new JobsAndQueuesFromDb<MockCellState>(_repo, _settings, this);
    _jq.OnNewCurrentStatus += OnNewCurrentStatus;
    _jq.StartThread();
    await newCellSt;
  }

  public IEnumerable<string> CheckNewJobs(IRepository db, NewJobs jobs)
  {
    return [];
  }

  MockCellState ISynchronizeCellState<MockCellState>.CalculateCellState(IRepository db)
  {
    if (_calculateStateError != null)
    {
      throw new Exception(_calculateStateError);
    }
    else
    {
      return _curSt;
    }
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
          Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
          Material = [],
          Alarms = [],
          Queues = ImmutableDictionary<string, QueueInfo>.Empty
        }
      };
      return true;
    }
    else if (_executeActionError != null)
    {
      throw new Exception(_executeActionError);
    }
    else
    {
      return false;
    }
  }

  bool ISynchronizeCellState<MockCellState>.DecrementJobs(IRepository db, MockCellState st)
  {
    _expectsDecrement.Should().BeTrue();
    var toDecr = _curSt.CurrentStatus.BuildJobsToDecrement();
    toDecr.Should().NotBeEmpty();
    db.AddNewDecrement(toDecr, nowUTC: _curSt.CurrentStatus.TimeOfCurrentStatusUTC);
    return true;
  }

  private TaskCompletionSource<CurrentStatus> _newCellStateTcs;

  private void OnNewCurrentStatus(CurrentStatus st)
  {
    var tcs = _newCellStateTcs;
    _newCellStateTcs = null;
    (tcs as object).Should().NotBeNull();
    tcs.SetResult(st);
  }

  private async Task SetCurrentState(bool stateUpdated, bool executeAction, CurrentStatus curSt = null)
  {
    curSt ??= new CurrentStatus()
    {
      TimeOfCurrentStatusUTC = DateTime.UtcNow,
      Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
      Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
      Material = [],
      Alarms = [],
      Queues = ImmutableDictionary<string, QueueInfo>.Empty
    };
    _curSt = new MockCellState()
    {
      Uniq = 0,
      CurrentStatus = curSt,
      StateUpdated = stateUpdated
    };
    _executeActions = executeAction;

    // wait for NewCurrentStatus after raising NewCellState event
    var tcs = new TaskCompletionSource<CurrentStatus>();
    (_newCellStateTcs as object).Should().BeNull();
    _newCellStateTcs = tcs;
    NewCellState?.Invoke();
    (await tcs.Task).Should().Be(curSt);
  }

  private async Task SetCurrentMaterial(ImmutableList<InProcessMaterial> material)
  {
    await SetCurrentState(
      stateUpdated: false,
      executeAction: false,
      curSt: _curSt.CurrentStatus with
      {
        Material = material
      }
    );
  }

  private Task CreateTaskToWaitForNewCellState()
  {
    var st = _curSt?.CurrentStatus;
    (_newCellStateTcs as object).Should().BeNull();
    var tcs = new TaskCompletionSource<CurrentStatus>();
    _newCellStateTcs = tcs;
    return tcs.Task.ContinueWith(s =>
    {
      if (st == null)
      {
        s.Result.Should()
          .BeEquivalentTo(
            new CurrentStatus()
            {
              TimeOfCurrentStatusUTC = DateTime.UtcNow,
              Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
              Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
              Material = [],
              Queues = ImmutableDictionary<string, QueueInfo>.Empty,
              Alarms = ["FMS Insight is starting up..."]
            },
            options =>
              options
                .Using<DateTime>(ctx =>
                  ctx.Subject.Should().BeCloseTo(ctx.Expectation, TimeSpan.FromSeconds(4))
                )
                .WhenTypeIs<DateTime>()
          );
      }
      else
      {
        s.Result.Should().Be(st);
      }
    });
  }
  #endregion

  [Fact]
  public async void HandlesErrorDuringCalculateState()
  {
    await StartSyncThread();

    var curSt = new CurrentStatus()
    {
      TimeOfCurrentStatusUTC = DateTime.UtcNow,
      Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
      Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
      Material = [],
      Alarms = ["An alarm"],
      Queues = ImmutableDictionary<string, QueueInfo>.Empty
    };

    await SetCurrentState(stateUpdated: true, executeAction: false, curSt: curSt);

    _calculateStateError = "An error occurred";

    var tcs = new TaskCompletionSource<CurrentStatus>();
    (_newCellStateTcs as object).Should().BeNull();
    _newCellStateTcs = tcs;
    NewCellState?.Invoke();
    (await tcs.Task)
      .Should()
      .BeEquivalentTo(
        curSt with
        {
          Alarms =
          [
            "An alarm",
            "Error communicating with machines: An error occurred. Will try again in a few minutes."
          ]
        },
        options =>
          options
            .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, TimeSpan.FromSeconds(4)))
            .WhenTypeIs<DateTime>()
      );

    // the error handling waits 2 seconds and tries again, so wait 2.1 seconds.
    // This checks that since the error is the same the OnNewCurrentStatus is not raised,
    // since it would get an error since OnNewCurrentStatus checks for task completion to be null
    await Task.Delay(TimeSpan.FromSeconds(2.1));

    // clear the error, and then wait for the new current status again
    // CreateTaskToWaitForNewCellState checks that the current status matches with curSt without the extra alarm
    var newStatusTask = CreateTaskToWaitForNewCellState();
    _calculateStateError = null;
    await newStatusTask;
  }

  [Fact]
  public async Task ErrorsDuringActions()
  {
    await StartSyncThread();

    await SetCurrentState(stateUpdated: true, executeAction: false);

    var curSt = new CurrentStatus()
    {
      TimeOfCurrentStatusUTC = DateTime.UtcNow,
      Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
      Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
      Material = [],
      Alarms = ["An alarm"],
      Queues = ImmutableDictionary<string, QueueInfo>.Empty
    };

    _curSt = new MockCellState()
    {
      Uniq = 0,
      CurrentStatus = curSt,
      StateUpdated = false
    };
    _executeActionError = "An error occurred";

    // A single NewCellState should load the new _curSt with An alarm and
    // then after that get the error during the action
    var tcs = new TaskCompletionSource<CurrentStatus>();
    (_newCellStateTcs as object).Should().BeNull();
    _newCellStateTcs = tcs;
    NewCellState?.Invoke();
    (await tcs.Task)
      .Should()
      .BeEquivalentTo(
        curSt with
        {
          Alarms =
          [
            "An alarm",
            "Error communicating with machines: An error occurred. Will try again in a few minutes."
          ]
        },
        options =>
          options
            .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, TimeSpan.FromSeconds(4)))
            .WhenTypeIs<DateTime>()
      );

    // the error handling waits 2 seconds and tries again, so wait 2.1 seconds.
    // This checks that since the error is the same the OnNewCurrentStatus is not raised,
    // since it would get an error since OnNewCurrentStatus checks for task completion to be null
    await Task.Delay(TimeSpan.FromSeconds(2.1));

    // clear the error, and then wait for the new current status again
    // CreateTaskToWaitForNewCellState checks that the current status matches with curSt without the extra alarm
    var newStatusTask = CreateTaskToWaitForNewCellState();
    _executeActionError = null;
    await newStatusTask;
  }

  #region Jobs

  private Job RandJob()
  {
    var job = _fixture.Create<Job>();
    return job with
    {
      RouteEndUTC = job.RouteStartUTC.AddHours(1),
      Processes = job
        .Processes.Select(
          (proc, procIdx) =>
            new ProcessInfo()
            {
              Paths = proc
                .Paths.Select(path => path with { Casting = procIdx == 0 ? path.Casting : null })
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
    await StartSyncThread();

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
      new NewJobs() { ScheduleId = "old", Jobs = [completedJob, toKeepJob] },
      null,
      addAsCopiedToSystem: true
    );

    await SetCurrentState(
      stateUpdated: false,
      executeAction: false,
      curSt: new CurrentStatus()
      {
        TimeOfCurrentStatusUTC = DateTime.UtcNow,
        Jobs = ImmutableDictionary<string, ActiveJob>
          .Empty.Add(completedActive.UniqueStr, completedActive)
          .Add(toKeepJob.UniqueStr, toKeepActive),
        Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
        Material = [],
        Alarms = [],
        Queues = ImmutableDictionary<string, QueueInfo>.Empty
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
      Jobs = [newJob1, newJob2],
      Programs =
      [
        new NewProgramContent()
        {
          ProgramName = "prog1",
          ProgramContent = "content1",
          Revision = 0
        },
        new NewProgramContent()
        {
          ProgramName = "prog2",
          ProgramContent = "content2",
          Revision = 0
        },
      ]
    };

    var newStatusTask = CreateTaskToWaitForNewCellState();

    ((IJobAndQueueControl)_jq).AddJobs(newJobs, null);

    await newStatusTask;

    db.LoadUnarchivedJobs()
      .Select(j => j.UniqueStr)
      .Should()
      .BeEquivalentTo([completedJob.UniqueStr, toKeepJob.UniqueStr, newJob1.UniqueStr, newJob2.UniqueStr]);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task Decrement(bool byDate)
  {
    await StartSyncThread();
    var now = DateTime.UtcNow;
    using var db = _repo.OpenConnection();

    var j1 = RandJob() with { UniqueStr = "u1", Cycles = 12, };
    var j1Active = j1.CloneToDerived<ActiveJob, Job>() with { RemainingToStart = 7 };

    var j2 = RandJob() with { UniqueStr = "u2", Cycles = 22, };
    var j2Active = j2.CloneToDerived<ActiveJob, Job>() with { RemainingToStart = 0 };

    db.AddJobs(new NewJobs() { ScheduleId = "old", Jobs = [j1, j2] }, null, addAsCopiedToSystem: true);

    await SetCurrentState(
      stateUpdated: false,
      executeAction: false,
      curSt: new CurrentStatus()
      {
        TimeOfCurrentStatusUTC = now,
        Jobs = ImmutableDictionary<string, ActiveJob>
          .Empty.Add(j1.UniqueStr, j1Active)
          .Add(j2.UniqueStr, j2Active),
        Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
        Material = [],
        Alarms = [],
        Queues = ImmutableDictionary<string, QueueInfo>.Empty
      }
    );

    var newStatusTask = CreateTaskToWaitForNewCellState();

    IEnumerable<JobAndDecrementQuantity> decrs;
    _expectsDecrement = true;
    if (byDate)
    {
      decrs = ((IJobAndQueueControl)_jq).DecrementJobQuantites(DateTime.UtcNow.AddHours(-5));
    }
    else
    {
      decrs = ((IJobAndQueueControl)_jq).DecrementJobQuantites(-1);
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
    await StartSyncThread();

    await SetCurrentState(stateUpdated: false, executeAction: false);
    var newStatusTask = CreateTaskToWaitForNewCellState();

    //add a casting
    _jq.AddUnallocatedCastingToQueue(
        casting: "c1",
        qty: 2,
        queue: "q1",
        serial: ["aaa"],
        workorder: null,
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
            .Using<DateTime?>(ctx =>
              ctx.Subject.Value.Should().BeCloseTo(ctx.Expectation.Value, TimeSpan.FromSeconds(4))
            )
            .WhenTypeIs<DateTime?>()
      );

    await newStatusTask;

    var mat1 = MkLogMat.Mk(
      matID: 1,
      uniq: "",
      proc: 0,
      part: "c1",
      numProc: 1,
      serial: "aaa",
      workorder: "",
      face: ""
    );
    var mat2 = MkLogMat.Mk(
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
            reason: "",
            position: 0,
            elapsedMin: 0,
            operName: "theoper"
          ),
        },
        options =>
          options
            .Using<DateTime>(ctx =>
              ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4))
            )
            .WhenTypeIs<DateTime>()
            .Using<TimeSpan>(ctx =>
              ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4))
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
            .Using<DateTime>(ctx =>
              ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4))
            )
            .WhenTypeIs<DateTime>()
            .Using<TimeSpan>(ctx =>
              ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4))
            )
            .WhenTypeIs<TimeSpan>()
            .ComparingByMembers<LogEntry>()
      );

    var expectedMat2 = QueuedMat(
      matId: 2,
      job: null,
      part: "c1",
      proc: 0,
      path: 1,
      serial: "",
      queue: "q1",
      pos: 1
    );
    await SetCurrentMaterial(
      [
        expectedMat2 with
        {
          Location = new InProcessMaterialLocation() { Type = InProcessMaterialLocation.LocType.OnPallet, }
        }
      ]
    );

    _jq.Invoking(j => j.RemoveMaterialFromAllQueues([2L], "theoper"))
      .Should()
      .Throw<BadRequestException>()
      .WithMessage("Material on pallet can not be removed from queues");

    await SetCurrentMaterial(
      [
        expectedMat2 with
        {
          Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Loading }
        }
      ]
    );

    _jq.Invoking(j => j.RemoveMaterialFromAllQueues([2L], "theoper"))
      .Should()
      .Throw<BadRequestException>()
      .WithMessage("Only waiting material can be removed from queues");

    db.GetMaterialInAllQueues().Should().Contain(m => m.MaterialID == 2);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  public async Task UnprocessedMaterial(int lastCompletedProcess)
  {
    await StartSyncThread();
    using var db = _repo.OpenConnection();

    var job = new Job()
    {
      UniqueStr = "uuu1",
      PartName = "p1",
      Cycles = 0,
      Processes =
      [
        new ProcessInfo() { Paths = [JobLogTest.EmptyPath] },
        new ProcessInfo() { Paths = [JobLogTest.EmptyPath] },
      ],
      RouteStartUTC = DateTime.MinValue,
      RouteEndUTC = DateTime.MinValue,
      Archived = false,
    };
    db.AddJobs(new NewJobs() { ScheduleId = "abcd", Jobs = [job] }, null, addAsCopiedToSystem: true);

    await SetCurrentState(stateUpdated: false, executeAction: false);

    //add an allocated material
    var expectedMat1 = QueuedMat(
      matId: 1,
      job: job,
      part: "p1",
      proc: lastCompletedProcess,
      path: 1,
      serial: "aaa",
      workorder: "work11",
      queue: "q1",
      pos: 0
    );

    var newStatusTask = CreateTaskToWaitForNewCellState();
    _jq.AddUnprocessedMaterialToQueue(
        "uuu1",
        process: lastCompletedProcess,
        queue: "q1",
        position: 0,
        serial: "aaa",
        workorder: "work11",
        operatorName: "theoper"
      )
      .Should()
      .BeEquivalentTo(expectedMat1);
    await newStatusTask;

    db.GetMaterialDetails(1)
      .Should()
      .BeEquivalentTo(
        new MaterialDetails()
        {
          MaterialID = 1,
          PartName = "p1",
          NumProcesses = 2,
          Serial = "aaa",
          Workorder = "work11",
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
            Workorder = "work11",
            NextProcess = lastCompletedProcess + 1,
            Paths = ImmutableDictionary<int, int>.Empty
          }
        }
      );

    await SetCurrentMaterial([expectedMat1]);

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
            Workorder = "work11",
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
            Workorder = "work11",
            NextProcess = lastCompletedProcess + 1,
            Paths = ImmutableDictionary<int, int>.Empty
          }
        }
      );

    var logMat = MkLogMat.Mk(
      matID: 1,
      uniq: "uuu1",
      proc: lastCompletedProcess,
      part: "p1",
      numProc: 2,
      serial: "aaa",
      workorder: "work11",
      face: ""
    );
    var expectedLog = new[]
    {
      MarkExpectedEntry(logMat, cntr: 1, serial: "aaa"),
      AssignWorkExpectedEntry(logMat, cntr: 2, workorder: "work11"),
      AddToQueueExpectedEntry(
        logMat,
        cntr: 3,
        queue: "q1",
        position: 0,
        operName: "theoper",
        reason: "SetByOperator"
      ),
      RemoveFromQueueExpectedEntry(
        logMat,
        cntr: 4,
        queue: "q1",
        position: 0,
        reason: "",
        elapsedMin: 0,
        operName: "myoper"
      ),
      AddToQueueExpectedEntry(
        logMat,
        cntr: 5,
        queue: "q1",
        position: 0,
        operName: "theoper",
        reason: "SetByOperator"
      ),
    };

    db.GetLogForMaterial(materialID: 1)
      .Should()
      .BeEquivalentTo(
        expectedLog,
        options =>
          options
            .Using<DateTime>(ctx =>
              ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4))
            )
            .WhenTypeIs<DateTime>()
            .Using<TimeSpan>(ctx =>
              ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4))
            )
            .WhenTypeIs<TimeSpan>()
            .ComparingByMembers<LogEntry>()
      );

    // should error if it is loading or on a pallet
    await SetCurrentMaterial(
      [
        expectedMat1 with
        {
          Location = new InProcessMaterialLocation() { Type = InProcessMaterialLocation.LocType.OnPallet }
        }
      ]
    );

    _jq.Invoking(j => j.SetMaterialInQueue(materialId: 1, "q1", 3, "oper"))
      .Should()
      .Throw<BadRequestException>()
      .WithMessage("Material on pallet can not be moved to a queue");

    await SetCurrentMaterial(
      [
        expectedMat1 with
        {
          Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Loading, }
        }
      ]
    );

    _jq.Invoking(j => j.SetMaterialInQueue(materialId: 1, "q2", 3, "oper"))
      .Should()
      .Throw<BadRequestException>()
      .WithMessage("Only waiting material can be moved between queues");

    db.GetLogForMaterial(materialID: 1)
      .Should()
      .BeEquivalentTo(
        expectedLog,
        options =>
          options
            .Using<DateTime>(ctx =>
              ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4))
            )
            .WhenTypeIs<DateTime>()
            .Using<TimeSpan>(ctx =>
              ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4))
            )
            .WhenTypeIs<TimeSpan>()
            .ComparingByMembers<LogEntry>()
      );
  }

  [Fact]
  public async Task AllowsSwapOfLoadingMaterial()
  {
    await StartSyncThread();
    using var db = _repo.OpenConnection();

    await SetCurrentState(stateUpdated: false, executeAction: false);

    var expectedMat1 = QueuedMat(
      matId: 1,
      job: null,
      part: "c1",
      proc: 0,
      path: 1,
      serial: "aaa",
      queue: "q1",
      pos: 0
    );
    var expectedMat2 = QueuedMat(
      matId: 2,
      job: null,
      part: "c1",
      proc: 0,
      path: 1,
      serial: "",
      queue: "q1",
      pos: 1
    );
    _jq.AddUnallocatedCastingToQueue(
        casting: "c1",
        qty: 2,
        queue: "q1",
        serial: ["aaa"],
        workorder: null,
        operatorName: "theoper"
      )
      .Should()
      .BeEquivalentTo(new[] { expectedMat1, expectedMat2 });

    await SetCurrentMaterial(
      [
        expectedMat1 with
        {
          Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Loading, }
        },
        expectedMat2,
      ]
    );

    db.GetMaterialInAllQueues().Select(m => m.MaterialID).Should().Equal([1L, 2L]);

    var newStatusTask = CreateTaskToWaitForNewCellState();

    _jq.SetMaterialInQueue(materialId: 1, "q1", 1, "oper");

    await newStatusTask;

    db.GetMaterialInAllQueues().Select(m => m.MaterialID).Should().Equal([2L, 1L]);

    _jq.Invoking(j => j.SetMaterialInQueue(materialId: 1, "q2", -1, "oper"))
      .Should()
      .Throw<BadRequestException>()
      .WithMessage("Only waiting material can be moved between queues");
  }

  public record SignalQuarantineTheoryData
  {
    public enum QuarantineType
    {
      Add,
      Signal,
      Remove
    }

    public required InProcessMaterialLocation.LocType LocType { get; init; }
    public required InProcessMaterialAction.ActionType ActionType { get; init; }
    public required string QuarantineQueue { get; init; }
    public string Error { get; init; } = null;
    public QuarantineType? QuarantineAction { get; init; } = null;
    public int Process { get; init; } = 0;
    public string JobTransferQeuue { get; init; } = "q1";
    public bool AllowQuarantineToCancelLoad { get; init; } = false;
  }

  public static readonly IEnumerable<object[]> SignalTheoryData = new[]
  {
    new SignalQuarantineTheoryData
    {
      ActionType = InProcessMaterialAction.ActionType.Waiting,
      LocType = InProcessMaterialLocation.LocType.InQueue,
      QuarantineQueue = "quarqqq",
      QuarantineAction = SignalQuarantineTheoryData.QuarantineType.Add
    },
    new SignalQuarantineTheoryData()
    {
      ActionType = InProcessMaterialAction.ActionType.Waiting,
      LocType = InProcessMaterialLocation.LocType.InQueue,
      QuarantineQueue = null,
      QuarantineAction = SignalQuarantineTheoryData.QuarantineType.Remove
    },
    new SignalQuarantineTheoryData
    {
      ActionType = InProcessMaterialAction.ActionType.Waiting,
      LocType = InProcessMaterialLocation.LocType.Free,
      QuarantineQueue = "quarqqq",
      QuarantineAction = SignalQuarantineTheoryData.QuarantineType.Add
    },
    // Now OnPallet with various tests for transfer queues and proces
    new SignalQuarantineTheoryData
    {
      ActionType = InProcessMaterialAction.ActionType.Waiting,
      LocType = InProcessMaterialLocation.LocType.OnPallet,
      QuarantineQueue = "quarqqq",
      Process = 1,
      JobTransferQeuue = "q1",
      QuarantineAction = SignalQuarantineTheoryData.QuarantineType.Signal
    },
    new SignalQuarantineTheoryData
    {
      ActionType = InProcessMaterialAction.ActionType.UnloadToInProcess,
      LocType = InProcessMaterialLocation.LocType.OnPallet,
      QuarantineQueue = "quarqqq",
      Process = 1,
      JobTransferQeuue = "q1",
      QuarantineAction = SignalQuarantineTheoryData.QuarantineType.Signal
    },
    new SignalQuarantineTheoryData
    {
      ActionType = InProcessMaterialAction.ActionType.Loading,
      LocType = InProcessMaterialLocation.LocType.OnPallet,
      QuarantineQueue = "quarqqq",
      Process = 1,
      JobTransferQeuue = "q1",
      Error = "Material on pallet can not be quarantined while loading"
    },
    new SignalQuarantineTheoryData
    {
      ActionType = InProcessMaterialAction.ActionType.Loading,
      LocType = InProcessMaterialLocation.LocType.OnPallet,
      QuarantineQueue = "quarqqq",
      Process = 1,
      JobTransferQeuue = "q1",
      AllowQuarantineToCancelLoad = true,
      QuarantineAction = SignalQuarantineTheoryData.QuarantineType.Signal,
    },
    new SignalQuarantineTheoryData()
    {
      ActionType = InProcessMaterialAction.ActionType.Waiting,
      LocType = InProcessMaterialLocation.LocType.OnPallet,
      QuarantineQueue = null,
      Process = 2,
      JobTransferQeuue = "q1",
      QuarantineAction = SignalQuarantineTheoryData.QuarantineType.Signal
    },
    new SignalQuarantineTheoryData
    {
      ActionType = InProcessMaterialAction.ActionType.Machining,
      LocType = InProcessMaterialLocation.LocType.OnPallet,
      QuarantineQueue = "quarqqq",
      Process = 1,
      JobTransferQeuue = null,
      Error = "Can only signal material for quarantine if the current process and path has an output queue"
    },
    new SignalQuarantineTheoryData
    {
      ActionType = InProcessMaterialAction.ActionType.Machining,
      LocType = InProcessMaterialLocation.LocType.OnPallet,
      QuarantineQueue = "quarqqq",
      Process = 1,
      JobTransferQeuue = null,
      AllowQuarantineToCancelLoad = true,
      QuarantineAction = SignalQuarantineTheoryData.QuarantineType.Signal
    },
    new SignalQuarantineTheoryData
    {
      ActionType = InProcessMaterialAction.ActionType.Machining,
      LocType = InProcessMaterialLocation.LocType.OnPallet,
      QuarantineQueue = "quarqqq",
      Process = 2,
      JobTransferQeuue = null,
      QuarantineAction = SignalQuarantineTheoryData.QuarantineType.Signal
    },
    // Loading from a queue
    new SignalQuarantineTheoryData()
    {
      ActionType = InProcessMaterialAction.ActionType.Loading,
      LocType = InProcessMaterialLocation.LocType.InQueue,
      QuarantineQueue = "quarqqq",
      Error = "Invalid material state for quarantine"
    },
    new SignalQuarantineTheoryData()
    {
      ActionType = InProcessMaterialAction.ActionType.Loading,
      LocType = InProcessMaterialLocation.LocType.InQueue,
      QuarantineQueue = "quarqqq",
      AllowQuarantineToCancelLoad = true,
      QuarantineAction = SignalQuarantineTheoryData.QuarantineType.Add
    },
    new SignalQuarantineTheoryData()
    {
      ActionType = InProcessMaterialAction.ActionType.Loading,
      LocType = InProcessMaterialLocation.LocType.InQueue,
      QuarantineQueue = null,
      Error = "Invalid material state for quarantine"
    },
    new SignalQuarantineTheoryData()
    {
      ActionType = InProcessMaterialAction.ActionType.Loading,
      LocType = InProcessMaterialLocation.LocType.InQueue,
      QuarantineQueue = null,
      AllowQuarantineToCancelLoad = true,
      QuarantineAction = SignalQuarantineTheoryData.QuarantineType.Remove
    },
  }.Select(d => new object[] { d });

  [Theory]
  [MemberData(nameof(SignalTheoryData))]
  public async Task QuarantinesMatOnPallet(SignalQuarantineTheoryData data)
  {
    _settings = _settings with { QuarantineQueue = data.QuarantineQueue };
    AllowQuarantineToCancelLoad = data.AllowQuarantineToCancelLoad;
    await StartSyncThread();

    using var db = _repo.OpenConnection();

    var job = new ActiveJob()
    {
      UniqueStr = "uuu1",
      PartName = "p1",
      Processes =
      [
        new ProcessInfo() { Paths = [JobLogTest.EmptyPath with { OutputQueue = data.JobTransferQeuue }] },
        new ProcessInfo() { Paths = [JobLogTest.EmptyPath] },
      ],
      RouteStartUTC = DateTime.MinValue,
      RouteEndUTC = DateTime.MinValue,
      Archived = false,
      Cycles = 0,
      CopiedToSystem = true
    };

    db.AddJobs(new NewJobs() { ScheduleId = "abcd", Jobs = [job] }, null, addAsCopiedToSystem: true);

    db.AllocateMaterialID("uuu1", "p1", 2).Should().Be(1);
    var logMat = MkLogMat.Mk(
      matID: 1,
      uniq: "uuu1",
      proc: data.Process,
      part: "p1",
      numProc: 2,
      serial: "",
      workorder: "",
      face: ""
    );

    await SetCurrentState(
      stateUpdated: false,
      executeAction: false,
      curSt: new CurrentStatus()
      {
        TimeOfCurrentStatusUTC = DateTime.UtcNow,
        Jobs = ImmutableDictionary<string, ActiveJob>.Empty.Add(job.UniqueStr, job),
        Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
        Material = [],
        Alarms = [],
        Queues = ImmutableDictionary<string, QueueInfo>.Empty
      }
    );

    var expectedLog = new List<LogEntry>();
    if (data.LocType == InProcessMaterialLocation.LocType.InQueue)
    {
      db.RecordAddMaterialToQueue(
        new EventLogMaterial()
        {
          MaterialID = 1,
          Process = data.Process,
          Face = 0
        },
        queue: "q1",
        position: 0,
        operatorName: "anoper",
        reason: "Test"
      );

      expectedLog.Add(
        AddToQueueExpectedEntry(
          cntr: 1,
          mat: logMat,
          queue: "q1",
          position: 0,
          operName: "anoper",
          reason: "Test"
        )
      );
    }

    _jq.Invoking(j =>
        j.SignalMaterialForQuarantine(materialId: 1, operatorName: "theoper", reason: "a reason")
      )
      .Should()
      .Throw<BadRequestException>()
      .WithMessage("Material not found");

    var queuedMat = QueuedMat(
      matId: 1,
      job: job,
      part: "p1",
      proc: data.Process,
      path: 1,
      serial: "",
      queue: "q1",
      pos: 0
    );

    await SetCurrentMaterial(
      [
        queuedMat with
        {
          Action = new InProcessMaterialAction() { Type = data.ActionType },
          Location = new InProcessMaterialLocation()
          {
            Type = data.LocType,
            PalletNum = data.LocType == InProcessMaterialLocation.LocType.OnPallet ? 4 : null
          }
        }
      ]
    );

    if (data.Error != null)
    {
      _jq.Invoking(j => j.SignalMaterialForQuarantine(materialId: 1, "theoper", reason: "a reason"))
        .Should()
        .Throw<BadRequestException>()
        .WithMessage(data.Error);
    }
    else
    {
      var newStatusTask = CreateTaskToWaitForNewCellState();

      _jq.SignalMaterialForQuarantine(1, "theoper", reason: "signaling reason");

      await newStatusTask;

      if (data.QuarantineAction == SignalQuarantineTheoryData.QuarantineType.Signal)
      {
        expectedLog.Add(
          SignalQuarantineExpectedEntry(
            logMat,
            cntr: expectedLog.Count + 1,
            pal: 4,
            queue: data.QuarantineQueue ?? "",
            operName: "theoper",
            reason: "signaling reason"
          )
        );
      }
      else
      {
        expectedLog.Add(
          OperatorNoteExpectedEntry(
            logMat,
            cntr: expectedLog.Count + 1,
            note: "signaling reason",
            operName: "theoper"
          )
        );

        if (data.LocType == InProcessMaterialLocation.LocType.InQueue)
        {
          expectedLog.Add(
            RemoveFromQueueExpectedEntry(
              logMat,
              cntr: expectedLog.Count + 1,
              queue: "q1",
              position: 0,
              elapsedMin: 0,
              reason: data.QuarantineAction == SignalQuarantineTheoryData.QuarantineType.Add
                ? "MovingInQueue"
                : "Quarantine",
              operName: "theoper"
            )
          );
        }
      }

      if (data.QuarantineAction == SignalQuarantineTheoryData.QuarantineType.Add)
      {
        expectedLog.Add(
          AddToQueueExpectedEntry(
            logMat,
            cntr: expectedLog.Count + 1,
            queue: data.QuarantineQueue,
            position: 0,
            reason: "Quarantine",
            operName: "theoper"
          )
        );
      }
    }

    db.GetLogForMaterial(materialID: 1)
      .Should()
      .BeEquivalentTo(
        expectedLog,
        options =>
          options
            .Using<DateTime>(ctx =>
              ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4))
            )
            .WhenTypeIs<DateTime>()
            .Using<TimeSpan>(ctx =>
              ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4))
            )
            .WhenTypeIs<TimeSpan>()
            .ComparingByMembers<LogEntry>()
      );
  }

  private static LogEntry MarkExpectedEntry(
    LogMaterial mat,
    long cntr,
    string serial,
    DateTime? timeUTC = null
  )
  {
    var e = new LogEntry(
      cntr: cntr,
      mat: new[] { mat },
      pal: 0,
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

  private static LogEntry AssignWorkExpectedEntry(
    LogMaterial mat,
    long cntr,
    string workorder,
    DateTime? timeUTC = null
  )
  {
    var e = new LogEntry(
      cntr: cntr,
      mat: new[] { mat },
      pal: 0,
      ty: LogType.OrderAssignment,
      locName: "Order",
      locNum: 1,
      prog: "",
      start: false,
      endTime: timeUTC ?? DateTime.UtcNow,
      result: workorder
    );
    return e;
  }

  private static LogEntry LoadStartExpectedEntry(
    LogMaterial mat,
    long cntr,
    int pal,
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

  private static LogEntry AddToQueueExpectedEntry(
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
      pal: 0,
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

  private static LogEntry SignalQuarantineExpectedEntry(
    LogMaterial mat,
    long cntr,
    int pal,
    string queue,
    DateTime? timeUTC = null,
    string operName = null,
    string reason = null
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
    if (!string.IsNullOrEmpty(reason))
    {
      e = e with { ProgramDetails = e.ProgramDetails.Add("note", reason) };
    }
    return e;
  }

  private static LogEntry RemoveFromQueueExpectedEntry(
    LogMaterial mat,
    long cntr,
    string queue,
    int position,
    int elapsedMin,
    string reason,
    DateTime? timeUTC = null,
    string operName = null
  )
  {
    var e = new LogEntry(
      cntr: cntr,
      mat: new[] { mat },
      pal: 0,
      ty: LogType.RemoveFromQueue,
      locName: queue,
      locNum: position,
      prog: reason ?? "",
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

  private static InProcessMaterial QueuedMat(
    long matId,
    Job job,
    string part,
    int proc,
    int path,
    string serial,
    string queue,
    int pos,
    string workorder = null
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
      WorkorderId = workorder,
      SignaledInspections = [],
      QuarantineAfterUnload = null,
      Location = new InProcessMaterialLocation()
      {
        Type = InProcessMaterialLocation.LocType.InQueue,
        CurrentQueue = queue,
        QueuePosition = pos,
      },
      Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting }
    };
  }

  private static LogEntry OperatorNoteExpectedEntry(
    LogMaterial mat,
    long cntr,
    string note,
    DateTime? timeUTC = null,
    string operName = null
  )
  {
    var e = new LogEntry(
      cntr: cntr,
      mat: new[] { mat },
      pal: 0,
      ty: LogType.GeneralMessage,
      locName: "Message",
      locNum: 1,
      prog: "OperatorNotes",
      start: false,
      endTime: timeUTC ?? DateTime.UtcNow,
      result: "Operator Notes"
    );
    e = e with { ProgramDetails = ImmutableDictionary<string, string>.Empty.Add("note", note) };
    if (!string.IsNullOrEmpty(operName))
    {
      e %= en => en.ProgramDetails.Add("operator", operName);
    }
    return e;
  }

  private static InProcessMaterial MatOnPal(
    long matId,
    string uniq,
    string part,
    int proc,
    int path,
    string serial,
    int pal
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
      SignaledInspections = [],
      QuarantineAfterUnload = null,
      Location = new InProcessMaterialLocation()
      {
        Type = InProcessMaterialLocation.LocType.OnPallet,
        PalletNum = pal,
      },
      Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting }
    };
  }
  #endregion
}
