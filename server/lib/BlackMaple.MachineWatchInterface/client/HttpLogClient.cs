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
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;

namespace BlackMaple.MachineWatchInterface
{
    public class HttpLogClient : MachineWatchHttpClient, ILogDatabaseAsync
    {
        public HttpLogClient(string host, string token) : base(host, token)
        {}

        private async Task<List<LogEntry>> GetCycles(string path)
        {
            return await RecvJson<List<LogEntry>>(HttpMethod.Get, path);
        }

        public Task<List<LogEntry>> GetCompletedPartLogs(DateTime startUTC, DateTime endUTC)
        {
            return GetCycles(
                "/api/v1/log/cycles/completed-parts?startUTC=" +
                startUTC.ToString("yyyy-MM-ddTHH:mm:ssZ") +
                "&endUTC=" +
                endUTC.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        }

        public Task<List<LogEntry>> GetLogEntries(DateTime startUTC, DateTime endUTC)
        {
            return GetCycles(
                "/api/v1/log/cycles/all?startUTC=" +
                startUTC.ToString("yyyy-MM-ddTHH:mm:ssZ") +
                "&endUTC=" +
                endUTC.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        }

        public Task<List<LogEntry>> GetLogForMaterial(long materialID)
        {
            return GetCycles("/api/v1/log/cycles/material/" + materialID.ToString());
        }

        public Task<List<LogEntry>> GetLogForSerial(string serial)
        {
            return GetCycles("/api/v1/log/cycles/serial/" +
                WebUtility.UrlEncode(serial.ToString()));
        }

        public Task<List<LogEntry>> GetLogForWorkorder(string workorder)
        {
            return GetCycles("/api/v1/log/cycles/workorder/" +
                WebUtility.UrlEncode(workorder.ToString()));
        }

        public Task<List<LogEntry>> GetLogFromCounter(long lastSeenCounter)
        {
            return GetCycles("/api/v1/log/cycles/recent?lastSeenCounter=" + lastSeenCounter.ToString());
        }

        public Task<List<WorkorderSummary>> GetWorkorderSummaries(IEnumerable<string> workorderIds)
        {
            return SendRecvJson<IEnumerable<string>, List<WorkorderSummary>>(
                HttpMethod.Get,
                "/api/v1/log/workorders", workorderIds);
        }

        public Task<LogEntry> RecordFinalizedWorkorder(string workorder)
        {
            return SendRecvJson<bool, LogEntry>(HttpMethod.Post,
                "/api/v1/log/workorder/finalized", true);
        }

        public Task<LogEntry> RecordSerialForMaterialID(LogMaterial material, string serial)
        {
            return SendRecvJson<LogMaterial, LogEntry>(HttpMethod.Post,
                "/api/v1/log/material/serial/" + WebUtility.UrlEncode(serial),
                material);
        }

        public Task<LogEntry> RecordWorkorderForMaterialID(LogMaterial material, string workorder)
        {
            return SendRecvJson<LogMaterial, LogEntry>(HttpMethod.Post,
                "/api/v1/log/material/workorder/" + WebUtility.UrlEncode(workorder),
                material);
        }

        public Task<SerialSettings> GetSerialSettings()
        {
            return RecvJson<SerialSettings>(HttpMethod.Get, "/api/v1/log/serial-settings");
        }

        public Task SetSerialSettings(SerialSettings s)
        {
            return SendJson<SerialSettings>(HttpMethod.Put, "/api/v1/log/serial-settings", s);
        }
    }
}
