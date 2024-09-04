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
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
#if !NET35
using Dapper;
#endif

namespace MazakMachineInterface
{
  public class LoadOperationsFromFile : ICurrentLoadActions
  {
    private readonly string mazakPath;

    public LoadOperationsFromFile(MazakConfig cfg)
    {
      mazakPath = cfg.LoadCSVPath;
    }

    public IEnumerable<LoadAction> CurrentLoadActions()
    {
      if (!Directory.Exists(mazakPath))
        return new List<LoadAction>();

      var ret = new List<LoadAction>();

      foreach (var f in Directory.GetFiles(mazakPath, "*.csv"))
      {
        Match m = Regex.Match(Path.GetFileName(f).ToLower(), "lds([0-9]*)_operation.*csv");
        if (!m.Success || m.Groups.Count < 2)
          continue;
        int lds = int.Parse(m.Groups[1].Value);

        if (File.Exists(f))
          ret.AddRange(ReadFile(lds, f));
      }

      return ret;
    }

    private List<LoadAction> ReadFile(int stat, string fName)
    {
      var ret = new List<LoadAction>();

      using (StreamReader f = File.OpenText(fName))
      {
        while (f.Peek() >= 0)
        {
          string[] split = f.ReadLine().Split(',');

          if (split.Length > 8 && !string.IsNullOrEmpty(split[1]))
          {
            string part = split[1];
            string comment = split[2];
            int idx = part.IndexOf(':');
            if (idx >= 0)
            {
              part = part.Substring(0, idx);
            }
            int proc = int.Parse(split[3]);
            int qty = int.Parse(split[5]);

            if (!string.IsNullOrEmpty(part))
            {
              bool load = false;
              if (split[0].StartsWith("FIX"))
                load = true;
              if (split[0].StartsWith("REM"))
                load = false;

              ret.Add(
                new LoadAction()
                {
                  LoadEvent = load,
                  LoadStation = stat,
                  Part = part,
                  Process = proc,
                  Unique = string.IsNullOrEmpty(comment) ? "" : MazakPart.ParseComment(comment),
                  Path = 1,
                  Qty = qty,
                }
              );
            }
          }
        }
      }

      return ret;
    }
  }

  public class LoadOperationsFromDB : ICurrentLoadActions
  {
    private string _connStr;

    public LoadOperationsFromDB(MazakConfig cfg)
    {
      _connStr = cfg.SQLConnectionString + ";Database=PMC_Basic";
    }

    public IEnumerable<LoadAction> CurrentLoadActions()
    {
      using (var conn = new SqlConnection(_connStr))
      {
        using var trans = conn.BeginTransaction();
        return LoadActions(conn, trans).Concat(RemoveActions(conn, trans));
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

    private IEnumerable<LoadAction> LoadActions(SqlConnection conn, SqlTransaction trans)
    {
      var qry =
        "SELECT OperationID, a9_prcnum, a9_ptnam, a9_fixqty, a1_schcom "
        + " FROM A9_FixWork "
        + " LEFT OUTER JOIN A1_Schedule ON A1_Schedule.ScheduleID = a9_ScheduleID";
      var ret = new List<LoadAction>();
      var elems = conn.Query(qry, transaction: trans);
      foreach (var e in conn.Query<FixWork>(qry, transaction: trans))
      {
        if (string.IsNullOrEmpty(e.a9_ptnam))
        {
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

        ret.Add(
          new LoadAction()
          {
            LoadEvent = true,
            LoadStation = stat,
            Part = part,
            Unique = string.IsNullOrEmpty(comment) ? "" : MazakPart.ParseComment(comment),
            Path = 1,
            Process = proc,
            Qty = qty,
          }
        );
      }
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

    private IEnumerable<LoadAction> RemoveActions(SqlConnection conn, SqlTransaction trans)
    {
      var qry =
        "SELECT OperationID,a8_prcnum,a8_ptnam,a8_fixqty,a1_schcom "
        + " FROM A8_RemoveWork "
        + " LEFT OUTER JOIN A1_Schedule ON A1_Schedule.ScheduleID = a8_ScheduleID";
      var ret = new List<LoadAction>();
      var elems = conn.Query(qry, transaction: trans);
      foreach (var e in conn.Query<RemoveWork>(qry, transaction: trans))
      {
        if (string.IsNullOrEmpty(e.a8_ptnam))
        {
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

        ret.Add(
          new LoadAction()
          {
            LoadEvent = false,
            LoadStation = stat,
            Part = part,
            Unique = string.IsNullOrEmpty(comment) ? "" : MazakPart.ParseComment(comment),
            Process = proc,
            Qty = qty,
            Path = 1,
          }
        );
      }
      return ret;
    }
  }
}
