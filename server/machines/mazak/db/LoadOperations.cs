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
    private readonly string _connStr;
    private bool _loggedDatabaseSettings;

    public LoadOperationsFromDB(MazakConfig cfg)
    {
      _connStr = cfg.SQLConnectionString + ";Database=PMC_Basic";
    }

    public IEnumerable<LoadAction> CurrentLoadActions()
    {
      using (var conn = new SqlConnection(_connStr))
      {
        conn.Open();
        // Prefer failing our observation over aborting a controller update if a deadlock occurs.
        using (var priority = conn.CreateCommand())
        {
          priority.CommandText =
            "SET DEADLOCK_PRIORITY LOW; SET TRANSACTION ISOLATION LEVEL READ COMMITTED";
          priority.ExecuteNonQuery();
        }
        LogDatabaseSettings(conn);
        // One statement, ordinary READ COMMITTED: no range locks held across several reads.
        // This is not a point-in-time snapshot on a lock-based READ COMMITTED database.
        // The controller must still publish a serviceable request/station/assignment together.
        return BuildActions(conn.Query<ActionRow>(RequestQuery, transaction: null).ToList());
      }
    }

    internal class StationRow
    {
      public int OperationID { get; set; }
      public int Station { get; set; }
      public int StationOperationCount { get; set; }
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

    internal sealed class ActionRow : StationRow
    {
      public int ActionID { get; set; }
      public bool LoadEvent { get; set; }
      public string ActionPart { get; set; }
      public string ActionComment { get; set; }
      public int ActionProcess { get; set; }
      public int ActionQuantity { get; set; }
    }

    internal const string RequestQuery =
      @"
      WITH Actions AS (
        SELECT ID AS ActionID, CAST(1 AS bit) AS LoadEvent, OperationID,
          a9_ptnam AS ActionPart, a9_prcnum AS ActionProcess,
          a9_fixqty AS ActionQuantity, a9_ScheduleID AS ScheduleID
        FROM A9_FixWork
        UNION ALL
        SELECT ID, CAST(0 AS bit), OperationID, a8_ptnam, a8_prcnum,
          a8_fixqty, a8_ScheduleID
        FROM A8_RemoveWork
      )
      SELECT a.ActionID, a.LoadEvent, a.OperationID, a.ActionPart,
        a.ActionProcess, a.ActionQuantity, aj.a1_schcom AS ActionComment,
        COALESCE(o.a7_ldsnum, a.OperationID) AS Station,
        (SELECT COUNT(*) FROM A7_IndicateOperation o2 WHERE o2.a7_ldsnum=o.a7_ldsnum) AS StationOperationCount,
        p.a6_pltnum AS Pallet, s.a3_hold AS Held,
        (SELECT COUNT(*) FROM A6_PositionData p2 WHERE p2.a6_pltnum=p.a6_pltnum) AS PositionCount,
        (SELECT COUNT(*) FROM A3_PalletStatus s2 WHERE s2.a3_pltnum=p.a6_pltnum) AS PalletCount,
        w.ID AS AssignmentID, w.a4_ptnam AS Part, j.a1_schcom AS Comment,
        w.a4_prcnum AS Process, w.a4_fixqty AS Quantity
      FROM Actions a
      LEFT JOIN A1_Schedule aj ON aj.ScheduleID=a.ScheduleID
      LEFT JOIN A7_IndicateOperation o ON o.OperationID=a.OperationID
      LEFT JOIN A6_PositionData p ON p.a6_pos='LS'+RIGHT('00'+CAST(o.a7_ldsnum AS varchar(2)),2)+'1'
      LEFT JOIN A3_PalletStatus s ON s.a3_pltnum=p.a6_pltnum
      LEFT JOIN A4_WorkInformation w ON w.PalletID=s.PalletID
      LEFT JOIN A1_Schedule j ON j.ScheduleID=w.a4_ScheduleID
      ORDER BY a.LoadEvent DESC, a.ActionID, w.ID";

    internal static List<LoadAction> BuildActions(IEnumerable<ActionRow> rows)
    {
      var result = new List<LoadAction>();
      // A combined request repeats its station assignments once for each action. Validate each
      // action's join independently; do not mistake this legitimate fan-out for duplicate data.
      foreach (var action in rows.GroupBy(r => new { r.LoadEvent, r.ActionID }))
      {
        var first = action.First();
        if (string.IsNullOrEmpty(first.ActionPart))
          continue;
        var context = BuildStationContexts(action.Cast<StationRow>())[first.OperationID];
        var colon = first.ActionPart.IndexOf(':');
        result.Add(
          new LoadAction
          {
            LoadEvent = first.LoadEvent,
            LoadStation = context.Station,
            StationPallet = context.Pallet,
            Part = colon < 0 ? first.ActionPart : first.ActionPart.Substring(0, colon),
            Comment = first.ActionComment,
            Process = first.ActionProcess,
            Qty = first.ActionQuantity,
          }
        );
      }
      return result;
    }

    private void LogDatabaseSettings(SqlConnection connection)
    {
      if (_loggedDatabaseSettings)
        return;
      _loggedDatabaseSettings = true;
      try
      {
        using var command = connection.CreateCommand();
        command.CommandText =
          @"SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)),
          snapshot_isolation_state_desc, is_read_committed_snapshot_on
          FROM sys.databases WHERE name=DB_NAME()";
        using var reader = command.ExecuteReader();
        if (reader.Read())
          Serilog.Log.Information(
            $"PMC database: SQL Server {reader.GetValue(0)}, snapshot isolation {reader.GetValue(1)}, read committed snapshot {reader.GetValue(2)}"
          );
      }
      catch (SqlException ex)
      {
        // Optional diagnostics must not prevent the operational read if metadata is restricted.
        Serilog.Log.Debug(ex, "Unable to read PMC database isolation settings");
      }
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
              && first.Station > 0
              && first.StationOperationCount == 1
              && g.All(r =>
                r.Pallet == first.Pallet
                && r.Station == first.Station
                && r.Held == first.Held
                && r.PositionCount == 1
                && r.PalletCount == 1
                && r.StationOperationCount == 1
              )
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
  }
}
