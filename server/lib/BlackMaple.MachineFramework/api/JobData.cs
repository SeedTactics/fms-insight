/* Copyright (c) 2020, John Lenz

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
using System.Runtime.Serialization;

namespace BlackMaple.MachineWatchInterface
{

  [DataContract]
  public record PathInspection
  {
    [DataMember(IsRequired = true)]
    public string InspectionType { get; init; }

    //There are two possible ways of triggering an inspection: counts and frequencies.
    // * For counts, the MaxVal will contain a number larger than zero and RandomFreq will contain -1
    // * For frequencies, the value of MaxVal is -1 and RandomFreq contains
    //   the frequency as a number between 0 and 1.

    //Every time a material completes, the counter string is expanded (see below).
    [DataMember(IsRequired = true)]
    public string Counter { get; init; }

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


  [DataContract, Germinate.Draftable]
  public record SimulatedStationUtilization
  {
    [DataMember(IsRequired = true)] public string ScheduleId { get; init; }
    [DataMember(IsRequired = true)] public string StationGroup { get; init; }
    [DataMember(IsRequired = true)] public int StationNum { get; init; }
    [DataMember(IsRequired = true)] public DateTime StartUTC { get; init; }
    [DataMember(IsRequired = true)] public DateTime EndUTC { get; init; }
    [DataMember(IsRequired = true)] public TimeSpan UtilizationTime { get; init; } //time between StartUTC and EndUTC the station is busy.
    [DataMember(IsRequired = true)] public TimeSpan PlannedDownTime { get; init; } //time between StartUTC and EndUTC the station is planned to be down.

    public static SimulatedStationUtilization operator %(SimulatedStationUtilization s, Action<Germinate.ISimulatedStationUtilizationDraft> f)
       => Germinate.Producer.Produce(s, f);
  }

  [DataContract, Germinate.Draftable]
  public record WorkorderProgram
  {
    /// <summary>Identifies the process on the part that this program is for.</summary>
    [DataMember]
    public int ProcessNumber { get; init; }

    /// <summary>Identifies which machine stop on the part that this program is for (only needed if a process has multiple
    /// machining stops before unload).  The stop numbers are zero-indexed.</summary>
    [DataMember]
    public int? StopIndex { get; init; }

    /// <summary>The program name, used to find the program contents.</summary>
    [DataMember]
    public string ProgramName { get; init; }

    ///<summary>The program revision to run.  Can be negative during download, is treated identically to how the revision
    ///in JobMachiningStop works.</summary>
    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public long? Revision { get; init; }

    public static WorkorderProgram operator %(WorkorderProgram w, Action<Germinate.IWorkorderProgramDraft> f)
       => Germinate.Producer.Produce(w, f);
  }

  [DataContract, Germinate.Draftable]
  public record PartWorkorder
  {
    [DataMember(IsRequired = true)] public string WorkorderId { get; init; }
    [DataMember(IsRequired = true)] public string Part { get; init; }
    [DataMember(IsRequired = true)] public int Quantity { get; init; }
    [DataMember(IsRequired = true)] public DateTime DueDate { get; init; }
    [DataMember(IsRequired = true)] public int Priority { get; init; }

    ///<summary>If given, this value overrides the programs to run for this specific workorder.</summary>
    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public ImmutableList<WorkorderProgram> Programs { get; init; }

    public static PartWorkorder operator %(PartWorkorder w, Action<Germinate.IPartWorkorderDraft> f)
       => Germinate.Producer.Produce(w, f);
  }

  [DataContract]
  public record QueueSize
  {
    //once an output queue grows to this size, stop unloading parts
    //and keep them in the buffer inside the cell
    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public int? MaxSizeBeforeStopUnloading { get; init; }
  }

  [DataContract]
  public record ProgramEntry
  {
    [DataMember(IsRequired = true)] public string ProgramName { get; init; }
    [DataMember(IsRequired = true)] public string Comment { get; init; }
    [DataMember(IsRequired = true)] public string ProgramContent { get; init; }

    // * A positive revision number will either add it to the DB with this revision if the revision does
    //   not yet exist, or verify the ProgramContent matches the ProgramContent from the DB if the revision
    //   exists and throw an error if the contents don't match.
    // * A zero revision means allocate a new revision if the program content does not match the most recent
    //   revision in the DB
    // * A negative revision number also allocates a new revision number if the program content does not match
    //   the most recent revision in the DB, and in addition any matching negative numbers in the JobMachiningStop
    //   will be translated to this revision number.
    // * The allocation happens in descending order of Revision, so if multiple negative or zero revisions exist
    //   for the same ProgramName, the one with the largest value will be checked to match the latest revision in
    //   the DB and potentially avoid allocating a new number.  The sorting is on negative numbers, so place
    //   the program entry which is likely to already exist with revision 0 or -1 so that it is the first examined.
    [DataMember(IsRequired = true)] public long Revision { get; init; }
  }

  [DataContract, Germinate.Draftable]
  public record NewJobs
  {
    [DataMember(IsRequired = true)]
    public string ScheduleId { get; init; }

    [DataMember(IsRequired = true)]
    public ImmutableList<JobPlan> Jobs { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public ImmutableList<SimulatedStationUtilization> StationUse { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public ImmutableDictionary<string, int> ExtraParts { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public ImmutableList<PartWorkorder> CurrentUnfilledWorkorders { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public ImmutableList<ProgramEntry> Programs { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public byte[] DebugMessage { get; init; }

    [DataMember(IsRequired = false)]
    public bool ArchiveCompletedJobs { get; init; } = true;

    public static NewJobs operator %(NewJobs j, Action<Germinate.INewJobsDraft> f)
       => Germinate.Producer.Produce(j, f);
  }

  [DataContract]
  public record DecrementQuantity
  {
    [DataMember(IsRequired = true)] public long DecrementId { get; init; }
    [DataMember(IsRequired = true)] public int Proc1Path { get; init; }
    [DataMember(IsRequired = true)] public DateTime TimeUTC { get; init; }
    [DataMember(IsRequired = true)] public int Quantity { get; init; }
  }

  [DataContract]
  public class HistoricJob : JobPlan
  {
    public HistoricJob(string unique, int numProc, int[] numPaths = null) : base(unique, numProc, numPaths) { }
    public HistoricJob(JobPlan job) : base(job) { }
    private HistoricJob() { } //for json deserialization

    [DataMember(Name = "Decrements", IsRequired = false)] public List<DecrementQuantity> Decrements { get; set; }
  }

  [DataContract]
  public record HistoricData
  {
    [DataMember(IsRequired = true)] public ImmutableDictionary<string, HistoricJob> Jobs { get; init; }
    [DataMember(IsRequired = true)] public ImmutableList<SimulatedStationUtilization> StationUse { get; init; }
  }

  [DataContract]
  public record PlannedSchedule
  {
    [DataMember(IsRequired = true)] public string LatestScheduleId { get; init; }
    [DataMember(IsRequired = true)] public ImmutableList<JobPlan> Jobs { get; init; }
    [DataMember(IsRequired = true)] public ImmutableDictionary<string, int> ExtraParts { get; init; }

    [DataMember(IsRequired = false)]
    public ImmutableList<PartWorkorder> CurrentUnfilledWorkorders { get; init; }
  }
}
