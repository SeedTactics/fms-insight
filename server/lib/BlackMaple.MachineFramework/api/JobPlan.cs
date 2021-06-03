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

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using Germinate;

namespace BlackMaple.MachineFramework
{
  [DataContract]
  public record PathInspection
  {
    [DataMember(IsRequired = true)]
    public string InspectionType { get; init; } = "";

    //There are two possible ways of triggering an inspection: counts and frequencies.
    // * For counts, the MaxVal will contain a number larger than zero and RandomFreq will contain -1
    // * For frequencies, the value of MaxVal is -1 and RandomFreq contains
    //   the frequency as a number between 0 and 1.

    //Every time a material completes, the counter string is expanded (see below).
    [DataMember(IsRequired = true)]
    public string Counter { get; init; } = "";

    //For each completed material, the counter is incremented.  If the counter is equal to MaxVal,
    //we signal an inspection and reset the counter to 0.
    [DataMember(IsRequired = true)]
    public int MaxVal { get; init; }

    //The random frequency of inspection
    [DataMember(IsRequired = true)]
    public double RandomFreq { get; init; }

    //If the last inspection signaled for this counter was longer than TimeInterval,
    //signal an inspection.  This can be disabled by using TimeSpan.Zero
    [DataMember(IsRequired = true)]
    public TimeSpan TimeInterval { get; init; }

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

  [DataContract, Draftable]
  public record MachiningStop
  {
    [DataMember(Name = "StationGroup", IsRequired = true)]
    public string StationGroup { get; init; } = "";

    [DataMember(Name = "StationNums", IsRequired = true)]
    public ImmutableList<int> Stations { get; init; } = ImmutableList<int>.Empty;

    [DataMember(Name = "Program", IsRequired = false)]
    public string? Program { get; init; }

    // During Download:
    //   * A null or zero revision value means use the latest program, either the one already in the cell controller
    //     or the most recent revision in the database.
    //   * A positive revision number will use this specified revision which must exist in the database or
    //     be included in the Programs field of the NewJobs structure accompaning the downloaded job.
    //   * A negative revision number must match a ProgramEntry in the NewJobs structure accompaning the download.
    //     The ProgramEntry will be assigned a (positive) revision during the download and then this stop will
    //     be updated to use that (positive) revision.
    // When loading Jobs,
    //   * A null revision means the program already exists in the cell controller and the DB is not managing programs.
    //   * A positive revision means the program exists in the job DB with this specific revision.
    //   * Negative revisions are never returned (they get translated as part of the download)
    [DataMember(Name = "ProgramRevision", IsRequired = false, EmitDefaultValue = false)]
    public long? ProgramRevision { get; init; }

    [DataMember(Name = "Tools", IsRequired = false, EmitDefaultValue = true)]
    public ImmutableDictionary<string, TimeSpan>? Tools { get; init; } = ImmutableDictionary<string, TimeSpan>.Empty; //key is tool, value is expected cutting time

    [DataMember(Name = "ExpectedCycleTime", IsRequired = true)]
    public TimeSpan ExpectedCycleTime { get; init; }

    public static MachiningStop operator %(MachiningStop s, Action<IMachiningStopDraft> f) => s.Produce(f);
  }

  [DataContract, Draftable]
  public record HoldPattern
  {
    // All of the following hold types are an OR, meaning if any one of them says a hold is in effect,
    // the job is on hold.

    [DataMember(IsRequired = true)]
    public bool UserHold { get; init; }

    [DataMember(IsRequired = true)]
    public string ReasonForUserHold { get; init; } = "";

    //A list of timespans the job should be on hold/not on hold.
    //During the first timespan, the job is on hold.
    [DataMember(IsRequired = true)]
    public ImmutableList<TimeSpan> HoldUnholdPattern { get; init; } = ImmutableList<TimeSpan>.Empty;

    [DataMember(IsRequired = true)]
    public DateTime HoldUnholdPatternStartUTC { get; init; }

    [DataMember(IsRequired = true)]
    public bool HoldUnholdPatternRepeats { get; init; }

  }

  [DataContract, Draftable]
  public record SimulatedProduction
  {
    [DataMember(IsRequired = true)] public DateTime TimeUTC { get; init; }
    [DataMember(IsRequired = true)] public int Quantity { get; init; } //total quantity simulated to be completed at TimeUTC
  }

  [DataContract, Draftable]
  public record ProcessInfo
  {
    [DataMember(Name = "paths", IsRequired = true)]
    public ImmutableList<ProcPathInfo> Paths { get; init; } = ImmutableList<ProcPathInfo>.Empty;

    public static ProcessInfo operator %(ProcessInfo v, Action<IProcessInfoDraft> f) => v.Produce(f);
  }

  [DataContract, Draftable]
  public record ProcPathInfo
  {
    [DataMember(IsRequired = true)]
    public ImmutableList<string> Pallets { get; init; } = ImmutableList<string>.Empty;

    [DataMember(IsRequired = false)]
    public string? Fixture { get; init; }

    [DataMember(IsRequired = false)]
    public int? Face { get; init; }

    [DataMember(IsRequired = true)]
    public ImmutableList<int> Load { get; init; } = ImmutableList<int>.Empty;

    [DataMember(IsRequired = true)]
    public TimeSpan ExpectedLoadTime { get; init; }

    [DataMember(IsRequired = true)]
    public ImmutableList<int> Unload { get; init; } = ImmutableList<int>.Empty;

    [DataMember(IsRequired = true)]
    public TimeSpan ExpectedUnloadTime { get; init; }

    [DataMember(IsRequired = true)]
    public ImmutableList<MachiningStop> Stops { get; init; } = ImmutableList<MachiningStop>.Empty;

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public ImmutableList<SimulatedProduction>? SimulatedProduction { get; init; }

    [DataMember(IsRequired = true)]
    public DateTime SimulatedStartingUTC { get; init; }

    [DataMember(IsRequired = true)]
    public TimeSpan SimulatedAverageFlowTime { get; init; } // average time a part takes to complete the entire sequence

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public HoldPattern? HoldMachining { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public HoldPattern? HoldLoadUnload { get; init; }

    [DataMember(IsRequired = true)]
    public int PartsPerPallet { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public string? InputQueue { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public string? OutputQueue { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public ImmutableList<MachineFramework.PathInspection>? Inspections { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public string? Casting { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = true), Obsolete]
    public int PathGroup { get; init; }


    public static ProcPathInfo operator %(ProcPathInfo v, Action<IProcPathInfoDraft> f) => v.Produce(f);
  }

  [DataContract, Draftable]
  public record Job
  {
    [DataMember(Name = "Unique", IsRequired = true)]
    public string UniqueStr { get; init; } = "";

    [DataMember(Name = "RouteStartUTC", IsRequired = true)]
    public DateTime RouteStartUTC { get; init; }

    [DataMember(Name = "RouteEndUTC", IsRequired = true)]
    public DateTime RouteEndUTC { get; init; }

    [DataMember(Name = "Archived", IsRequired = true)]
    public bool Archived { get; init; }

    [DataMember(Name = "PartName", IsRequired = true)]
    public string PartName { get; init; } = "";

    [DataMember(Name = "Comment", IsRequired = false, EmitDefaultValue = false)]
    public string? Comment { get; init; }

    [DataMember(Name = "Bookings", IsRequired = false, EmitDefaultValue = false)]
    public ImmutableList<string>? BookingIds { get; init; }

    [DataMember(Name = "ManuallyCreated", IsRequired = false)]
    public bool ManuallyCreated { get; init; }

    [DataMember(Name = "HoldEntireJob", IsRequired = false, EmitDefaultValue = false)]
    public HoldPattern? HoldJob { get; init; }

    [DataMember(Name = "CyclesOnFirstProcess", IsRequired = true)]
    public ImmutableList<int> CyclesOnFirstProcess { get; init; } = ImmutableList<int>.Empty;

    [DataMember(Name = "FlexCyclesOnFirstProcessBetweenAllPaths", IsRequired = false, EmitDefaultValue = false)]
    public bool? FlexCyclesOnFirstProcessBetweenAllPaths { get; init; }

    [DataMember(Name = "ProcsAndPaths", IsRequired = true)]
    public ImmutableList<ProcessInfo> Processes { get; init; } = ImmutableList<ProcessInfo>.Empty;

    public static Job operator %(Job j, Action<IJobDraft> f) => j.Produce(f);
  }

  [DataContract]
  public record DecrementQuantity
  {
    [DataMember(IsRequired = true)] public long DecrementId { get; init; }
    [DataMember(IsRequired = true)] public int Proc1Path { get; init; }
    [DataMember(IsRequired = true)] public DateTime TimeUTC { get; init; }
    [DataMember(IsRequired = true)] public int Quantity { get; init; }
  }

  [DataContract, Draftable]
  public record HistoricJob : Job
  {
    [DataMember(Name = "ScheduleId", IsRequired = false, EmitDefaultValue = false)]
    public string? ScheduleId { get; init; }

    [DataMember(Name = "CopiedToSystem", IsRequired = true)]
    public bool CopiedToSystem { get; init; }

    [DataMember(Name = "Decrements", IsRequired = false)]
    public ImmutableList<DecrementQuantity>? Decrements { get; init; }
  }

  [DataContract, Draftable]
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

    public static ActiveJob operator %(ActiveJob j, Action<IActiveJobDraft> f) => j.Produce(f);
  }

  public static class JobAdjustment
  {
    public static Job AdjustPath(this Job job, int proc, int path, Action<IProcPathInfoDraft> f)
    {
      return job.Produce(d => AdjustPath(d, proc, path, f));
    }

    public static void AdjustPath(this IJobDraft job, int proc, int path, Action<IProcPathInfoDraft> f)
    {
      job.Processes[proc - 1] %= procDraft =>
      {
        procDraft.Paths[path - 1] %= f;
      };
    }

    public static Job AdjustAllPaths(this Job job, Action<IProcPathInfoDraft> f)
    {
      return job.Produce(d => AdjustAllPaths(d, f));
    }

    public static void AdjustAllPaths(this IJobDraft job, Action<IProcPathInfoDraft> f)
    {
      for (int proc = 0; proc < job.Processes.Count; proc++)
      {
        job.Processes[proc] %= draftProc =>
        {
          for (int path = 0; path < draftProc.Paths.Count; path++)
          {
            draftProc.Paths[path] %= f;
          }
        };
      }
    }

    public static Job AdjustAllPaths(this Job job, Action<int, int, IProcPathInfoDraft> f)
    {
      return job.Produce(d => AdjustAllPaths(d, f));
    }

    public static void AdjustAllPaths(this IJobDraft job, Action<int, int, IProcPathInfoDraft> f)
    {
      for (int proc = 0; proc < job.Processes.Count; proc++)
      {
        job.Processes[proc] %= draftProc =>
        {
          for (int path = 0; path < draftProc.Paths.Count; path++)
          {
            draftProc.Paths[path] %= p => f(proc + 1, path + 1, p);
          }
        };
      }
    }
  }
}