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
using BlackMaple.MachineWatchInterface;
using BlackMaple.MachineFramework;

namespace Makino
{
	public class LogTimer
	{
		private object _lock;
		private JobLogDB _log;
		private JobDB _jobDB;
		private InspectionDB _inspectDB;
		private MakinoDB _makinoDB;
		private StatusDB _status;
		private System.Timers.Timer _timer;
		private System.Diagnostics.TraceSource _trace;

		public delegate void LogsProcessedDel();
		public event LogsProcessedDel LogsProcessed;

        public SerialSettings SerialSettings
        {
            get;
            set;
        }

		public LogTimer(
			JobLogDB log, JobDB jobDB, InspectionDB inspect, MakinoDB makinoDB, StatusDB status,
            SerialSettings serSettings,
			System.Diagnostics.TraceSource trace)
		{
			_lock = new object();
			_log = log;
			_jobDB = jobDB;
            SerialSettings = serSettings;
			_inspectDB = inspect;
			_makinoDB = makinoDB;
			_status = status;
			_trace = trace;
            TimerSignaled(null, null);
			_timer = new System.Timers.Timer(TimeSpan.FromMinutes(1).TotalMilliseconds);
			_timer.Elapsed += TimerSignaled;
			_timer.Start();
		}

		public void Halt()
		{
			_timer.Stop();
			_timer.Elapsed -= TimerSignaled;
		}

		private void TimerSignaled(object sender, System.Timers.ElapsedEventArgs e)
		{
			try {
				lock (_lock) {
					// Load one month
					var lastDate = _log.MaxLogDate();
					if (DateTime.UtcNow.Subtract(lastDate) > TimeSpan.FromDays(30))
						lastDate = DateTime.UtcNow.AddDays(-30);

					CheckLogs(lastDate);
				}

			} catch (Exception ex) {
				_trace.TraceEvent(System.Diagnostics.TraceEventType.Error, 0,
					"Unhandled error recording log data" + Environment.NewLine +
					ex.ToString());
			}
		}

		/* This has public instead of private for testing */
		public void CheckLogs(DateTime lastDate)
		{
			var devices = _makinoDB.Devices();

			var machine = _makinoDB.QueryMachineResults(lastDate, DateTime.UtcNow.AddMinutes(1));
			var loads = _makinoDB.QueryLoadUnloadResults(lastDate, DateTime.UtcNow.AddMinutes(1));

			//need to iterate machines and loads by date
			machine.Sort((x, y) => x.EndDateTimeUTC.CompareTo(y.EndDateTimeUTC));
			loads.Sort((x,y) => x.EndDateTimeUTC.CompareTo(y.EndDateTimeUTC));

			var mE = machine.GetEnumerator();
			var lE = loads.GetEnumerator();
			var moreMachines = mE.MoveNext();
			var moreLoads = lE.MoveNext();

			bool newLogEntries = false;
			while (moreMachines || moreLoads) {
				newLogEntries = true;
				if (moreMachines && moreLoads) {

					//check which event occured first and process it
					if (mE.Current.EndDateTimeUTC < lE.Current.EndDateTimeUTC) {
						AddMachineToLog(lastDate, devices, mE.Current);
						moreMachines = mE.MoveNext();
					} else {
						AddLoadToLog(lastDate, devices, lE.Current);
						moreLoads = lE.MoveNext();
					}

				} else if (moreMachines) {
					AddMachineToLog(lastDate, devices, mE.Current);
					moreMachines = mE.MoveNext();
				} else {
					AddLoadToLog(lastDate, devices, lE.Current);
					moreLoads = lE.MoveNext();
				}
			}

			if (newLogEntries)
				LogsProcessed?.Invoke();
		}

        private void AddMachineToLog(
            DateTime timeToSkip, IDictionary<int, PalletLocation> devices, MakinoDB.MachineResults m)
        {
            //find the location
            PalletLocation loc;
            if (devices.ContainsKey(m.DeviceID))
                loc = devices[m.DeviceID];
            else
                loc = new PalletLocation(PalletLocationEnum.Buffer, "Unknown", 0);

            if (loc.Location != PalletLocationEnum.Machine)
            {
                _trace.TraceEvent(System.Diagnostics.TraceEventType.Error, 0,
                    "Creating machine cycle for device that is not a machine: " + loc.Location.ToString());
            }

            //count the number of parts
            int numParts = 0;
            foreach (var i in m.OperQuantities)
                numParts += i;
            if (numParts <= 0)
                return;

            //create the material
            var matList = FindOrCreateMaterial(m.PalletID, m.FixtureNumber, m.EndDateTimeUTC,
                                               m.OrderName, m.PartName, m.ProcessNum, numParts);

            var elapsed = m.EndDateTimeUTC.Subtract(m.StartDateTimeUTC);

            //create the cycle
            var cycle = new LogEntry(-1, matList, m.PalletID.ToString(),
                LogType.MachineCycle, "MC", loc.Num,
                                     m.Program, false, m.EndDateTimeUTC, "", false, elapsed,
                                     TimeSpan.FromSeconds(m.SpindleTimeSeconds));

            //check if the cycle already exists
            if (timeToSkip == m.EndDateTimeUTC && _log.CycleExists(cycle))
                return;

            if (matList.Count > 0) {
                var matID1 = ((LogMaterial)matList[0]).MaterialID;
                _trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
                    "Starting load of common values between the times of " + m.StartDateTimeLocal.ToString() +
                    " and " + m.EndDateTimeLocal.ToString() + "on DeviceID " + m.DeviceID.ToString() +
                    ". These values will be attached to part " + m.PartName + " with serial " + ConvertToBase62(matID1));
                foreach (var v in _makinoDB.QueryCommonValues(m)) {
                    _trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
                        "Common value with number " + v.Number.ToString() + " and value " + v.Value);
                    cycle.ProgramDetails[v.Number.ToString()] = v.Value;
                }
            }

			_log.AddLogEntry(cycle);

			AddInspection(m, matList);
		}

		private void AddInspection(MakinoDB.MachineResults m, IList<LogMaterial> material)
		{
			if (_jobDB == null && _inspectDB == null)
				return;

			var job = _jobDB.LoadJob(m.OrderName);
			if (job == null)
				return;

			if (m.ProcessNum != job.NumProcesses)
				return;

			foreach (LogMaterial mat in material) {
				foreach (var insp in job.GetInspections()) {
					_inspectDB.MakeInspectionDecision(mat.MaterialID, job, insp);
				}
                foreach (var insp in _inspectDB.LoadAllGlobalInspections())
                {
                    _inspectDB.MakeInspectionDecision(mat.MaterialID, job,
                        insp.ConvertToJobInspection(job.PartName, job.NumProcesses));
                }
			}
		}

		private void AddLoadToLog(
			DateTime timeToSkip, IDictionary<int, PalletLocation> devices, MakinoDB.WorkSetResults w)
		{
			//find the location
			PalletLocation loc;
			if (devices.ContainsKey(w.DeviceID))
				loc = devices[w.DeviceID];
			else
				loc = new PalletLocation(PalletLocationEnum.Buffer, "Unknown", 1);

            if (loc.Location != PalletLocationEnum.LoadUnload)
            {
                _trace.TraceEvent(System.Diagnostics.TraceEventType.Error, 0,
                    "Creating machine cycle for device that is not a load: " + loc.Location.ToString());
            }

            //calculate the elapsed time
            var elapsed = w.EndDateTimeUTC.Subtract(w.StartDateTimeUTC);
			elapsed = new TimeSpan(elapsed.Ticks / 2);

			//count the number of unloaded parts
			int numParts = 0;
			foreach (var i in w.UnloadNormalQuantities)
				numParts += i;

			//Only process unload cycles if remachine is false
			if (numParts > 0 && !w.Remachine) {
				//create the material for unload
				var matList = FindOrCreateMaterial(w.PalletID, w.FixtureNumber, w.EndDateTimeUTC,
					                               w.UnloadOrderName, w.UnloadPartName, w.UnloadProcessNum, numParts);

				//create the cycle
				var cycle = new LogEntry(-1, matList, w.PalletID.ToString(),
                    LogType.LoadUnloadCycle, "Load", loc.Num,
					"UNLOAD", false, w.EndDateTimeUTC, "", true,  elapsed, elapsed);

				//check if the cycle already exists
				if (timeToSkip == w.EndDateTimeUTC && _log.CycleExists(cycle))
					return;

				_log.AddLogEntry(cycle);
			}

			//Pallet Cycle
			_log.CompletePalletCycle(w.PalletID.ToString(), w.EndDateTimeUTC, "");

			//now the load cycle
			numParts = 0;
			foreach (var i in w.LoadQuantities)
				numParts += i;

			if (numParts > 0) {
				//create the material
				var matList = CreateMaterial(w.PalletID, w.FixtureNumber, w.EndDateTimeUTC.AddSeconds(1),
				                             w.LoadOrderName, w.LoadPartName, w.LoadProcessNum, numParts);

				//create the cycle
				var cycle = new LogEntry(-1, matList, w.PalletID.ToString(),
                    LogType.LoadUnloadCycle, "Load", loc.Num,
					"LOAD", false, w.EndDateTimeUTC.AddSeconds(1), "", false, elapsed, elapsed);

				_log.AddLogEntry(cycle);
			}
		}

		private IList<LogMaterial> CreateMaterial(int pallet, int fixturenum, DateTime endUTC, string order, string part, int process, int count)
		{
			var rows = _status.CreateMaterialIDs(pallet, fixturenum, endUTC, order, count, 0);

			var ret = new List<LogMaterial>();
			foreach (var row in rows)
				ret.Add(new LogMaterial(row.MatID, order, process, part, process, fixturenum.ToString()));
			return ret;
		}

		private IList<LogMaterial> FindOrCreateMaterial(int pallet, int fixturenum, DateTime endUTC, string order, string part, int process, int count)
		{
			var rows = _status.FindMaterialIDs(pallet, fixturenum, endUTC);

			if (rows.Count == 0) {
				OutputTrace("Unable to find any material ids for pallet " + pallet.ToString() + "-" +
					fixturenum.ToString() + " for order " + order + " for event at time " + endUTC.ToString());
				rows = _status.CreateMaterialIDs(pallet, fixturenum, endUTC, order, count, 0);
			}

			if (rows[0].Order != order) {
				OutputTrace("MaterialIDs for pallet " + pallet.ToString() + "-" + fixturenum.ToString() +
					" for event at time " + endUTC.ToString() + " does not have matching orders: " +
					"expected " + order + " but found " + rows[0].Order);

				rows = _status.CreateMaterialIDs(pallet, fixturenum, endUTC, order, count, 0);
			}

			if (rows.Count < count) {
				OutputTrace("Pallet " + pallet.ToString() + "-" + fixturenum.ToString() +
					" at event time " + endUTC.ToString() + " with order " + order + " was expected to have " +
					count.ToString() + " material ids, but only " + rows.Count + " were loaded");

				int maxCounter = -1;
				foreach (var row in rows) {
					if (row.LocCounter > maxCounter)
						maxCounter = row.LocCounter;
				}

				//stupid that IList doesn't have AddRange
				foreach (var row in _status.CreateMaterialIDs(
					pallet, fixturenum, rows[0].LoadedUTC, order, count - rows.Count, maxCounter + 1)) {
					rows.Add(row);
				}
			}

			var ret = new List<LogMaterial>();
			foreach (var row in rows) {
                if (SerialSettings.SerialType != SerialType.NoAutomaticSerials)
				    CreateSerial(row.MatID, order, part, process, fixturenum.ToString(), _log, SerialSettings.SerialLength, _trace);
				//TODO: maxProcess
				ret.Add(new LogMaterial(row.MatID, order, process, part, process, fixturenum.ToString()));
			}
			return ret;
		}

		public static void CreateSerial(long matID, string jobUniqe, string partName, int process, string face,
                                        JobLogDB _log, int serLength, System.Diagnostics.TraceSource _trace)
		{
			foreach (var stat in _log.GetLogForMaterial(matID)) {
				if (stat.LogType == LogType.PartMark &&
				    stat.LocationNum == 1) {
					foreach (LogMaterial mat in stat.Material) {
						if (mat.Process == process) {
							//We have recorded the serial already
							return;
						}
					}
				}
			}

			var serial = ConvertToBase62(matID);

			//length 10 gets us to 1.5e18 which is not quite 2^64
			//still large enough so we will practically never roll around
			serial = serial.Substring(0, Math.Min(serLength, serial.Length));
			serial = serial.PadLeft(10, '0');

			if (_trace != null)
				_trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
				                  "Recording serial for matid: " + matID.ToString() + " - " + serial);


            var logMat = new LogMaterial(matID, jobUniqe, process, partName, process, face);
            _log.RecordSerialForMaterialID(logMat, serial);
		}

		private static string ConvertToBase62(long num)
		{
			string baseChars = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

			string res = "";
			long cur = num;

			while (cur > 0) {
				long quotient = cur / 62;
				int remainder = (int)cur % 62;

				res = baseChars[remainder] + res;
				cur = quotient;
			}

			return res;
		}


#if DEBUG
		internal static List<string> errors = new List<string>();
#endif

		private void OutputTrace(string msg)
		{
			if (_trace == null) {
#if DEBUG
				errors.Add(msg);
#else
				throw new ApplicationException(msg);
#endif
			} else {
				_trace.TraceEvent(System.Diagnostics.TraceEventType.Warning, 0, msg);
			}
		}
	}
}

