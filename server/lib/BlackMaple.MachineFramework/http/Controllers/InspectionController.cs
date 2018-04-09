/* Copyright (c) 2018, John Lenz

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

using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using BlackMaple.MachineWatchInterface;
using BlackMaple.MachineFramework;

namespace MachineWatchApiServer.Controllers
{
    [Route("api/v1/[controller]")]
    public class inspectionController : ControllerBase
    {
        private IInspectionControl _server;

        public inspectionController(IFMSBackend backend)
        {
            _server = backend.InspectionControl();
        }

        [HttpPut ("material/{materialID}/{inspectionType}")]
        public void ForceInspection(long materialID, string inspectionType)
        {
            _server.ForceInspection(materialID, inspectionType);
        }

        [HttpPut ("next-piece/{inspectionType}")]
        public void NextPieceInspection(string inspectionType, [FromBody] PalletLocation loc)
        {
            _server.NextPieceInspection(loc, inspectionType);
        }


        [HttpGet("counts")]
        public List<InspectCount> GetCounts()
        {
            return _server.LoadInspectCounts();
        }

        [HttpPut("counts")]
        public void SetCounts([FromBody] List<InspectCount> newCounts)
        {
            _server.SetInspectCounts(newCounts);
        }

        /*
        [HttpGet ("types")]
        public List<string> GetGlobalInspectionTypes()
        {
            return _server.LoadGlobalInspectionTypes();
        }

        [HttpGet ("type/{ty}")]
        public InspectionType GetGlobalInspectionType(string ty)
        {
            return _server.LoadGlobalInspectionType(ty);
        }

        [HttpPut ("type/{ty}")]
        public void SetGlobalInspection(string ty, [FromBody] InspectionType ity)
        {
            _server.SetGlobalInspectionType(ity);
        }

        [HttpDelete ("type/{ty}")]
        public void DeleteGlobalInspection(string ty)
        {
            _server.DeleteGlobalInspectionType(ty);
        }
        */

    }
}