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

namespace BlackMaple.MachineFramework
{
  public record ToolSnapshot
  {
    public required int Pocket { get; init; }

    public required string ToolName { get; init; }

    public string? Serial { get; init; }

    // The usage can optionally be tracked by time and if so the time and lifetime are kept in these fields
    public TimeSpan? CurrentUse { get; init; }

    public TimeSpan? TotalLifeTime { get; init; }

    // The usage can also optionally be tracked by a count of the number of uses (number of times tool is loaded into the spindle),
    // and if so the current count and lifetime are kept in these fields
    public int? CurrentUseCount { get; init; }

    public int? TotalLifeCount { get; init; }
  }

  public record ToolInMachine : ToolSnapshot
  {
    public required string MachineGroupName { get; init; }

    public required int MachineNum { get; init; }
  }

  public record ToolUse
  {
    public required string Tool { get; init; }

    public required int Pocket { get; init; }

    public bool? ToolChangeOccurred { get; init; }

    public string? ToolSerialAtStartOfCycle { get; init; }

    public string? ToolSerialAtEndOfCycle { get; init; }

    // The usage can optionally be tracked by time and if so the time and lifetime are kept in these fields
    public TimeSpan? ToolUseDuringCycle { get; init; }

    public TimeSpan? TotalToolUseAtEndOfCycle { get; init; }

    public TimeSpan? ConfiguredToolLife { get; init; }

    // The usage can also optionally be tracked by a count of the number of uses (number of times tool is loaded into the spindle),
    // and if so the current count and lifetime are kept in these fields
    public int? ToolUseCountDuringCycle { get; init; }

    public int? TotalToolUseCountAtEndOfCycle { get; init; }

    public int? ConfiguredToolLifeCount { get; init; }
  }
}
