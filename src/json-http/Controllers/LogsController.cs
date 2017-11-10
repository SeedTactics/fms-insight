using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using BlackMaple.MachineWatchInterface;

namespace MachineWatchApiServer.Controllers
{
    [Route("api/[controller]")]
    public class LogController : Controller
    {
        private ILogServerV2 _server;

        public LogController(IServerBackend backend)
        {
            _server = backend.LogServer();
        }

        [HttpGet]
        public List<LogEntry> Get([FromQuery] DateTime startUTC, [FromQuery] DateTime endUTC)
        {
            return _server.GetLogEntries(startUTC, endUTC);
        }

        [HttpPost]
        public void Add([FromBody] LogEntry cycle)
        {
            _server.AddLogEntry(cycle);
        }

        [HttpGet("recent")]
        public List<LogEntry> Recent([FromQuery] long lastSeenCounter)
        {
            return _server.GetLog(lastSeenCounter);
        }

        [HttpGet("material/{materialID}")]
        public List<LogEntry> Material(long materialID)
        {
            return _server.GetLogForMaterial(materialID);
        }

        [HttpGet("serial/{serial}")]
        public List<LogEntry> Serial(string serial)
        {
            return _server.GetLogForSerial(serial);
        }

        [HttpPost("serial/{serial}")]
        public LogEntry AddToSerial(string serial, [FromBody] LogMaterial mat)
        {
            return _server.RecordSerialForMaterialID(mat, serial);
        }
    }
}
