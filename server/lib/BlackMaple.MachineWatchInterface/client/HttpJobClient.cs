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
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace BlackMaple.MachineWatchInterface
{
    public class HttpJobClient : MachineWatchHttpClient
    {
        public HttpJobClient(string host, string token) : base(host, token)
        { }

        public async Task<AddJobsResult> AddJobs(NewJobs newJ, string expectedPreviousScheduleId)
        {
            return await SendRecvJson<NewJobs, AddJobsResult>(HttpMethod.Post,
             "/api/jobs/add?expectedPreviousScheduleId=" + WebUtility.UrlEncode(expectedPreviousScheduleId),
             newJ);
        }

        public async Task ArchiveJob(string jobUniqueStr)
        {
            await Send(HttpMethod.Delete, "/api/jobs/job/" + WebUtility.UrlEncode(jobUniqueStr));
        }

        public async Task<List<string>> CheckValidRoutes(IEnumerable<JobPlan> newJobs)
        {
            return await SendRecvJson<IEnumerable<JobPlan>, List<string>>
              (HttpMethod.Post, "/api/jobs/check-valid", newJobs);
        }

        public async Task<Dictionary<JobAndPath, int>> DecrementJobQuantites(string clientID)
        {
            return await RecvJson<Dictionary<JobAndPath, int>>
              (HttpMethod.Delete, "/api/jobs/quantities?clientID=" + WebUtility.UrlEncode(clientID));
        }

        public async Task<int> DecrementSinglePath(string clientID, bool finalize, string uniqueStr, int path, int quantity)
        {
            return await SendRecvJson<int, int>(HttpMethod.Delete,
                "/api/jobs/job/" + WebUtility.UrlEncode(uniqueStr) +
                 "/quantity/" + path.ToString() + "?finalize=" + finalize.ToString() +
                 "&clientID=" + WebUtility.UrlEncode(clientID),
                 quantity
            );
        }

        public async Task FinalizeDecrement(string clientID)
        {
            await Send(HttpMethod.Post, "/api/jobs/finalize-decrement/" + WebUtility.UrlEncode(clientID));
        }

        public async Task<CurrentStatus> GetCurrentStatus()
        {
            return await RecvJson<CurrentStatus>(HttpMethod.Get, "/api/jobs");
        }

        public async Task<HistoricData> LoadJobHistory(DateTime startUTC, DateTime endUTC)
        {
            return await RecvJson<HistoricData>(HttpMethod.Get,
              "/api/jobs/history?startUTC=" + startUTC.ToString("yyyy-MM-ddTHH:mm:ssZ") +
              "&endUTC=" + endUTC.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        }

        public async Task<JobsAndExtraParts> LoadJobsAfterScheduleId(string scheduleId)
        {
            return await RecvJson<JobsAndExtraParts>(HttpMethod.Get,
              "/api/jobs/recent?scheduleId=" + WebUtility.UrlEncode(scheduleId));
        }

        public async Task OverrideJob(JobPlan job)
        {
            await SendJson<JobPlan>(HttpMethod.Put, "/api/jobs/job/" + WebUtility.UrlEncode(job.UniqueStr), job);
        }

        public async Task UpdateJobHold(string unique, JobHoldPattern newHold)
        {
            await SendJson<JobHoldPattern>(HttpMethod.Put,
              "/api/jobs/job/" + WebUtility.UrlEncode(unique) + "/holds", newHold);
        }

        public async Task UpdateJobLoadUnloadHold(string unique, int process, int path, JobHoldPattern newHold)
        {
            await SendJson<JobHoldPattern>(HttpMethod.Put,
              "/api/jobs/job/" + WebUtility.UrlEncode(unique) + "/load_hold/" + process.ToString() + "/" + path.ToString(),
               newHold);
        }

        public async Task UpdateJobMachiningHold(string unique, int process, int path, JobHoldPattern newHold)
        {
            await SendJson<JobHoldPattern>(HttpMethod.Put,
              "/api/jobs/job/" + WebUtility.UrlEncode(unique) + "/machine_hold/" + process.ToString() + "/" + path.ToString(),
               newHold);
        }

        public async Task UpdateJobPriority(string unique, int newPriority, string newComment)
        {
            await SendJson<int>(HttpMethod.Put,
              "/api/jobs/job/" + WebUtility.UrlEncode(unique) + "/priority?comment=" + WebUtility.UrlEncode(newComment),
              newPriority);
        }
    }
}
