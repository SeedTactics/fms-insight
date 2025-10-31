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
using Shouldly;

namespace BlackMaple.FMSInsight.Tests;

// warning that Test methods should not assign instance data
#pragma warning disable TUnit0018

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
  private ServerSettings _serverSettings;
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

    _serverSettings = new ServerSettings();

    _curSt = new MockCellState()
    {
      CurrentStatus = new CurrentStatus()
      {
        TimeOfCurrentStatusUTC = DateTime.UtcNow,
        Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
        Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
        Material = [],
        Alarms = ["Initial First Startup Status"],
        Queues = ImmutableDictionary<string, QueueInfo>.Empty,
      },
    };
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

  private async Task StartSyncThread(string extraAlarm = null)
  {
    var newCellSt = CreateTaskToWaitForNewStatus();
    _jq = new JobsAndQueuesFromDb<MockCellState>(_repo, _serverSettings, _settings, this, startThread: false);
    _jq.OnNewCurrentStatus += OnNewCurrentStatus;

    // before starting (or while starting), the current status is Starting up
    var initial = _jq.GetCurrentStatus();
    initial.TimeOfCurrentStatusUTC.ShouldBe(DateTime.UtcNow, tolerance: TimeSpan.FromSeconds(4));
    initial.ShouldBeEquivalentTo(
      new CurrentStatus()
      {
        TimeOfCurrentStatusUTC = initial.TimeOfCurrentStatusUTC,
        Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
        Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
        Material = [],
        Queues = ImmutableDictionary<string, QueueInfo>.Empty,
        Alarms = ["FMS Insight is starting up..."],
      }
    );

    _jq.StartThread();

    var actual = await newCellSt;

    actual.TimeOfCurrentStatusUTC.ShouldBe(DateTime.UtcNow, tolerance: TimeSpan.FromSeconds(6));
    actual.ShouldBeEquivalentTo(
      new CurrentStatus()
      {
        TimeOfCurrentStatusUTC = actual.TimeOfCurrentStatusUTC,
        Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
        Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
        Material = [],
        Queues = ImmutableDictionary<string, QueueInfo>.Empty,
        Alarms = !string.IsNullOrEmpty(extraAlarm)
          ? ["Initial First Startup Status", extraAlarm]
          : ["Initial First Startup Status"],
      }
    );
  }

  IEnumerable<string> ISynchronizeCellState<MockCellState>.CheckNewJobs(IRepository db, NewJobs jobs)
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
    st.ShouldBe(_curSt);
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
          Queues = ImmutableDictionary<string, QueueInfo>.Empty,
        },
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
    _expectsDecrement.ShouldBeTrue();
    var toDecr = _curSt.CurrentStatus.BuildJobsToDecrement();
    toDecr.ShouldNotBeEmpty();
    db.AddNewDecrement(toDecr, nowUTC: _curSt.CurrentStatus.TimeOfCurrentStatusUTC);
    return true;
  }

  private TaskCompletionSource<CurrentStatus> _newCellStateTcs;

  private void OnNewCurrentStatus(CurrentStatus st)
  {
    var tcs = _newCellStateTcs;
    _newCellStateTcs = null;
    tcs.ShouldNotBeNull();
    tcs.SetResult(st);
  }

  private Task<CurrentStatus> CreateTaskToWaitForNewStatus()
  {
    (_newCellStateTcs as object).ShouldBeNull();
    var tcs = new TaskCompletionSource<CurrentStatus>();
    _newCellStateTcs = tcs;
    return tcs.Task;
  }
  #endregion

  #region Expected Status

  private async Task SetCurrentState(bool stateUpdated, bool executeAction, CurrentStatus curSt = null)
  {
    curSt ??= new CurrentStatus()
    {
      TimeOfCurrentStatusUTC = DateTime.UtcNow,
      Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
      Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
      Material = [],
      Alarms = [],
      Queues = ImmutableDictionary<string, QueueInfo>.Empty,
    };
    _curSt = new MockCellState()
    {
      Uniq = 0,
      CurrentStatus = curSt,
      StateUpdated = stateUpdated,
    };
    _executeActions = executeAction;

    // wait for NewCurrentStatus after raising NewCellState event
    var task = CreateTaskToWaitForNewStatus();
    NewCellState?.Invoke();
    (await task).ShouldBe(curSt);
  }

  private async Task SetCurrentMaterial(ImmutableList<InProcessMaterial> material)
  {
    await SetCurrentState(
      stateUpdated: false,
      executeAction: false,
      curSt: _curSt.CurrentStatus with
      {
        Material = material,
      }
    );
  }

  #endregion

  [Test]
  public async Task HandlesErrorDuringCalculateState()
  {
    await StartSyncThread();

    var curSt = new CurrentStatus()
    {
      TimeOfCurrentStatusUTC = DateTime.UtcNow,
      Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
      Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
      Material = [],
      Alarms = ["An alarm"],
      Queues = ImmutableDictionary<string, QueueInfo>.Empty,
    };

    await SetCurrentState(stateUpdated: true, executeAction: false, curSt: curSt);

    _calculateStateError = "An error occurred";

    var task = CreateTaskToWaitForNewStatus();
    NewCellState?.Invoke();
    (await task).ShouldBeEquivalentTo(
      curSt with
      {
        Alarms =
        [
          "An alarm",
          "Error communicating with machines: An error occurred. Will try again in a few minutes.",
        ],
      }
    );

    // the error handling waits 2 seconds and tries again, so wait 3 seconds.
    // This checks that since the error is the same the OnNewCurrentStatus is not raised,
    // since it would get an error since OnNewCurrentStatus checks for task completion to be null
    await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.Execution.CancellationToken);

    // clear the error, and then wait for a new current status since the error changed
    var newStatusTask = CreateTaskToWaitForNewStatus();
    _calculateStateError = null;
    (await newStatusTask).ShouldBe(_curSt.CurrentStatus);
  }

  [Test]
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
      Queues = ImmutableDictionary<string, QueueInfo>.Empty,
    };

    _curSt = new MockCellState()
    {
      Uniq = 0,
      CurrentStatus = curSt,
      StateUpdated = false,
    };
    _executeActionError = "An error occurred";

    // A single NewCellState should load the new _curSt with An alarm and
    // then after that get the error during the action
    var task = CreateTaskToWaitForNewStatus();
    NewCellState?.Invoke();
    (await task).ShouldBeEquivalentTo(
      curSt with
      {
        Alarms =
        [
          "An alarm",
          "Error communicating with machines: An error occurred. Will try again in a few minutes.",
        ],
      }
    );

    // the error handling waits 2 seconds and tries again, so wait 3 seconds.
    // This checks that since the error is the same the OnNewCurrentStatus is not raised,
    // since it would get an error since OnNewCurrentStatus checks for task completion to be null
    await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.Execution.CancellationToken);

    // clear the error, and then wait for the new current status again
    var newStatusTask = CreateTaskToWaitForNewStatus();
    _executeActionError = null;
    (await newStatusTask).ShouldBe(_curSt.CurrentStatus);
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
                .ToImmutableList(),
            }
        )
        .ToImmutableList(),
    };
  }

  [Test]
  public async Task AddsBasicJobs()
  {
    using var db = _repo.OpenConnection();
    await StartSyncThread();

    //add some existing jobs
    var completedJob = RandJob() with
    {
      Cycles = 3,
      Archived = false,
    };
    var toKeepJob = RandJob() with { Cycles = 10, Archived = false };

    var completedActive = completedJob.CloneToDerived<ActiveJob, Job>() with
    {
      Cycles = 4,
      RemainingToStart = 0,
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
        Queues = ImmutableDictionary<string, QueueInfo>.Empty,
      }
    );

    //some new jobs
    var newJob1 = RandJob() with
    {
      Archived = false,
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
          Revision = 0,
        },
        new NewProgramContent()
        {
          ProgramName = "prog2",
          ProgramContent = "content2",
          Revision = 0,
        },
      ],
    };

    var newStatusTask = CreateTaskToWaitForNewStatus();

    ((IJobAndQueueControl)_jq).AddJobs(newJobs, null);

    (await newStatusTask).ShouldBe(_curSt.CurrentStatus);

    db.LoadUnarchivedJobs()
      .Select(j => j.UniqueStr)
      .ShouldBe(
        new[] { completedJob.UniqueStr, toKeepJob.UniqueStr, newJob1.UniqueStr, newJob2.UniqueStr },
        ignoreOrder: true
      );
  }

  [Test]
  [Arguments(true)]
  [Arguments(false)]
  public async Task Decrement(bool byDate)
  {
    await StartSyncThread();
    var now = DateTime.UtcNow;
    using var db = _repo.OpenConnection();

    var j1 = RandJob() with { UniqueStr = "u1", Cycles = 12 };
    var j1Active = j1.CloneToDerived<ActiveJob, Job>() with { RemainingToStart = 7 };

    var j2 = RandJob() with { UniqueStr = "u2", Cycles = 22 };
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
        Queues = ImmutableDictionary<string, QueueInfo>.Empty,
      }
    );

    var newStatusTask = CreateTaskToWaitForNewStatus();

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

    decrs.ShouldBe(
      new[]
      {
        new JobAndDecrementQuantity()
        {
          DecrementId = 0,
          JobUnique = j1.UniqueStr,
          TimeUTC = now,
          Part = j1.PartName,
          Quantity = 7,
        },
      }
    );

    (await newStatusTask).ShouldBe(_curSt.CurrentStatus);

    db.LoadDecrementsForJob("u1")
      .ShouldBe(
        ImmutableList.Create(
          new[]
          {
            new DecrementQuantity()
            {
              DecrementId = 0,
              TimeUTC = now,
              Quantity = 7,
            },
          }
        )
      );

    db.LoadDecrementsForJob("u2").ShouldBeEmpty();
  }
  #endregion


  #region Queues
  [Test]
  public async Task UnallocatedQueues()
  {
    using var db = _repo.OpenConnection();
    await StartSyncThread();

    await SetCurrentState(stateUpdated: false, executeAction: false);
    var newStatusTask = CreateTaskToWaitForNewStatus();

    var now = DateTime.UtcNow;

    //add a casting
    _jq.AddUnallocatedCastingToQueue(
        casting: "c1",
        qty: 2,
        queue: "q1",
        serial: ["aaa"],
        workorder: null,
        operatorName: "theoper"
      )
      .ShouldBe(
        new[]
        {
          QueuedMat(matId: 1, job: null, part: "c1", proc: 0, path: 1, serial: "aaa", queue: "q1", pos: 0),
          QueuedMat(matId: 2, job: null, part: "c1", proc: 0, path: 1, serial: "", queue: "q1", pos: 1),
        }
      );
    db.GetMaterialDetails(1)
      .ShouldBe(
        new MaterialDetails()
        {
          MaterialID = 1,
          PartName = "c1",
          NumProcesses = 1,
          Serial = "aaa",
        }
      );
    db.GetMaterialDetails(2)
      .ShouldBe(
        new MaterialDetails()
        {
          MaterialID = 2,
          PartName = "c1",
          NumProcesses = 1,
          Serial = null,
        }
      );

    var mats = db.GetMaterialInAllQueues().ToList();
    mats[0].AddTimeUTC.Value.ShouldBe(DateTime.UtcNow, tolerance: TimeSpan.FromSeconds(4));
    mats.ShouldBe(
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
          Paths = ImmutableDictionary<int, int>.Empty,
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
          Paths = ImmutableDictionary<int, int>.Empty,
        },
      }
    );

    (await newStatusTask).ShouldBe(_curSt.CurrentStatus);

    newStatusTask = CreateTaskToWaitForNewStatus();

    _jq.RemoveMaterialFromAllQueues(new List<long> { 1 }, "theoper");
    mats = db.GetMaterialInAllQueues().ToList();
    mats.ShouldBe(
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
          Paths = ImmutableDictionary<int, int>.Empty,
        },
      }
    );

    (await newStatusTask).ShouldBe(_curSt.CurrentStatus);

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

    var actualMat1Logs = db.GetLogForMaterial(materialID: 1).ToList();
    actualMat1Logs.ShouldAllBe(e =>
      Math.Abs(e.EndTimeUTC.Subtract(now).TotalSeconds) < 4 && e.ElapsedTime < TimeSpan.FromSeconds(4)
    );
    actualMat1Logs
      .Select(e => e with { EndTimeUTC = now, ElapsedTime = TimeSpan.Zero })
      .ToArray()
      .ShouldBeEquivalentTo(
        new[]
        {
          MarkExpectedEntry(mat1, cntr: 1, serial: "aaa", timeUTC: now),
          AddToQueueExpectedEntry(
            mat1,
            cntr: 2,
            queue: "q1",
            position: 0,
            operName: "theoper",
            reason: "SetByOperator",
            timeUTC: now
          ),
          RemoveFromQueueExpectedEntry(
            mat1,
            cntr: 4,
            queue: "q1",
            reason: "",
            position: 0,
            elapsedMin: 0,
            operName: "theoper",
            timeUTC: now
          ),
        }
      );

    var actualMat2Logs = db.GetLogForMaterial(materialID: 2).ToList();
    actualMat2Logs.ShouldAllBe(e =>
      Math.Abs(e.EndTimeUTC.Subtract(now).TotalSeconds) < 4 && e.ElapsedTime < TimeSpan.FromSeconds(4)
    );
    actualMat2Logs
      .Select(e => e with { EndTimeUTC = now, ElapsedTime = TimeSpan.Zero })
      .ToArray()
      .ShouldBeEquivalentTo(
        new[]
        {
          AddToQueueExpectedEntry(
            mat2,
            cntr: 3,
            queue: "q1",
            position: 1,
            operName: "theoper",
            reason: "SetByOperator",
            timeUTC: now
          ),
        }
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
          Location = new InProcessMaterialLocation() { Type = InProcessMaterialLocation.LocType.OnPallet },
        },
      ]
    );

    Should
      .Throw<BadRequestException>(() => _jq.RemoveMaterialFromAllQueues([2L], "theoper"))
      .Message.ShouldBe("Material on pallet can not be removed from queues");

    await SetCurrentMaterial(
      [
        expectedMat2 with
        {
          Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Loading },
        },
      ]
    );

    Should
      .Throw<BadRequestException>(() => _jq.RemoveMaterialFromAllQueues([2L], "theoper"))
      .Message.ShouldBe("Only waiting material can be removed from queues");

    db.GetMaterialInAllQueues().ShouldContain(m => m.MaterialID == 2);
  }

  [Test]
  [Arguments(0)]
  [Arguments(1)]
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

    var now = DateTime.UtcNow;

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

    var newStatusTask = CreateTaskToWaitForNewStatus();
    _jq.AddUnprocessedMaterialToQueue(
        "uuu1",
        process: lastCompletedProcess,
        queue: "q1",
        position: 0,
        serial: "aaa",
        workorder: "work11",
        operatorName: "theoper"
      )
      .ShouldBe(expectedMat1);
    (await newStatusTask).ShouldBe(_curSt.CurrentStatus);

    db.GetMaterialDetails(1)
      .ShouldBe(
        new MaterialDetails()
        {
          MaterialID = 1,
          PartName = "p1",
          NumProcesses = 2,
          Serial = "aaa",
          Workorder = "work11",
          JobUnique = "uuu1",
        }
      );

    var mats = db.GetMaterialInAllQueues().ToList();
    mats[0].AddTimeUTC.Value.ShouldBe(DateTime.UtcNow, tolerance: TimeSpan.FromSeconds(4));
    mats.ShouldBe(
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
          Paths = ImmutableDictionary<int, int>.Empty,
        },
      }
    );

    await SetCurrentMaterial([expectedMat1]);

    newStatusTask = CreateTaskToWaitForNewStatus();

    //remove it
    _jq.RemoveMaterialFromAllQueues(ImmutableList.Create(1L), "myoper");
    db.GetMaterialInAllQueues().ShouldBeEmpty();

    (await newStatusTask).ShouldBe(_curSt.CurrentStatus);

    newStatusTask = CreateTaskToWaitForNewStatus();

    //add it back in
    _jq.SetMaterialInQueue(1, "q1", 0, "theoper");
    mats = db.GetMaterialInAllQueues().ToList();
    mats.ShouldBe(
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
          Paths = ImmutableDictionary<int, int>.Empty,
        },
      }
    );

    (await newStatusTask).ShouldBe(_curSt.CurrentStatus);

    mats = db.GetMaterialInAllQueues().ToList();
    mats[0].AddTimeUTC.Value.ShouldBe(DateTime.UtcNow, tolerance: TimeSpan.FromSeconds(4));
    mats.ShouldBe(
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
          Paths = ImmutableDictionary<int, int>.Empty,
        },
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
      MarkExpectedEntry(logMat, cntr: 1, serial: "aaa", timeUTC: now),
      AssignWorkExpectedEntry(logMat, cntr: 2, workorder: "work11", timeUTC: now),
      AddToQueueExpectedEntry(
        logMat,
        cntr: 3,
        queue: "q1",
        position: 0,
        operName: "theoper",
        reason: "SetByOperator",
        timeUTC: now
      ),
      RemoveFromQueueExpectedEntry(
        logMat,
        cntr: 4,
        queue: "q1",
        position: 0,
        reason: "",
        elapsedMin: 0,
        operName: "myoper",
        timeUTC: now
      ),
      AddToQueueExpectedEntry(
        logMat,
        cntr: 5,
        queue: "q1",
        position: 0,
        operName: "theoper",
        reason: "SetByOperator",
        timeUTC: now
      ),
    };

    var actualMat1Logs = db.GetLogForMaterial(materialID: 1).ToList();
    actualMat1Logs.ShouldAllBe(e =>
      Math.Abs(e.EndTimeUTC.Subtract(now).TotalSeconds) < 4 && e.ElapsedTime < TimeSpan.FromSeconds(4)
    );
    actualMat1Logs
      .Select(e => e with { EndTimeUTC = now, ElapsedTime = TimeSpan.Zero })
      .ToArray()
      .ShouldBeEquivalentTo(expectedLog);

    // should error if it is loading or on a pallet
    await SetCurrentMaterial(
      [
        expectedMat1 with
        {
          Location = new InProcessMaterialLocation() { Type = InProcessMaterialLocation.LocType.OnPallet },
        },
      ]
    );

    Should
      .Throw<BadRequestException>(() => _jq.SetMaterialInQueue(materialId: 1, "q1", 3, "theoper"))
      .Message.ShouldBe("Material on pallet can not be moved to a queue");

    await SetCurrentMaterial(
      [
        expectedMat1 with
        {
          Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Loading },
        },
      ]
    );

    Should
      .Throw<BadRequestException>(() => _jq.SetMaterialInQueue(materialId: 1, "q2", 3, "oper"))
      .Message.ShouldBe("Only waiting material can be moved between queues");

    // no new events added, already checked them equal to expectedLog above
    db.GetLogForMaterial(materialID: 1).Count().ShouldBe(expectedLog.Length);
  }

  [Test]
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
      .ShouldBe(new[] { expectedMat1, expectedMat2 });

    await SetCurrentMaterial(
      [
        expectedMat1 with
        {
          Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Loading },
        },
        expectedMat2,
      ]
    );

    db.GetMaterialInAllQueues().Select(m => m.MaterialID).ShouldBe(new[] { 1L, 2L });

    var newStatusTask = CreateTaskToWaitForNewStatus();

    _jq.SetMaterialInQueue(materialId: 1, "q1", 1, "oper");

    (await newStatusTask).ShouldBe(_curSt.CurrentStatus);

    db.GetMaterialInAllQueues().Select(m => m.MaterialID).ShouldBe(new[] { 2L, 1L });

    Should
      .Throw<BadRequestException>(() => _jq.SetMaterialInQueue(materialId: 1, "q2", -1, "oper"))
      .Message.ShouldBe("Only waiting material can be moved between queues");
  }

  public record SignalQuarantineTheoryData
  {
    public enum QuarantineType
    {
      Add,
      Signal,
      Remove,
    }

    public required InProcessMaterialLocation.LocType LocType { get; set; }
    public required InProcessMaterialAction.ActionType ActionType { get; set; }
    public required string QuarantineQueue { get; set; }
    public string Error { get; set; } = null;
    public QuarantineType? QuarantineAction { get; set; } = null;
    public int Process { get; set; } = 0;
    public string JobTransferQeuue { get; set; } = "q1";
    public bool AllowQuarantineToCancelLoad { get; set; } = false;
  }

  public readonly ImmutableList<SignalQuarantineTheoryData> SignalTheoryData =
  [
    new SignalQuarantineTheoryData
    {
      ActionType = InProcessMaterialAction.ActionType.Waiting,
      LocType = InProcessMaterialLocation.LocType.InQueue,
      QuarantineQueue = "quarqqq",
      QuarantineAction = SignalQuarantineTheoryData.QuarantineType.Add,
    },
    new SignalQuarantineTheoryData()
    {
      ActionType = InProcessMaterialAction.ActionType.Waiting,
      LocType = InProcessMaterialLocation.LocType.InQueue,
      QuarantineQueue = null,
      QuarantineAction = SignalQuarantineTheoryData.QuarantineType.Remove,
    },
    new SignalQuarantineTheoryData
    {
      ActionType = InProcessMaterialAction.ActionType.Waiting,
      LocType = InProcessMaterialLocation.LocType.Free,
      QuarantineQueue = "quarqqq",
      QuarantineAction = SignalQuarantineTheoryData.QuarantineType.Add,
    },
    // Now OnPallet with various tests for transfer queues and proces
    new SignalQuarantineTheoryData
    {
      ActionType = InProcessMaterialAction.ActionType.Waiting,
      LocType = InProcessMaterialLocation.LocType.OnPallet,
      QuarantineQueue = "quarqqq",
      Process = 1,
      JobTransferQeuue = "q1",
      QuarantineAction = SignalQuarantineTheoryData.QuarantineType.Signal,
    },
    new SignalQuarantineTheoryData
    {
      ActionType = InProcessMaterialAction.ActionType.UnloadToInProcess,
      LocType = InProcessMaterialLocation.LocType.OnPallet,
      QuarantineQueue = "quarqqq",
      Process = 1,
      JobTransferQeuue = "q1",
      QuarantineAction = SignalQuarantineTheoryData.QuarantineType.Signal,
    },
    new SignalQuarantineTheoryData
    {
      ActionType = InProcessMaterialAction.ActionType.Loading,
      LocType = InProcessMaterialLocation.LocType.OnPallet,
      QuarantineQueue = "quarqqq",
      Process = 1,
      JobTransferQeuue = "q1",
      Error = "Material on pallet can not be quarantined while loading",
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
      QuarantineAction = SignalQuarantineTheoryData.QuarantineType.Signal,
    },
    new SignalQuarantineTheoryData
    {
      ActionType = InProcessMaterialAction.ActionType.Machining,
      LocType = InProcessMaterialLocation.LocType.OnPallet,
      QuarantineQueue = "quarqqq",
      Process = 1,
      JobTransferQeuue = null,
      Error = "Can only signal material for quarantine if the current process and path has an output queue",
    },
    new SignalQuarantineTheoryData
    {
      ActionType = InProcessMaterialAction.ActionType.Machining,
      LocType = InProcessMaterialLocation.LocType.OnPallet,
      QuarantineQueue = "quarqqq",
      Process = 1,
      JobTransferQeuue = null,
      AllowQuarantineToCancelLoad = true,
      QuarantineAction = SignalQuarantineTheoryData.QuarantineType.Signal,
    },
    new SignalQuarantineTheoryData
    {
      ActionType = InProcessMaterialAction.ActionType.Machining,
      LocType = InProcessMaterialLocation.LocType.OnPallet,
      QuarantineQueue = "quarqqq",
      Process = 2,
      JobTransferQeuue = null,
      QuarantineAction = SignalQuarantineTheoryData.QuarantineType.Signal,
    },
    // Loading from a queue
    new SignalQuarantineTheoryData()
    {
      ActionType = InProcessMaterialAction.ActionType.Loading,
      LocType = InProcessMaterialLocation.LocType.InQueue,
      QuarantineQueue = "quarqqq",
      Error = "Invalid material state for quarantine",
    },
    new SignalQuarantineTheoryData()
    {
      ActionType = InProcessMaterialAction.ActionType.Loading,
      LocType = InProcessMaterialLocation.LocType.InQueue,
      QuarantineQueue = "quarqqq",
      AllowQuarantineToCancelLoad = true,
      QuarantineAction = SignalQuarantineTheoryData.QuarantineType.Add,
    },
    new SignalQuarantineTheoryData()
    {
      ActionType = InProcessMaterialAction.ActionType.Loading,
      LocType = InProcessMaterialLocation.LocType.InQueue,
      QuarantineQueue = null,
      Error = "Invalid material state for quarantine",
    },
    new SignalQuarantineTheoryData()
    {
      ActionType = InProcessMaterialAction.ActionType.Loading,
      LocType = InProcessMaterialLocation.LocType.InQueue,
      QuarantineQueue = null,
      AllowQuarantineToCancelLoad = true,
      QuarantineAction = SignalQuarantineTheoryData.QuarantineType.Remove,
    },
  ];

  public IEnumerable<Func<SignalQuarantineTheoryData>> SignalTheoryDataSource()
  {
    foreach (var data in SignalTheoryData)
    {
      yield return () => data;
    }
  }

  [Test]
  [MethodDataSource(nameof(SignalTheoryDataSource))]
  public async Task QuarantinesMatOnPallet(SignalQuarantineTheoryData data)
  {
    _settings = _settings with { QuarantineQueue = data.QuarantineQueue };
    AllowQuarantineToCancelLoad = data.AllowQuarantineToCancelLoad;
    await StartSyncThread();

    var now = DateTime.UtcNow;

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
      CopiedToSystem = true,
    };

    db.AddJobs(new NewJobs() { ScheduleId = "abcd", Jobs = [job] }, null, addAsCopiedToSystem: true);

    db.AllocateMaterialID("uuu1", "p1", 2).ShouldBe(1);
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
        Queues = ImmutableDictionary<string, QueueInfo>.Empty,
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
          Face = 0,
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
          reason: "Test",
          timeUTC: now
        )
      );
    }

    Should
      .Throw<BadRequestException>(() =>
        _jq.SignalMaterialForQuarantine(materialId: 1, operatorName: "theoper", reason: "a reason")
      )
      .Message.ShouldBe("Material not found");

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
            PalletNum = data.LocType == InProcessMaterialLocation.LocType.OnPallet ? 4 : null,
          },
        },
      ]
    );

    if (data.Error != null)
    {
      Should
        .Throw<BadRequestException>(() =>
          _jq.SignalMaterialForQuarantine(materialId: 1, "theoper", reason: "a reason")
        )
        .Message.ShouldBe(data.Error);
    }
    else
    {
      var newStatusTask = CreateTaskToWaitForNewStatus();

      _jq.SignalMaterialForQuarantine(1, "theoper", reason: "signaling reason");

      (await newStatusTask).ShouldBe(_curSt.CurrentStatus);

      if (data.QuarantineAction == SignalQuarantineTheoryData.QuarantineType.Signal)
      {
        expectedLog.Add(
          SignalQuarantineExpectedEntry(
            logMat,
            cntr: expectedLog.Count + 1,
            pal: 4,
            queue: data.QuarantineQueue ?? "",
            operName: "theoper",
            reason: "signaling reason",
            timeUTC: now
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
            operName: "theoper",
            timeUTC: now
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
              operName: "theoper",
              timeUTC: now
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
            operName: "theoper",
            timeUTC: now
          )
        );
      }
    }

    var mat1Log = db.GetLogForMaterial(materialID: 1).ToList();
    mat1Log.ShouldAllBe(e =>
      Math.Abs(e.EndTimeUTC.Subtract(now).TotalSeconds) < 4 && e.ElapsedTime < TimeSpan.FromSeconds(4)
    );
    mat1Log
      .Select(e => e with { EndTimeUTC = now, ElapsedTime = TimeSpan.Zero })
      .ToList()
      .ShouldBeEquivalentTo(expectedLog);
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
      result: serial,
      elapsed: TimeSpan.Zero,
      active: TimeSpan.Zero
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
      result: workorder,
      elapsed: TimeSpan.Zero,
      active: TimeSpan.Zero
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
      result: "",
      elapsed: TimeSpan.Zero,
      active: TimeSpan.Zero
    );
    if (!string.IsNullOrEmpty(operName))
    {
      e = e with
      {
        ProgramDetails = (e.ProgramDetails ?? ImmutableDictionary<string, string>.Empty).Add(
          "operator",
          operName
        ),
      };
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
      result: "QuarantineAfterUnload",
      elapsed: TimeSpan.Zero,
      active: TimeSpan.Zero
    );
    if (!string.IsNullOrEmpty(operName))
    {
      e = e with
      {
        ProgramDetails = (e.ProgramDetails ?? ImmutableDictionary<string, string>.Empty).Add(
          "operator",
          operName
        ),
      };
    }
    if (!string.IsNullOrEmpty(reason))
    {
      e = e with
      {
        ProgramDetails = (e.ProgramDetails ?? ImmutableDictionary<string, string>.Empty).Add("note", reason),
      };
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
      e = e with
      {
        ProgramDetails = (e.ProgramDetails ?? ImmutableDictionary<string, string>.Empty).Add(
          "operator",
          operName
        ),
      };
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
      JobUnique = job?.UniqueStr ?? "",
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
      Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
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
      result: "Operator Notes",
      elapsed: TimeSpan.Zero,
      active: TimeSpan.Zero
    );
    e = e with { ProgramDetails = ImmutableDictionary<string, string>.Empty.Add("note", note) };
    if (!string.IsNullOrEmpty(operName))
    {
      e = e with
      {
        ProgramDetails = (e.ProgramDetails ?? ImmutableDictionary<string, string>.Empty).Add(
          "operator",
          operName
        ),
      };
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
      Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
    };
  }
  #endregion

  #region TimeZone

  [Test]
  public async Task NoAlarm()
  {
    _serverSettings = new ServerSettings() { ExpectedTimeZone = TimeZoneInfo.Local };

    await StartSyncThread();

    var curSt = new CurrentStatus()
    {
      TimeOfCurrentStatusUTC = DateTime.UtcNow,
      Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
      Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
      Material = [],
      Alarms = [],
      Queues = ImmutableDictionary<string, QueueInfo>.Empty,
    };

    await SetCurrentState(stateUpdated: true, executeAction: false, curSt: curSt);
  }

  [Test]
  public async Task WrongTimezone()
  {
    _serverSettings = new ServerSettings()
    {
      ExpectedTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Hawaiian Standard Time"),
    };

    await StartSyncThread(
      $"The server is running in timezone {TimeZoneInfo.Local.Id} but was expected to be Hawaiian Standard Time."
    );
  }

  #endregion
}
