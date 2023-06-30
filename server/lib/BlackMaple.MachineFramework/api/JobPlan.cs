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

#nullable enable

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;

namespace BlackMaple.MachineFramework
{
  [DataContract]
  public record PathInspection
  {
    [DataMember(IsRequired = true)]
    public required string InspectionType { get; init; }

    //There are two possible ways of triggering an inspection: counts and frequencies.
    // * For counts, the MaxVal will contain a number larger than zero and RandomFreq will contain -1
    // * For frequencies, the value of MaxVal is -1 and RandomFreq contains
    //   the frequency as a number between 0 and 1.

    //Every time a material completes, the counter string is expanded (see below).
    [DataMember(IsRequired = true)]
    public required string Counter { get; init; }

    //For each completed material, the counter is incremented.  If the counter is equal to MaxVal,
    //we signal an inspection and reset the counter to 0.
    [DataMember(IsRequired = true)]
    public required int MaxVal { get; init; }

    //The random frequency of inspection
    [DataMember(IsRequired = true)]
    public required double RandomFreq { get; init; }

    //If the last inspection signaled for this counter was longer than TimeInterval,
    //signal an inspection.  This can be disabled by using TimeSpan.Zero
    [DataMember(IsRequired = true)]
    public required TimeSpan TimeInterval { get; init; }

    // Expected inspection type
    [DataMember(IsRequired = false, EmitDefaultValue = false)]
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
  }

  [DataContract]
  public record MachiningStop
  {
    [DataMember(Name = "StationGroup", IsRequired = true)]
    public required string StationGroup { get; init; }

    [DataMember(Name = "StationNums", IsRequired = true)]
    public required ImmutableList<int> Stations { get; init; }

    [DataMember(Name = "ExpectedCycleTime", IsRequired = true)]
    public required TimeSpan ExpectedCycleTime { get; init; }

    // Programs can be specified in two possible ways: either here as part of the job or separately as part of
    // the workorder.  If this value is non-null, it specifies the program name to use for this machining step.
    // The program itself is found either by matching program name already existing in the cell controller or a program included
    // as part of the download in a NewProgramEntry structure.  If this is null, it means that programs are instead
    // specified in workorders, as part of the ProgramForJobStep record.
    [DataMember(Name = "Program", IsRequired = false)]
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
    [DataMember(Name = "ProgramRevision", IsRequired = false, EmitDefaultValue = false)]
    public long? ProgramRevision { get; init; }

    [DataMember(Name = "Tools", IsRequired = false, EmitDefaultValue = true)]
    public ImmutableDictionary<string, TimeSpan>? Tools { get; init; } //key is tool, value is expected cutting time
  }

  [DataContract]
  public record HoldPattern
  {
    // All of the following hold types are an OR, meaning if any one of them says a hold is in effect,
    // the job is on hold.

    [DataMember(IsRequired = true)]
    public required bool UserHold { get; init; }

    [DataMember(IsRequired = true)]
    public required string ReasonForUserHold { get; init; }

    //A list of timespans the job should be on hold/not on hold.
    //During the first timespan, the job is on hold.
    [DataMember(IsRequired = true)]
    public required ImmutableList<TimeSpan> HoldUnholdPattern { get; init; }

    [DataMember(IsRequired = true)]
    public required DateTime HoldUnholdPatternStartUTC { get; init; }

    [DataMember(IsRequired = true)]
    public required bool HoldUnholdPatternRepeats { get; init; }
  }

  [DataContract]
  public record SimulatedProduction
  {
    [DataMember(IsRequired = true)]
    public required DateTime TimeUTC { get; init; }

    [DataMember(IsRequired = true)]
    public required int Quantity { get; init; } //total quantity simulated to be completed at TimeUTC
  }

  [DataContract]
  public record ProcessInfo
  {
    [DataMember(Name = "paths", IsRequired = true)]
    public required ImmutableList<ProcPathInfo> Paths { get; init; } = ImmutableList<ProcPathInfo>.Empty;
  }

  [DataContract]
  public record ProcPathInfo
  {
    // not required only for backwards compatibility, make required once Pallets as strings is removed
    [DataMember(IsRequired = false, EmitDefaultValue = true)]
    public required ImmutableList<int> PalletNums { get; init; }

    [DataMember(IsRequired = false)]
    public string? Fixture { get; init; }

    [DataMember(IsRequired = false)]
    public int? Face { get; init; }

    [DataMember(IsRequired = true)]
    public required ImmutableList<int> Load { get; init; }

    [DataMember(IsRequired = true)]
    public required TimeSpan ExpectedLoadTime { get; init; }

    [DataMember(IsRequired = true)]
    public required ImmutableList<int> Unload { get; init; }

    [DataMember(IsRequired = true)]
    public required TimeSpan ExpectedUnloadTime { get; init; }

    [DataMember(IsRequired = true)]
    public required ImmutableList<MachiningStop> Stops { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public ImmutableList<SimulatedProduction>? SimulatedProduction { get; init; }

    [DataMember(IsRequired = true)]
    public required DateTime SimulatedStartingUTC { get; init; }

    [DataMember(IsRequired = true)]
    public required TimeSpan SimulatedAverageFlowTime { get; init; } // average time a part takes to complete the entire sequence

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public HoldPattern? HoldMachining { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public HoldPattern? HoldLoadUnload { get; init; }

    [DataMember(IsRequired = true)]
    public required int PartsPerPallet { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public string? InputQueue { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public string? OutputQueue { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public ImmutableList<MachineFramework.PathInspection>? Inspections { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public string? Casting { get; init; }

    // for backwards compatibility
    [DataMember(IsRequired = false, EmitDefaultValue = true), Obsolete]
    public ImmutableList<string> Pallets
    {
      get => PalletNums?.Select(n => n.ToString())?.ToImmutableList() ?? ImmutableList<string>.Empty;
      init
      {
        if (PalletNums != null && PalletNums.Count > 0)
          return;
        PalletNums = value
          .SelectMany(s => int.TryParse(s, out var n) ? new[] { n } : Enumerable.Empty<int>())
          .ToImmutableList();
      }
    }
  }

  [DataContract]
  public record Job
  {
    [DataMember(Name = "Unique", IsRequired = true)]
    public required string UniqueStr { get; init; }

    [DataMember(Name = "RouteStartUTC", IsRequired = true)]
    public required DateTime RouteStartUTC { get; init; }

    [DataMember(Name = "RouteEndUTC", IsRequired = true)]
    public required DateTime RouteEndUTC { get; init; }

    [DataMember(Name = "Archived", IsRequired = true)]
    public required bool Archived { get; init; }

    [DataMember(Name = "PartName", IsRequired = true)]
    public required string PartName { get; init; }

    [DataMember(Name = "Comment", IsRequired = false, EmitDefaultValue = false)]
    public string? Comment { get; init; }

    [DataMember(Name = "AllocationAlgorithm", IsRequired = false, EmitDefaultValue = false)]
    public string? AllocationAlgorithm { get; init; }

    [DataMember(Name = "Bookings", IsRequired = false, EmitDefaultValue = false)]
    public ImmutableList<string>? BookingIds { get; init; }

    [DataMember(Name = "ManuallyCreated", IsRequired = false)]
    public bool ManuallyCreated { get; init; }

    [DataMember(Name = "HoldEntireJob", IsRequired = false, EmitDefaultValue = false)]
    public HoldPattern? HoldJob { get; init; }

    [DataMember(Name = "Cycles", IsRequired = false, EmitDefaultValue = true)]
    public required int Cycles { get; init; }

    [DataMember(Name = "ProcsAndPaths", IsRequired = true)]
    public required ImmutableList<ProcessInfo> Processes { get; init; }
  }

  [DataContract]
  public record DecrementQuantity
  {
    [DataMember(IsRequired = true)]
    public required long DecrementId { get; init; }

    [DataMember(IsRequired = true)]
    public required DateTime TimeUTC { get; init; }

    [DataMember(IsRequired = true)]
    public required int Quantity { get; init; }
  }

  [DataContract]
  public record HistoricJob : Job
  {
    [DataMember(Name = "ScheduleId", IsRequired = false, EmitDefaultValue = false)]
    public string? ScheduleId { get; init; }

    [DataMember(Name = "CopiedToSystem", IsRequired = true)]
    public required bool CopiedToSystem { get; init; }

    [DataMember(Name = "Decrements", IsRequired = false)]
    public ImmutableList<DecrementQuantity>? Decrements { get; init; }
  }

  [DataContract]
  public record ActiveJob : HistoricJob
  {
    [DataMember(Name = "Completed", IsRequired = false, EmitDefaultValue = false)]
    public ImmutableList<ImmutableList<int>>? Completed { get; init; }

    // a number reflecting the order in which the cell controller will consider the processes and paths for activation.
    // lower numbers come first, while -1 means no-data.
    [DataMember(Name = "Precedence", IsRequired = false, EmitDefaultValue = false)]
    public ImmutableList<ImmutableList<long>>? Precedence { get; init; }

    [DataMember(Name = "AssignedWorkorders", IsRequired = false, EmitDefaultValue = false)]
    public ImmutableList<string>? AssignedWorkorders { get; init; }

    [DataMember(Name = "RemainingToStart", IsRequired = false, EmitDefaultValue = false)]
    public long? RemainingToStart { get; init; }
  }

  public static class JobAdjustment
  {
    public static Job AdjustPath(this Job job, int proc, int path, Func<ProcPathInfo, ProcPathInfo> f)
    {
      return job with
      {
        Processes = job.Processes
          .Select(
            (p, i) =>
              i == proc - 1
                ? p with
                {
                  Paths = p.Paths.Select((pa, j) => j == path - 1 ? f(pa) : pa).ToImmutableList()
                }
                : p
          )
          .ToImmutableList()
      };
    }

    public static Job AdjustAllPaths(this Job job, Func<ProcPathInfo, ProcPathInfo> f)
    {
      return job with
      {
        Processes = job.Processes
          .Select(p => p with { Paths = p.Paths.Select(f).ToImmutableList() })
          .ToImmutableList()
      };
    }

    public static Job AdjustAllPaths(this Job job, Func<int, int, ProcPathInfo, ProcPathInfo> f)
    {
      return job with
      {
        Processes = job.Processes
          .Select(
            (p, i) => p with { Paths = p.Paths.Select((pa, j) => f(i + 1, j + 1, pa)).ToImmutableList() }
          )
          .ToImmutableList()
      };
    }
  }
}
