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
#nullable enable

using System;
using System.Collections.Immutable;

namespace BlackMaple.MachineFramework
{
  public record SimulatedStationPart
  {
    public required string JobUnique { get; init; }

    public required int Process { get; init; }

    public required int Path { get; init; }
  }

  public record SimulatedStationUtilization
  {
    public required string ScheduleId { get; init; }

    public required string StationGroup { get; init; }

    public required int StationNum { get; init; }

    public required DateTime StartUTC { get; init; }

    public required DateTime EndUTC { get; init; }

    public bool? PlanDown { get; init; }

    public ImmutableList<SimulatedStationPart>? Parts { get; init; }
  }

  public record SimulatedDayUsage
  {
    public required DateOnly Day { get; init; }

    public required string MachineGroup { get; init; }

    public required double Usage { get; init; }
  }

  public record SimulationResults
  {
    public required string ScheduleId { get; init; }
    public required ImmutableList<Job> Jobs { get; init; }
    public ImmutableDictionary<string, int>? NewExtraParts { get; init; }
    public required ImmutableList<SimulatedStationUtilization> SimStations { get; init; }
    public ImmutableList<SimulatedStationUtilization>? SimStationsForExecutionOfCurrentStatus { get; init; }
    public ImmutableList<SimulatedDayUsage>? SimDayUsage { get; init; }
    public ImmutableList<WorkorderSimFilled>? SimWorkordersFilled { get; init; }
    public ImmutableList<string>? AllocationWarning { get; init; }
    public byte[]? DebugMessage { get; init; }
  }

  // NewJobs is SimulationResults plus workorders and programs and so conceptually should be derived
  // from SimulationResults, but a few fields are renamed because of backwards compatibility with
  // older APIs
  public record NewJobs
  {
    public required string ScheduleId { get; init; }

    public required ImmutableList<Job> Jobs { get; init; }

    public ImmutableDictionary<string, int>? ExtraParts { get; init; }

    public ImmutableList<SimulatedStationUtilization>? StationUse { get; init; }

    public ImmutableList<SimulatedStationUtilization>? StationUseForCurrentStatus { get; init; }

    public ImmutableList<SimulatedDayUsage>? SimDayUsage { get; init; }

    public ImmutableList<WorkorderSimFilled>? SimWorkordersFilled { get; init; }

    public ImmutableList<Workorder>? CurrentUnfilledWorkorders { get; init; }

    public ImmutableList<NewProgramContent>? Programs { get; init; }

    public ImmutableList<string>? AllocationWarning { get; init; }

    public byte[]? DebugMessage { get; init; }
  }
}
