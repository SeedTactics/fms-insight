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
using System.Collections.Immutable;

namespace BlackMaple.MachineFramework
{
  public enum QueueRole
  {
    RawMaterial,
    InProcessTransfer,
    Quarantine,
    Other,
  }

  public record QueueInfo
  {
    //once an output queue grows to this size, stop unloading parts
    //and keep them in the buffer inside the cell
    public int? MaxSizeBeforeStopUnloading { get; init; }

    public QueueRole? Role { get; init; }
  }

  public record CurrentStatus
  {
    public required DateTime TimeOfCurrentStatusUTC { get; init; }

    public required ImmutableDictionary<string, MachineFramework.ActiveJob> Jobs { get; init; }

    public required ImmutableDictionary<int, PalletStatus> Pallets { get; init; }

    public required ImmutableList<InProcessMaterial> Material { get; init; }

    public required ImmutableList<string> Alarms { get; init; }

    public required ImmutableDictionary<string, QueueInfo> Queues { get; init; }

    // The following is only filled in if the machines move to the load station
    // instead of the pallets moving to the machine
    public ImmutableList<MachineLocation>? MachineLocations { get; init; }

    public ImmutableList<ActiveWorkorder>? Workorders { get; init; } = null;
  }

  public record JobAndDecrementQuantity
  {
    public required long DecrementId { get; init; }

    public required string JobUnique { get; init; }

    public required DateTime TimeUTC { get; init; }

    public required string Part { get; init; }

    public required int Quantity { get; init; }
  }

  public record Rebooking
  {
    public required string BookingId { get; init; }
    public required string PartName { get; init; }
    public required int Quantity { get; init; }
    public required DateTime TimeUTC { get; init; }
    public int? Priority { get; init; }
    public string? Notes { get; init; }
    public string? Workorder { get; init; }
  }

  public record HistoricData
  {
    public required ImmutableDictionary<string, MachineFramework.HistoricJob> Jobs { get; init; }

    public required ImmutableList<SimulatedStationUtilization> StationUse { get; init; }
  }

  public record RecentHistoricData : HistoricData
  {
    public string? MostRecentSimulationId { get; init; }

    public ImmutableList<SimulatedDayUsage>? MostRecentSimDayUsage { get; init; }
  }

  public record MostRecentSchedule
  {
    public required string LatestScheduleId { get; init; }

    public required ImmutableList<MachineFramework.HistoricJob> Jobs { get; init; }

    public required ImmutableDictionary<string, int> ExtraParts { get; init; }

    public ImmutableList<Rebooking>? UnscheduledRebookings { get; init; }
  }
}
