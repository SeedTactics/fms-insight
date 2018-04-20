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

    // Some old events we don't use anymore

    //Pallet transfer refers to the rotation of pallets between the machining table
    //and the input/output cart pickup location.  There are three kinds of transfers: both pallets,
    //only a pallet moving to the machine, and only a pallet moving out of the machine.
    //PalletTransfer = 221,
    //PalletTransferNoPalletToMachine = 433,


  }

  public class LogEntry
  {
    public DateTime TimeUTC;
    public LogCode Code;
    public string ForeignID;

    //Only sometimes filled in depending on the log code
    public int Pallet;
    public string FullPartName; //Full part name in the mazak system
    public string JobPartName;  //Part name with : stripped off
    public int Process;
    public int FixedQuantity;
    public string Program;
    public int StationNumber;

    //Only filled in for pallet movement
    public string TargetPosition;
    public string FromPosition;
  }

  public interface ILogData
  {
    List<LogEntry> LoadLog(string lastForeignID);
    void DeleteLog(string lastForeignID, System.Diagnostics.TraceSource trace);
  }

#if USE_OLEDB
	public class LogDataVerE : ILogData
	{
		private IReadDataAccess _db;
        private const string DateTimeFormat = "yyyyMMddHHmmss";

		public LogDataVerE(IReadDataAccess db)
		{
			_db = db;
		}

		public List<LogEntry> LoadLog (string lastForeignID)
		{
			return _db.WithReadDBConnection(conn =>
			{
                var trans = conn.BeginTransaction();
                try {

                    System.Data.OleDb.OleDbCommand cmd = (System.Data.OleDb.OleDbCommand)conn.CreateCommand();
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
                        param.DbType = System.Data.DbType.VarNumeric;
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
                } catch {
                    trans.Rollback();
                    throw;
                }
            });
		}

		private void CheckIDRollover(System.Data.IDbTransaction trans, System.Data.IDbConnection conn,
			ref long epoch, ref long lastID, DateTime lastDate)
		{
			var cmd = conn.CreateCommand();
			cmd.Transaction = trans;
			cmd.CommandText = "SELECT Date FROM Log WHERE ID = ?";
            var param = cmd.CreateParameter();
            param.DbType = System.Data.DbType.VarNumeric;
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

		public void DeleteLog(string lastForeignID, System.Diagnostics.TraceSource trace)
		{
			//do nothing
		}
	}
#endif

  public class LogDataWeb : ILogData
  {
    private string _path;

    public LogDataWeb(string path)
    {
      _path = path;
    }

    public List<LogEntry> LoadLog(string lastForeignID)
    {
      var files = new List<string>(System.IO.Directory.GetFiles(_path, "*.csv"));
      files.Sort();

      var ret = new List<LogEntry>();

      foreach (var f in files)
      {
        var filename = System.IO.Path.GetFileName(f);
        if (filename.CompareTo(lastForeignID) <= 0)
          continue;

        using (var stream = new System.IO.StreamReader(f))
        {
          while (stream.Peek() >= 0)
          {

            var s = stream.ReadLine().Split(',');
            if (s.Length < 18)
              continue;

            int code;
            if (!int.TryParse(s[6], out code))
              continue;
            if (!Enum.IsDefined(typeof(LogCode), code)) continue;

            var e = new LogEntry();

            e.ForeignID = filename;
            e.TimeUTC = new DateTime(int.Parse(s[0]), int.Parse(s[1]), int.Parse(s[2]),
                                     int.Parse(s[3]), int.Parse(s[4]), int.Parse(s[5]),
                                     DateTimeKind.Local);
            e.TimeUTC = e.TimeUTC.ToUniversalTime();
            e.Code = (LogCode)code;

            if (!int.TryParse(s[13], out e.Pallet))
              e.Pallet = -1;
            e.FullPartName = s[10].Trim();
            int idx = e.FullPartName.IndexOf(':');
            if (idx > 0)
              e.JobPartName = e.FullPartName.Substring(0, idx);
            else
              e.JobPartName = e.FullPartName;
            int.TryParse(s[11], out e.Process);
            int.TryParse(s[12], out e.FixedQuantity);
            e.Program = s[14];
            int.TryParse(s[8], out e.StationNumber);
            e.FromPosition = s[16];
            e.TargetPosition = s[17];

            ret.Add(e);
          }
        }
      }

      return ret;
    }

    public void DeleteLog(string lastForeignID, System.Diagnostics.TraceSource trace)
    {
      var files = new List<string>(System.IO.Directory.GetFiles(_path, "*.csv"));
      files.Sort();

      foreach (var f in files)
      {
        var filename = System.IO.Path.GetFileName(f);
        if (filename.CompareTo(lastForeignID) > 0)
          break;

        try
        {
          System.IO.File.Delete(f);
        }
        catch (Exception ex)
        {
          trace.TraceEvent(System.Diagnostics.TraceEventType.Warning, 0,
                           "Error deleting file: " + f + Environment.NewLine + ex.ToString());
        }
      }
    }
  }

}

