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
using BlackMaple.MachineWatchInterface;

namespace BlackMaple.MachineFramework.Controllers
{
    [DataContract] public struct NewInspectionCompleted
    {
        [DataMember(IsRequired=true)] public LogMaterial Material {get;set;}
        [DataMember(IsRequired=true)] public int InspectionLocationNum {get;set;}
        [DataMember(IsRequired=true)] public string InspectionType {get;set;}
        [DataMember(IsRequired=true)] public bool Success {get;set;}
        [DataMember(IsRequired=false)] public Dictionary<string, string> ExtraData {get;set;}
        [DataMember(IsRequired=true)] public TimeSpan Elapsed {get;set;}
        [DataMember(IsRequired=true)] public TimeSpan Active {get;set;}
    }

    [DataContract] public struct NewWash
    {
        [DataMember(IsRequired=true)] public LogMaterial Material {get;set;}
        [DataMember(IsRequired=true)] public int WashLocationNum {get;set;}
        [DataMember(IsRequired=false)] public Dictionary<string, string> ExtraData {get;set;}
        [DataMember(IsRequired=true)] public TimeSpan Elapsed {get;set;}
        [DataMember(IsRequired=true)] public TimeSpan Active {get;set;}
    }

    [Route("api/v1/[controller]")]
    public class logController : ControllerBase
    {
        private ILogDatabase _server;

        public logController(IFMSBackend backend)
        {
            _server = backend.LogDatabase();
        }

        [HttpGet("events/all")]
        public List<LogEntry> Get([FromQuery] DateTime startUTC, [FromQuery] DateTime endUTC)
        {
            return _server.GetLogEntries(startUTC, endUTC);
        }

        [HttpGet("events.csv")]
        [Produces("text/csv")]
        public IActionResult GetEventCSV([FromQuery] DateTime startUTC, [FromQuery] DateTime endUTC) {
            var ms = new System.IO.MemoryStream();
            try {
                var tx = new System.IO.StreamWriter(ms);
                CSVLogConverter.WriteCSV(tx, _server.GetLogEntries(startUTC, endUTC));
            } catch {
                ms.Close();
                throw;
            }
            ms.Position = 0;
            // filestreamresult will close the memorystream
            return new FileStreamResult(ms, "text/csv") {
                FileDownloadName = "events.csv"
            };
        }

        [HttpGet("events/all-completed-parts")]
        public List<LogEntry> GetCompletedParts([FromQuery] DateTime startUTC, [FromQuery] DateTime endUTC)
        {
            return _server.GetCompletedPartLogs(startUTC, endUTC);
        }

        [HttpGet("events/recent")]
        public List<LogEntry> Recent([FromQuery] long lastSeenCounter)
        {
            return _server.GetLog(lastSeenCounter);
        }

        [HttpGet("events/for-material/{materialID}")]
        public List<LogEntry> LogForMaterial(long materialID)
        {
            return _server.GetLogForMaterial(materialID);
        }

        [HttpGet("events/for-serial/{serial}")]
        public List<LogEntry> LogForSerial(string serial)
        {
            return _server.GetLogForSerial(serial);
        }

        [HttpGet("events/for-workorder/{workorder}")]
        public List<LogEntry> LogForWorkorder(string workorder)
        {
            return _server.GetLogForWorkorder(workorder);
        }

        [HttpGet("workorders")]
        public List<WorkorderSummary> GetWorkorders([FromQuery] IEnumerable<string> ids)
        {
            return _server.GetWorkorderSummaries(ids);
        }

        [HttpGet("workorders.csv")]
        [Produces("text/csv")]
        public IActionResult GetWorkordersCSV([FromQuery] IEnumerable<string> ids)
        {
            var ms = new System.IO.MemoryStream();
            try {
                var tx = new System.IO.StreamWriter(ms);
                CSVWorkorderConverter.WriteCSV(tx, _server.GetWorkorderSummaries(ids));
            } catch {
                ms.Close();
                throw;
            }
            ms.Position = 0;
            // filestreamresult will close the memorystream
            return new FileStreamResult(ms, "text/csv") {
                FileDownloadName = "workorders.csv"
            };
        }

        [HttpPost("serial/{serial}/material")]
        public LogEntry SetSerial(string serial, [FromBody] LogMaterial mat)
        {
            return _server.RecordSerialForMaterialID(mat, serial);
        }

        [HttpPost("workorder/{workorder}/material")]
        public LogEntry SetWorkorder(string workorder, [FromBody] LogMaterial mat)
        {
            return _server.RecordWorkorderForMaterialID(mat, workorder);
        }

        [HttpPost("inspections/{inspType}/material")]
        public LogEntry SetInspectionDecision(string inspType, [FromBody] LogMaterial mat, [FromQuery] bool inspect = true)
        {
            return _server.ForceInspection(mat, inspType, inspect);
        }

        [HttpPost("events/inspection-result")]
        public LogEntry RecordInspectionCompleted([FromBody] NewInspectionCompleted insp)
        {
            if (string.IsNullOrEmpty(insp.InspectionType)) throw new BadRequestException("Must give inspection type");
            if (insp.Material == null) throw new BadRequestException("Must give material type");
            return _server.RecordInspectionCompleted(
                insp.Material,
                insp.InspectionLocationNum,
                insp.InspectionType,
                insp.Success,
                insp.ExtraData == null ? new Dictionary<string, string>() : insp.ExtraData,
                insp.Elapsed,
                insp.Active
            );
        }

        [HttpPost("events/wash")]
        public LogEntry RecordWashCompleted([FromBody] NewWash insp)
        {
            return _server.RecordWashCompleted(
                insp.Material,
                insp.WashLocationNum,
                insp.ExtraData == null ? new Dictionary<string, string>() : insp.ExtraData,
                insp.Elapsed,
                insp.Active
            );
        }

        [HttpPost("workorder/{workorder}/finalize")]
        public LogEntry FinalizeWorkorder(string workorder)
        {
            return _server.RecordFinalizedWorkorder(workorder);
        }
    }
}
