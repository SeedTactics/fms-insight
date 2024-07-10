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

using System.Collections.Immutable;
using Microsoft.AspNetCore.Mvc;

namespace BlackMaple.MachineFramework.Controllers
{
  [ApiController]
  [Route("api/v1/machines")]
  public class MachinesController(IMachineControl mach) : ControllerBase
  {
    [HttpGet("tools")]
    public ImmutableList<ToolInMachine> GetToolsInMachines()
    {
      if (mach == null)
      {
        return [];
      }
      else
      {
        return mach.CurrentToolsInMachines();
      }
    }

    [HttpGet("programs-in-cell-controller")]
    public ImmutableList<ProgramInCellController> GetProgramsInCellController()
    {
      if (mach == null)
      {
        return [];
      }
      else
      {
        return mach.CurrentProgramsInCellController();
      }
    }

    [HttpGet("program/{programName}/revisions")]
    public ImmutableList<ProgramRevision> GetProgramRevisionsInDescendingOrderOfRevision(
      string programName,
      [FromQuery] int count,
      [FromQuery] long? revisionToStart = null
    )
    {
      if (mach == null)
      {
        return [];
      }
      else
      {
        return mach.ProgramRevisionsInDecendingOrderOfRevision(programName, count, revisionToStart);
      }
    }

    [HttpGet("program/{programName}/revision/{revision}/content")]
    public string GetProgramRevisionContent(string programName, long revision)
    {
      if (mach == null)
      {
        return "";
      }
      else
      {
        return mach.GetProgramContent(programName, revision);
      }
    }

    [HttpGet("program/{programName}/latest-revision/content")]
    public string GetLatestProgramRevisionContent(string programName)
    {
      if (mach == null)
      {
        return "";
      }
      else
      {
        return mach.GetProgramContent(programName, null);
      }
    }
  }
}
