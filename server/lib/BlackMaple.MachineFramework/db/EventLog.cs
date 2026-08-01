/* Copyright (c) 2024, John Lenz

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

#nullable disable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;

namespace BlackMaple.MachineFramework
{
  internal sealed partial class Repository
  {
    #region Loading
    private IEnumerable<LogEntry> LoadLog(IDataReader reader, IDbTransaction trans)
    {
      using (var matCmd = _connection.CreateCommand())
      using (var detailCmd = _connection.CreateCommand())
      using (var toolCmd = _connection.CreateCommand())
      using (var membershipCmd = _connection.CreateCommand())
      {
        if (trans != null)
        {
          ((IDbCommand)matCmd).Transaction = trans;
          ((IDbCommand)detailCmd).Transaction = trans;
          ((IDbCommand)toolCmd).Transaction = trans;
          ((IDbCommand)membershipCmd).Transaction = trans;
        }
        matCmd.CommandText =
          "SELECT sm.MaterialID, m.UniqueStr, sm.Process, m.PartName, m.NumProcesses, sm.Face, m.Serial, m.Workorder, mp.Path "
          + " FROM stations_mat sm "
          + " LEFT OUTER JOIN matdetails m ON sm.MaterialID = m.MaterialID "
          + " LEFT OUTER JOIN mat_path_details mp ON sm.MaterialID = mp.MaterialID AND sm.Process = mp.Process "
          + " WHERE sm.Counter = $cntr";
        matCmd.Parameters.Add("cntr", SqliteType.Integer);

        detailCmd.CommandText = "SELECT Key, Value FROM program_details WHERE Counter = $cntr";
        detailCmd.Parameters.Add("cntr", SqliteType.Integer);

        toolCmd.CommandText =
          "SELECT Tool, Pocket, UseInCycle, UseAtEndOfCycle, ToolLife, ToolChange, SerialAtStart, SerialAtEnd, CountInCycle, CountAtEndOfCycle, LifeCount FROM station_tool_use WHERE Counter = $cntr";
        toolCmd.Parameters.Add("cntr", SqliteType.Integer);

        membershipCmd.CommandText =
          "SELECT ContainerId FROM basket_cycle_container_ids WHERE CycleCounter = $cntr ORDER BY ContainerId";
        membershipCmd.Parameters.Add("cntr", SqliteType.Integer);

        while (reader.Read())
        {
          long ctr = reader.GetInt64(0);
          int pal;
          if (reader.GetFieldType(1) == typeof(string))
          {
            int.TryParse(reader.GetString(1), out pal);
          }
          else
          {
            pal = reader.GetInt32(1);
          }
          int logType = reader.GetInt32(2);
          int locNum = reader.GetInt32(3);
          string prog = reader.GetString(4);
          bool start = reader.GetBoolean(5);
          System.DateTime timeUTC = new DateTime(reader.GetInt64(6), DateTimeKind.Utc);
          string result = reader.GetString(7);
          TimeSpan elapsed = TimeSpan.FromMinutes(-1);
          if (!reader.IsDBNull(9))
            elapsed = TimeSpan.FromTicks(reader.GetInt64(9));
          TimeSpan active = TimeSpan.Zero;
          if (!reader.IsDBNull(10))
            active = TimeSpan.FromTicks(reader.GetInt64(10));
          string locName = null;
          if (!reader.IsDBNull(11))
            locName = reader.GetString(11);
          Guid? containerId =
            reader.FieldCount <= 12 || reader.IsDBNull(12)
              ? null
              : Guid.Parse(reader.GetString(12));

          LogType ty;
          if (Enum.IsDefined(typeof(LogType), logType))
          {
            ty = (LogType)logType;
            if (locName == null)
            {
              //For compatibility with old logs
              switch (ty)
              {
                case LogType.GeneralMessage:
                  locName = "General";
                  break;
                case LogType.Inspection:
                  locName = "Inspect";
                  break;
                case LogType.LoadUnloadCycle:
                  locName = "Load";
                  break;
                case LogType.MachineCycle:
                  locName = "MC";
                  break;
                case LogType.OrderAssignment:
                  locName = "Order";
                  break;
                case LogType.PartMark:
                  locName = "Mark";
                  break;
                case LogType.PalletCycle:
                  locName = "Pallet Cycle";
                  break;
              }
            }
          }
          else
          {
            ty = LogType.GeneralMessage;
            switch (logType)
            {
              case 3:
                locName = "Machine";
                break;
              case 4:
                locName = "Buffer";
                break;
              case 5:
                locName = "Cart";
                break;
              case 8:
                locName = "Wash";
                break;
              case 9:
                locName = "Deburr";
                break;
              default:
                locName = "Unknown";
                break;
            }
          }

          var matLst = ImmutableList.CreateBuilder<LogMaterial>();
          matCmd.Parameters[0].Value = ctr;
          using (var matReader = matCmd.ExecuteReader())
          {
            while (matReader.Read())
            {
              string uniq = "";
              string part = "";
              int numProc = -1;
              int face = 0;
              string serial = "";
              string workorder = "";
              int? path = null;
              if (!matReader.IsDBNull(1))
                uniq = matReader.GetString(1);
              if (!matReader.IsDBNull(3))
                part = matReader.GetString(3);
              if (!matReader.IsDBNull(4))
                numProc = matReader.GetInt32(4);
              if (!matReader.IsDBNull(5))
              {
                if (reader.GetFieldType(5) == typeof(string))
                {
                  var faceStr = matReader.GetString(5);
                  if (!int.TryParse(faceStr, out face))
                  {
                    face = 0;
                  }
                }
                else
                {
                  face = matReader.GetInt32(5);
                }
              }
              if (!matReader.IsDBNull(6))
                serial = matReader.GetString(6);
              if (!matReader.IsDBNull(7))
                workorder = matReader.GetString(7);
              if (!matReader.IsDBNull(8))
                path = matReader.GetInt32(8);
              matLst.Add(
                new LogMaterial()
                {
                  MaterialID = matReader.GetInt64(0),
                  JobUniqueStr = uniq,
                  Process = matReader.GetInt32(2),
                  PartName = part,
                  NumProcesses = numProc,
                  Serial = serial,
                  Workorder = workorder,
                  Face = face,
                  Path = path,
                }
              );
            }
          }

          detailCmd.Parameters[0].Value = ctr;
          var progDetails = ImmutableDictionary.CreateBuilder<string, string>();
          using (var detailReader = detailCmd.ExecuteReader())
          {
            while (detailReader.Read())
            {
              progDetails[detailReader.GetString(0)] = detailReader.GetString(1);
            }
          }

          toolCmd.Parameters[0].Value = ctr;
          var tools = ImmutableList.CreateBuilder<ToolUse>();
          using (var toolReader = toolCmd.ExecuteReader())
          {
            while (toolReader.Read())
            {
              tools.Add(
                new ToolUse()
                {
                  Tool = toolReader.GetString(0),
                  Pocket = toolReader.GetInt32(1),
                  ToolUseDuringCycle = toolReader.IsDBNull(2)
                    ? null
                    : TimeSpan.FromTicks(toolReader.GetInt64(2)),
                  TotalToolUseAtEndOfCycle = toolReader.IsDBNull(3)
                    ? null
                    : TimeSpan.FromTicks(toolReader.GetInt64(3)),
                  ConfiguredToolLife = toolReader.IsDBNull(4)
                    ? null
                    : TimeSpan.FromTicks(toolReader.GetInt64(4)),
                  ToolChangeOccurred = toolReader.IsDBNull(5) ? null : toolReader.GetBoolean(5),
                  ToolSerialAtStartOfCycle = toolReader.IsDBNull(6)
                    ? null
                    : toolReader.GetString(6),
                  ToolSerialAtEndOfCycle = toolReader.IsDBNull(7) ? null : toolReader.GetString(7),
                  ToolUseCountDuringCycle = toolReader.IsDBNull(8) ? null : toolReader.GetInt32(8),
                  TotalToolUseCountAtEndOfCycle = toolReader.IsDBNull(9)
                    ? null
                    : toolReader.GetInt32(9),
                  ConfiguredToolLifeCount = toolReader.IsDBNull(10)
                    ? null
                    : toolReader.GetInt32(10),
                }
              );
            }
          }

          var cycleContainerIds = ImmutableList.CreateBuilder<Guid>();
          if (ty == LogType.BasketCycle && !start)
          {
            membershipCmd.Parameters[0].Value = ctr;
            using var membershipReader = membershipCmd.ExecuteReader();
            while (membershipReader.Read())
              cycleContainerIds.Add(Guid.Parse(membershipReader.GetString(0)));
          }

          yield return new LogEntry()
          {
            Counter = ctr,
            Material = matLst.ToImmutable(),
            Pallet = pal,
            ContainerId = containerId,
            CycleEndContainerIds =
              cycleContainerIds.Count > 0 ? cycleContainerIds.ToImmutable() : null,
            LogType = ty,
            LocationName = locName,
            LocationNum = locNum,
            Program = prog,
            StartOfCycle = start,
            EndTimeUTC = timeUTC,
            Result = result,
            ElapsedTime = elapsed,
            ActiveOperationTime = active,
            ProgramDetails = progDetails.Count == 0 ? null : progDetails.ToImmutable(),
            Tools = tools.Count == 0 ? null : tools.ToImmutable(),
          };
        }
      } // close usings
    }

    private static readonly string ignoreInvalidEventCondition =
      "   NOT EXISTS ("
      + "    SELECT 1 FROM program_details d "
      + "      WHERE s.Counter = d.Counter AND d.Key = 'PalletCycleInvalidated'"
      + "   ) AND "
      + "   StationLoc != ("
      + ((int)LogType.SwapMaterialOnPallet).ToString()
      + ")";

    public IEnumerable<LogEntry> GetLogEntries(System.DateTime startUTC, System.DateTime endUTC)
    {
      using (var trans = _connection.BeginTransaction())
      using (var cmd = _connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText =
          "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName, ContainerId "
          + " FROM stations WHERE TimeUTC >= $start AND TimeUTC <= $end ORDER BY Counter ASC";

        cmd.Parameters.Add("start", SqliteType.Integer).Value = startUTC.Ticks;
        cmd.Parameters.Add("end", SqliteType.Integer).Value = endUTC.Ticks;

        using (var reader = cmd.ExecuteReader())
        {
          foreach (var l in LoadLog(reader, trans))
          {
            yield return l;
          }
        }
      }
    }

    public IEnumerable<LogEntry> GetRecentLog(long counter, DateTime? expectedEndUTCofLastSeen)
    {
      using (var trans = _connection.BeginTransaction())
      {
        if (expectedEndUTCofLastSeen.HasValue)
        {
          using (var cmd = _connection.CreateCommand())
          {
            cmd.Transaction = trans;
            cmd.CommandText = "SELECT TimeUTC FROM stations WHERE Counter = $cntr";
            cmd.Parameters.Add("cntr", SqliteType.Integer).Value = counter;
            var val = cmd.ExecuteScalar();
            if (val == null)
              throw new ConflictRequestException("Counter " + counter.ToString() + " not found");
            if (expectedEndUTCofLastSeen.Value.Ticks != (long)val)
              throw new ConflictRequestException(
                "Counter " + counter.ToString() + " has different end time"
              );
          }
        }

        using (var cmd = _connection.CreateCommand())
        {
          cmd.Transaction = trans;
          cmd.CommandText =
            "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName, ContainerId "
            + " FROM stations WHERE Counter > $cntr ORDER BY Counter ASC";
          cmd.Parameters.Add("cntr", SqliteType.Integer).Value = counter;

          using (var reader = cmd.ExecuteReader())
          {
            foreach (var l in LoadLog(reader, trans))
            {
              yield return l;
            }
          }
        }
      }
    }

    public LogEntry MostRecentLogEntryForForeignID(string foreignID)
    {
      using (var trans = _connection.BeginTransaction())
      using (var cmd = _connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText =
          "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName, ContainerId "
          + " FROM stations WHERE ForeignID = $foreign ORDER BY Counter DESC LIMIT 1";
        cmd.Parameters.Add("foreign", SqliteType.Text).Value = foreignID;

        using (var reader = cmd.ExecuteReader())
        {
          return LoadLog(reader, trans).FirstOrDefault();
        }
      }
    }

    public LogEntry MostRecentLogEntryLessOrEqualToForeignID(string foreignID)
    {
      using (var trans = _connection.BeginTransaction())
      using (var cmd = _connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText =
          "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName, ContainerId "
          + " FROM stations WHERE ForeignID <= $foreign ORDER BY ForeignID DESC, Counter DESC LIMIT 1";
        cmd.Parameters.Add("foreign", SqliteType.Text).Value = foreignID;

        using (var reader = cmd.ExecuteReader())
        {
          return LoadLog(reader, trans).FirstOrDefault();
        }
      }
    }

    public string OriginalMessageByForeignID(string foreignID)
    {
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText =
          "SELECT OriginalMessage "
          + " FROM stations WHERE ForeignID = $foreign ORDER BY Counter DESC LIMIT 1";
        cmd.Parameters.Add("foreign", SqliteType.Text).Value = foreignID;

        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            if (reader.IsDBNull(0))
            {
              return "";
            }
            else
            {
              return reader.GetString(0);
            }
          }
        }
      }
      return "";
    }

    public IEnumerable<LogEntry> GetLogForMaterial(
      long materialID,
      bool includeInvalidatedCycles = true
    )
    {
      if (materialID < 0)
      {
        yield break;
      }

      using var trans = _connection.BeginTransaction();
      using var cmd = _connection.CreateCommand();
      cmd.Transaction = trans;
      cmd.CommandText =
        "SELECT s.Counter, s.Pallet, s.StationLoc, s.StationNum, s.Program, s.Start, s.TimeUTC, s.Result, s.EndOfRoute, s.Elapsed, s.ActiveTime, s.StationName "
        + " FROM stations s "
        + " WHERE s.Counter IN (SELECT m.Counter FROM stations_mat m WHERE m.MaterialID = $mat)"
        + (includeInvalidatedCycles ? "" : " AND " + ignoreInvalidEventCondition)
        + " ORDER BY s.Counter ASC";
      cmd.Parameters.Add("mat", SqliteType.Integer).Value = materialID;

      using var reader = cmd.ExecuteReader();

      foreach (var e in LoadLog(reader, trans))
      {
        yield return e;
      }
    }

    public IEnumerable<LogEntry> GetLogForMaterial(
      IEnumerable<long> materialIDs,
      bool includeInvalidatedCycles = true
    )
    {
      using var cmd = _connection.CreateCommand();
      using var trans = _connection.BeginTransaction();

      cmd.Transaction = trans;
      cmd.CommandText = "CREATE TEMP TABLE temp_mat_ids (MaterialID INTEGER)";
      cmd.ExecuteNonQuery();

      cmd.CommandText = "INSERT INTO temp_mat_ids (MaterialID) VALUES ($mat)";
      cmd.Parameters.Add("mat", SqliteType.Integer);
      foreach (var mat in materialIDs)
      {
        cmd.Parameters[0].Value = mat;
        cmd.ExecuteNonQuery();
      }

      cmd.CommandText =
        "SELECT s.Counter, s.Pallet, s.StationLoc, s.StationNum, s.Program, s.Start, s.TimeUTC, s.Result, s.EndOfRoute, s.Elapsed, s.ActiveTime, s.StationName "
        + " FROM stations s WHERE s.Counter IN "
        + "     (SELECT m.Counter FROM stations_mat m WHERE m.MaterialID IN "
        + "        (SELECT t.MaterialID FROM temp_mat_ids t))"
        + (includeInvalidatedCycles ? "" : " AND " + ignoreInvalidEventCondition)
        + " ORDER BY s.Counter ASC";
      cmd.Parameters.Clear();

      using var reader = cmd.ExecuteReader();
      foreach (var e in LoadLog(reader, trans))
      {
        yield return e;
      }
    }

    public IEnumerable<LogEntry> GetLogForSerial(string serial)
    {
      using (var trans = _connection.BeginTransaction())
      using (var cmd = _connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText =
          "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName, ContainerId "
          + " FROM stations WHERE Counter IN (SELECT stations_mat.Counter FROM matdetails INNER JOIN stations_mat ON stations_mat.MaterialID = matdetails.MaterialID WHERE matdetails.Serial = $ser) ORDER BY Counter ASC";
        cmd.Parameters.Add("ser", SqliteType.Text).Value = serial;

        using (var reader = cmd.ExecuteReader())
        {
          foreach (var l in LoadLog(reader, trans))
          {
            yield return l;
          }
        }
      }
    }

    public IEnumerable<LogEntry> GetLogForJobUnique(string jobUnique)
    {
      using (var trans = _connection.BeginTransaction())
      using (var cmd = _connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText =
          "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName, ContainerId "
          + " FROM stations WHERE Counter IN (SELECT stations_mat.Counter FROM matdetails INNER JOIN stations_mat ON stations_mat.MaterialID = matdetails.MaterialID WHERE matdetails.UniqueStr = $uniq) ORDER BY Counter ASC";
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = jobUnique;

        using (var reader = cmd.ExecuteReader())
        {
          foreach (var l in LoadLog(reader, trans))
          {
            yield return l;
          }
        }
      }
    }

    public IEnumerable<LogEntry> GetLogForWorkorder(string workorder)
    {
      using (var trans = _connection.BeginTransaction())
      using (var cmd = _connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText =
          "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName, ContainerId "
          + " FROM stations "
          + " WHERE Counter IN (SELECT stations_mat.Counter FROM matdetails INNER JOIN stations_mat ON stations_mat.MaterialID = matdetails.MaterialID WHERE matdetails.Workorder = $work) "
          + "    OR (Pallet = 0 AND Result = $work AND StationLoc = $workloc) "
          + " ORDER BY Counter ASC";
        cmd.Parameters.Add("work", SqliteType.Text).Value = workorder;
        cmd.Parameters.Add("workloc", SqliteType.Integer).Value = (int)LogType.WorkorderComment;

        using (var reader = cmd.ExecuteReader())
        {
          foreach (var l in LoadLog(reader, trans))
          {
            yield return l;
          }
        }
      }
    }

    public IEnumerable<LogEntry> GetLogOfAllCompletedParts(DateTime startUTC, DateTime endUTC)
    {
      // This gets all logs between the start and end dates, but may include log events earlier than startUTC
      // if it comes from a completed part which completed during the time range.  Thus, allows a view of all
      // logs for all parts which completed during the time range.
      var searchCompleted =
        @"
                SELECT Counter FROM stations_mat
                    WHERE MaterialId IN
                        (SELECT stations_mat.MaterialId FROM stations, stations_mat, matdetails
                            WHERE
                                stations.Counter = stations_mat.Counter
                                AND
                                stations.StationLoc = $loadty
                                AND
                                stations.Result = 'UNLOAD'
                                AND
                                stations.Start = 0
                                AND
                                stations.TimeUTC <= $endUTC
                                AND
                                stations.TimeUTC >= $startUTC
                                AND
                                stations_mat.MaterialID = matdetails.MaterialID
                                AND
                                stations_mat.Process = matdetails.NumProcesses
                        )";

      using (var trans = _connection.BeginTransaction())
      using (var cmd = _connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText =
          "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName, ContainerId "
          + " FROM stations WHERE Counter IN ("
          + searchCompleted
          + ") ORDER BY Counter ASC";
        cmd.Parameters.Add("loadty", SqliteType.Integer).Value = (int)LogType.LoadUnloadCycle;
        cmd.Parameters.Add("endUTC", SqliteType.Integer).Value = endUTC.Ticks;
        cmd.Parameters.Add("startUTC", SqliteType.Integer).Value = startUTC.Ticks;

        using (var reader = cmd.ExecuteReader())
        {
          foreach (var l in LoadLog(reader, trans))
          {
            yield return l;
          }
        }
      }
    }

    public IEnumerable<LogEntry> CompletedUnloadsSince(long counter)
    {
      using var trans = _connection.BeginTransaction();
      using var cmd = _connection.CreateCommand();
      cmd.Transaction = trans;
      cmd.CommandText =
        "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName, ContainerId "
        + " FROM stations WHERE Counter > $cntr AND StationLoc = $loadty AND Result = 'UNLOAD' AND Start = 0";
      cmd.Parameters.Add("cntr", SqliteType.Integer).Value = counter;
      cmd.Parameters.Add("loadty", SqliteType.Integer).Value = (int)LogType.LoadUnloadCycle;

      using var reader = cmd.ExecuteReader();
      foreach (var l in LoadLog(reader, trans))
      {
        yield return l;
      }
    }

    //Loads the log for the current pallet cycle, which is all events from the last Result = "PalletCycle"
    public List<LogEntry> CurrentPalletLog(int pallet, bool includeLastPalletCycleEvt = false)
    {
      using (var trans = _connection.BeginTransaction())
      {
        var ret = CurrentPalletLog(pallet, includeLastPalletCycleEvt, trans);
        trans.Commit();
        return ret;
      }
    }

    private List<LogEntry> CurrentPalletLog(
      int pallet,
      bool includeLastPalletCycleEvt,
      SqliteTransaction trans
    )
    {
      using (var cmd = _connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText =
          "SELECT MAX(Counter) FROM stations where Pallet = $pal AND Result = 'PalletCycle'";
        cmd.Parameters.Add("pal", SqliteType.Integer).Value = pallet;

        var counter = cmd.ExecuteScalar();

        if (counter == DBNull.Value)
        {
          cmd.CommandText =
            "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName, ContainerId "
            + " FROM stations s "
            + " WHERE Pallet = $pal AND "
            + ignoreInvalidEventCondition
            + " ORDER BY Counter ASC";
          using (var reader = cmd.ExecuteReader())
          {
            return LoadLog(reader, trans).ToList();
          }
        }
        else
        {
          cmd.CommandText =
            "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName, ContainerId "
            + " FROM stations s "
            + " WHERE Pallet = $pal AND Counter "
            + (includeLastPalletCycleEvt ? ">=" : ">")
            + " $cntr AND "
            + ignoreInvalidEventCondition
            + " ORDER BY Counter ASC";
          cmd.Parameters.Add("cntr", SqliteType.Integer).Value = (long)counter;

          using (var reader = cmd.ExecuteReader())
          {
            return LoadLog(reader, trans).ToList();
          }
        }
      }
    }

    private List<LogEntry> PalletEventsPrecedingCurrentCycle(
      int pallet,
      long palletCycleStartCounter,
      SqliteTransaction trans
    )
    {
      using var cmd = _connection.CreateCommand();
      cmd.Transaction = trans;
      cmd.CommandText =
        "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName, ContainerId "
        + " FROM stations s "
        + " WHERE Pallet = $pal AND Counter < $cycleStart "
        + " AND "
        + ignoreInvalidEventCondition
        + " AND Counter > COALESCE(("
        + "   SELECT MAX(Counter) FROM stations "
        + "   WHERE Pallet = $pal AND Result = 'PalletCycle' AND Counter < $cycleStart"
        + " ), 0)"
        + " ORDER BY Counter ASC";
      cmd.Parameters.Add("pal", SqliteType.Integer).Value = pallet;
      cmd.Parameters.Add("cycleStart", SqliteType.Integer).Value = palletCycleStartCounter;

      using var reader = cmd.ExecuteReader();
      return LoadLog(reader, trans).ToList();
    }

    public List<LogEntry> CurrentBasketLog(int basketId, bool includeLastCycleEvt = false)
    {
      using (var trans = _connection.BeginTransaction())
      {
        var ret = CurrentBasketLog(basketId, includeLastCycleEvt, trans);
        trans.Commit();
        return ret;
      }
    }

    private List<LogEntry> CurrentBasketLog(
      int basketId,
      bool includeLastCycleEvt,
      SqliteTransaction trans
    )
    {
      using (var cmd = _connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText =
          "SELECT MAX(Counter) FROM stations WHERE Pallet = $basketId AND Result = 'BasketCycle'";
        cmd.Parameters.Add("basketId", SqliteType.Integer).Value = basketId;

        var counter = cmd.ExecuteScalar();

        if (counter == DBNull.Value)
        {
          cmd.CommandText =
            "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName, ContainerId "
            + " FROM stations s "
            + " WHERE Pallet = $basketId AND ContainerId IS NULL AND "
            + ignoreInvalidEventCondition
            + " ORDER BY Counter ASC";
          cmd.Parameters.Add("loadUnloadType", SqliteType.Integer).Value = (int)
            LogType.BasketLoadUnload;
          cmd.Parameters.Add("cycleType", SqliteType.Integer).Value = (int)LogType.BasketCycle;
          using (var reader = cmd.ExecuteReader())
          {
            return LoadLog(reader, trans).ToList();
          }
        }
        else
        {
          cmd.CommandText =
            "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName, ContainerId "
            + " FROM stations s "
            + " WHERE Pallet = $basketId AND ContainerId IS NULL AND Counter "
            + (includeLastCycleEvt ? ">=" : ">")
            + " $cntr AND "
            + ignoreInvalidEventCondition
            + " ORDER BY Counter ASC";
          cmd.Parameters.Add("loadUnloadType", SqliteType.Integer).Value = (int)
            LogType.BasketLoadUnload;
          cmd.Parameters.Add("cycleType", SqliteType.Integer).Value = (int)LogType.BasketCycle;
          cmd.Parameters.Add("cntr", SqliteType.Integer).Value = (long)counter;

          using (var reader = cmd.ExecuteReader())
          {
            return LoadLog(reader, trans).ToList();
          }
        }
      }
    }

    public ImmutableList<LogEntry> CurrentBasketLog(
      ContainerIdentity basketIdentity,
      bool includeLastCycleEvt = false
    )
    {
      using var trans = _connection.BeginTransaction();
      var logs = basketIdentity switch
      {
        ContainerIdentity.None => [],
        ContainerIdentity.Numbered numbered when numbered.ContainerNum > 0 =>
          CurrentNumberedBasketAndFragments(numbered.ContainerNum, includeLastCycleEvt, trans),
        ContainerIdentity.Uuid uuid when uuid.ContainerId != Guid.Empty => CurrentUuidFragment(
          uuid.ContainerId,
          trans
        ),
        _ => throw new ArgumentException("Invalid basket identity.", nameof(basketIdentity)),
      };
      trans.Commit();
      return logs;
    }

    private ImmutableList<LogEntry> CurrentNumberedBasketAndFragments(
      int basketId,
      bool includeLastCycleEvt,
      SqliteTransaction trans
    )
    {
      var logs = CurrentBasketLog(basketId, includeLastCycleEvt, trans);
      using var cmd = _connection.CreateCommand();
      cmd.Transaction = trans;
      cmd.CommandText =
        "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName, ContainerId "
        + "FROM stations s WHERE ContainerId IN (SELECT ContainerId FROM current_basket_identity_hints WHERE BasketNum = $num) AND "
        + ignoreInvalidEventCondition
        + " ORDER BY Counter";
      cmd.Parameters.Add("num", SqliteType.Integer).Value = basketId;
      using var reader = cmd.ExecuteReader();
      logs.AddRange(LoadLog(reader, trans));
      return logs.OrderBy(log => log.Counter).ToImmutableList();
    }

    private ImmutableList<LogEntry> CurrentUuidFragment(Guid containerId, SqliteTransaction trans)
    {
      using var cmd = _connection.CreateCommand();
      cmd.Transaction = trans;
      cmd.CommandText =
        "SELECT EXISTS(SELECT 1 FROM basket_cycle_container_ids WHERE ContainerId = $id)";
      cmd.Parameters.Add("id", SqliteType.Text).Value = containerId.ToString("D");
      if ((long)cmd.ExecuteScalar() != 0)
        return [];

      cmd.CommandText =
        "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName, ContainerId "
        + "FROM stations s WHERE ContainerId = $id AND "
        + ignoreInvalidEventCondition
        + " ORDER BY Counter";
      using var reader = cmd.ExecuteReader();
      return LoadLog(reader, trans).ToImmutableList();
    }

    public ImmutableList<CurrentBasketIdentityHint> GetCurrentBasketIdentityHints(
      int? basketNum = null
    )
    {
      using var cmd = _connection.CreateCommand();
      cmd.CommandText =
        "SELECT ContainerId, BasketNum, HintCounter FROM current_basket_identity_hints "
        + (basketNum.HasValue ? "WHERE BasketNum = $num " : "")
        + "ORDER BY BasketNum, ContainerId";
      if (basketNum.HasValue)
        cmd.Parameters.Add("num", SqliteType.Integer).Value = basketNum.Value;
      using var reader = cmd.ExecuteReader();
      var hints = ImmutableList.CreateBuilder<CurrentBasketIdentityHint>();
      while (reader.Read())
      {
        hints.Add(
          new CurrentBasketIdentityHint
          {
            ContainerId = Guid.Parse(reader.GetString(0)),
            BasketNum = reader.GetInt32(1),
            HintEventCounter = reader.GetInt64(2),
          }
        );
      }
      return hints.ToImmutable();
    }

    public ImmutableList<Guid> GetUnresolvedOpenBasketContainerIds()
    {
      using var cmd = _connection.CreateCommand();
      cmd.CommandText =
        "SELECT DISTINCT s.ContainerId FROM stations s "
        + "WHERE s.ContainerId IS NOT NULL "
        + "AND s.StationLoc IN ($loadUnloadType, $locationType, $snapshotType, $cycleType) "
        + "AND "
        + ignoreInvalidEventCondition
        + " "
        + "AND NOT EXISTS(SELECT 1 FROM current_basket_identity_hints h WHERE h.ContainerId = s.ContainerId) "
        + "AND NOT EXISTS(SELECT 1 FROM basket_cycle_container_ids f WHERE f.ContainerId = s.ContainerId) "
        + "ORDER BY s.ContainerId";
      cmd.Parameters.Add("loadUnloadType", SqliteType.Integer).Value = (int)
        LogType.BasketLoadUnload;
      cmd.Parameters.Add("locationType", SqliteType.Integer).Value = (int)LogType.BasketInLocation;
      cmd.Parameters.Add("snapshotType", SqliteType.Integer).Value = (int)
        LogType.BasketContentSnapshot;
      cmd.Parameters.Add("cycleType", SqliteType.Integer).Value = (int)LogType.BasketCycle;
      using var reader = cmd.ExecuteReader();
      var ids = ImmutableList.CreateBuilder<Guid>();
      while (reader.Read())
        ids.Add(Guid.Parse(reader.GetString(0)));
      return ids.ToImmutable();
    }

    public IEnumerable<ToolSnapshot> ToolPocketSnapshotForCycle(long counter)
    {
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText =
          "SELECT PocketNumber, Tool, CurrentUse, ToolLife, CurrentCount, LifeCount, Serial FROM tool_snapshots WHERE Counter = $cntr";
        cmd.Parameters.Add("cntr", SqliteType.Integer).Value = counter;

        using (var reader = cmd.ExecuteReader())
        {
          var ret = new List<ToolSnapshot>();
          while (reader.Read())
          {
            ret.Add(
              new ToolSnapshot()
              {
                Pocket = reader.GetInt32(0),
                ToolName = reader.GetString(1),
                CurrentUse = reader.IsDBNull(2) ? null : TimeSpan.FromTicks(reader.GetInt64(2)),
                TotalLifeTime = reader.IsDBNull(3) ? null : TimeSpan.FromTicks(reader.GetInt64(3)),
                CurrentUseCount = reader.IsDBNull(4) ? null : (int?)reader.GetInt32(4),
                TotalLifeCount = reader.IsDBNull(5) ? null : (int?)reader.GetInt32(5),
                Serial = reader.IsDBNull(6) ? null : reader.GetString(6),
              }
            );
          }
          return ret;
        }
      }
    }

    public System.DateTime MaxLogDate()
    {
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText = "SELECT MAX(TimeUTC) FROM stations";

        System.DateTime ret = DateTime.MinValue;

        using (var reader = cmd.ExecuteReader())
        {
          if (reader.Read())
          {
            if (!reader.IsDBNull(0))
            {
              ret = new DateTime(reader.GetInt64(0), DateTimeKind.Utc);
            }
          }
        }

        return ret;
      }
    }

    public string MaxForeignID()
    {
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText = "SELECT MAX(ForeignID) FROM stations WHERE ForeignID IS NOT NULL";
        var maxStat = cmd.ExecuteScalar();

        if (maxStat == DBNull.Value)
          return "";
        else
          return (string)maxStat;
      }
    }

    public string ForeignIDForCounter(long counter)
    {
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText = "SELECT ForeignID FROM stations WHERE Counter = $cntr";
        cmd.Parameters.Add("cntr", SqliteType.Integer).Value = counter;
        var ret = cmd.ExecuteScalar();
        if (ret == DBNull.Value)
          return "";
        else if (ret == null)
          return "";
        else
          return (string)ret;
      }
    }

    public bool CycleExists(DateTime endUTC, int pal, LogType logTy, string locName, int locNum)
    {
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText =
          "SELECT COUNT(*) FROM stations WHERE "
          + "TimeUTC = $time AND Pallet = $pal AND StationLoc = $loc AND StationNum = $locnum AND StationName = $locname";
        cmd.Parameters.Add("time", SqliteType.Integer).Value = endUTC.Ticks;
        cmd.Parameters.Add("pal", SqliteType.Integer).Value = pal;
        cmd.Parameters.Add("loc", SqliteType.Integer).Value = (int)logTy;
        cmd.Parameters.Add("locnum", SqliteType.Integer).Value = locNum;
        cmd.Parameters.Add("locname", SqliteType.Text).Value = locName;

        var ret = cmd.ExecuteScalar();
        if (ret == null || Convert.ToInt32(ret) <= 0)
          return false;
        else
          return true;
      }
    }

    public ImmutableList<ActiveWorkorder> GetActiveWorkorder(string workorder)
    {
      return GetActiveWorkorders(workorderToFilter: workorder, additionalWorkorders: null);
    }

    public ImmutableList<ActiveWorkorder> GetActiveWorkorders(
      IReadOnlySet<string> additionalWorkorders = null
    )
    {
      return GetActiveWorkorders(
        workorderToFilter: null,
        additionalWorkorders: additionalWorkorders
      );
    }

    private ImmutableList<ActiveWorkorder> GetActiveWorkorders(
      string workorderToFilter,
      IReadOnlySet<string> additionalWorkorders
    )
    {
      using var trans = _connection.BeginTransaction();

      // we distinguish between a cell never using workorders at all and return null
      // vs a cell that has no workorders currently active where we return an empty list
      using (var checkExistingRowCmd = _connection.CreateCommand())
      {
        checkExistingRowCmd.CommandText = "SELECT EXISTS (SELECT 1 FROM workorder_cache LIMIT 1)";
        if (!Convert.ToBoolean(checkExistingRowCmd.ExecuteScalar()))
        {
          return null;
        }
      }

      var lastSchId = LatestSimulationScheduleId(trans);
      if (string.IsNullOrEmpty(lastSchId))
      {
        // lastSchId is only used in the left outer join, and if it is null or empty it means
        // the sim_workorders table is empty.  Therefore, we can use anything for the lastSchId
        // and it will not affect the results.
        lastSchId = "";
      }

      if (additionalWorkorders != null && additionalWorkorders.Count > 0)
      {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = trans;
        cmd.CommandText = "CREATE TEMP TABLE temp_workorders (Workorder TEXT)";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "INSERT INTO temp_workorders (Workorder) VALUES ($workorder)";
        cmd.Parameters.Add("workorder", SqliteType.Text);
        foreach (var workorder in additionalWorkorders)
        {
          cmd.Parameters[0].Value = workorder;
          cmd.ExecuteNonQuery();
        }
      }

      var workQry =
        $@"
          SELECT uw.Workorder, uw.Part, uw.Quantity, uw.DueDate, uw.Priority, sw.SimulatedStartDay, sw.SimulatedFilledDay,
          (
            SELECT COUNT(matdetails.MaterialID)
            FROM stations, stations_mat, matdetails
            WHERE
            stations.Counter = stations_mat.Counter
            AND
            stations.StationLoc = $loadty
            AND
            stations.Result = 'UNLOAD'
            AND
            stations.Start = 0
            AND
            stations_mat.Process = matdetails.NumProcesses
            AND
            stations_mat.MaterialID = matdetails.MaterialID
            AND
            matdetails.Workorder = uw.Workorder
            AND
            matdetails.PartName = uw.Part
          ) AS Completed

          FROM workorder_cache uw
          LEFT OUTER JOIN sim_workorders sw ON sw.ScheduleId = $schid AND uw.Workorder = sw.Workorder AND uw.Part = sw.Part
          WHERE
      ";

      if (!string.IsNullOrEmpty(workorderToFilter))
      {
        workQry += " uw.Workorder = $workorder";
      }
      else
      {
        workQry += " uw.Archived = 0";
      }

      if (additionalWorkorders != null && additionalWorkorders.Count > 0)
      {
        workQry += " OR uw.Workorder IN (SELECT Workorder FROM temp_workorders)";
      }

      // For a given matdetails.MaterialID, check either for the most recent quarantine event
      // or for a non-invalidated load/mc event, since a load/mc after a quarantine cancels
      // the quarantine.  Thus by checking the result, can determine if the part is still in
      // quarantine.
      var quarantineSubQuery =
        @"(
           SELECT s.StationLoc
            FROM stations s, stations_mat m
            WHERE
              s.Counter = m.Counter
              AND
              m.MaterialID = matdetails.MaterialID
              AND
              (s.StationLoc = $quarantineTy
               OR
               (s.StationLoc IN ($addQueueTy, $removeQueueTy) AND s.Program = 'Quarantine')
               OR
               (s.StationLoc IN ($loadty, $mcty) AND NOT EXISTS(
                 SELECT 1 FROM program_details d
                   WHERE s.Counter = d.Counter AND d.Key = 'PalletCycleInvalidated'
               ))
              )
            ORDER BY s.Counter DESC
            LIMIT 1
          )
         ";

      // check if an inspection failed for matdetails.MaterialID
      var inspFailedSubQuery =
        @"
          EXISTS(
           SELECT 1
            FROM stations s, stations_mat m
            WHERE
              s.Counter = m.Counter
              AND
              m.MaterialID = matdetails.MaterialID
              AND
              s.StationLoc = $inspTy
              AND
              s.Result = 'False'
          )
         ";

      // load the most recent closeout event for matdetails.MaterialID
      var closeoutSubQuery =
        @"(
           SELECT s.Result
            FROM stations s, stations_mat m
            WHERE
              s.Counter = m.Counter
              AND
              m.MaterialID = matdetails.MaterialID
              AND
              s.StationLoc = $closeoutTy
            ORDER BY s.Counter DESC
            LIMIT 1
         )";

      var serialQry =
        $@"
				SELECT matdetails.MaterialID, matdetails.Serial, {quarantineSubQuery} AS Quarantined, {inspFailedSubQuery} AS InspFailed, {closeoutSubQuery} AS Closeout
            FROM matdetails
				    WHERE
					    matdetails.Workorder = $workid
              AND
              matdetails.PartName = $partname";

      var timeQry =
        @"
                SELECT StationName, SUM(Elapsed / totcount), SUM(ActiveTime / totcount)
                    FROM
                        (
                            SELECT s.StationName, s.Elapsed, s.ActiveTime,
                                   (SELECT COUNT(*) FROM stations_mat AS m2 WHERE m2.Counter = s.Counter) totcount
                              FROM stations AS s, stations_mat AS m, matdetails
                              WHERE
                                s.Counter = m.Counter
                                AND
                                m.MaterialID = matdetails.MaterialID
                                AND
                                matdetails.Workorder = $workid
                                AND
                                matdetails.PartName = $partname
                                AND
                                s.Start = 0
                                AND
                                s.StationLoc IN ($loadty, $mcty)
                                AND NOT EXISTS(
                                  SELECT 1 FROM program_details d
                                    WHERE s.Counter = d.Counter AND d.Key = 'PalletCycleInvalidated'
                                )
                        )
                    GROUP BY StationName";

      var commentQry =
        @"
          SELECT
            TimeUTC,
            (SELECT pd.Value FROM program_details AS pd WHERE pd.Counter = s.Counter AND pd.Key = 'Comment')
              AS Comment
          FROM
            stations AS s
          WHERE
            s.Pallet = 0
            AND
            s.Result = $workid
            AND
            s.StationLoc = $workty
        ";

      using var workCmd = _connection.CreateCommand();
      using var serialCmd = _connection.CreateCommand();
      using var quarantinedCmd = _connection.CreateCommand();
      using var timeCmd = _connection.CreateCommand();
      using var commentCmd = _connection.CreateCommand();

      workCmd.Transaction = trans;
      workCmd.CommandText = workQry;
      workCmd.Parameters.Add("schid", SqliteType.Text).Value = lastSchId;
      workCmd.Parameters.Add("loadty", SqliteType.Integer).Value = (int)LogType.LoadUnloadCycle;
      if (!string.IsNullOrEmpty(workorderToFilter))
      {
        workCmd.Parameters.Add("workorder", SqliteType.Text).Value = workorderToFilter;
      }
      serialCmd.CommandText = serialQry;
      serialCmd.Parameters.Add("workid", SqliteType.Text);
      serialCmd.Parameters.Add("partname", SqliteType.Text);
      serialCmd.Parameters.Add("quarantineTy", SqliteType.Integer).Value = (int)
        LogType.SignalQuarantine;
      serialCmd.Parameters.Add("addQueueTy", SqliteType.Integer).Value = (int)LogType.AddToQueue;
      serialCmd.Parameters.Add("removeQueueTy", SqliteType.Integer).Value = (int)
        LogType.RemoveFromQueue;
      serialCmd.Parameters.Add("loadty", SqliteType.Integer).Value = (int)LogType.LoadUnloadCycle;
      serialCmd.Parameters.Add("mcty", SqliteType.Integer).Value = (int)LogType.MachineCycle;
      serialCmd.Parameters.Add("inspTy", SqliteType.Integer).Value = (int)LogType.InspectionResult;
      serialCmd.Parameters.Add("closeoutTy", SqliteType.Integer).Value = (int)LogType.CloseOut;
      timeCmd.CommandText = timeQry;
      timeCmd.Parameters.Add("workid", SqliteType.Text);
      timeCmd.Parameters.Add("partname", SqliteType.Text);
      timeCmd.Parameters.Add("loadty", SqliteType.Integer).Value = (int)LogType.LoadUnloadCycle;
      timeCmd.Parameters.Add("mcty", SqliteType.Integer).Value = (int)LogType.MachineCycle;
      commentCmd.CommandText = commentQry;
      commentCmd.Transaction = trans;
      commentCmd.Parameters.Add("workty", SqliteType.Integer).Value = (int)LogType.WorkorderComment;
      var commentWorkParam = commentCmd.Parameters.Add("workid", SqliteType.Text);

      var ret = ImmutableList.CreateBuilder<ActiveWorkorder>();

      using var workReader = workCmd.ExecuteReader();

      while (workReader.Read())
      {
        var workorder = workReader.GetString(0);
        var part = workReader.GetString(1);
        var qty = workReader.GetInt32(2);
        var dueDate = new DateTime(workReader.GetInt64(3));
        var priority = workReader.GetInt32(4);
        var start = workReader.IsDBNull(5)
          ? (DateOnly?)null
          : DateOnly.FromDayNumber(workReader.GetInt32(5));
        var filled = workReader.IsDBNull(6)
          ? (DateOnly?)null
          : DateOnly.FromDayNumber(workReader.GetInt32(6));
        var completed = workReader.IsDBNull(7) ? 0 : workReader.GetInt32(7);

        serialCmd.Parameters[0].Value = workorder;
        serialCmd.Parameters[1].Value = part;
        var serials = ImmutableList.CreateBuilder<WorkorderMaterial>();
        using (var serialReader = serialCmd.ExecuteReader())
        {
          while (serialReader.Read())
          {
            bool quarantined = false;
            if (!serialReader.IsDBNull(2))
            {
              var quarTy = (LogType)serialReader.GetInt32(2);
              quarantined =
                quarTy == LogType.SignalQuarantine
                || quarTy == LogType.AddToQueue
                || quarTy == LogType.RemoveFromQueue;
            }
            serials.Add(
              new WorkorderMaterial()
              {
                MaterialID = serialReader.GetInt64(0),
                Serial = serialReader.IsDBNull(1) ? null : serialReader.GetString(1),
                Quarantined = quarantined,
                InspectionFailed = !serialReader.IsDBNull(3) && serialReader.GetBoolean(3),
                Closeout =
                  serialReader.IsDBNull(4) ? WorkorderSerialCloseout.None
                  : serialReader.GetString(4) == "Failed" ? WorkorderSerialCloseout.CloseOutFailed
                  : WorkorderSerialCloseout.ClosedOut,
              }
            );
          }
        }

        timeCmd.Parameters[0].Value = workorder;
        timeCmd.Parameters[1].Value = part;
        var elapsed = ImmutableDictionary.CreateBuilder<string, TimeSpan>();
        var active = ImmutableDictionary.CreateBuilder<string, TimeSpan>();
        using (var timeReader = timeCmd.ExecuteReader())
        {
          while (timeReader.Read())
          {
            var station = timeReader.GetString(0);
            var el = timeReader.IsDBNull(1)
              ? TimeSpan.Zero
              : TimeSpan.FromTicks((long)timeReader.GetDecimal(1));
            var ac = timeReader.IsDBNull(2)
              ? TimeSpan.Zero
              : TimeSpan.FromTicks((long)timeReader.GetDecimal(2));
            elapsed.Add(station, el);
            active.Add(station, ac);
          }
        }

        var comments = ImmutableList.CreateBuilder<WorkorderComment>();
        commentWorkParam.Value = workorder;
        using (var commentReader = commentCmd.ExecuteReader())
        {
          while (commentReader.Read())
          {
            comments.Add(
              new WorkorderComment()
              {
                TimeUTC = new DateTime(commentReader.GetInt64(0)),
                Comment = commentReader.IsDBNull(1) ? "" : commentReader.GetString(1),
              }
            );
          }
        }

        ret.Add(
          new ActiveWorkorder()
          {
            WorkorderId = workorder,
            Part = part,
            PlannedQuantity = qty,
            DueDate = dueDate,
            Priority = priority,
            SimulatedStart = start,
            SimulatedFilled = filled,
            Comments = comments.Count == 0 ? null : comments.ToImmutable(),
            CompletedQuantity = completed,
            Material = serials.Count == 0 ? null : serials.ToImmutable(),
            ElapsedStationTime = elapsed.ToImmutable(),
            ActiveStationTime = active.ToImmutable(),
          }
        );
      }

      return ret.ToImmutable();
    }

    public ImmutableSortedSet<string> GetWorkordersForUnique(string jobUnique)
    {
      using (var cmd = _connection.CreateCommand())
      using (var trans = _connection.BeginTransaction())
      {
        cmd.CommandText =
          "SELECT DISTINCT Workorder FROM matdetails WHERE UniqueStr = $uniq AND Workorder IS NOT NULL ORDER BY Workorder ASC";
        cmd.Transaction = trans;
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = jobUnique;

        var ret = ImmutableSortedSet.CreateBuilder<string>();
        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            ret.Add(reader.GetString(0));
          }
        }

        trans.Commit();
        return ret.ToImmutable();
      }
    }

    #endregion

    #region Adding

    private record NewEventLogEntry
    {
      public required IEnumerable<EventLogMaterial> Material { get; init; }
      public required LogType LogType { get; init; }
      public required bool StartOfCycle { get; init; }
      public required DateTime EndTimeUTC { get; init; }
      public required string LocationName { get; init; }
      public required int LocationNum { get; init; }
      public required int Pallet { get; init; }
      public Guid? ContainerId { get; init; }
      public ImmutableList<Guid> CycleEndContainerIds { get; init; }
      public required string Program { get; init; }
      public required string Result { get; init; }
      public TimeSpan ElapsedTime { get; init; } = TimeSpan.FromMinutes(-1); //time from cycle-start to cycle-stop
      public TimeSpan ActiveOperationTime { get; init; } = TimeSpan.Zero; //time that the machining or operation is actually active
      private Dictionary<string, string> _details = new Dictionary<string, string>();
      public IDictionary<string, string> ProgramDetails
      {
        get { return _details; }
      }
      public ImmutableList<ToolUse> Tools { get; init; } = ImmutableList<ToolUse>.Empty;
      public IEnumerable<ToolSnapshot> ToolPockets { get; init; }

      internal LogEntry ToLogEntry(long newCntr, Func<long, MaterialDetails> getDetails)
      {
        return new LogEntry()
        {
          Counter = newCntr,
          Material = this
            .Material.Select(m =>
            {
              var details = getDetails(m.MaterialID);
              int? path =
                details?.Paths == null ? null
                : details.Paths.TryGetValue(m.Process, out var p) ? p
                : null;
              return new LogMaterial()
              {
                MaterialID = m.MaterialID,
                Process = m.Process,
                Face = m.Face,
                JobUniqueStr = details?.JobUnique ?? "",
                PartName = details?.PartName ?? "",
                NumProcesses = details?.NumProcesses ?? 1,
                Serial = details?.Serial ?? "",
                Workorder = details?.Workorder ?? "",
                Path = path,
              };
            })
            .ToImmutableList(),
          LogType = this.LogType,
          StartOfCycle = this.StartOfCycle,
          EndTimeUTC = this.EndTimeUTC,
          LocationName = this.LocationName,
          LocationNum = this.LocationNum,
          Pallet = this.Pallet,
          ContainerId = this.ContainerId,
          CycleEndContainerIds = this.CycleEndContainerIds,
          Program = this.Program,
          Result = this.Result,
          ElapsedTime = this.ElapsedTime,
          ActiveOperationTime = this.ActiveOperationTime,
          ProgramDetails =
            this.ProgramDetails == null || this.ProgramDetails.Count == 0
              ? null
              : this.ProgramDetails.ToImmutableDictionary(),
          Tools = this.Tools == null || this.Tools.Count == 0 ? null : this.Tools,
        };
      }

      internal static NewEventLogEntry FromLogEntry(LogEntry e)
      {
        var ret = new NewEventLogEntry()
        {
          Material = e.Material.Select(EventLogMaterial.FromLogMat),
          Pallet = e.Pallet,
          ContainerId = e.ContainerId,
          CycleEndContainerIds = e.CycleEndContainerIds,
          LogType = e.LogType,
          LocationName = e.LocationName,
          LocationNum = e.LocationNum,
          Program = e.Program,
          StartOfCycle = e.StartOfCycle,
          EndTimeUTC = e.EndTimeUTC,
          Result = e.Result,
          ElapsedTime = e.ElapsedTime,
          ActiveOperationTime = e.ActiveOperationTime,
          Tools = e.Tools,
        };
        if (e.ProgramDetails != null)
        {
          foreach (var d in e.ProgramDetails)
          {
            ret.ProgramDetails[d.Key] = d.Value;
          }
        }
        return ret;
      }
    }

    private LogEntry AddLogEntry(
      IDbTransaction trans,
      NewEventLogEntry log,
      string foreignID,
      string origMessage
    )
    {
      ValidateLogEntry(log);
      if (log.ContainerId.HasValue)
        EnsureContainerIdOpen(log.ContainerId.Value, trans);
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText =
          "INSERT INTO stations(Pallet, StationLoc, StationName, StationNum, Program, Start, TimeUTC, Result, Elapsed, ActiveTime, ForeignID,OriginalMessage,ContainerId)"
          + "VALUES ($pal,$loc,$locname,$locnum,$prog,$start,$time,$result,$elapsed,$active,$foreign,$orig,$containerId)";

        cmd.Parameters.Add("pal", SqliteType.Integer).Value = log.Pallet;
        cmd.Parameters.Add("loc", SqliteType.Integer).Value = (int)log.LogType;
        cmd.Parameters.Add("locname", SqliteType.Text).Value = log.LocationName;
        cmd.Parameters.Add("locnum", SqliteType.Integer).Value = log.LocationNum;
        cmd.Parameters.Add("prog", SqliteType.Text).Value = log.Program;
        cmd.Parameters.Add("start", SqliteType.Integer).Value = log.StartOfCycle;
        cmd.Parameters.Add("time", SqliteType.Integer).Value = log.EndTimeUTC.Ticks;
        cmd.Parameters.Add("result", SqliteType.Text).Value = log.Result;
        cmd.Parameters.Add("containerId", SqliteType.Text).Value = log.ContainerId.HasValue
          ? log.ContainerId.Value.ToString("D")
          : DBNull.Value;
        if (log.ElapsedTime.Ticks >= 0)
          cmd.Parameters.Add("elapsed", SqliteType.Integer).Value = log.ElapsedTime.Ticks;
        else
          cmd.Parameters.Add("elapsed", SqliteType.Integer).Value = DBNull.Value;
        if (log.ActiveOperationTime.Ticks > 0)
          cmd.Parameters.Add("active", SqliteType.Integer).Value = log.ActiveOperationTime.Ticks;
        else
          cmd.Parameters.Add("active", SqliteType.Integer).Value = DBNull.Value;
        if (foreignID == null || foreignID == "")
          cmd.Parameters.Add("foreign", SqliteType.Text).Value = DBNull.Value;
        else
          cmd.Parameters.Add("foreign", SqliteType.Text).Value = foreignID;
        if (origMessage == null || origMessage == "")
          cmd.Parameters.Add("orig", SqliteType.Text).Value = DBNull.Value;
        else
          cmd.Parameters.Add("orig", SqliteType.Text).Value = origMessage;

        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT last_insert_rowid()";
        cmd.Parameters.Clear();
        long ctr = (long)cmd.ExecuteScalar();

        AddMaterial(ctr, log.Material, trans);
        AddProgramDetail(ctr, log.ProgramDetails, trans);
        AddToolUse(ctr, log.Tools, trans);
        AddToolSnapshots(ctr, log.ToolPockets, trans);

        foreach (var containerId in log.CycleEndContainerIds ?? [])
        {
          cmd.CommandText =
            "INSERT INTO basket_cycle_container_ids(CycleCounter, ContainerId) VALUES($counter, $containerId)";
          cmd.Parameters.Clear();
          cmd.Parameters.Add("counter", SqliteType.Integer).Value = ctr;
          cmd.Parameters.Add("containerId", SqliteType.Text).Value = containerId.ToString("D");
          cmd.ExecuteNonQuery();
        }

        return log.ToLogEntry(ctr, m => this.GetMaterialDetails(m, trans));
      }
    }

    private void EnsureContainerIdOpen(Guid containerId, IDbTransaction trans)
    {
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText =
        "SELECT CycleCounter FROM basket_cycle_container_ids WHERE ContainerId = $containerId";
      cmd.Parameters.Add("containerId", SqliteType.Text).Value = containerId.ToString("D");
      var cycleCounter = cmd.ExecuteScalar();
      if (cycleCounter is not null)
        throw new ConflictRequestException(
          $"Container UUID {containerId:D} was finalized by basket cycle {cycleCounter}."
        );
    }

    private static void ValidateLogEntry(NewEventLogEntry log)
    {
      var validIdentity = (log.Pallet, log.ContainerId, log.LogType) switch
      {
        (0, null, _) => true,
        (> 0, null, _) => true,
        (-1, Guid id, _) => id != Guid.Empty,
        (> 0, Guid id, LogType.BasketIdentityHint) => id != Guid.Empty,
        _ => false,
      };
      if (!validIdentity)
        throw new ArgumentException(
          $"Invalid container identity: container number {log.Pallet}, container ID {log.ContainerId}, log type {log.LogType}."
        );

      if (
        log.LogType == LogType.BasketIdentityHint
        && (log.Pallet <= 0 || !log.ContainerId.HasValue)
      )
        throw new ArgumentException(
          "BasketIdentityHint requires a positive basket number and a non-empty UUID."
        );
      if (log.LogType == LogType.BasketContentSnapshot && log.Pallet == 0)
        throw new ArgumentException("BasketContentSnapshot requires a container identity.");
      if (
        log.CycleEndContainerIds is { Count: > 0 } cycleEndContainerIds
        && (
          log.LogType != LogType.BasketCycle
          || log.StartOfCycle
          || log.Pallet <= 0
          || log.ContainerId.HasValue
          || cycleEndContainerIds.Any(id => id == Guid.Empty)
          || cycleEndContainerIds.Count != cycleEndContainerIds.Distinct().Count()
        )
      )
        throw new ArgumentException(
          "UUID membership is valid only on a numbered basket-cycle end and must contain unique, non-empty UUIDs."
        );
    }

    private void AddMaterial(long counter, IEnumerable<EventLogMaterial> mat, IDbTransaction trans)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText =
          "INSERT INTO stations_mat(Counter,MaterialID,Process,Face)"
          + "VALUES($cntr,$mat,$proc,$face)";
        cmd.Parameters.Add("cntr", SqliteType.Integer).Value = counter;
        cmd.Parameters.Add("mat", SqliteType.Integer);
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("face", SqliteType.Integer);

        foreach (var m in mat)
        {
          cmd.Parameters[1].Value = m.MaterialID;
          cmd.Parameters[2].Value = m.Process;
          cmd.Parameters[3].Value = m.Face;
          cmd.ExecuteNonQuery();
        }
      }
    }

    private void AddProgramDetail(
      long counter,
      IDictionary<string, string> details,
      IDbTransaction trans
    )
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText = "INSERT INTO program_details(Counter,Key,Value) VALUES($cntr,$key,$val)";
        cmd.Parameters.Add("cntr", SqliteType.Integer).Value = counter;
        cmd.Parameters.Add("key", SqliteType.Text);
        cmd.Parameters.Add("val", SqliteType.Text);

        foreach (var pair in details)
        {
          cmd.Parameters[1].Value = pair.Key;
          cmd.Parameters[2].Value = string.IsNullOrEmpty(pair.Value) ? DBNull.Value : pair.Value;
          cmd.ExecuteNonQuery();
        }
      }
    }

    private void AddToolUse(long counter, IEnumerable<ToolUse> tools, IDbTransaction trans)
    {
      if (tools == null)
        return;
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText =
          "INSERT INTO station_tool_use(Counter, Tool, Pocket, UseInCycle, UseAtEndOfCycle, ToolLife, ToolChange, SerialAtStart, SerialAtEnd, CountInCycle, CountAtEndOfCycle, LifeCount) VALUES ($cntr,$tool,$pocket,$use,$totalUse,$life,$change,$serStart,$serEnd,$countIn,$countEnd,$lifeCount)";
        cmd.Parameters.Add("cntr", SqliteType.Integer).Value = counter;
        cmd.Parameters.Add("tool", SqliteType.Text);
        cmd.Parameters.Add("pocket", SqliteType.Integer);
        cmd.Parameters.Add("use", SqliteType.Integer);
        cmd.Parameters.Add("totalUse", SqliteType.Integer);
        cmd.Parameters.Add("life", SqliteType.Integer);
        cmd.Parameters.Add("change", SqliteType.Integer);
        cmd.Parameters.Add("serStart", SqliteType.Text);
        cmd.Parameters.Add("serEnd", SqliteType.Text);
        cmd.Parameters.Add("countIn", SqliteType.Integer);
        cmd.Parameters.Add("countEnd", SqliteType.Integer);
        cmd.Parameters.Add("lifeCount", SqliteType.Integer);

        foreach (var tool in tools)
        {
          cmd.Parameters[1].Value = tool.Tool;
          cmd.Parameters[2].Value = tool.Pocket;
          cmd.Parameters[3].Value = tool.ToolUseDuringCycle.HasValue
            ? tool.ToolUseDuringCycle.Value.Ticks
            : DBNull.Value;
          cmd.Parameters[4].Value = tool.TotalToolUseAtEndOfCycle.HasValue
            ? tool.TotalToolUseAtEndOfCycle.Value.Ticks
            : DBNull.Value;
          cmd.Parameters[5].Value = tool.ConfiguredToolLife.HasValue
            ? tool.ConfiguredToolLife.Value.Ticks
            : DBNull.Value;
          cmd.Parameters[6].Value = tool.ToolChangeOccurred.HasValue
            ? tool.ToolChangeOccurred.Value
            : DBNull.Value;
          cmd.Parameters[7].Value =
            tool.ToolSerialAtStartOfCycle == null
              ? DBNull.Value
              : (object)tool.ToolSerialAtStartOfCycle;
          cmd.Parameters[8].Value =
            tool.ToolSerialAtEndOfCycle == null
              ? DBNull.Value
              : (object)tool.ToolSerialAtEndOfCycle;
          cmd.Parameters[9].Value = tool.ToolUseCountDuringCycle.HasValue
            ? tool.ToolUseCountDuringCycle.Value
            : DBNull.Value;
          cmd.Parameters[10].Value = tool.TotalToolUseCountAtEndOfCycle.HasValue
            ? tool.TotalToolUseCountAtEndOfCycle.Value
            : DBNull.Value;
          cmd.Parameters[11].Value = tool.ConfiguredToolLifeCount.HasValue
            ? tool.ConfiguredToolLifeCount.Value
            : DBNull.Value;
          cmd.ExecuteNonQuery();
        }
      }
    }

    private void AddToolSnapshots(
      long counter,
      IEnumerable<ToolSnapshot> pockets,
      IDbTransaction trans
    )
    {
      if (pockets == null || !pockets.Any())
        return;

      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText =
          "INSERT OR REPLACE INTO tool_snapshots(Counter, PocketNumber, Tool, CurrentUse, ToolLife, CurrentCount, LifeCount, Serial) VALUES ($cntr,$pocket,$tool,$use,$life,$useCnt,$lifeCnt,$ser)";
        cmd.Parameters.Add("cntr", SqliteType.Integer).Value = counter;
        cmd.Parameters.Add("pocket", SqliteType.Integer);
        cmd.Parameters.Add("tool", SqliteType.Text);
        cmd.Parameters.Add("use", SqliteType.Integer);
        cmd.Parameters.Add("life", SqliteType.Integer);
        cmd.Parameters.Add("useCnt", SqliteType.Integer);
        cmd.Parameters.Add("lifeCnt", SqliteType.Integer);
        cmd.Parameters.Add("ser", SqliteType.Text);

        foreach (var pocket in pockets)
        {
          cmd.Parameters[1].Value = pocket.Pocket;
          cmd.Parameters[2].Value = pocket.ToolName;
          cmd.Parameters[3].Value = pocket.CurrentUse.HasValue
            ? pocket.CurrentUse.Value.Ticks
            : DBNull.Value;
          cmd.Parameters[4].Value = pocket.TotalLifeTime.HasValue
            ? pocket.TotalLifeTime.Value.Ticks
            : DBNull.Value;
          cmd.Parameters[5].Value = pocket.CurrentUseCount.HasValue
            ? pocket.CurrentUseCount.Value
            : DBNull.Value;
          cmd.Parameters[6].Value = pocket.TotalLifeCount.HasValue
            ? pocket.TotalLifeCount.Value
            : DBNull.Value;
          cmd.Parameters[7].Value = pocket.Serial == null ? DBNull.Value : (object)pocket.Serial;
          cmd.ExecuteNonQuery();
        }
      }
    }

    private LogEntry AddEntryInTransaction(Func<IDbTransaction, LogEntry> f, string foreignId = "")
    {
      LogEntry log;
      lock (_cfg)
      {
        using (var trans = _connection.BeginTransaction())
        {
          log = f(trans);
          trans.Commit();
        }
      }
      _cfg.OnNewLogEntry(log, foreignId, this);
      return log;
    }

    private IEnumerable<LogEntry> AddEntryInTransaction(
      Func<IDbTransaction, IReadOnlyList<LogEntry>> f,
      string foreignId = ""
    )
    {
      IEnumerable<LogEntry> logs;
      lock (_cfg)
      {
        using (var trans = _connection.BeginTransaction())
        {
          logs = f(trans).ToList();
          trans.Commit();
        }
      }
      foreach (var l in logs)
        _cfg.OnNewLogEntry(l, foreignId, this);
      return logs;
    }

    public LogEntry RecordLoadStart(
      IEnumerable<EventLogMaterial> mats,
      int pallet,
      int lulNum,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    )
    {
      var log = new NewEventLogEntry()
      {
        Material = mats,
        Pallet = pallet,
        LogType = LogType.LoadUnloadCycle,
        LocationName = "L/U",
        LocationNum = lulNum,
        Program = "LOAD",
        StartOfCycle = true,
        EndTimeUTC = timeUTC,
        Result = "LOAD",
      };
      return AddEntryInTransaction(trans => AddLogEntry(trans, log, foreignId, originalMessage));
    }

    public LogEntry RecordUnloadStart(
      IEnumerable<EventLogMaterial> mats,
      int pallet,
      int lulNum,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    )
    {
      var log = new NewEventLogEntry()
      {
        Material = mats,
        Pallet = pallet,
        LogType = LogType.LoadUnloadCycle,
        LocationName = "L/U",
        LocationNum = lulNum,
        Program = "UNLOAD",
        StartOfCycle = true,
        EndTimeUTC = timeUTC,
        Result = "UNLOAD",
      };
      return AddEntryInTransaction(trans => AddLogEntry(trans, log, foreignId, originalMessage));
    }

    public IEnumerable<LogEntry> RecordPartialLoadUnload(
      IReadOnlyList<MaterialToLoadOntoFace> toLoad,
      IReadOnlyList<MaterialToUnloadFromFace> toUnload,
      int lulNum,
      int pallet,
      TimeSpan totalElapsed,
      DateTime timeUTC,
      IReadOnlyDictionary<string, string> externalQueues,
      PalletBasketLoadUnloadCompletion palletBasketCompletion = null
    )
    {
      ValidatePalletBasketCompletion(palletBasketCompletion, toLoad, toUnload);
      var sendToExternal = new List<MaterialToSendToExternalQueue>();

      var newLogs = AddEntryInTransaction(trans =>
      {
        var logs = new List<LogEntry>();

        // calculate total active time and total material count to be able
        // to split the totalElpased up
        var totMatCnt =
          (toLoad?.Sum(l => l.MaterialIDs.Count) ?? 0)
          + (toUnload?.Sum(l => l.MaterialIDToDestination.Count) ?? 0);

        bool allHaveActive = true;
        TimeSpan totalActive = TimeSpan.Zero;
        foreach (var l in toLoad ?? [])
        {
          if (l.ActiveOperationTime > TimeSpan.Zero)
          {
            totalActive += l.ActiveOperationTime;
          }
          else
          {
            allHaveActive = false;
          }
        }
        foreach (var u in toUnload ?? [])
        {
          if (u.ActiveOperationTime > TimeSpan.Zero)
          {
            totalActive += u.ActiveOperationTime;
          }
          else
          {
            allHaveActive = false;
          }
        }

        RecordUnloadEnd(
          toUnload: toUnload,
          lulNum: lulNum,
          pallet: pallet,
          totalElapsed: totalElapsed,
          totMatCnt: totMatCnt,
          totalActive: allHaveActive && totalActive > TimeSpan.Zero ? totalActive : null,
          timeUTC: timeUTC,
          logs: logs,
          externalQueues: externalQueues,
          sendToExternal: sendToExternal,
          trans: trans
        );

        RecordPalletBasketTransferEnds<PalletBasketTransfer.UnloadFromBasket>(
          palletBasketCompletion,
          lulNum,
          timeUTC,
          trans,
          logs
        );
        RecordExplicitBasketCycleEnds(palletBasketCompletion, lulNum, timeUTC, logs, trans);

        RecordLoadMaterialPaths(toLoad: toLoad, trans: trans);

        RecordLoadEnd(
          toLoad: toLoad,
          lulNum: lulNum,
          pallet: pallet,
          totalElapsed: totalElapsed,
          totMatCnt: totMatCnt,
          totalActive: allHaveActive && totalActive > TimeSpan.Zero ? totalActive : null,
          timeUTC: timeUTC,
          logs: logs,
          trans: trans,
          materialFromBaskets: PalletBasketMaterialIds<PalletBasketTransfer.UnloadFromBasket>(
            palletBasketCompletion
          )
        );

        RecordPalletBasketTransferEnds<PalletBasketTransfer.LoadOntoBasket>(
          palletBasketCompletion,
          lulNum,
          timeUTC,
          trans,
          logs
        );
        RecordExplicitBasketCycleStarts(palletBasketCompletion, lulNum, timeUTC, logs, trans);

        return logs;
      });

      if (sendToExternal.Count > 0)
      {
        System.Threading.Tasks.Task.Run(() => SendMaterialToExternalQueue.Post(sendToExternal));
      }

      return newLogs;
    }

    private static void ValidatePalletBasketCompletion(
      PalletBasketLoadUnloadCompletion palletBasketCompletion,
      IReadOnlyList<MaterialToLoadOntoFace> toLoad,
      IReadOnlyList<MaterialToUnloadFromFace> toUnload
    )
    {
      if (palletBasketCompletion is null)
      {
        if (
          toUnload?.Any(unload =>
            unload.MaterialIDToDestination.Values.Any(destination =>
              destination is not null && destination.Queue is null
            )
          ) == true
        )
          throw new ArgumentException(
            "Pallet unloads without a queue destination require an explicit basket completion.",
            nameof(palletBasketCompletion)
          );
        return;
      }
      if (
        palletBasketCompletion.Transfers.Count == 0
        && palletBasketCompletion.CycleBoundaries.Count == 0
      )
        throw new ArgumentException(
          "A basket completion requires a transfer or cycle boundary.",
          nameof(palletBasketCompletion)
        );

      var transferredToBasket = ImmutableHashSet.CreateBuilder<long>();
      var transferredFromBasket = ImmutableHashSet.CreateBuilder<long>();
      var transferredToBasketWithProcess = ImmutableHashSet.CreateBuilder<(long, int)>();
      var transferredFromBasketWithProcess = ImmutableHashSet.CreateBuilder<(long, int)>();
      foreach (var transfer in palletBasketCompletion.Transfers)
      {
        RecordedContainerIdentity(transfer.BasketIdentity);
        if (transfer.Material.Count == 0)
          throw new ArgumentException(
            "A basket transfer requires material.",
            nameof(palletBasketCompletion)
          );
        var target =
          transfer is PalletBasketTransfer.LoadOntoBasket
            ? transferredToBasket
            : transferredFromBasket;
        var targetWithProcess =
          transfer is PalletBasketTransfer.LoadOntoBasket
            ? transferredToBasketWithProcess
            : transferredFromBasketWithProcess;
        foreach (var material in transfer.Material)
        {
          if (!target.Add(material.MaterialID))
            throw new ArgumentException(
              $"Material {material.MaterialID} is repeated in basket transfers.",
              nameof(palletBasketCompletion)
            );
          targetWithProcess.Add((material.MaterialID, material.Process));
        }
      }

      var starts = new HashSet<ContainerIdentity>();
      var ends = new HashSet<ContainerIdentity>();
      foreach (var boundary in palletBasketCompletion.CycleBoundaries)
      {
        RecordedContainerIdentity(boundary.BasketIdentity);
        if (
          boundary.Material.Select(material => material.MaterialID).Distinct().Count()
          != boundary.Material.Count
        )
          throw new ArgumentException(
            "Basket cycle material must contain each material exactly once.",
            nameof(palletBasketCompletion)
          );
        if (boundary is BasketCycleBoundary.Start start)
        {
          if (boundary.Material.Count == 0 || !starts.Add(boundary.BasketIdentity))
            throw new ArgumentException(
              "Each nonempty basket boundary requires one cycle start.",
              nameof(palletBasketCompletion)
            );
          if (
            start.AssociatedBasketNum is { } basketNum
            && (basketNum <= 0 || start.BasketIdentity is not ContainerIdentity.Uuid)
          )
            throw new ArgumentException(
              "A basket-cycle start association requires a positive basket number and a UUID identity.",
              nameof(palletBasketCompletion)
            );
        }
        else if (boundary is BasketCycleBoundary.End end)
        {
          if (!ends.Add(boundary.BasketIdentity))
            throw new ArgumentException(
              "Each completed basket can have only one cycle end.",
              nameof(palletBasketCompletion)
            );
          if (
            boundary.BasketIdentity is not ContainerIdentity.Numbered
            || end.ReconciledBasketIdentities.Any(id => id == Guid.Empty)
          )
            throw new ArgumentException(
              "A basket cycle end requires a numbered basket identity and may contain only non-empty fragment UUIDs.",
              nameof(palletBasketCompletion)
            );
        }
      }

      foreach (var transfer in palletBasketCompletion.Transfers)
      {
        var matchingBoundary = palletBasketCompletion.CycleBoundaries.FirstOrDefault(boundary =>
          boundary.BasketIdentity == transfer.BasketIdentity
          && (
            (
              transfer is PalletBasketTransfer.LoadOntoBasket
              && boundary is BasketCycleBoundary.Start
            )
            || (
              transfer is PalletBasketTransfer.UnloadFromBasket
              && boundary is BasketCycleBoundary.End
            )
          )
        );
        if (
          matchingBoundary is not null
          && transfer.Material.Any(material =>
            !matchingBoundary.Material.Any(cycleMaterial =>
              cycleMaterial.MaterialID == material.MaterialID
              && cycleMaterial.Process == material.Process
            )
          )
        )
          throw new ArgumentException(
            "Transferred material must be present in the corresponding complete cycle material.",
            nameof(palletBasketCompletion)
          );

        if (
          transfer
            is PalletBasketTransfer.UnloadFromBasket
            {
              BasketIdentity: ContainerIdentity.Uuid { ContainerId: var containerId },
            }
          && palletBasketCompletion
            .CycleBoundaries.OfType<BasketCycleBoundary.End>()
            .FirstOrDefault(end => end.ReconciledBasketIdentities.Contains(containerId))
            is { } fragmentEnd
          && transfer.Material.Any(material =>
            !fragmentEnd.Material.Any(cycleMaterial =>
              cycleMaterial.MaterialID == material.MaterialID
              && cycleMaterial.Process == material.Process
            )
          )
        )
          throw new ArgumentException(
            "Material unloaded from a finalized UUID fragment must be present in the complete cycle end.",
            nameof(palletBasketCompletion)
          );
      }

      foreach (
        var start in palletBasketCompletion.CycleBoundaries.OfType<BasketCycleBoundary.Start>()
      )
      {
        var end = palletBasketCompletion
          .CycleBoundaries.OfType<BasketCycleBoundary.End>()
          .FirstOrDefault(boundary => boundary.BasketIdentity == start.BasketIdentity);
        if (end is null)
          continue;

        var unloaded = palletBasketCompletion
          .Transfers.OfType<PalletBasketTransfer.UnloadFromBasket>()
          .Where(transfer => transfer.BasketIdentity == start.BasketIdentity)
          .SelectMany(transfer =>
            transfer.Material.Select(material => (material.MaterialID, material.Process))
          )
          .ToImmutableHashSet();
        var loaded = palletBasketCompletion
          .Transfers.OfType<PalletBasketTransfer.LoadOntoBasket>()
          .Where(transfer => transfer.BasketIdentity == start.BasketIdentity)
          .SelectMany(transfer =>
            transfer.Material.Select(material => (material.MaterialID, material.Process))
          )
          .ToImmutableHashSet();
        var expectedStartMaterial = end
          .Material.Select(material => (material.MaterialID, material.Process))
          .ToImmutableHashSet()
          .Except(unloaded)
          .Union(loaded);
        var actualStartMaterial = start
          .Material.Select(material => (material.MaterialID, material.Process))
          .ToImmutableHashSet();
        if (!expectedStartMaterial.SetEquals(actualStartMaterial))
          throw new ArgumentException(
            "A basket cycle start after turnover must equal the completed cycle material with unloads removed and loads added.",
            nameof(palletBasketCompletion)
          );
      }

      if (toLoad is not null)
      {
        var palletLoadMaterial = toLoad
          .SelectMany(load => load.MaterialIDs.Select(id => (MaterialID: id, load.Process)))
          .ToImmutableHashSet();
        if (
          transferredFromBasketWithProcess.Any(material => !palletLoadMaterial.Contains(material))
        )
          throw new ArgumentException(
            "Basket-to-pallet material and process must match the pallet load.",
            nameof(palletBasketCompletion)
          );
      }
      if (toUnload is not null)
      {
        var basketDestinationMaterial = toUnload
          .SelectMany(unload =>
            unload
              .MaterialIDToDestination.Where(destination =>
                destination.Value is not null && destination.Value.Queue is null
              )
              .Select(destination => (MaterialID: destination.Key, unload.Process))
          )
          .ToImmutableHashSet();
        if (!transferredToBasketWithProcess.SetEquals(basketDestinationMaterial))
          throw new ArgumentException(
            "Pallet unloads without a queue destination must exactly match pallet-to-basket material and process.",
            nameof(palletBasketCompletion)
          );
      }
    }

    private static ImmutableHashSet<long> PalletBasketMaterialIds<TTransfer>(
      PalletBasketLoadUnloadCompletion palletBasketCompletion
    )
      where TTransfer : PalletBasketTransfer =>
      palletBasketCompletion
        ?.Transfers.OfType<TTransfer>()
        .SelectMany(transfer => transfer.Material)
        .Select(material => material.MaterialID)
        .ToImmutableHashSet()
      ?? [];

    private void RecordPalletBasketTransferEnds<TTransfer>(
      PalletBasketLoadUnloadCompletion palletBasketCompletion,
      int lulNum,
      DateTime timeUTC,
      IDbTransaction trans,
      List<LogEntry> logs,
      string foreignId = null,
      string originalMessage = null
    )
      where TTransfer : PalletBasketTransfer
    {
      foreach (var transfer in palletBasketCompletion?.Transfers.OfType<TTransfer>() ?? [])
      {
        var (containerNum, containerId) = RecordedContainerIdentity(transfer.BasketIdentity);
        var loadOntoBasket = transfer is PalletBasketTransfer.LoadOntoBasket;
        logs.Add(
          AddLogEntry(
            trans,
            new NewEventLogEntry
            {
              Material = transfer.Material,
              Pallet = containerNum,
              ContainerId = containerId,
              LogType = LogType.BasketLoadUnload,
              LocationName = "L/U",
              LocationNum = lulNum,
              Program = loadOntoBasket ? "LOAD" : "UNLOAD",
              StartOfCycle = false,
              EndTimeUTC = timeUTC,
              Result = loadOntoBasket ? "LOAD" : "UNLOAD",
              ElapsedTime = TimeSpan.Zero,
              ActiveOperationTime = TimeSpan.Zero,
            },
            foreignId,
            originalMessage
          )
        );
      }
    }

    private void RecordExplicitBasketCycleEnds(
      PalletBasketLoadUnloadCompletion palletBasketCompletion,
      int lulNum,
      DateTime timeUTC,
      List<LogEntry> logs,
      IDbTransaction trans,
      string foreignId = null,
      string originalMessage = null
    )
    {
      foreach (
        var boundary in palletBasketCompletion?.CycleBoundaries.OfType<BasketCycleBoundary.End>()
          ?? []
      )
      {
        var (containerNum, containerId) = RecordedContainerIdentity(boundary.BasketIdentity);
        var containerIds = boundary.ReconciledBasketIdentities.OrderBy(id => id).ToImmutableList();
        var firstEventTime = timeUTC;
        foreach (var id in containerIds)
        {
          var eventTime = EnsureOpenBasketFragment(id, trans);
          if (eventTime < firstEventTime)
            firstEventTime = eventTime;
        }
        if (containerIds.Count == 0)
        {
          var recentCycle = MostRecentBasketCycleEvent(boundary.BasketIdentity, trans);
          if (recentCycle is { StartOfCycle: true, Invalidated: false })
          {
            firstEventTime = recentCycle.TimeUTC;
          }
          else if (!boundary.Material.IsEmpty)
          {
            throw new ConflictRequestException(
              "A nonempty numbered basket-cycle end requires a non-invalidated open cycle."
            );
          }
        }

        var cycleEnd = new NewEventLogEntry
        {
          Material = boundary.Material,
          Pallet = containerNum,
          ContainerId = containerId,
          LogType = LogType.BasketCycle,
          LocationName = "Basket Cycle",
          LocationNum = lulNum,
          Program = "",
          StartOfCycle = false,
          EndTimeUTC = timeUTC,
          Result = "BasketCycle",
          ElapsedTime = timeUTC >= firstEventTime ? timeUTC - firstEventTime : TimeSpan.Zero,
          ActiveOperationTime = TimeSpan.Zero,
          CycleEndContainerIds = containerIds.Count == 0 ? null : containerIds,
        };
        logs.Add(AddLogEntry(trans, cycleEnd, foreignId, originalMessage));

        if (containerIds.Count > 0)
        {
          using var removeHints = _connection.CreateCommand();
          ((IDbCommand)removeHints).Transaction = trans;
          removeHints.CommandText =
            "DELETE FROM current_basket_identity_hints WHERE ContainerId = $id";
          removeHints.Parameters.Add("id", SqliteType.Text);
          foreach (var id in containerIds)
          {
            removeHints.Parameters[0].Value = id.ToString("D");
            removeHints.ExecuteNonQuery();
          }
        }
      }
    }

    private sealed record BasketCycleEventState
    {
      public required DateTime TimeUTC { get; init; }
      public required bool StartOfCycle { get; init; }
      public required bool Invalidated { get; init; }
    }

    private BasketCycleEventState MostRecentBasketCycleEvent(
      ContainerIdentity basketIdentity,
      IDbTransaction trans
    )
    {
      var (containerNum, containerId) = RecordedContainerIdentity(basketIdentity);
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText =
        "SELECT TimeUTC, Start, EXISTS("
        + "SELECT 1 FROM program_details d WHERE d.Counter = s.Counter AND d.Key = 'PalletCycleInvalidated'"
        + ") FROM stations s WHERE Pallet = $num "
        + "AND (($id IS NULL AND ContainerId IS NULL) OR ContainerId = $id) "
        + "AND StationLoc = $cycleType ORDER BY Counter DESC LIMIT 1";
      cmd.Parameters.Add("num", SqliteType.Integer).Value = containerNum;
      cmd.Parameters.Add("id", SqliteType.Text).Value = containerId is { } id
        ? id.ToString("D")
        : DBNull.Value;
      cmd.Parameters.Add("cycleType", SqliteType.Integer).Value = (int)LogType.BasketCycle;
      using var reader = cmd.ExecuteReader();
      if (!reader.Read())
        return null;
      return new BasketCycleEventState
      {
        TimeUTC = new DateTime(reader.GetInt64(0), DateTimeKind.Utc),
        StartOfCycle = reader.GetBoolean(1),
        Invalidated = reader.GetBoolean(2),
      };
    }

    private void RecordExplicitBasketCycleStarts(
      PalletBasketLoadUnloadCompletion palletBasketCompletion,
      int lulNum,
      DateTime timeUTC,
      List<LogEntry> logs,
      IDbTransaction trans,
      string foreignId = null,
      string originalMessage = null
    )
    {
      foreach (
        var boundary in palletBasketCompletion?.CycleBoundaries.OfType<BasketCycleBoundary.Start>()
          ?? []
      )
      {
        var (containerNum, containerId) = RecordedContainerIdentity(boundary.BasketIdentity);
        if (
          MostRecentBasketCycleEvent(boundary.BasketIdentity, trans) is
          { StartOfCycle: true, Invalidated: false }
        )
          throw new ConflictRequestException(
            "A basket-cycle start can not replace an already-open cycle."
          );
        var cycleStart = new NewEventLogEntry
        {
          Material = boundary.Material,
          Pallet = containerNum,
          ContainerId = containerId,
          LogType = LogType.BasketCycle,
          LocationName = "Basket Cycle",
          LocationNum = lulNum,
          Program = "",
          StartOfCycle = true,
          EndTimeUTC = timeUTC,
          Result = "BasketCycle",
          ElapsedTime = TimeSpan.Zero,
          ActiveOperationTime = TimeSpan.Zero,
        };
        logs.Add(AddLogEntry(trans, cycleStart, foreignId, originalMessage));
        if (boundary.AssociatedBasketNum is { } basketNum)
          logs.Add(
            AddBasketIdentityHint(
              containerId!.Value,
              basketNum,
              timeUTC,
              trans,
              foreignId,
              originalMessage
            )
          );
      }
    }

    private ImmutableList<LogEntry> BasketStationOperationForIdempotencyKey(
      string idempotencyKey,
      IDbTransaction trans
    )
    {
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText =
        "SELECT s.Counter, s.Pallet, s.StationLoc, s.StationNum, s.Program, s.Start, s.TimeUTC, s.Result, s.EndOfRoute, s.Elapsed, s.ActiveTime, s.StationName, s.ContainerId "
        + "FROM basket_station_operation_events o "
        + "JOIN stations s ON s.Counter = o.Counter "
        + "WHERE o.IdempotencyKey = $key ORDER BY o.Position";
      cmd.Parameters.Add("key", SqliteType.Text).Value = idempotencyKey;
      using var reader = cmd.ExecuteReader();
      return LoadLog(reader, trans).ToImmutableList();
    }

    // RecordLoadUnloadComplete is used ONLY for load/unload operations involving a pallet.
    // This includes:
    //   - Pallet to/from queue (material loaded from queue onto pallet, or unloaded from pallet to queue)
    //   - Pallet to/from basket (material transferred between pallet face and basket)
    // Basket events are never inferred from event history. Callers that know basket state
    // synchronously provide an explicit palletBasketCompletion. Delayed evidence and basket-station
    // queue work are recorded with RecordBasketStationOperation.
    public IEnumerable<LogEntry> RecordLoadUnloadComplete(
      IReadOnlyList<MaterialToLoadOntoFace> toLoad,
      IReadOnlyList<EventLogMaterial> previouslyLoaded,
      IReadOnlyList<MaterialToUnloadFromFace> toUnload,
      IReadOnlyList<EventLogMaterial> previouslyUnloaded,
      int lulNum,
      int pallet,
      TimeSpan totalElapsed,
      DateTime timeUTC,
      IReadOnlyDictionary<string, string> externalQueues,
      PalletBasketLoadUnloadCompletion palletBasketCompletion = null
    )
    {
      ValidatePalletBasketCompletion(palletBasketCompletion, toLoad, toUnload);
      var sendToExternal = new List<MaterialToSendToExternalQueue>();

      var newLogs = AddEntryInTransaction(trans =>
      {
        var logs = new List<LogEntry>();

        // calculate total active time and total material count to be able
        // to split the totalElpased up
        var totMatCnt =
          (toLoad?.Sum(l => l.MaterialIDs.Count) ?? 0)
          + (toUnload?.Sum(l => l.MaterialIDToDestination.Count) ?? 0);

        bool allHaveActive = true;
        TimeSpan totalActive = TimeSpan.Zero;
        foreach (var l in toLoad ?? [])
        {
          if (l.ActiveOperationTime > TimeSpan.Zero)
          {
            totalActive += l.ActiveOperationTime;
          }
          else
          {
            allHaveActive = false;
          }
        }
        foreach (var u in toUnload ?? [])
        {
          if (u.ActiveOperationTime > TimeSpan.Zero)
          {
            totalActive += u.ActiveOperationTime;
          }
          else
          {
            allHaveActive = false;
          }
        }

        RecordUnloadEnd(
          toUnload: toUnload,
          lulNum: lulNum,
          pallet: pallet,
          totalElapsed: totalElapsed,
          totMatCnt: totMatCnt,
          totalActive: allHaveActive && totalActive > TimeSpan.Zero ? totalActive : null,
          timeUTC: timeUTC,
          logs: logs,
          externalQueues: externalQueues,
          sendToExternal: sendToExternal,
          trans: trans
        );

        RecordPalletBasketTransferEnds<PalletBasketTransfer.UnloadFromBasket>(
          palletBasketCompletion,
          lulNum,
          timeUTC,
          trans,
          logs
        );

        var allUnload = (previouslyUnloaded ?? []).Concat(
          (toUnload ?? []).SelectMany(u =>
            u.MaterialIDToDestination.Keys.Select(mid => new EventLogMaterial()
            {
              MaterialID = mid,
              Process = u.Process,
              Face = u.FaceNum,
            })
          )
        );
        if (allUnload.Any())
        {
          RecordPalletCycleEnd(
            pallet: pallet,
            mats: allUnload,
            timeUTC: timeUTC,
            logs: logs,
            trans: trans
          );
        }

        RecordExplicitBasketCycleEnds(palletBasketCompletion, lulNum, timeUTC, logs, trans);

        RecordLoadMaterialPaths(toLoad: toLoad, trans: trans);

        var allLoad = (previouslyLoaded ?? []).Concat(
          (toLoad ?? []).SelectMany(l =>
            l.MaterialIDs.Select(mid => new EventLogMaterial()
            {
              MaterialID = mid,
              Process = l.Process,
              Face = l.FaceNum,
            })
          )
        );
        if (allLoad.Any())
        {
          RecordPalletCycleStart(
            pallet: pallet,
            mats: allLoad,
            timeUTC: timeUTC,
            logs: logs,
            trans: trans,
            foreignId: null
          );
        }

        RecordLoadEnd(
          toLoad: toLoad,
          lulNum: lulNum,
          pallet: pallet,
          totalElapsed: totalElapsed,
          totMatCnt: totMatCnt,
          totalActive: allHaveActive && totalActive > TimeSpan.Zero ? totalActive : null,
          timeUTC: timeUTC,
          logs: logs,
          trans: trans,
          materialFromBaskets: PalletBasketMaterialIds<PalletBasketTransfer.UnloadFromBasket>(
            palletBasketCompletion
          )
        );

        RecordPalletBasketTransferEnds<PalletBasketTransfer.LoadOntoBasket>(
          palletBasketCompletion,
          lulNum,
          timeUTC,
          trans,
          logs
        );
        RecordExplicitBasketCycleStarts(palletBasketCompletion, lulNum, timeUTC, logs, trans);

        return logs;
      });

      if (sendToExternal.Count > 0)
      {
        System.Threading.Tasks.Task.Run(() => SendMaterialToExternalQueue.Post(sendToExternal));
      }

      return newLogs;
    }

    public IEnumerable<LogEntry> RecordBasketStationOperation(
      BasketStationOperation operation,
      int lulNum,
      TimeSpan totalElapsed,
      DateTime timeUTC,
      IReadOnlyDictionary<string, string> externalQueues,
      string idempotencyKey,
      string foreignId = null,
      string originalMessage = null
    )
    {
      ArgumentNullException.ThrowIfNull(operation);
      if (string.IsNullOrWhiteSpace(idempotencyKey))
        throw new ArgumentException(
          "A basket-station operation requires an idempotency key.",
          nameof(idempotencyKey)
        );
      if (totalElapsed < TimeSpan.Zero)
        throw new ArgumentOutOfRangeException(nameof(totalElapsed));
      foreignId = string.IsNullOrEmpty(foreignId) ? null : foreignId;

      var palletCompletion = ToPalletBasketLoadUnloadCompletion(operation);
      ValidatePalletBasketCompletion(palletCompletion, toLoad: null, toUnload: null);
      foreach (var transfer in operation.Transfers)
      {
        if (transfer.ActiveOperationTime < TimeSpan.Zero)
          throw new ArgumentOutOfRangeException(
            nameof(operation),
            "Basket-station active operation time cannot be negative."
          );
        if (
          transfer
            is BasketStationTransfer.UnloadFromBasket { DestinationQueue: { } destinationQueue }
          && string.IsNullOrWhiteSpace(destinationQueue)
        )
          throw new ArgumentException(
            "A basket-station queue must be null or non-empty.",
            nameof(operation)
          );
      }

      var fingerprint = BasketStationFingerprint(operation, lulNum, totalElapsed, externalQueues);
      var sendToExternal = new List<MaterialToSendToExternalQueue>();
      ImmutableList<LogEntry> logs;
      var added = false;
      lock (_cfg)
      {
        using var trans = _connection.BeginTransaction();
        using var existingCommand = _connection.CreateCommand();
        existingCommand.Transaction = trans;
        existingCommand.CommandText =
          "SELECT Fingerprint, ForeignID, OriginalMessage "
          + "FROM basket_station_operations WHERE IdempotencyKey = $key";
        existingCommand.Parameters.Add("key", SqliteType.Text).Value = idempotencyKey;
        string existingFingerprint = null;
        string existingForeignId = null;
        string existingOriginalMessage = null;
        using (var reader = existingCommand.ExecuteReader())
        {
          if (reader.Read())
          {
            existingFingerprint = reader.GetString(0);
            existingForeignId = reader.IsDBNull(1) ? null : reader.GetString(1);
            existingOriginalMessage = reader.GetString(2);
          }
        }
        if (existingFingerprint is not null)
        {
          if (
            existingFingerprint != fingerprint
            || existingForeignId != foreignId
            || existingOriginalMessage != (originalMessage ?? "")
          )
            throw new ConflictRequestException(
              $"Idempotency key {idempotencyKey} already identifies a different basket-station operation."
            );
          logs = BasketStationOperationForIdempotencyKey(idempotencyKey, trans);
          trans.Commit();
          return logs;
        }

        var newLogs = new List<LogEntry>();
        var transferMaterialCount = operation.Transfers.Sum(transfer => transfer.Material.Count);
        var activeTimes = operation
          .Transfers.Select(transfer => transfer.ActiveOperationTime)
          .ToImmutableList();
        var totalActive = activeTimes.All(active => active > TimeSpan.Zero)
          ? TimeSpan.FromTicks(activeTimes.Sum(active => active.Ticks))
          : (TimeSpan?)null;

        foreach (
          var transfer in operation.Transfers.OfType<BasketStationTransfer.UnloadFromBasket>()
        )
        {
          RecordBasketStationTransfer(
            transfer,
            loadOntoBasket: false,
            BasketStationTransferElapsed(
              transfer,
              totalElapsed,
              transferMaterialCount,
              totalActive
            ),
            lulNum,
            timeUTC,
            trans,
            newLogs,
            foreignId,
            originalMessage
          );
          if (transfer.DestinationQueue is null)
            continue;
          foreach (var material in transfer.Material)
          {
            if (
              externalQueues is not null
              && externalQueues.TryGetValue(transfer.DestinationQueue, out var externalServer)
            )
            {
              var details = GetMaterialDetails(material.MaterialID, trans);
              sendToExternal.Add(
                new MaterialToSendToExternalQueue
                {
                  Server = externalServer,
                  PartName = details?.PartName ?? "",
                  Queue = transfer.DestinationQueue,
                  Serial = details?.Serial ?? "",
                }
              );
            }
            else
            {
              AddToQueue(
                trans,
                material,
                transfer.DestinationQueue,
                position: -1,
                operatorName: null,
                timeUTC,
                reason: null
              );
            }
          }
        }

        RecordExplicitBasketCycleEnds(
          palletCompletion,
          lulNum,
          timeUTC,
          newLogs,
          trans,
          foreignId,
          originalMessage
        );

        foreach (var transfer in operation.Transfers.OfType<BasketStationTransfer.LoadOntoBasket>())
        {
          foreach (var material in transfer.Material)
            RemoveFromAllQueues(trans, material, operatorName: null, reason: null, timeUTC);
          RecordBasketStationTransfer(
            transfer,
            loadOntoBasket: true,
            BasketStationTransferElapsed(
              transfer,
              totalElapsed,
              transferMaterialCount,
              totalActive
            ),
            lulNum,
            timeUTC,
            trans,
            newLogs,
            foreignId,
            originalMessage
          );
        }

        RecordExplicitBasketCycleStarts(
          palletCompletion,
          lulNum,
          timeUTC,
          newLogs,
          trans,
          foreignId,
          originalMessage
        );

        using var recordOperation = _connection.CreateCommand();
        recordOperation.Transaction = trans;
        recordOperation.CommandText =
          "INSERT INTO basket_station_operations"
          + "(IdempotencyKey, Fingerprint, ForeignID, OriginalMessage) "
          + "VALUES($key, $fingerprint, $foreign, $original)";
        recordOperation.Parameters.Add("key", SqliteType.Text).Value = idempotencyKey;
        recordOperation.Parameters.Add("fingerprint", SqliteType.Text).Value = fingerprint;
        recordOperation.Parameters.Add("foreign", SqliteType.Text).Value = string.IsNullOrEmpty(
          foreignId
        )
          ? DBNull.Value
          : foreignId;
        recordOperation.Parameters.Add("original", SqliteType.Text).Value = originalMessage ?? "";
        recordOperation.ExecuteNonQuery();
        recordOperation.CommandText =
          "INSERT INTO basket_station_operation_events(IdempotencyKey, Position, Counter) "
          + "VALUES($key, $position, $counter)";
        recordOperation.Parameters.Clear();
        recordOperation.Parameters.Add("key", SqliteType.Text).Value = idempotencyKey;
        recordOperation.Parameters.Add("position", SqliteType.Integer);
        recordOperation.Parameters.Add("counter", SqliteType.Integer);
        for (var position = 0; position < newLogs.Count; ++position)
        {
          recordOperation.Parameters[1].Value = position;
          recordOperation.Parameters[2].Value = newLogs[position].Counter;
          recordOperation.ExecuteNonQuery();
        }
        trans.Commit();
        logs = newLogs.ToImmutableList();
        added = true;
      }

      if (added)
      {
        foreach (var log in logs)
          _cfg.OnNewLogEntry(log, foreignId, this);
        if (sendToExternal.Count > 0)
          System.Threading.Tasks.Task.Run(() => SendMaterialToExternalQueue.Post(sendToExternal));
      }
      return logs;
    }

    private static PalletBasketLoadUnloadCompletion ToPalletBasketLoadUnloadCompletion(
      BasketStationOperation operation
    ) =>
      new()
      {
        Transfers = operation
          .Transfers.Select<BasketStationTransfer, PalletBasketTransfer>(transfer =>
            transfer switch
            {
              BasketStationTransfer.LoadOntoBasket load => new PalletBasketTransfer.LoadOntoBasket
              {
                BasketIdentity = load.BasketIdentity,
                Material = load.Material,
              },
              BasketStationTransfer.UnloadFromBasket unload =>
                new PalletBasketTransfer.UnloadFromBasket
                {
                  BasketIdentity = unload.BasketIdentity,
                  Material = unload.Material,
                },
              _ => throw new ArgumentOutOfRangeException(nameof(operation)),
            }
          )
          .ToImmutableList(),
        CycleBoundaries = operation.CycleBoundaries,
      };

    private void RecordBasketStationTransfer(
      BasketStationTransfer transfer,
      bool loadOntoBasket,
      TimeSpan elapsed,
      int lulNum,
      DateTime timeUTC,
      IDbTransaction trans,
      List<LogEntry> logs,
      string foreignId,
      string originalMessage
    )
    {
      var (containerNum, containerId) = RecordedContainerIdentity(transfer.BasketIdentity);
      logs.Add(
        AddLogEntry(
          trans,
          new NewEventLogEntry
          {
            Material = transfer.Material,
            Pallet = containerNum,
            ContainerId = containerId,
            LogType = LogType.BasketLoadUnload,
            LocationName = "L/U",
            LocationNum = lulNum,
            Program = loadOntoBasket ? "LOAD" : "UNLOAD",
            StartOfCycle = false,
            EndTimeUTC = timeUTC,
            Result = loadOntoBasket ? "LOAD" : "UNLOAD",
            ElapsedTime = elapsed,
            ActiveOperationTime = transfer.ActiveOperationTime,
          },
          foreignId,
          originalMessage
        )
      );
    }

    private static TimeSpan BasketStationTransferElapsed(
      BasketStationTransfer transfer,
      TimeSpan totalElapsed,
      int transferMaterialCount,
      TimeSpan? totalActive
    )
    {
      var weight = totalActive is { } active
        ? (double)transfer.ActiveOperationTime.Ticks / active.Ticks
        : (double)transfer.Material.Count / transferMaterialCount;
      return TimeSpan.FromSeconds(Math.Round(totalElapsed.TotalSeconds * weight, 1));
    }

    private static string BasketStationFingerprint(
      BasketStationOperation operation,
      int lulNum,
      TimeSpan totalElapsed,
      IReadOnlyDictionary<string, string> externalQueues
    )
    {
      var fingerprint = new StringBuilder();
      AppendFingerprint(fingerprint, lulNum.ToString(CultureInfo.InvariantCulture));
      AppendFingerprint(fingerprint, totalElapsed.Ticks.ToString(CultureInfo.InvariantCulture));
      foreach (var transfer in operation.Transfers)
      {
        AppendFingerprint(
          fingerprint,
          transfer is BasketStationTransfer.LoadOntoBasket ? "load" : "unload"
        );
        AppendFingerprint(fingerprint, BasketStationIdentity(transfer.BasketIdentity));
        AppendFingerprint(
          fingerprint,
          transfer.ActiveOperationTime.Ticks.ToString(CultureInfo.InvariantCulture)
        );
        var queue = (transfer as BasketStationTransfer.UnloadFromBasket)?.DestinationQueue;
        AppendFingerprint(fingerprint, queue);
        if (
          transfer is BasketStationTransfer.UnloadFromBasket
          && queue is not null
          && externalQueues is not null
          && externalQueues.TryGetValue(queue, out var externalServer)
        )
          AppendFingerprint(fingerprint, externalServer);
        else
          AppendFingerprint(fingerprint, null);
        foreach (
          var material in transfer
            .Material.OrderBy(material => material.MaterialID)
            .ThenBy(material => material.Process)
            .ThenBy(material => material.Face)
        )
        {
          AppendFingerprint(
            fingerprint,
            material.MaterialID.ToString(CultureInfo.InvariantCulture)
          );
          AppendFingerprint(fingerprint, material.Process.ToString(CultureInfo.InvariantCulture));
          AppendFingerprint(fingerprint, material.Face.ToString(CultureInfo.InvariantCulture));
        }
      }
      foreach (var boundary in operation.CycleBoundaries)
      {
        AppendFingerprint(fingerprint, boundary is BasketCycleBoundary.Start ? "start" : "end");
        AppendFingerprint(fingerprint, BasketStationIdentity(boundary.BasketIdentity));
        AppendFingerprint(
          fingerprint,
          (boundary as BasketCycleBoundary.Start)?.AssociatedBasketNum?.ToString(
            CultureInfo.InvariantCulture
          )
        );
        foreach (
          var material in boundary
            .Material.OrderBy(material => material.MaterialID)
            .ThenBy(material => material.Process)
            .ThenBy(material => material.Face)
        )
        {
          AppendFingerprint(
            fingerprint,
            material.MaterialID.ToString(CultureInfo.InvariantCulture)
          );
          AppendFingerprint(fingerprint, material.Process.ToString(CultureInfo.InvariantCulture));
          AppendFingerprint(fingerprint, material.Face.ToString(CultureInfo.InvariantCulture));
        }
        foreach (
          var containerId in (
            boundary as BasketCycleBoundary.End
          )?.ReconciledBasketIdentities.OrderBy(id => id) ?? Enumerable.Empty<Guid>()
        )
          AppendFingerprint(fingerprint, containerId.ToString("D"));
      }
      return fingerprint.ToString();
    }

    private static string BasketStationIdentity(ContainerIdentity identity) =>
      identity switch
      {
        ContainerIdentity.Numbered numbered => $"numbered:{numbered.ContainerNum}",
        ContainerIdentity.Uuid uuid => $"uuid:{uuid.ContainerId:D}",
        _ => "none",
      };

    private static void AppendFingerprint(StringBuilder fingerprint, string value)
    {
      value ??= "";
      fingerprint
        .Append(value.Length.ToString(CultureInfo.InvariantCulture))
        .Append(':')
        .Append(value);
    }

    public IEnumerable<LogEntry> RecordEmptyPallet(
      int pallet,
      DateTime timeUTC,
      string foreignId = null,
      bool palletEnd = false
    )
    {
      return AddEntryInTransaction(
        trans =>
        {
          var logs = new List<LogEntry>();
          if (palletEnd)
          {
            RecordPalletCycleEnd(
              pallet: pallet,
              mats: [],
              timeUTC: timeUTC,
              logs: logs,
              trans: trans
            );
          }
          else
          {
            RecordPalletCycleStart(
              pallet: pallet,
              mats: [],
              timeUTC: timeUTC,
              logs: logs,
              trans: trans,
              foreignId: foreignId
            );
          }
          return logs;
        },
        foreignId: foreignId
      );
    }

    // Records pallet unload events and queue destinations. Basket destinations are recorded only
    // from an explicit PalletBasketLoadUnloadCompletion.
    private void RecordUnloadEnd(
      IEnumerable<MaterialToUnloadFromFace> toUnload,
      int lulNum,
      int pallet,
      TimeSpan totalElapsed,
      TimeSpan? totalActive,
      int totMatCnt,
      DateTime timeUTC,
      List<LogEntry> logs,
      IReadOnlyDictionary<string, string> externalQueues,
      List<MaterialToSendToExternalQueue> sendToExternal,
      IDbTransaction trans
    )
    {
      foreach (var face in toUnload ?? [])
      {
        foreach (var matAndDest in face.MaterialIDToDestination)
        {
          var dest = matAndDest.Value;
          if (dest == null)
            continue;

          if (dest.Queue != null)
          {
            if (externalQueues != null && externalQueues.TryGetValue(dest.Queue, out var server))
            {
              var details = GetMaterialDetails(matAndDest.Key, trans: trans);
              sendToExternal.Add(
                new MaterialToSendToExternalQueue()
                {
                  Queue = dest.Queue,
                  Server = server,
                  PartName = details?.PartName,
                  Serial = details?.Serial,
                }
              );
            }
            else
            {
              logs.AddRange(
                AddToQueue(
                  trans: trans,
                  matId: matAndDest.Key,
                  process: face.Process,
                  queue: dest.Queue,
                  position: -1,
                  operatorName: null,
                  timeUTC: timeUTC,
                  reason: "Unloaded"
                )
              );
            }
          }
        }

        TimeSpan elapsed;
        if (totalActive.HasValue)
        {
          elapsed = TimeSpan.FromSeconds(
            Math.Round(
              totalElapsed.TotalSeconds
                * face.ActiveOperationTime.TotalSeconds
                / totalActive.Value.TotalSeconds,
              1
            )
          );
        }
        else
        {
          elapsed = TimeSpan.FromSeconds(
            Math.Round(
              totalElapsed.TotalSeconds * face.MaterialIDToDestination.Count / totMatCnt,
              1
            )
          );
        }

        logs.Add(
          AddLogEntry(
            trans,
            new NewEventLogEntry()
            {
              Material = face.MaterialIDToDestination.Keys.Select(m => new EventLogMaterial()
              {
                MaterialID = m,
                Face = face.FaceNum,
                Process = face.Process,
              }),
              Pallet = pallet,
              LogType = LogType.LoadUnloadCycle,
              LocationName = "L/U",
              LocationNum = lulNum,
              Program = "UNLOAD",
              StartOfCycle = false,
              EndTimeUTC = timeUTC,
              ElapsedTime = elapsed,
              ActiveOperationTime = face.ActiveOperationTime,
              Result = "UNLOAD",
            },
            face.ForeignID,
            face.OriginalMessage
          )
        );
      }
    }

    private void RecordPalletCycleStart(
      int pallet,
      IEnumerable<EventLogMaterial> mats,
      DateTime timeUTC,
      List<LogEntry> logs,
      IDbTransaction trans,
      string foreignId
    )
    {
      logs.Add(
        AddLogEntry(
          trans,
          new NewEventLogEntry()
          {
            Material = mats,
            Pallet = pallet,
            LogType = LogType.PalletCycle,
            LocationName = "Pallet Cycle",
            LocationNum = 1,
            Program = "",
            StartOfCycle = true,
            EndTimeUTC = timeUTC,
            Result = "PalletCycle",
            ElapsedTime = TimeSpan.Zero,
            ActiveOperationTime = TimeSpan.Zero,
          },
          foreignID: foreignId,
          null
        )
      );
    }

    private void RecordPalletCycleEnd(
      int pallet,
      IEnumerable<EventLogMaterial> mats,
      DateTime timeUTC,
      List<LogEntry> logs,
      IDbTransaction trans
    )
    {
      using var lastTimeCmd = _connection.CreateCommand();
      lastTimeCmd.CommandText =
        "SELECT TimeUTC FROM stations where Pallet = $pal AND Result = 'PalletCycle' "
        + "ORDER BY Counter DESC LIMIT 1";
      lastTimeCmd.Parameters.Add("pal", SqliteType.Integer).Value = pallet;

      var elapsedTime = TimeSpan.Zero;
      var lastCycleTime = lastTimeCmd.ExecuteScalar();
      if (lastCycleTime != null && lastCycleTime != DBNull.Value)
        elapsedTime = timeUTC.Subtract(new DateTime((long)lastCycleTime, DateTimeKind.Utc));

      logs.Add(
        AddLogEntry(
          trans,
          new NewEventLogEntry()
          {
            Material = mats,
            Pallet = pallet,
            LogType = LogType.PalletCycle,
            LocationName = "Pallet Cycle",
            LocationNum = 1,
            Program = "",
            StartOfCycle = false,
            EndTimeUTC = timeUTC,
            Result = "PalletCycle",
            ElapsedTime = elapsedTime,
            ActiveOperationTime = TimeSpan.Zero,
          },
          null,
          null
        )
      );
    }

    private void RecordLoadMaterialPaths(
      IEnumerable<MaterialToLoadOntoFace> toLoad,
      IDbTransaction trans
    )
    {
      foreach (var face in toLoad ?? [])
      {
        if (face.Path.HasValue)
        {
          foreach (var mat in face.MaterialIDs)
          {
            RecordPathForProcess(mat, face.Process, face.Path.Value, trans);
          }
        }
      }
    }

    // Records pallet load events.
    private void RecordLoadEnd(
      IEnumerable<MaterialToLoadOntoFace> toLoad,
      int lulNum,
      int pallet,
      TimeSpan totalElapsed,
      TimeSpan? totalActive,
      int totMatCnt,
      DateTime timeUTC,
      List<LogEntry> logs,
      IDbTransaction trans,
      IReadOnlySet<long> materialFromBaskets = null
    )
    {
      foreach (var face in toLoad ?? [])
      {
        foreach (var mat in face.MaterialIDs)
        {
          if (materialFromBaskets?.Contains(mat) == true)
            continue;
          var prevProcMat = new EventLogMaterial()
          {
            MaterialID = mat,
            Process = face.Process - 1,
            Face = 0,
          };

          logs.AddRange(
            RemoveFromAllQueues(
              trans,
              prevProcMat,
              operatorName: null,
              reason: "LoadedToPallet",
              timeUTC: timeUTC
            )
          );
        }

        TimeSpan elapsed;
        if (totalActive.HasValue)
        {
          elapsed = TimeSpan.FromSeconds(
            Math.Round(
              totalElapsed.TotalSeconds
                * face.ActiveOperationTime.TotalSeconds
                / totalActive.Value.TotalSeconds,
              1
            )
          );
        }
        else
        {
          elapsed = TimeSpan.FromSeconds(
            Math.Round(totalElapsed.TotalSeconds * face.MaterialIDs.Count / totMatCnt, 1)
          );
        }

        logs.Add(
          AddLogEntry(
            trans,
            new NewEventLogEntry()
            {
              Material = face.MaterialIDs.Select(m => new EventLogMaterial()
              {
                MaterialID = m,
                Face = face.FaceNum,
                Process = face.Process,
              }),
              Pallet = pallet,
              LogType = LogType.LoadUnloadCycle,
              LocationName = "L/U",
              LocationNum = lulNum,
              Program = "LOAD",
              StartOfCycle = false,
              // Add 1 second to be after the pallet cycle
              EndTimeUTC = timeUTC.AddSeconds(1),
              Result = "LOAD",
              ElapsedTime = elapsed,
              ActiveOperationTime = face.ActiveOperationTime,
            },
            face.ForeignID,
            face.OriginalMessage
          )
        );
      }
    }

    public LogEntry RecordManualWorkAtLULStart(
      IEnumerable<EventLogMaterial> mats,
      int pallet,
      int lulNum,
      DateTime timeUTC,
      string operationName,
      string foreignId = null,
      string originalMessage = null
    )
    {
      if (operationName == "LOAD" || operationName == "UNLOAD")
      {
        throw new ArgumentException(
          "ManualWorkAtLUL operation cannot be LOAD or UNLOAD",
          "operationName"
        );
      }
      var log = new NewEventLogEntry()
      {
        Material = mats,
        Pallet = pallet,
        LogType = LogType.LoadUnloadCycle,
        LocationName = "L/U",
        LocationNum = lulNum,
        Program = operationName,
        StartOfCycle = true,
        EndTimeUTC = timeUTC,
        Result = operationName,
      };
      return AddEntryInTransaction(trans => AddLogEntry(trans, log, foreignId, originalMessage));
    }

    public LogEntry RecordManualWorkAtLULEnd(
      IEnumerable<EventLogMaterial> mats,
      int pallet,
      int lulNum,
      DateTime timeUTC,
      TimeSpan elapsed,
      TimeSpan active,
      string operationName,
      string foreignId = null,
      string originalMessage = null
    )
    {
      if (operationName == "LOAD" || operationName == "UNLOAD")
      {
        throw new ArgumentException(
          "ManualWorkAtLUL operation cannot be LOAD or UNLOAD",
          "operationName"
        );
      }
      var log = new NewEventLogEntry()
      {
        Material = mats,
        Pallet = pallet,
        LogType = LogType.LoadUnloadCycle,
        LocationName = "L/U",
        LocationNum = lulNum,
        Program = operationName,
        StartOfCycle = false,
        EndTimeUTC = timeUTC,
        ElapsedTime = elapsed,
        ActiveOperationTime = active,
        Result = operationName,
      };
      return AddEntryInTransaction(trans => AddLogEntry(trans, log, foreignId, originalMessage));
    }

    public LogEntry RecordMachineStart(
      IEnumerable<EventLogMaterial> mats,
      int pallet,
      string statName,
      int statNum,
      string program,
      DateTime timeUTC,
      IDictionary<string, string> extraData = null,
      IEnumerable<ToolSnapshot> pockets = null,
      string foreignId = null,
      string originalMessage = null
    )
    {
      var log = new NewEventLogEntry()
      {
        Material = mats,
        Pallet = pallet,
        LogType = LogType.MachineCycle,
        LocationName = statName,
        LocationNum = statNum,
        Program = program,
        StartOfCycle = true,
        EndTimeUTC = timeUTC,
        Result = "",
        ToolPockets = pockets,
      };
      if (extraData != null)
      {
        foreach (var k in extraData)
          log.ProgramDetails[k.Key] = k.Value;
      }
      return AddEntryInTransaction(trans => AddLogEntry(trans, log, foreignId, originalMessage));
    }

    public LogEntry RecordMachineEnd(
      IEnumerable<EventLogMaterial> mats,
      int pallet,
      string statName,
      int statNum,
      string program,
      string result,
      DateTime timeUTC,
      TimeSpan elapsed,
      TimeSpan active,
      IDictionary<string, string> extraData = null,
      ImmutableList<ToolUse> tools = null,
      IEnumerable<ToolSnapshot> pockets = null,
      long? deleteToolSnapshotsFromCntr = null,
      string foreignId = null,
      string originalMessage = null
    )
    {
      var log = new NewEventLogEntry()
      {
        Material = mats,
        Pallet = pallet,
        LogType = LogType.MachineCycle,
        LocationName = statName,
        LocationNum = statNum,
        Program = program,
        StartOfCycle = false,
        EndTimeUTC = timeUTC,
        Result = result,
        ElapsedTime = elapsed,
        ActiveOperationTime = active,
        ToolPockets = pockets,
        Tools = tools,
      };
      if (extraData != null)
      {
        foreach (var k in extraData)
          log.ProgramDetails[k.Key] = k.Value;
      }
      return AddEntryInTransaction(trans =>
      {
        var evts = AddLogEntry(trans, log, foreignId, originalMessage);

        if (deleteToolSnapshotsFromCntr.HasValue)
        {
          using (var cmd = _connection.CreateCommand())
          {
            ((IDbCommand)cmd).Transaction = trans;
            cmd.CommandText = "DELETE FROM tool_snapshots WHERE Counter = $cntr";
            cmd.Parameters.Add("cntr", SqliteType.Integer).Value =
              deleteToolSnapshotsFromCntr.Value;
            cmd.ExecuteNonQuery();
          }
        }

        return evts;
      });
    }

    public LogEntry RecordPalletArriveRotaryInbound(
      IEnumerable<EventLogMaterial> mats,
      int pallet,
      string statName,
      int statNum,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    )
    {
      return AddEntryInTransaction(trans =>
        AddLogEntry(
          trans,
          new NewEventLogEntry()
          {
            Material = mats,
            Pallet = pallet,
            LogType = LogType.PalletOnRotaryInbound,
            LocationName = statName,
            LocationNum = statNum,
            Program = "Arrive",
            StartOfCycle = true,
            EndTimeUTC = timeUTC,
            Result = "Arrive",
          },
          foreignId,
          originalMessage
        )
      );
    }

    public LogEntry RecordPalletDepartRotaryInbound(
      IEnumerable<EventLogMaterial> mats,
      int pallet,
      string statName,
      int statNum,
      DateTime timeUTC,
      TimeSpan elapsed,
      bool rotateIntoWorktable,
      string foreignId = null,
      string originalMessage = null
    )
    {
      return AddEntryInTransaction(trans =>
        AddLogEntry(
          trans,
          new NewEventLogEntry()
          {
            Material = mats,
            Pallet = pallet,
            LogType = LogType.PalletOnRotaryInbound,
            LocationName = statName,
            LocationNum = statNum,
            Program = "Depart",
            StartOfCycle = false,
            EndTimeUTC = timeUTC,
            Result = rotateIntoWorktable ? "RotateIntoWorktable" : "LeaveMachine",
            ElapsedTime = elapsed,
          },
          foreignId,
          originalMessage
        )
      );
    }

    public LogEntry RecordPalletArriveStocker(
      IEnumerable<EventLogMaterial> mats,
      int pallet,
      int stockerNum,
      DateTime timeUTC,
      bool waitForMachine,
      string foreignId = null,
      string originalMessage = null
    )
    {
      return AddEntryInTransaction(trans =>
        AddLogEntry(
          trans,
          new NewEventLogEntry()
          {
            Material = mats,
            Pallet = pallet,
            LogType = LogType.PalletInStocker,
            LocationName = "Stocker",
            LocationNum = stockerNum,
            Program = "Arrive",
            StartOfCycle = true,
            EndTimeUTC = timeUTC,
            Result = waitForMachine ? "WaitForMachine" : "WaitForUnload",
          },
          foreignId,
          originalMessage
        )
      );
    }

    public LogEntry RecordPalletDepartStocker(
      IEnumerable<EventLogMaterial> mats,
      int pallet,
      int stockerNum,
      DateTime timeUTC,
      bool waitForMachine,
      TimeSpan elapsed,
      string foreignId = null,
      string originalMessage = null
    )
    {
      return AddEntryInTransaction(trans =>
        AddLogEntry(
          trans,
          new NewEventLogEntry()
          {
            Material = mats,
            Pallet = pallet,
            LogType = LogType.PalletInStocker,
            LocationName = "Stocker",
            LocationNum = stockerNum,
            Program = "Depart",
            StartOfCycle = false,
            EndTimeUTC = timeUTC,
            Result = waitForMachine ? "WaitForMachine" : "WaitForUnload",
            ElapsedTime = elapsed,
          },
          foreignId,
          originalMessage
        )
      );
    }

    public LogEntry RecordBasketLoadBegin(
      IEnumerable<EventLogMaterial> mats,
      int basketId,
      int lulNum,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    )
    {
      return RecordBasketLoadBegin(
        mats,
        new ContainerIdentity.Numbered { ContainerNum = basketId },
        lulNum,
        timeUTC,
        foreignId,
        originalMessage
      );
    }

    public LogEntry RecordBasketLoadBegin(
      IEnumerable<EventLogMaterial> mats,
      ContainerIdentity basketIdentity,
      int lulNum,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    )
    {
      return AddEntryInTransaction(trans =>
      {
        var (containerNum, containerId) = RecordedContainerIdentity(basketIdentity);
        var entry = new NewEventLogEntry()
        {
          Material = mats,
          Pallet = containerNum,
          ContainerId = containerId,
          LogType = LogType.BasketLoadUnload,
          LocationName = "L/U",
          LocationNum = lulNum,
          Program = "LOAD",
          StartOfCycle = true,
          EndTimeUTC = timeUTC,
          Result = "LOAD",
          ElapsedTime = TimeSpan.Zero,
          ActiveOperationTime = TimeSpan.Zero,
        };
        return AddLogEntry(trans, entry, foreignId, originalMessage);
      });
    }

    public LogEntry RecordBasketUnloadBegin(
      IEnumerable<EventLogMaterial> mats,
      int basketId,
      int lulNum,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    )
    {
      return RecordBasketUnloadBegin(
        mats,
        new ContainerIdentity.Numbered { ContainerNum = basketId },
        lulNum,
        timeUTC,
        foreignId,
        originalMessage
      );
    }

    public LogEntry RecordBasketUnloadBegin(
      IEnumerable<EventLogMaterial> mats,
      ContainerIdentity basketIdentity,
      int lulNum,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    )
    {
      return AddEntryInTransaction(trans =>
      {
        var (containerNum, containerId) = RecordedContainerIdentity(basketIdentity);
        // Create BasketLoadUnload event
        var entry = new NewEventLogEntry()
        {
          Material = mats,
          Pallet = containerNum,
          ContainerId = containerId,
          LogType = LogType.BasketLoadUnload,
          LocationName = "L/U",
          LocationNum = lulNum,
          Program = "UNLOAD",
          StartOfCycle = true,
          EndTimeUTC = timeUTC,
          Result = "UNLOAD",
          ElapsedTime = TimeSpan.Zero,
          ActiveOperationTime = TimeSpan.Zero,
        };
        return AddLogEntry(trans, entry, foreignId, originalMessage);
      });
    }

    public LogEntry RecordBasketArriveLocation(
      IEnumerable<EventLogMaterial> mats,
      int basketId,
      string locationName,
      int locationPosition,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    )
    {
      return RecordBasketArriveLocation(
        mats,
        new ContainerIdentity.Numbered { ContainerNum = basketId },
        locationName,
        locationPosition,
        timeUTC,
        foreignId,
        originalMessage
      );
    }

    public LogEntry RecordBasketArriveLocation(
      IEnumerable<EventLogMaterial> mats,
      ContainerIdentity basketIdentity,
      string locationName,
      int locationPosition,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    )
    {
      return AddEntryInTransaction(trans =>
      {
        var (containerNum, containerId) = RecordedContainerIdentity(basketIdentity);
        var entry = new NewEventLogEntry()
        {
          Material = mats,
          Pallet = containerNum,
          ContainerId = containerId,
          LogType = LogType.BasketInLocation,
          LocationName = locationName,
          LocationNum = locationPosition,
          Program = "Arrive",
          StartOfCycle = true,
          EndTimeUTC = timeUTC,
          Result = "",
          ElapsedTime = TimeSpan.Zero,
          ActiveOperationTime = TimeSpan.Zero,
        };
        return AddLogEntry(trans, entry, foreignId, originalMessage);
      });
    }

    public LogEntry RecordBasketDepartLocation(
      IEnumerable<EventLogMaterial> mats,
      int basketId,
      string locationName,
      int locationPosition,
      DateTime timeUTC,
      TimeSpan elapsed,
      string foreignId = null,
      string originalMessage = null
    )
    {
      return RecordBasketDepartLocation(
        mats,
        new ContainerIdentity.Numbered { ContainerNum = basketId },
        locationName,
        locationPosition,
        timeUTC,
        elapsed,
        foreignId,
        originalMessage
      );
    }

    public LogEntry RecordBasketDepartLocation(
      IEnumerable<EventLogMaterial> mats,
      ContainerIdentity basketIdentity,
      string locationName,
      int locationPosition,
      DateTime timeUTC,
      TimeSpan elapsed,
      string foreignId = null,
      string originalMessage = null
    )
    {
      return AddEntryInTransaction(trans =>
      {
        var (containerNum, containerId) = RecordedContainerIdentity(basketIdentity);
        var entry = new NewEventLogEntry()
        {
          Material = mats,
          Pallet = containerNum,
          ContainerId = containerId,
          LogType = LogType.BasketInLocation,
          LocationName = locationName,
          LocationNum = locationPosition,
          Program = "Depart",
          StartOfCycle = false,
          EndTimeUTC = timeUTC,
          Result = "",
          ElapsedTime = elapsed,
          ActiveOperationTime = TimeSpan.Zero,
        };
        return AddLogEntry(trans, entry, foreignId, originalMessage);
      });
    }

    public LogEntry RecordBasketContentSnapshot(
      IEnumerable<EventLogMaterial> mats,
      ContainerIdentity basketIdentity,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    )
    {
      return AddEntryInTransaction(trans =>
      {
        var (containerNum, containerId) = RecordedContainerIdentity(basketIdentity);
        return AddLogEntry(
          trans,
          new NewEventLogEntry
          {
            Material = mats,
            Pallet = containerNum,
            ContainerId = containerId,
            LogType = LogType.BasketContentSnapshot,
            LocationName = "Basket",
            LocationNum = 1,
            Program = "Snapshot",
            StartOfCycle = false,
            EndTimeUTC = timeUTC,
            Result = "CompleteContent",
            ElapsedTime = TimeSpan.Zero,
            ActiveOperationTime = TimeSpan.Zero,
          },
          foreignId,
          originalMessage
        );
      });
    }

    public LogEntry RecordBasketIdentityHint(
      Guid containerId,
      int basketNum,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    )
    {
      if (containerId == Guid.Empty)
        throw new ArgumentException("Container UUID can not be empty.", nameof(containerId));
      if (basketNum <= 0)
        throw new ArgumentOutOfRangeException(nameof(basketNum));

      return AddEntryInTransaction(
        trans =>
        {
          EnsureOpenBasketFragment(containerId, trans);
          return AddBasketIdentityHint(
            containerId,
            basketNum,
            timeUTC,
            trans,
            foreignId,
            originalMessage
          );
        },
        foreignId
      );
    }

    private LogEntry AddBasketIdentityHint(
      Guid containerId,
      int basketNum,
      DateTime timeUTC,
      IDbTransaction trans,
      string foreignId,
      string originalMessage
    )
    {
      var hint = AddLogEntry(
        trans,
        new NewEventLogEntry
        {
          Material = [],
          Pallet = basketNum,
          ContainerId = containerId,
          LogType = LogType.BasketIdentityHint,
          LocationName = "Identity",
          LocationNum = 1,
          Program = "Hint",
          StartOfCycle = false,
          EndTimeUTC = timeUTC,
          Result = "Current",
          ElapsedTime = TimeSpan.Zero,
          ActiveOperationTime = TimeSpan.Zero,
        },
        foreignId,
        originalMessage
      );
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText =
        "INSERT INTO current_basket_identity_hints(ContainerId, BasketNum, HintCounter) "
        + "VALUES($id, $num, $counter) ON CONFLICT(ContainerId) DO UPDATE SET "
        + "BasketNum = excluded.BasketNum, HintCounter = excluded.HintCounter "
        + "WHERE excluded.HintCounter > current_basket_identity_hints.HintCounter";
      cmd.Parameters.Add("id", SqliteType.Text).Value = containerId.ToString("D");
      cmd.Parameters.Add("num", SqliteType.Integer).Value = basketNum;
      cmd.Parameters.Add("counter", SqliteType.Integer).Value = hint.Counter;
      cmd.ExecuteNonQuery();
      return hint;
    }

    private DateTime EnsureOpenBasketFragment(Guid containerId, IDbTransaction trans)
    {
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText =
        "SELECT MIN(TimeUTC) FROM stations s WHERE ContainerId = $id "
        + "AND StationLoc IN ($loadUnloadType, $locationType, $snapshotType, $cycleType) "
        + "AND "
        + ignoreInvalidEventCondition
        + " "
        + "AND NOT EXISTS(SELECT 1 FROM basket_cycle_container_ids f WHERE f.ContainerId = $id)";
      cmd.Parameters.Add("id", SqliteType.Text).Value = containerId.ToString("D");
      cmd.Parameters.Add("loadUnloadType", SqliteType.Integer).Value = (int)
        LogType.BasketLoadUnload;
      cmd.Parameters.Add("locationType", SqliteType.Integer).Value = (int)LogType.BasketInLocation;
      cmd.Parameters.Add("snapshotType", SqliteType.Integer).Value = (int)
        LogType.BasketContentSnapshot;
      cmd.Parameters.Add("cycleType", SqliteType.Integer).Value = (int)LogType.BasketCycle;
      var firstEventTime = cmd.ExecuteScalar();
      if (firstEventTime is null or DBNull)
        throw new ConflictRequestException(
          $"Container UUID {containerId:D} does not identify an open basket fragment."
        );
      return new DateTime((long)firstEventTime, DateTimeKind.Utc);
    }

    private static (int ContainerNum, Guid? ContainerId) RecordedContainerIdentity(
      ContainerIdentity identity
    ) =>
      identity switch
      {
        ContainerIdentity.Numbered numbered when numbered.ContainerNum > 0 => (
          numbered.ContainerNum,
          null
        ),
        ContainerIdentity.Uuid uuid when uuid.ContainerId != Guid.Empty => (-1, uuid.ContainerId),
        _ => throw new ArgumentException(
          "A basket event requires a positive numbered identity or a non-empty UUID.",
          nameof(identity)
        ),
      };

    public LogEntry RecordSerialForMaterialID(
      long materialID,
      int proc,
      string serial,
      DateTime timeUTC,
      string foreignID = null,
      string originalMessage = null
    )
    {
      var mat = new EventLogMaterial()
      {
        MaterialID = materialID,
        Process = proc,
        Face = 0,
      };
      return RecordSerialForMaterialID(mat, serial, timeUTC, foreignID, originalMessage);
    }

    public LogEntry RecordSerialForMaterialID(
      EventLogMaterial mat,
      string serial,
      DateTime endTimeUTC,
      string foreignID = null,
      string originalMessage = null
    )
    {
      return AddEntryInTransaction(trans =>
      {
        return RecordSerialForMaterialID(
          trans,
          mat,
          serial,
          endTimeUTC,
          foreignID,
          originalMessage
        );
      });
    }

    private LogEntry RecordSerialForMaterialID(
      IDbTransaction trans,
      EventLogMaterial mat,
      string serial,
      DateTime endTimeUTC,
      string foreignID,
      string originalMessage
    )
    {
      var log = new NewEventLogEntry()
      {
        Material = new[] { mat },
        Pallet = 0,
        LogType = LogType.PartMark,
        LocationName = "Mark",
        LocationNum = 1,
        Program = "MARK",
        StartOfCycle = false,
        EndTimeUTC = endTimeUTC,
        Result = serial,
      };
      RecordSerialForMaterialID(trans, mat.MaterialID, serial);
      return AddLogEntry(trans, log, foreignID, originalMessage);
    }

    // For backwards compatibility
    public LogEntry RecordWorkorderForMaterialID(long materialID, int proc, string workorder)
    {
      var mat = new EventLogMaterial()
      {
        MaterialID = materialID,
        Process = proc,
        Face = 0,
      };
      return RecordWorkorderForMaterialID(mat, workorder, DateTime.UtcNow);
    }

    public LogEntry RecordWorkorderForMaterialID(EventLogMaterial mat, string workorder)
    {
      return RecordWorkorderForMaterialID(mat, workorder, DateTime.UtcNow);
    }

    public LogEntry RecordWorkorderForMaterialID(
      EventLogMaterial mat,
      string workorder,
      DateTime recordUtc
    )
    {
      return AddEntryInTransaction(trans =>
      {
        return RecordWorkorderForMaterialID(trans, mat, workorder, recordUtc);
      });
    }

    public LogEntry RecordWorkorderForMaterialID(
      IDbTransaction trans,
      EventLogMaterial mat,
      string workorder,
      DateTime recordUtc
    )
    {
      var log = new NewEventLogEntry()
      {
        Material = new[] { mat },
        Pallet = 0,
        LogType = LogType.OrderAssignment,
        LocationName = "Order",
        LocationNum = 1,
        Program = "",
        StartOfCycle = false,
        EndTimeUTC = recordUtc,
        Result = workorder,
      };
      RecordWorkorderForMaterialID(trans, mat.MaterialID, workorder);
      return AddLogEntry(trans, log, null, null);
    }

    public LogEntry RecordInspectionCompleted(
      long materialID,
      int process,
      int inspectionLocNum,
      string inspectionType,
      bool success,
      IDictionary<string, string> extraData,
      TimeSpan elapsed,
      TimeSpan active
    )
    {
      var mat = new EventLogMaterial()
      {
        MaterialID = materialID,
        Process = process,
        Face = 0,
      };
      return RecordInspectionCompleted(
        mat,
        inspectionLocNum,
        inspectionType,
        success,
        extraData,
        elapsed,
        active,
        DateTime.UtcNow
      );
    }

    public LogEntry RecordInspectionCompleted(
      EventLogMaterial mat,
      int inspectionLocNum,
      string inspectionType,
      bool success,
      IDictionary<string, string> extraData,
      TimeSpan elapsed,
      TimeSpan active
    )
    {
      return RecordInspectionCompleted(
        mat,
        inspectionLocNum,
        inspectionType,
        success,
        extraData,
        elapsed,
        active,
        DateTime.UtcNow
      );
    }

    public LogEntry RecordInspectionCompleted(
      EventLogMaterial mat,
      int inspectionLocNum,
      string inspectionType,
      bool success,
      IDictionary<string, string> extraData,
      TimeSpan elapsed,
      TimeSpan active,
      DateTime inspectTimeUTC
    )
    {
      var log = new NewEventLogEntry()
      {
        Material = new[] { mat },
        Pallet = 0,
        LogType = LogType.InspectionResult,
        LocationName = "Inspection",
        LocationNum = inspectionLocNum,
        Program = inspectionType,
        StartOfCycle = false,
        EndTimeUTC = inspectTimeUTC,
        Result = success.ToString(),
        ElapsedTime = elapsed,
        ActiveOperationTime = active,
      };
      foreach (var x in extraData)
        log.ProgramDetails.Add(x.Key, x.Value);

      return AddEntryInTransaction(trans => AddLogEntry(trans, log, null, null));
    }

    public LogEntry RecordCloseoutCompleted(
      long materialID,
      int process,
      int locNum,
      string closeoutType,
      bool success,
      IDictionary<string, string> extraData,
      TimeSpan elapsed,
      TimeSpan active
    )
    {
      var mat = new EventLogMaterial()
      {
        MaterialID = materialID,
        Process = process,
        Face = 0,
      };
      return RecordCloseoutCompleted(
        mat,
        locNum,
        closeoutType,
        success,
        extraData,
        elapsed,
        active,
        DateTime.UtcNow
      );
    }

    public LogEntry RecordCloseoutCompleted(
      EventLogMaterial mat,
      int locNum,
      string closeoutType,
      bool success,
      IDictionary<string, string> extraData,
      TimeSpan elapsed,
      TimeSpan active
    )
    {
      return RecordCloseoutCompleted(
        mat,
        locNum,
        closeoutType,
        success,
        extraData,
        elapsed,
        active,
        DateTime.UtcNow
      );
    }

    public LogEntry RecordCloseoutCompleted(
      EventLogMaterial mat,
      int locNum,
      string closeoutType,
      bool success,
      IDictionary<string, string> extraData,
      TimeSpan elapsed,
      TimeSpan active,
      DateTime completeTimeUTC
    )
    {
      var log = new NewEventLogEntry()
      {
        Material = new[] { mat },
        Pallet = 0,
        LogType = LogType.CloseOut,
        LocationName = "CloseOut",
        LocationNum = locNum,
        Program = closeoutType,
        StartOfCycle = false,
        EndTimeUTC = completeTimeUTC,
        Result = success ? "" : "Failed",
        ElapsedTime = elapsed,
        ActiveOperationTime = active,
      };
      foreach (var x in extraData)
        log.ProgramDetails.Add(x.Key, x.Value);
      return AddEntryInTransaction(trans => AddLogEntry(trans, log, null, null));
    }

    public LogEntry RecordWorkorderComment(
      string workorder,
      string comment,
      string operName,
      DateTime? timeUTC = null
    )
    {
      var log = new NewEventLogEntry()
      {
        Material = Array.Empty<EventLogMaterial>(),
        Pallet = 0,
        LogType = LogType.WorkorderComment,
        LocationName = "WorkorderComment",
        LocationNum = 1,
        Program = "",
        StartOfCycle = false,
        EndTimeUTC = timeUTC ?? DateTime.UtcNow,
        Result = workorder,
      };
      log.ProgramDetails.Add("Comment", comment);
      if (!string.IsNullOrEmpty(operName))
      {
        log.ProgramDetails.Add("Operator", operName);
      }
      return AddEntryInTransaction(trans => AddLogEntry(trans, log, null, null));
    }

    public IEnumerable<LogEntry> RecordAddMaterialToQueue(
      EventLogMaterial mat,
      string queue,
      int position,
      string operatorName,
      string reason,
      DateTime? timeUTC = null
    )
    {
      return AddEntryInTransaction(trans =>
        AddToQueue(
          trans,
          mat,
          queue,
          position,
          operatorName: operatorName,
          timeUTC: timeUTC ?? DateTime.UtcNow,
          reason: reason
        )
      );
    }

    public IEnumerable<LogEntry> RecordAddMaterialToQueue(
      long matID,
      int process,
      string queue,
      int position,
      string operatorName,
      string reason,
      DateTime? timeUTC = null
    )
    {
      return AddEntryInTransaction(trans =>
        AddToQueue(
          trans,
          matID,
          process,
          queue,
          position,
          operatorName: operatorName,
          timeUTC: timeUTC ?? DateTime.UtcNow,
          reason: reason
        )
      );
    }

    public IEnumerable<LogEntry> RecordRemoveMaterialFromAllQueues(
      EventLogMaterial mat,
      string operatorName = null,
      DateTime? timeUTC = null
    )
    {
      return AddEntryInTransaction(trans =>
        RemoveFromAllQueues(
          trans,
          mat,
          operatorName: operatorName,
          reason: null,
          timeUTC ?? DateTime.UtcNow
        )
      );
    }

    public IEnumerable<LogEntry> RecordRemoveMaterialFromAllQueues(
      long matID,
      int process,
      string operatorName = null,
      DateTime? timeUTC = null
    )
    {
      return AddEntryInTransaction(trans =>
        RemoveFromAllQueues(
          trans,
          matID,
          process,
          operatorName: operatorName,
          reason: null,
          timeUTC ?? DateTime.UtcNow
        )
      );
    }

    public IEnumerable<LogEntry> BulkRemoveMaterialFromAllQueues(
      IEnumerable<long> matIds,
      string operatorName = null,
      string reason = null,
      DateTime? timeUTC = null
    )
    {
      return AddEntryInTransaction(trans =>
      {
        var evts = new List<LogEntry>();
        foreach (var matId in matIds)
        {
          var nextProc = NextProcessForQueuedMaterial(trans, matId);
          var proc = (nextProc ?? 1) - 1;
          evts.AddRange(
            RemoveFromAllQueues(
              trans,
              matId,
              proc,
              operatorName,
              reason,
              timeUTC ?? DateTime.UtcNow
            )
          );
        }
        return evts;
      });
    }

    public IEnumerable<LogEntry> QuarantineQueuedMaterial(
      long matId,
      string quarantineQueue,
      string operatorName = null,
      string reason = null,
      DateTime? timeUTC = null
    )
    {
      return AddEntryInTransaction(trans =>
      {
        var time = timeUTC ?? DateTime.UtcNow;
        var nextProc = NextProcessForQueuedMaterial(trans, matId);
        var proc = (nextProc ?? 1) - 1;
        var logs = new List<LogEntry>();

        if (!string.IsNullOrWhiteSpace(reason))
        {
          logs.Add(
            RecordOperatorNotes(
              trans,
              materialId: matId,
              process: proc,
              notes: reason,
              operatorName: operatorName,
              timeUTC: time
            )
          );
        }

        if (string.IsNullOrWhiteSpace(quarantineQueue))
        {
          logs.AddRange(
            RemoveFromAllQueues(
              trans,
              matId,
              proc,
              operatorName,
              reason: "Quarantine",
              timeUTC: time
            )
          );
        }
        else
        {
          logs.AddRange(
            AddToQueue(
              trans,
              matId,
              proc,
              quarantineQueue,
              position: -1,
              operatorName,
              time,
              reason: "Quarantine"
            )
          );
        }

        return logs;
      });
    }

    public LogEntry SignalMaterialForQuarantine(
      EventLogMaterial mat,
      int pallet,
      string queue,
      string operatorName,
      string reason,
      DateTime? timeUTC = null,
      string foreignId = null,
      string originalMessage = null
    )
    {
      var log = new NewEventLogEntry()
      {
        Material = new[] { mat },
        Pallet = pallet,
        LogType = LogType.SignalQuarantine,
        LocationName = queue,
        LocationNum = -1,
        Program = "QuarantineAfterUnload",
        StartOfCycle = false,
        EndTimeUTC = timeUTC ?? DateTime.UtcNow,
        Result = "QuarantineAfterUnload",
      };

      if (!string.IsNullOrEmpty(operatorName))
      {
        log.ProgramDetails["operator"] = operatorName;
      }
      if (!string.IsNullOrEmpty(reason))
      {
        log.ProgramDetails["note"] = reason;
      }
      return AddEntryInTransaction(trans => AddLogEntry(trans, log, foreignId, originalMessage));
    }

    public LogEntry RecordGeneralMessage(
      EventLogMaterial mat,
      string program,
      string result,
      int pallet = 0,
      DateTime? timeUTC = null,
      string foreignId = null,
      string originalMessage = null,
      IDictionary<string, string> extraData = null
    )
    {
      return RecordGeneralMessage(
        mats: mat != null ? new[] { mat } : [],
        program: program,
        result: result,
        pallet: pallet,
        timeUTC: timeUTC,
        foreignId: foreignId,
        originalMessage: originalMessage,
        extraData: extraData
      );
    }

    public LogEntry RecordGeneralMessage(
      IEnumerable<EventLogMaterial> mats,
      string program,
      string result,
      int pallet = 0,
      DateTime? timeUTC = null,
      string foreignId = null,
      string originalMessage = null,
      IDictionary<string, string> extraData = null
    )
    {
      var log = new NewEventLogEntry()
      {
        Material = mats,
        Pallet = pallet,
        LogType = LogType.GeneralMessage,
        LocationName = "Message",
        LocationNum = 1,
        Program = program,
        StartOfCycle = false,
        EndTimeUTC = timeUTC ?? DateTime.UtcNow,
        Result = result,
      };
      if (extraData != null)
      {
        foreach (var x in extraData)
          log.ProgramDetails.Add(x.Key, x.Value);
      }
      return AddEntryInTransaction(trans => AddLogEntry(trans, log, foreignId, originalMessage));
    }

    public LogEntry RecordOperatorNotes(
      long materialId,
      int process,
      string notes,
      string operatorName
    )
    {
      return RecordOperatorNotes(materialId, process, notes, operatorName, null);
    }

    public LogEntry RecordOperatorNotes(
      long materialId,
      int process,
      string notes,
      string operatorName,
      DateTime? timeUtc
    )
    {
      if (string.IsNullOrEmpty(notes))
      {
        throw new ArgumentException("Operator notes cannot be empty", nameof(notes));
      }
      return AddEntryInTransaction(trans =>
        RecordOperatorNotes(
          trans,
          materialId,
          process,
          notes,
          operatorName,
          timeUtc ?? DateTime.UtcNow
        )
      );
    }

    private LogEntry RecordOperatorNotes(
      IDbTransaction trans,
      long materialId,
      int process,
      string notes,
      string operatorName,
      DateTime timeUTC
    )
    {
      var extra = new Dictionary<string, string>();
      extra["note"] = notes;
      if (!string.IsNullOrEmpty(operatorName))
      {
        extra["operator"] = operatorName;
      }
      var log = new NewEventLogEntry()
      {
        Material = new[]
        {
          new EventLogMaterial()
          {
            MaterialID = materialId,
            Process = process,
            Face = 0,
          },
        },
        Pallet = 0,
        LogType = LogType.GeneralMessage,
        LocationName = "Message",
        LocationNum = 1,
        Program = "OperatorNotes",
        StartOfCycle = false,
        EndTimeUTC = timeUTC,
        Result = "Operator Notes",
      };
      foreach (var x in extra)
        log.ProgramDetails.Add(x.Key, x.Value);
      return AddLogEntry(trans, log, null, null);
    }

    public SwapMaterialResult SwapMaterialInCurrentPalletCycle(
      int pallet,
      long oldMatId,
      long newMatId,
      string operatorName,
      string quarantineQueue, // put the old material in this queue if it's not already in a queue
      DateTime? timeUTC = null
    )
    {
      var newLogEntries = new List<LogEntry>();
      var changedLogEntries = new List<LogEntry>();

      lock (_cfg)
      {
        using (var updateMatsCmd = _connection.CreateCommand())
        using (var trans = _connection.BeginTransaction())
        {
          updateMatsCmd.Transaction = trans;

          // get old material details
          var oldMatDetails = GetMaterialDetails(oldMatId, trans);
          if (oldMatDetails == null)
          {
            throw new ConflictRequestException("Unable to find material");
          }

          // load old events
          var oldEvents = CurrentPalletLog(pallet, includeLastPalletCycleEvt: true, trans);
          if (
            oldEvents.FirstOrDefault(entry =>
              entry is { LogType: LogType.PalletCycle, StartOfCycle: true }
            ) is
            { } palletCycleStart
          )
          {
            oldEvents.InsertRange(
              0,
              PalletEventsPrecedingCurrentCycle(pallet, palletCycleStart.Counter, trans)
            );
          }
          var oldMatProcM = oldEvents
            .SelectMany(e => e.Material)
            .Where(m => m.MaterialID == oldMatId)
            .Max(m => (int?)m.Process);
          if (!oldMatProcM.HasValue)
          {
            throw new ConflictRequestException(
              "Unable to find material, or material not currently on a pallet"
            );
          }
          var oldMatProc = oldMatProcM.Value;

          // check new material path matches
          var newMatDetails = GetMaterialDetails(newMatId, trans);
          if (newMatDetails == null)
          {
            throw new ConflictRequestException("Unable to find new material");
          }
          var newMatIsUnassigned = string.IsNullOrEmpty(newMatDetails.JobUnique);
          if (newMatIsUnassigned)
          {
            if (oldMatProc != 1)
            {
              throw new ConflictRequestException(
                "Swaps of non-process-1 must use material from the same job"
              );
            }
            if (newMatDetails.PartName != oldMatDetails.PartName)
            {
              using (var checkCastingCmd = _connection.CreateCommand())
              {
                checkCastingCmd.Transaction = trans;
                checkCastingCmd.CommandText =
                  "SELECT COUNT(*) FROM pathdata WHERE UniqueStr = $uniq AND Process = 1 AND Casting = $casting";
                checkCastingCmd.Parameters.Add("uniq", SqliteType.Text).Value =
                  oldMatDetails.JobUnique;
                checkCastingCmd.Parameters.Add("casting", SqliteType.Text).Value =
                  newMatDetails.PartName;
                if (Convert.ToInt64(checkCastingCmd.ExecuteScalar()) == 0)
                {
                  throw new ConflictRequestException(
                    "Material swap of unassigned material does not match part name or raw material name"
                  );
                }
              }
            }
          }
          else
          {
            if (oldMatDetails.JobUnique != newMatDetails.JobUnique)
            {
              throw new ConflictRequestException(
                "Overriding material on pallet must use material from the same job"
              );
            }
          }

          // perform the swap
          updateMatsCmd.CommandText =
            "UPDATE stations_mat SET MaterialID = $newmat WHERE Counter = $cntr AND MaterialID = $oldmat";
          updateMatsCmd.Parameters.Add("newmat", SqliteType.Integer).Value = newMatId;
          updateMatsCmd.Parameters.Add("cntr", SqliteType.Integer);
          updateMatsCmd.Parameters.Add("oldmat", SqliteType.Integer).Value = oldMatId;

          foreach (var evt in oldEvents)
          {
            if (evt.Material.Any(m => m.MaterialID == oldMatId))
            {
              updateMatsCmd.Parameters[1].Value = evt.Counter;
              updateMatsCmd.ExecuteNonQuery();

              changedLogEntries.Add(
                evt with
                {
                  Material = evt
                    .Material.Select(m =>
                      m.MaterialID == oldMatId
                        ? m with
                        {
                          MaterialID = newMatId,
                          Serial = newMatDetails.Serial ?? "",
                          Workorder = newMatDetails.Workorder ?? "",
                        }
                        : m
                    )
                    .ToImmutableList(),
                }
              );
            }
          }

          // update job assignment
          if (newMatIsUnassigned)
          {
            using (var setJobCmd = _connection.CreateCommand())
            {
              setJobCmd.Transaction = trans;
              setJobCmd.CommandText =
                "UPDATE matdetails SET UniqueStr = $uniq, PartName = $part, NumProcesses = $numproc WHERE MaterialID = $mat";
              var uniqParam = setJobCmd.Parameters.Add("uniq", SqliteType.Text);
              var nameParam = setJobCmd.Parameters.Add("part", SqliteType.Text);
              setJobCmd.Parameters.Add("numproc", SqliteType.Integer).Value =
                oldMatDetails.NumProcesses;
              var matIdParam = setJobCmd.Parameters.Add("mat", SqliteType.Integer);

              matIdParam.Value = newMatId;
              uniqParam.Value = oldMatDetails.JobUnique;
              nameParam.Value = oldMatDetails.PartName;
              setJobCmd.ExecuteNonQuery();

              matIdParam.Value = oldMatId;
              uniqParam.Value = DBNull.Value;
              nameParam.Value = newMatDetails.PartName; // could restore oldMatId back to a casting name
              setJobCmd.ExecuteNonQuery();
            }
          }

          //update paths
          if (
            oldMatDetails.Paths != null
            && oldMatDetails.Paths.TryGetValue(oldMatProc, out var oldPath)
          )
          {
            using (var newPathCmd = _connection.CreateCommand())
            {
              newPathCmd.Transaction = trans;
              newPathCmd.CommandText =
                "INSERT OR REPLACE INTO mat_path_details(MaterialID, Process, Path) VALUES ($mid, $proc, $path)";
              newPathCmd.Parameters.Add("mid", SqliteType.Integer).Value = newMatId;
              newPathCmd.Parameters.Add("proc", SqliteType.Integer).Value = oldMatProc;
              newPathCmd.Parameters.Add("path", SqliteType.Integer).Value = oldPath;
              newPathCmd.ExecuteNonQuery();
            }

            if (newMatIsUnassigned || oldMatProc >= 2)
            {
              using (var delPathCmd = _connection.CreateCommand())
              {
                delPathCmd.Transaction = trans;
                delPathCmd.CommandText =
                  "DELETE FROM mat_path_details WHERE MaterialID = $mid AND Process = $proc";
                delPathCmd.Parameters.Add("mid", SqliteType.Integer).Value = oldMatId;
                delPathCmd.Parameters.Add("proc", SqliteType.Integer).Value = oldMatProc;
                delPathCmd.ExecuteNonQuery();
              }
            }
          }

          // Record a message for the override
          var time = timeUTC ?? DateTime.UtcNow;

          var newMsg = new NewEventLogEntry()
          {
            Material = new EventLogMaterial[]
            {
              new EventLogMaterial()
              {
                MaterialID = oldMatId,
                Process = oldMatProc,
                Face = 0,
              },
              new EventLogMaterial()
              {
                MaterialID = newMatId,
                Process = oldMatProc,
                Face = 0,
              },
            },
            Pallet = pallet,
            LogType = LogType.SwapMaterialOnPallet,
            LocationName = "SwapMatOnPallet",
            LocationNum = 1,
            Program = "SwapMatOnPallet",
            StartOfCycle = false,
            EndTimeUTC = time,
            Result =
              "Replace "
              + (oldMatDetails?.Serial ?? "material")
              + " with "
              + (newMatDetails?.Serial ?? "material")
              + " on pallet "
              + pallet,
          };
          newLogEntries.Add(AddLogEntry(trans, newMsg, null, null));

          // update queues
          var removeQueueEvts = RemoveFromAllQueues(
            trans,
            matID: newMatId,
            process: oldMatProc - 1,
            operatorName: operatorName,
            reason: "SwapMaterial",
            time
          );
          newLogEntries.AddRange(removeQueueEvts);

          var oldMatPutInQueue =
            removeQueueEvts
              .Where(e =>
                e.LogType == LogType.RemoveFromQueue && !string.IsNullOrEmpty(e.LocationName)
              )
              .Select(e => e.LocationName)
              .FirstOrDefault()
            ?? quarantineQueue;

          if (!string.IsNullOrEmpty(oldMatPutInQueue))
          {
            newLogEntries.AddRange(
              AddToQueue(
                trans,
                matId: oldMatId,
                process: oldMatProc - 1,
                queue: oldMatPutInQueue,
                position: -1,
                operatorName: operatorName,
                timeUTC: time,
                reason: "SwapMaterial"
              )
            );
          }

          trans.Commit();
        }
      }

      foreach (var l in newLogEntries)
        _cfg.OnNewLogEntry(l, null, this);

      return new SwapMaterialResult()
      {
        ChangedLogEntries = changedLogEntries,
        NewLogEntries = newLogEntries,
      };
    }

    private static readonly string LogTypesToCheckForNextProcess = string.Join(
      ",",
      new int[]
      {
        (int)LogType.AddToQueue,
        (int)LogType.RemoveFromQueue,
        (int)LogType.LoadUnloadCycle,
        (int)LogType.MachineCycle,
        (int)LogType.BasketLoadUnload,
        (int)LogType.BasketCycle,
      }
    );

    private void InvalidatePalletCycle(
      long matId,
      int? process,
      string operatorName,
      DateTime timeUTC,
      SqliteTransaction trans,
      List<LogEntry> newLogEntries,
      Action<ImmutableList<EventLogMaterial>> validateAffectedMaterials = null
    )
    {
      using var getCycles = _connection.CreateCommand();
      using var getMatsCmd = _connection.CreateCommand();
      using var updateEvtCmd = _connection.CreateCommand();
      using var removePathDetailsCmd = _connection.CreateCommand();
      using var addMessageCmd = _connection.CreateCommand();
      using var checkQueueCmd = _connection.CreateCommand();

      getCycles.CommandText =
        "SELECT s.Counter FROM stations s WHERE "
        + " EXISTS ("
        + "   SELECT 1 FROM stations_mat m "
        + "        WHERE s.Counter = m.Counter "
        + "          AND m.MaterialID = $matid "
        + (process.HasValue ? "AND m.Process >= $proc" : "")
        + " ) AND "
        + " s.StationLoc IN ("
        + LogTypesToCheckForNextProcess
        + ") AND "
        + " NOT EXISTS("
        + "   SELECT 1 FROM program_details d WHERE s.Counter = d.Counter AND d.Key = 'PalletCycleInvalidated'"
        + " )";
      getCycles.Parameters.Add("matid", SqliteType.Integer).Value = matId;
      if (process.HasValue)
      {
        getCycles.Parameters.Add("proc", SqliteType.Integer).Value = process.Value;
      }
      getCycles.Transaction = trans;

      getMatsCmd.CommandText = "SELECT MaterialID, Process FROM stations_mat WHERE Counter = $cntr";
      getMatsCmd.Parameters.Add("cntr", SqliteType.Integer);
      getMatsCmd.Transaction = trans;

      updateEvtCmd.CommandText = "UPDATE stations SET ActiveTime = 0 WHERE Counter = $cntr";
      updateEvtCmd.Parameters.Add("cntr", SqliteType.Integer);
      updateEvtCmd.Transaction = trans;

      removePathDetailsCmd.CommandText =
        "DELETE FROM mat_path_details WHERE MaterialID = $mid AND Process = $proc";
      removePathDetailsCmd.Parameters.Add("mid", SqliteType.Integer);
      removePathDetailsCmd.Parameters.Add("proc", SqliteType.Integer);
      removePathDetailsCmd.Transaction = trans;

      // Note: 'PalletCycleInvalidated' is used for both pallet and basket
      // invalidations. The name includes "Pallet" for backwards compatibility with existing
      // databases, but this key marks invalidation of any cycle (pallet or basket).
      addMessageCmd.CommandText =
        "INSERT OR REPLACE INTO program_details(Counter, Key, Value) VALUES ($cntr,'PalletCycleInvalidated','1')";
      addMessageCmd.Parameters.Add("cntr", SqliteType.Integer);
      addMessageCmd.Transaction = trans;

      checkQueueCmd.CommandText = "SELECT Queue FROM queues WHERE MaterialID = $matid";
      checkQueueCmd.Parameters.Add("matid", SqliteType.Integer).Value = matId;
      checkQueueCmd.Transaction = trans;

      // Determine the complete affected event and material sets before changing anything.
      var invalidatedCntrs = new List<long>();
      var allMatIds = new HashSet<(long matId, int proc)>();
      using (var reader = getCycles.ExecuteReader())
      {
        while (reader.Read())
        {
          var cntr = reader.GetInt64(0);
          invalidatedCntrs.Add(cntr);

          getMatsCmd.Parameters[0].Value = cntr;
          using (var matIdReader = getMatsCmd.ExecuteReader())
          {
            while (matIdReader.Read())
            {
              long removedMatId = matIdReader.GetInt64(0);
              int removedProc = matIdReader.GetInt32(1);
              allMatIds.Add((removedMatId, removedProc));
            }
          }
        }
      }

      if (process is > 1)
      {
        using var getLatestProcess = _connection.CreateCommand();
        getLatestProcess.CommandText =
          "SELECT MAX(m.Process) FROM stations s "
          + "JOIN stations_mat m ON s.Counter = m.Counter "
          + "WHERE m.MaterialID = $matid "
          + "AND s.StationLoc IN ("
          + LogTypesToCheckForNextProcess
          + ") AND NOT EXISTS("
          + "SELECT 1 FROM program_details d WHERE s.Counter = d.Counter AND d.Key = 'PalletCycleInvalidated'"
          + ")";
        getLatestProcess.Parameters.Add("matid", SqliteType.Integer).Value = matId;
        getLatestProcess.Transaction = trans;

        var latestProcess = getLatestProcess.ExecuteScalar();
        if (latestProcess is DBNull || latestProcess is null)
        {
          throw new ConflictRequestException("No valid process remains to invalidate.");
        }

        var latestProcessNumber = Convert.ToInt32(latestProcess);
        if (latestProcessNumber != process.Value)
        {
          throw new ConflictRequestException("The requested process is no longer current.");
        }
      }

      foreach (var affectedMatId in allMatIds.Select(m => m.matId).Distinct())
      {
        checkQueueCmd.Parameters[0].Value = affectedMatId;
        using var queueReader = checkQueueCmd.ExecuteReader();
        if (queueReader.Read())
        {
          throw new ConflictRequestException(
            $"Can not invalidate a cycle while material {affectedMatId} is in queue {queueReader.GetString(0)}."
          );
        }
      }

      if (invalidatedCntrs.Count == 0)
      {
        throw new ConflictRequestException("No valid process remains to invalidate.");
      }

      var affectedMaterials = allMatIds
        .Select(m => new EventLogMaterial()
        {
          MaterialID = m.matId,
          Process = m.proc,
          Face = 0,
        })
        .ToImmutableList();
      validateAffectedMaterials?.Invoke(affectedMaterials);

      foreach (var cntr in invalidatedCntrs)
      {
        updateEvtCmd.Parameters[0].Value = cntr;
        updateEvtCmd.ExecuteNonQuery();

        addMessageCmd.Parameters[0].Value = cntr;
        addMessageCmd.ExecuteNonQuery();
      }

      foreach (var (affectedMatId, affectedProcess) in allMatIds)
      {
        removePathDetailsCmd.Parameters[0].Value = affectedMatId;
        removePathDetailsCmd.Parameters[1].Value = affectedProcess;
        removePathDetailsCmd.ExecuteNonQuery();
      }

      // record events
      var newMsg = new NewEventLogEntry()
      {
        Material = affectedMaterials,
        Pallet = 0,
        LogType = LogType.InvalidateCycle,
        LocationName = "InvalidateCycle",
        LocationNum = 1,
        Program = "InvalidateCycle",
        StartOfCycle = false,
        EndTimeUTC = timeUTC,
        Result = "Invalidate all events on cycles",
      };
      newMsg.ProgramDetails["EditedCounters"] = string.Join(",", invalidatedCntrs);
      if (!string.IsNullOrEmpty(operatorName))
      {
        newMsg.ProgramDetails["operator"] = operatorName;
      }
      newLogEntries.Add(AddLogEntry(trans, newMsg, null, null));
    }

    public IEnumerable<LogEntry> InvalidatePalletCycle(
      long matId,
      int process,
      string operatorName,
      DateTime? timeUTC = null,
      Action<ImmutableList<EventLogMaterial>> validateAffectedMaterials = null
    )
    {
      var newLogEntries = new List<LogEntry>();

      lock (_cfg)
      {
        using var trans = _connection.BeginTransaction();

        InvalidatePalletCycle(
          matId: matId,
          process: process,
          operatorName: operatorName,
          timeUTC: timeUTC ?? DateTime.UtcNow,
          trans: trans,
          newLogEntries: newLogEntries,
          validateAffectedMaterials: validateAffectedMaterials
        );

        trans.Commit();
      }

      foreach (var l in newLogEntries)
        _cfg.OnNewLogEntry(l, null, this);

      return newLogEntries;
    }

    public IEnumerable<LogEntry> InvalidateAndChangeAssignment(
      long matId,
      string operatorName,
      string changeJobUniqueTo,
      string changePartNameTo,
      int changeNumProcessesTo,
      DateTime? timeUTC = null,
      Action<ImmutableList<EventLogMaterial>> validateAffectedMaterials = null
    )
    {
      var newLogEntries = new List<LogEntry>();

      lock (_cfg)
      {
        using var trans = _connection.BeginTransaction();

        using var updateMatDetailsCmd = _connection.CreateCommand();
        updateMatDetailsCmd.Transaction = trans;
        updateMatDetailsCmd.CommandText =
          "UPDATE matdetails SET UniqueStr = $uniq, PartName = $part, NumProcesses = $numproc WHERE MaterialID = $mid";
        updateMatDetailsCmd.Parameters.Add("uniq", SqliteType.Text).Value = string.IsNullOrEmpty(
          changeJobUniqueTo
        )
          ? DBNull.Value
          : changeJobUniqueTo;
        updateMatDetailsCmd.Parameters.Add("part", SqliteType.Text).Value = changePartNameTo;
        updateMatDetailsCmd.Parameters.Add("numproc", SqliteType.Integer).Value =
          changeNumProcessesTo;
        updateMatDetailsCmd.Parameters.Add("mid", SqliteType.Integer).Value = matId;
        updateMatDetailsCmd.ExecuteNonQuery();

        // Update the assignment before creating the invalidation event so the event describes the
        // material's new assignment. Both operations use the same transaction, so a validation
        // failure during invalidation rolls back the assignment update.
        InvalidatePalletCycle(
          matId: matId,
          process: null,
          operatorName: operatorName,
          timeUTC: timeUTC ?? DateTime.UtcNow,
          trans: trans,
          newLogEntries: newLogEntries,
          validateAffectedMaterials: validateAffectedMaterials
        );

        trans.Commit();
      }

      foreach (var l in newLogEntries)
        _cfg.OnNewLogEntry(l, null, this);

      return newLogEntries;
    }

    public LogEntry CreateRebooking(
      string bookingId,
      string partName,
      int qty = 1,
      string notes = null,
      int? priority = null,
      string workorder = null,
      DateTime? timeUTC = null
    )
    {
      if (qty <= 0)
      {
        throw new ArgumentException("Quantity must be greater than 0", nameof(qty));
      }

      return AddEntryInTransaction(trans =>
      {
        var time = timeUTC ?? DateTime.UtcNow;

        using var cmd = _connection.CreateCommand();
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText =
          "INSERT INTO rebookings(BookingId, TimeUTC, Part, Notes, Workorder, Quantity, Priority) VALUES ($id, $time, $part, $notes, $workorder, $qty, $pri)";

        cmd.Parameters.Add("id", SqliteType.Text).Value = bookingId;
        cmd.Parameters.Add("time", SqliteType.Integer).Value = time.Ticks;
        cmd.Parameters.Add("part", SqliteType.Text).Value = partName;
        cmd.Parameters.Add("notes", SqliteType.Text).Value = string.IsNullOrEmpty(notes)
          ? DBNull.Value
          : notes;
        cmd.Parameters.Add("workorder", SqliteType.Text).Value = string.IsNullOrEmpty(workorder)
          ? DBNull.Value
          : workorder;
        cmd.Parameters.Add("qty", SqliteType.Integer).Value = qty;
        cmd.Parameters.Add("pri", SqliteType.Integer).Value = priority.HasValue
          ? priority.Value
          : DBNull.Value;

        cmd.ExecuteNonQuery();

        var log = new NewEventLogEntry()
        {
          Material = [],
          Pallet = 0,
          LogType = LogType.Rebooking,
          LocationName = "Rebooking",
          LocationNum = priority ?? 0,
          Program = partName,
          StartOfCycle = false,
          EndTimeUTC = time,
          ElapsedTime = TimeSpan.Zero,
          ActiveOperationTime = TimeSpan.Zero,
          Result = bookingId,
        };
        if (!string.IsNullOrEmpty(notes))
        {
          log.ProgramDetails.Add("Notes", notes);
        }
        if (!string.IsNullOrEmpty(workorder))
        {
          log.ProgramDetails.Add("Workorder", workorder);
        }
        log.ProgramDetails.Add("Quantity", qty.ToString());

        return AddLogEntry(trans, log, foreignID: null, origMessage: null);
      });
    }

    public LogEntry CancelRebooking(string bookingId, DateTime? timeUTC = null)
    {
      return AddEntryInTransaction(trans =>
      {
        var log = new NewEventLogEntry()
        {
          Material = [],
          Pallet = 0,
          LogType = LogType.CancelRebooking,
          LocationName = "CancelRebooking",
          LocationNum = 1,
          Program = "",
          StartOfCycle = false,
          EndTimeUTC = timeUTC ?? DateTime.UtcNow,
          ElapsedTime = TimeSpan.Zero,
          ActiveOperationTime = TimeSpan.Zero,
          Result = bookingId,
        };

        using var cmd = _connection.CreateCommand();
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "UPDATE rebookings SET Canceled = 1 WHERE BookingId = $id";
        cmd.Parameters.Add("id", SqliteType.Text).Value = bookingId;
        cmd.ExecuteNonQuery();

        return AddLogEntry(trans, log, foreignID: null, origMessage: null);
      });
    }

    public Rebooking LookupRebooking(string bookingId)
    {
      using var cmd = _connection.CreateCommand();
      using var trans = _connection.BeginTransaction();
      cmd.Transaction = trans;

      cmd.CommandText =
        "SELECT BookingId, TimeUTC, Part, Notes, Workorder, Quantity, Priority FROM rebookings WHERE BookingId = $id LIMIT 1";
      cmd.Parameters.Add("id", SqliteType.Text).Value = bookingId;

      using (var reader = cmd.ExecuteReader())
      {
        if (reader.Read() == false)
        {
          return null;
        }

        return new Rebooking()
        {
          BookingId = reader.GetString(0),
          TimeUTC = new DateTime(reader.GetInt64(1), DateTimeKind.Utc),
          PartName = reader.GetString(2),
          Notes = reader.IsDBNull(3) ? null : reader.GetString(3),
          Workorder = reader.IsDBNull(4) ? null : reader.GetString(4),
          Quantity = reader.GetInt32(5),
          Priority = reader.IsDBNull(6) ? null : reader.GetInt32(6),
        };
      }
    }
    #endregion

    #region Material IDs
    private long AllocateMaterialID(IDbTransaction trans, string unique, string part, int numProc)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText =
          "INSERT INTO matdetails(UniqueStr, PartName, NumProcesses) VALUES ($uniq,$part,$numproc)";
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = string.IsNullOrEmpty(unique)
          ? DBNull.Value
          : unique;
        cmd.Parameters.Add("part", SqliteType.Text).Value = part;
        cmd.Parameters.Add("numproc", SqliteType.Integer).Value = numProc;
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT last_insert_rowid()";
        cmd.Parameters.Clear();
        var materialId = (long)cmd.ExecuteScalar();
        ValidateMaterialId(materialId);
        return materialId;
      }
    }

    public long AllocateMaterialID(string unique, string part, int numProc)
    {
      lock (_cfg)
      {
        using (var trans = _connection.BeginTransaction())
        {
          var matId = AllocateMaterialID(trans, unique, part, numProc);
          trans.Commit();
          return matId;
        }
      }
    }

    public ImmutableList<MaterialDetails> AllocateMaterialIDs(
      ImmutableList<MaterialToAllocate> material,
      string idempotencyKey
    )
    {
      ArgumentNullException.ThrowIfNull(material);
      if (material.IsEmpty)
        throw new ArgumentException("At least one material must be allocated.", nameof(material));
      if (string.IsNullOrWhiteSpace(idempotencyKey))
        throw new ArgumentException(
          "An idempotent material allocation requires an idempotency key.",
          nameof(idempotencyKey)
        );
      foreach (var item in material)
      {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.JobUnique))
          throw new ArgumentException("A material job unique is required.", nameof(material));
        if (string.IsNullOrWhiteSpace(item.PartName))
          throw new ArgumentException("A material part name is required.", nameof(material));
        if (item.NumProcesses <= 0)
          throw new ArgumentOutOfRangeException(
            nameof(material),
            "The number of material processes must be positive."
          );
        ArgumentNullException.ThrowIfNull(item.Paths);
        if (
          item.Paths.Any(path => path.Key <= 0 || path.Key > item.NumProcesses || path.Value <= 0)
        )
          throw new ArgumentException(
            "Material paths require a positive path for a valid process.",
            nameof(material)
          );
      }

      var fingerprint = MaterialAllocationFingerprint(material);
      lock (_cfg)
      {
        using var trans = _connection.BeginTransaction();
        using var existingCommand = _connection.CreateCommand();
        existingCommand.Transaction = trans;
        existingCommand.CommandText =
          "SELECT Fingerprint FROM material_allocation_operations WHERE IdempotencyKey = $key";
        existingCommand.Parameters.Add("key", SqliteType.Text).Value = idempotencyKey;
        var existingFingerprint = existingCommand.ExecuteScalar() as string;
        if (existingFingerprint is not null)
        {
          if (existingFingerprint != fingerprint)
            throw new ConflictRequestException(
              $"Idempotency key {idempotencyKey} already identifies a different material allocation."
            );
          var existing = MaterialAllocationForIdempotencyKey(idempotencyKey, trans);
          if (existing.Count != material.Count)
            throw new InvalidOperationException(
              $"Material allocation {idempotencyKey} has an incomplete durable result."
            );
          trans.Commit();
          return existing;
        }

        var allocated = ImmutableList.CreateBuilder<MaterialDetails>();
        for (var position = 0; position < material.Count; ++position)
        {
          var requested = material[position];
          var materialId = AllocateMaterialID(
            trans,
            requested.JobUnique,
            requested.PartName,
            requested.NumProcesses
          );
          foreach (var path in requested.Paths)
            RecordPathForProcess(materialId, path.Key, path.Value, trans);
          using var recordMaterial = _connection.CreateCommand();
          recordMaterial.Transaction = trans;
          recordMaterial.CommandText =
            "INSERT INTO material_allocation_material(IdempotencyKey, Position, MaterialID) "
            + "VALUES($key, $position, $material)";
          recordMaterial.Parameters.Add("key", SqliteType.Text).Value = idempotencyKey;
          recordMaterial.Parameters.Add("position", SqliteType.Integer).Value = position;
          recordMaterial.Parameters.Add("material", SqliteType.Integer).Value = materialId;
          recordMaterial.ExecuteNonQuery();
          allocated.Add(GetMaterialDetails(materialId, trans));
        }
        using var recordOperation = _connection.CreateCommand();
        recordOperation.Transaction = trans;
        recordOperation.CommandText =
          "INSERT INTO material_allocation_operations(IdempotencyKey, Fingerprint) "
          + "VALUES($key, $fingerprint)";
        recordOperation.Parameters.Add("key", SqliteType.Text).Value = idempotencyKey;
        recordOperation.Parameters.Add("fingerprint", SqliteType.Text).Value = fingerprint;
        recordOperation.ExecuteNonQuery();
        trans.Commit();
        return allocated.ToImmutable();
      }
    }

    private ImmutableList<MaterialDetails> MaterialAllocationForIdempotencyKey(
      string idempotencyKey,
      IDbTransaction trans
    )
    {
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText =
        "SELECT MaterialID FROM material_allocation_material "
        + "WHERE IdempotencyKey = $key ORDER BY Position";
      cmd.Parameters.Add("key", SqliteType.Text).Value = idempotencyKey;
      var materialIds = ImmutableList.CreateBuilder<long>();
      using (var reader = cmd.ExecuteReader())
      {
        while (reader.Read())
          materialIds.Add(reader.GetInt64(0));
      }
      return materialIds.Select(id => GetMaterialDetails(id, trans)).ToImmutableList();
    }

    private static string MaterialAllocationFingerprint(ImmutableList<MaterialToAllocate> material)
    {
      var fingerprint = new StringBuilder();
      AppendFingerprint(fingerprint, material.Count.ToString(CultureInfo.InvariantCulture));
      foreach (var item in material)
      {
        AppendFingerprint(fingerprint, item.JobUnique);
        AppendFingerprint(fingerprint, item.PartName);
        AppendFingerprint(fingerprint, item.NumProcesses.ToString(CultureInfo.InvariantCulture));
        AppendFingerprint(fingerprint, item.Paths.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var path in item.Paths.OrderBy(path => path.Key))
        {
          AppendFingerprint(fingerprint, path.Key.ToString(CultureInfo.InvariantCulture));
          AppendFingerprint(fingerprint, path.Value.ToString(CultureInfo.InvariantCulture));
        }
      }
      return fingerprint.ToString();
    }

    public long AllocateMaterialIDAndGenerateSerial(
      string unique,
      string part,
      int numProc,
      DateTime timeUTC,
      out LogEntry serialLogEntry,
      string foreignID = null,
      string originalMessage = null
    )
    {
      lock (_cfg)
      {
        if (_cfg.SerialSettings == null)
        {
          serialLogEntry = null;
          return AllocateMaterialID(unique, part, numProc);
        }
        else
        {
          long matId = -1;
          serialLogEntry = AddEntryInTransaction(trans =>
          {
            matId = AllocateMaterialID(trans, unique, part, numProc);
            var serial = _cfg.SerialSettings.ConvertMaterialIDToSerial(matId);
            return RecordSerialForMaterialID(
              trans,
              new EventLogMaterial()
              {
                MaterialID = matId,
                Process = 0,
                Face = 0,
              },
              serial,
              timeUTC,
              foreignID: foreignID,
              originalMessage: originalMessage
            );
          });
          return matId;
        }
      }
    }

    public long AllocateMaterialIDForCasting(string casting)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        using (var trans = _connection.BeginTransaction())
        {
          cmd.Transaction = trans;
          cmd.CommandText = "INSERT INTO matdetails(PartName, NumProcesses) VALUES ($casting,1)";
          cmd.Parameters.Add("casting", SqliteType.Text).Value = casting;
          cmd.ExecuteNonQuery();
          cmd.CommandText = "SELECT last_insert_rowid()";
          cmd.Parameters.Clear();
          var matID = (long)cmd.ExecuteScalar();
          ValidateMaterialId(matID);
          trans.Commit();
          return matID;
        }
      }
    }

    public MaterialDetails AllocateMaterialIDWithSerialAndWorkorder(
      string unique,
      string part,
      int numProc,
      string serial,
      string workorder,
      out IEnumerable<LogEntry> newLogEntries,
      DateTime? timeUTC = null
    )
    {
      var time = timeUTC ?? DateTime.UtcNow;
      lock (_cfg)
      {
        long matId = -1;
        newLogEntries = AddEntryInTransaction(trans =>
        {
          matId = AllocateMaterialID(trans, unique, part, numProc);
          var logs = new List<LogEntry>();
          if (!string.IsNullOrEmpty(serial))
          {
            logs.Add(
              RecordSerialForMaterialID(
                trans,
                new EventLogMaterial()
                {
                  MaterialID = matId,
                  Process = 0,
                  Face = 0,
                },
                serial,
                time,
                foreignID: null,
                originalMessage: null
              )
            );
          }
          if (!string.IsNullOrEmpty(workorder))
          {
            logs.Add(
              RecordWorkorderForMaterialID(
                trans,
                new EventLogMaterial()
                {
                  MaterialID = matId,
                  Process = 0,
                  Face = 0,
                },
                workorder,
                time
              )
            );
          }
          return logs;
        });

        return new MaterialDetails()
        {
          MaterialID = matId,
          JobUnique = unique,
          PartName = part,
          NumProcesses = numProc,
          Serial = serial,
          Workorder = workorder,
        };
      }
    }

    public void SetDetailsForMaterialID(long matID, string unique, string part, int? numProc)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText =
            "UPDATE matdetails SET UniqueStr = coalesce($uniq, UniqueStr), PartName = coalesce($part, PartName), NumProcesses = coalesce($numproc, NumProcesses) WHERE MaterialID = $mat";
          cmd.Parameters.Add("uniq", SqliteType.Text).Value =
            unique == null ? DBNull.Value : (object)unique;
          cmd.Parameters.Add("part", SqliteType.Text).Value =
            part == null ? DBNull.Value : (object)part;
          cmd.Parameters.Add("numproc", SqliteType.Integer).Value =
            numProc == null ? DBNull.Value : (object)numProc;
          cmd.Parameters.Add("mat", SqliteType.Integer).Value = matID;
          cmd.ExecuteNonQuery();
        }
      }
    }

    public void RecordPathForProcess(long matID, int process, int path)
    {
      lock (_cfg)
      {
        RecordPathForProcess(matID, process, path, null);
      }
    }

    private void RecordPathForProcess(long matID, int process, int path, IDbTransaction trans)
    {
      using (var cmd = _connection.CreateCommand())
      {
        if (trans != null)
        {
          ((IDbCommand)cmd).Transaction = trans;
        }
        cmd.CommandText =
          "INSERT OR REPLACE INTO mat_path_details(MaterialID, Process, Path) VALUES ($mid, $proc, $path)";
        cmd.Parameters.Add("mid", SqliteType.Integer).Value = matID;
        cmd.Parameters.Add("proc", SqliteType.Integer).Value = process;
        cmd.Parameters.Add("path", SqliteType.Integer).Value = path;
        cmd.ExecuteNonQuery();
      }
    }

    public void CreateMaterialID(long matID, string unique, string part, int numProc)
    {
      ValidateMaterialId(matID);
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText =
            "INSERT OR REPLACE INTO matdetails(MaterialID, UniqueStr, PartName, NumProcesses) "
            + " VALUES ($mid, $uniq, $part, $numproc)";
          cmd.Parameters.Add("mid", SqliteType.Integer).Value = matID;
          cmd.Parameters.Add("uniq", SqliteType.Text).Value = string.IsNullOrEmpty(unique)
            ? DBNull.Value
            : unique;
          cmd.Parameters.Add("part", SqliteType.Text).Value = part;
          cmd.Parameters.Add("numproc", SqliteType.Integer).Value = numProc;
          cmd.ExecuteNonQuery();
        }
      }
    }

    private static void ValidateMaterialId(long materialId)
    {
      if (materialId > MaterialId.MaxValue)
        throw new ArgumentOutOfRangeException(
          nameof(materialId),
          materialId,
          $"Material IDs must not exceed {MaterialId.MaxValue} so JSON clients can represent them exactly."
        );
    }

    public MaterialDetails GetMaterialDetails(long matID)
    {
      using var trans = _connection.BeginTransaction();
      var ret = GetMaterialDetails(matID, trans);
      trans.Commit();
      return ret;
    }

    private MaterialDetails GetMaterialDetails(long matID, IDbTransaction trans)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText =
          "SELECT UniqueStr, PartName, NumProcesses, Workorder, Serial FROM matdetails WHERE MaterialID = $mat";
        cmd.Parameters.Add("mat", SqliteType.Integer).Value = matID;

        MaterialDetails ret = null;
        using (var reader = cmd.ExecuteReader())
        {
          if (reader.Read())
          {
            ret = new MaterialDetails()
            {
              MaterialID = matID,
              JobUnique = (!reader.IsDBNull(0)) ? reader.GetString(0) : null,
              PartName = (!reader.IsDBNull(1)) ? reader.GetString(1) : null,
              NumProcesses = (!reader.IsDBNull(2)) ? reader.GetInt32(2) : 0,
              Workorder = (!reader.IsDBNull(3)) ? reader.GetString(3) : null,
              Serial = (!reader.IsDBNull(4)) ? reader.GetString(4) : null,
            };
          }
        }

        if (ret != null)
        {
          var paths = ImmutableDictionary<int, int>.Empty.ToBuilder();
          cmd.CommandText = "SELECT Process, Path FROM mat_path_details WHERE MaterialID = $mat";
          using (var reader = cmd.ExecuteReader())
          {
            while (reader.Read())
            {
              var proc = reader.GetInt32(0);
              var path = reader.GetInt32(1);
              paths[proc] = path;
            }
          }
          if (paths.Count > 0)
          {
            ret = ret with { Paths = paths.ToImmutable() };
          }
        }

        return ret;
      }
    }

    public IReadOnlyList<MaterialDetails> GetMaterialDetailsForSerial(string serial)
    {
      using (var trans = _connection.BeginTransaction())
      using (var cmd = _connection.CreateCommand())
      using (var cmd2 = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        ((IDbCommand)cmd2).Transaction = trans;
        cmd.CommandText =
          "SELECT MaterialID, UniqueStr, PartName, NumProcesses, Workorder FROM matdetails WHERE Serial = $ser";
        cmd.Parameters.Add("ser", SqliteType.Text).Value = serial;

        cmd2.CommandText = "SELECT Process, Path FROM mat_path_details WHERE MaterialID = $mat";
        cmd2.Parameters.Add("mat", SqliteType.Integer);

        var ret = new List<MaterialDetails>();
        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var mat = new MaterialDetails()
            {
              MaterialID = (!reader.IsDBNull(0)) ? reader.GetInt64(0) : 0,
              JobUnique = (!reader.IsDBNull(1)) ? reader.GetString(1) : null,
              PartName = (!reader.IsDBNull(2)) ? reader.GetString(2) : null,
              NumProcesses = (!reader.IsDBNull(3)) ? reader.GetInt32(3) : 0,
              Workorder = (!reader.IsDBNull(4)) ? reader.GetString(4) : null,
              Serial = serial,
            };

            cmd2.Parameters[0].Value = mat.MaterialID;
            var paths = ImmutableDictionary<int, int>.Empty.ToBuilder();
            using (var reader2 = cmd2.ExecuteReader())
            {
              while (reader2.Read())
              {
                var proc = reader2.GetInt32(0);
                var path = reader2.GetInt32(1);
                paths[proc] = path;
              }
            }
            if (paths.Count > 0)
            {
              mat = mat with { Paths = paths.ToImmutable() };
            }
            ret.Add(mat);
          }
        }

        trans.Commit();
        return ret;
      }
    }

    public List<MaterialDetails> GetMaterialForWorkorder(string workorder)
    {
      using (var trans = _connection.BeginTransaction())
      using (var cmd = _connection.CreateCommand())
      using (var pathCmd = _connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText =
          "SELECT MaterialID, UniqueStr, PartName, NumProcesses, Serial FROM matdetails WHERE Workorder IS NOT NULL AND Workorder = $work";
        cmd.Parameters.Add("work", SqliteType.Text).Value = workorder;

        pathCmd.CommandText = "SELECT Process, Path FROM mat_path_details WHERE MaterialID = $mat";
        pathCmd.Transaction = trans;
        var param = pathCmd.Parameters.Add("mat", SqliteType.Integer);

        var ret = new List<MaterialDetails>();
        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var mat = new MaterialDetails()
            {
              MaterialID = (!reader.IsDBNull(0)) ? reader.GetInt64(0) : 0,
              JobUnique = (!reader.IsDBNull(1)) ? reader.GetString(1) : null,
              PartName = (!reader.IsDBNull(2)) ? reader.GetString(2) : null,
              NumProcesses = (!reader.IsDBNull(3)) ? reader.GetInt32(3) : 0,
              Serial = (!reader.IsDBNull(4)) ? reader.GetString(4) : null,
              Workorder = workorder,
            };

            var paths = ImmutableDictionary<int, int>.Empty.ToBuilder();
            param.Value = mat.MaterialID;
            using (var reader2 = pathCmd.ExecuteReader())
            {
              while (reader2.Read())
              {
                var proc = reader2.GetInt32(0);
                var path = reader2.GetInt32(1);
                paths[proc] = path;
              }
            }

            if (paths.Count > 0)
            {
              mat = mat with { Paths = paths.ToImmutable() };
            }
            ret.Add(mat);
          }
        }

        trans.Commit();
        return ret;
      }
    }

    public long CountMaterialForWorkorder(string workorder, string part)
    {
      using var cmd = _connection.CreateCommand();

      if (string.IsNullOrEmpty(part))
      {
        cmd.CommandText =
          "SELECT COUNT(*) FROM matdetails WHERE Workorder IS NOT NULL AND Workorder = $work";
        cmd.Parameters.Add("work", SqliteType.Text).Value = workorder;
        return (long)cmd.ExecuteScalar();
      }
      else
      {
        cmd.CommandText =
          "SELECT COUNT(*) FROM matdetails WHERE Workorder IS NOT NULL AND Workorder = $work AND PartName = $part";
        cmd.Parameters.Add("work", SqliteType.Text).Value = workorder;
        cmd.Parameters.Add("part", SqliteType.Text).Value = part;
        return (long)cmd.ExecuteScalar();
      }
    }

    public List<MaterialDetails> GetMaterialForJobUnique(string jobUnique)
    {
      using (var trans = _connection.BeginTransaction())
      using (var cmd = _connection.CreateCommand())
      using (var pathCmd = _connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText =
          "SELECT MaterialID, PartName, NumProcesses, Serial, Workorder FROM matdetails WHERE UniqueStr = $uniq ORDER BY Workorder, Serial";
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = jobUnique;

        pathCmd.CommandText = "SELECT Process, Path FROM mat_path_details WHERE MaterialID = $mat";
        pathCmd.Transaction = trans;
        var param = pathCmd.Parameters.Add("mat", SqliteType.Integer);

        var ret = new List<MaterialDetails>();
        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var mat = new MaterialDetails()
            {
              MaterialID = (!reader.IsDBNull(0)) ? reader.GetInt64(0) : 0,
              JobUnique = jobUnique,
              PartName = (!reader.IsDBNull(1)) ? reader.GetString(1) : null,
              NumProcesses = (!reader.IsDBNull(2)) ? reader.GetInt32(2) : 0,
              Serial = (!reader.IsDBNull(3)) ? reader.GetString(3) : null,
              Workorder = (!reader.IsDBNull(4)) ? reader.GetString(4) : null,
            };

            var paths = ImmutableDictionary<int, int>.Empty.ToBuilder();
            param.Value = mat.MaterialID;
            using (var reader2 = pathCmd.ExecuteReader())
            {
              while (reader2.Read())
              {
                var proc = reader2.GetInt32(0);
                var path = reader2.GetInt32(1);
                paths[proc] = path;
              }
            }

            if (paths.Count > 0)
            {
              mat = mat with { Paths = paths.ToImmutable() };
            }
            ret.Add(mat);
          }
        }

        trans.Commit();
        return ret;
      }
    }

    public long CountMaterialForJobUnique(string jobUnique)
    {
      using var cmd = _connection.CreateCommand();
      cmd.CommandText = "SELECT COUNT(*) FROM matdetails WHERE UniqueStr = $uniq";
      cmd.Parameters.Add("uniq", SqliteType.Text).Value = jobUnique;
      return (long)cmd.ExecuteScalar();
    }

    private void RecordSerialForMaterialID(IDbTransaction trans, long matID, string serial)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "UPDATE matdetails SET Serial = $ser WHERE MaterialID = $mat";
        if (string.IsNullOrEmpty(serial))
          cmd.Parameters.Add("ser", SqliteType.Text).Value = DBNull.Value;
        else
          cmd.Parameters.Add("ser", SqliteType.Text).Value = serial;
        cmd.Parameters.Add("mat", SqliteType.Integer).Value = matID;
        cmd.ExecuteNonQuery();
      }
    }

    private void RecordWorkorderForMaterialID(IDbTransaction trans, long matID, string workorder)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "UPDATE matdetails SET Workorder = $work WHERE MaterialID = $mat";
        if (string.IsNullOrEmpty(workorder))
          cmd.Parameters.Add("work", SqliteType.Text).Value = DBNull.Value;
        else
          cmd.Parameters.Add("work", SqliteType.Text).Value = workorder;
        cmd.Parameters.Add("mat", SqliteType.Integer).Value = matID;
        cmd.ExecuteNonQuery();
      }
    }
    #endregion

    #region Queues

    private IReadOnlyList<LogEntry> AddToQueue(
      IDbTransaction trans,
      long matId,
      int process,
      string queue,
      int position,
      string operatorName,
      DateTime timeUTC,
      string reason
    )
    {
      var mat = new EventLogMaterial()
      {
        MaterialID = matId,
        Process = process,
        Face = 0,
      };

      return AddToQueue(trans, mat, queue, position, operatorName, timeUTC, reason);
    }

    private IReadOnlyList<LogEntry> AddToQueue(
      IDbTransaction trans,
      EventLogMaterial mat,
      string queue,
      int position,
      string operatorName,
      DateTime timeUTC,
      string reason
    )
    {
      var ret = new List<LogEntry>();
      mat = mat with { Face = 0 };

      ret.AddRange(RemoveFromAllQueues(trans, mat, operatorName, reason: "MovingInQueue", timeUTC));

      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        if (position >= 0)
        {
          cmd.CommandText =
            "UPDATE queues SET Position = Position + 1 " + " WHERE Queue = $q AND Position >= $p";
          cmd.Parameters.Add("q", SqliteType.Text).Value = queue;
          cmd.Parameters.Add("p", SqliteType.Integer).Value = position;
          cmd.ExecuteNonQuery();

          cmd.CommandText =
            "INSERT INTO queues(MaterialID, Queue, Position, AddTimeUTC) "
            + " VALUES ($m, $q, (SELECT MIN(IFNULL(MAX(Position) + 1, 0), $p) FROM queues WHERE Queue = $q), $t)";
          cmd.Parameters.Add("m", SqliteType.Integer).Value = mat.MaterialID;
          cmd.Parameters.Add("t", SqliteType.Integer).Value = timeUTC.Ticks;
          cmd.ExecuteNonQuery();
        }
        else
        {
          cmd.CommandText =
            "INSERT INTO queues(MaterialID, Queue, Position, AddTimeUTC) "
            + " VALUES ($m, $q, (SELECT IFNULL(MAX(Position) + 1, 0) FROM queues WHERE Queue = $q), $t)";
          cmd.Parameters.Add("m", SqliteType.Integer).Value = mat.MaterialID;
          cmd.Parameters.Add("q", SqliteType.Text).Value = queue;
          cmd.Parameters.Add("t", SqliteType.Integer).Value = timeUTC.Ticks;
          cmd.ExecuteNonQuery();
        }
      }

      int resultingPosition;
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText = "SELECT Position FROM queues WHERE Queue = $q AND MaterialID = $m";
        cmd.Parameters.Add("m", SqliteType.Integer).Value = mat.MaterialID;
        cmd.Parameters.Add("q", SqliteType.Text).Value = queue;
        resultingPosition = Convert.ToInt32(cmd.ExecuteScalar());
      }

      var log = new NewEventLogEntry()
      {
        Material = new[] { mat },
        Pallet = 0,
        LogType = LogType.AddToQueue,
        LocationName = queue,
        LocationNum = resultingPosition,
        Program = reason ?? "",
        StartOfCycle = false,
        EndTimeUTC = timeUTC,
        Result = "",
      };
      if (!string.IsNullOrEmpty(operatorName))
      {
        log.ProgramDetails["operator"] = operatorName;
      }

      ret.Add(AddLogEntry(trans, log, null, null));

      return ret;
    }

    private IReadOnlyList<LogEntry> RemoveFromAllQueues(
      IDbTransaction trans,
      long matID,
      int process,
      string operatorName,
      string reason,
      DateTime timeUTC
    )
    {
      var mat = new EventLogMaterial()
      {
        MaterialID = matID,
        Process = process,
        Face = 0,
      };

      return RemoveFromAllQueues(trans, mat, operatorName, reason, timeUTC);
    }

    private IReadOnlyList<LogEntry> RemoveFromAllQueues(
      IDbTransaction trans,
      EventLogMaterial mat,
      string operatorName,
      string reason,
      DateTime timeUTC
    )
    {
      using (var findCmd = _connection.CreateCommand())
      using (var updatePosCmd = _connection.CreateCommand())
      using (var deleteCmd = _connection.CreateCommand())
      {
        ((IDbCommand)findCmd).Transaction = trans;
        findCmd.CommandText =
          "SELECT Queue, Position, AddTimeUTC FROM queues WHERE MaterialID = $mid";
        findCmd.Parameters.Add("mid", SqliteType.Integer).Value = mat.MaterialID;

        ((IDbCommand)updatePosCmd).Transaction = trans;
        updatePosCmd.CommandText =
          "UPDATE queues SET Position = Position - 1 " + " WHERE Queue = $q AND Position > $pos";
        updatePosCmd.Parameters.Add("q", SqliteType.Text);
        updatePosCmd.Parameters.Add("pos", SqliteType.Integer);

        ((IDbCommand)deleteCmd).Transaction = trans;
        deleteCmd.CommandText = "DELETE FROM queues WHERE MaterialID = $mid";
        deleteCmd.Parameters.Add("mid", SqliteType.Integer).Value = mat.MaterialID;

        var logs = new List<LogEntry>();

        using (var reader = findCmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var queue = reader.GetString(0);
            var pos = reader.GetInt32(1);
            var addTime = reader.IsDBNull(2)
              ? null
              : (DateTime?)(new DateTime(reader.GetInt64(2), DateTimeKind.Utc));

            var log = new NewEventLogEntry()
            {
              Material = new[] { mat },
              Pallet = 0,
              LogType = LogType.RemoveFromQueue,
              LocationName = queue,
              LocationNum = pos,
              Program = reason ?? "",
              StartOfCycle = false,
              EndTimeUTC = timeUTC,
              Result = "",
              ElapsedTime = addTime.HasValue ? timeUTC.Subtract(addTime.Value) : TimeSpan.Zero,
            };
            if (!string.IsNullOrEmpty(operatorName))
            {
              log.ProgramDetails["operator"] = operatorName;
            }

            logs.Add(AddLogEntry(trans, log, null, null));

            updatePosCmd.Parameters[0].Value = queue;
            updatePosCmd.Parameters[1].Value = pos;
            updatePosCmd.ExecuteNonQuery();
          }
        }

        deleteCmd.ExecuteNonQuery();

        return logs;
      }
    }

    public BulkAddCastingResult BulkAddNewCastingsInQueue(
      string casting,
      int qty,
      string queue,
      IList<string> serials,
      string workorder,
      string operatorName,
      string reason = null,
      DateTime? timeUTC = null,
      bool throwOnExistingSerial = false
    )
    {
      var ret = new List<LogEntry>();
      var matIds = new HashSet<long>();
      var addTimeUTC = timeUTC ?? DateTime.UtcNow;

      lock (_cfg)
      {
        using (var trans = _connection.BeginTransaction())
        using (var maxPosCmd = _connection.CreateCommand())
        using (var allocateCmd = _connection.CreateCommand())
        using (var updatePartCmd = _connection.CreateCommand())
        using (var checkCmd = _connection.CreateCommand())
        using (var getMatIdCmd = _connection.CreateCommand())
        using (var addToQueueCmd = _connection.CreateCommand())
        {
          maxPosCmd.Transaction = trans;
          maxPosCmd.CommandText = "SELECT MAX(Position) FROM queues WHERE Queue = $q";
          maxPosCmd.Parameters.Add("q", SqliteType.Text).Value = queue;
          var posObj = maxPosCmd.ExecuteScalar();
          int maxExistingPos = -1;
          if (posObj != null && posObj != DBNull.Value)
          {
            maxExistingPos = Convert.ToInt32(posObj);
          }

          checkCmd.Transaction = trans;
          checkCmd.CommandText =
            "SELECT matdetails.MaterialID, "
            + "(SELECT COUNT(*) FROM stations, stations_mat "
            + "  WHERE stations.Counter = stations_mat.Counter "
            + "  AND stations_mat.MaterialID = matdetails.MaterialID "
            + "  AND stations.StationLoc IN ($lulLoc, $mcLoc)"
            + ") as Started, "
            + "(SELECT COUNT(*) FROM queues WHERE queues.MaterialID = matdetails.MaterialID) as Queued "
            + "FROM matdetails "
            + "WHERE matdetails.Serial = $ser "
            + "ORDER BY matdetails.MaterialID DESC LIMIT 1";
          checkCmd.Parameters.Add("$lulLoc", SqliteType.Integer).Value = (int)
            LogType.LoadUnloadCycle;
          checkCmd.Parameters.Add("$mcLoc", SqliteType.Integer).Value = (int)LogType.MachineCycle;
          var checkSerialParam = checkCmd.Parameters.Add("ser", SqliteType.Text);

          allocateCmd.Transaction = trans;
          allocateCmd.CommandText =
            "INSERT INTO matdetails(PartName, NumProcesses, Serial,Workorder) VALUES ($casting,1,$serial,$workorder)";
          allocateCmd.Parameters.Add("casting", SqliteType.Text).Value = casting;
          var allocateSerialParam = allocateCmd.Parameters.Add("serial", SqliteType.Text);
          var allocateWorkParam = allocateCmd.Parameters.Add("workorder", SqliteType.Text);

          updatePartCmd.Transaction = trans;
          updatePartCmd.CommandText =
            "UPDATE matdetails SET PartName = $part, Workorder = $work WHERE MaterialID = $matid";
          var updatePartPartParam = updatePartCmd.Parameters.Add("part", SqliteType.Text);
          var updatePartWorkParam = updatePartCmd.Parameters.Add("work", SqliteType.Text);
          var updatePartMatIdParam = updatePartCmd.Parameters.Add("matid", SqliteType.Integer);

          getMatIdCmd.Transaction = trans;
          getMatIdCmd.CommandText = "SELECT last_insert_rowid()";

          addToQueueCmd.Transaction = trans;
          addToQueueCmd.CommandText =
            "INSERT INTO queues(MaterialID, Queue, Position, AddTimeUTC) "
            + " VALUES ($m, $q, $pos, $t)";
          var addQueueMatIdParam = addToQueueCmd.Parameters.Add("m", SqliteType.Integer);
          addToQueueCmd.Parameters.Add("q", SqliteType.Text).Value = queue;
          var addQueuePosCmd = addToQueueCmd.Parameters.Add("pos", SqliteType.Integer);
          addToQueueCmd.Parameters.Add("t", SqliteType.Integer).Value = addTimeUTC.Ticks;

          for (int i = 0; i < qty; i++)
          {
            long matID = -1;

            if (i < serials.Count)
            {
              checkSerialParam.Value = serials[i];
              using (var reader = checkCmd.ExecuteReader())
              {
                if (reader.Read())
                {
                  var started = reader.GetInt32(1);
                  var queued = reader.GetInt32(2);
                  if (started == 0 && queued == 0 && !reader.IsDBNull(0))
                  {
                    matID = reader.GetInt64(0);
                    // material has not yet started or in a queue, so we can reuse it
                    updatePartPartParam.Value = casting;
                    updatePartMatIdParam.Value = matID;
                    updatePartWorkParam.Value = string.IsNullOrEmpty(workorder)
                      ? DBNull.Value
                      : (object)workorder;
                    updatePartCmd.ExecuteNonQuery();
                  }
                  else if (!reader.IsDBNull(0) && throwOnExistingSerial)
                  {
                    throw new Exception(
                      "Serial "
                        + serials[i]
                        + " already exists in the database with MaterialID "
                        + reader.GetInt64(0).ToString()
                    );
                  }
                }
              }
            }

            if (matID < 0)
            {
              allocateSerialParam.Value = i < serials.Count ? (object)serials[i] : DBNull.Value;
              allocateWorkParam.Value = string.IsNullOrEmpty(workorder)
                ? DBNull.Value
                : (object)workorder;
              allocateCmd.ExecuteNonQuery();
              matID = (long)getMatIdCmd.ExecuteScalar();
            }

            matIds.Add(matID);

            if (i < serials.Count)
            {
              var serLog = new NewEventLogEntry()
              {
                Material = new[]
                {
                  new EventLogMaterial()
                  {
                    MaterialID = matID,
                    Process = 0,
                    Face = 0,
                  },
                },
                Pallet = 0,
                LogType = LogType.PartMark,
                LocationName = "Mark",
                LocationNum = 1,
                Program = "MARK",
                StartOfCycle = false,
                EndTimeUTC = addTimeUTC,
                Result = serials[i],
              };
              ret.Add(AddLogEntry(trans, serLog, null, null));
            }

            if (!string.IsNullOrEmpty(workorder))
            {
              var workAssin = new NewEventLogEntry()
              {
                Material = new[]
                {
                  new EventLogMaterial()
                  {
                    MaterialID = matID,
                    Process = 0,
                    Face = 0,
                  },
                },
                Pallet = 0,
                LogType = LogType.OrderAssignment,
                LocationName = "Order",
                LocationNum = 1,
                Program = "",
                StartOfCycle = false,
                EndTimeUTC = addTimeUTC,
                Result = workorder,
              };
              ret.Add(AddLogEntry(trans, workAssin, null, null));
            }

            addQueueMatIdParam.Value = matID;
            addQueuePosCmd.Value = maxExistingPos + i + 1;
            addToQueueCmd.ExecuteNonQuery();

            var log = new NewEventLogEntry()
            {
              Material = new[]
              {
                new EventLogMaterial()
                {
                  MaterialID = matID,
                  Process = 0,
                  Face = 0,
                },
              },
              Pallet = 0,
              LogType = LogType.AddToQueue,
              LocationName = queue,
              LocationNum = maxExistingPos + i + 1,
              Program = reason ?? "",
              StartOfCycle = false,
              EndTimeUTC = addTimeUTC,
              Result = "",
            };
            if (!string.IsNullOrEmpty(operatorName))
            {
              log.ProgramDetails["operator"] = operatorName;
            }

            ret.Add(AddLogEntry(trans, log, null, null));
          }

          trans.Commit();
        }
      }
      return new BulkAddCastingResult() { MaterialIds = matIds, Logs = ret };
    }

    /// Find parts without an assigned unique in the queue, and assign them to the given unique
    public IReadOnlyList<long> AllocateCastingsInQueue(
      string queue,
      string casting,
      string unique,
      string part,
      int proc1Path,
      int numProcesses,
      int count
    )
    {
      lock (_cfg)
      {
        var matIds = new List<long>();
        using (var trans = _connection.BeginTransaction())
        using (var cmd = _connection.CreateCommand())
        {
          cmd.Transaction = trans;

          cmd.CommandText =
            "SELECT queues.MaterialID FROM queues "
            + " INNER JOIN matdetails ON queues.MaterialID = matdetails.MaterialID "
            + " WHERE Queue = $q AND matdetails.PartName = $c AND matdetails.UniqueStr IS NULL "
            + " ORDER BY Position ASC"
            + " LIMIT $cnt ";
          cmd.Parameters.Add("q", SqliteType.Text).Value = queue;
          cmd.Parameters.Add("c", SqliteType.Text).Value = casting;
          cmd.Parameters.Add("cnt", SqliteType.Integer).Value = count;
          using (var reader = cmd.ExecuteReader())
          {
            while (reader.Read())
              matIds.Add(reader.GetInt64(0));
          }

          if (matIds.Count != count)
          {
            trans.Rollback();
            return new List<long>();
          }

          cmd.CommandText =
            "UPDATE matdetails SET UniqueStr = $uniq, PartName = $p, NumProcesses = $numproc WHERE MaterialID = $mid";
          cmd.Parameters.Clear();
          cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
          cmd.Parameters.Add("p", SqliteType.Text).Value = part;
          cmd.Parameters.Add("numproc", SqliteType.Integer).Value = numProcesses;
          cmd.Parameters.Add("mid", SqliteType.Integer);

          foreach (var matId in matIds)
          {
            cmd.Parameters[3].Value = matId;
            cmd.ExecuteNonQuery();
          }

          cmd.CommandText =
            "INSERT OR REPLACE INTO mat_path_details(MaterialID, Process, Path) VALUES ($mid, 1, $path)";
          cmd.Parameters.Clear();
          cmd.Parameters.Add("mid", SqliteType.Integer);
          cmd.Parameters.Add("path", SqliteType.Integer).Value = proc1Path;
          foreach (var matId in matIds)
          {
            cmd.Parameters[0].Value = matId;
            cmd.ExecuteNonQuery();
          }
          trans.Commit();
          return matIds;
        }
      }
    }

    public void MarkCastingsAsUnallocated(IEnumerable<long> matIds, string casting)
    {
      lock (_cfg)
      {
        using (var trans = _connection.BeginTransaction())
        using (var cmd = _connection.CreateCommand())
        {
          cmd.Transaction = trans;

          cmd.CommandText =
            "UPDATE matdetails SET UniqueStr = NULL, PartName = $c WHERE MaterialID = $mid";
          cmd.Parameters.Clear();
          cmd.Parameters.Add("mid", SqliteType.Integer);
          cmd.Parameters.Add("c", SqliteType.Text).Value = casting;

          foreach (var matId in matIds)
          {
            cmd.Parameters[0].Value = matId;
            cmd.ExecuteNonQuery();
          }

          cmd.CommandText = "DELETE FROM mat_path_details WHERE MaterialID = $mid";
          cmd.Parameters.Clear();
          cmd.Parameters.Add("mid", SqliteType.Integer);

          foreach (var matId in matIds)
          {
            cmd.Parameters[0].Value = matId;
            cmd.ExecuteNonQuery();
          }
          trans.Commit();
        }
      }
    }

    public bool IsMaterialInQueue(long matId)
    {
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText = "SELECT COUNT(*) FROM queues WHERE MaterialID = $matid";
        cmd.Parameters.Add("matid", SqliteType.Integer).Value = matId;
        var ret = cmd.ExecuteScalar();
        return (ret != null && ret != DBNull.Value && (long)ret > 0);
      }
    }

    private void AddAdditionalDataToQueuedMaterial(
      SqliteTransaction trans,
      List<QueuedMaterial> mats
    )
    {
      using (var pathCmd = _connection.CreateCommand())
      using (var nextProcCmd = _connection.CreateCommand())
      {
        pathCmd.Transaction = trans;
        pathCmd.CommandText = "SELECT Process, Path FROM mat_path_details WHERE MaterialID = $mid";
        pathCmd.Parameters.Add("mid", SqliteType.Integer);

        nextProcCmd.Transaction = trans;
        nextProcCmd.CommandText = _nextProcessForQueuedMaterialSql;
        nextProcCmd.Parameters.Add("matid", SqliteType.Integer);

        for (var i = 0; i < mats.Count; i++)
        {
          var m = mats[i];
          pathCmd.Parameters[0].Value = m.MaterialID;
          var paths = ImmutableDictionary.CreateBuilder<int, int>();
          using (var pathReader = pathCmd.ExecuteReader())
          {
            while (pathReader.Read())
            {
              paths.Add(pathReader.GetInt32(0), pathReader.GetInt32(1));
            }
          }

          nextProcCmd.Parameters[0].Value = m.MaterialID;
          var nextProcObj = nextProcCmd.ExecuteScalar();
          int? nextProc;
          if (nextProcObj != null && nextProcObj != DBNull.Value)
          {
            nextProc = Convert.ToInt32(nextProcObj) + 1;
          }
          else
          {
            nextProc = null;
          }

          mats[i] = mats[i] with { Paths = paths.ToImmutable(), NextProcess = nextProc };
        }
      }
    }

    public IEnumerable<QueuedMaterial> GetMaterialInQueueByUnique(string queue, string unique)
    {
      var ret = new List<QueuedMaterial>();
      using (var trans = _connection.BeginTransaction())
      using (var cmd = _connection.CreateCommand())
      {
        cmd.Transaction = trans;

        cmd.CommandText =
          "SELECT q.MaterialID, q.Position, m.UniqueStr, m.PartName, m.NumProcesses, m.Serial, m.Workorder, q.AddTimeUTC "
          + " FROM queues q "
          + " LEFT OUTER JOIN matdetails m ON q.MaterialID = m.MaterialID "
          + " WHERE q.Queue = $q AND m.UniqueStr = $uniq "
          + " ORDER BY q.Position";
        cmd.Parameters.Add("q", SqliteType.Text).Value = queue;
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;

        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            ret.Add(
              new QueuedMaterial()
              {
                MaterialID = reader.GetInt64(0),
                Queue = queue,
                Position = reader.GetInt32(1),
                Unique = reader.IsDBNull(2) ? "" : reader.GetString(2),
                PartNameOrCasting = reader.IsDBNull(3) ? "" : reader.GetString(3),
                NumProcesses = reader.IsDBNull(4) ? 1 : reader.GetInt32(4),
                Serial = reader.IsDBNull(5) ? null : reader.GetString(5),
                Workorder = reader.IsDBNull(6) ? null : reader.GetString(6),
                AddTimeUTC = reader.IsDBNull(7)
                  ? null
                  : ((DateTime?)(new DateTime(reader.GetInt64(7), DateTimeKind.Utc))),
                // these are filled in by AddAdditionalDataToQueuedMaterial
                Paths = ImmutableDictionary<int, int>.Empty,
              }
            );
          }
        }

        AddAdditionalDataToQueuedMaterial(trans, ret);

        trans.Commit();
        return ret;
      }
    }

    public IEnumerable<QueuedMaterial> GetUnallocatedMaterialInQueue(
      string queue,
      string partNameOrCasting
    )
    {
      var ret = new List<QueuedMaterial>();
      using (var trans = _connection.BeginTransaction())
      using (var cmd = _connection.CreateCommand())
      using (var pathCmd = _connection.CreateCommand())
      {
        cmd.Transaction = trans;
        pathCmd.Transaction = trans;

        cmd.CommandText =
          "SELECT q.MaterialID, q.Position, m.UniqueStr, m.PartName, m.NumProcesses, m.Serial, m.Workorder, q.AddTimeUTC "
          + " FROM queues q "
          + " LEFT OUTER JOIN matdetails m ON q.MaterialID = m.MaterialID "
          + " WHERE q.Queue = $q AND m.UniqueStr IS NULL AND m.PartName = $part "
          + " ORDER BY q.Position";
        cmd.Parameters.Add("q", SqliteType.Text).Value = queue;
        cmd.Parameters.Add("part", SqliteType.Text).Value = partNameOrCasting;

        pathCmd.CommandText = "SELECT Path FROM mat_path_details WHERE MaterialID = $mid";

        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            ret.Add(
              new QueuedMaterial()
              {
                MaterialID = reader.GetInt64(0),
                Queue = queue,
                Position = reader.GetInt32(1),
                Unique = reader.IsDBNull(2) ? "" : reader.GetString(2),
                PartNameOrCasting = reader.IsDBNull(3) ? "" : reader.GetString(3),
                NumProcesses = reader.IsDBNull(4) ? 1 : reader.GetInt32(4),
                Serial = reader.IsDBNull(5) ? null : reader.GetString(5),
                Workorder = reader.IsDBNull(6) ? null : reader.GetString(6),
                AddTimeUTC = reader.IsDBNull(7)
                  ? null
                  : ((DateTime?)(new DateTime(reader.GetInt64(7), DateTimeKind.Utc))),
                // these are filled in by AddAdditionalDataToQueuedMaterial
                Paths = ImmutableDictionary<int, int>.Empty,
              }
            );
          }
        }

        AddAdditionalDataToQueuedMaterial(trans, ret);

        trans.Commit();
      }
      return ret;
    }

    public IEnumerable<QueuedMaterial> GetMaterialInAllQueues()
    {
      var ret = new List<QueuedMaterial>();
      using (var trans = _connection.BeginTransaction())
      using (var cmd = _connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText =
          "SELECT q.MaterialID, q.Queue, q.Position, m.UniqueStr, m.PartName, m.NumProcesses, m.Serial, m.Workorder, q.AddTimeUTC "
          + " FROM queues q "
          + " LEFT OUTER JOIN matdetails m ON q.MaterialID = m.MaterialID "
          + " ORDER BY q.Queue, q.Position";
        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            ret.Add(
              new QueuedMaterial()
              {
                MaterialID = reader.GetInt64(0),
                Queue = reader.GetString(1),
                Position = reader.GetInt32(2),
                Unique = reader.IsDBNull(3) ? "" : reader.GetString(3),
                PartNameOrCasting = reader.IsDBNull(4) ? "" : reader.GetString(4),
                NumProcesses = reader.IsDBNull(5) ? 1 : reader.GetInt32(5),
                Serial = reader.IsDBNull(6) ? null : reader.GetString(6),
                Workorder = reader.IsDBNull(7) ? null : reader.GetString(7),
                AddTimeUTC = reader.IsDBNull(8)
                  ? null
                  : ((DateTime?)(new DateTime(reader.GetInt64(8), DateTimeKind.Utc))),
                // these are filled in by AddAdditionalDataToQueuedMaterial
                Paths = ImmutableDictionary<int, int>.Empty,
              }
            );
          }
        }

        AddAdditionalDataToQueuedMaterial(trans, ret);
        trans.Commit();
        return ret;
      }
    }

    public int? NextProcessForQueuedMaterial(long matId)
    {
      using (var trans = _connection.BeginTransaction())
      {
        return NextProcessForQueuedMaterial(trans, matId);
      }
    }

    private static readonly string _nextProcessForQueuedMaterialSql =
      "SELECT MAX(m.Process) FROM "
      + "stations_mat m "
      + "INNER JOIN stations s ON m.Counter = s.Counter "
      + "LEFT OUTER JOIN program_details d ON m.Counter = d.Counter AND d.Key = 'PalletCycleInvalidated'"
      + "WHERE "
      + "  m.MaterialId = $matid "
      + " AND s.StationLoc IN ("
      + LogTypesToCheckForNextProcess
      + ") "
      + " AND d.Key IS NULL";

    private int? NextProcessForQueuedMaterial(IDbTransaction trans, long matId)
    {
      using (var loadCmd = _connection.CreateCommand())
      {
        ((IDbCommand)loadCmd).Transaction = trans;
        loadCmd.CommandText = _nextProcessForQueuedMaterialSql;
        loadCmd.Parameters.Add("matid", SqliteType.Integer).Value = matId;

        var val = loadCmd.ExecuteScalar();
        if (val != null && val != DBNull.Value)
        {
          return Convert.ToInt32(val) + 1;
        }
        else
        {
          return null;
        }
      }
    }
    #endregion

    #region Inspection Counts
    private static Random _rand = new Random();

    private InspectCount QueryCount(IDbTransaction trans, string counter, int maxVal)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "SELECT Val, LastUTC FROM inspection_counters WHERE Counter = $cntr";
        cmd.Parameters.Add("cntr", SqliteType.Text).Value = counter;

        using (IDataReader reader = cmd.ExecuteReader())
        {
          if (reader.Read())
          {
            return new InspectCount()
            {
              Counter = counter,
              Value = reader.GetInt32(0),
              LastUTC = reader.IsDBNull(1)
                ? DateTime.MaxValue
                : new DateTime(reader.GetInt64(1), DateTimeKind.Utc),
            };
          }
          else
          {
            return new InspectCount()
            {
              Counter = counter,
              Value = maxVal <= 1 ? 0 : _rand.Next(0, maxVal - 1),
              LastUTC = DateTime.MaxValue,
            };
          }
        }
      }
    }

    public List<InspectCount> LoadInspectCounts()
    {
      lock (_cfg)
      {
        List<InspectCount> ret = new List<InspectCount>();

        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "SELECT Counter, Val, LastUTC FROM inspection_counters";

          using (var reader = cmd.ExecuteReader())
          {
            while (reader.Read())
            {
              ret.Add(
                new InspectCount()
                {
                  Counter = reader.GetString(0),
                  Value = reader.GetInt32(1),
                  LastUTC = reader.IsDBNull(2)
                    ? DateTime.MaxValue
                    : new DateTime(reader.GetInt64(2), DateTimeKind.Utc),
                }
              );
            }
          }
        }

        return ret;
      }
    }

    private void SetInspectionCount(IDbTransaction trans, InspectCount cnt)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText =
          "INSERT OR REPLACE INTO inspection_counters(Counter,Val,LastUTC) VALUES ($cntr,$val,$time)";
        cmd.Parameters.Add("cntr", SqliteType.Text).Value = cnt.Counter;
        cmd.Parameters.Add("val", SqliteType.Integer).Value = cnt.Value;
        cmd.Parameters.Add("time", SqliteType.Integer).Value = cnt.LastUTC.Ticks;
        cmd.ExecuteNonQuery();
      }
    }

    public void SetInspectCounts(IEnumerable<InspectCount> counts)
    {
      lock (_cfg)
      {
        using (var trans = _connection.BeginTransaction())
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText =
            "INSERT OR REPLACE INTO inspection_counters(Counter, Val, LastUTC) VALUES ($cntr,$val,$last)";
          cmd.Parameters.Add("cntr", SqliteType.Text);
          cmd.Parameters.Add("val", SqliteType.Integer);
          cmd.Parameters.Add("last", SqliteType.Integer);

          cmd.Transaction = trans;

          foreach (var insp in counts)
          {
            cmd.Parameters[0].Value = insp.Counter;
            cmd.Parameters[1].Value = insp.Value;
            cmd.Parameters[2].Value = insp.LastUTC.Ticks;
            cmd.ExecuteNonQuery();
          }

          trans.Commit();
        }
      }
    }
    #endregion

    #region Inspection Translation
    private Dictionary<int, MaterialProcessActualPath> LookupActualPath(
      IDbTransaction trans,
      long matID
    )
    {
      var byProc = new Dictionary<int, MaterialProcessActualPath>();
      void adjustPath(int proc, Func<MaterialProcessActualPath, MaterialProcessActualPath> f)
      {
        if (byProc.TryGetValue(proc, out var value))
        {
          byProc[proc] = f(value);
        }
        else
        {
          var m = new MaterialProcessActualPath()
          {
            MaterialID = matID,
            Process = proc,
            Pallet = 0,
            LoadStation = -1,
            UnloadStation = -1,
            Stops = ImmutableList<MaterialProcessActualPath.Stop>.Empty,
          };
          byProc.Add(proc, f(m));
        }
      }

      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText =
          "SELECT Pallet, StationLoc, StationName, StationNum, Process "
          + " FROM stations "
          + " INNER JOIN stations_mat ON stations.Counter = stations_mat.Counter "
          + " WHERE "
          + "    MaterialID = $mat AND Start = 0 "
          + "    AND (StationLoc = $ty1 OR StationLoc = $ty2) "
          + " ORDER BY stations.Counter ASC";
        cmd.Parameters.Add("mat", SqliteType.Integer).Value = matID;
        cmd.Parameters.Add("ty1", SqliteType.Integer).Value = (int)LogType.LoadUnloadCycle;
        cmd.Parameters.Add("ty2", SqliteType.Integer).Value = (int)LogType.MachineCycle;

        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            //for each log entry, we search for a matching route stop in the job
            //if we find one, we replace the counter in the program
            int pal;
            if (reader.GetFieldType(0) == typeof(string))
            {
              int.TryParse(reader.GetString(0), out pal);
            }
            else
            {
              pal = reader.GetInt32(0);
            }
            var logTy = (LogType)reader.GetInt32(1);
            string statName = reader.GetString(2);
            int statNum = reader.GetInt32(3);
            int process = reader.GetInt32(4);

            adjustPath(
              process,
              mat =>
              {
                if (pal > 0)
                  mat = mat with { Pallet = pal };

                switch (logTy)
                {
                  case LogType.LoadUnloadCycle:
                    if (mat.LoadStation == -1)
                      mat = mat with { LoadStation = statNum };
                    else
                      mat = mat with { UnloadStation = statNum };
                    break;

                  case LogType.MachineCycle:
                    mat = mat with
                    {
                      Stops = mat.Stops.Add(
                        new MaterialProcessActualPath.Stop()
                        {
                          StationName = statName,
                          StationNum = statNum,
                        }
                      ),
                    };
                    break;
                }

                return mat;
              }
            );
          }
        }
      }

      return byProc;
    }

    private string TranslateInspectionCounter(
      long matID,
      Dictionary<int, MaterialProcessActualPath> actualPath,
      string counter
    )
    {
      foreach (var p in actualPath.Values)
      {
        counter = counter.Replace(PathInspection.PalletFormatFlag(p.Process), p.Pallet.ToString());
        counter = counter.Replace(
          PathInspection.LoadFormatFlag(p.Process),
          p.LoadStation.ToString()
        );
        counter = counter.Replace(
          PathInspection.UnloadFormatFlag(p.Process),
          p.UnloadStation.ToString()
        );
        for (int stopNum = 1; stopNum <= p.Stops.Count; stopNum++)
        {
          counter = counter.Replace(
            PathInspection.StationFormatFlag(p.Process, stopNum),
            p.Stops[stopNum - 1].StationNum.ToString()
          );
        }
      }
      return counter;
    }
    #endregion

    #region Inspection Decisions

    public IReadOnlyList<Decision> LookupInspectionDecisions(long matID)
    {
      using var trans = _connection.BeginTransaction();
      var ret = LookupInspectionDecisions(trans, new[] { matID });
      trans.Commit();
      return ret.GetValueOrDefault(matID, new Decision[] { });
    }

    public ImmutableDictionary<long, IReadOnlyList<Decision>> LookupInspectionDecisions(
      IEnumerable<long> matIDs
    )
    {
      using var trans = _connection.BeginTransaction();
      var ret = LookupInspectionDecisions(trans, matIDs);
      trans.Commit();
      return ret.ToImmutable();
    }

    private ImmutableDictionary<long, IReadOnlyList<Decision>>.Builder LookupInspectionDecisions(
      IDbTransaction trans,
      IEnumerable<long> matIDs
    )
    {
      var ret = ImmutableDictionary.CreateBuilder<long, IReadOnlyList<Decision>>();
      using (var detailCmd = _connection.CreateCommand())
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText =
          "SELECT Counter, StationLoc, Program, TimeUTC, Result "
          + " FROM stations "
          + " WHERE "
          + "    Counter IN (SELECT Counter FROM stations_mat WHERE MaterialID = $mat) "
          + "    AND (StationLoc = $loc1 OR StationLoc = $loc2) "
          + " ORDER BY Counter ASC";
        cmd.Parameters.Add("$mat", SqliteType.Integer);
        cmd.Parameters.Add("$loc1", SqliteType.Integer).Value = LogType.InspectionForce;
        cmd.Parameters.Add("$loc2", SqliteType.Integer).Value = LogType.Inspection;

        ((IDbCommand)detailCmd).Transaction = trans;
        detailCmd.CommandText =
          "SELECT Value FROM program_details WHERE Counter = $cntr AND Key = 'InspectionType'";
        detailCmd.Parameters.Add("cntr", SqliteType.Integer);

        foreach (var matId in matIDs)
        {
          cmd.Parameters[0].Value = matId;
          var decisions = new List<Decision>();

          using (var reader = cmd.ExecuteReader())
          {
            while (reader.Read())
            {
              var cntr = reader.GetInt64(0);
              var logTy = (LogType)reader.GetInt32(1);
              var prog = reader.GetString(2);
              var timeUtc = new DateTime(reader.GetInt64(3), DateTimeKind.Utc);
              var result = reader.GetString(4);
              var inspect = false;
              bool.TryParse(result, out inspect);

              if (logTy == LogType.Inspection)
              {
                detailCmd.Parameters[0].Value = cntr;
                var inspVal = detailCmd.ExecuteScalar();
                string inspType;
                if (inspVal != null)
                {
                  inspType = inspVal.ToString();
                }
                else
                {
                  // old code didn't record in details, so assume the counter is in a specific format
                  var parts = prog.Split(',');
                  if (parts.Length >= 2)
                    inspType = parts[1];
                  else
                    inspType = "";
                }
                decisions.Add(
                  new Decision()
                  {
                    MaterialID = matId,
                    InspType = inspType,
                    Counter = prog,
                    Inspect = inspect,
                    Forced = false,
                    CreateUTC = timeUtc,
                  }
                );
              }
              else
              {
                decisions.Add(
                  new Decision()
                  {
                    MaterialID = matId,
                    InspType = prog,
                    Counter = "",
                    Inspect = inspect,
                    Forced = true,
                    CreateUTC = timeUtc,
                  }
                );
              }
            }
          }

          ret.Add(matId, decisions);
        }
      }

      return ret;
    }

    public IEnumerable<LogEntry> MakeInspectionDecisions(
      long matID,
      int process,
      IEnumerable<PathInspection> inspections,
      DateTime? mutcNow = null
    )
    {
      return AddEntryInTransaction(trans =>
        MakeInspectionDecisions(trans, matID, process, inspections, mutcNow)
      );
    }

    private List<LogEntry> MakeInspectionDecisions(
      IDbTransaction trans,
      long matID,
      int process,
      IEnumerable<PathInspection> inspections,
      DateTime? mutcNow
    )
    {
      var utcNow = mutcNow ?? DateTime.UtcNow;
      var logEntries = new List<LogEntry>();

      var actualPath = LookupActualPath(trans, matID);

      var decisions = LookupInspectionDecisions(trans, new[] { matID })
        .GetValueOrDefault(matID, new Decision[] { })
        .ToLookup(d => d.InspType, d => d);

      Dictionary<string, PathInspection> insps;
      if (inspections == null)
        insps = new Dictionary<string, PathInspection>();
      else
        insps = inspections.ToDictionary(x => x.InspectionType, x => x);

      var inspsToCheck = decisions.Select(x => x.Key).Union(insps.Keys).Distinct();
      foreach (var inspType in inspsToCheck)
      {
        bool inspect = false;
        string counter = "";
        bool alreadyRecorded = false;

        PathInspection iProg = null;
        if (insps.ContainsKey(inspType))
        {
          iProg = insps[inspType];
          counter = TranslateInspectionCounter(matID, actualPath, iProg.Counter);
        }

        if (decisions.Contains(inspType))
        {
          // use the decision
          foreach (var d in decisions[inspType])
          {
            inspect = inspect || d.Inspect;
            alreadyRecorded = alreadyRecorded || !d.Forced;
          }
        }

        if (!alreadyRecorded && iProg != null)
        {
          // use the counter
          var currentCount = QueryCount(trans, counter, iProg.MaxVal);
          if (iProg.MaxVal > 0)
          {
            currentCount = currentCount with { Value = currentCount.Value + 1 };

            if (currentCount.Value >= iProg.MaxVal)
            {
              currentCount = currentCount with { Value = 0 };
              inspect = true;
            }
          }
          else if (iProg.RandomFreq > 0)
          {
            if (_rand.NextDouble() < iProg.RandomFreq)
              inspect = true;
          }

          //now check lastutc
          if (
            iProg.TimeInterval > TimeSpan.Zero
            && currentCount.LastUTC != DateTime.MaxValue
            && currentCount.LastUTC.Add(iProg.TimeInterval) < utcNow
          )
          {
            inspect = true;
          }

          //update lastutc if there is an inspection
          if (inspect)
            currentCount = currentCount with { LastUTC = utcNow };

          //if no lastutc has been recoreded, record the current time.
          if (currentCount.LastUTC == DateTime.MaxValue)
            currentCount = currentCount with { LastUTC = utcNow };

          SetInspectionCount(trans, currentCount);
        }

        if (!alreadyRecorded)
        {
          var log = StoreInspectionDecision(
            trans,
            matID,
            process,
            actualPath,
            inspType,
            counter,
            utcNow,
            inspect
          );
          logEntries.Add(log);
        }
      }

      return logEntries;
    }

    public LogEntry StoreInspectionDecision(
      long matID,
      int proc,
      PathInspection insp,
      bool inspect,
      DateTime? utcNow = null
    )
    {
      return AddEntryInTransaction(trans =>
        StoreInspectionDecision(
          trans: trans,
          matID: matID,
          proc: proc,
          actualPath: LookupActualPath(trans, matID),
          inspType: insp.InspectionType,
          counter: insp.Counter,
          utcNow: utcNow ?? DateTime.UtcNow,
          inspect: inspect
        )
      );
    }

    private LogEntry StoreInspectionDecision(
      IDbTransaction trans,
      long matID,
      int proc,
      Dictionary<int, MaterialProcessActualPath> actualPath,
      string inspType,
      string counter,
      DateTime utcNow,
      bool inspect
    )
    {
      var mat = new EventLogMaterial()
      {
        MaterialID = matID,
        Process = proc,
        Face = 0,
      };
      var pathSteps = actualPath.Values.OrderBy(p => p.Process).ToList();

      var log = new NewEventLogEntry()
      {
        Material = new[] { mat },
        Pallet = 0,
        LogType = LogType.Inspection,
        LocationName = "Inspect",
        LocationNum = 1,
        Program = counter,
        StartOfCycle = false,
        EndTimeUTC = utcNow,
        Result = inspect.ToString(),
      };

      log.ProgramDetails["InspectionType"] = inspType;
      log.ProgramDetails["ActualPath"] = System.Text.Json.JsonSerializer.Serialize(pathSteps);

      return AddLogEntry(trans, log, null, null);
    }

    #endregion

    #region Force and Next Piece Inspection
    public LogEntry ForceInspection(long matID, string inspType)
    {
      var mat = new EventLogMaterial()
      {
        MaterialID = matID,
        Process = 1,
        Face = 0,
      };
      return ForceInspection(mat, inspType, inspect: true, utcNow: DateTime.UtcNow);
    }

    public LogEntry ForceInspection(long materialID, int process, string inspType, bool inspect)
    {
      var mat = new EventLogMaterial()
      {
        MaterialID = materialID,
        Process = process,
        Face = 0,
      };
      return ForceInspection(mat, inspType, inspect, DateTime.UtcNow);
    }

    public LogEntry ForceInspection(EventLogMaterial mat, string inspType, bool inspect)
    {
      return ForceInspection(mat, inspType, inspect, DateTime.UtcNow);
    }

    public LogEntry ForceInspection(
      EventLogMaterial mat,
      string inspType,
      bool inspect,
      DateTime utcNow
    )
    {
      return AddEntryInTransaction(trans =>
        RecordForceInspection(trans, mat, inspType, inspect, utcNow)
      );
    }

    private LogEntry RecordForceInspection(
      IDbTransaction trans,
      EventLogMaterial mat,
      string inspType,
      bool inspect,
      DateTime utcNow
    )
    {
      var log = new NewEventLogEntry()
      {
        Material = new[] { mat },
        Pallet = 0,
        LogType = LogType.InspectionForce,
        LocationName = "Inspect",
        LocationNum = 1,
        Program = inspType,
        StartOfCycle = false,
        EndTimeUTC = utcNow,
        Result = inspect.ToString(),
      };
      return AddLogEntry(trans, log, null, null);
    }

    public void NextPieceInspection(PalletLocation palLoc, string inspType)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText =
            "INSERT OR REPLACE INTO inspection_next_piece(StatType, StatNum, InspType)"
            + " VALUES ($loc,$locnum,$insp)";
          cmd.Parameters.Add("loc", SqliteType.Integer).Value = (int)palLoc.Location;
          cmd.Parameters.Add("locnum", SqliteType.Integer).Value = palLoc.Num;
          cmd.Parameters.Add("insp", SqliteType.Text).Value = inspType;

          cmd.ExecuteNonQuery();
        }
      }
    }

    public void CheckMaterialForNextPeiceInspection(PalletLocation palLoc, long matID)
    {
      var logs = new List<LogEntry>();

      lock (_cfg)
      {
        using (var trans = _connection.BeginTransaction())
        using (var cmd = _connection.CreateCommand())
        using (var cmd2 = _connection.CreateCommand())
        {
          cmd.CommandText =
            "SELECT InspType FROM inspection_next_piece WHERE StatType = $loc AND StatNum = $locnum";
          cmd.Parameters.Add("loc", SqliteType.Integer).Value = (int)palLoc.Location;
          cmd.Parameters.Add("locnum", SqliteType.Integer).Value = palLoc.Num;

          cmd.Transaction = trans;

          using (IDataReader reader = cmd.ExecuteReader())
          {
            var now = DateTime.UtcNow;
            while (reader.Read())
            {
              if (!reader.IsDBNull(0))
              {
                var mat = new EventLogMaterial()
                {
                  MaterialID = matID,
                  Process = 1,
                  Face = 0,
                };
                logs.Add(
                  RecordForceInspection(trans, mat, reader.GetString(0), inspect: true, utcNow: now)
                );
              }
            }
          }

          cmd.CommandText =
            "DELETE FROM inspection_next_piece WHERE StatType = $loc AND StatNum = $locnum";
          //keep the same parameters as above
          cmd.ExecuteNonQuery();

          trans.Commit();
        }

        foreach (var log in logs)
          _cfg.OnNewLogEntry(log, null, this);
      }
    }
    #endregion
  }
}
