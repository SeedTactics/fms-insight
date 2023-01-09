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

namespace BlackMaple.MachineFramework
{
  [DataContract]
  public record ToolSnapshot
  {
    [DataMember(IsRequired = true)]
    public required int Pocket { get; init; }

    [DataMember(IsRequired = true)]
    public required string ToolName { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public string? Serial { get; init; }

    // The usage can optionally be tracked by time and if so the time and lifetime are kept in these fields
    [DataMember(IsRequired = false, EmitDefaultValue = true)] // default value = true for backards compatibility
    public TimeSpan? CurrentUse { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public TimeSpan? TotalLifeTime { get; init; }

    // The usage can also optionally be tracked by a count of the number of uses (number of times tool is loaded into the spindle),
    // and if so the current count and lifetime are kept in these fields
    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public int? CurrentUseCount { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public int? TotalLifeCount { get; init; }
  }

  [DataContract]
  public record ToolInMachine : ToolSnapshot
  {
    [DataMember(IsRequired = true)]
    public required string MachineGroupName { get; init; }

    [DataMember(IsRequired = true)]
    public required int MachineNum { get; init; }
  }

  [DataContract, Draftable]
  public record ToolUse
  {
    [DataMember(IsRequired = true)]
    public required string Tool { get; init; }

    [DataMember(IsRequired = true)]
    public required int Pocket { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public bool? ToolChangeOccurred { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public string? ToolSerialAtStartOfCycle { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public string? ToolSerialAtEndOfCycle { get; init; }

    // The usage can optionally be tracked by time and if so the time and lifetime are kept in these fields
    [DataMember(IsRequired = false, EmitDefaultValue = true)] // emit default value for backwards compatibility
    public TimeSpan? ToolUseDuringCycle { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = true)] // emit default value for backwards compatibility
    public TimeSpan? TotalToolUseAtEndOfCycle { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public TimeSpan? ConfiguredToolLife { get; init; }

    // The usage can also optionally be tracked by a count of the number of uses (number of times tool is loaded into the spindle),
    // and if so the current count and lifetime are kept in these fields
    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public int? ToolUseCountDuringCycle { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public int? TotalToolUseCountAtEndOfCycle { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public int? ConfiguredToolLifeCount { get; init; }

    public static ToolUse operator %(ToolUse t, Action<IToolUseDraft> f) => t.Produce(f);
  }
}
