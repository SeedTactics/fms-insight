/* Copyright (c) 2019, John Lenz

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
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BlackMaple.MachineWatchInterface;
using System.Runtime.Serialization;

namespace BlackMaple.MachineFramework.Controllers
{
  //[ApiController]
  //[Authorize]
  //[Route("api/unstable/[controller]")]
  public class programsController //: ControllerBase
  {
    private IProgramManagement _m;

    public programsController(IProgramManagement m)
    {
      _m = m;
    }

    [HttpGet("production-programs")]
    public Task<IEnumerable<ProgramRevision>> GetLatestPrograms()
    {
      return _m.GetLatestPrograms();
    }

    [HttpGet("production-program/{programName}/latest")]
    public Task<ProgramRevision> GetLatestProgram(string programName)
    {
      return _m.GetLatestProgram(programName);
    }

    [HttpGet("production-program/{programName}/history")]
    public Task<IEnumerable<ProgramRevision>> GetProgramHistory(string programName, [FromQuery] int skip = 0, [FromQuery] int count = 10)
    {
      if (skip < 0)
        throw new BadRequestException("Skip must be non-negative");
      if (count <= 0)
        throw new BadRequestException("Count must be positive");
      return _m.GetProgramHistory(programName, skip, count);
    }

    [HttpGet("production-program/{programName}/{revisionId}/content")]
    public Task<ProgramContent> GetProgramContent(string programName, string revisionId)
    {
      return _m.GetProgramContent(programName, revisionId);
    }

    [HttpPost("production-program/{programName}")]
    public Task<ProgramRevision> NewProductionProgram(string programName, [FromBody] ProgramVersion program)
    {
      if (program.ProgramName != programName)
        throw new BadRequestException("Program name from path does not match program body");
      return _m.NewProductionRevision(program);
    }



    [HttpGet("branches")]
    public Task<IEnumerable<ProgramFeatureBranch>> GetFeatureBranches()
    {
      return _m.GetFeatureBranches();
    }

    [HttpGet("branch/{branchName}")]
    public Task<ProgramFeatureBranch> GetFeatureBranch(string branchName)
    {
      return _m.GetFeatureBranch(branchName);
    }

    [HttpPost("branch/{branchName}")]
    public Task<ProgramFeatureBranch> CreateFeatureBranch(string branchName)
    {
      return _m.CreateFeatureBranch(branchName);
    }

    [HttpDelete("branch/{branchName}")]
    public Task DeleteFeatureBranch(string branchName)
    {
      return _m.DeleteFeatureBranch(branchName);
    }

    [HttpGet("branch/{branchName}/history")]
    public Task<IEnumerable<ProgramRevision>> GetBranchHistory(string branchName, [FromQuery] int skip = 0, [FromQuery] int count = 20)
    {
      if (skip < 0)
        throw new BadRequestException("Skip must be non-negative");
      if (count <= 0)
        throw new BadRequestException("Count must be positive");
      return _m.GetBranchHistory(branchName, skip, count);
    }

    [HttpPut("branch/{branchName}/release-to-production")]
    public Task ReleaseBranchToProduction(string branchName)
    {
      return _m.ReleaseBranchToProduction(branchName);
    }

    [HttpPost("branch/{branchName}/new-revision")]
    public Task<ProgramRevision> NewRevisionOnBranch(string branchName, [FromBody] ProgramVersion program)
    {
      return _m.NewRevisionOnBranch(branchName, program);
    }

  }
}