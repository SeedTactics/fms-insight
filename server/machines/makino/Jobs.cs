using System;
using System.Collections.Generic;
using BlackMaple.MachineWatchInterface;

namespace Makino
{
	public class Jobs : IJobControl, IOldJobDecrement
	{
		private MakinoDB _db;
		private BlackMaple.MachineFramework.JobDB _jobDB;
		private string _xmlPath;
		private bool _onlyOrders;

        public event NewCurrentStatus OnNewCurrentStatus;
        protected void RaiseNewCurrentStatus(CurrentStatus s) => OnNewCurrentStatus?.Invoke(s);

		public Jobs(MakinoDB db, BlackMaple.MachineFramework.JobDB jdb, string xmlPath, bool onlyOrders)
		{
			_db = db;
			_jobDB = jdb;
			_xmlPath = xmlPath;
			_onlyOrders = onlyOrders;
		}

		public CurrentStatus GetCurrentStatus()
		{
            if (_db == null)
                return new CurrentStatus();
            else
			    return _db.LoadCurrentInfo();
		}

        public List<string> CheckValidRoutes(IEnumerable<JobPlan> newJobs)
		{
			return new List<string>();
		}

        public void AddJobs(NewJobs newJ, string expectedPreviousScheduleId)
        {
			var newJobs = new List<JobPlan>();
			foreach (var j in newJ.Jobs) {
                j.Archived = true;
                j.JobCopiedToSystem = true;
				if (!_jobDB.DoesJobExist(j.UniqueStr)) {
                    for (int proc = 1; proc <= j.NumProcesses; proc++)
                    {
                        for (int path = 1; path <= j.GetNumPaths(proc); path++)
                        {
                            foreach (var stop in j.GetMachiningStop(proc, path))
                            {
                                //The station group name on the job and the LocationName from the
                                //generated log entries must match.  Rather than store and try and lookup
                                //the station name when creating log entries, since we only support a single
                                //machine group, just set the group name to MC here during storage and
                                //always create log entries with MC.
                                stop.StationGroup = "MC";
                            }
                        }
                    }
					newJobs.Add(j);
				}
			}

			_jobDB.AddJobs(newJ, expectedPreviousScheduleId);
			OrderXML.WriteOrderXML(System.IO.Path.Combine(_xmlPath, "sail.xml"), newJobs, _onlyOrders);
		}

		#region Decrement
        List<JobAndDecrementQuantity> IJobControl.DecrementJobQuantites(string loadDecrementsStrictlyAfterDecrementId)
        {
            return new List<JobAndDecrementQuantity>();
        }

        public List<JobAndDecrementQuantity> DecrementJobQuantites(DateTime loadDecrementsAfterTimeUTC)
        {
            return new List<JobAndDecrementQuantity>();
        }

        public Dictionary<JobAndPath, int> OldDecrementJobQuantites()
        {
            return new Dictionary<JobAndPath, int>();
        }

        public void OldFinalizeDecrement()
        {
            //do nothing
        }
        #endregion

        #region Queues
        public void AddUnprocessedMaterialToQueue(string jobUnique, int process, string queue, int position, string serial)
        {
            //do nothing
        }

        public void SetMaterialInQueue(long materialId, string queue, int position)
        {
            //do nothing
        }

        public void RemoveMaterialFromAllQueues(long materialId)
        {
            //do nothing
        }
        #endregion
    }
}

