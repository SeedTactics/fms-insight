/* Copyright (c) 2023, John Lenz

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
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization;

namespace BlackMaple.MachineFramework
{
  public record PathInspection : IComparable<PathInspection>
  {
    public required string InspectionType { get; init; }

    //There are two possible ways of triggering an inspection: counts and frequencies.
    // * For counts, the MaxVal will contain a number larger than zero and RandomFreq will contain -1
    // * For frequencies, the value of MaxVal is -1 and RandomFreq contains
    //   the frequency as a number between 0 and 1.

    //Every time a material completes, the counter string is expanded (see below).
    public required string Counter { get; init; }

    //For each completed material, the counter is incremented.  If the counter is equal to MaxVal,
    //we signal an inspection and reset the counter to 0.
    public required int MaxVal { get; init; }

    //The random frequency of inspection
    public required double RandomFreq { get; init; }

    //If the last inspection signaled for this counter was longer than TimeInterval,
    //signal an inspection.  This can be disabled by using TimeSpan.Zero
    public required TimeSpan TimeInterval { get; init; }

    // Expected inspection type
    public TimeSpan? ExpectedInspectionTime { get; init; }

    //The final counter string is determined by replacing following substrings in the counter
    public static string PalletFormatFlag(int proc)
    {
      return "%pal" + proc.ToString() + "%";
    }

    public static string LoadFormatFlag(int proc)
    {
      return "%load" + proc.ToString() + "%";
    }

    public static string UnloadFormatFlag(int proc)
    {
      return "%unload" + proc.ToString() + "%";
    }

    public static string StationFormatFlag(int proc, int routeNum)
    {
      return "%stat" + proc.ToString() + "," + routeNum.ToString() + "%";
    }

    int IComparable<PathInspection>.CompareTo(PathInspection? other)
    {
      var r = InspectionType.CompareTo(other?.InspectionType);
      if (r != 0)
        return r;
      r = Counter.CompareTo(other?.Counter);
      if (r != 0)
        return r;
      r = MaxVal.CompareTo(other?.MaxVal);
      if (r != 0)
        return r;
      r = RandomFreq.CompareTo(other?.RandomFreq);
      if (r != 0)
        return r;
      r = TimeInterval.CompareTo(other?.TimeInterval);
      if (r != 0)
        return r;
      if (ExpectedInspectionTime.HasValue)
      {
        return ExpectedInspectionTime.Value.CompareTo(other?.ExpectedInspectionTime);
      }
      else
      {
        return other == null || !other.ExpectedInspectionTime.HasValue ? 0 : -1;
      }
    }
  }

  public record MachiningStop
  {
    public required string StationGroup { get; init; }

    [JsonPropertyName("StationNums")]
    public required ImmutableSortedSet<int> Stations { get; init; }

    public required TimeSpan ExpectedCycleTime { get; init; }

    // Programs can be specified in two possible ways: either here as part of the job or separately as part of
    // the workorder.  If this value is non-null, it specifies the program name to use for this machining step.
    // The program itself is found either by matching program name already existing in the cell controller or a program included
    // as part of the download in a NewProgramEntry structure.  If this is null, it means that programs are instead
    // specified in workorders, as part of the ProgramForJobStep record.
    public string? Program { get; init; }

    // During Download:
    //   * A null or zero revision value means use the latest program, either the one already in the cell controller
    //     or the most recent revision in the database.
    //   * A positive revision number will use this specified revision which must exist in the database or
    //     be included in the Programs field of the NewJobs structure accompaning the downloaded job.
    //   * A negative revision number must match a NewProgramEntry in the NewJobs structure accompaning the download.
    //     The NewProgramEntry will be assigned a (positive) revision during the download and then this stop will
    //     be updated to use that (positive) revision.
    // When loading Jobs,
    //   * A null revision means the program already exists in the cell controller and the DB is not managing programs.
    //   * A positive revision means the program exists in the job DB with this specific revision.
    //   * Negative revisions are never returned (they get translated as part of the download)
    public long? ProgramRevision { get; init; }
  }

  public record HoldPattern
  {
    // All of the following hold types are an OR, meaning if any one of them says a hold is in effect,
    // the job is on hold.

    public required bool UserHold { get; init; }

    public required string ReasonForUserHold { get; init; }

    //A list of timespans the job should be on hold/not on hold.
    //During the first timespan, the job is on hold.
    public required ImmutableList<TimeSpan> HoldUnholdPattern { get; init; }

    public required DateTime HoldUnholdPatternStartUTC { get; init; }

    public required bool HoldUnholdPatternRepeats { get; init; }
  }

  public record SimulatedProduction : IComparable<SimulatedProduction>
  {
    public required DateTime TimeUTC { get; init; }

    public required int Quantity { get; init; } //total quantity simulated to be completed at TimeUTC

    int IComparable<SimulatedProduction>.CompareTo(SimulatedProduction? other)
    {
      var r = TimeUTC.CompareTo(other?.TimeUTC);
      if (r != 0)
        return r;
      return Quantity.CompareTo(other?.Quantity);
    }
  }

  public record ProcessInfo
  {
    [JsonPropertyName("paths")]
    public required ImmutableList<ProcPathInfo> Paths { get; init; } = ImmutableList<ProcPathInfo>.Empty;
  }

  public record ProcPathInfo
  {
    public required ImmutableSortedSet<int> PalletNums { get; init; }

    public string? Fixture { get; init; }

    public int? Face { get; init; }

    public required ImmutableSortedSet<int> Load { get; init; }

    public required TimeSpan ExpectedLoadTime { get; init; }

    public required ImmutableSortedSet<int> Unload { get; init; }

    public required TimeSpan ExpectedUnloadTime { get; init; }

    public required ImmutableList<MachiningStop> Stops { get; init; }

    public ImmutableSortedSet<SimulatedProduction>? SimulatedProduction { get; init; }

    public required DateTime SimulatedStartingUTC { get; init; }

    public required TimeSpan SimulatedAverageFlowTime { get; init; } // average time a part takes to complete the entire sequence

    public HoldPattern? HoldMachining { get; init; }

    public HoldPattern? HoldLoadUnload { get; init; }

    public required int PartsPerPallet { get; init; }

    public string? InputQueue { get; init; }

    public string? OutputQueue { get; init; }

    public ImmutableSortedSet<PathInspection>? Inspections { get; init; }

    public string? Casting { get; init; }
  }

  public record Job
  {
    [JsonPropertyName("Unique")]
    public required string UniqueStr { get; init; }

    public required DateTime RouteStartUTC { get; init; }

    public required DateTime RouteEndUTC { get; init; }

    public required bool Archived { get; init; }

    public required string PartName { get; init; }

    public string? Comment { get; init; }

    public string? AllocationAlgorithm { get; init; }

    [JsonPropertyName("Bookings")]
    public ImmutableSortedSet<string>? BookingIds { get; init; }

    public bool ManuallyCreated { get; init; }

    [JsonPropertyName("HoldEntireJob")]
    public HoldPattern? HoldJob { get; init; }

    public required int Cycles { get; init; }

    [JsonPropertyName("ProcsAndPaths")]
    public required ImmutableList<ProcessInfo> Processes { get; init; }
  }

  public record DecrementQuantity
  {
    public required long DecrementId { get; init; }

    public required DateTime TimeUTC { get; init; }

    public required int Quantity { get; init; }
  }

  public record HistoricJob : Job
  {
    public string? ScheduleId { get; init; }

    public required bool CopiedToSystem { get; init; }

    public ImmutableList<DecrementQuantity>? Decrements { get; init; }
  }

  public record ActiveJob : HistoricJob
  {
    public ImmutableList<ImmutableList<int>>? Completed { get; init; }

    // a number reflecting the order in which the cell controller will consider the processes and paths for activation.
    // lower numbers come first, while -1 means no-data.
    public ImmutableList<ImmutableList<long>>? Precedence { get; init; }

    public ImmutableSortedSet<string>? AssignedWorkorders { get; init; }

    public long? RemainingToStart { get; init; }
  }

  public static class JobAdjustment
  {
    public static Job AdjustPath(this Job job, int proc, int path, Func<ProcPathInfo, ProcPathInfo> f)
    {
      return job with
      {
        Processes = job
          .Processes.Select(
            (p, i) =>
              i == proc - 1
                ? p with
                {
                  Paths = p.Paths.Select((pa, j) => j == path - 1 ? f(pa) : pa).ToImmutableList(),
                }
                : p
          )
          .ToImmutableList(),
      };
    }

    public static Job AdjustAllPaths(this Job job, Func<ProcPathInfo, ProcPathInfo> f)
    {
      return job with
      {
        Processes = job
          .Processes.Select(p => p with { Paths = p.Paths.Select(f).ToImmutableList() })
          .ToImmutableList(),
      };
    }

    public static Job AdjustAllPaths(this Job job, Func<int, int, ProcPathInfo, ProcPathInfo> f)
    {
      return job with
      {
        Processes = job
          .Processes.Select(
            (p, i) => p with { Paths = p.Paths.Select((pa, j) => f(i + 1, j + 1, pa)).ToImmutableList() }
          )
          .ToImmutableList(),
      };
    }

    public static HistoricJob AdjustAllPaths(
      this HistoricJob job,
      Func<int, int, ProcPathInfo, ProcPathInfo> f
    )
    {
      return job with
      {
        Processes = job
          .Processes.Select(
            (p, i) => p with { Paths = p.Paths.Select((pa, j) => f(i + 1, j + 1, pa)).ToImmutableList() }
          )
          .ToImmutableList(),
      };
    }
  }
}
