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
using System.Collections.Immutable;
using Germinate;

namespace BlackMaple.MachineFramework
{
  [DataContract]
  public record QueueSize
  {
    //once an output queue grows to this size, stop unloading parts
    //and keep them in the buffer inside the cell
    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public int? MaxSizeBeforeStopUnloading { get; init; }
  }

  [DataContract, Draftable]
  public record CurrentStatus
  {
    [DataMember(IsRequired = true)]
    public required DateTime TimeOfCurrentStatusUTC { get; init; }

    [DataMember(Name = "Jobs", IsRequired = true)]
    public required ImmutableDictionary<string, MachineFramework.ActiveJob> Jobs { get; init; }

    [DataMember(Name = "Pallets", IsRequired = true)]
    public required ImmutableDictionary<string, PalletStatus> Pallets { get; init; }

    [DataMember(Name = "Material", IsRequired = true)]
    public required ImmutableList<InProcessMaterial> Material { get; init; }

    [DataMember(Name = "Alarms", IsRequired = true)]
    public required ImmutableList<string> Alarms { get; init; }

    [DataMember(Name = "Queues", IsRequired = true)]
    public required ImmutableDictionary<string, QueueSize> QueueSizes { get; init; }

    // The following is only filled in if the machines move to the load station
    // instead of the pallets moving to the machine
    [DataMember(Name = "MachineLocations", IsRequired = false, EmitDefaultValue = false)]
    public ImmutableList<MachineLocation>? MachineLocations { get; init; }

    public static CurrentStatus operator %(CurrentStatus s, Action<ICurrentStatusDraft> f) => s.Produce(f);
  }

  [DataContract]
  public record JobAndDecrementQuantity
  {
    [DataMember(IsRequired = true)]
    public required long DecrementId { get; init; }

    [DataMember(IsRequired = true)]
    public required string JobUnique { get; init; }

    [DataMember(IsRequired = true)]
    public required DateTime TimeUTC { get; init; }

    [DataMember(IsRequired = true)]
    public required string Part { get; init; }

    [DataMember(IsRequired = true)]
    public required int Quantity { get; init; }
  }

  [DataContract]
  public record HistoricData
  {
    [DataMember(IsRequired = true)]
    public required ImmutableDictionary<string, MachineFramework.HistoricJob> Jobs { get; init; }

    [DataMember(IsRequired = true)]
    public required ImmutableList<SimulatedStationUtilization> StationUse { get; init; }
  }

  [DataContract]
  public record PlannedSchedule
  {
    [DataMember(IsRequired = true)]
    public required string LatestScheduleId { get; init; }

    [DataMember(IsRequired = true)]
    public required ImmutableList<MachineFramework.HistoricJob> Jobs { get; init; }

    [DataMember(IsRequired = true)]
    public required ImmutableDictionary<string, int> ExtraParts { get; init; }

    [DataMember(IsRequired = false)]
    public ImmutableList<Workorder>? CurrentUnfilledWorkorders { get; init; }
  }
}
