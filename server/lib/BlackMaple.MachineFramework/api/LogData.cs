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
using System.Collections.Generic;
using System.Runtime.Serialization;
using Germinate;
using System.Collections.Immutable;

namespace BlackMaple.MachineFramework
{
  [DataContract, Draftable]
  public record LogMaterial
  {
    [DataMember(Name = "id", IsRequired = true)]
    public required long MaterialID { get; init; }

    [DataMember(Name = "uniq", IsRequired = true)]
    public required string JobUniqueStr { get; init; }

    [DataMember(Name = "part", IsRequired = true)]
    public required string PartName { get; init; }

    [DataMember(Name = "proc", IsRequired = true)]
    public required int Process { get; init; }

    [DataMember(Name = "numproc", IsRequired = true)]
    public required int NumProcesses { get; init; }

    [DataMember(Name = "face", IsRequired = true)]
    public required string Face { get; init; }

    [DataMember(Name = "serial", IsRequired = false, EmitDefaultValue = false)]
    public string? Serial { get; init; }

    [DataMember(Name = "workorder", IsRequired = false, EmitDefaultValue = false)]
    public string? Workorder { get; init; }

    public LogMaterial() { }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public LogMaterial(
      long matID,
      string uniq,
      int proc,
      string part,
      int numProc,
      string serial,
      string workorder,
      string face
    )
    {
      MaterialID = matID;
      JobUniqueStr = uniq;
      PartName = part;
      Process = proc;
      NumProcesses = numProc;
      Face = face;
      Serial = serial;
      Workorder = workorder;
    }

    public static LogMaterial operator %(LogMaterial m, Action<ILogMaterialDraft> f) => m.Produce(f);
  }

  [DataContract]
  public enum LogType
  {
    [EnumMember]
    LoadUnloadCycle = 1, //numbers are for backwards compatibility with old type enumeration

    [EnumMember]
    MachineCycle = 2,

    [EnumMember]
    PartMark = 6,

    [EnumMember]
    Inspection = 7,

    [EnumMember]
    OrderAssignment = 10,

    [EnumMember]
    GeneralMessage = 100,

    [EnumMember]
    PalletCycle = 101,

    [EnumMember]
    WorkorderComment = 102,

    [EnumMember]
    InspectionResult = 103,

    [EnumMember]
    CloseOut = 104,

    [EnumMember]
    AddToQueue = 105,

    [EnumMember]
    RemoveFromQueue = 106,

    [EnumMember]
    InspectionForce = 107,

    [EnumMember]
    PalletOnRotaryInbound = 108,

    [EnumMember]
    PalletInStocker = 110,

    [EnumMember]
    SignalQuarantine = 111,

    [EnumMember]
    InvalidateCycle = 112,

    [EnumMember]
    SwapMaterialOnPallet = 113,
    // when adding types, must also update the convertLogType() function in client/backup-viewer/src/background.ts
  }

  [DataContract, Draftable, KnownType(typeof(MaterialProcessActualPath))]
  public record LogEntry
  {
    [DataMember(Name = "counter", IsRequired = true)]
    public required long Counter { get; init; }

    [DataMember(Name = "material", IsRequired = true)]
    public required ImmutableList<LogMaterial> Material { get; init; }

    [DataMember(Name = "type", IsRequired = true)]
    public required LogType LogType { get; init; }

    [DataMember(Name = "startofcycle", IsRequired = true)]
    public required bool StartOfCycle { get; init; }

    [DataMember(Name = "endUTC", IsRequired = true)]
    public required DateTime EndTimeUTC { get; init; }

    [DataMember(Name = "loc", IsRequired = true)]
    public required string LocationName { get; init; }

    [DataMember(Name = "locnum", IsRequired = true)]
    public required int LocationNum { get; init; }

    [DataMember(Name = "pal", IsRequired = true)]
    public required int Pallet { get; init; }

    [DataMember(Name = "program", IsRequired = true)]
    public required string Program { get; init; }

    [DataMember(Name = "result", IsRequired = true)]
    public required string Result { get; init; }

    [DataMember(Name = "elapsed", IsRequired = true)]
    public required TimeSpan ElapsedTime { get; init; } //time from cycle-start to cycle-stop

    [DataMember(Name = "active", IsRequired = true)]
    public required TimeSpan ActiveOperationTime { get; init; } //time that the machining or operation is actually active

    [DataMember(Name = "details", IsRequired = false, EmitDefaultValue = false)]
    public ImmutableDictionary<string, string>? ProgramDetails { get; init; } = null;

    [DataMember(Name = "tooluse", IsRequired = false, EmitDefaultValue = false)]
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

    public bool ShouldSerializeProgramDetails()
    {
      return ProgramDetails != null && ProgramDetails.Count > 0;
    }

    public bool ShouldSerializeTools()
    {
      return Tools != null && Tools.Count > 0;
    }

    public static LogEntry operator %(LogEntry e, Action<ILogEntryDraft> f) => e.Produce(f);
  }

  // stored serialized in json format in the details for inspection logs.
  [DataContract, Draftable]
  public record MaterialProcessActualPath
  {
    [DataContract]
    public record Stop
    {
      [DataMember(IsRequired = true)]
      public required string StationName { get; init; }

      [DataMember(IsRequired = true)]
      public required int StationNum { get; init; }
    }

    [DataMember(IsRequired = true)]
    public required long MaterialID { get; init; }

    [DataMember(IsRequired = true)]
    public required int Process { get; init; }

    [DataMember(IsRequired = true)]
    public required int Pallet { get; init; }

    [DataMember(IsRequired = true)]
    public required int LoadStation { get; init; }

    [DataMember(IsRequired = true)]
    public required ImmutableList<Stop> Stops { get; init; }

    [DataMember(IsRequired = true)]
    public required int UnloadStation { get; init; }

    public static MaterialProcessActualPath operator %(
      MaterialProcessActualPath m,
      Action<IMaterialProcessActualPathDraft> f
    ) => m.Produce(f);
  }

  [DataContract]
  public record EditMaterialInLogEvents
  {
    [DataMember(IsRequired = true)]
    public required long OldMaterialID { get; init; }

    [DataMember(IsRequired = true)]
    public required long NewMaterialID { get; init; }

    [DataMember(IsRequired = true)]
    public required IEnumerable<LogEntry> EditedEvents { get; init; }
  }
}
