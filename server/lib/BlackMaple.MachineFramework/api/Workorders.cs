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
using System.Runtime.Serialization;
using Germinate;
using System.Collections.Immutable;

namespace BlackMaple.MachineFramework
{
  [DataContract, Draftable]
  public record Workorder
  {
    [DataMember(IsRequired = true)]
    public required string WorkorderId { get; init; }

    [DataMember(IsRequired = true)]
    public required string Part { get; init; }

    [DataMember(IsRequired = true)]
    public required int Quantity { get; init; }

    [DataMember(IsRequired = true)]
    public required DateTime DueDate { get; init; }

    [DataMember(IsRequired = true)]
    public required int Priority { get; init; }

    ///<summary>If given, this value overrides the programs to run for this specific workorder.</summary>
    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public ImmutableList<ProgramForJobStep>? Programs { get; init; }

    public static Workorder operator %(Workorder w, Action<IWorkorderDraft> f) => w.Produce(f);
  }

  [DataContract]
  public record WorkorderComment
  {
    [DataMember(IsRequired = true)]
    public required string Comment { get; init; }

    [DataMember(IsRequired = true)]
    public required DateTime TimeUTC { get; init; }
  }

  [DataContract]
  public record ActiveWorkorder
  {
    // original workorder details

    [DataMember(IsRequired = true)]
    public required string WorkorderId { get; init; }

    [DataMember(IsRequired = true)]
    public required string Part { get; init; }

    [DataMember(IsRequired = true)]
    public required int PlannedQuantity { get; init; }

    [DataMember(IsRequired = true)]
    public required DateTime DueDate { get; init; }

    [DataMember(IsRequired = true)]
    public required int Priority { get; init; }

    // active data
    [DataMember(IsRequired = true)]
    public required int CompletedQuantity { get; init; }

    [DataMember(IsRequired = true)]
    public required ImmutableList<string> Serials { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public ImmutableList<WorkorderComment>? Comments { get; init; }

    [DataMember(IsRequired = true)]
    public required ImmutableDictionary<string, TimeSpan> ElapsedStationTime { get; init; }

    [DataMember(IsRequired = true)]
    public required ImmutableDictionary<string, TimeSpan> ActiveStationTime { get; init; }
  }
}
