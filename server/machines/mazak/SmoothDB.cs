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

using System.Collections.Generic;
using System.Linq;
using System.Data.SqlClient;
using Dapper;
using System;
using System.Data;

namespace MazakMachineInterface
{
  public class SmoothReadOnlyDB : IReadDataAccess
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<SmoothReadOnlyDB>();

    // for now, some stuff is proxied to the open database kit databases
    private OpenDatabaseKitReadDB _openReadDB;
    private string _connStr;
    private string _readConnStr;

    public MazakDbType MazakType => MazakDbType.MazakSmooth;

    public SmoothReadOnlyDB(string connectionStr, OpenDatabaseKitReadDB readDb)
    {
      _connStr = connectionStr + ";Database=PMC_Basic";
      _readConnStr = connectionStr + ";Database=FCREADDAT01";
      _openReadDB = readDb;
    }

    public TResult WithReadDBConnection<TResult>(Func<IDbConnection, TResult> action)
    {
      using (var conn = new SqlConnection(_readConnStr))
      {
        conn.Open();
        return action(conn);
      }
    }

    public MazakSchedules LoadSchedules()
    {
      return _openReadDB.LoadSchedules();
    }

    public MazakSchedulesAndLoadActions LoadSchedulesAndLoadActions()
    {
      return WithReadDBConnection(conn =>
      {
        var trans = conn.BeginTransaction();
        try
        {
          var ret = new MazakSchedulesAndLoadActions()
          {
            Schedules = _openReadDB.LoadSchedules(conn, trans).Schedules,
            LoadActions = CurrentLoadActions(),
            Tools = LoadTools(conn, trans)
          };
          trans.Commit();
          return ret;
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      });
    }

    public MazakSchedulesPartsPallets LoadSchedulesPartsPallets()
    {
      var sch = _openReadDB.LoadSchedulesPartsPallets(includeLoadActions: false);
      sch.LoadActions = CurrentLoadActions();
      return sch;
    }

    public MazakAllData LoadAllData()
    {
      var all = _openReadDB.LoadAllData(includeLoadActions: false);
      all.LoadActions = CurrentLoadActions();
      return all;
    }

    public IEnumerable<MazakProgramRow> LoadPrograms()
    {
      return _openReadDB.LoadPrograms();
    }

    public bool CheckPartExists(string partName)
    {
      return _openReadDB.CheckPartExists(partName);
    }

    public bool CheckProgramExists(string mainProgram)
    {
      return _openReadDB.CheckProgramExists(mainProgram);
    }

    #region Tools

    public IEnumerable<ToolPocketRow> LoadTools(IDbConnection conn, IDbTransaction trans)
    {
      var q = @"SELECT
          MachineNumber,
          PocketNumber,
          PocketType,
          ToolID,
          ToolNameCode,
          ToolNumber,
          IsLengthMeasured,
          IsToolDataValid,
          IsToolLifeOver,
          IsToolLifeBroken,
          IsDataAccessed,
          IsBeingUsed,
          UsableNow,
          WillBrokenInform,
          InterferenceType,
          LifeSpan,
          LifeUsed,
          NominalDiameter,
          SuffixCode,
          ToolDiameter,
          GroupNo
        FROM ToolPocket
      ";
      return conn.Query<ToolPocketRow>(q, transaction: trans);
    }

    public IEnumerable<ToolPocketRow> LoadTools()
    {
      return WithReadDBConnection(conn =>
      {
        using (var trans = conn.BeginTransaction())
        {
          return LoadTools(conn, trans).ToList();
        }
      });
    }

    #endregion

    #region LoadActions
    private IEnumerable<LoadAction> CurrentLoadActions()
    {
      using (var conn = new SqlConnection(_connStr))
      {
        return LoadActions(conn).Concat(RemoveActions(conn));
      }
    }

    private class FixWork
    {
      public int OperationID { get; set; }
      public int a9_prcnum { get; set; }
      public string a9_ptnam { get; set; }
      public int a9_fixqty { get; set; }
      public string a1_schcom { get; set; }
    }

    private IEnumerable<LoadAction> LoadActions(SqlConnection conn)
    {
      var qry = "SELECT OperationID, a9_prcnum, a9_ptnam, a9_fixqty, a1_schcom " +
                   " FROM A9_FixWork " +
                   " LEFT OUTER JOIN A1_Schedule ON A1_Schedule.ScheduleID = a9_ScheduleID";
      var ret = new List<LoadAction>();
      var elems = conn.Query(qry);
      foreach (var e in conn.Query<FixWork>(qry))
      {
        Log.Debug("Received load action {@action}", e);
        if (string.IsNullOrEmpty(e.a9_ptnam))
        {
          Log.Warning("Load operation has no part name {@load}", e);
          continue;
        }

        int stat = e.OperationID;
        string part = e.a9_ptnam;
        string comment = e.a1_schcom;
        int idx = part.IndexOf(':');
        if (idx >= 0)
        {
          part = part.Substring(0, idx);
        }
        int proc = e.a9_prcnum;
        int qty = e.a9_fixqty;

        ret.Add(new LoadAction(true, stat, part, comment, proc, qty));
      }
      Log.Debug("Parsed load {@actions}", ret);
      return ret;
    }

    private class RemoveWork
    {
      public int OperationID { get; set; }
      public int a8_prcnum { get; set; }
      public string a8_ptnam { get; set; }
      public int a8_fixqty { get; set; }
      public string a1_schcom { get; set; }
    }

    private IEnumerable<LoadAction> RemoveActions(SqlConnection conn)
    {
      var qry = "SELECT OperationID,a8_prcnum,a8_ptnam,a8_fixqty,a1_schcom " +
          " FROM A8_RemoveWork " +
          " LEFT OUTER JOIN A1_Schedule ON A1_Schedule.ScheduleID = a8_ScheduleID";
      var ret = new List<LoadAction>();
      var elems = conn.Query(qry);
      foreach (var e in conn.Query<RemoveWork>(qry))
      {
        Log.Debug("Received remove work {@action}", e);
        if (string.IsNullOrEmpty(e.a8_ptnam))
        {
          Log.Warning("Load operation has no part name {@load}", e);
          continue;
        }

        int stat = e.OperationID;
        string part = e.a8_ptnam;
        string comment = e.a1_schcom;
        int idx = part.IndexOf(':');
        if (idx >= 0)
        {
          part = part.Substring(0, idx);
        }
        int proc = e.a8_prcnum;
        int qty = e.a8_fixqty;

        ret.Add(new LoadAction(false, stat, part, comment, proc, qty));
      }
      Log.Debug("Parsed unload actions to {@actions}", ret);
      return ret;
    }
    #endregion
  }
}