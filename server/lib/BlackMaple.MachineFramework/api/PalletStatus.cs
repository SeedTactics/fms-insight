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
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Germinate;

namespace BlackMaple.MachineFramework
{
  public enum PalletLocationEnum
  {
    LoadUnload,

    Machine,

    MachineQueue,

    Buffer,

    Cart
  }

  public record PalletLocation
  {
    [JsonPropertyName("loc")]
    public required PalletLocationEnum Location { get; init; }

    [JsonPropertyName("group")]
    public required string StationGroup { get; init; }

    [JsonPropertyName("num")]
    public required int Num { get; init; }

    public PalletLocation() { }

    [SetsRequiredMembers]
    public PalletLocation(PalletLocationEnum l, string group, int n)
    {
      Location = l;
      StationGroup = group;
      Num = n;
    }
  }

  [Draftable]
  public record PalletStatus
  {
    public required int PalletNum { get; init; }

    public required string FixtureOnPallet { get; init; }

    public required bool OnHold { get; init; }

    public required PalletLocation CurrentPalletLocation { get; init; }

    // If the pallet is at a load station and a new fixture should be loaded, this is filled in.
    public string? NewFixture { get; init; }

    // num faces on new fixture, or current fixture if no change
    public required int NumFaces { get; init; }

    public ImmutableList<string>? FaceNames { get; init; }

    //If CurrentPalletLocation is Cart, the following two fields will be filled in.
    public PalletLocation? TargetLocation { get; init; }

    public decimal? PercentMoveCompleted { get; init; }

    public static PalletStatus operator %(PalletStatus s, Action<IPalletStatusDraft> f) => s.Produce(f);
  }
}
