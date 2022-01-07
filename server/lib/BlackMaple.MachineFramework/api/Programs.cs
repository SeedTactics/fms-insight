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
using System.Runtime.Serialization;
using Germinate;

namespace BlackMaple.MachineFramework
{
  [DataContract]
  public record ProgramInCellController
  {
    [DataMember(IsRequired = true)]
    public string CellControllerProgramName { get; init; } = "";

    [DataMember(IsRequired = true)]
    public string ProgramName { get; init; } = "";

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public long? Revision { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public string? Comment { get; init; }
  }

  [DataContract]
  public record ProgramRevision
  {
    [DataMember(IsRequired = true)]
    public string ProgramName { get; init; } = "";

    [DataMember(IsRequired = true)]
    public long Revision { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public string? Comment { get; init; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public string? CellControllerProgramName { get; init; }
  }

  /// Represents the content of a new program revision which is passed into FMS Insight as part of a new schedule.
  [DataContract]
  public record NewProgramContent
  {
    [DataMember(IsRequired = true)] public string ProgramName { get; init; } = "";
    [DataMember(IsRequired = false, EmitDefaultValue = true)] public string? Comment { get; init; }
    [DataMember(IsRequired = true)] public string ProgramContent { get; init; } = "";

    // * A positive revision number will either add it to the DB with this revision if the revision does
    //   not yet exist, or verify the ProgramContent matches the ProgramContent from the DB if the revision
    //   exists and throw an error if the contents don't match.
    // * A zero revision means allocate a new revision if the program content does not match the most recent
    //   revision in the DB
    // * A negative revision number also allocates a new revision number if the program content does not match
    //   the most recent revision in the DB, and in addition any matching negative numbers in the JobMachiningStop
    //   will be translated to this revision number.
    // * The allocation happens in descending order of Revision, so if multiple negative or zero revisions exist
    //   for the same ProgramName, the one with the largest value will be checked to match the latest revision in
    //   the DB and potentially avoid allocating a new number.  The sorting is on negative numbers, so place
    //   the program entry which is likely to already exist with revision 0 or -1 so that it is the first examined.
    [DataMember(IsRequired = true)] public long Revision { get; init; }
  }

  [DataContract, Draftable]
  public record ProgramForJobStep
  {
    /// <summary>Identifies the process on the part that this program is for.</summary>
    [DataMember(IsRequired = true)]
    public int ProcessNumber { get; init; }

    /// <summary>Identifies which machine stop on the part that this program is for (only needed if a process has multiple
    /// machining stops before unload).  The stop numbers are zero-indexed.</summary>
    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public int? StopIndex { get; init; }

    /// <summary>The program name, used to find the program contents.</summary>
    [DataMember(IsRequired = true)]
    public string ProgramName { get; init; } = "";

    ///<summary>The program revision to run.  Can be negative during download, is treated identically to how the revision
    ///in JobMachiningStop works.</summary>
    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public long? Revision { get; init; }

    public static ProgramForJobStep operator %(ProgramForJobStep w, Action<IProgramForJobStepDraft> f)
       => w.Produce(f);
  }

}