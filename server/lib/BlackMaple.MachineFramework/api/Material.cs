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
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Collections.Immutable;
using Germinate;

namespace BlackMaple.MachineWatchInterface
{
  ///Stores what is currently happening to a piece of material.
  [DataContract, Draftable]
  public record InProcessMaterialAction
  {
    // This should be a sum type, and while C# sum types can work with some helper code it doesn't work
    // well for serialization.

    [DataContract]
    public enum ActionType
    {
      Waiting = 0,
      Loading,
      UnloadToInProcess, // unload, but keep the material around because more processes must be machined
      UnloadToCompletedMaterial, // unload and the material has been completed
      Machining
    }
    [DataMember(IsRequired = true)] public ActionType Type { get; init; }

    // If Type = Loading
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public string? LoadOntoPallet { get; init; }
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public int? LoadOntoFace { get; init; }
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public int? ProcessAfterLoad { get; init; }
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public int? PathAfterLoad { get; init; }

    //If Type = UnloadToInProcess
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public string? UnloadIntoQueue { get; init; }

    //If Type = Loading or UnloadToInProcess or UnloadToCompletedMaterial
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public TimeSpan? ElapsedLoadUnloadTime { get; init; }

    // If Type = Machining
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public string? Program { get; init; }
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public TimeSpan? ElapsedMachiningTime { get; init; }
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public TimeSpan? ExpectedRemainingMachiningTime { get; init; }
  }

  ///Stores the current location of a piece of material.  If a transfer operation is currently in process
  ///(such as unloading), the location will store the previous location and the action will store the new location.
  [DataContract, Draftable]
  public record InProcessMaterialLocation
  {
    //Again, this should be a sum type.
    [DataContract]
    public enum LocType
    {
      Free = 0,
      OnPallet,
      InQueue,
    }
    [DataMember(IsRequired = true)] public LocType Type { get; init; }

    //If Type == OnPallet
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public string? Pallet { get; init; }
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public int? Face { get; init; }

    //If Type == InQueue
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public string? CurrentQueue { get; init; }

    //If Type == InQueue or Type == Free
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public int? QueuePosition { get; init; }
  }

  //Stores information about a piece of material, where it is, and what is happening to it.
  [DataContract, Draftable]
  public record InProcessMaterial
  {
    // Information about the material
    [DataMember(IsRequired = true)] public long MaterialID { get; init; }
    [DataMember(IsRequired = true)] public string JobUnique { get; init; } = "";
    [DataMember(IsRequired = true)] public string PartName { get; init; } = "";
    [DataMember(IsRequired = true)] public int Process { get; init; }  // When in a queue, the process is the last completed process
    [DataMember(IsRequired = true)] public int Path { get; init; }
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public string? Serial { get; init; }
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public string? WorkorderId { get; init; }
    [DataMember(IsRequired = true)] public ImmutableList<string> SignaledInspections { get; init; } = ImmutableList<string>.Empty;

    // 0-based index into the JobPlan.MachiningStops array for the last completed stop.  Null or negative values
    // indicate no machining stops have yet completed.
    [DataMember(IsRequired = false)] public int? LastCompletedMachiningRouteStopIndex { get; init; }

    // Where is the material?
    [DataMember(IsRequired = true)] public InProcessMaterialLocation Location { get; init; } = new InProcessMaterialLocation();

    // What is currently happening to the material?
    [DataMember(IsRequired = true)] public InProcessMaterialAction Action { get; init; } = new InProcessMaterialAction();

    public static InProcessMaterial operator %(InProcessMaterial m, Action<IInProcessMaterialDraft> f) => m.Produce(f);
  }

  [DataContract]
  public record MaterialDetails
  {
    [DataMember(IsRequired = true)] public long MaterialID { get; init; }
    [DataMember(IsRequired = false, EmitDefaultValue = true)] public string? JobUnique { get; init; }
    [DataMember(IsRequired = true)] public string PartName { get; init; } = "";
    [DataMember(IsRequired = false, EmitDefaultValue = true)] public int NumProcesses { get; init; }
    [DataMember(IsRequired = false, EmitDefaultValue = true)] public string? Workorder { get; init; }
    [DataMember(IsRequired = false, EmitDefaultValue = true)] public string? Serial { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public ImmutableDictionary<int, int>? Paths { get; init; } // key is process, value is path
  }
}