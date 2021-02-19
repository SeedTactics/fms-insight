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
using System.Linq;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;
using Microsoft.Data.Sqlite;
using Xunit;
using FluentAssertions;
using System.Collections.Immutable;
using AutoFixture;

namespace MachineWatchTest
{
  public class JobDBSpec : IDisposable
  {
    private RepositoryConfig _repoCfg;
    private IRepository _jobDB;

    public JobDBSpec()
    {
      _repoCfg = RepositoryConfig.InitializeSingleThreadedMemoryDB(new FMSSettings());
      _jobDB = _repoCfg.OpenConnection();
    }

    public void Dispose()
    {
      _repoCfg.CloseMemoryConnection();
    }

    [Fact]
    public void AddsJobs()
    {
      var fix = new Fixture();

      var job1 = fix.Create<Job>();
      job1 = job1 with
      {
        RouteEndUTC = job1.RouteStartUTC.AddHours(1),
        Processes = job1.Processes.Select((proc, procIdx) => new ProcessInfo()
        {
          Paths =
          proc.Paths.Select(path => path with
          {
            Casting = procIdx == 0 ? path.Casting : null
          }).ToArray()
        }).ToArray()
      };
      var job1ExtraParts = fix.Create<Dictionary<string, int>>();
      var job1UnfilledWorks = fix.Create<List<PartWorkorder>>();
      var job1StatUse = RandSimStationUse(job1.RouteStartUTC);
      var addAsCopied = fix.Create<bool>();

      _jobDB.AddJobs(new NewJobs()
      {
        ScheduleId = job1.ScheduleId,
        Jobs = ImmutableList.Create(job1),
        ExtraParts = job1ExtraParts.ToImmutableDictionary(),
        CurrentUnfilledWorkorders = job1UnfilledWorks.ToImmutableList(),
        StationUse = job1StatUse,
      }, null, addAsCopiedToSystem: addAsCopied);

      var job1history = job1.CloneToDerived<HistoricJob, Job>() with
      {
        CopiedToSystem = addAsCopied,
        Decrements = new DecrementQuantity[] { }
      };

      _jobDB.LoadJobHistory(job1.RouteStartUTC.AddHours(-1), job1.RouteStartUTC.AddHours(10)).Should().BeEquivalentTo(
        new HistoricData()
        {
          Jobs = ImmutableDictionary.Create<string, HistoricJob>().Add(job1.UniqueStr, job1history),
          StationUse = job1StatUse
        },
        options => options
          .ComparingByMembers<HistoricData>()
          .ComparingByMembers<HistoricJob>()
          .ComparingByMembers<HoldPattern>()
          .ComparingByMembers<ProcessInfo>()
          .ComparingByMembers<ProcPathInfo>()
          .ComparingByMembers<MachiningStop>()
      );
    }


    private static void AddObsoleteInspData(JobPlan job)
    {
      // check obsolete saves properly
      job.GetType().GetField("_inspections", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(job,
        new List<JobInspectionData>() {
          new JobInspectionData("OldInsp1", "counter1", 53, TimeSpan.FromMinutes(22)),
          new JobInspectionData("OldInsp2", "counter2", 12.8, TimeSpan.FromMinutes(33), 1)
        }
      );
    }
    private static void AddExpectedPathDataFromObsoleteInspections(JobPlan job)
    {
      // OldInsp1 is null InspProc so should be final process
      var oldInsp1 = new PathInspection() { InspectionType = "OldInsp1", Counter = "counter1", MaxVal = 53, TimeInterval = TimeSpan.FromMinutes(22) };
      for (int path = 1; path <= job.GetNumPaths(job.NumProcesses); path++)
      {
        job.PathInspections(job.NumProcesses, path).Add(oldInsp1);
      }

      // OldInsp2 is InspProc 1 so should be first process
      var oldInsp2 = new PathInspection() { InspectionType = "OldInsp2", Counter = "counter2", RandomFreq = 12.8, TimeInterval = TimeSpan.FromMinutes(33) };
      for (int path = 1; path <= job.GetNumPaths(1); path++)
      {
        job.PathInspections(1, path).Add(oldInsp2);
      }

      // clear old inspection data
      job.GetType().GetField("_inspections", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(job, null);
    }

    private static ImmutableList<SimulatedStationUtilization> RandSimStationUse(DateTime start)
    {
      var rnd = new Random();
      var ret = ImmutableList.CreateBuilder<SimulatedStationUtilization>();
      for (int i = 0; i < 3; i++)
      {
        ret.Add(new SimulatedStationUtilization()
        {
          ScheduleId = "id" + rnd.Next(0, 10000).ToString(),
          StationGroup = "group" + rnd.Next(0, 100000).ToString(),
          StationNum = rnd.Next(0, 10000),
          StartUTC = start.AddMinutes(-rnd.Next(200, 300)),
          EndUTC = start.AddMinutes(rnd.Next(0, 100)),
          UtilizationTime = TimeSpan.FromMinutes(rnd.Next(10, 1000)),
          PlannedDownTime = TimeSpan.FromMinutes(rnd.Next(10, 1000))
        });
      }
      return ret.ToImmutable();
    }
  }
}