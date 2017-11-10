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
