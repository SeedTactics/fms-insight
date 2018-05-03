using System;
using System.Collections.Generic;
using System.Data;
using BlackMaple.MachineWatchInterface;

namespace Makino
{
	#region Description of DB
	/* Makino static data.
	 * ----------------------------------------------------------------------------------
	 * A part consists of:
	 *   - A globally unique integer called PartID
	 *   - A name, revision, comment, priority
	 *   - dbo.Parts stores the above data with one row per part
	 *   - list of processes (found in dbo.Processes)
	 * 
	 * A process consists of:
	 *   - A globally unique integer called ProcessID (unique over all parts)
	 *   - The PartID
	 *   - A process number (a counter starting at 1 for each part)
	 *   - A name, comment, offset file, priority
	 *   - dbo.Process stores the above data with one row per process
	 * 
	 *   - a list of operations
	 *   - a list of jobs
	 * 
	 * An operation consists of
	 *   - A globally unique integer called OperationID
	 *   - ProcessID  (which since it is unique identifies also the part)
	 *   - An OperationSequence starting at 1 for each process
	 *   - Three fields called ClampQuantity, UnclampMultiplier, OperationName
	 *   - dbo.Operations stores the above data
	 * 
	 * A job consists of
	 *   - A globally unique JobID
	 *   - ProcessID (which identifies a process of a part)
	 *   - A JobNumber starting at 1 for each process
	 *   - Comment
	 *   - A JobType, which is 1 for load/unload and 2 for machining
	 *   - dbo.Jobs stores the above data
	 * 
	 * If a job is a load/unload job, the following additional data is stored
	 *   - JobID
	 *   - OperationType: 0 = load, 1 = unload
	 *   - MotionType, InformationFile, WorkSetTime
	 *   - dbo.WorkSetJobs stores this.
	 * 
	 * If a job is a machining job, the following additional data is stored
	 *   - JobID
	 *   - OperatorCall (0 = none, 1 = permanent, 2 = oneshot)
	 *   - OptionalBlockSkip (comma delimited list of values 2 through 9)
	 *   - NCProgramID (lookup in dbo.NCProgramFiles)
	 *   - MachiningMode, MachiningTime, TimeStudy, WHPMachiningType
	 *   - dbo.MachiningJobs stores this
	 * 
	 * A job also consists of a list of feasible devices in dbo.JobFeasibleDevices
	 *   - JobID, FeasibleDeviceID (lookup in dbo.Devices)
	 * 
	 * -------------------------------------------------------------------------------
	 * 
	 * A fixture consists of
	 *   - Globally unique FixtureID
	 *   - name, comment, offsetfile
	 *   - above stored in dbo.Fixtures.
	 *   - list of processes
	 * 
	 * The list of processes on a fixture is
	 *   - FixtureID
	 *   - ProcessID (uniquely identifies a process and part)
	 *   - FixturePriority
	 *   - OffsetFile
	 *   - above stored in dbo.FixtureProcesses
	 * 
	 * -------------------------------------------------------------------------------
	 * 
	 * An order consists of
	 *   - OrderID
	 *   - PartID
	 *   - name, comment, quantity, priority, start date, due date
	 *   - above stored in dbo.Orders
	 * 
	 * -------------------------------------------------------------------------------
	 * 
	 * A pallet consists of
	 *   - PalletID (globally unique integer)
	 *   - PalletNumber, name, comment, pallet type (seems to be 0 always), priority, home info, vehicle id
	 *   - above stored in dbo.Pallets
	 *   - A list of fixtures currently mounted on the pallet
	 *   - a list of feasible devices
	 * 
	 * The list of fixutres assigned to the pallet
	 *   - PalletFixtureID (globally unique integer)
	 *   - PalletID
	 *   - FixtureNumber (counter starting at 1 for each pallet id)
	 *   - FixtureID currently in this (PalletID, FixtureNumber)
	 *   - offset file, comment, pallet index (always seems to be zero)
	 *   - some status columns
	 *   - Above stored in dbo.PalletFixtures
	 *   
	 * The list of pallet feasible devices is in dbo.PalletFeasibleDevice
	 */
	
	/* Current Status
	 *  -------------------------------------------------------------------------------
	 * 	 - dbo.Quantities (one row per Order, Process pair)
	 *        OrderID, ProcessID, quantities
	 * 
	 *   - dbo.FixtureRun  (one row per PalletFixtureID)
	 *       PalletFixtureID (a location on a pallet)
	 *       current process, order, job
	 *       status flag (seems to be 1 or 2)
	 *       MachineDeviceID
	 *       some status columns 
	 * 
	 *   - dbo.FixtureQuantity (one row per PalletFixtureID)
	 *       PalletFixtureID (a location on a pallet)
	 *       OperationID ??
	 *       some quantities
	 * 
	 *   - dbo.PalletLocation
	 *       PalletID, DeviceID
     *
	 *   - dbo.ManualTransport
	 *       Current manual transports
	 * 
	 *   - dbo.WorkSetStatus (one row per load station)
	 *       DeviceID, status info
	 * 
	 * 	 - dbo.MachineStatus (one row per machine)
	 *       DeviceID, status info
	 * 
	 *   - dbo.WorkSetCommand (load/unload instructions)
	 *       DeviceID
	 *       PalletFixtureID (a location on a pallet)
	 *       UnclampJobID, UnclampOrderID
	 *       ClampJobID, ClampOrderID
	 *       A bunch of other columns, not sure what they do
	 */
	
	 /* Unknown Tables
	  * - dbo.ONumbers ???? 
	  * - lots of tooling tables
	  */
     #endregion
	
	public class MakinoDB
	{
		#region Init
		public enum DBTypeEnum
		{
			SqlLocal,
			SqlConnStr
		}
		
		private IDbConnection _db;
		private StatusDB _status;
		private string dbo;
		private System.Diagnostics.TraceSource trace;
        private BlackMaple.MachineFramework.JobLogDB _logDb;
        private BlackMaple.MachineFramework.InspectionDB _inspDb;

        public MakinoDB(DBTypeEnum dbType, string dbConnStr, StatusDB status,
            BlackMaple.MachineFramework.JobLogDB log, BlackMaple.MachineFramework.InspectionDB insp,
            System.Diagnostics.TraceSource t)
		{
			trace = t;
			_status = status;
            _logDb = log;
            _inspDb = insp;
            switch (dbType) {
			case DBTypeEnum.SqlLocal:
                _db = new System.Data.SqlClient.SqlConnection("Data Source = (localdb)\\MSSQLLocalDB; Initial Catalog = Makino;");
                _db.Open();
				dbo = "dbo.";
				break;
				
			case DBTypeEnum.SqlConnStr:
				_db = new System.Data.SqlClient.SqlConnection(dbConnStr);
                _db.Open();
				dbo = "dbo.";
				break;
			}
		}

        public void Close()
        {
            _db.Close();
        }
		#endregion
				
		#region Log Data		
	    /* 
		 * Log Data
		 *   - dbo.CNCAlarams
		 *   - dbo.MachineResults
		 *   - dbo.PMCAlarms
		 *   - dbo.VehicleResults
		 *   - dbo.WorkSetResults
		 * 	 - dbo.OrderResults (row seems to be recorded when the order is completed)
		 * 	 - dbo.CommonValues
		 */
		
		public class MachineResults
		{
			public DateTime StartDateTimeLocal;
			public DateTime EndDateTimeLocal;
			public DateTime StartDateTimeUTC;
			public DateTime EndDateTimeUTC;
			
			public int DeviceID;
			
			/* PalletID and FixtureNumber uniquely identifiy the location on the pallet */
			public int PalletID;
			public int FixtureNumber;
			
			/* data about the fixture */
			public string FixtureName;
			public string FixtureComment;
			
			public string OrderName;
			
			/* Part, revision, process, and job uniquely identify what the machine is currently doing */
			public string PartName;
			public string Revision;
			public int ProcessNum;
			public int JobNum;
			
			/* Process name is just some data about the process entered by the user,
		     * not used as a key or anything like that */
			public string ProcessName;
			
			/* program for this (part,revision,process,job) combo */
			public string Program;
			
			/* Some status about the operation */
			public int SpindleTimeSeconds;
			public List<int> OperQuantities;
			
			/* Fields not loaded
			 * 
			 * PalletIndex:
			 *   always zero in the test data
			 * 
			 * OffestFileName:
			 *   not currently used/needed by machine watch
			 * 
			 * AlarmNumber
			 *   can/should we load and use this?
			 *   it is always null in the test data
			 * 
			 * FinishStatus
			 *   what is this?
			 *   it is either 'Normal' or 'ToolLife' in the test data
			 */
		}
		
		public List<MachineResults> QueryMachineResults(DateTime startUTC, DateTime endUTC)
		{
			using (var cmd = _db.CreateCommand()) {
				cmd.CommandText = "SELECT StartDateTime,FinishDateTime,DeviceID,PalletID,"
					+ "FixtureNumber,FixtureName,FixtureComment,OrderName,PartName,Revision,ProcessNumber,"
				    + "JobNumber,ProcessName,NCProgramFileName,SpindleTime,"
					+ "MaxOperation,QuantityOpe1,QuantityOpe2,QuantityOpe3,QuantityOpe4"
					+ " FROM " + dbo + "MachineResults WHERE FinishDateTime >= @start AND FinishDateTime <= @end";
				var param = cmd.CreateParameter();
                param.ParameterName = "@start";
				param.DbType = DbType.DateTime;
				param.Value = startUTC.ToLocalTime();
				cmd.Parameters.Add(param);
				param = cmd.CreateParameter();
                param.ParameterName = "@end";
				param.DbType = DbType.DateTime;
				param.Value = endUTC.ToLocalTime();
				cmd.Parameters.Add(param);
				
				var ret = new List<MachineResults>();
				
				using (var reader = cmd.ExecuteReader()) {
					while (reader.Read()) {
						
						var m = new MachineResults();

						m.StartDateTimeLocal = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Local);
						m.EndDateTimeLocal = DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Local);
						m.StartDateTimeUTC = m.StartDateTimeLocal.ToUniversalTime();
						m.EndDateTimeUTC = m.EndDateTimeLocal.ToUniversalTime();
						
						m.DeviceID = reader.GetInt32(2);
						m.PalletID = reader.GetInt32(3);
						m.FixtureNumber = reader.GetInt32(4);
						if (!reader.IsDBNull(5))
							m.FixtureName = reader.GetString(5);
						m.FixtureComment = reader.GetString(6);
						m.OrderName = reader.GetString(7);
						m.PartName = reader.GetString(8);
						if (!reader.IsDBNull(9))
							m.Revision = reader.GetString(9);
						m.ProcessNum = reader.GetInt32(10);
						m.JobNum = reader.GetInt32(11);
						if (!reader.IsDBNull(12))
							m.ProcessName = reader.GetString(12);
						m.Program = reader.GetString(13);
						m.SpindleTimeSeconds = reader.GetInt32(14);
						m.OperQuantities = new List<int>();
						int numOper = reader.GetInt32(15);
						for (int i = 0; i < Math.Min(4, numOper); i++) {
							m.OperQuantities.Add(reader.GetInt32(16+i));
						}
						
						ret.Add(m);						
					}
				}
				
				return ret;
			}
		}
		
		public class WorkSetResults
		{
			public DateTime StartDateTimeUTC;
			public DateTime EndDateTimeUTC;
			
			public int DeviceID;
			
			/* PalletID and FixtureNumber uniquely identifiy the location on the pallet */
			public int PalletID;
			public int FixtureNumber;
			
			/* data about the fixture entered by the user */
			public string FixtureName;
			public string FixtureComment;
			
			public string UnloadOrderName;
			public string LoadOrderName;
			
			/* Part, revision, process, and job uniquely identify which step we are on */
			public string UnloadPartName;
			public string UnloadRevision;
			public int UnloadProcessNum;
			public int UnloadJobNum;			
			public string LoadPartName;
			public string LoadRevision;
			public int LoadProcessNum;
			public int LoadJobNum;
			
			/* Process name is just some data about the process entered by the user,
		     * not used as a key or anything like that */
			public string UnloadProcessName;
			public string LoadProcessName;

			public List<int> LoadQuantities;
			public List<int> UnloadNormalQuantities;
			public List<int> UnloadScrapQuantities;
			public List<int> UnloadOutProcQuantities; /* TODO: What is this? */
			
			/* At the load station, the operator can push a button saying nothing was unloaded.
			 * This still adds an entry to the log but it is marked as a remachine, and no quantities are updated
			 */
			public bool Remachine; 
			
			/* Fields not loaded
			  * OperationType: 
			  *    seems to be 2 for load and unload, 6 for no load or unload, 4 for unload only,
			  *    and 1 for load only, plus 3 shows up sometimes.
			  * 
			  * OperationStatus:
			  *    not sure what this is
			  * 
			  * ErrorCode:
			  *    always null in my test data
			  * 
			  * ResultVersion
			  *    always 1 in the test data
			  */
			
		}
		
		public List<WorkSetResults> QueryLoadUnloadResults(DateTime startUTC, DateTime endUTC)
		{
			using (var cmd = _db.CreateCommand()) {
				cmd.CommandText = "SELECT StartDateTime,FinishDateTime,DeviceID,PalletID,"
					+ "FixtureNumber,FixtureName,FixtureComment,"
					+ "UnclampOrderName,UnclampPartName,UnclampRevision,UnclampProcessNumber,UnclampJobNumber,UnclampProcessName,"
					+ "ClampOrderName,ClampPartName,ClampRevision,ClampProcessNumber,ClampJobNumber,ClampProcessName,"
					+ "Remachine,"
					+ "ClampQuantityOpe1,ClampQuantityOpe2,ClampQuantityOpe3,ClampQuantityOpe4,"
				    + "UnclampNormalQtyOpe1, UnclampNormalQtyOpe2, UnclampNormalQtyOpe3, UnclampNormalQtyOpe4,"
					+ "UnclampScrapQtyOpe1, UnclampScrapQtyOpe2, UnclampScrapQtyOpe3, UnclampScrapQtyOpe4,"
					+ "UnclampOutProcQtyOpe1, UnclampOutProcQtyOpe2, UnclampOutProcQtyOpe3, UnclampOutProcQtyOpe4"					
					+ " FROM " + dbo + "WorkSetResults WHERE FinishDateTime >= @start AND FinishDateTime <= @end";
				var param = cmd.CreateParameter();
                param.ParameterName = "@start";
				param.DbType = DbType.DateTime;
				param.Value = startUTC.ToLocalTime();
				cmd.Parameters.Add(param);
				param = cmd.CreateParameter();
                param.ParameterName = "@end";
				param.DbType = DbType.DateTime;
				param.Value = endUTC.ToLocalTime();
				cmd.Parameters.Add(param);
				
				var ret = new List<WorkSetResults>();
				
				using (var reader = cmd.ExecuteReader()) {
					while (reader.Read()) {
						
						var m = new WorkSetResults();
						
						m.StartDateTimeUTC = DateTime.SpecifyKind(reader.GetDateTime(0),DateTimeKind.Local);
						m.EndDateTimeUTC = DateTime.SpecifyKind(reader.GetDateTime(1),DateTimeKind.Local);
						m.StartDateTimeUTC = m.StartDateTimeUTC.ToUniversalTime();
						m.EndDateTimeUTC = m.EndDateTimeUTC.ToUniversalTime();
						
						m.DeviceID = reader.GetInt32(2);
						m.PalletID = reader.GetInt32(3);
						m.FixtureNumber = reader.GetInt32(4);
						if (!reader.IsDBNull(5))
							m.FixtureName = reader.GetString(5);
						if (!reader.IsDBNull(6))
							m.FixtureComment = reader.GetString(6);
						if (!reader.IsDBNull(7)) {
							m.UnloadOrderName = reader.GetString(7);
							m.UnloadPartName = reader.GetString(8);
							if (!reader.IsDBNull(9))
								m.UnloadRevision = reader.GetString(9);
							m.UnloadProcessNum = reader.GetInt32(10);
							m.UnloadJobNum = reader.GetInt32(11);
							if (!reader.IsDBNull(12))
								m.UnloadProcessName = reader.GetString(12);
						}
						if (!reader.IsDBNull(13)) {
							m.LoadOrderName = reader.GetString(13);
							m.LoadPartName = reader.GetString(14);
							if (!reader.IsDBNull(15))
								m.LoadRevision = reader.GetString(15);
							m.LoadProcessNum = reader.GetInt32(16);
							m.LoadJobNum = reader.GetInt32(17);
							if (!reader.IsDBNull(18))
								m.LoadProcessName = reader.GetString(18);
						}
						m.Remachine = Convert.ToBoolean(reader.GetValue(19));
						
						m.LoadQuantities = new List<int>();
						m.UnloadNormalQuantities = new List<int>();
						m.UnloadScrapQuantities = new List<int>();
						m.UnloadOutProcQuantities = new List<int>();
						for (int i = 0; i < 4; i++) {
							if (reader.GetInt32(20+i) != 0)
								m.LoadQuantities.Add(reader.GetInt32(20+i));
							if (reader.GetInt32(24+i) != 0)
								m.UnloadNormalQuantities.Add(reader.GetInt32(24+i));
							if (reader.GetInt32(28+i) != 0)
								m.UnloadScrapQuantities.Add(reader.GetInt32(28+i));
							if (reader.GetInt32(32+i) != 0)
								m.UnloadOutProcQuantities.Add(reader.GetInt32(32+i));
						}
						
						ret.Add(m);						
					}
				}
				
				return ret;
			}
		}

		//A common value from the CommonValue table holds values that were produced by 
		//the execution of the part program.  This is commonly hold results.
		public class CommonValue
		{
			public MachineResults ParentMachineResults;
			public DateTime ExecDateTimeUTC;

			public int Number;
			public string Value;

			/* Fields Not Loaded
			 * 
			 * - PalletNumber
			 * - MainONNumber
			 * - ExecOnNumber
			 * - SequenceNumber
			 */
		}

		public List<CommonValue> QueryCommonValues(MachineResults mach)
		{
			using (var cmd = _db.CreateCommand()) {
				cmd.CommandText = "SELECT ExecDateTime,Number,Value"
					+ " FROM " + dbo + "CommonValues "
					+ " WHERE ExecDateTime >= @start AND ExecDateTime <= @end AND DeviceID = @dev";
				var param = cmd.CreateParameter();
                param.ParameterName = "@start";
				param.DbType = DbType.DateTime;
				param.Value = mach.StartDateTimeLocal;
				cmd.Parameters.Add(param);
				param = cmd.CreateParameter();
                param.ParameterName = "@end";
				param.DbType = DbType.DateTime;
				param.Value = mach.EndDateTimeLocal;
				cmd.Parameters.Add(param);
				param = cmd.CreateParameter();
                param.ParameterName = "@dev";
				param.DbType = DbType.Int64;
				param.Value = mach.DeviceID;
				cmd.Parameters.Add(param);

				var ret = new List<CommonValue>();

				using (var reader = cmd.ExecuteReader()) {
					while (reader.Read()) {
						var v = new CommonValue();
						v.ParentMachineResults = mach;
						var execLocal = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Local);
						v.ExecDateTimeUTC = execLocal.ToUniversalTime();
						v.Number = reader.GetInt32(1);
						v.Value = reader.GetValue(2).ToString();
						ret.Add(v);
					}
				}

				return ret;
			}
		}

        #endregion
		
		#region Current Status

		public IDictionary<int, PalletLocation> Devices()
		{
			using (var cmd = _db.CreateCommand()) {
			//Makino: Devices				
			var devices = new Dictionary<int, PalletLocation>();
				
				cmd.CommandText = "SELECT DeviceID, DeviceType, DeviceNumber FROM " + dbo + "Devices";
				
				using (var reader = cmd.ExecuteReader()) {
					while (reader.Read()) {
						var devID = reader.GetInt32(0);
                        //some versions of makino use short
						var dType = Convert.ToInt32(reader.GetValue(1));
						var dNum = Convert.ToInt32(reader.GetValue(2));
					
						devices.Add(devID, ParseDevice(dType, dNum));
					}
				}
			
				return devices;
			}
		}
		
		public CurrentStatus LoadCurrentInfo()
		{
			using (var cmd = _db.CreateCommand()) {
				
				var map = new MakinoToJobMap(_logDb, _inspDb);
				var palMap = new MakinoToPalletMap();
				
				Load("SELECT PartID, ProcessNumber, ProcessID FROM " + dbo + "Processes", reader => {
					map.AddProcess(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));											
				});
				
				//Makino: Parts   MachineWatch: Jobs
				Load("SELECT PartID, PartName, Revision, Comment, Priority FROM " + dbo + "Parts", reader => {				
						var partID = reader.GetInt32(0);
						
						var partName = reader.GetString(1);
						if (!reader.IsDBNull(2))
							partName += reader.GetString(2);
						
						var job = map.CreateJob(partName, partID);

                        job.JobCopiedToSystem = true;
						job.PartName = reader.GetString(1);
						if (!reader.IsDBNull(3))
							job.Comment = reader.GetString(3);
						if (!reader.IsDBNull(4))
							job.Priority = reader.GetInt16(4);
						job.ManuallyCreatedJob = false;
						job.CreateMarkerData = false;
				});

				
				var devices = Devices();
				
				Load("SELECT ProcessID, JobNumber, JobID FROM " + dbo + "Jobs", reader => {
					map.AddJobToProcess(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
				});
				
				Load("SELECT a.JobID, b.Name FROM " + dbo + "MachineJobs a " +
					"INNER JOIN " + dbo + "NCProgramFiles b " +
					"ON a.NCProgramFileID = b.NCProgramFileID", reader => {
					map.AddProgramToJob(reader.GetInt32(0), reader.GetString(1));
				});
				
				Load("SELECT JobID, FeasibleDeviceID FROM " + dbo + "JobFeasibleDevices", reader => {
					map.AddAllowedStationToJob(reader.GetInt32(0), devices[reader.GetInt32(1)]);
				});
				
				map.CompleteStations();
				
				Load("SELECT a.PalletFixtureID, a.FixtureNumber, a.FixtureID, b.PalletNumber, c.CurDeviceType, c.CurDeviceNumber " +
					"FROM " + dbo + "PalletFixtures a, " + dbo + "Pallets b, " + dbo + "PalletLocation c " +
					"WHERE a.PalletID = b.PalletID AND a.PalletID = c.PalletID " +
					" AND a.FixtureID IS NOT NULL", reader => {
					
					var loc = new PalletLocation(PalletLocationEnum.Buffer, "Unknown", 0);
					if (!reader.IsDBNull(4) && !reader.IsDBNull(5))
						loc = ParseDevice(reader.GetInt16(4), reader.GetInt16(5));
												
					palMap.AddPalletInfo(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
						reader.GetInt32(3), loc);
				});

				Load("SELECT ProcessID, FixtureID FROM " + dbo + "FixtureProcesses", reader => {
					map.AddFixtureToProcess(reader.GetInt32(0), reader.GetInt32(1),
						palMap.PalletsForFixture(reader.GetInt32(1)));
				});
								
				Load("SELECT OrderID, OrderName, PartID, Comment, Quantity, Priority, StartDate " +
					"FROM " + dbo + "Orders", reader => {
					
					var newJob = map.DuplicateForOrder(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2));						
					
					if (!reader.IsDBNull(3))
						newJob.Comment = reader.GetString(3);
					
					newJob.SetPlannedCyclesOnFirstProcess(1, reader.GetInt32(4));
					
					if (!reader.IsDBNull(5))
						newJob.Priority = reader.GetInt16(5);
					
					DateTime start = DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Local);
					start = start.ToUniversalTime();
					
					for (int i = 1; i <= newJob.NumProcesses; i++)
						newJob.SetSimulatedStartingTimeUTC(i, 1, start);					
				});
				
				Load("SELECT OrderID, ProcessID, RemainingQuantity, NormalQuantity, ScrapQuantity " +
					"FROM " + dbo + "Quantities", reader => {
					map.AddQuantityToProcess(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(3));
				});
				
				Load("SELECT PalletFixtureID, CurProcessID, CurOrderID, CurJobID, WorkStatus " +
					"FROM " + dbo + "FixtureRun WHERE CurProcessID IS NOT NULL", reader => {
					
					int palfixID = reader.GetInt32(0);
					int orderID = reader.GetInt32(2);
					var job = map.JobForOrder(orderID);
					
					string orderName = "";
					if (job != null)
						orderName = job.UniqueStr;
					
					// Lookup (pallet,location) for this palfixID
					int palletNum;
					int fixtureNum;					
					palMap.PalletLocInfo(palfixID, out palletNum, out fixtureNum);
					
					//look for material id
					IList<StatusDB.MatIDRow> matIDs;
					if (_status == null)
						matIDs = new List<StatusDB.MatIDRow>();
					else
						matIDs = _status.FindMaterialIDs(palletNum, fixtureNum, DateTime.UtcNow.AddSeconds(10));
					
					//check material ids share the same order
					if (orderName != "") {
						foreach (var m in matIDs) {
							if (m.Order != orderName) {
								OutputTrace(System.Diagnostics.TraceEventType.Information,
								            "Current material on pallet " + palletNum.ToString() +
								            " loc " + fixtureNum.ToString() + " was expecting orderID " +
								            orderID.ToString() + " for order " + orderName.ToString() +
								            ", but found order " + m.Order + " for part loaded at " + m.LoadedUTC.ToString());
								matIDs.Clear();
								break;
							}
						}
					}
					
					if (matIDs.Count == 0) {
						var m = default(StatusDB.MatIDRow);
						m.MatID = -1;
						matIDs.Add(m);
					}
						
					foreach (var m in matIDs) {
						var inProcMat =
                            map.CreateMaterial(
                                orderID, reader.GetInt32(1), reader.GetInt32(3), palletNum,
                                fixtureNum, m.MatID);
					
						palMap.AddMaterial(palfixID, inProcMat);
					}
				});
				
				//There is a MovePalletFixtureID column which presumebly means rotate through process?
				//Other columns include remachining, cancel, recleaning, offdutyprocess, operation status
				Load("SELECT PalletFixtureID, UnclampJobID, UnclampOrderID, ClampJobID, ClampOrderID, ClampQuantity " +
					"FROM " + dbo + "WorkSetCommand WHERE PalletFixtureID IS NOT NULL", reader => {

					var palfixID = reader.GetInt32(0);
					
					if (!reader.IsDBNull(1) && !reader.IsDBNull(2)) {
						var unclampJobID = reader.GetInt32(1);
						var unclampOrder = reader.GetInt32(2);
						
						var procNum = map.ProcessForJobID(unclampJobID);
						var job = map.JobForOrder(unclampOrder);
						if (job != null)
							palMap.SetMaterialAsUnload(palfixID, job.NumProcesses == procNum);
					}
					
					if (!reader.IsDBNull(3) && !reader.IsDBNull(4)) {
						var clampJobID = reader.GetInt32(3);
						var clampOrder = reader.GetInt32(4);
						
						var procNum = map.ProcessForJobID(clampJobID);
						var job = map.JobForOrder(clampOrder);
						int qty = 1;
						if (!reader.IsDBNull(5))
							qty = reader.GetInt32(5);
						
						if (job != null)
							palMap.AddMaterialToLoad(palfixID, job.UniqueStr, job.PartName, procNum, qty);
					}
					

				});

                //TODO: inspections (from jobdb), holds

                var st = new CurrentStatus();
                foreach (var j in map.Jobs) st.Jobs.Add(j.UniqueStr, j);
                foreach (var p in palMap.Pallets) st.Pallets.Add(p);
                foreach (var m in palMap.Material) st.Material.Add(m);
                return st;
			}
		}
		
		private void Load(string command, Action<IDataReader> onEachRow) {
			trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
				"Loading " + command.Substring(7));
			
			using (var cmd = _db.CreateCommand()) {
				cmd.CommandText = command;
				using (var reader = cmd.ExecuteReader()) {
					while (reader.Read()) {
						
						var row = "   ";
						for (int i = 0; i < reader.FieldCount; i++) {
							row += reader.GetName(i) + " ";
							row += reader.GetDataTypeName(i) + ": ";
							if (reader.IsDBNull(i)) {
								row += "(null)";
							} else {
								row += reader.GetValue(i).ToString();
							}
							row += "  ";
						}
						trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
							row);
						
						onEachRow(reader);
					}
				}
			}
		}
		
		private static PalletLocation ParseDevice(int deviceType, int deviceNumber)
		{
			var ret = new PalletLocation(PalletLocationEnum.Buffer, "Unknown", 0);
			
			if (deviceType == 1)
				ret = new PalletLocation(PalletLocationEnum.Machine, "MC", deviceNumber);
			else if (deviceType == 2)
				ret = new PalletLocation(PalletLocationEnum.Cart, "Cart", deviceNumber);
			else if (deviceType == 3)
				ret = new PalletLocation(PalletLocationEnum.LoadUnload, "L/U", deviceNumber);
			else if (deviceType == 4)
				ret = new PalletLocation(PalletLocationEnum.Buffer, "Buffer", deviceNumber);
			//dType = 5 is Tool Crib
			//dType = 7 is Presetter
			return ret;
		}
		
#if DEBUG
		public static List<string> errors = new List<string>();
#endif
		
		private void OutputTrace(System.Diagnostics.TraceEventType t, string msg)
		{
			if (trace == null) {
#if DEBUG
				errors.Add(msg);
#else
				throw new ApplicationException(msg);
#endif
			} else {
				trace.TraceEvent(t, 0, msg);
			}
		}
		#endregion
	}
}

