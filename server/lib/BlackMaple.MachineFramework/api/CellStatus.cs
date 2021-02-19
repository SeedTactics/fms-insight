/* Copyright (c) 2021, John Lenz

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
using System.Runtime.Serialization;
using System.Collections.Immutable;
using Germinate;

namespace BlackMaple.MachineWatchInterface
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
    public DateTime TimeOfCurrentStatusUTC { get; init; }

    [DataMember(Name = "Jobs", IsRequired = true)]
    public ImmutableDictionary<string, InProcessJob> Jobs { get; init; } = ImmutableDictionary<string, InProcessJob>.Empty;

    [DataMember(Name = "Pallets", IsRequired = true)]
    public ImmutableDictionary<string, PalletStatus> Pallets { get; init; } = ImmutableDictionary<string, PalletStatus>.Empty;

    [DataMember(Name = "Material", IsRequired = true)]
    public ImmutableList<InProcessMaterial> Material { get; init; } = ImmutableList<InProcessMaterial>.Empty;

    [DataMember(Name = "Alarms", IsRequired = true)]
    public ImmutableList<string> Alarms { get; init; } = ImmutableList<string>.Empty;

    [DataMember(Name = "Queues", IsRequired = true)]
    public ImmutableDictionary<string, QueueSize> QueueSizes { get; init; } = ImmutableDictionary<string, QueueSize>.Empty;

    public static CurrentStatus operator %(CurrentStatus s, Action<ICurrentStatusDraft> f) => s.Produce(f);
  }

  [DataContract]
  public record JobAndDecrementQuantity
  {
    [DataMember(IsRequired = true)] public long DecrementId { get; init; }
    [DataMember(IsRequired = true)] public string JobUnique { get; init; }
    [DataMember(IsRequired = true)] public int Proc1Path { get; init; }
    [DataMember(IsRequired = true)] public DateTime TimeUTC { get; init; }
    [DataMember(IsRequired = true)] public string Part { get; init; }
    [DataMember(IsRequired = true)] public int Quantity { get; init; }
  }

  [DataContract]
  public record HistoricData
  {
    [DataMember(IsRequired = true)] public ImmutableDictionary<string, MachineFramework.HistoricJob> Jobs { get; init; }
    [DataMember(IsRequired = true)] public ImmutableList<SimulatedStationUtilization> StationUse { get; init; }
  }

  [DataContract]
  public record PlannedSchedule
  {
    [DataMember(IsRequired = true)] public string LatestScheduleId { get; init; }
    [DataMember(IsRequired = true)] public ImmutableList<MachineFramework.HistoricJob> Jobs { get; init; }
    [DataMember(IsRequired = true)] public ImmutableDictionary<string, int> ExtraParts { get; init; }

    [DataMember(IsRequired = false)]
    public ImmutableList<PartWorkorder> CurrentUnfilledWorkorders { get; init; }
  }
}