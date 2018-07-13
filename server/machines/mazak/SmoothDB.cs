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
    // for now, some stuff is proxied to the open database kit databases
    private OpenDatabaseKitReadDB _openReadDB;
    private string _connStr;

    public SmoothReadOnlyDB(string connectionStr, OpenDatabaseKitReadDB readDb)
    {
      _connStr = connectionStr + ";Database=PMC_Basic";
      _openReadDB = readDb;
    }

    public TResult WithReadDBConnection<TResult>(Func<IDbConnection, TResult> action)
    {
      //this is only used for VersionE log loading
      throw new NotImplementedException();
    }

    public IMazakData LoadMazakData()
    {
      return new SmoothMazakData(_connStr, _openReadDB.LoadMazakData());
    }

    public (IMazakData, ReadOnlyDataSet) LoadDataAndReadSet()
    {
      var (baseMazakData, readSet) = _openReadDB.LoadDataAndReadSet();
      return (new SmoothMazakData(_connStr, baseMazakData), readSet);
    }

    private class SmoothMazakData : IMazakData
    {
      private string _connStr;
      private IMazakData _baseData; //for now, some stuff is proxied to open database kit
      public SmoothMazakData(string connStr, IMazakData d)
      {
        _connStr = connStr;
        _baseData = d;
      }

      #region LoadActions
      public IEnumerable<LoadAction> CurrentLoadActions()
      {
        using (var conn = new SqlConnection(_connStr))
        {
          return LoadActions(conn).Concat(RemoveActions(conn));
        }
      }

      private class FixWork
      {
        public int a9_prcnum {get;set;}
        public string a9_ptnam {get;set;}
        public int a9_fixqty {get;set;}
        public string a6_pos {get;set;}
        public string a1_schcom {get;set;}
      }

      private IEnumerable<LoadAction> LoadActions(SqlConnection conn)
      {
          var qry =
            "SELECT a9_prcnum, a9_ptnam, a9_fixqty, a6_pos, a1_schcom " +
              "FROM A9_FixWork " +
              "LEFT OUTER JOIN M4_PalletData ON M4_PalletData.PalletDataID = a9_PalletDataID_ra " +
              "LEFT OUTER JOIN A6_PositionData ON a6_pltnum = m4_pltnum " +
              "LEFT OUTER JOIN A1_Schedule ON A1_Schedule.ScheduleID = a9_ScheduleID";
          var ret = new List<LoadAction>();
          var elems = conn.Query(qry);
          foreach (var e in conn.Query<FixWork>(qry))
          {
              int stat;
              if (e.a6_pos.StartsWith("LS"))
              {
                if (!int.TryParse(e.a6_pos.Substring(2,2), out stat))
                  continue;
              } else {
                continue;
              }

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
          return ret;
      }

      private class RemoveWork
      {
        public int a8_prcnum {get;set;}
        public string a8_ptnam {get;set;}
        public int a8_fixqty {get;set;}
        public string a6_pos {get;set;}
        public string a1_schcom {get;set;}
      }

      private IEnumerable<LoadAction> RemoveActions(SqlConnection conn)
      {
          var qry =
            "SELECT a8_prcnum,a8_ptnam,a8_fixqty,a6_pos,a1_schcom " +
              "FROM A8_RemoveWork " +
              "LEFT OUTER JOIN A6_PositionData ON a6_pltnum = a8_1 " +
              "LEFT OUTER JOIN A1_Schedule ON A1_Schedule.ScheduleID = a8_ScheduleID";
          var ret = new List<LoadAction>();
          var elems = conn.Query(qry);
          foreach (var e in conn.Query<RemoveWork>(qry))
          {
              int stat;
              if (e.a6_pos.StartsWith("LS"))
              {
                if (!int.TryParse(e.a6_pos.Substring(2,2), out stat))
                  continue;
              } else {
                continue;
              }

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
          return ret;
      }
      #endregion

      #region Schedules and Parts
      public IEnumerable<MazakScheduleRow> LoadSchedules()
      {
        return _baseData.LoadSchedules();
      }

      public void FindPart(int pallet, string mazakPartName, int proc, out string unique, out int path, out int numProc)
      {
        _baseData.FindPart(pallet, mazakPartName, proc, out unique, out path, out numProc);
      }

      public int PartFixQuantity(string mazakPartName, int proc)
      {
        return _baseData.PartFixQuantity(mazakPartName, proc);
      }
      #endregion
    }

  }

}