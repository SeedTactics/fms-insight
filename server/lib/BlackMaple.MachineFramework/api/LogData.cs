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
    BasketContentSnapshot = 120,
    BasketRegionSurvey = 124,
    BasketMisload = 125,
    BasketMisloadResolution = 126,

    // 119, 121, 122, and 123 belonged to beta-only basket event types. Keep those values unused.
    BasketObservation = 127,
    BasketObservationCorrection = 128,
    // when adding types, must also update the display in client/insight/src/components/LogEntry.tsx
  }

  public enum BasketEvidenceSourceKind
  {
    Operator,
    Sensor,
    Integration,
  }

  public sealed record BasketEvidenceSource
  {
    public required BasketEvidenceSourceKind Kind { get; init; }
    public required string Name { get; init; }
  }

  /// <summary>
  /// Direct evidence that a numbered basket was observed at a physical position. Content episode
  /// IDs are included only when the recorder has direct physical continuity to one uniquely
  /// tracked occupant; their absence makes no content-identity claim. Integration sources should
  /// record an observation only when they possess the underlying physical or external evidence for
  /// the claim. A calculated best-fit reconstruction is not itself an observation.
  /// </summary>
  public sealed record BasketObservation
  {
    public required Guid ObservationId { get; init; }
    public required int BasketId { get; init; }
    public required BasketPosition Position { get; init; }
    public ImmutableSortedSet<Guid> ContentEpisodeIds { get; init; } = [];
    public required BasketEvidenceSource Source { get; init; }
    public string? Note { get; init; }
    public string? CorrelationId { get; init; }
    public required DateTime TimeUTC { get; init; }
    public required long EventCounter { get; init; }
  }

  /// <summary>
  /// The active claims currently supported by one immutable basket observation. Position evidence
  /// and content continuity can have different lifetimes, so an older observation may remain here
  /// only because it still owns active content episode claims.
  /// </summary>
  public sealed record ActiveBasketObservationEvidence
  {
    /// <summary>The original immutable historical observation.</summary>
    public required BasketObservation Observation { get; init; }

    /// <summary>
    /// Whether this observation is the currently effective positive position evidence for its
    /// numbered basket. This describes the current evidence projection, not omniscient physical
    /// truth.
    /// </summary>
    public required bool IsCurrentPositionEvidence { get; init; }

    /// <summary>
    /// The content episode claims from <see cref="Observation"/> that remain active through this
    /// observation.
    /// </summary>
    public required ImmutableSortedSet<Guid> ActiveContentEpisodeIds { get; init; }
  }

  public sealed record BasketObservationCorrection
  {
    public required Guid CorrectionId { get; init; }
    public required Guid TargetObservationId { get; init; }
    public Guid? ReplacementObservationId { get; init; }
    public required BasketEvidenceSource Source { get; init; }
    public string? Note { get; init; }
    public string? CorrelationId { get; init; }
    public required DateTime TimeUTC { get; init; }
    public required long EventCounter { get; init; }
  }

  public enum BasketRegionSurveyCompleteness
  {
    Partial,
    Complete,
  }

  public sealed record BasketRegionSurvey
  {
    public required Guid SurveyId { get; init; }
    public required BasketPosition Region { get; init; }
    public required ImmutableSortedSet<int> ObservedBasketIds { get; init; }
    public required int UnidentifiedBasketCount { get; init; }
    public required BasketRegionSurveyCompleteness Completeness { get; init; }
    public required BasketEvidenceSource Source { get; init; }
    public string? CorrelationId { get; init; }
    public required DateTime TimeUTC { get; init; }
    public required long EventCounter { get; init; }
  }

  public sealed record BasketMisload
  {
    public required Guid MisloadId { get; init; }
    public int? BasketId { get; init; }
    public ImmutableSortedSet<Guid> ContentEpisodeIds { get; init; } = [];
    public required BasketPosition DetectedAt { get; init; }
    public required BasketEvidenceSource Source { get; init; }
    public required string Reason { get; init; }
    public string? CorrelationId { get; init; }
    public required DateTime TimeUTC { get; init; }
    public required long EventCounter { get; init; }
  }

  public enum BasketMisloadResolutionKind
  {
    ClearedAfterCorrection,
    ReportedInError,
    Superseded,
  }

  public sealed record BasketMisloadResolution
  {
    public required Guid ResolutionId { get; init; }
    public required Guid MisloadId { get; init; }
    public required BasketMisloadResolutionKind Kind { get; init; }
    public required BasketEvidenceSource Source { get; init; }
    public string? Note { get; init; }
    public string? CorrelationId { get; init; }
    public required DateTime TimeUTC { get; init; }
    public required long EventCounter { get; init; }
  }

  public sealed record EventLogMetadata
  {
    /// External message, observation, command, or source operation that caused the event. One
    /// external input may generate several log events, so this is not necessarily unique and does
    /// not itself provide an idempotency contract.
    public string? ForeignId { get; init; }

    /// Broader workflow or operation grouping events from several external inputs. This is not an
    /// idempotency key.
    public string? CorrelationId { get; init; }

    /// Original message which created the event, retained primarily for auditing.
    public string? OriginalMessage { get; init; }
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

    /// <summary>
    /// Historical pallet number field. Basket events also use this field for a known numbered
    /// basket; unresolved basket content episodes use -1 with
    /// <see cref="BasketContentEpisodeId"/>. Non-basket events retain their ordinary pallet
    /// number here.
    /// </summary>
    [JsonPropertyName("pal")]
    public required int Pallet { get; init; }

    /// <summary>
    /// For basket events whose numbered basket identity is unresolved, identifies the basket
    /// content episode and <see cref="Pallet"/> is -1. Numbered basket events use a positive
    /// <see cref="Pallet"/> and leave this null. Non-basket events leave this null.
    /// </summary>
    [JsonPropertyName("basketContentEpisodeId")]
    public Guid? BasketContentEpisodeId { get; init; }

    /// <summary>
    /// Basket content episodes authoritatively finalized by this numbered cycle-end event.
    /// </summary>
    [JsonPropertyName("basketCycleEndContentEpisodeIds")]
    public ImmutableList<Guid>? BasketCycleEndContentEpisodeIds { get; init; }

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

    [JsonIgnore]
    public string? ForeignID { get; init; } = null;

    [JsonIgnore]
    public string? CorrelationId { get; init; } = null;

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
      BasketContentEpisodeId = null;
      BasketCycleEndContentEpisodeIds = null;
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
      ForeignID = null;
      CorrelationId = null;
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
