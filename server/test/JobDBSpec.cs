/* Copyright (c) 2021, John Lenz

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
using AutoFixture;
using BlackMaple.MachineFramework;
using Shouldly;

namespace BlackMaple.FMSInsight.Tests
{
  public class JobDBSpec : IDisposable
  {
    private RepositoryConfig _repoCfg;
    private Fixture _fixture;

    public JobDBSpec()
    {
      _repoCfg = RepositoryConfig.InitializeMemoryDB(null);
      _fixture = new Fixture();
      _fixture.Customizations.Add(new ImmutableSpecimenBuilder());
      _fixture.Customizations.Add(new DateOnlySpecimenBuilder());
    }

    public void Dispose()
    {
      _repoCfg.Dispose();
    }

    [Test]
    public void AddsJobs()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      var schId = "SchId" + _fixture.Create<string>();
      var job1 = RandJob() with { ManuallyCreated = false };
      var job1ExtraParts = _fixture.Create<Dictionary<string, int>>();
      var job1UnfilledWorks = _fixture.Create<List<Workorder>>();
      var job1StatUse = RandSimStationUse(schId, job1.RouteStartUTC);
      var addAsCopied = _fixture.Create<bool>();

      // Add first job

      _jobDB.AddJobs(
        new NewJobs()
        {
          ScheduleId = schId,
          Jobs = ImmutableList.Create(job1),
          ExtraParts = job1ExtraParts.ToImmutableDictionary(),
          CurrentUnfilledWorkorders = job1UnfilledWorks.ToImmutableList(),
          StationUse = job1StatUse,
        },
        null,
        addAsCopiedToSystem: addAsCopied
      );
      job1StatUse = job1StatUse
        .Select(s => s.PlanDown == false ? s with { PlanDown = null } : s)
        .ToImmutableList();

      var job1history = job1.CloneToDerived<HistoricJob, Job>() with
      {
        ScheduleId = schId,
        CopiedToSystem = addAsCopied,
        Decrements = ImmutableList<DecrementQuantity>.Empty,
      };

      _jobDB
        .LoadJobHistory(job1.RouteStartUTC.AddHours(-1), job1.RouteStartUTC.AddHours(10))
        .ShouldBeEquivalentToD(
          new HistoricData()
          {
            Jobs = ImmutableDictionary.Create<string, HistoricJob>().Add(job1.UniqueStr, job1history),
            StationUse = job1StatUse,
          }
        );

      _jobDB
        .LoadJobHistory(
          job1.RouteStartUTC.AddHours(-1),
          job1.RouteStartUTC.AddHours(10),
          new HashSet<string>(new[] { schId })
        )
        .ShouldBeEquivalentTo(
          new HistoricData()
          {
            Jobs = ImmutableDictionary<string, HistoricJob>.Empty,
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
          }
        );

      Should
        .Throw<ConflictRequestException>(() =>
          _jobDB.LoadJobHistory(
            job1.RouteStartUTC.AddHours(-1),
            job1.RouteStartUTC.AddHours(10),
            new HashSet<string>(new[] { schId, "asdfouh" })
          )
        )
        .Message.ShouldBe("Schedule ID asdfouh does not exist");

      _jobDB
        .LoadJobHistory(job1.RouteStartUTC.AddHours(-20), job1.RouteStartUTC.AddHours(-10))
        .ShouldBeEquivalentTo(
          new HistoricData()
          {
            Jobs = ImmutableDictionary<string, HistoricJob>.Empty,
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
          }
        );

      _jobDB.LoadJob(job1.UniqueStr).ShouldBeEquivalentTo(job1history);
      _jobDB.DoesJobExist(job1.UniqueStr).ShouldBeTrue();
      _jobDB.DoesJobExist("afouiaehwiouhwef").ShouldBeFalse();

      var belowJob1 = job1.UniqueStr.Substring(0, job1.UniqueStr.Length / 2);
      var aboveJob1 =
        belowJob1.Substring(0, belowJob1.Length - 1)
        + ((char)(belowJob1[belowJob1.Length - 1] + 1)).ToString();
      _jobDB.LoadJobsBetween(belowJob1, aboveJob1).ShouldBeEquivalentToD(ImmutableList.Create(job1history));
      _jobDB
        .LoadJobsBetween(belowJob1, job1.UniqueStr)
        .ShouldBeEquivalentTo(ImmutableList.Create(job1history));
      _jobDB.LoadJobsBetween(belowJob1, belowJob1 + "!").ShouldBeEmpty();
      _jobDB
        .LoadJobsBetween(job1.UniqueStr, aboveJob1)
        .ShouldBeEquivalentTo(ImmutableList.Create(job1history));

      _jobDB
        .LoadMostRecentSchedule()
        .ShouldBeEquivalentTo(
          new MostRecentSchedule()
          {
            LatestScheduleId = schId,
            Jobs = ImmutableList.Create(job1history),
            ExtraParts = job1ExtraParts.ToImmutableDictionary(),
          }
        );

      _jobDB
        .StationGroupsOnMostRecentSchedule()
        .ToList()
        .ShouldBeEquivalentTo(
          job1history
            .Processes.SelectMany(p => p.Paths)
            .SelectMany(p => p.Stops)
            .Select(s => s.StationGroup)
            .Distinct()
            .ToList()
        );

      var (rawMatQ, inProcQ) = _jobDB.QueuesOnMostRecentSchedule();
      rawMatQ.ShouldBe(
        job1.Processes[0].Paths.Select(p => p.InputQueue).Where(q => !string.IsNullOrEmpty(q)).ToHashSet(),
        ignoreOrder: true
      );
      inProcQ.ShouldBe(
        job1.Processes.SelectMany(
            (proc, procIdx) =>
              proc.Paths.SelectMany<ProcPathInfo, string>(path =>
                [
                  .. procIdx > 0 ? [path.InputQueue] : Enumerable.Empty<string>(),
                  .. procIdx < job1.Processes.Count - 1 ? [path.OutputQueue] : Enumerable.Empty<string>(),
                ]
              )
          )
          .Where(q => !string.IsNullOrEmpty(q))
          .ToHashSet(),
        ignoreOrder: true
      );

      // Add second job
      var schId2 = "ZZ" + schId;
      var job2 = RandJob(job1.RouteStartUTC.AddHours(4)) with
      {
        ManuallyCreated = true,
        PartName = job1.PartName,
      };
      var job2SimUse = RandSimStationUse(schId2, job2.RouteStartUTC);
      var job2ExtraParts = _fixture.Create<Dictionary<string, int>>();
      var job2UnfilledWorks = _fixture.Create<List<Workorder>>();
      job2UnfilledWorks.Add(_fixture.Create<Workorder>() with { Part = job1UnfilledWorks[0].Part });

      var newJobs2 = new NewJobs()
      {
        ScheduleId = schId2,
        Jobs = ImmutableList.Create<Job>(job2),
        StationUse = job2SimUse,
        ExtraParts = job2ExtraParts.ToImmutableDictionary(),
        CurrentUnfilledWorkorders = job2UnfilledWorks.ToImmutableList(),
      };

      Should
        .Throw<Exception>(() => _jobDB.AddJobs(newJobs2, "badsch", true))
        .Message.ShouldBe("Mismatch in previous schedule: expected 'badsch' but got '" + schId + "'");

      _jobDB.LoadJob(job2.UniqueStr).ShouldBeNull();
      _jobDB.DoesJobExist(job2.UniqueStr).ShouldBeFalse();
      _jobDB.LoadMostRecentSchedule().LatestScheduleId.ShouldBe(schId);

      _jobDB.AddJobs(newJobs2, expectedPreviousScheduleId: schId, addAsCopiedToSystem: true);

      job2SimUse = job2SimUse
        .Select(s => s.PlanDown == false ? s with { PlanDown = null } : s)
        .ToImmutableList();

      _jobDB.DoesJobExist(job2.UniqueStr).ShouldBeTrue();

      var job2history = job2.CloneToDerived<HistoricJob, Job>() with
      {
        ScheduleId = schId2,
        CopiedToSystem = true,
        Decrements = ImmutableList<DecrementQuantity>.Empty,
      };

      _jobDB
        .LoadJobHistory(job1.RouteStartUTC.AddHours(-1), job1.RouteStartUTC.AddHours(10))
        .ShouldBeEquivalentToD(
          new HistoricData()
          {
            Jobs = ImmutableDictionary
              .Create<string, HistoricJob>()
              .Add(job1.UniqueStr, job1history)
              .Add(job2.UniqueStr, job2history),
            StationUse = job1StatUse.AddRange(job2SimUse),
          }
        );

      _jobDB
        .LoadJobHistory(
          job1.RouteStartUTC.AddHours(-1),
          job1.RouteStartUTC.AddHours(10),
          new HashSet<string>(new[] { schId })
        )
        .ShouldBeEquivalentToD(
          new HistoricData()
          {
            Jobs = ImmutableDictionary.Create<string, HistoricJob>().Add(job2.UniqueStr, job2history),
            StationUse = job2SimUse,
          }
        );

      _jobDB
        .LoadJobHistory(job1.RouteStartUTC.AddHours(3), job1.RouteStartUTC.AddHours(10))
        .ShouldBeEquivalentToD(
          new HistoricData()
          {
            Jobs = ImmutableDictionary.Create<string, HistoricJob>().Add(job2.UniqueStr, job2history),
            StationUse = job2SimUse,
          }
        );

      _jobDB
        .LoadRecentJobHistory(job1.RouteStartUTC.AddHours(-1))
        .ShouldBeEquivalentToD(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary
              .Create<string, HistoricJob>()
              .Add(job1.UniqueStr, job1history)
              .Add(job2.UniqueStr, job2history),
            StationUse = job1StatUse.AddRange(job2SimUse),
            MostRecentSimulationId = schId2,
          }
        );

      _jobDB
        .LoadRecentJobHistory(job1.RouteStartUTC.AddHours(-1), new[] { schId })
        .ShouldBeEquivalentToD(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary.Create<string, HistoricJob>().Add(job2.UniqueStr, job2history),
            StationUse = job2SimUse,
            MostRecentSimulationId = schId2,
          }
        );

      _jobDB
        .LoadRecentJobHistory(job1.RouteEndUTC.AddHours(1))
        .ShouldBeEquivalentToD(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary.Create<string, HistoricJob>().Add(job2.UniqueStr, job2history),
            StationUse = job2SimUse,
            MostRecentSimulationId = schId2,
          }
        );

      _jobDB
        .LoadRecentJobHistory(job1.RouteEndUTC.AddHours(1), new[] { schId2 })
        .ShouldBeEquivalentTo(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary<string, HistoricJob>.Empty,
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
          }
        );

      _jobDB
        .LoadRecentJobHistory(job2.RouteEndUTC.AddHours(1))
        .ShouldBeEquivalentTo(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary<string, HistoricJob>.Empty,
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
            MostRecentSimulationId = schId2,
          }
        );

      _jobDB
        .LoadMostRecentSchedule()
        .ShouldBeEquivalentTo(
          // job2 is manually created and should be ignored
          new MostRecentSchedule()
          {
            LatestScheduleId = schId,
            Jobs = ImmutableList.Create(job1history),
            ExtraParts = job1ExtraParts.ToImmutableDictionary(),
          }
        );

      var belowJob2 = job2.UniqueStr.Substring(0, job2.UniqueStr.Length / 2);
      var aboveJob2 =
        belowJob2.Substring(0, belowJob2.Length - 1)
        + ((char)(belowJob2[belowJob2.Length - 1] + 1)).ToString();

      if (belowJob1.CompareTo(belowJob2) < 0)
      {
        _jobDB
          .LoadJobsBetween(belowJob1, aboveJob2)
          .ShouldBeEquivalentTo(ImmutableList.Create(job1history, job2history));
        _jobDB.LoadJobsBetween(belowJob1, belowJob2).ShouldBeEquivalentTo(ImmutableList.Create(job1history));
        _jobDB.LoadJobsBetween(aboveJob1, aboveJob2).ShouldBeEquivalentTo(ImmutableList.Create(job2history));
        _jobDB.LoadJobsBetween(aboveJob1, belowJob2).ShouldBeEmpty();
      }
      else
      {
        _jobDB
          .LoadJobsBetween(belowJob2, aboveJob1)
          .ShouldBeEquivalentTo(ImmutableList.Create(job2history, job1history));
        _jobDB.LoadJobsBetween(belowJob2, belowJob1).ShouldBeEquivalentTo(ImmutableList.Create(job2history));
        _jobDB.LoadJobsBetween(aboveJob2, aboveJob1).ShouldBeEquivalentTo(ImmutableList.Create(job1history));
        _jobDB.LoadJobsBetween(aboveJob2, belowJob1).ShouldBeEmpty();
      }
    }

    [Test]
    public void SimDays()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      var now = DateTime.UtcNow;
      var schId1 = "schId" + _fixture.Create<string>();
      var job1 = RandJob() with { RouteStartUTC = now, RouteEndUTC = now.AddHours(1) };
      var simDays1 = _fixture.Create<IEnumerable<SimulatedDayUsage>>().OrderBy(s => s.Day).ToImmutableList();
      var job1history = job1.CloneToDerived<HistoricJob, Job>() with
      {
        ScheduleId = schId1,
        CopiedToSystem = true,
        Decrements = ImmutableList<DecrementQuantity>.Empty,
      };

      simDays1.Count.ShouldBeGreaterThan(0);

      _jobDB.AddJobs(
        new NewJobs()
        {
          ScheduleId = schId1,
          Jobs = ImmutableList.Create(job1),
          SimDayUsage = simDays1,
        },
        null,
        addAsCopiedToSystem: true
      );

      _jobDB
        .LoadJobHistory(now, now)
        .ShouldBeEquivalentToD(
          new HistoricData()
          {
            Jobs = ImmutableDictionary.Create<string, HistoricJob>().Add(job1.UniqueStr, job1history),
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
          }
        );

      _jobDB
        .LoadRecentJobHistory(now)
        .ShouldBeEquivalentToD(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary.Create<string, HistoricJob>().Add(job1.UniqueStr, job1history),
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
            MostRecentSimulationId = schId1,
            MostRecentSimDayUsage = simDays1,
          }
        );

      _jobDB
        .LoadRecentJobHistory(now, new[] { schId1 })
        .ShouldBeEquivalentTo(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary<string, HistoricJob>.Empty,
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
          }
        );

      var schId2 = schId1 + "222";
      var job2 = RandJob() with { RouteStartUTC = now, RouteEndUTC = now.AddHours(2) };
      var simDays2 = _fixture.Create<IEnumerable<SimulatedDayUsage>>().OrderBy(s => s.Day).ToImmutableList();
      var job2history = job2.CloneToDerived<HistoricJob, Job>() with
      {
        ScheduleId = schId2,
        CopiedToSystem = true,
        Decrements = ImmutableList<DecrementQuantity>.Empty,
      };

      _jobDB.AddJobs(
        new NewJobs()
        {
          ScheduleId = schId2,
          Jobs = ImmutableList.Create(job2),
          SimDayUsage = simDays2,
        },
        schId1,
        addAsCopiedToSystem: true
      );

      _jobDB
        .LoadRecentJobHistory(now)
        .ShouldBeEquivalentToD(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary
              .Create<string, HistoricJob>()
              .Add(job1.UniqueStr, job1history)
              .Add(job2.UniqueStr, job2history),
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
            MostRecentSimulationId = schId2,
            MostRecentSimDayUsage = simDays2,
          }
        );

      _jobDB
        .LoadRecentJobHistory(now, new[] { schId2 })
        .ShouldBeEquivalentToD(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary.Create<string, HistoricJob>().Add(job1.UniqueStr, job1history),
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
          }
        );
      _jobDB
        .LoadRecentJobHistory(now, new[] { schId1 })
        .ShouldBeEquivalentToD(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary.Create<string, HistoricJob>().Add(job2.UniqueStr, job2history),
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
            MostRecentSimulationId = schId2,
            MostRecentSimDayUsage = simDays2,
          }
        );
    }

    [Test]
    public void SimDaysWithoutJobs()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      var now = DateTime.UtcNow;
      var schId1 = "schId" + _fixture.Create<string>();
      var simDays1 = _fixture.Create<IEnumerable<SimulatedDayUsage>>().OrderBy(s => s.Day).ToImmutableList();

      _jobDB.AddJobs(
        new NewJobs()
        {
          ScheduleId = schId1,
          Jobs = ImmutableList<Job>.Empty,
          SimDayUsage = simDays1,
        },
        null,
        addAsCopiedToSystem: true
      );

      _jobDB
        .LoadJobHistory(now, now)
        .ShouldBeEquivalentTo(
          new HistoricData()
          {
            Jobs = ImmutableDictionary<string, HistoricJob>.Empty,
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
          }
        );

      _jobDB
        .LoadRecentJobHistory(now)
        .ShouldBeEquivalentTo(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary<string, HistoricJob>.Empty,
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
            MostRecentSimulationId = schId1,
            MostRecentSimDayUsage = simDays1,
          }
        );

      _jobDB
        .LoadRecentJobHistory(now, new[] { schId1 })
        .ShouldBeEquivalentTo(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary<string, HistoricJob>.Empty,
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
          }
        );
    }

    [Test]
    public void SetsComment()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      var schId = "SchId" + _fixture.Create<string>();
      var job1 = RandJob() with { ManuallyCreated = false };

      _jobDB.AddJobs(
        new NewJobs() { ScheduleId = schId, Jobs = ImmutableList.Create(job1) },
        null,
        addAsCopiedToSystem: true
      );

      var job1history = job1.CloneToDerived<HistoricJob, Job>() with
      {
        ScheduleId = schId,
        CopiedToSystem = true,
        Decrements = ImmutableList<DecrementQuantity>.Empty,
      };

      _jobDB.LoadJob(job1.UniqueStr).ShouldBeEquivalentTo(job1history);

      var newComment = _fixture.Create<string>();
      _jobDB.SetJobComment(job1.UniqueStr, newComment);

      _jobDB.LoadJob(job1.UniqueStr).ShouldBeEquivalentTo(job1history with { Comment = newComment });
    }

    [Test]
    public void UpdatesHold()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      var schId = "SchId" + _fixture.Create<string>();
      var job1 = RandJob() with { ManuallyCreated = false };

      _jobDB.AddJobs(
        new NewJobs() { ScheduleId = schId, Jobs = ImmutableList.Create(job1) },
        null,
        addAsCopiedToSystem: true
      );

      var job1history = job1.CloneToDerived<HistoricJob, Job>() with
      {
        ScheduleId = schId,
        CopiedToSystem = true,
        Decrements = ImmutableList<DecrementQuantity>.Empty,
      };

      _jobDB.LoadJob(job1.UniqueStr).ShouldBeEquivalentTo(job1history);

      var newHold = _fixture.Create<HoldPattern>();

      _jobDB.UpdateJobHold(job1.UniqueStr, newHold);

      _jobDB.LoadJob(job1.UniqueStr).ShouldBeEquivalentTo(job1history with { HoldJob = newHold });

      var newMachHold = _fixture.Create<HoldPattern>();
      _jobDB.UpdateJobMachiningHold(job1.UniqueStr, 1, 1, newMachHold);

      _jobDB
        .LoadJob(job1.UniqueStr)
        .ShouldBeEquivalentTo(
          job1history with
          {
            HoldJob = newHold,
            Processes = (
              new[]
              {
                new ProcessInfo()
                {
                  Paths = (new[] { job1.Processes[0].Paths[0] with { HoldMachining = newMachHold } })
                    .Concat(job1.Processes[0].Paths.Skip(1))
                    .ToImmutableList(),
                },
              }
            )
              .Concat(job1.Processes.Skip(1))
              .ToImmutableList(),
          }
        );

      var newLoadHold = _fixture.Create<HoldPattern>();
      _jobDB.UpdateJobLoadUnloadHold(job1.UniqueStr, 1, 2, newLoadHold);

      _jobDB
        .LoadJob(job1.UniqueStr)
        .ShouldBeEquivalentTo(
          job1history with
          {
            HoldJob = newHold,
            Processes = (
              new[]
              {
                new ProcessInfo()
                {
                  Paths = (
                    new[]
                    {
                      job1.Processes[0].Paths[0] with
                      {
                        HoldMachining = newMachHold,
                      },
                      job1.Processes[0].Paths[1] with
                      {
                        HoldLoadUnload = newLoadHold,
                      },
                    }
                  )
                    .Concat(job1.Processes[0].Paths.Skip(2))
                    .ToImmutableList(),
                },
              }
            )
              .Concat(job1.Processes.Skip(1))
              .ToImmutableList(),
          }
        );
    }

    [Test]
    public void MarksAsCopied()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      var schId = "SchId" + _fixture.Create<string>();
      var job1 = RandJob() with { ManuallyCreated = false };

      _jobDB.AddJobs(
        new NewJobs() { ScheduleId = schId, Jobs = ImmutableList.Create(job1) },
        null,
        addAsCopiedToSystem: false
      );

      var job1history = job1.CloneToDerived<HistoricJob, Job>() with
      {
        ScheduleId = schId,
        CopiedToSystem = false,
        Decrements = ImmutableList<DecrementQuantity>.Empty,
      };

      _jobDB
        .LoadJobsNotCopiedToSystem(job1.RouteStartUTC.AddHours(-10), job1.RouteStartUTC.AddHours(-5))
        .ShouldBeEmpty();

      _jobDB
        .LoadJobsNotCopiedToSystem(job1.RouteStartUTC.AddHours(-1), job1.RouteStartUTC.AddHours(2))
        .ShouldBeEquivalentTo(ImmutableList.Create(job1history));

      _jobDB.MarkJobCopiedToSystem(job1.UniqueStr);

      _jobDB
        .LoadJobsNotCopiedToSystem(job1.RouteStartUTC.AddHours(-1), job1.RouteStartUTC.AddHours(2))
        .ShouldBeEmpty();

      _jobDB.LoadJob(job1.UniqueStr).ShouldBeEquivalentTo(job1history with { CopiedToSystem = true });
    }

    [Test]
    public void ArchivesJobs()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      var schId = "SchId" + _fixture.Create<string>();
      var job1 = RandJob() with { Archived = false };

      _jobDB.AddJobs(
        new NewJobs() { ScheduleId = schId, Jobs = ImmutableList.Create(job1) },
        null,
        addAsCopiedToSystem: true
      );

      var job1history = job1.CloneToDerived<HistoricJob, Job>() with
      {
        ScheduleId = schId,
        CopiedToSystem = true,
        Decrements = ImmutableList<DecrementQuantity>.Empty,
      };

      _jobDB.LoadJob(job1.UniqueStr).ShouldBeEquivalentTo(job1history);
      _jobDB.LoadUnarchivedJobs().ShouldBeEquivalentTo(ImmutableList.Create(job1history));

      _jobDB.ArchiveJob(job1.UniqueStr);

      _jobDB.LoadJob(job1.UniqueStr).ShouldBeEquivalentTo(job1history with { Archived = true });
      _jobDB.LoadUnarchivedJobs().ShouldBeEmpty();
    }

    [Test]
    public void Decrements()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      var now = DateTime.UtcNow;
      var job1 = RandJob(now);
      var job2 = RandJob(now);

      _jobDB.AddJobs(
        new NewJobs() { Jobs = ImmutableList.Create<Job>(job1, job2), ScheduleId = "schId" },
        null,
        addAsCopiedToSystem: false
      );
      _jobDB.MarkJobCopiedToSystem(job2.UniqueStr);

      _jobDB
        .LoadJobsNotCopiedToSystem(now, now)
        .ShouldBeEquivalentTo(
          ImmutableList.Create(
            job1.CloneToDerived<HistoricJob, Job>() with
            {
              CopiedToSystem = false,
              ScheduleId = "schId",
              Decrements = ImmutableList<DecrementQuantity>.Empty,
            }
          )
        );

      var time1 = now.AddHours(-2);
      _jobDB.AddNewDecrement(
        new[]
        {
          new NewDecrementQuantity
          {
            JobUnique = job1.UniqueStr,
            Part = job1.PartName,
            Quantity = 53,
          },
          new NewDecrementQuantity()
          {
            JobUnique = job2.UniqueStr,
            Part = job2.PartName,
            Quantity = 821,
          },
        },
        time1
      );

      var expected1JobAndDecr = new[]
      {
        new JobAndDecrementQuantity()
        {
          DecrementId = 0,
          JobUnique = job1.UniqueStr,
          TimeUTC = time1,
          Part = job1.PartName,
          Quantity = 53,
        },
        new JobAndDecrementQuantity()
        {
          DecrementId = 0,
          JobUnique = job2.UniqueStr,
          TimeUTC = time1,
          Part = job2.PartName,
          Quantity = 821,
        },
      };

      var expected1Job1 = ImmutableList.Create(
        new DecrementQuantity()
        {
          DecrementId = 0,
          TimeUTC = time1,
          Quantity = 53,
        }
      );
      var expected1Job2 = ImmutableList.Create(
        new DecrementQuantity()
        {
          DecrementId = 0,
          TimeUTC = time1,
          Quantity = 821,
        }
      );

      _jobDB.LoadDecrementQuantitiesAfter(-1).ShouldBe(expected1JobAndDecr, ignoreOrder: true);

      _jobDB
        .LoadJobsNotCopiedToSystem(now, now, includeDecremented: true)
        .ShouldBeEquivalentTo(
          ImmutableList.Create(
            job1.CloneToDerived<HistoricJob, Job>() with
            {
              CopiedToSystem = false,
              ScheduleId = "schId",
              Decrements = expected1Job1,
            }
          )
        );

      _jobDB.LoadJobsNotCopiedToSystem(now, now, includeDecremented: false).ShouldBeEmpty();

      _jobDB
        .LoadJob(job1.UniqueStr)
        .ShouldBeEquivalentTo(
          job1.CloneToDerived<HistoricJob, Job>() with
          {
            CopiedToSystem = false,
            ScheduleId = "schId",
            Decrements = expected1Job1,
          }
        );

      _jobDB
        .LoadJob(job2.UniqueStr)
        .ShouldBeEquivalentTo(
          job2.CloneToDerived<HistoricJob, Job>() with
          {
            CopiedToSystem = true,
            ScheduleId = "schId",
            Decrements = expected1Job2,
          }
        );

      _jobDB
        .LoadJobHistory(now, now)
        .ShouldBeEquivalentToD(
          new HistoricData()
          {
            Jobs = ImmutableDictionary<string, HistoricJob>
              .Empty.Add(
                job1.UniqueStr,
                job1.CloneToDerived<HistoricJob, Job>() with
                {
                  CopiedToSystem = false,
                  ScheduleId = "schId",
                  Decrements = expected1Job1,
                }
              )
              .Add(
                job2.UniqueStr,
                job2.CloneToDerived<HistoricJob, Job>() with
                {
                  CopiedToSystem = true,
                  ScheduleId = "schId",
                  Decrements = expected1Job2,
                }
              ),
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
          }
        );

      // now a second decrement
      var time2 = now.AddHours(-1);

      _jobDB.AddNewDecrement(
        new[]
        {
          new NewDecrementQuantity()
          {
            JobUnique = job1.UniqueStr,
            Part = job1.PartName,
            Quantity = 26,
          },
          new NewDecrementQuantity()
          {
            JobUnique = job2.UniqueStr,
            Part = job2.PartName,
            Quantity = 44,
          },
        },
        time2,
        new[]
        {
          new RemovedBooking() { JobUnique = job1.UniqueStr, BookingId = job1.BookingIds.First() },
          new RemovedBooking() { JobUnique = job2.UniqueStr, BookingId = job2.BookingIds.First() },
        }
      );

      var expected2JobAndDecr = new[]
      {
        new JobAndDecrementQuantity()
        {
          DecrementId = 1,
          JobUnique = job1.UniqueStr,
          TimeUTC = time2,
          Part = job1.PartName,
          Quantity = 26,
        },
        new JobAndDecrementQuantity()
        {
          DecrementId = 1,
          JobUnique = job2.UniqueStr,
          TimeUTC = time2,
          Part = job2.PartName,
          Quantity = 44,
        },
      };

      _jobDB
        .LoadDecrementQuantitiesAfter(-1)
        .ShouldBe(expected1JobAndDecr.Concat(expected2JobAndDecr), ignoreOrder: true);
      _jobDB.LoadDecrementQuantitiesAfter(0).ShouldBe(expected2JobAndDecr, ignoreOrder: true);
      _jobDB.LoadDecrementQuantitiesAfter(1).ShouldBeEmpty();

      _jobDB
        .LoadDecrementQuantitiesAfter(time1.AddHours(-1))
        .ShouldBe(expected1JobAndDecr.Concat(expected2JobAndDecr), ignoreOrder: true);
      _jobDB
        .LoadDecrementQuantitiesAfter(time1.AddMinutes(30))
        .ShouldBe(expected2JobAndDecr, ignoreOrder: true);
      _jobDB.LoadDecrementQuantitiesAfter(time2.AddMinutes(30)).ShouldBeEmpty();

      _jobDB
        .LoadDecrementsForJob(job1.UniqueStr)
        .ShouldBe(
          ImmutableList.Create(
            new DecrementQuantity()
            {
              DecrementId = 0,
              TimeUTC = time1,
              Quantity = 53,
            },
            new DecrementQuantity()
            {
              DecrementId = 1,
              TimeUTC = time2,
              Quantity = 26,
            }
          ),
          ignoreOrder: true
        );

      _jobDB
        .LoadDecrementsForJob(job2.UniqueStr)
        .ShouldBe(
          ImmutableList.Create(
            new DecrementQuantity()
            {
              DecrementId = 0,
              TimeUTC = time1,
              Quantity = 821,
            },
            new DecrementQuantity()
            {
              DecrementId = 1,
              TimeUTC = time2,
              Quantity = 44,
            }
          ),
          ignoreOrder: true
        );
    }

    [Test]
    public void DecrementsDuringArchive()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      var job = RandJob() with { Archived = false };

      _jobDB.AddJobs(new NewJobs() { Jobs = ImmutableList.Create(job), ScheduleId = "theschId" }, null, true);

      _jobDB
        .LoadJob(job.UniqueStr)
        .ShouldBeEquivalentTo(
          job.CloneToDerived<HistoricJob, Job>() with
          {
            CopiedToSystem = true,
            ScheduleId = "theschId",
            Decrements = ImmutableList<DecrementQuantity>.Empty,
          }
        );

      var now = DateTime.UtcNow;

      _jobDB.ArchiveJobs(
        new[] { job.UniqueStr },
        new[]
        {
          new NewDecrementQuantity()
          {
            JobUnique = job.UniqueStr,
            Part = job.PartName,
            Quantity = 44,
          },
        },
        now
      );

      _jobDB
        .LoadJob(job.UniqueStr)
        .ShouldBeEquivalentTo(
          job.CloneToDerived<HistoricJob, Job>() with
          {
            Archived = true,
            CopiedToSystem = true,
            ScheduleId = "theschId",
            Decrements = ImmutableList.Create(
              new DecrementQuantity()
              {
                DecrementId = 0,
                TimeUTC = now,
                Quantity = 44,
              }
            ),
          }
        );
    }

    [Test]
    public void Programs()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      var schId = "SchId" + _fixture.Create<string>();
      var job1 = RandJob()
        .AdjustPath(
          1,
          1,
          d =>
            d with
            {
              Stops = ImmutableList.Create(
                new MachiningStop()
                {
                  Program = "aaa",
                  ProgramRevision = null,
                  StationGroup = "",
                  Stations = [],
                  ExpectedCycleTime = TimeSpan.Zero,
                }
              ),
            }
        )
        .AdjustPath(
          1,
          2,
          d =>
            d with
            {
              Stops = ImmutableList.Create(
                new MachiningStop()
                {
                  Program = "aaa",
                  ProgramRevision = 1,
                  StationGroup = "",
                  Stations = [],
                  ExpectedCycleTime = TimeSpan.Zero,
                }
              ),
            }
        )
        .AdjustPath(
          2,
          1,
          d =>
            d with
            {
              Stops = ImmutableList.Create(
                new MachiningStop()
                {
                  Program = "bbb",
                  ProgramRevision = null,
                  StationGroup = "",
                  Stations = [],
                  ExpectedCycleTime = TimeSpan.Zero,
                }
              ),
            }
        )
        .AdjustPath(
          2,
          2,
          d =>
            d with
            {
              Stops = ImmutableList.Create(
                new MachiningStop()
                {
                  Program = "bbb",
                  ProgramRevision = 6,
                  StationGroup = "",
                  Stations = [],
                  ExpectedCycleTime = TimeSpan.Zero,
                }
              ),
            }
        );

      var initialWorks = _fixture
        .Create<List<Workorder>>()
        .Select(w => w with { Programs = w.Programs.OrderBy(p => p.ProcessNumber).ToImmutableList() })
        .ToList();
      initialWorks[0] = initialWorks[0] with
      {
        Programs =
        [
          new ProgramForJobStep()
          {
            ProcessNumber = 1,
            ProgramName = "aaa",
            Revision = null,
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 0,
            ProgramName = "aaa",
            Revision = 1,
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 1,
            ProgramName = "bbb",
            Revision = null,
          },
        ],
      };
      initialWorks[1] = initialWorks[1] with
      {
        Programs =
        [
          new ProgramForJobStep()
          {
            ProcessNumber = 1,
            StopIndex = 0,
            ProgramName = "bbb",
            Revision = 6,
          },
        ],
      };

      var workStart = ImmutableList.Create(
        _fixture
          .Build<WorkorderSimFilled>()
          .With(w => w.WorkorderId, initialWorks[0].WorkorderId)
          .With(w => w.Part, initialWorks[0].Part)
          .Create(),
        _fixture
          .Build<WorkorderSimFilled>()
          .With(w => w.WorkorderId, initialWorks[1].WorkorderId)
          .With(w => w.Part, initialWorks[1].Part)
          .Create()
      );

      _jobDB.AddJobs(
        new NewJobs
        {
          ScheduleId = schId,
          Jobs = ImmutableList.Create(job1),
          CurrentUnfilledWorkorders = initialWorks.ToImmutableList(),
          Programs = ImmutableList.Create(
            new NewProgramContent()
            {
              ProgramName = "aaa",
              Revision = 0, // auto assign
              Comment = "aaa comment",
              ProgramContent = "aaa program content",
            },
            new NewProgramContent()
            {
              ProgramName = "bbb",
              Revision = 6, // new revision
              Comment = "bbb comment",
              ProgramContent = "bbb program content",
            }
          ),
          SimWorkordersFilled = workStart,
        },
        null,
        addAsCopiedToSystem: true
      );

      // should lookup latest revision to 1 and 6
      job1 = job1.AdjustPath(
          1,
          1,
          d =>
            d with
            {
              Stops = ImmutableList.Create(
                new MachiningStop()
                {
                  Program = "aaa",
                  ProgramRevision = 1,
                  StationGroup = "",
                  Stations = [],
                  ExpectedCycleTime = TimeSpan.Zero,
                }
              ),
            }
        )
        .AdjustPath(
          2,
          1,
          d =>
            d with
            {
              Stops = ImmutableList.Create(
                new MachiningStop()
                {
                  Program = "bbb",
                  ProgramRevision = 6,
                  StationGroup = "",
                  Stations = [],
                  ExpectedCycleTime = TimeSpan.Zero,
                }
              ),
            }
        );
      initialWorks[0] = initialWorks[0] with
      {
        Programs =
        [
          new ProgramForJobStep()
          {
            ProcessNumber = 1,
            ProgramName = "aaa",
            Revision = 1,
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 0,
            ProgramName = "aaa",
            Revision = 1,
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 1,
            ProgramName = "bbb",
            Revision = 6,
          },
        ],
      };

      _jobDB
        .LoadJob(job1.UniqueStr)
        .ShouldBeEquivalentTo(
          job1.CloneToDerived<HistoricJob, Job>() with
          {
            ScheduleId = schId,
            CopiedToSystem = true,
            Decrements = ImmutableList<DecrementQuantity>.Empty,
          }
        );

      _jobDB
        .GetActiveWorkorders()
        .ShouldBeEquivalentTo(
          initialWorks
            .Select(w => new ActiveWorkorder()
            {
              WorkorderId = w.WorkorderId,
              Part = w.Part,
              PlannedQuantity = w.Quantity,
              CompletedQuantity = 0,
              DueDate = w.DueDate,
              Priority = w.Priority,
              ElapsedStationTime = ImmutableDictionary<string, TimeSpan>.Empty,
              ActiveStationTime = ImmutableDictionary<string, TimeSpan>.Empty,
            })
            .Select(w =>
            {
              if (w.WorkorderId == initialWorks[0].WorkorderId)
              {
                return w with
                {
                  SimulatedStart = workStart[0].Started,
                  SimulatedFilled = workStart[0].Filled,
                };
              }
              else if (w.WorkorderId == initialWorks[1].WorkorderId)
              {
                return w with
                {
                  SimulatedStart = workStart[1].Started,
                  SimulatedFilled = workStart[1].Filled,
                };
              }
              else
              {
                return w;
              }
            })
            .ToImmutableList()
        );

      _jobDB
        .WorkordersById(initialWorks[0].WorkorderId)
        .ShouldBeEquivalentTo(ImmutableList.Create(initialWorks[0]));
      _jobDB
        .WorkordersById(initialWorks.Select(w => w.WorkorderId).ToHashSet())
        .ShouldBeEquivalentToD(
          initialWorks.GroupBy(w => w.WorkorderId).ToImmutableDictionary(g => g.Key, g => g.ToImmutableList())
        );

      _jobDB
        .LoadProgram("aaa", 1)
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 1,
            Comment = "aaa comment",
          }
        );
      _jobDB
        .LoadMostRecentProgram("aaa")
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 1,
            Comment = "aaa comment",
          }
        );
      _jobDB
        .LoadProgram("bbb", 6)
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "bbb",
            Revision = 6,
            Comment = "bbb comment",
          }
        );
      _jobDB
        .LoadMostRecentProgram("bbb")
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "bbb",
            Revision = 6,
            Comment = "bbb comment",
          }
        );
      _jobDB.LoadProgram("aaa", 2).ShouldBeNull();
      _jobDB.LoadProgram("ccc", 1).ShouldBeNull();

      _jobDB.LoadProgramContent("aaa", 1).ShouldBe("aaa program content");
      _jobDB.LoadProgramContent("bbb", 6).ShouldBe("bbb program content");
      _jobDB.LoadProgramContent("aaa", 2).ShouldBeNull();
      _jobDB.LoadProgramContent("ccc", 1).ShouldBeNull();
      _jobDB.LoadProgramsInCellController().ShouldBeEmpty();

      // error on program content mismatch
      Should
        .Throw<BadRequestException>(() =>
          _jobDB.AddJobs(
            new NewJobs
            {
              Jobs = ImmutableList<Job>.Empty,
              ScheduleId = "unusedSchId",
              Programs = ImmutableList.Create(
                new NewProgramContent()
                {
                  ProgramName = "aaa",
                  Revision = 0, // auto assign
                  Comment = "aaa comment rev 2",
                  ProgramContent = "aaa program content rev 2",
                },
                new NewProgramContent()
                {
                  ProgramName = "bbb",
                  Revision = 6, // existing revision
                  Comment = "bbb comment",
                  ProgramContent = "awofguhweoguhweg",
                }
              ),
            },
            null,
            addAsCopiedToSystem: true
          )
        )
        .Message.ShouldBe("Program bbb rev6 has already been used and the program contents do not match.");

      Should
        .Throw<BadRequestException>(() =>
          _jobDB.AddPrograms(
            new List<NewProgramContent>
            {
              new NewProgramContent()
              {
                ProgramName = "aaa",
                Revision = 0, // auto assign
                Comment = "aaa comment rev 2",
                ProgramContent = "aaa program content rev 2",
              },
              new NewProgramContent()
              {
                ProgramName = "bbb",
                Revision = 6, // existing revision
                Comment = "bbb comment",
                ProgramContent = "awofguhweoguhweg",
              },
            },
            DateTime.Parse("2019-09-14T03:52:12Z")
          )
        )
        .Message.ShouldBe("Program bbb rev6 has already been used and the program contents do not match.");

      _jobDB.LoadProgram("aaa", 2).ShouldBeNull();
      _jobDB
        .LoadMostRecentProgram("aaa")
        .ShouldBe(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 1,
            Comment = "aaa comment",
          }
        );
      _jobDB
        .LoadMostRecentProgram("bbb")
        .ShouldBe(
          new ProgramRevision()
          {
            ProgramName = "bbb",
            Revision = 6,
            Comment = "bbb comment",
          }
        );
      _jobDB.LoadProgramContent("aaa", 1).ShouldBe("aaa program content");
      _jobDB.LoadProgramContent("aaa", 2).ShouldBeNull();

      // adds new workorders and programs
      var newWorkorders = _fixture.Create<List<Workorder>>();
      newWorkorders[0] = newWorkorders[0] with
      {
        Programs =
        [
          new ProgramForJobStep()
          {
            ProcessNumber = 1,
            ProgramName = "aaa",
            Revision = 1,
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 0,
            ProgramName = "aaa",
            Revision = 2,
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 1,
            ProgramName = "bbb",
            Revision = 6,
          },
        ],
      };
      newWorkorders[1] = newWorkorders[1] with
      {
        Programs =
        [
          new ProgramForJobStep()
          {
            ProcessNumber = 1,
            StopIndex = 0,
            ProgramName = "ccc",
            Revision = 0,
          },
        ],
      };

      _jobDB.AddJobs(
        new NewJobs()
        {
          ScheduleId = schId + "ZZZ",
          Jobs = ImmutableList<Job>.Empty,
          CurrentUnfilledWorkorders = newWorkorders.ToImmutableList(),
          Programs = ImmutableList.Create(
            new NewProgramContent()
            {
              ProgramName = "ccc",
              Revision = 0,
              Comment = "the ccc comment",
              ProgramContent = "ccc first program",
            }
          ),
        },
        null,
        true
      );

      // update with allocated revisions
      newWorkorders[1] = newWorkorders[1] with
      {
        Programs =
        [
          new ProgramForJobStep()
          {
            ProcessNumber = 1,
            StopIndex = 0,
            ProgramName = "ccc",
            Revision = 1,
          },
        ],
      };

      _jobDB
        .GetActiveWorkorders()
        .Select(w => w.WorkorderId)
        .ToList()
        .ShouldBeEquivalentTo(newWorkorders.Select(w => w.WorkorderId).ToList()); // initialWorks have been archived and don't appear
      _jobDB
        .WorkordersById(initialWorks[0].WorkorderId)
        .ShouldBeEquivalentTo(ImmutableList.Create(initialWorks[0])); // but still exist when looked up directly
      _jobDB
        .GetActiveWorkorders(additionalWorkorders: new HashSet<string>() { initialWorks[0].WorkorderId })
        .Select(w => w.WorkorderId)
        .ToImmutableList()
        .ShouldBeEquivalentTo(
          newWorkorders.Select(w => w.WorkorderId).Append(initialWorks[0].WorkorderId).ToImmutableList()
        );

      _jobDB
        .LoadMostRecentProgram("ccc")
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "ccc",
            Revision = 1,
            Comment = "the ccc comment",
          }
        );

      // now should ignore when program content matches exact revision or most recent revision
      var schId2 = "ZZ" + schId;
      _jobDB.AddJobs(
        new NewJobs
        {
          ScheduleId = schId2,
          Jobs = ImmutableList<Job>.Empty,
          Programs = ImmutableList.Create(
            new NewProgramContent()
            {
              ProgramName = "aaa",
              Revision = 0, // auto assign
              Comment = "aaa comment rev 2",
              ProgramContent = "aaa program content rev 2",
            },
            new NewProgramContent()
            {
              ProgramName = "bbb",
              Revision = 6, // existing revision
              Comment = "bbb comment",
              ProgramContent = "bbb program content",
            },
            new NewProgramContent()
            {
              ProgramName = "ccc",
              Revision = 0, // auto assign
              Comment = "ccc new comment", // comment does not match most recent revision
              ProgramContent =
                "ccc first program" // content matches most recent revision
              ,
            }
          ),
        },
        schId,
        addAsCopiedToSystem: true
      );

      _jobDB
        .LoadProgram("aaa", 2)
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 2, // creates new revision
            Comment = "aaa comment rev 2",
          }
        );
      _jobDB
        .LoadMostRecentProgram("aaa")
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 2,
            Comment = "aaa comment rev 2",
          }
        );
      _jobDB
        .LoadMostRecentProgram("bbb")
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "bbb",
            Revision = 6,
            Comment = "bbb comment",
          }
        );
      _jobDB.LoadProgramContent("aaa", 2).ShouldBe("aaa program content rev 2");
      _jobDB
        .LoadMostRecentProgram("ccc")
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "ccc",
            Revision = 1,
            Comment = "the ccc comment",
          }
        );
      _jobDB.LoadProgramContent("ccc", 1).ShouldBe("ccc first program");

      //now set cell controller names
      _jobDB.LoadProgramsInCellController().ShouldBeEmpty();
      _jobDB.SetCellControllerProgramForProgram("aaa", 1, "aaa-1");
      _jobDB.SetCellControllerProgramForProgram("bbb", 6, "bbb-6");

      _jobDB
        .ProgramFromCellControllerProgram("aaa-1")
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 1,
            Comment = "aaa comment",
            CellControllerProgramName = "aaa-1",
          }
        );
      _jobDB
        .LoadProgram("aaa", 1)
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 1,
            Comment = "aaa comment",
            CellControllerProgramName = "aaa-1",
          }
        );
      _jobDB
        .ProgramFromCellControllerProgram("bbb-6")
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "bbb",
            Revision = 6,
            Comment = "bbb comment",
            CellControllerProgramName = "bbb-6",
          }
        );
      _jobDB
        .LoadMostRecentProgram("bbb")
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "bbb",
            Revision = 6,
            Comment = "bbb comment",
            CellControllerProgramName = "bbb-6",
          }
        );
      _jobDB.ProgramFromCellControllerProgram("aagaiouhgi").ShouldBeNull();
      _jobDB
        .LoadProgramsInCellController()
        .ShouldBeEquivalentTo(
          new List<ProgramRevision>
          {
            new ProgramRevision()
            {
              ProgramName = "aaa",
              Revision = 1,
              Comment = "aaa comment",
              CellControllerProgramName = "aaa-1",
            },
            new ProgramRevision()
            {
              ProgramName = "bbb",
              Revision = 6,
              Comment = "bbb comment",
              CellControllerProgramName = "bbb-6",
            },
          }
        );

      _jobDB.SetCellControllerProgramForProgram("aaa", 1, null);

      _jobDB
        .LoadProgramsInCellController()
        .ShouldBeEquivalentTo(
          new List<ProgramRevision>
          {
            new ProgramRevision()
            {
              ProgramName = "bbb",
              Revision = 6,
              Comment = "bbb comment",
              CellControllerProgramName = "bbb-6",
            },
          }
        );

      _jobDB.ProgramFromCellControllerProgram("aaa-1").ShouldBeNull();
      _jobDB
        .LoadProgram("aaa", 1)
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 1,
            Comment = "aaa comment",
          }
        );

      Should
        .Throw<Exception>(() => _jobDB.SetCellControllerProgramForProgram("aaa", 2, "bbb-6"))
        .Message.ShouldBe("Cell program name bbb-6 already in use");

      _jobDB.AddPrograms(
        new[]
        {
          new NewProgramContent()
          {
            ProgramName = "aaa",
            Revision = 0, // should be ignored because comment and content matches revision 1
            Comment = "aaa comment",
            ProgramContent = "aaa program content",
          },
          new NewProgramContent()
          {
            ProgramName = "bbb",
            Revision = 0, // allocate new
            Comment = "bbb comment rev7",
            ProgramContent = "bbb program content rev7",
          },
        },
        job1.RouteStartUTC
      );

      _jobDB
        .LoadMostRecentProgram("aaa")
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 2, // didn't allocate 3
            Comment = "aaa comment rev 2",
          }
        );
      _jobDB
        .LoadMostRecentProgram("bbb")
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "bbb",
            Revision = 7,
            Comment = "bbb comment rev7",
          }
        );
      _jobDB.LoadProgramContent("bbb", 7).ShouldBe("bbb program content rev7");

      // adds new when comment matches, but content does not
      _jobDB.AddPrograms(
        new[]
        {
          new NewProgramContent()
          {
            ProgramName = "aaa",
            Revision = 0, // allocate new
            Comment = "aaa comment", // comment matches
            ProgramContent =
              "aaa program content rev 3" // content does not
            ,
          },
        },
        job1.RouteStartUTC
      );

      _jobDB
        .LoadMostRecentProgram("aaa")
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 3,
            Comment = "aaa comment",
          }
        );
      _jobDB.LoadProgramContent("aaa", 3).ShouldBeEquivalentTo("aaa program content rev 3");

      // loading all revisions
      _jobDB
        .LoadProgramRevisionsInDescendingOrderOfRevision("aaa", 3, startRevision: null)
        .ShouldBeEquivalentTo(
          ImmutableList.Create(
            new ProgramRevision()
            {
              ProgramName = "aaa",
              Revision = 3,
              Comment = "aaa comment",
            },
            new ProgramRevision()
            {
              ProgramName = "aaa",
              Revision = 2,
              Comment = "aaa comment rev 2",
            },
            new ProgramRevision()
            {
              ProgramName = "aaa",
              Revision = 1,
              Comment = "aaa comment",
            }
          )
        );
      _jobDB
        .LoadProgramRevisionsInDescendingOrderOfRevision("aaa", 1, startRevision: null)
        .ShouldBeEquivalentTo(
          ImmutableList.Create(
            new ProgramRevision()
            {
              ProgramName = "aaa",
              Revision = 3,
              Comment = "aaa comment",
            }
          )
        );
      _jobDB
        .LoadProgramRevisionsInDescendingOrderOfRevision("aaa", 2, startRevision: 1)
        .ShouldBeEquivalentTo(
          ImmutableList.Create(
            new ProgramRevision()
            {
              ProgramName = "aaa",
              Revision = 1,
              Comment = "aaa comment",
            }
          )
        );

      _jobDB.LoadProgramRevisionsInDescendingOrderOfRevision("wesrfohergh", 10000, null).ShouldBeEmpty();
    }

    [Test]
    public void NegativeProgramRevisions()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      // add an existing revision 6 for bbb and 3,4 for ccc
      _jobDB.AddJobs(
        new NewJobs
        {
          Jobs = ImmutableList<Job>.Empty,
          ScheduleId = "negprogschId",
          Programs = ImmutableList.Create(
            new NewProgramContent()
            {
              ProgramName = "bbb",
              Revision = 6,
              Comment = "bbb comment 6",
              ProgramContent = "bbb program content 6",
            },
            new NewProgramContent()
            {
              ProgramName = "ccc",
              Revision = 3,
              Comment = "ccc comment 3",
              ProgramContent = "ccc program content 3",
            },
            new NewProgramContent()
            {
              ProgramName = "ccc",
              Revision = 4,
              Comment = "ccc comment 4",
              ProgramContent = "ccc program content 4",
            }
          ),
        },
        null,
        addAsCopiedToSystem: true
      );

      var schId = "SchId" + _fixture.Create<string>();
      var job1 = RandJob()
        .AdjustPath(
          1,
          1,
          d =>
            d with
            {
              Stops = ImmutableList.Create(
                new MachiningStop()
                {
                  Program = "aaa",
                  ProgramRevision = -1,
                  StationGroup = "",
                  Stations = [],
                  ExpectedCycleTime = TimeSpan.Zero,
                }
              ),
            }
        )
        .AdjustPath(
          1,
          2,
          d =>
            d with
            {
              Stops = ImmutableList.Create(
                new MachiningStop()
                {
                  Program = "aaa",
                  ProgramRevision = -2,
                  StationGroup = "",
                  Stations = [],
                  ExpectedCycleTime = TimeSpan.Zero,
                }
              ),
            }
        )
        .AdjustPath(
          2,
          1,
          d =>
            d with
            {
              Stops = ImmutableList.Create(
                new MachiningStop()
                {
                  Program = "bbb",
                  ProgramRevision = -1,
                  StationGroup = "",
                  Stations = [],
                  ExpectedCycleTime = TimeSpan.Zero,
                }
              ),
            }
        )
        .AdjustPath(
          2,
          2,
          d =>
            d with
            {
              Stops = ImmutableList.Create(
                new MachiningStop()
                {
                  Program = "bbb",
                  ProgramRevision = -2,
                  StationGroup = "",
                  Stations = [],
                  ExpectedCycleTime = TimeSpan.Zero,
                }
              ),
            }
        )
        .AdjustPath(
          2,
          3,
          d =>
            d with
            {
              Stops = ImmutableList.Create(
                new MachiningStop()
                {
                  Program = "ccc",
                  ProgramRevision = -2,
                  StationGroup = "",
                  Stations = [],
                  ExpectedCycleTime = TimeSpan.Zero,
                }
              ),
            }
        );

      var initialWorks = _fixture.Create<List<Workorder>>();
      initialWorks[0] = initialWorks[0] with
      {
        Programs =
        [
          new ProgramForJobStep()
          {
            ProcessNumber = 1,
            ProgramName = "aaa",
            Revision = -1,
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 0,
            ProgramName = "aaa",
            Revision = -2,
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 1,
            ProgramName = "bbb",
            Revision = -1,
          },
        ],
      };
      initialWorks[1] = initialWorks[1] with
      {
        Programs =
        [
          new ProgramForJobStep()
          {
            ProcessNumber = 1,
            StopIndex = 0,
            ProgramName = "bbb",
            Revision = -2,
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 1,
            ProgramName = "ccc",
            Revision = -1,
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 2,
            ProgramName = "ccc",
            Revision = -2,
          },
        ],
      };

      _jobDB.AddJobs(
        new NewJobs
        {
          ScheduleId = schId,
          Jobs = ImmutableList.Create(job1),
          CurrentUnfilledWorkorders = initialWorks.ToImmutableList(),
          Programs = ImmutableList.Create(
            new NewProgramContent()
            {
              ProgramName = "aaa",
              Revision = -1, // should be created to be revision 1
              Comment = "aaa comment 1",
              ProgramContent = "aaa program content for 1",
            },
            new NewProgramContent()
            {
              ProgramName = "aaa",
              Revision = -2, // should be created to be revision 2
              Comment = "aaa comment 2",
              ProgramContent = "aaa program content for 2",
            },
            new NewProgramContent()
            {
              ProgramName = "bbb",
              Revision = -1, // matches latest content so should be converted to 6
              Comment = "bbb other comment", // comment doesn't match but is ignored
              ProgramContent = "bbb program content 6",
            },
            new NewProgramContent()
            {
              ProgramName = "bbb",
              Revision = -2, // assigned a new val 7 since content differs
              Comment = "bbb comment 7",
              ProgramContent = "bbb program content 7",
            },
            new NewProgramContent()
            {
              ProgramName = "ccc",
              Revision = -1, // assigned to 3 (older than most recent) because comment and content match
              Comment = "ccc comment 3",
              ProgramContent = "ccc program content 3",
            },
            new NewProgramContent()
            {
              ProgramName = "ccc",
              Revision = -2, // assigned a new val since doesn't match existing, even if comment matches
              Comment = "ccc comment 3",
              ProgramContent = "ccc program content 5",
            }
          ),
        },
        null,
        true
      );

      job1 = job1.AdjustPath(
          1,
          1,
          d =>
            d with
            {
              Stops = ImmutableList.Create(
                new MachiningStop()
                {
                  Program = "aaa",
                  ProgramRevision = 1, // -1
                  StationGroup = "",
                  Stations = [],
                  ExpectedCycleTime = TimeSpan.Zero,
                }
              ),
            }
        )
        .AdjustPath(
          1,
          2,
          d =>
            d with
            {
              Stops = ImmutableList.Create(
                new MachiningStop()
                {
                  Program = "aaa",
                  ProgramRevision = 2, // -2
                  StationGroup = "",
                  Stations = [],
                  ExpectedCycleTime = TimeSpan.Zero,
                }
              ),
            }
        )
        .AdjustPath(
          2,
          1,
          d =>
            d with
            {
              Stops = ImmutableList.Create(
                new MachiningStop()
                {
                  Program = "bbb",
                  ProgramRevision = 6, // -1
                  StationGroup = "",
                  Stations = [],
                  ExpectedCycleTime = TimeSpan.Zero,
                }
              ),
            }
        )
        .AdjustPath(
          2,
          2,
          d =>
            d with
            {
              Stops = ImmutableList.Create(
                new MachiningStop()
                {
                  Program = "bbb",
                  ProgramRevision = 7, // -2
                  StationGroup = "",
                  Stations = [],
                  ExpectedCycleTime = TimeSpan.Zero,
                }
              ),
            }
        )
        .AdjustPath(
          2,
          3,
          d =>
            d with
            {
              Stops = ImmutableList.Create(
                new MachiningStop()
                {
                  Program = "ccc",
                  ProgramRevision = 5, // -2
                  StationGroup = "",
                  Stations = [],
                  ExpectedCycleTime = TimeSpan.Zero,
                }
              ),
            }
        );

      initialWorks[0] = initialWorks[0] with
      {
        Programs =
        [
          new ProgramForJobStep()
          {
            ProcessNumber = 1,
            ProgramName = "aaa",
            Revision = 1,
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 0,
            ProgramName = "aaa",
            Revision = 2,
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 1,
            ProgramName = "bbb",
            Revision = 6,
          },
        ],
      };
      initialWorks[1] = initialWorks[1] with
      {
        Programs =
        [
          new ProgramForJobStep()
          {
            ProcessNumber = 1,
            StopIndex = 0,
            ProgramName = "bbb",
            Revision = 7,
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 1,
            ProgramName = "ccc",
            Revision = 3,
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 2,
            ProgramName = "ccc",
            Revision = 5,
          },
        ],
      };

      _jobDB
        .LoadJob(job1.UniqueStr)
        .ShouldBeEquivalentTo(
          job1.CloneToDerived<HistoricJob, Job>() with
          {
            ScheduleId = schId,
            CopiedToSystem = true,
            Decrements = ImmutableList<DecrementQuantity>.Empty,
          }
        );

      _jobDB
        .LoadProgram("aaa", 1)
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 1,
            Comment = "aaa comment 1",
          }
        );
      _jobDB.LoadProgramContent("aaa", 1).ShouldBe("aaa program content for 1");
      _jobDB
        .LoadProgram("aaa", 2)
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 2,
            Comment = "aaa comment 2",
          }
        );
      _jobDB.LoadProgramContent("aaa", 2).ShouldBe("aaa program content for 2");
      _jobDB.LoadMostRecentProgram("aaa").Revision.ShouldBe(2);

      _jobDB
        .LoadProgram("bbb", 6)
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "bbb",
            Revision = 6,
            Comment = "bbb comment 6",
          }
        );
      _jobDB.LoadProgramContent("bbb", 6).ShouldBe("bbb program content 6");
      _jobDB
        .LoadProgram("bbb", 7)
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "bbb",
            Revision = 7,
            Comment = "bbb comment 7",
          }
        );
      _jobDB.LoadProgramContent("bbb", 7).ShouldBe("bbb program content 7");
      _jobDB.LoadMostRecentProgram("bbb").Revision.ShouldBe(7);

      _jobDB
        .LoadProgram("ccc", 3)
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "ccc",
            Revision = 3,
            Comment = "ccc comment 3",
          }
        );
      _jobDB.LoadProgramContent("ccc", 3).ShouldBe("ccc program content 3");
      _jobDB
        .LoadProgram("ccc", 4)
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "ccc",
            Revision = 4,
            Comment = "ccc comment 4",
          }
        );
      _jobDB.LoadProgramContent("ccc", 4).ShouldBe("ccc program content 4");
      _jobDB
        .LoadProgram("ccc", 5)
        .ShouldBeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "ccc",
            Revision = 5,
            Comment = "ccc comment 3",
          }
        );
      _jobDB.LoadProgramContent("ccc", 5).ShouldBe("ccc program content 5");
      _jobDB.LoadMostRecentProgram("ccc").Revision.ShouldBe(5);
    }

    [Test]
    public void UpdatesWorkorders()
    {
      var w1 = _fixture.Create<Workorder>();
      var w2 = _fixture.Build<Workorder>().With(w => w.WorkorderId, w1.WorkorderId).Create();
      var w3 = _fixture.Create<Workorder>();

      using var _jobDB = _repoCfg.OpenConnection();

      _jobDB.GetActiveWorkorders().ShouldBeNull();

      _jobDB.AddJobs(
        new NewJobs()
        {
          ScheduleId = "aaa1",
          Jobs = [],
          CurrentUnfilledWorkorders = [w1, w2, w3],
        },
        null,
        true
      );

      _jobDB
        .GetActiveWorkorders()
        .Select(w => (w.WorkorderId, w.Part))
        .ShouldBe([(w1.WorkorderId, w1.Part), (w2.WorkorderId, w2.Part), (w3.WorkorderId, w3.Part)]);

      // now update w1 with new data, keep w2 unchanged, and remove w3, and add a new w4
      var w1New = _fixture
        .Build<Workorder>()
        .With(w => w.WorkorderId, w1.WorkorderId)
        .With(w => w.Part, w1.Part)
        .Create();
      w1New = w1New with { Programs = w1New.Programs.OrderBy(w => w.ProcessNumber).ToImmutableList() };
      var w4 = _fixture.Create<Workorder>();

      _jobDB.AddJobs(
        new NewJobs()
        {
          ScheduleId = "aaa1",
          Jobs = [],
          CurrentUnfilledWorkorders = [w1New, w2, w4],
        },
        null,
        true
      );

      _jobDB
        .GetActiveWorkorders()
        .Select(w => (w.WorkorderId, w.Part, w.Priority, w.DueDate, w.PlannedQuantity))
        .ShouldBe([
          (w1New.WorkorderId, w1New.Part, w1New.Priority, w1New.DueDate, w1New.Quantity),
          (w2.WorkorderId, w2.Part, w2.Priority, w2.DueDate, w2.Quantity),
          (w4.WorkorderId, w4.Part, w4.Priority, w4.DueDate, w4.Quantity),
        ]);

      // now update w2 with new data, delete w4, and add a new w5
      var w2New = _fixture
        .Build<Workorder>()
        .With(w => w.WorkorderId, w2.WorkorderId)
        .With(w => w.Part, w2.Part)
        .Create();
      w2New = w2New with { Programs = w2New.Programs.OrderBy(w => w.ProcessNumber).ToImmutableList() };
      var w5 = _fixture.Create<Workorder>();

      _jobDB.UpdateCachedWorkorders([w1New, w2New, w5]);

      _jobDB
        .GetActiveWorkorders()
        .Select(w => (w.WorkorderId, w.Part, w.Priority, w.DueDate, w.PlannedQuantity))
        .ShouldBe(
          [
            (w1New.WorkorderId, w1New.Part, w1New.Priority, w1New.DueDate, w1New.Quantity),
            (w2New.WorkorderId, w2New.Part, w2New.Priority, w2New.DueDate, w2New.Quantity),
            (w5.WorkorderId, w5.Part, w5.Priority, w5.DueDate, w5.Quantity),
          ],
          ignoreOrder: true
        );

      // can include w4 even though it is archived
      _jobDB
        .GetActiveWorkorders(additionalWorkorders: new HashSet<string>() { w4.WorkorderId })
        .Select(w => (w.WorkorderId, w.Part, w.Priority, w.DueDate, w.PlannedQuantity))
        .ShouldBe(
          [
            (w1New.WorkorderId, w1New.Part, w1New.Priority, w1New.DueDate, w1New.Quantity),
            (w2New.WorkorderId, w2New.Part, w2New.Priority, w2New.DueDate, w2New.Quantity),
            (w4.WorkorderId, w4.Part, w4.Priority, w4.DueDate, w4.Quantity),
            (w5.WorkorderId, w5.Part, w5.Priority, w5.DueDate, w5.Quantity),
          ],
          ignoreOrder: true
        );

      _jobDB.UpdateCachedWorkorders([]);

      _jobDB.GetActiveWorkorders().ShouldBeEmpty();

      _jobDB
        .GetActiveWorkorders(additionalWorkorders: new HashSet<string>() { w1.WorkorderId, w2.WorkorderId })
        .Select(w => (w.WorkorderId, w.Part, w.Priority, w.DueDate, w.PlannedQuantity))
        .ShouldBe(
          [
            (w1New.WorkorderId, w1New.Part, w1New.Priority, w1New.DueDate, w1New.Quantity),
            (w2New.WorkorderId, w2New.Part, w2New.Priority, w2New.DueDate, w2New.Quantity),
          ],
          ignoreOrder: true
        );

      _jobDB
        .WorkordersById(w1New.WorkorderId)
        .OrderBy(w => w.WorkorderId)
        .ThenBy(w => w.Part)
        .ToList()
        .ShouldBeEquivalentTo(
          ImmutableList.Create(w1New, w2New).OrderBy(w => w.WorkorderId).ThenBy(w => w.Part).ToList()
        );
    }

    [Test]
    public void ErrorOnDuplicateSchId()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      var job1 = RandJob();
      var job2 = RandJob();

      _jobDB.AddJobs(new NewJobs() { Jobs = ImmutableList.Create(job1), ScheduleId = "sch1" }, null, true);

      _jobDB
        .LoadJob(job1.UniqueStr)
        .ShouldBeEquivalentTo(
          job1.CloneToDerived<HistoricJob, Job>() with
          {
            ScheduleId = "sch1",
            CopiedToSystem = true,
            Decrements = ImmutableList<DecrementQuantity>.Empty,
          }
        );

      Should
        .Throw<BadRequestException>(() =>
          _jobDB.AddJobs(new NewJobs() { Jobs = ImmutableList.Create(job2), ScheduleId = "sch1" }, null, true)
        )
        .Message.ShouldBe("Schedule ID sch1 already exists!");
    }

    [Test]
    public void SchedulesRebooking()
    {
      using var db = _repoCfg.OpenConnection();

      var now = DateTime.UtcNow;
      var booking1 = _fixture.Create<string>();
      var booking2 = _fixture.Create<string>();
      var job = RandJob() with { BookingIds = [booking1] };

      db.CreateRebooking(bookingId: booking1, partName: job.PartName, qty: 4, priority: 12, timeUTC: now);

      var rebooking1 = new Rebooking()
      {
        BookingId = booking1,
        PartName = job.PartName,
        Quantity = 4,
        TimeUTC = now,
        Priority = 12,
      };

      db.CreateRebooking(
        bookingId: booking2,
        partName: job.PartName,
        qty: 5,
        priority: 13,
        workorder: "work22",
        timeUTC: now.AddMinutes(1)
      );

      var rebooking2 = new Rebooking()
      {
        BookingId = booking2,
        PartName = job.PartName,
        Quantity = 5,
        TimeUTC = now.AddMinutes(1),
        Priority = 13,
        Workorder = "work22",
      };

      db.LoadUnscheduledRebookings().ShouldBeEquivalentTo(ImmutableList.Create(rebooking1, rebooking2));
      db.LoadMostRecentSchedule()
        .ShouldBeEquivalentTo(
          new MostRecentSchedule()
          {
            LatestScheduleId = "",
            Jobs = [],
            ExtraParts = ImmutableDictionary<string, int>.Empty,
            UnscheduledRebookings = [rebooking1, rebooking2],
          }
        );

      var schId = "sch" + _fixture.Create<string>();

      db.AddJobs(new NewJobs() { ScheduleId = schId, Jobs = [job] }, null, true);

      var jobhistory = job.CloneToDerived<HistoricJob, Job>() with
      {
        ScheduleId = schId,
        CopiedToSystem = true,
        Decrements = ImmutableList<DecrementQuantity>.Empty,
      };

      db.LoadUnscheduledRebookings().ShouldBeEquivalentTo(ImmutableList.Create(rebooking2));
      db.LoadMostRecentSchedule()
        .ShouldBeEquivalentTo(
          new MostRecentSchedule()
          {
            LatestScheduleId = schId,
            Jobs = [jobhistory],
            ExtraParts = ImmutableDictionary<string, int>.Empty,
            UnscheduledRebookings = [rebooking2],
          }
        );
    }

    private static ImmutableList<SimulatedStationUtilization> RandSimStationUse(string schId, DateTime start)
    {
      var rnd = new Random();
      var ret = ImmutableList.CreateBuilder<SimulatedStationUtilization>();
      for (int i = 0; i < 3; i++)
      {
        ImmutableList<SimulatedStationPart> parts;
        if (i == 1)
        {
          parts = null;
        }
        else
        {
          parts = ImmutableList.CreateRange(
            new[]
            {
              new SimulatedStationPart()
              {
                JobUnique = "uniq" + i,
                Process = rnd.Next(1, 10),
                Path = rnd.Next(1, 10),
              },
              new SimulatedStationPart()
              {
                JobUnique = "uniq" + i,
                Process = rnd.Next(15, 20),
                Path = 11 + rnd.Next(1, 10),
              },
            }
          );
        }

        ret.Add(
          new SimulatedStationUtilization()
          {
            ScheduleId = schId,
            StationGroup = "group" + rnd.Next(0, 100000).ToString(),
            StationNum = rnd.Next(0, 10000),
            StartUTC = start.AddHours(i * 8).AddMinutes(-rnd.Next(200, 300)),
            EndUTC = start.AddHours(i * 8).AddMinutes(rnd.Next(0, 100)),
            PlanDown = rnd.Next(0, 100) < 20 ? (rnd.Next(1, 100) < 50 ? null : false) : true,
            Pallet = rnd.Next(0, 100) < 10 ? null : rnd.Next(1, 100),
            Parts = parts,
          }
        );
      }
      return ret.ToImmutable();
    }

    private Job RandJob(DateTime? start = null)
    {
      var job = _fixture.Create<Job>();
      var s = start ?? job.RouteStartUTC;
      return job with
      {
        RouteStartUTC = s,
        RouteEndUTC = s.AddHours(1),
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
  }
}
