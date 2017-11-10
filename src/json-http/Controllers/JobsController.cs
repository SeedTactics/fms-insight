using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using BlackMaple.MachineWatchInterface;

namespace MachineWatchApiServer.Controllers
{
    [Route("api/[controller]")]
    public class JobsController : Controller
    {
        private IJobServerV2 _server;

        public JobsController(IServerBackend backend)
        {
            _server = backend.JobServer();
        }

        [HttpGet]
        public CurrentStatus Get()
        {
            return _server.GetCurrentStatus();
        }

        [HttpGet("history")]
        public HistoricData History([FromQuery] DateTime startUTC, [FromQuery] DateTime endUTC)
        {
            return _server.LoadJobHistory(startUTC, endUTC);
        }

        [HttpGet("recent")]
        public JobsAndExtraParts Recent([FromQuery]string scheduleId)
        {
            return _server.LoadJobsAfterScheduleId(scheduleId);
        }

        [HttpPost("check-valid")]
        public IList<string> CheckValid([FromBody] IList<JobPlan> jobs)
        {
            return _server.CheckValidRoutes(jobs);
        }

        // POST api/values
        [HttpPost("add")]
        public AddJobsResult Add([FromBody]NewJobs newJobs, [FromQuery] string expectedPreviousScheduleId)
        {
            return _server.AddJobs(newJobs, expectedPreviousScheduleId);
        }

        [HttpDelete("quantities")]
        public Dictionary<JobAndPath, int> DecrementQuantities([FromQuery] string clientID)
        {
            return _server.DecrementJobQuantites(clientID);
        }

        [HttpPost("finalize-decrement/{clientID}")]
        public void FinalizeDecrement(string clientID)
        {
            _server.FinalizeDecrement(clientID);
        }

        [HttpPut("job/{unique}")]
        public void Override(string unique, [FromBody] JobPlan job)
        {
            _server.OverrideJob(job);
        }

        [HttpDelete("job/{unique}")]
        public void Archive(string unique)
        {
            _server.ArchiveJob(unique);
        }

        [HttpPut("job/{unique}/priority")]
        public void UpdatePriority(string unique, [FromBody] int priority, [FromQuery] string comment)
        {
            _server.UpdateJobPriority(unique, priority, comment);
        }

        [HttpPut("job/{unique}/holds")]
        public void UpdateHold(string unique, [FromBody] JobHoldPattern hold)
        {
            _server.UpdateJobHold(unique, hold);
        }

        [HttpPut("job/{unique}/machine_hold/{process}/{path}")]
        public void UpdateMachiningHold(string unique, int process, int path, [FromBody] JobHoldPattern hold)
        {
            _server.UpdateJobMachiningHold(unique, process, path, hold);
        }

        [HttpPut("job/{unique}/load_hold/{process}/{path}")]
        public void UpdateLoadHold(string unique, int process, int path, [FromBody] JobHoldPattern hold)
        {
            _server.UpdateJobLoadUnloadHold(unique, process, path, hold);
        }

        [HttpDelete("job/{unique}/quantity/{path}")]
        public int DecrementSinglePath(string unique, int path, [FromQuery] bool finalize, [FromQuery] string clientID, [FromBody] int quantityToRemove)
        {
            return _server.DecrementSinglePath(clientID, finalize, unique, path, quantityToRemove);
        }


    }
}
