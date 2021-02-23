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

namespace MachineWatchTest
{
  public class JobEqualityChecks
  {
    protected static void EqualSort<T>(IEnumerable<T> e1, IEnumerable<T> e2)
    {
      var lst1 = new List<T>(e1);
      lst1.Sort();
      var lst2 = new List<T>(e2);
      lst2.Sort();
      Assert.Equal(lst1, lst2);
    }

    private static void CheckHoldEqual(JobHoldPattern h1, JobHoldPattern h2)
    {

      DateTime d1 = h1.HoldUnholdPatternStartUTC;
      DateTime d2 = h1.HoldUnholdPatternStartUTC;
      d1 = new DateTime(d1.Year, d1.Month, d1.Day, d1.Hour, d1.Minute, d1.Second);
      d2 = new DateTime(d2.Year, d2.Month, d2.Day, d2.Hour, d2.Minute, d2.Second);

      Assert.Equal(h1.UserHold, h2.UserHold);
      Assert.Equal(h1.ReasonForUserHold, h2.ReasonForUserHold);
      Assert.Equal(d1, d2);
      Assert.Equal(h1.HoldUnholdPatternRepeats, h2.HoldUnholdPatternRepeats);
      Assert.Equal(h1.HoldUnholdPattern.Count, h2.HoldUnholdPattern.Count);
      for (int i = 0; i < h1.HoldUnholdPattern.Count; i++)
      {
        Assert.Equal(h1.HoldUnholdPattern[i], h2.HoldUnholdPattern[i]);
      }
    }

    public static void CheckPlanEqual(JobPlan job1, JobPlan job2, bool checkHolds)
    {
      Assert.NotNull(job1);
      Assert.NotNull(job2);

      Assert.Equal(job1.PartName, job2.PartName);
      Assert.Equal(job1.UniqueStr, job2.UniqueStr);
      Assert.Equal(job1.NumProcesses, job2.NumProcesses);
      Assert.Equal(job1.RouteStartingTimeUTC, job2.RouteStartingTimeUTC);
      Assert.Equal(job1.RouteEndingTimeUTC, job2.RouteEndingTimeUTC);
      Assert.Equal(job1.Archived, job2.Archived);
      Assert.Equal(job1.JobCopiedToSystem, job2.JobCopiedToSystem);
      EqualSort(job1.ScheduledBookingIds, job2.ScheduledBookingIds);
      Assert.Equal(job1.ScheduleId, job2.ScheduleId);
      Assert.Equal(job1.ManuallyCreatedJob, job2.ManuallyCreatedJob);


      if (checkHolds)
        CheckHoldEqual(job1.HoldEntireJob, job2.HoldEntireJob);

      for (int proc = 1; proc <= job1.NumProcesses; proc++)
      {
        Assert.Equal(job1.GetNumPaths(proc), job2.GetNumPaths(proc));
      }

      for (int path = 1; path <= job1.GetNumPaths(1); path++)
      {
        Assert.Equal(job1.GetPlannedCyclesOnFirstProcess(path), job2.GetPlannedCyclesOnFirstProcess(path));
      }

      for (int proc = 1; proc <= job1.NumProcesses; proc++)
      {
        for (int path = 1; path <= job1.GetNumPaths(proc); path++)
        {
          Assert.Equal(job1.GetSimulatedStartingTimeUTC(proc, path),
             job2.GetSimulatedStartingTimeUTC(proc, path));

          Assert.Equal(job1.PartsPerPallet(proc, path),
             job2.PartsPerPallet(proc, path));

          Assert.Equal(job1.GetPathGroup(proc, path),
             job2.GetPathGroup(proc, path));

          Assert.Equal(job1.GetInputQueue(proc, path), job2.GetInputQueue(proc, path));
          Assert.Equal(job1.GetOutputQueue(proc, path), job2.GetOutputQueue(proc, path));

          if (proc == 1)
          {
            Assert.Equal(job1.GetCasting(path), job2.GetCasting(path));
          }

          Assert.Equal(job1.GetSimulatedProduction(proc, path), job2.GetSimulatedProduction(proc, path));
          Assert.Equal(job1.GetSimulatedAverageFlowTime(proc, path), job2.GetSimulatedAverageFlowTime(proc, path));

          Assert.Equal(job1.GetExpectedLoadTime(proc, path), job2.GetExpectedLoadTime(proc, path));
          Assert.Equal(job1.GetExpectedUnloadTime(proc, path), job2.GetExpectedUnloadTime(proc, path));

          EqualSort(job1.LoadStations(proc, path), job2.LoadStations(proc, path));
          EqualSort(job1.UnloadStations(proc, path), job2.UnloadStations(proc, path));
          EqualSort(job1.PlannedPallets(proc, path), job2.PlannedPallets(proc, path));

          Assert.Equal(job1.PlannedFixture(proc, path), job2.PlannedFixture(proc, path));

          CheckInspEqual(job1.PathInspections(proc, path), job2.PathInspections(proc, path));

          if (checkHolds)
          {
            CheckHoldEqual(job1.HoldMachining(proc, path), job2.HoldMachining(proc, path));
            CheckHoldEqual(job1.HoldLoadUnload(proc, path), job2.HoldLoadUnload(proc, path));
          }


          var e1 = job1.GetMachiningStop(proc, path).GetEnumerator();
          var e2 = job2.GetMachiningStop(proc, path).GetEnumerator();

          while (e1.MoveNext())
          {
            if (!e2.MoveNext())
            {
              Assert.True(false, "Unequal number of routes");
            }

            JobMachiningStop route1 = e1.Current;
            JobMachiningStop route2 = e2.Current;

            if (route1 == null)
            {
              Assert.Null(route2);
            }
            else
            {
              Assert.NotNull(route2);
              Assert.Equal(route1.ExpectedCycleTime, route2.ExpectedCycleTime);
              EqualSort(route1.Stations, route2.Stations);
              Assert.Equal(route1.ProgramName, route2.ProgramName);
              Assert.Equal(route1.ProgramRevision, route2.ProgramRevision);
              Assert.Equal(route1.Tools, route2.Tools);
            }
          }
        }
      }
    }

    public static void CheckJobEqual(JobPlan job1, JobPlan job2, bool checkHolds)
    {
      CheckPlanEqual(job1, job2, checkHolds);
    }

    private static void CheckInspEqual(IEnumerable<PathInspection> i1, IEnumerable<PathInspection> i2)
    {
      var i2Copy = new List<PathInspection>(i2);
      foreach (var j1 in i1)
      {
        foreach (var j2 in i2Copy)
        {
          if (j1.InspectionType == j2.InspectionType
              && j1.Counter == j2.Counter
              && j1.MaxVal == j2.MaxVal
              && j1.TimeInterval == j2.TimeInterval
              && j1.RandomFreq == j2.RandomFreq
              && j1.ExpectedInspectionTime == j2.ExpectedInspectionTime)
          {
            i2Copy.Remove(j2);
            goto found;
          }
        }

        Assert.True(false, "Unable to find " + j1.InspectionType + " " + j1.Counter);

      found:;
      }

      if (i2Copy.Count > 0)
      {
        Assert.True(false, "i2 has extra stuff");
      }
    }

  }
}
