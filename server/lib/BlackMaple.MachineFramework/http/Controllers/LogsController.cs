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
using System.Collections.Immutable;
using Microsoft.AspNetCore.Mvc;

namespace BlackMaple.MachineFramework.Controllers
{
  public record NewInspectionCompleted
  {
    public required long MaterialID { get; init; }

    public required int Process { get; init; }

    public required int InspectionLocationNum { get; init; }

    public required string InspectionType { get; init; }

    public required bool Success { get; init; }

    public Dictionary<string, string> ExtraData { get; init; }

    public required TimeSpan Elapsed { get; init; }

    public required TimeSpan Active { get; init; }
  }

  public record NewCloseout
  {
    public required long MaterialID { get; init; }

    public required int Process { get; init; }

    public required int LocationNum { get; init; }

    public required string CloseoutType { get; init; }

    public Dictionary<string, string> ExtraData { get; init; }

    public required TimeSpan Elapsed { get; init; }

    public required TimeSpan Active { get; init; }

    public bool Failed { get; init; } = false;
  }

  [ApiController]
  [Route("api/v1/log")]
  public class LogController(RepositoryConfig repo, IJobAndQueueControl jobAndQueue) : ControllerBase
  {
    [HttpGet("events/all")]
    public IEnumerable<LogEntry> Get([FromQuery] DateTime startUTC, [FromQuery] DateTime endUTC)
    {
      using var db = repo.OpenConnection();
      foreach (var l in db.GetLogEntries(startUTC, endUTC))
      {
        yield return l;
      }
    }

    [HttpGet("events.csv")]
    [Produces("text/csv")]
    public IActionResult GetEventCSV([FromQuery] DateTime startUTC, [FromQuery] DateTime endUTC)
    {
      IEnumerable<LogEntry> entries;
      using (var db = repo.OpenConnection())
      {
        entries = db.GetLogEntries(startUTC, endUTC);
      }

      var ms = new System.IO.MemoryStream();
      try
      {
        var tx = new System.IO.StreamWriter(ms);
        CSVLogConverter.WriteCSV(tx, entries);
        tx.Flush();
      }
      catch
      {
        ms.Close();
        throw;
      }
      ms.Position = 0;
      // filestreamresult will close the memorystream
      return new FileStreamResult(ms, "text/csv") { FileDownloadName = "events.csv" };
    }

    [HttpGet("events/all-completed-parts")]
    public IEnumerable<LogEntry> GetCompletedParts([FromQuery] DateTime startUTC, [FromQuery] DateTime endUTC)
    {
      using var db = repo.OpenConnection();
      foreach (var l in db.GetLogOfAllCompletedParts(startUTC, endUTC))
      {
        yield return l;
      }
    }

    [HttpGet("events/recent")]
    public IEnumerable<LogEntry> Recent(
      [FromQuery] long lastSeenCounter,
      [FromQuery] DateTime? expectedEndUTCofLastSeen = null
    )
    {
      using var db = repo.OpenConnection();
      foreach (var l in db.GetRecentLog(lastSeenCounter, expectedEndUTCofLastSeen))
      {
        yield return l;
      }
    }

    [HttpGet("events/for-material/{materialID}")]
    public List<LogEntry> LogForMaterial(long materialID)
    {
      using var db = repo.OpenConnection();
      return db.GetLogForMaterial(materialID);
    }

    [HttpGet("events/for-material")]
    public List<LogEntry> LogForMaterials([FromQuery] List<long> id)
    {
      if (id == null || id.Count == 0)
        return [];
      using var db = repo.OpenConnection();
      return db.GetLogForMaterial(id);
    }

    [HttpGet("events/for-serial/{serial}")]
    public IEnumerable<LogEntry> LogForSerial(string serial)
    {
      using var db = repo.OpenConnection();
      foreach (var l in db.GetLogForSerial(serial))
      {
        yield return l;
      }
    }

    [HttpGet("events/for-workorder/{workorder}")]
    public IEnumerable<LogEntry> LogForWorkorder(string workorder)
    {
      using var db = repo.OpenConnection();
      foreach (var l in db.GetLogForWorkorder(workorder))
      {
        yield return l;
      }
    }

    [HttpGet("material-details/{materialID}")]
    public MaterialDetails MaterialDetails(long materialID)
    {
      using var db = repo.OpenConnection();
      return db.GetMaterialDetails(materialID);
    }

    [HttpGet("material-for-job/{jobUnique}")]
    public List<MaterialDetails> MaterialDetailsForJob(string jobUnique)
    {
      using var db = repo.OpenConnection();
      return db.GetMaterialForJobUnique(jobUnique);
    }

    [HttpGet("material-for-serial/{serial}")]
    public IEnumerable<MaterialDetails> MaterialForSerial(string serial)
    {
      using var db = repo.OpenConnection();
      return db.GetMaterialDetailsForSerial(serial);
    }

    [HttpPost("material-details/{materialID}/serial")]
    public LogEntry SetSerial(long materialID, [FromBody] string serial, [FromQuery] int process = 1)
    {
      LogEntry log;
      using (var db = repo.OpenConnection())
      {
        log = db.RecordSerialForMaterialID(materialID, process, serial, DateTime.UtcNow);
      }
      jobAndQueue.RecalculateCellState();
      return log;
    }

    [HttpPost("material-details/{materialID}/workorder")]
    public LogEntry SetWorkorder(long materialID, [FromBody] string workorder, [FromQuery] int process = 1)
    {
      LogEntry log;
      using (var db = repo.OpenConnection())
      {
        log = db.RecordWorkorderForMaterialID(materialID, process, workorder);
      }
      jobAndQueue.RecalculateCellState();
      return log;
    }

    [HttpPost("material-details/{materialID}/inspections/{inspType}")]
    public LogEntry SetInspectionDecision(
      long materialID,
      string inspType,
      [FromBody] bool inspect,
      [FromQuery] int process = 1
    )
    {
      LogEntry log;
      using (var db = repo.OpenConnection())
      {
        log = db.ForceInspection(materialID, process, inspType, inspect);
      }
      jobAndQueue.RecalculateCellState();
      return log;
    }

    [HttpPost("material-details/{materialID}/notes")]
    public LogEntry RecordOperatorNotes(
      long materialID,
      [FromBody] string notes,
      [FromQuery] int process = 1,
      [FromQuery] string operatorName = null
    )
    {
      LogEntry log;
      using (var db = repo.OpenConnection())
      {
        log = db.RecordOperatorNotes(materialID, process, notes, operatorName);
      }
      jobAndQueue.RecalculateCellState();
      return log;
    }

    [HttpPost("events/inspection-result")]
    public LogEntry RecordInspectionCompleted([FromBody] NewInspectionCompleted insp)
    {
      if (string.IsNullOrEmpty(insp.InspectionType))
        throw new BadRequestException("Must give inspection type");
      LogEntry log;
      using (var db = repo.OpenConnection())
      {
        log = db.RecordInspectionCompleted(
          insp.MaterialID,
          insp.Process,
          insp.InspectionLocationNum,
          insp.InspectionType,
          insp.Success,
          insp.ExtraData ?? [],
          insp.Elapsed,
          insp.Active
        );
      }
      jobAndQueue.RecalculateCellState();
      return log;
    }

    [HttpPost("events/closeout")]
    public LogEntry RecordCloseoutCompleted([FromBody] NewCloseout insp)
    {
      LogEntry log;
      using (var db = repo.OpenConnection())
      {
        log = db.RecordCloseoutCompleted(
          materialID: insp.MaterialID,
          process: insp.Process,
          locNum: insp.LocationNum,
          closeoutType: insp.CloseoutType,
          success: !insp.Failed,
          extraData: insp.ExtraData ?? [],
          elapsed: insp.Elapsed,
          active: insp.Active
        );
      }
      jobAndQueue.RecalculateCellState();
      return log;
    }

    [HttpPost("workorder/{workorder}/comment")]
    public LogEntry RecordWorkorderComment(
      string workorder,
      [FromBody] string comment,
      [FromQuery] string operatorName = null
    )
    {
      LogEntry log;
      using (var db = repo.OpenConnection())
      {
        log = db.RecordWorkorderComment(workorder: workorder, comment: comment, operName: operatorName);
      }
      jobAndQueue.RecalculateCellState();
      return log;
    }

    [HttpGet("workorder/{workorder}")]
    public ImmutableList<ActiveWorkorder> GetActiveWorkorder(string workorder)
    {
      using var db = repo.OpenConnection();
      return db.GetActiveWorkorder(workorder);
    }
  }
}
