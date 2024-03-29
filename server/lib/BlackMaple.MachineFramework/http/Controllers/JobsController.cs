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
using System.Collections.Immutable;
using Microsoft.AspNetCore.Mvc;

namespace BlackMaple.MachineFramework.Controllers
{
  public record QueuePosition
  {
    public required string Queue { get; init; }

    public required int Position { get; init; }
  }

  [ApiController]
  [Route("api/v1/[controller]")]
  public class jobsController : ControllerBase
  {
    private FMSImplementation _impl;

    public jobsController(FMSImplementation impl)
    {
      _impl = impl;
    }

    [HttpGet("history")]
    public HistoricData History([FromQuery] DateTime startUTC, [FromQuery] DateTime endUTC)
    {
      using (var db = _impl.Backend.RepoConfig.OpenConnection())
      {
        return db.LoadJobHistory(startUTC, endUTC);
      }
    }

    [HttpPost("history")]
    public HistoricData FilteredHistory(
      [FromQuery] DateTime startUTC,
      [FromQuery] DateTime endUTC,
      [FromBody] List<string> alreadyKnownSchIds
    )
    {
      using (var db = _impl.Backend.RepoConfig.OpenConnection())
      {
        return db.LoadJobHistory(
          startUTC,
          endUTC,
          alreadyKnownSchIds: new HashSet<string>(alreadyKnownSchIds ?? new List<string>())
        );
      }
    }

    [HttpPost("recent")]
    public RecentHistoricData Recent(
      [FromQuery] DateTime startUTC,
      [FromBody] List<string> alreadyKnownSchIds
    )
    {
      using (var db = _impl.Backend.RepoConfig.OpenConnection())
      {
        return db.LoadRecentJobHistory(startUTC, alreadyKnownSchIds);
      }
    }

    [HttpGet("latest-schedule")]
    public PlannedSchedule LatestSchedule()
    {
      using (var db = _impl.Backend.RepoConfig.OpenConnection())
      {
        return db.LoadMostRecentSchedule();
      }
    }

    [HttpGet("unfilled-workorders/by-part/{part}")]
    public ImmutableList<ActiveWorkorder> MostRecentUnfilledWorkordersForPart(string part)
    {
      if (string.IsNullOrEmpty(part))
        throw new BadRequestException("Part must be non-empty");
      using (var db = _impl.Backend.RepoConfig.OpenConnection())
      {
        return db.GetActiveWorkorders(partToFilter: part);
      }
    }

    [HttpGet("status")]
    public CurrentStatus CurrentStatus()
    {
      return _impl.Backend.JobControl.GetCurrentStatus();
    }

    [HttpPost("add")]
    [ProducesResponseType(typeof(void), 200)]
    public void Add([FromBody] NewJobs newJobs, [FromQuery] string expectedPreviousScheduleId)
    {
      _impl.Backend.JobControl.AddJobs(jobs: newJobs, expectedPreviousScheduleId: expectedPreviousScheduleId);
    }

    [HttpPost("casting/{castingName}")]
    public List<InProcessMaterial> AddUnallocatedCastingToQueue(
      string castingName,
      [FromQuery] string queue,
      [FromBody] List<string> serials,
      [FromQuery] int qty = 1,
      [FromQuery] string operName = null,
      [FromQuery] string workorder = null
    )
    {
      if (string.IsNullOrEmpty(castingName))
        throw new BadRequestException("Casting name must be non-empty");
      if (string.IsNullOrEmpty(queue))
        throw new BadRequestException("Queue must be non-empty");
      return _impl.Backend.QueueControl.AddUnallocatedCastingToQueue(
        casting: castingName,
        qty: qty,
        queue: queue,
        serial: serials,
        workorder: workorder,
        operatorName: operName
      );
    }

    [HttpGet("job/{jobUnique}/plan")]
    public HistoricJob GetJobPlan(string jobUnique)
    {
      using (var db = _impl.Backend.RepoConfig.OpenConnection())
      {
        return db.LoadJob(jobUnique);
      }
    }

    [HttpPost("job/{jobUnique}/unprocessed-material")]
    public InProcessMaterial AddUnprocessedMaterialToQueue(
      string jobUnique,
      [FromQuery] int lastCompletedProcess,
      [FromQuery] string queue,
      [FromQuery] int pos,
      [FromBody] string serial,
      [FromQuery] string operName = null
    )
    {
      if (string.IsNullOrEmpty(jobUnique))
        throw new BadRequestException("Job unique must be non-empty");
      if (string.IsNullOrEmpty(queue))
        throw new BadRequestException("Queue must be non-empty");
      if (lastCompletedProcess < 0)
        lastCompletedProcess = 0;
      return _impl.Backend.QueueControl.AddUnprocessedMaterialToQueue(
        jobUnique,
        lastCompletedProcess,
        queue,
        pos,
        serial,
        operName
      );
    }

    [HttpPut("job/{jobUnique}/comment")]
    [ProducesResponseType(typeof(void), 200)]
    public void SetJobComment(string jobUnique, [FromBody] string comment)
    {
      _impl.Backend.JobControl.SetJobComment(jobUnique, comment);
    }

    [HttpPut("material/{materialId}/queue")]
    [ProducesResponseType(typeof(void), 200)]
    public void SetMaterialInQueue(
      long materialId,
      [FromBody] QueuePosition queue,
      [FromQuery] string operName = null
    )
    {
      if (string.IsNullOrEmpty(queue.Queue))
        throw new BadRequestException("Queue name must be non-empty");
      _impl.Backend.QueueControl.SetMaterialInQueue(materialId, queue.Queue, queue.Position, operName);
    }

    [HttpDelete("material/{materialId}/queue")]
    [ProducesResponseType(typeof(void), 200)]
    public void RemoveMaterialFromAllQueues(long materialId, [FromQuery] string operName = null)
    {
      _impl.Backend.QueueControl.RemoveMaterialFromAllQueues(new[] { materialId }, operName);
    }

    [HttpPut("material/{materialId}/signal-quarantine")]
    [ProducesResponseType(typeof(void), 200)]
    public void SignalMaterialForQuarantine(
      long materialId,
      [FromQuery] string operName = null,
      [FromBody] string reason = null
    )
    {
      _impl.Backend.QueueControl.SignalMaterialForQuarantine(
        materialId,
        operatorName: operName,
        reason: reason
      );
    }

    [HttpPut("material/{materialId}/invalidate-process")]
    [ProducesResponseType(typeof(void), 200)]
    public void InvalidatePalletCycle(
      long materialId,
      [FromBody] int process,
      [FromQuery] string putMatInQueue = null,
      [FromQuery] string operName = null
    )
    {
      _impl.Backend.QueueControl.InvalidatePalletCycle(
        matId: materialId,
        process: process,
        oldMatPutInQueue: putMatInQueue,
        operatorName: operName
      );
    }

    public record MatToPutOnPallet
    {
      public required int Pallet { get; init; }

      public required long MaterialIDToSetOnPallet { get; init; }
    }

    [HttpPut("material/{materialId}/swap-off-pallet")]
    [ProducesResponseType(typeof(void), 200)]
    public void SwapMaterialOnPallet(
      long materialId,
      [FromBody] MatToPutOnPallet mat,
      [FromQuery] string operName = null
    )
    {
      _impl.Backend.QueueControl.SwapMaterialOnPallet(
        oldMatId: materialId,
        newMatId: mat.MaterialIDToSetOnPallet,
        pallet: mat.Pallet,
        operatorName: operName
      );
    }

    [HttpDelete("material")]
    [ProducesResponseType(typeof(void), 200)]
    public void BulkRemoveMaterialFromQueues([FromBody] List<long> id, [FromQuery] string operName = null)
    {
      if (id == null || id.Count == 0)
        return;
      _impl.Backend.QueueControl.RemoveMaterialFromAllQueues(id, operName);
    }

    [HttpDelete("planned-cycles")]
    public List<JobAndDecrementQuantity> DecrementQuantities(
      [FromQuery] long? loadDecrementsStrictlyAfterDecrementId = null,
      [FromQuery] DateTime? loadDecrementsAfterTimeUTC = null
    )
    {
      if (loadDecrementsStrictlyAfterDecrementId != null)
        return _impl.Backend.JobControl.DecrementJobQuantites(loadDecrementsStrictlyAfterDecrementId ?? 0);
      else if (loadDecrementsAfterTimeUTC.HasValue)
        return _impl.Backend.JobControl.DecrementJobQuantites(loadDecrementsAfterTimeUTC.Value);
      else
        throw new BadRequestException(
          "Must specify either loadDecrementsStrictlyAfterDecrementId or loadDecrementsAfterTimeUTC"
        );
    }
  }
}
