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
using System.Collections.Immutable;
using Germinate;

namespace BlackMaple.MachineFramework
{
  ///Stores what is currently happening to a piece of material.
  [Draftable]
  public record InProcessMaterialAction
  {
    // This should be a sum type, and while C# sum types can work with some helper code it doesn't work
    // well for serialization.

    public enum ActionType
    {
      Waiting = 0,
      Loading,
      UnloadToInProcess, // unload, but keep the material around because more processes must be machined
      UnloadToCompletedMaterial, // unload and the material has been completed
      Machining
    }

    public required ActionType Type { get; init; }

    // If Type = Loading
    public int? LoadOntoPalletNum { get; init; }

    public int? LoadOntoFace { get; init; }

    public int? ProcessAfterLoad { get; init; }

    public int? PathAfterLoad { get; init; }

    //If Type = UnloadToInProcess
    public string? UnloadIntoQueue { get; init; }

    //If Type = Loading or UnloadToInProcess or UnloadToCompletedMaterial
    public TimeSpan? ElapsedLoadUnloadTime { get; init; }

    // If Type = Machining
    public string? Program { get; init; }

    public TimeSpan? ElapsedMachiningTime { get; init; }

    public TimeSpan? ExpectedRemainingMachiningTime { get; init; }
  }

  ///Stores the current location of a piece of material.  If a transfer operation is currently in process
  ///(such as unloading), the location will store the previous location and the action will store the new location.
  [Draftable]
  public record InProcessMaterialLocation
  {
    //Again, this should be a sum type.
    public enum LocType
    {
      Free = 0,
      OnPallet,
      InQueue,
    }

    public required LocType Type { get; init; }

    //If Type == OnPallet
    public int? PalletNum { get; init; }

    public int? Face { get; init; }

    //If Type == InQueue
    public string? CurrentQueue { get; init; }

    //If Type == InQueue or Type == Free
    public int? QueuePosition { get; init; }
  }

  //Stores information about a piece of material, where it is, and what is happening to it.
  [Draftable]
  public record InProcessMaterial
  {
    // Information about the material
    public required long MaterialID { get; init; }

    public required string JobUnique { get; init; }

    public required string PartName { get; init; }

    public required int Process { get; init; } // When in a queue, the process is the last completed process

    public required int Path { get; init; }

    public string? Serial { get; init; }

    public string? WorkorderId { get; init; }

    public required ImmutableList<string> SignaledInspections { get; init; }

    // 0-based index into the JobPlan.MachiningStops array for the last completed stop.  Null or negative values
    // indicate no machining stops have yet completed.
    public int? LastCompletedMachiningRouteStopIndex { get; init; }

    // Where is the material?
    public required InProcessMaterialLocation Location { get; init; }

    // What is currently happening to the material?
    public required InProcessMaterialAction Action { get; init; }

    public static InProcessMaterial operator %(InProcessMaterial m, Action<IInProcessMaterialDraft> f) =>
      m.Produce(f);
  }

  public record MaterialDetails
  {
    public required long MaterialID { get; init; }

    public string? JobUnique { get; init; }

    public required string PartName { get; init; }

    public int NumProcesses { get; init; }

    public string? Workorder { get; init; }

    public string? Serial { get; init; }

    public ImmutableDictionary<int, int>? Paths { get; init; } // key is process, value is path
  }

  public record ScannedCasting
  {
    public ImmutableList<string>? PossibleCastings { get; init; }

    public ImmutableList<string>? PossibleJobs { get; init; }

    public string? Workorder { get; init; }

    public string? Serial { get; init; }
  }

  // This is a Sum Type, only one of the fields will be non-null
  public record ScannedMaterial
  {
    public MaterialDetails? ExistingMaterial { get; init; }

    public ScannedCasting? Casting { get; init; }
  }
}
