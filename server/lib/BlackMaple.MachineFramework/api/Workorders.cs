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
using System.Text.Json.Serialization;

namespace BlackMaple.MachineFramework
{
  public record Workorder
  {
    public required string WorkorderId { get; init; }

    public required string Part { get; init; }

    public required int Quantity { get; init; }

    public required DateTime DueDate { get; init; }

    public required int Priority { get; init; }

    ///<summary>If given, this value overrides the programs to run for this specific workorder.</summary>
    public ImmutableList<ProgramForJobStep>? Programs { get; init; }
  }

  public record WorkorderSimFilled
  {
    public required string WorkorderId { get; init; }
    public required string Part { get; init; }
    public DateOnly? Started { get; init; }
    public DateOnly? Filled { get; init; }
  }

  public record WorkorderComment
  {
    public required string Comment { get; init; }

    public required DateTime TimeUTC { get; init; }
  }

  public enum WorkorderSerialCloseout
  {
    None,
    ClosedOut,
    CloseOutFailed,
  }

  public record WorkorderMaterial
  {
    public required long MaterialID { get; init; }
    public string? Serial { get; init; }
    public required bool Quarantined { get; init; }
    public required bool InspectionFailed { get; init; }
    public required WorkorderSerialCloseout Closeout { get; init; }
  }

  public record ActiveWorkorder
  {
    // original workorder details

    public required string WorkorderId { get; init; }

    public required string Part { get; init; }

    public required int PlannedQuantity { get; init; }

    public required DateTime DueDate { get; init; }

    public required int Priority { get; init; }

    // active data
    public DateOnly? SimulatedStart { get; init; }

    public DateOnly? SimulatedFilled { get; init; }

    public required int CompletedQuantity { get; init; }

    public ImmutableList<WorkorderComment>? Comments { get; init; }

    public required ImmutableDictionary<string, TimeSpan> ElapsedStationTime { get; init; }

    public required ImmutableDictionary<string, TimeSpan> ActiveStationTime { get; init; }

    public ImmutableList<WorkorderMaterial>? Material { get; init; }

    [
      Obsolete("Use Material instead, serials is written to the JSON for backwards compatibility"),
      JsonInclude
    ]
    protected ImmutableList<object> Serials => [];
  }
}
