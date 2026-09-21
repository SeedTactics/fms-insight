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
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
#if NET35
using System.Data.SqlClient;
#else
using Microsoft.Data.SqlClient;
using Dapper;
#endif

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("BlackMaple.FMSInsight.Tests")]

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
                  Comment = comment,
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
        conn.Open();
        // Prefer retrying our observation over aborting a controller update on lock contention.
        using (var priority = conn.CreateCommand())
        {
          priority.CommandText = "SET DEADLOCK_PRIORITY LOW";
          priority.ExecuteNonQuery();
        }
        using var trans = conn.BeginTransaction(System.Data.IsolationLevel.Serializable);
        var stations = ReadStations(conn, trans);
        // Materialize all results before releasing read locks. Reader failure propagates normally.
        var result = LoadActions(conn, trans, stations)
          .Concat(RemoveActions(conn, trans, stations))
          .ToList();
        trans.Commit();
        return result;
      }
    }

    internal sealed class StationRow
    {
      public int OperationID { get; set; }
      public int Station { get; set; }
      public int? Pallet { get; set; }
      public int? Held { get; set; }
      public int PositionCount { get; set; }
      public int PalletCount { get; set; }
      public int? AssignmentID { get; set; }
      public string Part { get; set; }
      public string Comment { get; set; }
      public int Process { get; set; }
      public int Quantity { get; set; }
    }

    internal sealed class StationContext
    {
      public int Station { get; init; }
      public MazakStationPallet Pallet { get; init; }
    }

    private static Dictionary<int, StationContext> ReadStations(
      SqlConnection connection,
      SqlTransaction transaction
    )
    {
      var rows = connection
        .Query<StationRow>(
          @"
        SELECT o.OperationID, o.a7_ldsnum AS Station, p.a6_pltnum AS Pallet,
          s.a3_hold AS Held,
          (SELECT COUNT(*) FROM A6_PositionData p2 WHERE p2.a6_pltnum=p.a6_pltnum) AS PositionCount,
          (SELECT COUNT(*) FROM A3_PalletStatus s2 WHERE s2.a3_pltnum=p.a6_pltnum) AS PalletCount,
          w.ID AS AssignmentID, w.a4_ptnam AS Part, j.a1_schcom AS Comment,
          w.a4_prcnum AS Process, w.a4_fixqty AS Quantity
        FROM A7_IndicateOperation o
        LEFT JOIN A6_PositionData p ON p.a6_pos='LS'+RIGHT('00'+CAST(o.a7_ldsnum AS varchar(2)),2)+'1'
        LEFT JOIN A3_PalletStatus s ON s.a3_pltnum=p.a6_pltnum
        LEFT JOIN A4_WorkInformation w ON w.PalletID=s.PalletID
        LEFT JOIN A1_Schedule j ON j.ScheduleID=w.a4_ScheduleID
        ",
          transaction: transaction
        )
        .ToList();
      return BuildStationContexts(rows);
    }

    internal static Dictionary<int, StationContext> BuildStationContexts(
      IEnumerable<StationRow> source
    )
    {
      var rows = source.ToList();
      return rows.GroupBy(r => r.OperationID)
        .ToDictionary(
          g => g.Key,
          g =>
          {
            var first = g.First();
            var coherent =
              first.Pallet > 0
              && first.Held.HasValue
              && first.PositionCount == 1
              && first.PalletCount == 1
              && g.All(r => r.Pallet == first.Pallet && r.Station == first.Station)
              && g.Select(r => r.AssignmentID).Distinct().Count() == g.Count()
              && rows.Where(r => r.Station == first.Station)
                .All(r => r.OperationID == first.OperationID);
            return new StationContext
            {
              Station = first.Station,
              Pallet = coherent
                ? new MazakStationPallet
                {
                  PalletNumber = first.Pallet.Value,
                  OnHold = first.Held.Value != 0,
                  Material = g.Where(r => r.AssignmentID.HasValue)
                    .Select(r => new MazakStationMaterial
                    {
                      PartName = r.Part,
                      Comment = r.Comment,
                      Process = r.Process,
                      Quantity = r.Quantity,
                    })
                    .ToList(),
                }
                : null,
            };
          }
        );
    }

    private class FixWork
    {
      public int OperationID { get; set; }
      public int a9_prcnum { get; set; }
      public string a9_ptnam { get; set; }
      public int a9_fixqty { get; set; }
      public string a1_schcom { get; set; }
    }

    private IEnumerable<LoadAction> LoadActions(
      SqlConnection conn,
      SqlTransaction trans,
      Dictionary<int, StationContext> stations
    )
    {
      var qry =
        "SELECT OperationID, a9_prcnum, a9_ptnam, a9_fixqty, a1_schcom "
        + " FROM A9_FixWork "
        + " LEFT OUTER JOIN A1_Schedule ON A1_Schedule.ScheduleID = a9_ScheduleID";
      var ret = new List<LoadAction>();
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
            LoadStation = stations.TryGetValue(stat, out var station) ? station.Station : stat,
            StationPallet = station?.Pallet,
            Part = part,
            Comment = comment,
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

    private IEnumerable<LoadAction> RemoveActions(
      SqlConnection conn,
      SqlTransaction trans,
      Dictionary<int, StationContext> stations
    )
    {
      var qry =
        "SELECT OperationID,a8_prcnum,a8_ptnam,a8_fixqty,a1_schcom "
        + " FROM A8_RemoveWork "
        + " LEFT OUTER JOIN A1_Schedule ON A1_Schedule.ScheduleID = a8_ScheduleID";
      var ret = new List<LoadAction>();
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
            LoadStation = stations.TryGetValue(stat, out var station) ? station.Station : stat,
            StationPallet = station?.Pallet,
            Part = part,
            Comment = comment,
            Process = proc,
            Qty = qty,
          }
        );
      }
      return ret;
    }
  }
}
