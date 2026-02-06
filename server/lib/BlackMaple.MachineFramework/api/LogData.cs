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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace BlackMaple.MachineFramework
{
  public record LogMaterial
  {
    [JsonPropertyName("id")]
    public required long MaterialID { get; init; }

    [JsonPropertyName("uniq")]
    public required string JobUniqueStr { get; init; }

    [JsonPropertyName("part")]
    public required string PartName { get; init; }

    [JsonPropertyName("proc")]
    public required int Process { get; init; }

    [JsonPropertyName("path")]
    public int? Path { get; init; }

    [JsonPropertyName("numproc")]
    public required int NumProcesses { get; init; }

    [JsonPropertyName("face")]
    public required int Face { get; init; }

    [JsonPropertyName("serial")]
    public string? Serial { get; init; }

    [JsonPropertyName("workorder")]
    public string? Workorder { get; init; }
  }

  public enum LogType
  {
    LoadUnloadCycle = 1, //numbers are for backwards compatibility with old type enumeration

    MachineCycle = 2,

    PartMark = 6,

    Inspection = 7,

    OrderAssignment = 10,

    GeneralMessage = 100,

    PalletCycle = 101,

    WorkorderComment = 102,

    InspectionResult = 103,

    CloseOut = 104,

    AddToQueue = 105,

    RemoveFromQueue = 106,

    InspectionForce = 107,

    PalletOnRotaryInbound = 108,

    PalletInStocker = 110,

    SignalQuarantine = 111,

    InvalidateCycle = 112,

    SwapMaterialOnPallet = 113,

    Rebooking = 114,
    CancelRebooking = 115,
    BasketLoadUnload = 116,
    BasketCycle = 117,
    BasketInLocation = 118,
    // when adding types, must also update the display in client/insight/src/components/LogEntry.tsx
  }

  [KnownType(typeof(MaterialProcessActualPath))]
  public record LogEntry
  {
    [JsonPropertyName("counter")]
    public required long Counter { get; init; }

    [JsonPropertyName("material")]
    public required ImmutableList<LogMaterial> Material { get; init; }

    [JsonPropertyName("type")]
    public required LogType LogType { get; init; }

    [JsonPropertyName("startofcycle")]
    public required bool StartOfCycle { get; init; }

    [JsonPropertyName("endUTC")]
    public required DateTime EndTimeUTC { get; init; }

    [JsonPropertyName("loc")]
    public required string LocationName { get; init; }

    [JsonPropertyName("locnum")]
    public required int LocationNum { get; init; }

    [JsonPropertyName("pal")]
    public required int Pallet { get; init; }

    [JsonPropertyName("program")]
    public required string Program { get; init; }

    [JsonPropertyName("result")]
    public required string Result { get; init; }

    [JsonPropertyName("elapsed")]
    public required TimeSpan ElapsedTime { get; init; } //time from cycle-start to cycle-stop

    [JsonPropertyName("active")]
    public required TimeSpan ActiveOperationTime { get; init; } //time that the machining or operation is actually active

    [JsonPropertyName("details")]
    public ImmutableDictionary<string, string>? ProgramDetails { get; init; } = null;

    [JsonPropertyName("tooluse")]
    public ImmutableList<ToolUse>? Tools { get; init; } = null;

    public LogEntry() { }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public LogEntry(
      long cntr,
      IEnumerable<LogMaterial> mat,
      int pal,
      LogType ty,
      string locName,
      int locNum,
      string prog,
      bool start,
      DateTime endTime,
      string result,
      TimeSpan? elapsed = null,
      TimeSpan? active = null
    )
    {
      Counter = cntr;
      Material = mat.ToImmutableList();
      Pallet = pal;
      LogType = ty;
      LocationName = locName;
      LocationNum = locNum;
      Program = prog;
      StartOfCycle = start;
      EndTimeUTC = endTime;
      Result = result;
      ElapsedTime = elapsed ?? TimeSpan.FromMinutes(-1);
      ActiveOperationTime = active ?? TimeSpan.Zero;
      ProgramDetails = null;
      Tools = null;
    }
  }

  // stored serialized in json format in the details for inspection logs.
  public record MaterialProcessActualPath
  {
    public record Stop
    {
      public required string StationName { get; init; }

      public required int StationNum { get; init; }
    }

    public required long MaterialID { get; init; }

    public required int Process { get; init; }

    public required int Pallet { get; init; }

    public required int LoadStation { get; init; }

    public required ImmutableList<Stop> Stops { get; init; }

    public required int UnloadStation { get; init; }
  }

  public record EditMaterialInLogEvents
  {
    public required long OldMaterialID { get; init; }

    public required long NewMaterialID { get; init; }

    public required IEnumerable<LogEntry> EditedEvents { get; init; }
  }
}
