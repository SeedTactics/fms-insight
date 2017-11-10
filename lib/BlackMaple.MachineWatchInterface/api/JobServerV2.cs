using System;
using System.Collections.Generic;

namespace BlackMaple.MachineWatchInterface
{
    public interface IJobServerV2
    {
        //loads info
        CurrentStatus GetCurrentStatus();
        //load all jobs, station, and tool utilization which intersect the given date range.
        HistoricData LoadJobHistory(DateTime startUTC, DateTime endUTC);
        //Loads all jobs which have a unique strictly larger than the given unique
        JobsAndExtraParts LoadJobsAfterScheduleId(string scheduleId);
        //Loads all jobs for the most recent schedule
        JobsAndExtraParts LoadMostRecentSchedule();

        //checks to see if the jobs are valid.  Some machine types might not support all the different
        //pallet->part->machine->process combinations.
        //Return value is a list of strings, detailing the problems.
        //An empty list or nothing signals the jobs are valid.
        List<string> CheckValidRoutes(IEnumerable<JobPlan> newJobs);

        //Adds new jobs into the cell controller.
        //This function should throw as errors any of the warnings produced by CheckValidRoutes.
        //The first two are to support backwards compatibility in the network API
        void AddJobs(IEnumerable<JobPlan> newJobs,
                     bool updateGlobalTag,
                     string newGlobalTag,
                     bool archiveCompletedJobs);
        void AddJobs(IEnumerable<JobPlan> newJobs,
                     IEnumerable<SimulatedStationUtilization> stationUse,
                     bool updateGlobalTag,
                     string newGlobalTag,
                     bool archiveCompletedJobs);
        AddJobsResult AddJobs(NewJobs jobs, string expectedPreviousScheduleId);

        //Update job data.
        void UpdateJobPriority(string unique, int newPriority, string newComment);
        void UpdateJobHold(string unique, JobHoldPattern newHold);
        void UpdateJobMachiningHold(string unique, int process, int path, JobHoldPattern newHold);
        void UpdateJobLoadUnloadHold(string unique, int process, int path, JobHoldPattern newHold);

        //override a job with the data passed in.  This ignores possible races, and allows the data
        //on the job to be changed.  All the data about the job can be changed, with the following expection:
        // +  The old job and the new job must have the same number of processes.
        // This function will throw an expection if these conditions are not met.
        // Note that material that is currently in execution will not be updated.
        void OverrideJob(JobPlan job);

        //A job can only be archived if the all the material currently in execution is on pallets
        //located at load stations.  Otherwise, this function throws an exception.
        void ArchiveJob(string jobUniqueStr);

        //Remove all planned parts from all jobs in the system.
        //
        //The function does 2 things:
        // - Check for planned but not yet machined quantites and if found remove them
        //   and store locally in the machine watch database with a new DecrementId.
        // - Load all decremented quantites (including the potentially new quantites)
        //   strictly after the given decrement ID.
        //Thus this function can be called multiple times to receive the same data.
        List<JobAndDecrementQuantity> DecrementJobQuantites(string loadDecrementsStrictlyAfterDecrementId);
        List<JobAndDecrementQuantity> DecrementJobQuantites(DateTime loadDecrementsAfterTimeUTC);

        //The old method of decrementing, which stores only a single decrement until finalize is called.
        Dictionary<JobAndPath, int> OldDecrementJobQuantites();
        void OldFinalizeDecrement();
    }
}