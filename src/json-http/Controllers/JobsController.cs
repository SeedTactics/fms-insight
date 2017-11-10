/* Copyright (c) 2017, John Lenz

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
using BlackMaple.MachineWatchInterface;

namespace MachineWatchApiServer.Controllers
{
    [Route("api/[controller]")]
    public class JobsController : Controller
    {
        private IJobServerV2 _server;

        public JobsController(IServerBackend backend)
        {
            _server = backend.JobServer();
        }

        [HttpGet]
        public CurrentStatus Get()
        {
            return _server.GetCurrentStatus();
        }

        [HttpGet("history")]
        public HistoricData History([FromQuery] DateTime startUTC, [FromQuery] DateTime endUTC)
        {
            return _server.LoadJobHistory(startUTC, endUTC);
        }

        [HttpGet("recent")]
        public JobsAndExtraParts Recent([FromQuery]string scheduleId)
        {
            return _server.LoadJobsAfterScheduleId(scheduleId);
        }

        [HttpPost("check-valid")]
        public IList<string> CheckValid([FromBody] IList<JobPlan> jobs)
        {
            return _server.CheckValidRoutes(jobs);
        }

        // POST api/values
        [HttpPost("add")]
        public AddJobsResult Add([FromBody]NewJobs newJobs, [FromQuery] string expectedPreviousScheduleId)
        {
            return _server.AddJobs(newJobs, expectedPreviousScheduleId);
        }

        [HttpDelete("quantities")]
        public Dictionary<JobAndPath, int> DecrementQuantities([FromQuery] string clientID)
        {
            return _server.DecrementJobQuantites(clientID);
        }

        [HttpPost("finalize-decrement/{clientID}")]
        public void FinalizeDecrement(string clientID)
        {
            _server.FinalizeDecrement(clientID);
        }

        [HttpPut("job/{unique}")]
        public void Override(string unique, [FromBody] JobPlan job)
        {
            _server.OverrideJob(job);
        }

        [HttpDelete("job/{unique}")]
        public void Archive(string unique)
        {
            _server.ArchiveJob(unique);
        }

        [HttpPut("job/{unique}/priority")]
        public void UpdatePriority(string unique, [FromBody] int priority, [FromQuery] string comment)
        {
            _server.UpdateJobPriority(unique, priority, comment);
        }

        [HttpPut("job/{unique}/holds")]
        public void UpdateHold(string unique, [FromBody] JobHoldPattern hold)
        {
            _server.UpdateJobHold(unique, hold);
        }

        [HttpPut("job/{unique}/machine_hold/{process}/{path}")]
        public void UpdateMachiningHold(string unique, int process, int path, [FromBody] JobHoldPattern hold)
        {
            _server.UpdateJobMachiningHold(unique, process, path, hold);
        }

        [HttpPut("job/{unique}/load_hold/{process}/{path}")]
        public void UpdateLoadHold(string unique, int process, int path, [FromBody] JobHoldPattern hold)
        {
            _server.UpdateJobLoadUnloadHold(unique, process, path, hold);
        }

        [HttpDelete("job/{unique}/quantity/{path}")]
        public int DecrementSinglePath(string unique, int path, [FromQuery] bool finalize, [FromQuery] string clientID, [FromBody] int quantityToRemove)
        {
            return _server.DecrementSinglePath(clientID, finalize, unique, path, quantityToRemove);
        }


    }
}
