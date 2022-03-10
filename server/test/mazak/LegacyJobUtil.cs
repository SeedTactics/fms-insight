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
using BlackMaple.MachineFramework;
using Xunit;
using FluentAssertions;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace MachineWatchTest
{
  public static class LegacyToNewJobConvert
  {

    private static HoldPattern ToInsightHold(MazakMachineInterface.JobHoldPattern h)
    {
      if (h == null) return null;

      return new HoldPattern()
      {
        UserHold = h.UserHold,
        ReasonForUserHold = h.ReasonForUserHold,
        HoldUnholdPatternStartUTC = h.HoldUnholdPatternStartUTC,
        HoldUnholdPattern = h.HoldUnholdPattern.ToImmutableList(),
        HoldUnholdPatternRepeats = h.HoldUnholdPatternRepeats
      };
    }

    public static HistoricJob ToHistoricJob(this MazakMachineInterface.JobPlan job)
    {
      return new HistoricJob()
      {
        Comment = job.Comment,
        HoldJob = ToInsightHold(job.HoldEntireJob),
        // ignoring obsolete job-level inspections
        // ignoring Priority, CreateMarkingData
        ManuallyCreated = job.ManuallyCreatedJob,
        BookingIds = job.ScheduledBookingIds.ToImmutableList(),
        ScheduleId = job.ScheduleId,
        UniqueStr = job.UniqueStr,
        PartName = job.PartName,
        CopiedToSystem = job.JobCopiedToSystem,
        Archived = job.Archived,
        RouteStartUTC = job.RouteStartingTimeUTC,
        RouteEndUTC = job.RouteEndingTimeUTC,
        Cycles = job.Cycles,
        Processes = Enumerable.Range(1, job.NumProcesses).Select(proc =>
          new ProcessInfo()
          {
            Paths = Enumerable.Range(1, job.GetNumPaths(process: proc)).Select(path =>
        new ProcPathInfo()
        {
          ExpectedUnloadTime = job.GetExpectedUnloadTime(proc, path),
          OutputQueue = job.GetOutputQueue(proc, path),
          InputQueue = job.GetInputQueue(proc, path),
          PartsPerPallet = job.PartsPerPallet(proc, path),
          HoldLoadUnload = ToInsightHold(job.HoldLoadUnload(proc, path)),
          HoldMachining = ToInsightHold(job.HoldMachining(proc, path)),
          SimulatedAverageFlowTime = job.GetSimulatedAverageFlowTime(proc, path),
          SimulatedStartingUTC = job.GetSimulatedStartingTimeUTC(proc, path),
          SimulatedProduction = job.GetSimulatedProduction(proc, path).Select(s => new SimulatedProduction()
          {
            TimeUTC = s.TimeUTC,
            Quantity = s.Quantity
          }).ToImmutableList(),
          Stops = job.GetMachiningStop(proc, path).Select(stop => new MachiningStop()
          {
            Stations = stop.Stations.ToImmutableList(),
            Program = stop.ProgramName,
            ProgramRevision = stop.ProgramRevision,
            Tools = stop.Tools.ToImmutableDictionary(k => k.Key, k => k.Value),
            StationGroup = stop.StationGroup,
            ExpectedCycleTime = stop.ExpectedCycleTime
          }).ToImmutableList(),
          Casting = proc == 1 ? job.GetCasting(path) : null,
          Unload = job.UnloadStations(proc, path).ToImmutableList(),
          ExpectedLoadTime = job.GetExpectedLoadTime(proc, path),
          Load = job.LoadStations(proc, path).ToImmutableList(),
          Face = job.PlannedFixture(proc, path).face,
          Fixture = job.PlannedFixture(proc, path).fixture,
          Pallets = job.PlannedPallets(proc, path).ToImmutableList(),
          Inspections =
      job.PathInspections(proc, path).Select(i => new PathInspection()
      {
        InspectionType = i.InspectionType,
        Counter = i.Counter,
        MaxVal = i.MaxVal,
        RandomFreq = i.RandomFreq,
        TimeInterval = i.TimeInterval,
        ExpectedInspectionTime = i.ExpectedInspectionTime
      }
      ).ToImmutableList()
        }
            ).ToImmutableList()
          }
        ).ToImmutableList()
      };
    }

  }
}