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
using System.Runtime.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BlackMaple.MachineWatchInterface;
using Microsoft.AspNetCore.Cors;

namespace BlackMaple.MachineFramework.Controllers
{
  [DataContract]
  public struct NewInspectionCompleted
  {
    [DataMember(IsRequired = true)] public long MaterialID { get; set; }
    [DataMember(IsRequired = true)] public int Process { get; set; }
    [DataMember(IsRequired = true)] public int InspectionLocationNum { get; set; }
    [DataMember(IsRequired = true)] public string InspectionType { get; set; }
    [DataMember(IsRequired = true)] public bool Success { get; set; }
    [DataMember(IsRequired = false)] public Dictionary<string, string> ExtraData { get; set; }
    [DataMember(IsRequired = true)] public TimeSpan Elapsed { get; set; }
    [DataMember(IsRequired = true)] public TimeSpan Active { get; set; }
  }

  [DataContract]
  public struct NewWash
  {
    [DataMember(IsRequired = true)] public long MaterialID { get; set; }
    [DataMember(IsRequired = true)] public int Process { get; set; }
    [DataMember(IsRequired = true)] public int WashLocationNum { get; set; }
    [DataMember(IsRequired = false)] public Dictionary<string, string> ExtraData { get; set; }
    [DataMember(IsRequired = true)] public TimeSpan Elapsed { get; set; }
    [DataMember(IsRequired = true)] public TimeSpan Active { get; set; }
  }

  [ApiController]
  [Authorize]
  [Route("api/v1/[controller]")]
  public class logController : ControllerBase
  {
    private IFMSBackend _backend;

    public logController(IFMSBackend backend)
    {
      _backend = backend;
    }

    [HttpGet("events/all")]
    public List<LogEntry> Get([FromQuery] DateTime startUTC, [FromQuery] DateTime endUTC)
    {
      using (var db = _backend.OpenLogDatabase())
      {
        return db.GetLogEntries(startUTC, endUTC);
      }
    }

    [HttpGet("events.csv")]
    [Produces("text/csv")]
    public IActionResult GetEventCSV([FromQuery] DateTime startUTC, [FromQuery] DateTime endUTC)
    {
      IEnumerable<LogEntry> entries;
      using (var db = _backend.OpenLogDatabase())
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
      return new FileStreamResult(ms, "text/csv")
      {
        FileDownloadName = "events.csv"
      };
    }

    [HttpGet("events/all-completed-parts")]
    public List<LogEntry> GetCompletedParts([FromQuery] DateTime startUTC, [FromQuery] DateTime endUTC)
    {
      using (var db = _backend.OpenLogDatabase())
      {
        return db.GetCompletedPartLogs(startUTC, endUTC);
      }
    }

    [HttpGet("events/recent")]
    public List<LogEntry> Recent([FromQuery] long lastSeenCounter)
    {
      using (var db = _backend.OpenLogDatabase())
      {
        return db.GetLog(lastSeenCounter);
      }
    }

    [HttpGet("events/for-material/{materialID}")]
    public List<LogEntry> LogForMaterial(long materialID)
    {
      using (var db = _backend.OpenLogDatabase())
      {
        return db.GetLogForMaterial(materialID);
      }
    }

    [HttpGet("events/for-material")]
    public List<LogEntry> LogForMaterials([FromQuery] List<long> id)
    {
      if (id == null || id.Count == 0) return new List<LogEntry>();
      using (var db = _backend.OpenLogDatabase())
      {
        return db.GetLogForMaterial(id);
      }
    }

    [HttpGet("events/for-serial/{serial}")]
    [EnableCors("AllowOtherLogServers")]
    public List<LogEntry> LogForSerial(string serial)
    {
      using (var db = _backend.OpenLogDatabase())
      {
        return db.GetLogForSerial(serial);
      }
    }

    [HttpGet("events/for-workorder/{workorder}")]
    public List<LogEntry> LogForWorkorder(string workorder)
    {
      using (var db = _backend.OpenLogDatabase())
      {
        return db.GetLogForWorkorder(workorder);
      }
    }

    [HttpGet("material-details/{materialID}")]
    public MaterialDetails MaterialDetails(long materialID)
    {
      using (var db = _backend.OpenLogDatabase())
      {
        return db.GetMaterialDetails(materialID);
      }
    }

    [HttpGet("workorders")]
    public List<WorkorderSummary> GetWorkorders([FromQuery] IEnumerable<string> ids)
    {
      using (var db = _backend.OpenLogDatabase())
      {
        return db.GetWorkorderSummaries(ids);
      }
    }

    [HttpGet("workorders.csv")]
    [Produces("text/csv")]
    public IActionResult GetWorkordersCSV([FromQuery] IEnumerable<string> ids)
    {
      IEnumerable<WorkorderSummary> workorders;
      using (var db = _backend.OpenLogDatabase())
      {
        workorders = db.GetWorkorderSummaries(ids);
      }

      var ms = new System.IO.MemoryStream();
      try
      {
        var tx = new System.IO.StreamWriter(ms);
        CSVWorkorderConverter.WriteCSV(tx, workorders);
        tx.Flush();
      }
      catch
      {
        ms.Close();
        throw;
      }
      ms.Position = 0;
      // filestreamresult will close the memorystream
      return new FileStreamResult(ms, "text/csv")
      {
        FileDownloadName = "workorders.csv"
      };
    }

    [HttpPost("material-details/{materialID}/serial")]
    public LogEntry SetSerial(long materialID, [FromBody] string serial, [FromQuery] int process = 1)
    {
      using (var db = _backend.OpenLogDatabase())
      {
        return db.RecordSerialForMaterialID(materialID, process, serial);
      }
    }

    [HttpPost("material-details/{materialID}/workorder")]
    public LogEntry SetWorkorder(long materialID, [FromBody] string workorder, [FromQuery] int process = 1)
    {
      using (var db = _backend.OpenLogDatabase())
      {
        return db.RecordWorkorderForMaterialID(materialID, process, workorder);
      }
    }

    [HttpPost("material-details/{materialID}/inspections/{inspType}")]
    public LogEntry SetInspectionDecision(long materialID, string inspType, [FromBody] bool inspect, [FromQuery] int process = 1)
    {
      using (var db = _backend.OpenLogDatabase())
      {
        return db.ForceInspection(materialID, process, inspType, inspect);
      }
    }

    [HttpPost("material-details/{materialID}/notes")]
    public LogEntry RecordOperatorNotes(long materialID, [FromBody] string notes, [FromQuery] int process = 1, [FromQuery] string operatorName = null)
    {
      using (var db = _backend.OpenLogDatabase())
      {
        return db.RecordOperatorNotes(materialID, process, notes, operatorName);
      }
    }


    [HttpPost("events/inspection-result")]
    public LogEntry RecordInspectionCompleted([FromBody] NewInspectionCompleted insp)
    {
      if (string.IsNullOrEmpty(insp.InspectionType)) throw new BadRequestException("Must give inspection type");
      using (var db = _backend.OpenLogDatabase())
      {
        return db.RecordInspectionCompleted(
            insp.MaterialID,
            insp.Process,
            insp.InspectionLocationNum,
            insp.InspectionType,
            insp.Success,
            insp.ExtraData == null ? new Dictionary<string, string>() : insp.ExtraData,
            insp.Elapsed,
            insp.Active
        );
      }
    }

    [HttpPost("events/wash")]
    public LogEntry RecordWashCompleted([FromBody] NewWash insp)
    {
      using (var db = _backend.OpenLogDatabase())
      {
        return db.RecordWashCompleted(
            insp.MaterialID,
            insp.Process,
            insp.WashLocationNum,
            insp.ExtraData == null ? new Dictionary<string, string>() : insp.ExtraData,
            insp.Elapsed,
            insp.Active
        );
      }
    }

    [HttpPost("workorder/{workorder}/finalize")]
    public LogEntry FinalizeWorkorder(string workorder)
    {
      using (var db = _backend.OpenLogDatabase())
      {
        return db.RecordFinalizedWorkorder(workorder);
      }
    }
  }
}
