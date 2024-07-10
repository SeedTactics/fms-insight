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
using FluentAssertions;
using Xunit;

namespace MachineWatchTest
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

    [Fact]
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
        Decrements = ImmutableList<DecrementQuantity>.Empty
      };

      _jobDB
        .LoadJobHistory(job1.RouteStartUTC.AddHours(-1), job1.RouteStartUTC.AddHours(10))
        .Should()
        .BeEquivalentTo(
          new HistoricData()
          {
            Jobs = ImmutableDictionary.Create<string, HistoricJob>().Add(job1.UniqueStr, job1history),
            StationUse = job1StatUse
          }
        );

      _jobDB
        .LoadJobHistory(
          job1.RouteStartUTC.AddHours(-1),
          job1.RouteStartUTC.AddHours(10),
          new HashSet<string>(new[] { schId })
        )
        .Should()
        .BeEquivalentTo(
          new HistoricData()
          {
            Jobs = ImmutableDictionary<string, HistoricJob>.Empty,
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty
          }
        );

      _jobDB
        .Invoking(j =>
          j.LoadJobHistory(
            job1.RouteStartUTC.AddHours(-1),
            job1.RouteStartUTC.AddHours(10),
            new HashSet<string>(new[] { schId, "asdfouh" })
          )
        )
        .Should()
        .Throw<ConflictRequestException>("Schedule ID asdfouh does not exist");

      _jobDB
        .LoadJobHistory(job1.RouteStartUTC.AddHours(-20), job1.RouteStartUTC.AddHours(-10))
        .Should()
        .BeEquivalentTo(
          new HistoricData()
          {
            Jobs = ImmutableDictionary<string, HistoricJob>.Empty,
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty
          }
        );

      _jobDB.LoadJob(job1.UniqueStr).Should().BeEquivalentTo(job1history);
      _jobDB.DoesJobExist(job1.UniqueStr).Should().BeTrue();
      _jobDB.DoesJobExist("afouiaehwiouhwef").Should().BeFalse();

      var belowJob1 = job1.UniqueStr.Substring(0, job1.UniqueStr.Length / 2);
      var aboveJob1 =
        belowJob1.Substring(0, belowJob1.Length - 1)
        + ((char)(belowJob1[belowJob1.Length - 1] + 1)).ToString();
      _jobDB.LoadJobsBetween(belowJob1, aboveJob1).Should().BeEquivalentTo(new[] { job1history });
      _jobDB.LoadJobsBetween(belowJob1, job1.UniqueStr).Should().BeEquivalentTo(new[] { job1history });
      _jobDB.LoadJobsBetween(belowJob1, belowJob1 + "!").Should().BeEmpty();
      _jobDB.LoadJobsBetween(job1.UniqueStr, aboveJob1).Should().BeEquivalentTo(new[] { job1history });

      _jobDB
        .LoadMostRecentSchedule()
        .Should()
        .BeEquivalentTo(
          new PlannedSchedule()
          {
            LatestScheduleId = schId,
            Jobs = ImmutableList.Create(job1history),
            ExtraParts = job1ExtraParts.ToImmutableDictionary(),
          }
        );

      _jobDB
        .StationGroupsOnMostRecentSchedule()
        .ToList()
        .Should()
        .BeEquivalentTo(
          job1history
            .Processes.SelectMany(p => p.Paths)
            .SelectMany(p => p.Stops)
            .Select(s => s.StationGroup)
            .Distinct()
            .ToList()
        );

      // Add second job
      var schId2 = "ZZ" + schId;
      var job2 = RandJob(job1.RouteStartUTC.AddHours(4)) with
      {
        ManuallyCreated = true,
        PartName = job1.PartName
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

      _jobDB
        .Invoking(d => d.AddJobs(newJobs2, "badsch", true))
        .Should()
        .Throw<Exception>()
        .WithMessage("Mismatch in previous schedule: expected 'badsch' but got '" + schId + "'");

      _jobDB.LoadJob(job2.UniqueStr).Should().BeNull();
      _jobDB.DoesJobExist(job2.UniqueStr).Should().BeFalse();
      _jobDB.LoadMostRecentSchedule().LatestScheduleId.Should().Be(schId);

      _jobDB.AddJobs(newJobs2, expectedPreviousScheduleId: schId, addAsCopiedToSystem: true);

      job2SimUse = job2SimUse
        .Select(s => s.PlanDown == false ? s with { PlanDown = null } : s)
        .ToImmutableList();

      _jobDB.DoesJobExist(job2.UniqueStr).Should().BeTrue();

      var job2history = job2.CloneToDerived<HistoricJob, Job>() with
      {
        ScheduleId = schId2,
        CopiedToSystem = true,
        Decrements = ImmutableList<DecrementQuantity>.Empty
      };

      _jobDB
        .LoadJobHistory(job1.RouteStartUTC.AddHours(-1), job1.RouteStartUTC.AddHours(10))
        .Should()
        .BeEquivalentTo(
          new HistoricData()
          {
            Jobs = ImmutableDictionary
              .Create<string, HistoricJob>()
              .Add(job1.UniqueStr, job1history)
              .Add(job2.UniqueStr, job2history),
            StationUse = job1StatUse.AddRange(job2SimUse)
          }
        );

      _jobDB
        .LoadJobHistory(
          job1.RouteStartUTC.AddHours(-1),
          job1.RouteStartUTC.AddHours(10),
          new HashSet<string>(new[] { schId })
        )
        .Should()
        .BeEquivalentTo(
          new HistoricData()
          {
            Jobs = ImmutableDictionary.Create<string, HistoricJob>().Add(job2.UniqueStr, job2history),
            StationUse = job2SimUse
          }
        );

      _jobDB
        .LoadJobHistory(job1.RouteStartUTC.AddHours(3), job1.RouteStartUTC.AddHours(10))
        .Should()
        .BeEquivalentTo(
          new HistoricData()
          {
            Jobs = ImmutableDictionary.Create<string, HistoricJob>().Add(job2.UniqueStr, job2history),
            StationUse = job2SimUse
          }
        );

      _jobDB
        .LoadRecentJobHistory(job1.RouteStartUTC.AddHours(-1))
        .Should()
        .BeEquivalentTo(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary
              .Create<string, HistoricJob>()
              .Add(job1.UniqueStr, job1history)
              .Add(job2.UniqueStr, job2history),
            StationUse = job1StatUse.AddRange(job2SimUse)
          }
        );

      _jobDB
        .LoadRecentJobHistory(job1.RouteStartUTC.AddHours(-1), new[] { schId })
        .Should()
        .BeEquivalentTo(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary.Create<string, HistoricJob>().Add(job2.UniqueStr, job2history),
            StationUse = job2SimUse
          }
        );

      _jobDB
        .LoadRecentJobHistory(job1.RouteEndUTC.AddHours(1))
        .Should()
        .BeEquivalentTo(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary.Create<string, HistoricJob>().Add(job2.UniqueStr, job2history),
            StationUse = job2SimUse
          }
        );

      _jobDB
        .LoadRecentJobHistory(job1.RouteEndUTC.AddHours(1), new[] { schId2 })
        .Should()
        .BeEquivalentTo(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary<string, HistoricJob>.Empty,
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty
          }
        );

      _jobDB
        .LoadRecentJobHistory(job2.RouteEndUTC.AddHours(1))
        .Should()
        .BeEquivalentTo(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary<string, HistoricJob>.Empty,
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty
          }
        );

      _jobDB
        .LoadMostRecentSchedule()
        .Should()
        .BeEquivalentTo(
          // job2 is manually created and should be ignored
          new PlannedSchedule()
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
          .Should()
          .BeEquivalentTo(new[] { job1history, job2history });
        _jobDB.LoadJobsBetween(belowJob1, belowJob2).Should().BeEquivalentTo(new[] { job1history });
        _jobDB.LoadJobsBetween(aboveJob1, aboveJob2).Should().BeEquivalentTo(new[] { job2history });
        _jobDB.LoadJobsBetween(aboveJob1, belowJob2).Should().BeEmpty();
      }
      else
      {
        _jobDB
          .LoadJobsBetween(belowJob2, aboveJob1)
          .Should()
          .BeEquivalentTo(new[] { job1history, job2history });
        _jobDB.LoadJobsBetween(belowJob2, belowJob1).Should().BeEquivalentTo(new[] { job2history });
        _jobDB.LoadJobsBetween(aboveJob2, aboveJob1).Should().BeEquivalentTo(new[] { job1history });
        _jobDB.LoadJobsBetween(aboveJob2, belowJob1).Should().BeEmpty();
      }
    }

    [Fact]
    public void SimDays()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      var now = DateTime.UtcNow;
      var schId1 = "schId" + _fixture.Create<string>();
      var job1 = RandJob() with { RouteStartUTC = now, RouteEndUTC = now.AddHours(1) };
      var simDays1 = _fixture.Create<ImmutableList<SimulatedDayUsage>>();
      var warning = _fixture.Create<string>();
      var job1history = job1.CloneToDerived<HistoricJob, Job>() with
      {
        ScheduleId = schId1,
        CopiedToSystem = true,
        Decrements = ImmutableList<DecrementQuantity>.Empty
      };

      simDays1.Count.Should().BeGreaterThan(0);

      _jobDB.AddJobs(
        new NewJobs()
        {
          ScheduleId = schId1,
          Jobs = ImmutableList.Create(job1),
          SimDayUsage = simDays1,
          SimDayUsageWarning = warning
        },
        null,
        addAsCopiedToSystem: true
      );

      _jobDB
        .LoadJobHistory(now, now)
        .Should()
        .BeEquivalentTo(
          new HistoricData()
          {
            Jobs = ImmutableDictionary.Create<string, HistoricJob>().Add(job1.UniqueStr, job1history),
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
          }
        );

      _jobDB
        .LoadRecentJobHistory(now)
        .Should()
        .BeEquivalentTo(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary.Create<string, HistoricJob>().Add(job1.UniqueStr, job1history),
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
            MostRecentSimulationId = schId1,
            MostRecentSimDayUsage = simDays1,
            MostRecentSimDayUsageWarning = warning
          }
        );

      _jobDB
        .LoadRecentJobHistory(now, new[] { schId1 })
        .Should()
        .BeEquivalentTo(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary<string, HistoricJob>.Empty,
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
          }
        );

      var schId2 = schId1 + "222";
      var job2 = RandJob() with { RouteStartUTC = now, RouteEndUTC = now.AddHours(2) };
      var simDays2 = _fixture.Create<ImmutableList<SimulatedDayUsage>>();
      var job2history = job2.CloneToDerived<HistoricJob, Job>() with
      {
        ScheduleId = schId2,
        CopiedToSystem = true,
        Decrements = ImmutableList<DecrementQuantity>.Empty
      };

      _jobDB.AddJobs(
        new NewJobs()
        {
          ScheduleId = schId2,
          Jobs = ImmutableList.Create(job2),
          SimDayUsage = simDays2,
          SimDayUsageWarning = null
        },
        schId1,
        addAsCopiedToSystem: true
      );

      _jobDB
        .LoadRecentJobHistory(now)
        .Should()
        .BeEquivalentTo(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary
              .Create<string, HistoricJob>()
              .Add(job1.UniqueStr, job1history)
              .Add(job2.UniqueStr, job2history),
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
            MostRecentSimulationId = schId2,
            MostRecentSimDayUsage = simDays2,
            MostRecentSimDayUsageWarning = null
          }
        );

      _jobDB
        .LoadRecentJobHistory(now, new[] { schId2 })
        .Should()
        .BeEquivalentTo(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary.Create<string, HistoricJob>().Add(job1.UniqueStr, job1history),
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
          }
        );
      _jobDB
        .LoadRecentJobHistory(now, new[] { schId1 })
        .Should()
        .BeEquivalentTo(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary.Create<string, HistoricJob>().Add(job2.UniqueStr, job2history),
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
            MostRecentSimulationId = schId2,
            MostRecentSimDayUsage = simDays2,
            MostRecentSimDayUsageWarning = null
          }
        );
    }

    [Fact]
    public void SimDaysWithoutJobs()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      var now = DateTime.UtcNow;
      var schId1 = "schId" + _fixture.Create<string>();
      var simDays1 = _fixture.Create<ImmutableList<SimulatedDayUsage>>();
      var warning = _fixture.Create<string>();

      _jobDB.AddJobs(
        new NewJobs()
        {
          ScheduleId = schId1,
          Jobs = ImmutableList<Job>.Empty,
          SimDayUsage = simDays1,
          SimDayUsageWarning = warning
        },
        null,
        addAsCopiedToSystem: true
      );

      _jobDB
        .LoadJobHistory(now, now)
        .Should()
        .BeEquivalentTo(
          new HistoricData()
          {
            Jobs = ImmutableDictionary<string, HistoricJob>.Empty,
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
          }
        );

      _jobDB
        .LoadRecentJobHistory(now)
        .Should()
        .BeEquivalentTo(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary<string, HistoricJob>.Empty,
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
            MostRecentSimulationId = schId1,
            MostRecentSimDayUsage = simDays1,
            MostRecentSimDayUsageWarning = warning
          }
        );

      _jobDB
        .LoadRecentJobHistory(now, new[] { schId1 })
        .Should()
        .BeEquivalentTo(
          new RecentHistoricData()
          {
            Jobs = ImmutableDictionary<string, HistoricJob>.Empty,
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty,
          }
        );
    }

    [Fact]
    public void SetsComment()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      var schId = "SchId" + _fixture.Create<string>();
      var job1 = RandJob() with { ManuallyCreated = false };

      _jobDB.AddJobs(
        new NewJobs() { ScheduleId = schId, Jobs = ImmutableList.Create(job1), },
        null,
        addAsCopiedToSystem: true
      );

      var job1history = job1.CloneToDerived<HistoricJob, Job>() with
      {
        ScheduleId = schId,
        CopiedToSystem = true,
        Decrements = ImmutableList<DecrementQuantity>.Empty
      };

      _jobDB.LoadJob(job1.UniqueStr).Should().BeEquivalentTo(job1history);

      var newComment = _fixture.Create<string>();
      _jobDB.SetJobComment(job1.UniqueStr, newComment);

      _jobDB.LoadJob(job1.UniqueStr).Should().BeEquivalentTo(job1history with { Comment = newComment });
    }

    [Fact]
    public void UpdatesHold()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      var schId = "SchId" + _fixture.Create<string>();
      var job1 = RandJob() with { ManuallyCreated = false };

      _jobDB.AddJobs(
        new NewJobs() { ScheduleId = schId, Jobs = ImmutableList.Create(job1), },
        null,
        addAsCopiedToSystem: true
      );

      var job1history = job1.CloneToDerived<HistoricJob, Job>() with
      {
        ScheduleId = schId,
        CopiedToSystem = true,
        Decrements = ImmutableList<DecrementQuantity>.Empty
      };

      _jobDB.LoadJob(job1.UniqueStr).Should().BeEquivalentTo(job1history);

      var newHold = _fixture.Create<HoldPattern>();

      _jobDB.UpdateJobHold(job1.UniqueStr, newHold);

      _jobDB.LoadJob(job1.UniqueStr).Should().BeEquivalentTo(job1history with { HoldJob = newHold });

      var newMachHold = _fixture.Create<HoldPattern>();
      _jobDB.UpdateJobMachiningHold(job1.UniqueStr, 1, 1, newMachHold);

      _jobDB
        .LoadJob(job1.UniqueStr)
        .Should()
        .BeEquivalentTo(
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
                    .ToImmutableList()
                }
              }
            )
              .Concat(job1.Processes.Skip(1))
              .ToImmutableList()
          }
        );

      var newLoadHold = _fixture.Create<HoldPattern>();
      _jobDB.UpdateJobLoadUnloadHold(job1.UniqueStr, 1, 2, newLoadHold);

      _jobDB
        .LoadJob(job1.UniqueStr)
        .Should()
        .BeEquivalentTo(
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
                        HoldMachining = newMachHold
                      },
                      job1.Processes[0].Paths[1] with
                      {
                        HoldLoadUnload = newLoadHold
                      }
                    }
                  )
                    .Concat(job1.Processes[0].Paths.Skip(2))
                    .ToImmutableList()
                }
              }
            )
              .Concat(job1.Processes.Skip(1))
              .ToImmutableList()
          }
        );
    }

    [Fact]
    public void MarksAsCopied()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      var schId = "SchId" + _fixture.Create<string>();
      var job1 = RandJob() with { ManuallyCreated = false };

      _jobDB.AddJobs(
        new NewJobs() { ScheduleId = schId, Jobs = ImmutableList.Create(job1), },
        null,
        addAsCopiedToSystem: false
      );

      var job1history = job1.CloneToDerived<HistoricJob, Job>() with
      {
        ScheduleId = schId,
        CopiedToSystem = false,
        Decrements = ImmutableList<DecrementQuantity>.Empty
      };

      _jobDB
        .LoadJobsNotCopiedToSystem(job1.RouteStartUTC.AddHours(-10), job1.RouteStartUTC.AddHours(-5))
        .Should()
        .BeEmpty();

      _jobDB
        .LoadJobsNotCopiedToSystem(job1.RouteStartUTC.AddHours(-1), job1.RouteStartUTC.AddHours(2))
        .Should()
        .BeEquivalentTo(new[] { job1history });

      _jobDB.MarkJobCopiedToSystem(job1.UniqueStr);

      _jobDB
        .LoadJobsNotCopiedToSystem(job1.RouteStartUTC.AddHours(-1), job1.RouteStartUTC.AddHours(2))
        .Should()
        .BeEmpty();

      _jobDB.LoadJob(job1.UniqueStr).Should().BeEquivalentTo(job1history with { CopiedToSystem = true });
    }

    [Fact]
    public void ArchivesJobs()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      var schId = "SchId" + _fixture.Create<string>();
      var job1 = RandJob() with { Archived = false };

      _jobDB.AddJobs(
        new NewJobs() { ScheduleId = schId, Jobs = ImmutableList.Create(job1), },
        null,
        addAsCopiedToSystem: true
      );

      var job1history = job1.CloneToDerived<HistoricJob, Job>() with
      {
        ScheduleId = schId,
        CopiedToSystem = true,
        Decrements = ImmutableList<DecrementQuantity>.Empty
      };

      _jobDB.LoadJob(job1.UniqueStr).Should().BeEquivalentTo(job1history);
      _jobDB.LoadUnarchivedJobs().Should().BeEquivalentTo(new[] { job1history });

      _jobDB.ArchiveJob(job1.UniqueStr);

      _jobDB.LoadJob(job1.UniqueStr).Should().BeEquivalentTo(job1history with { Archived = true });
      _jobDB.LoadUnarchivedJobs().Should().BeEmpty();
    }

    [Fact]
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
        .Should()
        .BeEquivalentTo(
          new[]
          {
            job1.CloneToDerived<HistoricJob, Job>() with
            {
              CopiedToSystem = false,
              ScheduleId = "schId",
              Decrements = ImmutableList<DecrementQuantity>.Empty
            }
          }
        );

      var time1 = now.AddHours(-2);
      _jobDB.AddNewDecrement(
        new[]
        {
          new NewDecrementQuantity
          {
            JobUnique = job1.UniqueStr,
            Part = job1.PartName,
            Quantity = 53
          },
          new NewDecrementQuantity()
          {
            JobUnique = job2.UniqueStr,
            Part = job2.PartName,
            Quantity = 821
          }
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
          Quantity = 53
        },
        new JobAndDecrementQuantity()
        {
          DecrementId = 0,
          JobUnique = job2.UniqueStr,
          TimeUTC = time1,
          Part = job2.PartName,
          Quantity = 821
        }
      };

      var expected1Job1 = ImmutableList.Create(
        new DecrementQuantity()
        {
          DecrementId = 0,
          TimeUTC = time1,
          Quantity = 53
        }
      );
      var expected1Job2 = ImmutableList.Create(
        new DecrementQuantity()
        {
          DecrementId = 0,
          TimeUTC = time1,
          Quantity = 821
        }
      );

      _jobDB.LoadDecrementQuantitiesAfter(-1).Should().BeEquivalentTo(expected1JobAndDecr);

      _jobDB
        .LoadJobsNotCopiedToSystem(now, now, includeDecremented: true)
        .Should()
        .BeEquivalentTo(
          new[]
          {
            job1.CloneToDerived<HistoricJob, Job>() with
            {
              CopiedToSystem = false,
              ScheduleId = "schId",
              Decrements = expected1Job1
            }
          }
        );

      _jobDB.LoadJobsNotCopiedToSystem(now, now, includeDecremented: false).Should().BeEmpty();

      _jobDB
        .LoadJob(job1.UniqueStr)
        .Should()
        .BeEquivalentTo(
          job1.CloneToDerived<HistoricJob, Job>() with
          {
            CopiedToSystem = false,
            ScheduleId = "schId",
            Decrements = expected1Job1
          }
        );

      _jobDB
        .LoadJob(job2.UniqueStr)
        .Should()
        .BeEquivalentTo(
          job2.CloneToDerived<HistoricJob, Job>() with
          {
            CopiedToSystem = true,
            ScheduleId = "schId",
            Decrements = expected1Job2
          }
        );

      _jobDB
        .LoadJobHistory(now, now)
        .Should()
        .BeEquivalentTo(
          new HistoricData()
          {
            Jobs = ImmutableDictionary<string, HistoricJob>
              .Empty.Add(
                job1.UniqueStr,
                job1.CloneToDerived<HistoricJob, Job>() with
                {
                  CopiedToSystem = false,
                  ScheduleId = "schId",
                  Decrements = expected1Job1
                }
              )
              .Add(
                job2.UniqueStr,
                job2.CloneToDerived<HistoricJob, Job>() with
                {
                  CopiedToSystem = true,
                  ScheduleId = "schId",
                  Decrements = expected1Job2
                }
              ),
            StationUse = ImmutableList<SimulatedStationUtilization>.Empty
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
            Quantity = 26
          },
          new NewDecrementQuantity()
          {
            JobUnique = job2.UniqueStr,
            Part = job2.PartName,
            Quantity = 44
          }
        },
        time2,
        new[]
        {
          new RemovedBooking() { JobUnique = job1.UniqueStr, BookingId = job1.BookingIds.First() },
          new RemovedBooking() { JobUnique = job2.UniqueStr, BookingId = job2.BookingIds.First() }
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
          Quantity = 26
        },
        new JobAndDecrementQuantity()
        {
          DecrementId = 1,
          JobUnique = job2.UniqueStr,
          TimeUTC = time2,
          Part = job2.PartName,
          Quantity = 44
        }
      };

      _jobDB
        .LoadDecrementQuantitiesAfter(-1)
        .Should()
        .BeEquivalentTo(expected1JobAndDecr.Concat(expected2JobAndDecr));
      _jobDB.LoadDecrementQuantitiesAfter(0).Should().BeEquivalentTo(expected2JobAndDecr);
      _jobDB.LoadDecrementQuantitiesAfter(1).Should().BeEmpty();

      _jobDB
        .LoadDecrementQuantitiesAfter(time1.AddHours(-1))
        .Should()
        .BeEquivalentTo(expected1JobAndDecr.Concat(expected2JobAndDecr));
      _jobDB.LoadDecrementQuantitiesAfter(time1.AddMinutes(30)).Should().BeEquivalentTo(expected2JobAndDecr);
      _jobDB.LoadDecrementQuantitiesAfter(time2.AddMinutes(30)).Should().BeEmpty();

      _jobDB
        .LoadDecrementsForJob(job1.UniqueStr)
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new DecrementQuantity()
            {
              DecrementId = 0,
              TimeUTC = time1,
              Quantity = 53
            },
            new DecrementQuantity()
            {
              DecrementId = 1,
              TimeUTC = time2,
              Quantity = 26
            }
          }
        );

      _jobDB
        .LoadDecrementsForJob(job2.UniqueStr)
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new DecrementQuantity()
            {
              DecrementId = 0,
              TimeUTC = time1,
              Quantity = 821
            },
            new DecrementQuantity()
            {
              DecrementId = 1,
              TimeUTC = time2,
              Quantity = 44
            }
          }
        );
    }

    [Fact]
    public void DecrementsDuringArchive()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      var job = RandJob() with { Archived = false };

      _jobDB.AddJobs(new NewJobs() { Jobs = ImmutableList.Create(job), ScheduleId = "theschId" }, null, true);

      _jobDB
        .LoadJob(job.UniqueStr)
        .Should()
        .BeEquivalentTo(
          job.CloneToDerived<HistoricJob, Job>() with
          {
            CopiedToSystem = true,
            ScheduleId = "theschId",
            Decrements = ImmutableList<DecrementQuantity>.Empty
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
            Quantity = 44
          },
        },
        now
      );

      _jobDB
        .LoadJob(job.UniqueStr)
        .Should()
        .BeEquivalentTo(
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
                Quantity = 44
              }
            )
          }
        );
    }

    [Fact]
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
                  Stations = ImmutableList<int>.Empty,
                  ExpectedCycleTime = TimeSpan.Zero
                }
              )
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
                  Stations = ImmutableList<int>.Empty,
                  ExpectedCycleTime = TimeSpan.Zero
                }
              )
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
                  Stations = ImmutableList<int>.Empty,
                  ExpectedCycleTime = TimeSpan.Zero
                }
              )
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
                  Stations = ImmutableList<int>.Empty,
                  ExpectedCycleTime = TimeSpan.Zero
                }
              )
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
            Revision = null
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 0,
            ProgramName = "aaa",
            Revision = 1
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 1,
            ProgramName = "bbb",
            Revision = null
          }
        ]
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
            Revision = 6
          }
        ]
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
              Revision = 0, // auto assign
              Comment = "aaa comment",
              ProgramContent = "aaa program content"
            },
            new NewProgramContent()
            {
              ProgramName = "bbb",
              Revision = 6, // new revision
              Comment = "bbb comment",
              ProgramContent = "bbb program content"
            }
          )
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
                  Stations = ImmutableList<int>.Empty,
                  ExpectedCycleTime = TimeSpan.Zero
                }
              )
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
                  Stations = ImmutableList<int>.Empty,
                  ExpectedCycleTime = TimeSpan.Zero
                }
              )
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
            Revision = 1
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 0,
            ProgramName = "aaa",
            Revision = 1
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 1,
            ProgramName = "bbb",
            Revision = 6
          }
        ]
      };

      _jobDB
        .LoadJob(job1.UniqueStr)
        .Should()
        .BeEquivalentTo(
          job1.CloneToDerived<HistoricJob, Job>() with
          {
            ScheduleId = schId,
            CopiedToSystem = true,
            Decrements = ImmutableList<DecrementQuantity>.Empty
          }
        );

      _jobDB
        .GetActiveWorkorders(initialWorks[0].Part)
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new ActiveWorkorder()
            {
              WorkorderId = initialWorks[0].WorkorderId,
              Part = initialWorks[0].Part,
              PlannedQuantity = initialWorks[0].Quantity,
              CompletedQuantity = 0,
              DueDate = initialWorks[0].DueDate,
              Priority = initialWorks[0].Priority,
              Serials = ImmutableDictionary<string, WorkorderSerialStatus>.Empty,
              ElapsedStationTime = ImmutableDictionary<string, TimeSpan>.Empty,
              ActiveStationTime = ImmutableDictionary<string, TimeSpan>.Empty,
              SimulatedStart = initialWorks[0].SimulatedStart,
              SimulatedFilled = initialWorks[0].SimulatedFilled
            }
          }
        );

      _jobDB.WorkordersById(initialWorks[0].WorkorderId).Should().BeEquivalentTo(new[] { initialWorks[0] });
      _jobDB
        .WorkordersById(initialWorks.Select(w => w.WorkorderId).ToHashSet())
        .Should()
        .BeEquivalentTo(
          initialWorks.GroupBy(w => w.WorkorderId).ToImmutableDictionary(g => g.Key, g => g.ToImmutableList())
        );

      _jobDB
        .LoadProgram("aaa", 1)
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 1,
            Comment = "aaa comment",
          }
        );
      _jobDB
        .LoadMostRecentProgram("aaa")
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 1,
            Comment = "aaa comment",
          }
        );
      _jobDB
        .LoadProgram("bbb", 6)
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "bbb",
            Revision = 6,
            Comment = "bbb comment",
          }
        );
      _jobDB
        .LoadMostRecentProgram("bbb")
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "bbb",
            Revision = 6,
            Comment = "bbb comment",
          }
        );
      _jobDB.LoadProgram("aaa", 2).Should().BeNull();
      _jobDB.LoadProgram("ccc", 1).Should().BeNull();

      _jobDB.LoadProgramContent("aaa", 1).Should().Be("aaa program content");
      _jobDB.LoadProgramContent("bbb", 6).Should().Be("bbb program content");
      _jobDB.LoadProgramContent("aaa", 2).Should().BeNull();
      _jobDB.LoadProgramContent("ccc", 1).Should().BeNull();
      _jobDB.LoadProgramsInCellController().Should().BeEmpty();

      // error on program content mismatch
      _jobDB
        .Invoking(j =>
          j.AddJobs(
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
                  ProgramContent = "aaa program content rev 2"
                },
                new NewProgramContent()
                {
                  ProgramName = "bbb",
                  Revision = 6, // existing revision
                  Comment = "bbb comment",
                  ProgramContent = "awofguhweoguhweg"
                }
              )
            },
            null,
            addAsCopiedToSystem: true
          )
        )
        .Should()
        .Throw<BadRequestException>()
        .WithMessage("Program bbb rev6 has already been used and the program contents do not match.");

      _jobDB
        .Invoking(j =>
          j.AddPrograms(
            new List<NewProgramContent>
            {
              new NewProgramContent()
              {
                ProgramName = "aaa",
                Revision = 0, // auto assign
                Comment = "aaa comment rev 2",
                ProgramContent = "aaa program content rev 2"
              },
              new NewProgramContent()
              {
                ProgramName = "bbb",
                Revision = 6, // existing revision
                Comment = "bbb comment",
                ProgramContent = "awofguhweoguhweg"
              },
            },
            DateTime.Parse("2019-09-14T03:52:12Z")
          )
        )
        .Should()
        .Throw<BadRequestException>()
        .WithMessage("Program bbb rev6 has already been used and the program contents do not match.");

      _jobDB.LoadProgram("aaa", 2).Should().BeNull();
      _jobDB
        .LoadMostRecentProgram("aaa")
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 1,
            Comment = "aaa comment",
          }
        );
      _jobDB
        .LoadMostRecentProgram("bbb")
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "bbb",
            Revision = 6,
            Comment = "bbb comment",
          }
        );
      _jobDB.LoadProgramContent("aaa", 1).Should().Be("aaa program content");
      _jobDB.LoadProgramContent("aaa", 2).Should().BeNull();

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
            Revision = 1
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 0,
            ProgramName = "aaa",
            Revision = 2
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 1,
            ProgramName = "bbb",
            Revision = 6
          }
        ]
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
            Revision = 0
          }
        ]
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
              ProgramContent = "ccc first program"
            }
          )
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
            Revision = 1
          }
        ]
      };

      _jobDB
        .GetActiveWorkorders()
        .Select(w => w.WorkorderId)
        .Should()
        .BeEquivalentTo(newWorkorders.Select(w => w.WorkorderId)); // initialWorks have been archived and don't appear
      _jobDB.WorkordersById(initialWorks[0].WorkorderId).Should().BeEquivalentTo(new[] { initialWorks[0] }); // but still exist when looked up directly

      _jobDB
        .LoadMostRecentProgram("ccc")
        .Should()
        .BeEquivalentTo(
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
              ProgramContent = "aaa program content rev 2"
            },
            new NewProgramContent()
            {
              ProgramName = "bbb",
              Revision = 6, // existing revision
              Comment = "bbb comment",
              ProgramContent = "bbb program content"
            },
            new NewProgramContent()
            {
              ProgramName = "ccc",
              Revision = 0, // auto assign
              Comment = "ccc new comment", // comment does not match most recent revision
              ProgramContent = "ccc first program" // content matches most recent revision
            }
          )
        },
        schId,
        addAsCopiedToSystem: true
      );

      _jobDB
        .LoadProgram("aaa", 2)
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 2, // creates new revision
            Comment = "aaa comment rev 2",
          }
        );
      _jobDB
        .LoadMostRecentProgram("aaa")
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 2,
            Comment = "aaa comment rev 2",
          }
        );
      _jobDB
        .LoadMostRecentProgram("bbb")
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "bbb",
            Revision = 6,
            Comment = "bbb comment",
          }
        );
      _jobDB.LoadProgramContent("aaa", 2).Should().Be("aaa program content rev 2");
      _jobDB
        .LoadMostRecentProgram("ccc")
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "ccc",
            Revision = 1,
            Comment = "the ccc comment",
          }
        );
      _jobDB.LoadProgramContent("ccc", 1).Should().Be("ccc first program");

      //now set cell controller names
      _jobDB.LoadProgramsInCellController().Should().BeEmpty();
      _jobDB.SetCellControllerProgramForProgram("aaa", 1, "aaa-1");
      _jobDB.SetCellControllerProgramForProgram("bbb", 6, "bbb-6");

      _jobDB
        .ProgramFromCellControllerProgram("aaa-1")
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 1,
            Comment = "aaa comment",
            CellControllerProgramName = "aaa-1"
          }
        );
      _jobDB
        .LoadProgram("aaa", 1)
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 1,
            Comment = "aaa comment",
            CellControllerProgramName = "aaa-1"
          }
        );
      _jobDB
        .ProgramFromCellControllerProgram("bbb-6")
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "bbb",
            Revision = 6,
            Comment = "bbb comment",
            CellControllerProgramName = "bbb-6"
          }
        );
      _jobDB
        .LoadMostRecentProgram("bbb")
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "bbb",
            Revision = 6,
            Comment = "bbb comment",
            CellControllerProgramName = "bbb-6"
          }
        );
      _jobDB.ProgramFromCellControllerProgram("aagaiouhgi").Should().BeNull();
      _jobDB
        .LoadProgramsInCellController()
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new ProgramRevision()
            {
              ProgramName = "aaa",
              Revision = 1,
              Comment = "aaa comment",
              CellControllerProgramName = "aaa-1"
            },
            new ProgramRevision()
            {
              ProgramName = "bbb",
              Revision = 6,
              Comment = "bbb comment",
              CellControllerProgramName = "bbb-6"
            }
          }
        );

      _jobDB.SetCellControllerProgramForProgram("aaa", 1, null);

      _jobDB
        .LoadProgramsInCellController()
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new ProgramRevision()
            {
              ProgramName = "bbb",
              Revision = 6,
              Comment = "bbb comment",
              CellControllerProgramName = "bbb-6"
            }
          }
        );

      _jobDB.ProgramFromCellControllerProgram("aaa-1").Should().BeNull();
      _jobDB
        .LoadProgram("aaa", 1)
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 1,
            Comment = "aaa comment",
          }
        );

      _jobDB
        .Invoking(j => j.SetCellControllerProgramForProgram("aaa", 2, "bbb-6"))
        .Should()
        .Throw<Exception>()
        .WithMessage("Cell program name bbb-6 already in use");

      _jobDB.AddPrograms(
        new[]
        {
          new NewProgramContent()
          {
            ProgramName = "aaa",
            Revision = 0, // should be ignored because comment and content matches revision 1
            Comment = "aaa comment",
            ProgramContent = "aaa program content"
          },
          new NewProgramContent()
          {
            ProgramName = "bbb",
            Revision = 0, // allocate new
            Comment = "bbb comment rev7",
            ProgramContent = "bbb program content rev7"
          },
        },
        job1.RouteStartUTC
      );

      _jobDB
        .LoadMostRecentProgram("aaa")
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 2, // didn't allocate 3
            Comment = "aaa comment rev 2",
          }
        );
      _jobDB
        .LoadMostRecentProgram("bbb")
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "bbb",
            Revision = 7,
            Comment = "bbb comment rev7",
          }
        );
      _jobDB.LoadProgramContent("bbb", 7).Should().Be("bbb program content rev7");

      // adds new when comment matches, but content does not
      _jobDB.AddPrograms(
        new[]
        {
          new NewProgramContent()
          {
            ProgramName = "aaa",
            Revision = 0, // allocate new
            Comment = "aaa comment", // comment matches
            ProgramContent = "aaa program content rev 3" // content does not
          }
        },
        job1.RouteStartUTC
      );

      _jobDB
        .LoadMostRecentProgram("aaa")
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 3,
            Comment = "aaa comment",
          }
        );
      _jobDB.LoadProgramContent("aaa", 3).Should().BeEquivalentTo("aaa program content rev 3");

      // loading all revisions
      _jobDB
        .LoadProgramRevisionsInDescendingOrderOfRevision("aaa", 3, startRevision: null)
        .Should()
        .BeEquivalentTo(
          new[]
          {
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
          },
          options => options.WithStrictOrdering()
        );
      _jobDB
        .LoadProgramRevisionsInDescendingOrderOfRevision("aaa", 1, startRevision: null)
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new ProgramRevision()
            {
              ProgramName = "aaa",
              Revision = 3,
              Comment = "aaa comment",
            }
          },
          options => options.WithStrictOrdering()
        );
      _jobDB
        .LoadProgramRevisionsInDescendingOrderOfRevision("aaa", 2, startRevision: 1)
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new ProgramRevision()
            {
              ProgramName = "aaa",
              Revision = 1,
              Comment = "aaa comment",
            }
          },
          options => options.WithStrictOrdering()
        );

      _jobDB.LoadProgramRevisionsInDescendingOrderOfRevision("wesrfohergh", 10000, null).Should().BeEmpty();
    }

    [Fact]
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
              ProgramContent = "bbb program content 6"
            },
            new NewProgramContent()
            {
              ProgramName = "ccc",
              Revision = 3,
              Comment = "ccc comment 3",
              ProgramContent = "ccc program content 3"
            },
            new NewProgramContent()
            {
              ProgramName = "ccc",
              Revision = 4,
              Comment = "ccc comment 4",
              ProgramContent = "ccc program content 4"
            }
          )
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
                  Stations = ImmutableList<int>.Empty,
                  ExpectedCycleTime = TimeSpan.Zero
                }
              )
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
                  Stations = ImmutableList<int>.Empty,
                  ExpectedCycleTime = TimeSpan.Zero
                }
              )
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
                  Stations = ImmutableList<int>.Empty,
                  ExpectedCycleTime = TimeSpan.Zero
                }
              )
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
                  Stations = ImmutableList<int>.Empty,
                  ExpectedCycleTime = TimeSpan.Zero
                }
              )
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
                  Stations = ImmutableList<int>.Empty,
                  ExpectedCycleTime = TimeSpan.Zero
                }
              )
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
            Revision = -1
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 0,
            ProgramName = "aaa",
            Revision = -2
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 1,
            ProgramName = "bbb",
            Revision = -1
          }
        ]
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
            Revision = -2
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 1,
            ProgramName = "ccc",
            Revision = -1
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 2,
            ProgramName = "ccc",
            Revision = -2
          }
        ]
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
              ProgramContent = "aaa program content for 1"
            },
            new NewProgramContent()
            {
              ProgramName = "aaa",
              Revision = -2, // should be created to be revision 2
              Comment = "aaa comment 2",
              ProgramContent = "aaa program content for 2"
            },
            new NewProgramContent()
            {
              ProgramName = "bbb",
              Revision = -1, // matches latest content so should be converted to 6
              Comment = "bbb other comment", // comment doesn't match but is ignored
              ProgramContent = "bbb program content 6"
            },
            new NewProgramContent()
            {
              ProgramName = "bbb",
              Revision = -2, // assigned a new val 7 since content differs
              Comment = "bbb comment 7",
              ProgramContent = "bbb program content 7"
            },
            new NewProgramContent()
            {
              ProgramName = "ccc",
              Revision = -1, // assigned to 3 (older than most recent) because comment and content match
              Comment = "ccc comment 3",
              ProgramContent = "ccc program content 3"
            },
            new NewProgramContent()
            {
              ProgramName = "ccc",
              Revision = -2, // assigned a new val since doesn't match existing, even if comment matches
              Comment = "ccc comment 3",
              ProgramContent = "ccc program content 5"
            }
          )
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
                  Stations = ImmutableList<int>.Empty,
                  ExpectedCycleTime = TimeSpan.Zero
                }
              )
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
                  Stations = ImmutableList<int>.Empty,
                  ExpectedCycleTime = TimeSpan.Zero
                }
              )
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
                  Stations = ImmutableList<int>.Empty,
                  ExpectedCycleTime = TimeSpan.Zero
                }
              )
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
                  Stations = ImmutableList<int>.Empty,
                  ExpectedCycleTime = TimeSpan.Zero
                }
              )
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
                  Stations = ImmutableList<int>.Empty,
                  ExpectedCycleTime = TimeSpan.Zero
                }
              )
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
            Revision = 1
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 0,
            ProgramName = "aaa",
            Revision = 2
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 1,
            ProgramName = "bbb",
            Revision = 6
          }
        ]
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
            Revision = 7
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 1,
            ProgramName = "ccc",
            Revision = 3
          },
          new ProgramForJobStep()
          {
            ProcessNumber = 2,
            StopIndex = 2,
            ProgramName = "ccc",
            Revision = 5
          }
        ]
      };

      _jobDB
        .LoadJob(job1.UniqueStr)
        .Should()
        .BeEquivalentTo(
          job1.CloneToDerived<HistoricJob, Job>() with
          {
            ScheduleId = schId,
            CopiedToSystem = true,
            Decrements = ImmutableList<DecrementQuantity>.Empty
          }
        );

      _jobDB
        .LoadProgram("aaa", 1)
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 1,
            Comment = "aaa comment 1",
          }
        );
      _jobDB.LoadProgramContent("aaa", 1).Should().Be("aaa program content for 1");
      _jobDB
        .LoadProgram("aaa", 2)
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 2,
            Comment = "aaa comment 2",
          }
        );
      _jobDB.LoadProgramContent("aaa", 2).Should().Be("aaa program content for 2");
      _jobDB.LoadMostRecentProgram("aaa").Revision.Should().Be(2);

      _jobDB
        .LoadProgram("bbb", 6)
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "bbb",
            Revision = 6,
            Comment = "bbb comment 6",
          }
        );
      _jobDB.LoadProgramContent("bbb", 6).Should().Be("bbb program content 6");
      _jobDB
        .LoadProgram("bbb", 7)
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "bbb",
            Revision = 7,
            Comment = "bbb comment 7",
          }
        );
      _jobDB.LoadProgramContent("bbb", 7).Should().Be("bbb program content 7");
      _jobDB.LoadMostRecentProgram("bbb").Revision.Should().Be(7);

      _jobDB
        .LoadProgram("ccc", 3)
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "ccc",
            Revision = 3,
            Comment = "ccc comment 3",
          }
        );
      _jobDB.LoadProgramContent("ccc", 3).Should().Be("ccc program content 3");
      _jobDB
        .LoadProgram("ccc", 4)
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "ccc",
            Revision = 4,
            Comment = "ccc comment 4",
          }
        );
      _jobDB.LoadProgramContent("ccc", 4).Should().Be("ccc program content 4");
      _jobDB
        .LoadProgram("ccc", 5)
        .Should()
        .BeEquivalentTo(
          new ProgramRevision()
          {
            ProgramName = "ccc",
            Revision = 5,
            Comment = "ccc comment 3",
          }
        );
      _jobDB.LoadProgramContent("ccc", 5).Should().Be("ccc program content 5");
      _jobDB.LoadMostRecentProgram("ccc").Revision.Should().Be(5);
    }

    [Fact]
    public void ErrorOnDuplicateSchId()
    {
      using var _jobDB = _repoCfg.OpenConnection();
      var job1 = RandJob();
      var job2 = RandJob();

      _jobDB.AddJobs(new NewJobs() { Jobs = ImmutableList.Create(job1), ScheduleId = "sch1" }, null, true);

      _jobDB
        .LoadJob(job1.UniqueStr)
        .Should()
        .BeEquivalentTo(
          job1.CloneToDerived<HistoricJob, Job>() with
          {
            ScheduleId = "sch1",
            CopiedToSystem = true,
            Decrements = ImmutableList<DecrementQuantity>.Empty
          }
        );

      _jobDB
        .Invoking(j =>
          j.AddJobs(new NewJobs() { Jobs = ImmutableList.Create(job2), ScheduleId = "sch1" }, null, true)
        )
        .Should()
        .Throw<BadRequestException>()
        .WithMessage("Schedule ID sch1 already exists!");
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
                Path = rnd.Next(1, 10)
              },
              new SimulatedStationPart()
              {
                JobUnique = "uniq" + i,
                Process = rnd.Next(1, 10),
                Path = 11 + rnd.Next(1, 10)
              }
            }
          );
        }

        ret.Add(
          new SimulatedStationUtilization()
          {
            ScheduleId = schId,
            StationGroup = "group" + rnd.Next(0, 100000).ToString(),
            StationNum = rnd.Next(0, 10000),
            StartUTC = start.AddMinutes(-rnd.Next(200, 300)),
            EndUTC = start.AddMinutes(rnd.Next(0, 100)),
            PlanDown = rnd.Next(0, 100) < 20 ? (rnd.Next(1, 100) < 50 ? null : false) : true,
            Parts = parts
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
                  .ToImmutableList()
              }
          )
          .ToImmutableList()
      };
    }
  }
}
