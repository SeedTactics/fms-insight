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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoFixture;
using BlackMaple.FMSInsight.Tests;
using BlackMaple.MachineFramework;
using NSubstitute;
using Shouldly;
using Xunit;

#nullable enable

namespace BlackMaple.FMSInsight.Makino.Tests;

public sealed class SyncSpec : IDisposable
{
  private readonly string _tempDir;
  private readonly IMakinoDB _makinoDB;
  private readonly RepositoryConfig _repo;
  private readonly AutoFixture.Fixture fix;
  private readonly MakinoSync sync;
  private readonly IReadOnlyDictionary<int, PalletLocation> _devices;

  public SyncSpec()
  {
    _tempDir = Directory.CreateTempSubdirectory("makinosyncspec").FullName;
    _makinoDB = Substitute.For<IMakinoDB>();
    _repo = RepositoryConfig.InitializeMemoryDB(
      new SerialSettings() { ConvertMaterialIDToSerial = m => SerialSettings.ConvertToBase62(m, 10) }
    );
    fix = new AutoFixture.Fixture();
    fix.Customizations.Add(new ImmutableSpecimenBuilder());
    fix.Customizations.Add(new DateOnlySpecimenBuilder());

    sync = new MakinoSync(
      new MakinoSettings()
      {
        ADEPath = _tempDir,
        DownloadOnlyOrders = true,
        DbConnectionString = "unused db conn str",
      },
      _makinoDB,
      new FMSSettings()
    );

    _devices = new Dictionary<int, PalletLocation>()
    {
      {
        1,
        new PalletLocation()
        {
          Location = PalletLocationEnum.LoadUnload,
          Num = 1,
          StationGroup = "L/U",
        }
      },
      {
        2,
        new PalletLocation()
        {
          Location = PalletLocationEnum.LoadUnload,
          Num = 2,
          StationGroup = "L/U",
        }
      },
      {
        3,
        new PalletLocation()
        {
          Location = PalletLocationEnum.Machine,
          Num = 1,
          StationGroup = "Mach",
        }
      },
      {
        4,
        new PalletLocation()
        {
          Location = PalletLocationEnum.Machine,
          Num = 2,
          StationGroup = "Mach",
        }
      },
    };
  }

  void IDisposable.Dispose()
  {
    Directory.Delete(_tempDir, true);
    _repo.Dispose();
  }

  private Task<(T, string)> WatchForFile<T>(Func<T> f)
  {
    var tcs = new TaskCompletionSource<string>();
    var watcher = new FileSystemWatcher(_tempDir, "insight*.xml");
    watcher.Created += (s, e) =>
    {
      tcs.SetResult(File.ReadAllText(Path.Combine(_tempDir, e.Name ?? "")));
      File.Delete(Path.Combine(_tempDir, e.Name ?? ""));
    };
    watcher.EnableRaisingEvents = true;

    var watcherTask = tcs.Task.ContinueWith(t =>
    {
      watcher.EnableRaisingEvents = false;
      watcher.Dispose();
      return t.Result;
    });

    var syncTask = Task.Run(f);

    var allTask = Task.WhenAll(syncTask, watcherTask);

    return Task.WhenAny(allTask, Task.Delay(TimeSpan.FromSeconds(12)))
      .ContinueWith(_ =>
      {
        if (allTask.Status == TaskStatus.RanToCompletion)
          return (syncTask.Result, watcherTask.Result);
        else
          throw new Exception("Timed out waiting for file");
      });
  }

  [Fact]
  public void ErrorsOnMultiProcJob()
  {
    using var db = _repo.OpenConnection();

    var partName = fix.Create<string>();

    sync.CheckNewJobs(
        db,
        new NewJobs()
        {
          ScheduleId = "schid",
          Jobs =
          [
            new Job()
            {
              UniqueStr = fix.Create<string>(),
              PartName = partName,
              RouteStartUTC = DateTime.UtcNow,
              RouteEndUTC = DateTime.UtcNow.AddHours(10),
              Archived = false,
              Cycles = fix.Create<int>(),
              Processes = [new ProcessInfo() { Paths = [] }, new ProcessInfo() { Paths = [] }],
            },
          ],
        }
      )
      .ShouldBe(
        [
          $"FMS Insight does not support multiple processes currently, please change {partName} to have one process.",
        ]
      );
  }

  [Fact]
  public void ErrorsOnTwoPaths()
  {
    using var db = _repo.OpenConnection();

    var partName = fix.Create<string>();

    sync.CheckNewJobs(
        db,
        new NewJobs()
        {
          ScheduleId = "schid",
          Jobs =
          [
            new Job()
            {
              UniqueStr = fix.Create<string>(),
              PartName = partName,
              RouteStartUTC = DateTime.UtcNow,
              RouteEndUTC = DateTime.UtcNow.AddHours(10),
              Archived = false,
              Cycles = fix.Create<int>(),
              Processes =
              [
                new ProcessInfo() { Paths = [fix.Create<ProcPathInfo>(), fix.Create<ProcPathInfo>()] },
              ],
            },
          ],
        }
      )
      .ShouldBe(
        [
          $"FMS Insight does not support paths with the same color, please make sure each path has a distinct color in {partName}",
        ]
      );
  }

  [Fact]
  public void ErrorsOnBadMachineName()
  {
    using var db = _repo.OpenConnection();

    var partName = fix.Create<string>();

    _makinoDB.LoadMakinoDevices().Returns(_devices);

    sync.CheckNewJobs(
        db,
        new NewJobs()
        {
          ScheduleId = "schid",
          Jobs =
          [
            new Job()
            {
              UniqueStr = fix.Create<string>(),
              PartName = partName,
              RouteStartUTC = DateTime.UtcNow,
              RouteEndUTC = DateTime.UtcNow.AddHours(10),
              Archived = false,
              Cycles = fix.Create<int>(),
              Processes =
              [
                new ProcessInfo()
                {
                  Paths =
                  [
                    fix.Create<ProcPathInfo>() with
                    {
                      Stops =
                      [
                        fix.Create<MachiningStop>() with
                        {
                          StationGroup = "bad",
                          Stations = [1],
                        },
                        fix.Create<MachiningStop>() with
                        {
                          StationGroup = "Mach",
                          Stations = [500],
                        },
                      ],
                    },
                  ],
                },
              ],
            },
          ],
        }
      )
      .ShouldBe(
        [
          $"The flexibility plan for part {partName} uses machine bad number 1, but that machine does not exist in the Makino system.  The makino system contains machines Mach1,Mach2",
          $"The flexibility plan for part {partName} uses machine Mach number 500, but that machine does not exist in the Makino system.  The makino system contains machines Mach1,Mach2",
        ]
      );
  }

  [Fact]
  public void LoadsStateWithNoNewEvents()
  {
    using var db = _repo.OpenConnection();

    _makinoDB
      .LoadResults(Arg.Any<DateTime>(), Arg.Any<DateTime>())
      .Returns(
        new MakinoResults()
        {
          Devices = _devices,
          MachineResults = [],
          WorkSetResults = [],
        }
      );

    var cur = fix.Create<CurrentStatus>();
    _makinoDB.LoadCurrentInfo(Arg.Is(db), Arg.Any<DateTime>()).Returns(cur);

    sync.CalculateCellState(db)
      .ShouldBeEquivalentTo(
        new MakinoCellState()
        {
          CurrentStatus = cur,
          JobsNotYetCopied = [],
          StateUpdated = false,
        }
      );

    db.MaxLogDate().ShouldBe(DateTime.MinValue);
  }

  [Fact]
  public void ProcessesEvents()
  {
    // more detailed event processing is in LogBuilderSpec, here we just test that
    // LogBuilder is being called

    using var db = _repo.OpenConnection();

    var now = DateTime.UtcNow;

    _makinoDB
      .LoadResults(Arg.Any<DateTime>(), Arg.Any<DateTime>())
      .Returns(
        new MakinoResults()
        {
          Devices = _devices,
          MachineResults = [],
          WorkSetResults =
          [
            fix.Build<WorkSetResults>()
              .With(w => w.StartDateTimeUTC, now)
              .With(w => w.EndDateTimeUTC, now + TimeSpan.FromMinutes(20))
              .With(w => w.DeviceID, 1)
              .With(w => w.Remachine, false)
              .Create(),
          ],
        }
      );

    var cur = fix.Create<CurrentStatus>();
    _makinoDB.LoadCurrentInfo(Arg.Is(db), Arg.Is(now)).Returns(cur);

    sync.CalculateCellState(db, now)
      .ShouldBeEquivalentTo(
        new MakinoCellState()
        {
          CurrentStatus = cur,
          JobsNotYetCopied = [],
          StateUpdated = true,
        }
      );

    db.MaxLogDate().ShouldBe(now + TimeSpan.FromMinutes(20) + TimeSpan.FromSeconds(1));
  }

  [Fact]
  public void LoadsNewJobs()
  {
    using var db = _repo.OpenConnection();

    var now = DateTime.UtcNow;

    var job1 = fix.Build<Job>()
      .With(j => j.RouteStartUTC, now.AddHours(-2))
      .With(j => j.RouteEndUTC, now.AddHours(2))
      .Create()
      .ClearCastingsOnLargerProcs();
    var job2 = fix.Build<Job>()
      .With(j => j.RouteStartUTC, now.AddHours(-1))
      .With(j => j.RouteEndUTC, now.AddHours(2))
      .Create()
      .ClearCastingsOnLargerProcs();
    // job3 is too old
    var job3 = fix.Build<Job>()
      .With(j => j.RouteStartUTC, now.AddHours(-13))
      .With(j => j.RouteEndUTC, now.AddHours(-11))
      .Create()
      .ClearCastingsOnLargerProcs();

    var schId = fix.Create<string>();

    db.AddJobs(
      new NewJobs() { ScheduleId = schId, Jobs = [job1, job2, job3] },
      expectedPreviousScheduleId: null,
      addAsCopiedToSystem: false
    );

    db.MarkJobCopiedToSystem(job1.UniqueStr);

    _makinoDB
      .LoadResults(Arg.Any<DateTime>(), Arg.Any<DateTime>())
      .Returns(
        new MakinoResults()
        {
          Devices = _devices,
          MachineResults = [],
          WorkSetResults = [],
        }
      );

    var cur = fix.Create<CurrentStatus>();
    _makinoDB.LoadCurrentInfo(Arg.Is(db), Arg.Any<DateTime>()).Returns(cur);

    sync.CalculateCellState(db)
      .ShouldBeEquivalentTo(
        new MakinoCellState()
        {
          CurrentStatus = cur,
          JobsNotYetCopied =
          [
            job2.CloneToDerived<HistoricJob, Job>() with
            {
              ScheduleId = schId,
              Decrements = [],
            },
          ],
          StateUpdated = false,
        }
      );
  }

  [Fact]
  public void DoesNothingWithNoJobsToDownload()
  {
    var st = fix.Build<MakinoCellState>().With(s => s.JobsNotYetCopied, []).Create();

    using var db = _repo.OpenConnection();

    sync.ApplyActions(db, st).ShouldBeFalse();
  }

  [Fact]
  public void MarksCopiedJobsToSystem()
  {
    using var db = _repo.OpenConnection();

    var now = DateTime.UtcNow;

    var job = fix.Build<Job>()
      .With(j => j.RouteStartUTC, now.AddHours(-2))
      .With(j => j.RouteEndUTC, now.AddHours(2))
      .Create()
      .ClearCastingsOnLargerProcs();

    var schId = fix.Create<string>();

    var st = fix.Build<MakinoCellState>()
      .With(
        s => s.JobsNotYetCopied,
        [
          job.CloneToDerived<HistoricJob, Job>() with
          {
            CopiedToSystem = false,
            ScheduleId = schId,
            Decrements = [],
          },
        ]
      )
      .With(
        s => s.CurrentStatus,
        (CurrentStatus cs) =>
          cs with
          {
            Jobs = ImmutableDictionary<string, ActiveJob>.Empty.Add(
              job.UniqueStr,
              job.CloneToDerived<ActiveJob, Job>()
            ),
          }
      )
      .Create();

    db.AddJobs(
      new NewJobs() { ScheduleId = schId, Jobs = [job] },
      expectedPreviousScheduleId: null,
      addAsCopiedToSystem: false
    );

    db.LoadJob(job.UniqueStr).CopiedToSystem.ShouldBeFalse();

    sync.ApplyActions(db, st).ShouldBeFalse();

    db.LoadJob(job.UniqueStr).CopiedToSystem.ShouldBeTrue();
  }

  [Fact]
  public async Task CreatesFile()
  {
    // more complicated tests are in OrderXMLSpec, here we just test that the file is created

    using var db = _repo.OpenConnection();

    var now = DateTime.UtcNow;

    var job = fix.Build<Job>()
      .With(j => j.RouteStartUTC, now.AddHours(-1))
      .With(j => j.RouteEndUTC, now.AddHours(4))
      .Create()
      .ClearCastingsOnLargerProcs();

    var schId = fix.Create<string>();

    db.AddJobs(
      new NewJobs() { ScheduleId = schId, Jobs = [job] },
      expectedPreviousScheduleId: null,
      addAsCopiedToSystem: false
    );

    var st = fix.Build<MakinoCellState>()
      .With(
        s => s.JobsNotYetCopied,
        [
          job.CloneToDerived<HistoricJob, Job>() with
          {
            CopiedToSystem = false,
            ScheduleId = schId,
            Decrements = [],
          },
        ]
      )
      .Create();

    db.LoadJob(job.UniqueStr).CopiedToSystem.ShouldBeFalse();

    var (applyResult, file) = await WatchForFile(() => sync.ApplyActions(db, st));

    applyResult.ShouldBeTrue();
    file.ShouldContain($"<Order action=\"ADD\" name=\"{job.UniqueStr}\">");

    db.LoadJob(job.UniqueStr).CopiedToSystem.ShouldBeTrue();
  }

  [Fact]
  public void TimesOutIfMakinoNotRunning()
  {
    using var db = _repo.OpenConnection();

    var now = DateTime.UtcNow;

    var job = fix.Build<Job>()
      .With(j => j.RouteStartUTC, now.AddHours(-1))
      .With(j => j.RouteEndUTC, now.AddHours(4))
      .Create()
      .ClearCastingsOnLargerProcs();

    var schId = fix.Create<string>();

    db.AddJobs(
      new NewJobs() { ScheduleId = schId, Jobs = [job] },
      expectedPreviousScheduleId: null,
      addAsCopiedToSystem: false
    );

    var st = fix.Build<MakinoCellState>()
      .With(
        s => s.JobsNotYetCopied,
        [
          job.CloneToDerived<HistoricJob, Job>() with
          {
            CopiedToSystem = false,
            ScheduleId = schId,
            Decrements = [],
          },
        ]
      )
      .Create();

    db.LoadJob(job.UniqueStr).CopiedToSystem.ShouldBeFalse();

    Should
      .Throw<Exception>(() => sync.ApplyActions(db, st))
      .Message.ShouldBe("Unable to copy orders to Makino: check that the Makino software is running");

    db.LoadJob(job.UniqueStr).CopiedToSystem.ShouldBeFalse();
  }

  [Fact]
  public void DoesNotDecrementOnEmptyList()
  {
    using var db = _repo.OpenConnection();
    var st = fix.Create<MakinoCellState>();

    _makinoDB.RemainingToRun().Returns([]);

    sync.DecrementJobs(db, st).ShouldBeFalse();
  }

  [Fact]
  public async Task WritesDecrement()
  {
    using var db = _repo.OpenConnection();
    var now = DateTime.UtcNow;

    // add as copied to system
    var job = fix.Build<Job>()
      .With(j => j.RouteStartUTC, now.AddHours(-1))
      .With(j => j.RouteEndUTC, now.AddHours(4))
      .Create()
      .ClearCastingsOnLargerProcs();

    var schId = fix.Create<string>();

    db.AddJobs(
      new NewJobs() { ScheduleId = schId, Jobs = [job] },
      expectedPreviousScheduleId: null,
      addAsCopiedToSystem: true
    );

    var st = new MakinoCellState()
    {
      CurrentStatus = fix.Build<CurrentStatus>()
        .With(
          j => j.Jobs,
          ImmutableDictionary<string, ActiveJob>.Empty.Add(
            job.UniqueStr,
            job.CloneToDerived<ActiveJob, Job>()
          )
        )
        .Create(),
      JobsNotYetCopied = [],
      StateUpdated = false,
    };
    var remaining = new RemainingToRun() { JobUnique = job.UniqueStr, RemainingQuantity = job.Cycles - 2 };

    _makinoDB.RemainingToRun().Returns([remaining]);

    var (applyResult, file) = await WatchForFile(() => sync.DecrementJobs(db, st));

    applyResult.ShouldBeTrue();
    file.ShouldContain($"<OrderQuantity orderName=\"{job.UniqueStr}\">");

    db.LoadJob(job.UniqueStr).Decrements!.Select(d => d.Quantity).ShouldBe([2]);
  }
}
