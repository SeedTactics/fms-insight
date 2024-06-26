/* Copyright (c) 2024, John Lenz

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
using System.Collections.Immutable;
using System.Data;
using System.Linq;
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Makino;

public class MakinoMachines(IMakinoDB db) : IMachineControl
{
  public ImmutableList<ProgramInCellController> CurrentProgramsInCellController()
  {
    return db.CurrentProgramsInCellController();
  }

  public ImmutableList<ToolInMachine> CurrentToolsInMachine(string machineGroup, int machineNum)
  {
    if (!string.IsNullOrEmpty(machineGroup) && machineNum > 0)
    {
      return db.AllTools(machineGroup, machineNum);
    }
    else
    {
      return [];
    }
  }

  public ImmutableList<ToolInMachine> CurrentToolsInMachines()
  {
    return db.AllTools();
  }

  public ImmutableList<ProgramRevision> ProgramRevisionsInDecendingOrderOfRevision(
    string programName,
    int count,
    long? revisionToStart
  )
  {
    return ImmutableList<ProgramRevision>.Empty;
  }

  public string GetProgramContent(string programName, long? revision)
  {
    return "";
  }
}
