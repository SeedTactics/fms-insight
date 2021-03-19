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

#nullable enable

using System;
using System.Runtime.Serialization;
using System.Linq;
using Germinate;
using System.Collections.Immutable;
using System.Collections.Generic;

namespace BlackMaple.MachineWatchInterface
{
  [DataContract, Draftable]
  public record WorkorderPartSummary
  {
    [DataMember(Name = "name", IsRequired = true)]
    public string Part { get; init; } = "";

    [DataMember(Name = "completed-qty", IsRequired = true)]
    public int PartsCompleted { get; init; }

    [DataMember(Name = "elapsed-station-time", IsRequired = true)]
    public ImmutableDictionary<string, TimeSpan> ElapsedStationTime { get; init; } = ImmutableDictionary<string, TimeSpan>.Empty;

    [DataMember(Name = "active-stat-time", IsRequired = true)]
    public ImmutableDictionary<string, TimeSpan> ActiveStationTime { get; init; } = ImmutableDictionary<string, TimeSpan>.Empty;

    public static WorkorderPartSummary operator %(WorkorderPartSummary m, Action<IWorkorderPartSummaryDraft> f)
       => m.Produce(f);
  }

  [DataContract, Draftable]
  public record WorkorderSummary
  {
    [DataMember(Name = "id", IsRequired = true)]
    public string WorkorderId { get; init; } = "";

    [DataMember(Name = "parts", IsRequired = true)]
    public ImmutableList<WorkorderPartSummary> Parts { get; init; } = ImmutableList<WorkorderPartSummary>.Empty;

    [DataMember(Name = "serials", IsRequired = true)]
    public ImmutableList<string> Serials { get; init; } = ImmutableList<string>.Empty;

    [DataMember(Name = "finalized", IsRequired = false, EmitDefaultValue = false)]
    public DateTime? FinalizedTimeUTC { get; init; }

    public static WorkorderSummary operator %(WorkorderSummary m, Action<IWorkorderSummaryDraft> f)
       => m.Produce(f);
  }

  [DataContract, Draftable]
  public record WorkorderProgram
  {
    /// <summary>Identifies the process on the part that this program is for.</summary>
    [DataMember(IsRequired = true)]
    public int ProcessNumber { get; init; }

    /// <summary>Identifies which machine stop on the part that this program is for (only needed if a process has multiple
    /// machining stops before unload).  The stop numbers are zero-indexed.</summary>
    [DataMember(IsRequired = false)]
    public int? StopIndex { get; init; }

    /// <summary>The program name, used to find the program contents.</summary>
    [DataMember(IsRequired = true)]
    public string ProgramName { get; init; } = "";

    ///<summary>The program revision to run.  Can be negative during download, is treated identically to how the revision
    ///in JobMachiningStop works.</summary>
    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public long? Revision { get; init; }

    public static WorkorderProgram operator %(WorkorderProgram w, Action<IWorkorderProgramDraft> f)
       => w.Produce(f);
  }

  [DataContract, Draftable]
  public record PartWorkorder
  {
    [DataMember(IsRequired = true)] public string WorkorderId { get; init; } = "";
    [DataMember(IsRequired = true)] public string Part { get; init; } = "";
    [DataMember(IsRequired = true)] public int Quantity { get; init; }
    [DataMember(IsRequired = true)] public DateTime DueDate { get; init; }
    [DataMember(IsRequired = true)] public int Priority { get; init; }

    ///<summary>If given, this value overrides the programs to run for this specific workorder.</summary>
    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public ImmutableList<WorkorderProgram>? Programs { get; init; } = ImmutableList<WorkorderProgram>.Empty;

    public static PartWorkorder operator %(PartWorkorder w, Action<IPartWorkorderDraft> f)
       => w.Produce(f);
  }

}