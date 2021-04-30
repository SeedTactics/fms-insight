/* Copyright (c) 2020, John Lenz

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
using System.Threading;
using System.IO;

namespace MazakMachineInterface
{
  public enum LogCode
  {
    MachineCycleStart = 441,
    MachineCycleEnd = 442,

    LoadBegin = 501,
    LoadEnd = 502,

    UnloadBegin = 511,
    UnloadEnd = 512,

    PalletMoving = 301,
    PalletMoveComplete = 302,

    // 431 and 432 are used when rotating something into machine, no matter if a
    // different pallet is rotating out or not.
    StartRotatePalletIntoMachine = 431, // event has pallet moving into machine
    //EndRotatePalletIntoMachine = 432, // event has pallet moving out of machine if it exists, otherwise pallet = 0

    // 433 and 434 are only used if nothing is being sent in.
    StartRotatePalletOutOfMachine = 433, // event has pallet moving out of machine
    //EndRotatePalletOutOfMachine = 434, // event also has pallet moving out of machine

    //StartOffsetProgram = 435,
    //EndOffsetProgram = 436
  }

  public record LogEntry
  {
    public DateTime TimeUTC { get; init; }
    public LogCode Code { get; init; }
    public string ForeignID { get; init; }

    //Only sometimes filled in depending on the log code
    public int Pallet { get; init; }
    public string FullPartName { get; init; } //Full part name in the mazak system
    public string JobPartName { get; init; }  //Part name with : stripped off
    public int Process { get; init; }
    public int FixedQuantity { get; init; }
    public string Program { get; init; }
    public int StationNumber { get; init; }

    //Only filled in for pallet movement
    public string TargetPosition { get; init; }
    public string FromPosition { get; init; }
  }

  public delegate void MazakLogEventDel(LogEntry e, BlackMaple.MachineFramework.IRepository jobDB);
  public interface INotifyMazakLogEvent
  {
    event MazakLogEventDel MazakLogEvent;
  }
  public delegate void NewEntriesDel();
  public interface IMazakLogReader : INotifyMazakLogEvent
  {
    void RecheckQueues(bool wait);
    void Halt();
  }

#if USE_OLEDB
	public class LogDataVerE : IMazakLogReader, INotifyMazakLogEvent
	{
    private const string DateTimeFormat = "yyyyMMddHHmmss";

    private MazakConfig _mazakConfig;
    private BlackMaple.MachineFramework.JobDB.Config _jobDBCfg;
    private BlackMaple.MachineFramework.EventLogDB.Config _logCfg;
    private MazakQueues _queues;
    private IReadDataAccess _readDB;
    private IMachineGroupName _machGroupName;
    private IHoldManagement _hold;
    private BlackMaple.MachineFramework.ISendMaterialToExternalQueue _sendToExternal;
    private BlackMaple.MachineFramework.FMSSettings FMSSettings { get; set; }
    private static Serilog.ILogger Log = Serilog.Log.ForContext<LogDataVerE>();
    private Action<BlackMaple.MachineFramework.JobDB, BlackMaple.MachineFramework.EventLogDB> _currentStatusChanged;

    private object _lock;
    private System.Timers.Timer _timer;

    public event MazakLogEventDel MazakLogEvent;

    public LogDataVerE(BlackMaple.MachineFramework.EventLogDB.Config logCfg,
                       BlackMaple.MachineFramework.JobDB.Config jobDBCfg,
                       BlackMaple.MachineFramework.ISendMaterialToExternalQueue send,
                       IMachineGroupName machGroupName,
                       IReadDataAccess readDB,
                       MazakQueues queues,
                       IHoldManagement hold,
                       BlackMaple.MachineFramework.FMSSettings settings,
                       Action<BlackMaple.MachineFramework.JobDB, BlackMaple.MachineFramework.EventLogDB> currentStatusChanged,
                       MazakConfig mazakConfig
                      )
    {
      _logCfg = logCfg;
      _currentStatusChanged = currentStatusChanged;
      _jobDBCfg = jobDBCfg;
      _hold = hold;
      _readDB = readDB;
      _mazakConfig = mazakConfig;
      _queues = queues;
      _sendToExternal = send;
      _machGroupName = machGroupName;
      FMSSettings = settings;
      _lock = new object();
      _timer = new System.Timers.Timer(TimeSpan.FromMinutes(1).TotalMilliseconds);
      _timer.Elapsed += HandleElapsed;
      _timer.Start();
    }

    public void Halt()
    {
      _timer.Stop();
    }

    public void RecheckQueues(bool wait)
    {
      //do nothing, wait for 1 minute timeout
    }

    private void HandleElapsed(object sender, System.Timers.ElapsedEventArgs e)
    {
      lock (_lock)
      {
        try
        {
          var mazakData = _readDB.LoadStatusAndTools();
          List<LogEntry> logs;
          var sendToExternal = new List<BlackMaple.MachineFramework.MaterialToSendToExternalQueue>();

          using (var logDB = _logCfg.OpenConnection())
          using (var jobDB = _jobDBCfg.OpenConnection()) {
            logs = LoadLog(logDB.MaxForeignID());
            var trans = new LogTranslation(jobDB, logDB, mazakData, _machGroupName, FMSSettings,
              le => MazakLogEvent?.Invoke(le, jobDB, logDB),
              mazakConfig: _mazakConfig
            );
            foreach (var ev in logs)
            {
              try
              {
                sendToExternal.AddRange(trans.HandleEvent(ev));
              }
              catch (Exception ex)
              {
                Log.Error(ex, "Error translating log event at time " + ev.TimeUTC.ToLocalTime().ToString());
              }
            }

            var palStChanged = trans.CheckPalletStatusMatchesLogs();

            var queuesChanged = _queues.CheckQueues(jobDB, logDB, mazakData);

            _hold.CheckForTransition(mazakData, jobDB);

            if (logs.Count > 0 || queuesChanged || palStChanged)
            {
              _currentStatusChanged(jobDB, logDB);
            }
          }

          if (sendToExternal.Count > 0) {
            _sendToExternal.Post(sendToExternal);
          }
        }
        catch (Exception ex)
        {
          Log.Error(ex, "Unhandled error processing log");
        }
      }
    }

		public List<LogEntry> LoadLog (string lastForeignID)
		{
			return _readDB.WithReadDBConnection(conn =>
			{
                var trans = conn.BeginTransaction();
                try {

                    using (System.Data.OleDb.OleDbCommand cmd = (System.Data.OleDb.OleDbCommand)conn.CreateCommand()) {
                    ((System.Data.IDbCommand)cmd).Transaction = trans;

                    long epoch = 1;
                    long lastID = 0;
                    DateTime lastDate = DateTime.MinValue;
                    bool useDate = false;
                    string[] s = lastForeignID.Split('-');
                    if (s.Length == 1) {
                        epoch = 1;
                        if (!long.TryParse(s[0], out lastID))
                            useDate = true;
                    } else if (s.Length == 2) {
                        if (!long.TryParse(s[0], out epoch))
                            useDate = true;
                        if (!long.TryParse(s[1], out lastID))
                            useDate = true;
                    } else if (s.Length == 3) {
                        if (!long.TryParse(s[0], out epoch))
                            useDate = true;
                        if (!long.TryParse(s[1], out lastID))
                            useDate = true;
                        lastDate = DateTime.ParseExact(s[2], DateTimeFormat, null);
                    } else {
                        useDate = true;
                    }

                    if (useDate) {
                        cmd.CommandText = "SELECT ID, Date, LogMessageCode, ResourceNumber, PartName, ProcessNumber," +
                            "FixedQuantity, PalletNumber, ProgramNumber, FromPosition, ToPosition " +
                            "FROM Log WHERE Date > ? ORDER BY ID ASC";
                        var param = cmd.CreateParameter();
                        param.OleDbType = System.Data.OleDb.OleDbType.Date;
                        param.Value = DateTime.Now.AddDays(-7);
                        cmd.Parameters.Add(param);
                    } else {
                        CheckIDRollover(trans, conn, ref epoch, ref lastID, lastDate);

                        cmd.CommandText = "SELECT ID, Date, LogMessageCode, ResourceNumber, PartName, ProcessNumber," +
                            "FixedQuantity, PalletNumber, ProgramNumber, FromPosition, ToPosition " +
                            "FROM Log WHERE ID > ? ORDER BY ID ASC";
                        var param = cmd.CreateParameter();
                        param.OleDbType = System.Data.OleDb.OleDbType.Numeric;
                        param.Value = lastID;
                        cmd.Parameters.Add(param);
                    }

                    var ret = new List<LogEntry>();

                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {

                            if (reader.IsDBNull(0)) continue;
                            if (reader.IsDBNull(1)) continue;
                            if (reader.IsDBNull(2)) continue;
                            if (!Enum.IsDefined(typeof(LogCode), reader.GetInt32(2))) continue;


                            var e = new LogEntry();

                            e.ForeignID = epoch.ToString() + "-" + reader.GetInt32(0).ToString()
                                + "-" + reader.GetDateTime(1).ToString(DateTimeFormat);
                            e.TimeUTC = new DateTime(reader.GetDateTime(1).Ticks, DateTimeKind.Local);
                            e.TimeUTC = e.TimeUTC.ToUniversalTime();
                            e.Code = (LogCode)reader.GetInt32(2);
                            e.StationNumber = reader.IsDBNull(3) ? -1 : reader.GetInt32(3);
                            e.FullPartName = reader.IsDBNull(4) ? "" : reader.GetString(4);
                            e.Process = reader.IsDBNull(5) ? 1 : reader.GetInt32(5);
                            e.FixedQuantity = reader.IsDBNull(6) ? 1 : reader.GetInt32(6);
                            e.Pallet = reader.IsDBNull(7) ? -1 : reader.GetInt32(7);
                            e.Program = reader.IsDBNull(8) ? "" : reader.GetInt32(8).ToString();
                            e.FromPosition = reader.IsDBNull(9) ? "" : reader.GetString(9);
                            e.TargetPosition = reader.IsDBNull(10) ? "" : reader.GetString(10);

                            int idx = e.FullPartName.IndexOf(':');
                            if (idx > 0)
                                e.JobPartName = e.FullPartName.Substring(0, idx);
                            else
                                e.JobPartName = e.FullPartName;

                            ret.Add(e);
                        }
                    }
                    trans.Commit();

                    return ret;
                    }
                } catch {
                    trans.Rollback();
                    throw;
                }
            });
		}

		private void CheckIDRollover(System.Data.IDbTransaction trans, System.Data.IDbConnection conn,
			ref long epoch, ref long lastID, DateTime lastDate)
		{
			using (var cmd = conn.CreateCommand()) {
			cmd.Transaction = trans;
			cmd.CommandText = "SELECT Date FROM Log WHERE ID = ?";
            var param = (System.Data.OleDb.OleDbParameter)cmd.CreateParameter();
            param.OleDbType = System.Data.OleDb.OleDbType.Numeric;
            param.Value = lastID;
            cmd.Parameters.Add(param);

			using (var reader = cmd.ExecuteReader()) {
				bool foundLine = false;
				while (reader.Read()) {
					foundLine = true;
					if (lastDate != DateTime.MinValue && reader.GetDateTime(0) != lastDate) {
						//roll to new epoch since the date for this ID is different
						epoch += 1;
						lastID = 0;
					}
					break;
				}

				if (!foundLine) {
					//roll to new epoch since no ID is found, the data has been deleted
					epoch += 1;
					lastID = 0;
				}
			}
      }
		}
	}
#endif

  public class LogDataWeb : IMazakLogReader, INotifyMazakLogEvent
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<LogDataWeb>();

    private MazakConfig _mazakConfig;
    private BlackMaple.MachineFramework.RepositoryConfig _logDbCfg;
    private IReadDataAccess _readDB;
    private IMachineGroupName _machGroupName;
    private IHoldManagement _hold;
    private BlackMaple.MachineFramework.FMSSettings _settings;
    private BlackMaple.MachineFramework.ISendMaterialToExternalQueue _sendToExternal;
    private MazakQueues _queues;
    private Action<BlackMaple.MachineFramework.IRepository> _currentStatusChanged;

    private string _path;
    private AutoResetEvent _shutdown;
    private AutoResetEvent _newLogFile;
    private AutoResetEvent _recheckQueues;
    private ManualResetEvent _recheckQueuesComplete;

    private Thread _thread;
    private FileSystemWatcher _watcher;

    public LogDataWeb(string path,
                      BlackMaple.MachineFramework.RepositoryConfig logCfg,
                      IMachineGroupName machineGroupName,
                      BlackMaple.MachineFramework.ISendMaterialToExternalQueue sendToExternal,
                      IReadDataAccess readDB,
                      MazakQueues queues,
                      IHoldManagement hold,
                      BlackMaple.MachineFramework.FMSSettings settings,
                      Action<BlackMaple.MachineFramework.IRepository> currentStatusChanged,
                      MazakConfig mazakConfig)
    {
      _path = path;
      _logDbCfg = logCfg;
      _readDB = readDB;
      _queues = queues;
      _hold = hold;
      _settings = settings;
      _machGroupName = machineGroupName;
      _mazakConfig = mazakConfig;
      _currentStatusChanged = currentStatusChanged;
      _sendToExternal = sendToExternal;
      _shutdown = new AutoResetEvent(false);
      _newLogFile = new AutoResetEvent(false);
      _recheckQueues = new AutoResetEvent(false);
      _recheckQueuesComplete = new ManualResetEvent(false);
      if (System.IO.Directory.Exists(path))
      {
        _thread = new Thread(new ThreadStart(ThreadFunc));
        _thread.IsBackground = true;
        _thread.Start();
        _watcher = new FileSystemWatcher(_path);
        _watcher.Filter = "*.csv";

        CancellationTokenSource cancelSource = null;
        _watcher.Created += (sender, evt) =>
        {
          cancelSource?.Cancel();
          cancelSource = new CancellationTokenSource();
          System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(1), cancelSource.Token).ContinueWith(t =>
          {
            if (!t.IsCanceled)
            {
              _newLogFile.Set();
            }
          }, System.Threading.Tasks.TaskScheduler.Default);
        };

        _watcher.EnableRaisingEvents = true;
        Log.Debug("Watching {path} for new CSV files", _path);
      }
    }

    public event MazakLogEventDel MazakLogEvent;

    public void ThreadFunc()
    {
      bool stoppedBecauseRecentMachineEnd = false;
      for (; ; )
      {
        try
        {

          var sleepTime = stoppedBecauseRecentMachineEnd ? TimeSpan.FromSeconds(10) : TimeSpan.FromMinutes(1);
          Log.Debug("Sleeping for {mins} minutes", sleepTime.TotalMinutes);

          Thread.Sleep(TimeSpan.FromSeconds(1));

          var ret = WaitHandle.WaitAny(new WaitHandle[] { _shutdown, _newLogFile, _recheckQueues }, sleepTime, false);
          if (ret == 0)
          {
            Log.Debug("Thread shutdown");
            return;
          }
          bool recheckingQueues = ret == 2;

          ThreadPool.GetAvailableThreads(out var workerThreads, out var ioThreads);
          ThreadPool.GetMaxThreads(out var maxWorkerThreads, out var maxIoThreads);
          Log.Debug(
            "Waking up log thread for {reason}: total GC memory {mem}, worker threads {workerThreads}/{maxWorkerThreads}, IO threads {ioThreads}/{maxIoThreads}",
            ret,
            GC.GetTotalMemory(false),
            workerThreads,
            maxWorkerThreads,
            ioThreads,
            maxIoThreads
          );

          var mazakData = _readDB.LoadStatusAndTools();

          Log.Debug("Loaded mazak schedules {@data}", mazakData);

          List<LogEntry> logs;
          var sendToExternal = new List<BlackMaple.MachineFramework.MaterialToSendToExternalQueue>();

          using (var logDb = _logDbCfg.OpenConnection())
          {
            logs = LoadLog(logDb.MaxForeignID());
            var trans = new LogTranslation(logDb, mazakData, _machGroupName, _settings,
              le => MazakLogEvent?.Invoke(le, logDb),
              mazakConfig: _mazakConfig
            );
            stoppedBecauseRecentMachineEnd = false;
            foreach (var ev in logs)
            {
              try
              {
                var result = trans.HandleEvent(ev);
                if (result.StoppedBecauseRecentMachineEnd)
                {
                  stoppedBecauseRecentMachineEnd = true;
                  break;
                }
                sendToExternal.AddRange(result.MatsToSendToExternal);
              }
              catch (Exception ex)
              {
                Log.Error(ex, "Error translating log event at time " + ev.TimeUTC.ToLocalTime().ToString());
              }
            }

            DeleteLog(logDb.MaxForeignID());

            bool palStChanged = false;
            if (!stoppedBecauseRecentMachineEnd)
            {
              palStChanged = trans.CheckPalletStatusMatchesLogs();
            }

            var queuesChanged = _queues.CheckQueues(logDb, mazakData);

            _hold.CheckForTransition(mazakData, logDb);

            if (logs.Count > 0 || queuesChanged || palStChanged)
            {
              _currentStatusChanged(logDb);
            }
          }

          if (sendToExternal.Count > 0)
          {
            _sendToExternal.Post(sendToExternal).Wait(TimeSpan.FromSeconds(30));
          }

          if (recheckingQueues)
          {
            _recheckQueuesComplete.Set();
          }

        }
        catch (Exception ex)
        {
          Log.Error(ex, "Error during log data processing");
        }
      }
    }

    public void Halt()
    {
      if (_watcher != null)
        _watcher.EnableRaisingEvents = false;
      _shutdown.Set();
    }

    public void RecheckQueues(bool wait)
    {
      if (wait)
      {
        _recheckQueuesComplete.Reset();
        _recheckQueues.Set();
        _recheckQueuesComplete.WaitOne(TimeSpan.FromSeconds(60));
      }
      else
      {
        _recheckQueues.Set();
      }
    }


    private FileStream WaitToOpenFile(string file)
    {
      int cnt = 0;
      while (cnt < 20)
      {
        try
        {
          return File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        catch (UnauthorizedAccessException ex)
        {
          // do nothing
          Log.Debug(ex, "Error opening {file}", file);
        }
        catch (IOException ex)
        {
          // do nothing
          Log.Debug(ex, "Error opening {file}", file);
        }
        Log.Debug("Could not open file {file}, sleeping for 10 seconds", file);
        Thread.Sleep(TimeSpan.FromSeconds(10));
      }
      throw new Exception("Unable to open file " + file);
    }

    public List<LogEntry> LoadLog(string lastForeignID)
    {
      var files = new List<string>(Directory.GetFiles(_path, "*.csv"));
      files.Sort();

      var ret = new List<LogEntry>();

      foreach (var f in files)
      {
        var filename = Path.GetFileName(f);
        if (filename.CompareTo(lastForeignID) <= 0)
          continue;

        using (var fstream = WaitToOpenFile(f))
        using (var stream = new StreamReader(fstream))
        {
          while (stream.Peek() >= 0)
          {

            var s = stream.ReadLine().Split(',');
            if (s.Length < 18)
              continue;

            int code;
            if (!int.TryParse(s[6], out code))
            {
              Log.Debug("Unable to parse code from log message {msg}", s);
              continue;
            }
            if (!Enum.IsDefined(typeof(LogCode), code))
            {
              Log.Debug("Unused log message {msg}", s);
              continue;
            }

            string fullPartName = s[10].Trim();
            int idx = fullPartName.IndexOf(':');

            var e = new LogEntry()
            {
              ForeignID = filename,
              TimeUTC = new DateTime(int.Parse(s[0]), int.Parse(s[1]), int.Parse(s[2]),
                                     int.Parse(s[3]), int.Parse(s[4]), int.Parse(s[5]),
                                     DateTimeKind.Local).ToUniversalTime(),
              Code = (LogCode)code,
              Pallet = (int.TryParse(s[13], out var pal)) ? pal : -1,
              FullPartName = fullPartName,
              JobPartName = (idx > 0) ? fullPartName.Substring(0, idx) : fullPartName,
              Process = int.TryParse(s[11], out var proc) ? proc : 0,
              FixedQuantity = int.TryParse(s[12], out var fixQty) ? fixQty : 0,
              Program = s[14],
              StationNumber = int.TryParse(s[8], out var statNum) ? statNum : 0,
              FromPosition = s[16],
              TargetPosition = s[17],
            };

            ret.Add(e);
          }
        }
      }

      return ret;
    }

    public void DeleteLog(string lastForeignID)
    {
      var files = new List<string>(Directory.GetFiles(_path, "*.csv"));
      files.Sort();

      foreach (var f in files)
      {
        var filename = Path.GetFileName(f);
        if (filename.CompareTo(lastForeignID) > 0)
          break;

        try
        {
          File.Delete(f);
        }
        catch (Exception ex)
        {
          Log.Warning(ex, "Error deleting file: " + f);
        }
      }
    }
  }

}

