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
using System.Threading.Tasks;

namespace BlackMaple.MachineWatchInterface
{
    public interface ILogDatabase
    {
        List<LogEntry> GetLogEntries(DateTime startUTC, DateTime endUTC);
        List<LogEntry> GetLog(long lastSeenCounter);
        List<LogEntry> GetLogForMaterial(long materialID);
        List<LogEntry> GetLogForSerial(string serial);
        List<LogEntry> GetLogForWorkorder(string workorder);
        List<LogEntry> GetCompletedPartLogs(DateTime startUTC, DateTime endUTC);
        List<WorkorderSummary> GetWorkorderSummaries(IEnumerable<string> workorderIds);

        LogEntry RecordSerialForMaterialID(LogMaterial mat, string serial);
        LogEntry RecordWorkorderForMaterialID(LogMaterial mat, string workorder);
        LogEntry RecordFinalizedWorkorder(string workorder);

        SerialSettings GetSerialSettings();
        void SetSerialSettings(SerialSettings s);
    }

    public interface ILogDatabaseAsync
    {
      Task<List<LogEntry>> GetLogEntries(DateTime startUTC, DateTime endUTC);
      Task<List<LogEntry>> GetLogFromCounter(long lastSeenCounter);
      Task<List<LogEntry>> GetLogForMaterial(long materialID);
      Task<List<LogEntry>> GetLogForSerial(string serial);
      Task<List<LogEntry>> GetLogForWorkorder(string workorder);
      Task<List<LogEntry>> GetCompletedPartLogs(DateTime startUTC, DateTime endUTC);
      Task<List<WorkorderSummary>> GetWorkorderSummaries(IEnumerable<string> workorderIds);

      Task<LogEntry> RecordSerialForMaterialID(LogMaterial material, string serial);
      Task<LogEntry> RecordWorkorderForMaterialID(LogMaterial material, string workorder);
      Task<LogEntry> RecordFinalizedWorkorder(string workorder);

      Task<SerialSettings> GetSerialSettings();
      Task SetSerialSettings(SerialSettings s);
    }

    public class LogDatabaseAsyncConverter : ILogDatabaseAsync
    {
      private ILogDatabase _d;
      public LogDatabaseAsyncConverter(ILogDatabase d) => _d = d;

        public async Task<List<LogEntry>> GetCompletedPartLogs(DateTime startUTC, DateTime endUTC)
          => await Task.Run(() => _d.GetCompletedPartLogs(startUTC, endUTC));

        public Task<List<LogEntry>> GetLogEntries(DateTime startUTC, DateTime endUTC)
          => Task.Run(() => _d.GetLogEntries(startUTC, endUTC));

        public Task<List<LogEntry>> GetLogForMaterial(long materialID)
          => Task.Run(() => _d.GetLogForMaterial(materialID));

        public Task<List<LogEntry>> GetLogForSerial(string serial)
          => Task.Run(() => _d.GetLogForSerial(serial));

        public Task<List<LogEntry>> GetLogForWorkorder(string workorder)
          => Task.Run(() => _d.GetLogForWorkorder(workorder));

        public Task<List<LogEntry>> GetLogFromCounter(long lastSeenCounter)
          => Task.Run(() => _d.GetLog(lastSeenCounter));

        public Task<SerialSettings> GetSerialSettings()
          => Task.Run(() => _d.GetSerialSettings());

        public Task<List<WorkorderSummary>> GetWorkorderSummaries(IEnumerable<string> workorderIds)
          => Task.Run(() => _d.GetWorkorderSummaries(workorderIds));

        public Task<LogEntry> RecordFinalizedWorkorder(string workorder)
          => Task.Run(() => _d.RecordFinalizedWorkorder(workorder));

        public Task<LogEntry> RecordSerialForMaterialID(LogMaterial material, string serial)
          => Task.Run(() => _d.RecordSerialForMaterialID(material, serial));

        public Task<LogEntry> RecordWorkorderForMaterialID(LogMaterial material, string workorder)
          => Task.Run(() => _d.RecordWorkorderForMaterialID(material, workorder));

        public Task SetSerialSettings(SerialSettings s)
          => Task.Run(() => _d.SetSerialSettings(s));
    }
}

