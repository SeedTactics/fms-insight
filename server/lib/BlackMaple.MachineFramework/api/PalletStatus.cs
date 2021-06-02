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

#nullable enable

using System;
using System.Runtime.Serialization;
using System.Collections.Immutable;
using Germinate;

namespace BlackMaple.MachineFramework
{
  [DataContract]
  public enum PalletLocationEnum
  {
    [EnumMember] LoadUnload,
    [EnumMember] Machine,
    [EnumMember] MachineQueue,
    [EnumMember] Buffer,
    [EnumMember] Cart
  }

  [DataContract]
  public record PalletLocation
  {

    [DataMember(Name = "loc", IsRequired = true)]
    public PalletLocationEnum Location { get; init; }

    [DataMember(Name = "group", IsRequired = true)]
    public string StationGroup { get; init; } = "";

    [DataMember(Name = "num", IsRequired = true)]
    public int Num { get; init; }

    public PalletLocation() { }

    public PalletLocation(PalletLocationEnum l, string group, int n)
    {
      Location = l;
      StationGroup = group;
      Num = n;
    }
  }

  [DataContract, Draftable]
  public record PalletStatus
  {
    [DataMember(IsRequired = true)] public string Pallet { get; init; } = "";
    [DataMember(IsRequired = true)] public string FixtureOnPallet { get; init; } = "";
    [DataMember(IsRequired = true)] public bool OnHold { get; init; }
    [DataMember(IsRequired = true)] public PalletLocation CurrentPalletLocation { get; init; } = new PalletLocation(PalletLocationEnum.Buffer, "Buff", 0);

    // If the pallet is at a load station and a new fixture should be loaded, this is filled in.
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public string? NewFixture { get; init; }

    // num faces on new fixture, or current fixture if no change
    [DataMember(IsRequired = true)] public int NumFaces { get; init; }

    //If CurrentPalletLocation is Cart, the following two fields will be filled in.
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public PalletLocation? TargetLocation { get; init; }
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public decimal? PercentMoveCompleted { get; init; }

    public static PalletStatus operator %(PalletStatus s, Action<IPalletStatusDraft> f) => s.Produce(f);
  }
}