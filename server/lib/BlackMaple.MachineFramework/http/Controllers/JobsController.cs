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
using Microsoft.AspNetCore.Authorization;
using BlackMaple.MachineWatchInterface;
using System.Runtime.Serialization;

namespace BlackMaple.MachineFramework.Controllers
{
  [DataContract]
  public class QueuePosition
  {
    [DataMember(IsRequired = true)] public string Queue { get; set; }
    [DataMember(IsRequired = true)] public int Position { get; set; }
  }

  [ApiController]
  [Authorize]
  [Route("api/v1/[controller]")]
  public class jobsController : ControllerBase
  {
    private IJobDatabase _db;
    private IJobControl _control;

    public jobsController(IFMSBackend backend)
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
    public HistoricData Recent([FromQuery]string afterScheduleId)
    {
      if (string.IsNullOrEmpty(afterScheduleId))
        throw new BadRequestException("After schedule ID must be non-empty");
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
      if (string.IsNullOrEmpty(part))
        throw new BadRequestException("Part must be non-empty");
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
    [ProducesResponseType(typeof(void), 200)]
    public void Add([FromBody]NewJobs newJobs, [FromQuery] string expectedPreviousScheduleId)
    {
      _control.AddJobs(newJobs, expectedPreviousScheduleId);
    }

    [HttpPost("part/{partName}/casting")]
    [ProducesResponseType(typeof(void), 200)]
    public void AddUnallocatedCastingToQueueByPart(string partName, [FromQuery] string queue, [FromQuery] int pos, [FromBody] string serial)
    {
      if (string.IsNullOrEmpty(partName))
        throw new BadRequestException("Part name must be non-empty");
      if (string.IsNullOrEmpty(queue))
        throw new BadRequestException("Queue must be non-empty");
      _control.AddUnallocatedPartToQueue(partName, queue, pos, serial);
    }

    [HttpPost("casting/{castingName}")]
    [ProducesResponseType(typeof(void), 200)]
    public void AddUnallocatedCastingToQueue(string castingName, [FromQuery] string queue, [FromQuery] int pos, [FromBody] List<string> serials, [FromQuery] int qty = 1)
    {
      if (string.IsNullOrEmpty(castingName))
        throw new BadRequestException("Casting name must be non-empty");
      if (string.IsNullOrEmpty(queue))
        throw new BadRequestException("Queue must be non-empty");
      _control.AddUnallocatedCastingToQueue(castingName, qty, queue, pos, serials);
    }

    [HttpPost("job/{jobUnique}/unprocessed-material")]
    [ProducesResponseType(typeof(void), 200)]
    public void AddUnprocessedMaterialToQueue(string jobUnique, [FromQuery] int lastCompletedProcess, [FromQuery] int pathGroup, [FromQuery] string queue, [FromQuery] int pos, [FromBody] string serial)
    {
      if (string.IsNullOrEmpty(jobUnique))
        throw new BadRequestException("Job unique must be non-empty");
      if (string.IsNullOrEmpty(queue))
        throw new BadRequestException("Queue must be non-empty");
      if (lastCompletedProcess < 0) lastCompletedProcess = 0;
      if (pathGroup < 0) pathGroup = 0;
      _control.AddUnprocessedMaterialToQueue(jobUnique, lastCompletedProcess, pathGroup, queue, pos, serial);
    }

    [HttpPut("job/{jobUnique}/comment")]
    [ProducesResponseType(typeof(void), 200)]
    public void SetJobComment(string jobUnique, [FromBody] string comment)
    {
      _control.SetJobComment(jobUnique, comment);
    }

    [HttpPut("material/{materialId}/queue")]
    [ProducesResponseType(typeof(void), 200)]
    public void SetMaterialInQueue(long materialId, [FromBody] QueuePosition queue)
    {
      if (string.IsNullOrEmpty(queue.Queue))
        throw new BadRequestException("Queue name must be non-empty");
      _control.SetMaterialInQueue(materialId, queue.Queue, queue.Position);
    }

    [HttpDelete("material/{materialId}/queue")]
    [ProducesResponseType(typeof(void), 200)]
    public void RemoveMaterialFromAllQueues(long materialId)
    {
      _control.RemoveMaterialFromAllQueues(new[] { materialId });
    }

    [HttpDelete("material")]
    [ProducesResponseType(typeof(void), 200)]
    public void BulkRemoveMaterialFromQueues([FromQuery] List<long> id)
    {
      if (id == null || id.Count == 0) return;
      _control.RemoveMaterialFromAllQueues(id);
    }

    [HttpDelete("planned-cycles")]
    public List<JobAndDecrementQuantity> DecrementQuantities(
        [FromQuery] long? loadDecrementsStrictlyAfterDecrementId = null,
        [FromQuery] DateTime? loadDecrementsAfterTimeUTC = null)
    {
      if (loadDecrementsStrictlyAfterDecrementId != null)
        return _control.DecrementJobQuantites(loadDecrementsStrictlyAfterDecrementId ?? 0);
      else if (loadDecrementsAfterTimeUTC.HasValue)
        return _control.DecrementJobQuantites(loadDecrementsAfterTimeUTC.Value);
      else
        throw new BadRequestException("Must specify either loadDecrementsStrictlyAfterDecrementId or loadDecrementsAfterTimeUTC");
    }
  }
}