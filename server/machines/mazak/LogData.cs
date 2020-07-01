/* Copyright (c) 2019, John Lenz

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

  public class LogEntry
  {
    public DateTime TimeUTC { get; set; }
    public LogCode Code { get; set; }
    public string ForeignID { get; set; }

    //Only sometimes filled in depending on the log code
    public int Pallet { get; set; }
    public string FullPartName { get; set; } //Full part name in the mazak system
    public string JobPartName { get; set; }  //Part name with : stripped off
    public int Process { get; set; }
    public int FixedQuantity { get; set; }
    public string Program { get; set; }
    public int StationNumber { get; set; }

    //Only filled in for pallet movement
    public string TargetPosition { get; set; }
    public string FromPosition { get; set; }
  }

  public delegate void MazakLogEventDel(LogEntry e);
  public interface INotifyMazakLogEvent
  {
    event MazakLogEventDel MazakLogEvent;
  }
  public delegate void NewEntriesDel();
  public interface IMazakLogReader : INotifyMazakLogEvent
  {
    void RecheckQueues(bool wait);
    void Halt();
    event NewEntriesDel NewEntries;
  }

#if USE_OLEDB
	public class LogDataVerE : IMazakLogReader, INotifyMazakLogEvent
	{
    private const string DateTimeFormat = "yyyyMMddHHmmss";

    private BlackMaple.MachineFramework.JobDB _jobDB;
    private BlackMaple.MachineFramework.JobLogDB _log;
    private MazakQueues _queues;
    private IReadDataAccess _readDB;
    private IMachineGroupName _machGroupName;
    private BlackMaple.MachineFramework.ISendMaterialToExternalQueue _sendToExternal;
    private BlackMaple.MachineFramework.FMSSettings FMSSettings { get; set; }
    private static Serilog.ILogger Log = Serilog.Log.ForContext<LogDataVerE>();

    private object _lock;
    private System.Timers.Timer _timer;

    public event MazakLogEventDel MazakLogEvent;
    public event NewEntriesDel NewEntries;

    public LogDataVerE(BlackMaple.MachineFramework.JobLogDB log,
                       BlackMaple.MachineFramework.JobDB jobDB,
                       BlackMaple.MachineFramework.ISendMaterialToExternalQueue send,
                       IMachineGroupName machGroupName,
                       IReadDataAccess readDB,
                       MazakQueues queues,
                       BlackMaple.MachineFramework.FMSSettings settings)
    {
      _log = log;
      _jobDB = jobDB;
      _readDB = readDB;
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
          var mazakData = _readDB.LoadSchedulesAndLoadActions();
          var logs = LoadLog(_log.MaxForeignID());
          var trans = new LogTranslation(_jobDB, _log, mazakData, _machGroupName, FMSSettings,
            le => MazakLogEvent?.Invoke(le)
          );
          var sendToExternal = new List<BlackMaple.MachineFramework.MaterialToSendToExternalQueue>();
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

          _queues.CheckQueues(mazakData);

          if (sendToExternal.Count > 0) {
            _sendToExternal.Post(sendToExternal);
          }

          if (logs.Count > 0) {
            NewEntries?.Invoke();
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

    private BlackMaple.MachineFramework.JobDB _jobDB;
    private BlackMaple.MachineFramework.JobLogDB _log;
    private IReadDataAccess _readDB;
    private IMachineGroupName _machGroupName;
    private BlackMaple.MachineFramework.FMSSettings _settings;
    private BlackMaple.MachineFramework.ISendMaterialToExternalQueue _sendToExternal;
    private MazakQueues _queues;

    private string _path;
    private AutoResetEvent _shutdown;
    private AutoResetEvent _newLogFile;
    private AutoResetEvent _recheckQueues;
    private ManualResetEvent _recheckQueuesComplete;

    private Thread _thread;
    private FileSystemWatcher _watcher;

    public LogDataWeb(string path,
                      BlackMaple.MachineFramework.JobLogDB log,
                      BlackMaple.MachineFramework.JobDB jobDB,
                      IMachineGroupName machineGroupName,
                      BlackMaple.MachineFramework.ISendMaterialToExternalQueue sendToExternal,
                      IReadDataAccess readDB,
                      MazakQueues queues,
                      BlackMaple.MachineFramework.FMSSettings settings)
    {
      _path = path;
      _log = log;
      _jobDB = jobDB;
      _readDB = readDB;
      _queues = queues;
      _settings = settings;
      _machGroupName = machineGroupName;
      _sendToExternal = sendToExternal;
      _shutdown = new AutoResetEvent(false);
      _newLogFile = new AutoResetEvent(false);
      _recheckQueues = new AutoResetEvent(false);
      _recheckQueuesComplete = new ManualResetEvent(false);
      if (System.IO.Directory.Exists(path))
      {
        _thread = new Thread(new ThreadStart(ThreadFunc));
        _thread.Start();
        _watcher = new FileSystemWatcher(_path);
        _watcher.Filter = "*.csv";
        _watcher.Created += (sender, evt) =>
          _newLogFile.Set();
        //_watcher.Changed += (sender, evt) => _newLogFile.Set();
        _watcher.EnableRaisingEvents = true;
        Log.Debug("Watching {path} for new CSV files", _path);
      }
    }

    public event MazakLogEventDel MazakLogEvent;
    public event NewEntriesDel NewEntries;

    public void ThreadFunc()
    {
      for (; ; )
      {
        try
        {

          var sleepTime = TimeSpan.FromMinutes(1);
          Log.Debug("Sleeping for {mins} minutes", sleepTime.TotalMinutes);
          var ret = WaitHandle.WaitAny(new WaitHandle[] { _shutdown, _newLogFile, _recheckQueues }, sleepTime, false);
          if (ret == 0)
          {
            Log.Debug("Thread shutdown");
            return;
          }
          bool recheckingQueues = ret == 2;

          Thread.Sleep(TimeSpan.FromSeconds(1));

          var mazakData = _readDB.LoadSchedulesAndLoadActions();
          var logs = LoadLog(_log.MaxForeignID());
          var trans = new LogTranslation(_jobDB, _log, mazakData, _machGroupName, _settings,
            le => MazakLogEvent?.Invoke(le)
          );
          var sendToExternal = new List<BlackMaple.MachineFramework.MaterialToSendToExternalQueue>();
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

          DeleteLog(_log.MaxForeignID());

          var queuesChanged = _queues.CheckQueues(mazakData);

          if (sendToExternal.Count > 0)
          {
            _sendToExternal.Post(sendToExternal).Wait(TimeSpan.FromSeconds(30));
          }

          if (logs.Count > 0 || queuesChanged)
          {
            NewEntries?.Invoke();
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

      if (_thread != null && !_thread.Join(TimeSpan.FromSeconds(15)))
        _thread.Abort();
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

            var e = new LogEntry();

            e.ForeignID = filename;
            e.TimeUTC = new DateTime(int.Parse(s[0]), int.Parse(s[1]), int.Parse(s[2]),
                                     int.Parse(s[3]), int.Parse(s[4]), int.Parse(s[5]),
                                     DateTimeKind.Local);
            e.TimeUTC = e.TimeUTC.ToUniversalTime();
            e.Code = (LogCode)code;

            e.Pallet = -1;
            if (int.TryParse(s[13], out var pal))
              e.Pallet = pal;
            e.FullPartName = s[10].Trim();
            int idx = e.FullPartName.IndexOf(':');
            if (idx > 0)
              e.JobPartName = e.FullPartName.Substring(0, idx);
            else
              e.JobPartName = e.FullPartName;
            int.TryParse(s[11], out var proc);
            e.Process = proc;
            int.TryParse(s[12], out var fixQty);
            e.FixedQuantity = fixQty;
            e.Program = s[14];
            int.TryParse(s[8], out var statNum);
            e.StationNumber = statNum;
            e.FromPosition = s[16];
            e.TargetPosition = s[17];

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

