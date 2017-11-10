using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;

namespace BlackMaple.MachineWatchInterface
{
    public class HttpLogClient : MachineWatchHttpClient
    {
        public HttpLogClient(string host, string token) : base(host, token)
        {}

        public async Task AddStationCycle(LogEntry cycle)
        {
            await SendJson(HttpMethod.Put, "/api/log", cycle);
        }

        public async Task<LogEntry> RecordSerialForMaterialID(LogMaterial mat, string serial)
        {
            return await SendRecvJson<LogMaterial, LogEntry>
              (HttpMethod.Put, "/api/log/serial/" + WebUtility.UrlEncode(serial), mat);
        }

        private async Task<List<LogEntry>> GetCycles(string path)
        {
            return await RecvJson<List<LogEntry>>(HttpMethod.Get, path);
        }

        public async Task<List<LogEntry>> GetLogForMaterial(long materialID)
        {
            return await GetCycles("/api/log/material/" + materialID.ToString());
        }

        public async Task<List<LogEntry>> GetLogForSerial(string serial)
        {
            return await GetCycles("/api/log/serial/" + WebUtility.UrlEncode(serial.ToString()));
        }

        public async Task<List<LogEntry>> GetStationCycleLog(DateTime startUTC, DateTime endUTC)
        {
            return await GetCycles("/api/log?startUTC=" +
               startUTC.ToString("yyyy-MM-ddTHH:mm:ssZ") +
               "&endUTC=" + endUTC.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        }

        public async Task<List<LogEntry>> GetStationCycleLog(long lastSeenCounter)
        {
            return await GetCycles("/api/log/recent?lastSeenCounter=" + lastSeenCounter.ToString());
        }
    }
}
