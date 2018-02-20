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

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using BlackMaple.MachineWatchInterface;

namespace MachineWatchApiServer.Controllers
{
    [Route("api/v1/[controller]")]
    public class jobsController : ControllerBase
    {
        private IJobDatabase _db;
        private IJobControl _control;

        public jobsController(IServerBackend backend)
        {
            _db = backend.JobDatabase();
            _control = backend.JobControl();
        }

        [HttpGet("history")]
        public HistoricData History([FromQuery] DateTime startUTC, [FromQuery] DateTime endUTC)
        {
            return _db.LoadJobHistory(startUTC, endUTC);
        }

        [HttpGet("recent")]
        public PlannedSchedule Recent([FromQuery]string afterScheduleId)
        {
            return _db.LoadJobsAfterScheduleId(afterScheduleId);
        }

        [HttpGet("latest-schedule")]
        public PlannedSchedule LatestSchedule()
        {
            return _db.LoadMostRecentSchedule();
        }

        [HttpGet("unfilled-workorders/by-part/{part}")]
        public IList<PartWorkorder> MostRecentUnfilledWorkordersForPart(string part)
        {
            return _db.MostRecentUnfilledWorkordersForPart(part);
        }

        [HttpGet("status")]
        public CurrentStatus CurrentStatus()
        {
            return _control.GetCurrentStatus();
        }

        [HttpGet("check-valid")]
        public IList<string> CheckValid([FromBody] IList<JobPlan> jobs)
        {
            return _control.CheckValidRoutes(jobs);
        }

        [HttpPost("add")]
        public void Add([FromBody]NewJobs newJobs, [FromQuery] string expectedPreviousScheduleId)
        {
            _control.AddJobs(newJobs, expectedPreviousScheduleId);
        }

        [HttpGet("/queues")]
        public List<string> QueueNames() {
            return _control.GetQueueNames();
        }

        [HttpPost("job/{jobUnique}/casting")]
        public void AddCastingToQueue(string jobUnique, [FromQuery] string queue, [FromBody] string serial)
        {
            _control.AddCastingToQueue(jobUnique, queue, serial);
        }

        [HttpPut("material/{materialId}/queue")]
        public void SetMaterialInQueue(long materialId, [FromBody] string queue) {
            _control.SetMaterialInQueue(materialId, queue);
        }

        [HttpDelete("material/{materialId}/queue")]
        public void RemoveMaterialFromAllQueues(long materialId)
        {
            _control.RemoveMaterialFromAllQueues(materialId);
        }

        [HttpDelete("planned-cycles")]
        public List<JobAndDecrementQuantity> DecrementQuantities(
            [FromQuery] string loadDecrementsStrictlyAfterDecrementId = null,
            [FromQuery] DateTime? loadDecrementsAfterTimeUTC = null)
        {
            if (!string.IsNullOrEmpty(loadDecrementsStrictlyAfterDecrementId))
                return _control.DecrementJobQuantites(loadDecrementsStrictlyAfterDecrementId);
            else if (loadDecrementsAfterTimeUTC.HasValue)
                return _control.DecrementJobQuantites(loadDecrementsAfterTimeUTC.Value);
            else
                throw new Exception("Must specify either loadDecrementsStrictlyAfterDecrementId or loadDecrementsAfterTimeUTC");
        }
    }
}