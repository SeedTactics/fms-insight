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
using System.Collections.Immutable;
using System.Runtime.Serialization;
using Germinate;

namespace BlackMaple.MachineFramework
{
  [DataContract, Draftable]
  public record SimulatedStationPart
  {
    [DataMember(IsRequired = true)]
    public string JobUnique { get; init; } = "";

    [DataMember(IsRequired = true)]
    public int Process { get; init; } = 1;

    [DataMember(IsRequired = true)]
    public int Path { get; init; } = 1;
  }

  [DataContract, Draftable]
  public record SimulatedStationUtilization
  {
    [DataMember(IsRequired = true)]
    public string ScheduleId { get; init; } = "";

    [DataMember(IsRequired = true)]
    public string StationGroup { get; init; } = "";

    [DataMember(IsRequired = true)]
    public int StationNum { get; init; }

    [DataMember(IsRequired = true)]
    public DateTime StartUTC { get; init; }

    [DataMember(IsRequired = true)]
    public DateTime EndUTC { get; init; }

    [DataMember(IsRequired = true)]
    public TimeSpan UtilizationTime { get; init; } //time between StartUTC and EndUTC the station is busy.

    [DataMember(IsRequired = true)]
    public TimeSpan PlannedDownTime { get; init; } //time between StartUTC and EndUTC the station is planned to be down.

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public ImmutableList<SimulatedStationPart>? Parts { get; init; }

    public static SimulatedStationUtilization operator %(
      SimulatedStationUtilization s,
      Action<ISimulatedStationUtilizationDraft> f
    ) => s.Produce(f);
  }

  [DataContract, Germinate.Draftable]
  public record NewJobs
  {
    [DataMember(IsRequired = true)]
    public string ScheduleId { get; init; } = "";

    [DataMember(IsRequired = true)]
    public ImmutableList<MachineFramework.Job> Jobs { get; init; } =
      ImmutableList<MachineFramework.Job>.Empty;

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public ImmutableList<SimulatedStationUtilization>? StationUse { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public ImmutableDictionary<string, int>? ExtraParts { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public ImmutableList<Workorder>? CurrentUnfilledWorkorders { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public ImmutableList<NewProgramContent>? Programs { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public byte[]? DebugMessage { get; init; }

    public static NewJobs operator %(NewJobs j, Action<INewJobsDraft> f) => j.Produce(f);
  }
}
