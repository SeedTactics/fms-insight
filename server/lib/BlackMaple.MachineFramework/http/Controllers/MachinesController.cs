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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BlackMaple.MachineWatchInterface;

namespace BlackMaple.MachineFramework.Controllers
{
  [ApiController]
  [Authorize]
  [Route("api/v1/machines")]
  public class machinesController : ControllerBase
  {
    private IMachineControl _machControl;

    public machinesController(IFMSBackend backend)
    {
      _machControl = backend.MachineControl;
    }

    [HttpGet("tools")]
    public List<ToolInMachine> GetToolsInMachines()
    {
      if (_machControl == null)
      {
        return new List<ToolInMachine>();
      }
      else
      {
        return _machControl.CurrentToolsInMachines();
      }
    }

    [HttpGet("programs-in-cell-controller")]
    public List<ProgramInCellController> GetProgramsInCellController()
    {
      if (_machControl == null)
      {
        return new List<ProgramInCellController>();
      }
      else
      {
        return _machControl.CurrentProgramsInCellController();
      }
    }

    [HttpGet("program/{programName}/revisions")]
    public List<ProgramRevision> GetProgramRevisionsInDescendingOrderOfRevision(string programName, [FromQuery] int count, [FromQuery] long? revisionToStart = null)
    {
      if (_machControl == null)
      {
        return new List<ProgramRevision>();
      }
      else
      {
        return _machControl.ProgramRevisionsInDecendingOrderOfRevision(programName, count, revisionToStart);
      }
    }

    [HttpGet("program/{programName}/revision/{revision}/content")]
    public string GetProgramRevisionContent(string programName, long revision)
    {
      if (_machControl == null)
      {
        return "";
      }
      else
      {
        return _machControl.GetProgramContent(programName, revision);
      }
    }

    [HttpGet("program/{programName}/latest-revision/content")]
    public string GetLatestProgramRevisionContent(string programName)
    {
      if (_machControl == null)
      {
        return "";
      }
      else
      {
        return _machControl.GetProgramContent(programName, null);
      }
    }
  }
}