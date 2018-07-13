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
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;

namespace Makino
{
	public class MakinoBackend : IFMSBackend, IFMSImplementation
	{
        private static Serilog.ILogger Log = Serilog.Log.ForContext<MakinoBackend>();

		// Common databases from machine framework
		private JobDB _jobDB;
		private JobLogDB _log;

		// Makino databases
		private MakinoDB _makinoDB;
		private StatusDB _status;

		private Jobs _jobs;
#pragma warning disable 649
		private LogTimer _logTimer;
#pragma warning restore 649
        private string _dataDirectory;

		public void Init(string dataDir, IConfig cfg, FMSSettings settings)
		{
			try {

				string adePath = cfg.GetValue<string>("Makino", "ADE Path");
                if (string.IsNullOrEmpty(adePath))
                {
                    adePath = @"c:\Makino\ADE";
                }

                string dbConnStr = cfg.GetValue<string>("Makino", "SQL Server Connection String");
                if (string.IsNullOrEmpty(dbConnStr))
                {
                    dbConnStr = DetectSqlConnectionStr();
                }

                bool downloadOnlyOrders = cfg.GetValue<bool>("Makino", "Download Only Orders");

				Log.Information(
                    "Starting makino backend. Connection Str: {connStr}, ADE Path: {path}, DownloadOnlyOrders: {downOnlyOrders}",
                     dbConnStr, adePath, downloadOnlyOrders);

				_dataDirectory = dataDir;

#if DEBUG
                var logConn = SqliteExtensions.ConnectMemory();
                logConn.Open();
                _log = new JobLogDB(logConn);
                _log.CreateTables(firstMaterialId: null);

                var jobConn = SqliteExtensions.ConnectMemory();
                jobConn.Open();
                _jobDB = new JobDB(jobConn);
                _jobDB.CreateTables();

                var statusConn = SqliteExtensions.ConnectMemory();
                statusConn.Open();
                _status = new StatusDB(statusConn);
                _status.CreateTables();

                _makinoDB = new MakinoDB(MakinoDB.DBTypeEnum.SqlLocal, "", _status, _log);
                _logTimer = new LogTimer(_log, _jobDB, _makinoDB, _status, settings);
#else
                _log = new JobLogDB();
                _log.Open(
                    System.IO.Path.Combine(_dataDirectory, "log.db"),
                    System.IO.Path.Combine(_dataDirectory, "inspections.db")
                );

                _jobDB = new BlackMaple.MachineFramework.JobDB();
                _jobDB.Open(System.IO.Path.Combine(_dataDirectory, "jobs.db"));

                _status = new StatusDB(System.IO.Path.Combine(_dataDirectory, "makino.db"));

                _makinoDB = new MakinoDB(MakinoDB.DBTypeEnum.SqlConnStr, dbConnStr, _status, _log, dbTrace);
                _logTimer = new LogTimer(_log, _jobDB, _makinoDB, _status, settings, logTrace);
#endif

                _jobs = new Jobs(_makinoDB, _jobDB, adePath, downloadOnlyOrders);

                _logTimer.LogsProcessed += OnLogsProcessed;

			} catch (Exception ex) {
                Log.Error(ex, "Error when initializing makino backend");
			}
		}

		public void Halt()
		{
            _logTimer.LogsProcessed -= OnLogsProcessed;
			if (_logTimer != null) _logTimer.Halt();
			if (_jobDB != null) _jobDB.Close();
			if (_log != null) _log.Close();
			if (_status != null) _status.Close();
            if (_makinoDB != null) _makinoDB.Close();
		}

        private void OnLogsProcessed()
        {
            _jobs.RaiseNewCurrentStatus(_jobs.GetCurrentStatus());
        }

        private string DetectSqlConnectionStr()
        {
            var b = new System.Data.SqlClient.SqlConnectionStringBuilder();
            b.UserID = "sa";
            b.Password = "M@k1n0Admin";
            b.InitialCatalog = "Makino";
            b.DataSource = "(local)";
            return b.ConnectionString;
        }

		public IJobDatabase JobDatabase()
		{
			return _jobDB;
		}

		public IJobControl JobControl()
		{
			return _jobs;
		}

		public IOldJobDecrement OldJobDecrement()
		{
			return _jobs;
		}

		public IInspectionControl InspectionControl()
		{
			return _log;
		}

		public ILogDatabase LogDatabase()
		{
			return _log;
		}

        public LogTimer LogTimer
        {
            get { return _logTimer; }
        }

        public string DataDir
        {
            get { return _dataDirectory; }
        }

    public FMSInfo Info => new FMSInfo()
		{
			Name = "Makino",
			Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString()
		};
    public IFMSBackend Backend => this;
    public IList<IBackgroundWorker> Workers => new List<IBackgroundWorker>();
  }

  public static class MakinoProgram
  {
    public static void Main()
    {
#if DEBUG
      var useService = false;
#else
			var useService = true;
#endif
      Program.Run(useService, new MakinoBackend());
    }
  }
}

