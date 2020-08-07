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

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BlackMaple.MachineWatchInterface
{
  [DataContract]
  public class ToolInMachine
  {
    [DataMember(IsRequired = true)]
    public string MachineGroupName { get; set; }

    [DataMember(IsRequired = true)]
    public int MachineNum { get; set; }

    [DataMember(IsRequired = true)]
    public int Pocket { get; set; }

    [DataMember(IsRequired = true)]
    public string ToolName { get; set; }

    [DataMember(IsRequired = true)]
    public TimeSpan CurrentUse { get; set; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public TimeSpan TotalLifeTime { get; set; }
  }

  [DataContract]
  public class ProgramInCellController
  {
    [DataMember(IsRequired = true)]
    public string MachineGroupName { get; set; }

    [DataMember(IsRequired = true)]
    public string CellControllerProgramName { get; set; }

    [DataMember(IsRequired = true)]
    public string ProgramName { get; set; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public long? Revision { get; set; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public string Comment { get; set; }
  }

  [DataContract]
  public class ProgramRevision
  {
    [DataMember(IsRequired = true)]
    public string ProgramName { get; set; }

    [DataMember(IsRequired = true)]
    public long Revision { get; set; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public string Comment { get; set; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public string CellControllerProgramName { get; set; }
  }

  public interface IMachineControl
  {
    List<ToolInMachine> CurrentToolsInMachines();
    List<ProgramInCellController> CurrentProgramsInCellController();
    List<ProgramRevision> ProgramRevisionsInDecendingOrderOfRevision(string programName, int count, long? revisionToStart);
    string GetProgramContent(string programName, long? revision);
  }
}