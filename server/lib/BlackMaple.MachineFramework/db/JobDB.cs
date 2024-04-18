/* Copyright (c) 2021, John Lenz

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
using System.Collections.Immutable;
using System.Data;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace BlackMaple.MachineFramework
{
  //database backend for the job db
  internal partial class Repository
  {
    #region "Loading Jobs"
    private record PathStopRow
    {
      public required string StationGroup { get; init; }
      public ImmutableList<int>.Builder Stations { get; } = ImmutableList.CreateBuilder<int>();
      public required string Program { get; init; }
      public required long? ProgramRevision { get; init; }
      public ImmutableDictionary<string, TimeSpan>.Builder Tools { get; } =
        ImmutableDictionary.CreateBuilder<string, TimeSpan>();
      public required TimeSpan ExpectedCycleTime { get; init; }
    }

    private record HoldRow
    {
      public required bool UserHold { get; init; }
      public required string ReasonForUserHold { get; init; }
      public ImmutableList<TimeSpan>.Builder HoldUnholdPattern { get; } =
        ImmutableList.CreateBuilder<TimeSpan>();
      public required DateTime HoldUnholdPatternStartUTC { get; init; }
      public required bool HoldUnholdPatternRepeats { get; init; }

      public HoldPattern ToHoldPattern()
      {
        return new HoldPattern()
        {
          UserHold = this.UserHold,
          ReasonForUserHold = this.ReasonForUserHold,
          HoldUnholdPattern = this.HoldUnholdPattern.ToImmutable(),
          HoldUnholdPatternRepeats = this.HoldUnholdPatternRepeats,
          HoldUnholdPatternStartUTC = this.HoldUnholdPatternStartUTC
        };
      }
    }

    private record PathDataRow
    {
      public required int Process { get; init; }
      public required int Path { get; init; }
      public required DateTime StartingUTC { get; init; }
      public required int PartsPerPallet { get; init; }
      public required TimeSpan SimAverageFlowTime { get; init; }
      public required string InputQueue { get; init; }
      public required string OutputQueue { get; init; }
      public required TimeSpan LoadTime { get; init; }
      public required TimeSpan UnloadTime { get; init; }
      public required string Fixture { get; init; }
      public required int? Face { get; init; }
      public required string Casting { get; init; }
      public ImmutableList<int>.Builder Loads { get; } = ImmutableList.CreateBuilder<int>();
      public ImmutableList<int>.Builder Unloads { get; } = ImmutableList.CreateBuilder<int>();
      public ImmutableList<PathInspection>.Builder Insps { get; } =
        ImmutableList.CreateBuilder<PathInspection>();
      public ImmutableList<int>.Builder Pals { get; } = ImmutableList.CreateBuilder<int>();
      public ImmutableList<SimulatedProduction>.Builder SimProd { get; } =
        ImmutableList.CreateBuilder<SimulatedProduction>();
      public SortedList<int, PathStopRow> Stops { get; } = new SortedList<int, PathStopRow>();
      public HoldRow MachHold { get; set; } = null;
      public HoldRow LoadHold { get; set; } = null;
    }

    private record JobDetails
    {
      public required ImmutableList<int> CyclesOnFirstProc { get; init; }
      public required ImmutableList<string> Bookings { get; init; }
      public required ImmutableList<ProcessInfo> Procs { get; init; }
      public required HoldRow Hold { get; init; }
    }

    private record SimStatUseKey : IComparable<SimStatUseKey>
    {
      public required string ScheduleId { get; init; }
      public required DateTime StartUTC { get; init; }
      public required DateTime EndUTC { get; init; }
      public required string StationGroup { get; init; }
      public required int StationNum { get; init; }

      public int CompareTo(SimStatUseKey other)
      {
        return (this.ScheduleId, this.StartUTC, this.EndUTC, this.StationGroup, this.StationNum).CompareTo(
          (other.ScheduleId, other.StartUTC, other.EndUTC, other.StationGroup, other.StationNum)
        );
      }
    }

    private record SimStatUseRow
    {
      public required bool PlanDown { get; init; }
      public ImmutableList<SimulatedStationPart>.Builder Parts { get; } =
        ImmutableList.CreateBuilder<SimulatedStationPart>();
    }

    private JobDetails LoadJobData(string uniq, IDbTransaction trans)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.Parameters.Add("$uniq", SqliteType.Text).Value = uniq;

        //read plan quantity
        cmd.CommandText = "SELECT Path, PlanQty FROM planqty WHERE UniqueStr = $uniq";
        var cyclesOnFirstProc = new SortedDictionary<int, int>();
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            cyclesOnFirstProc[reader.GetInt32(0)] = reader.GetInt32(1);
          }
        }

        //scheduled bookings
        cmd.CommandText = "SELECT BookingId FROM scheduled_bookings WHERE UniqueStr = $uniq";
        var bookings = ImmutableList.CreateBuilder<string>();
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            bookings.Add(reader.GetString(0));
          }
        }

        //path data
        var pathDatRows = new Dictionary<(int proc, int path), PathDataRow>();
        cmd.CommandText =
          "SELECT Process, Path, StartingUTC, PartsPerPallet, SimAverageFlowTime, InputQueue, OutputQueue, LoadTime, UnloadTime, Fixture, Face, Casting FROM pathdata WHERE UniqueStr = $uniq";
        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var proc = reader.GetInt32(0);
            var path = reader.GetInt32(1);

            string fixture = null;
            int? face = null;
            if (!reader.IsDBNull(9) && !reader.IsDBNull(10))
            {
              var faceTy = reader.GetFieldType(10);
              if (faceTy == typeof(string))
              {
                if (int.TryParse(reader.GetString(10), out int f))
                {
                  fixture = reader.GetString(9);
                  face = f;
                }
              }
              else
              {
                fixture = reader.GetString(9);
                face = reader.GetInt32(10);
              }
            }

            pathDatRows[(proc, path)] = new PathDataRow()
            {
              Process = proc,
              Path = path,
              StartingUTC = new DateTime(reader.GetInt64(2), DateTimeKind.Utc),
              PartsPerPallet = reader.GetInt32(3),
              SimAverageFlowTime = reader.IsDBNull(4)
                ? TimeSpan.Zero
                : TimeSpan.FromTicks(reader.GetInt64(4)),
              InputQueue = reader.IsDBNull(5) ? null : reader.GetString(5),
              OutputQueue = reader.IsDBNull(6) ? null : reader.GetString(6),
              LoadTime = reader.IsDBNull(7) ? TimeSpan.Zero : TimeSpan.FromTicks(reader.GetInt64(7)),
              UnloadTime = reader.IsDBNull(8) ? TimeSpan.Zero : TimeSpan.FromTicks(reader.GetInt64(8)),
              Fixture = fixture,
              Face = face,
              Casting = reader.IsDBNull(11) ? null : reader.GetString(11)
            };
          }
        }

        //read pallets
        cmd.CommandText = "SELECT Process, Path, Pallet FROM pallets WHERE UniqueStr = $uniq";
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            if (
              pathDatRows.TryGetValue((proc: reader.GetInt32(0), path: reader.GetInt32(1)), out var pathRow)
            )
            {
              // old versions of insight stored pallets as strings
              var palTy = reader.GetFieldType(2);
              if (palTy == typeof(string))
              {
                if (int.TryParse(reader.GetString(2), out int p))
                {
                  pathRow.Pals.Add(p);
                }
              }
              else
              {
                pathRow.Pals.Add(reader.GetInt32(2));
              }
            }
          }
        }

        //simulated production
        cmd.CommandText =
          "SELECT Process, Path, TimeUTC, Quantity FROM simulated_production WHERE UniqueStr = $uniq ORDER BY Process,Path,TimeUTC";
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var key = (proc: reader.GetInt32(0), path: reader.GetInt32(1));
            if (pathDatRows.TryGetValue(key, out var pathRow))
            {
              pathRow.SimProd.Add(
                new SimulatedProduction()
                {
                  TimeUTC = new DateTime(reader.GetInt64(2), DateTimeKind.Utc),
                  Quantity = reader.GetInt32(3),
                }
              );
            }
          }
        }

        //now add routes
        cmd.CommandText =
          "SELECT Process, Path, RouteNum, StatGroup, ExpectedCycleTime, Program, ProgramRevision FROM stops WHERE UniqueStr = $uniq";
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var key = (proc: reader.GetInt32(0), path: reader.GetInt32(1));
            int routeNum = reader.GetInt32(2);

            if (pathDatRows.TryGetValue(key, out var pathRow))
            {
              var stop = new PathStopRow()
              {
                StationGroup = reader.GetString(3),
                ExpectedCycleTime = reader.IsDBNull(4)
                  ? TimeSpan.Zero
                  : TimeSpan.FromTicks(reader.GetInt64(4)),
                Program = reader.IsDBNull(5) ? null : reader.GetString(5),
                ProgramRevision = reader.IsDBNull(6) ? null : reader.GetInt64(6),
              };
              pathRow.Stops[routeNum] = stop;
            }
          }
        }

        //programs for routes
        cmd.CommandText =
          "SELECT Process, Path, RouteNum, StatNum FROM stops_stations WHERE UniqueStr = $uniq";
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var key = (proc: reader.GetInt32(0), path: reader.GetInt32(1));
            int routeNum = reader.GetInt32(2);
            if (pathDatRows.TryGetValue(key, out var pathRow))
            {
              if (pathRow.Stops.TryGetValue(routeNum, out var stop))
              {
                stop.Stations.Add(reader.GetInt32(3));
              }
            }
          }
        }

        //tools for routes
        cmd.CommandText =
          "SELECT Process, Path, RouteNum, Tool, ExpectedUse FROM tools WHERE UniqueStr = $uniq";
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var key = (proc: reader.GetInt32(0), path: reader.GetInt32(1));
            int routeNum = reader.GetInt32(2);
            if (pathDatRows.TryGetValue(key, out var pathRow))
            {
              if (pathRow.Stops.TryGetValue(routeNum, out var stop))
              {
                stop.Tools[reader.GetString(3)] = TimeSpan.FromTicks(reader.GetInt64(4));
              }
            }
          }
        }

        //now add load/unload
        cmd.CommandText = "SELECT Process, Path, StatNum, Load FROM loadunload WHERE UniqueStr = $uniq";
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var key = (proc: reader.GetInt32(0), path: reader.GetInt32(1));
            if (pathDatRows.TryGetValue(key, out var pathData))
            {
              if (reader.GetBoolean(3))
              {
                pathData.Loads.Add(reader.GetInt32(2));
              }
              else
              {
                pathData.Unloads.Add(reader.GetInt32(2));
              }
            }
          }
        }

        //now inspections
        cmd.CommandText =
          "SELECT Process, Path, InspType, Counter, MaxVal, TimeInterval, RandomFreq, ExpectedTime FROM path_inspections WHERE UniqueStr = $uniq";
        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var insp = new PathInspection()
            {
              InspectionType = reader.GetString(2),
              Counter = reader.GetString(3),
              MaxVal = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
              TimeInterval = reader.IsDBNull(5) ? TimeSpan.Zero : TimeSpan.FromTicks(reader.GetInt64(5)),
              RandomFreq = reader.IsDBNull(6) ? 0 : reader.GetDouble(6),
              ExpectedInspectionTime = reader.IsDBNull(7)
                ? null
                : (TimeSpan?)TimeSpan.FromTicks(reader.GetInt64(7))
            };

            var proc = reader.GetInt32(0);
            var path = reader.GetInt32(1);

            if (path < 1)
            {
              // all paths
              foreach (var pathData in pathDatRows.Values.Where(p => p.Process == proc))
              {
                pathData.Insps.Add(insp);
              }
            }
            else
            {
              // single path
              if (pathDatRows.TryGetValue((proc, path), out var pathData))
              {
                pathData.Insps.Add(insp);
              }
            }
          }
        }

        //hold
        HoldRow jobHold = null;
        cmd.CommandText =
          "SELECT Process, Path, LoadUnload, UserHold, UserHoldReason, HoldPatternStartUTC, HoldPatternRepeats FROM holds WHERE UniqueStr = $uniq";
        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            int proc = reader.GetInt32(0);
            int path = reader.GetInt32(1);
            bool load = reader.GetBoolean(2);

            var hold = new HoldRow()
            {
              UserHold = reader.GetBoolean(3),
              ReasonForUserHold = reader.GetString(4),
              HoldUnholdPatternStartUTC = new DateTime(reader.GetInt64(5), DateTimeKind.Utc),
              HoldUnholdPatternRepeats = reader.GetBoolean(6),
            };

            if (proc < 0)
            {
              jobHold = hold;
            }
            else if (load)
            {
              if (pathDatRows.TryGetValue((proc, path), out var pathRow))
              {
                pathRow.LoadHold = hold;
              }
            }
            else
            {
              if (pathDatRows.TryGetValue((proc, path), out var pathRow))
              {
                pathRow.MachHold = hold;
              }
            }
          }
        }

        //hold pattern
        cmd.CommandText =
          "SELECT Process, Path, LoadUnload, Span FROM hold_pattern WHERE UniqueStr = $uniq ORDER BY Idx ASC";
        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            int proc = reader.GetInt32(0);
            int path = reader.GetInt32(1);
            bool load = reader.GetBoolean(2);
            var time = TimeSpan.FromTicks(reader.GetInt64(3));
            if (proc < 0)
            {
              jobHold?.HoldUnholdPattern.Add(time);
            }
            else if (load)
            {
              if (pathDatRows.TryGetValue((proc, path), out var pathRow))
              {
                pathRow.LoadHold?.HoldUnholdPattern.Add(time);
              }
            }
            else
            {
              if (pathDatRows.TryGetValue((proc, path), out var pathRow))
              {
                pathRow.MachHold?.HoldUnholdPattern.Add(time);
              }
            }
          }
        }

        return new JobDetails()
        {
          Hold = jobHold,
          CyclesOnFirstProc = cyclesOnFirstProc.Values.ToImmutableList(),
          Bookings = bookings.ToImmutable(),
          Procs = pathDatRows
            .Values.GroupBy(p => p.Process)
            .Select(proc => new ProcessInfo()
            {
              Paths = proc.OrderBy(p => p.Path)
                .Select(p => new ProcPathInfo()
                {
                  PalletNums = p.Pals.ToImmutable(),
                  Fixture = p.Fixture,
                  Face = p.Face,
                  Load = p.Loads.ToImmutable(),
                  ExpectedLoadTime = p.LoadTime,
                  Unload = p.Unloads.ToImmutable(),
                  ExpectedUnloadTime = p.UnloadTime,
                  Stops = p
                    .Stops.Values.Select(s => new MachiningStop()
                    {
                      StationGroup = s.StationGroup,
                      Stations = s.Stations.ToImmutable(),
                      Program = s.Program,
                      ProgramRevision = s.ProgramRevision,
                      Tools = s.Tools.Count == 0 ? null : s.Tools.ToImmutable(),
                      ExpectedCycleTime = s.ExpectedCycleTime
                    })
                    .ToImmutableList(),
                  SimulatedProduction = p.SimProd.ToImmutable(),
                  SimulatedStartingUTC = p.StartingUTC,
                  SimulatedAverageFlowTime = p.SimAverageFlowTime,
                  HoldMachining = p.MachHold?.ToHoldPattern(),
                  HoldLoadUnload = p.LoadHold?.ToHoldPattern(),
                  PartsPerPallet = p.PartsPerPallet,
                  InputQueue = p.InputQueue,
                  OutputQueue = p.OutputQueue,
                  Inspections = p.Insps.Count == 0 ? null : p.Insps.ToImmutable(),
                  Casting = p.Casting
                })
                .ToImmutableList()
            })
            .ToImmutableList()
        };
      }
    }

    private ImmutableList<HistoricJob> LoadJobsHelper(IDbCommand cmd, IDbTransaction trans)
    {
      var ret = ImmutableList.CreateBuilder<HistoricJob>();
      using (IDataReader reader = cmd.ExecuteReader())
      {
        while (reader.Read())
        {
          string unique = reader.GetString(0);
          var details = LoadJobData(unique, trans);

          ret.Add(
            new HistoricJob()
            {
              UniqueStr = unique,
              PartName = reader.GetString(1),
              Comment = reader.IsDBNull(3) ? "" : reader.GetString(3),
              RouteStartUTC = reader.IsDBNull(4)
                ? DateTime.MinValue
                : new DateTime(reader.GetInt64(4), DateTimeKind.Utc),
              RouteEndUTC = reader.IsDBNull(5)
                ? DateTime.MaxValue
                : new DateTime(reader.GetInt64(5), DateTimeKind.Utc),
              Archived = reader.GetBoolean(6),
              CopiedToSystem = reader.IsDBNull(7) ? false : reader.GetBoolean(7),
              ScheduleId = reader.IsDBNull(8) ? null : reader.GetString(8),
              ManuallyCreated = !reader.IsDBNull(9) && reader.GetBoolean(9),
              AllocationAlgorithm = reader.IsDBNull(10) ? null : reader.GetString(10),
              Cycles = details.CyclesOnFirstProc.Sum(),
              Processes = details.Procs,
              BookingIds = details.Bookings,
              HoldJob = details.Hold?.ToHoldPattern(),
              Decrements = LoadDecrementsForJob(trans, unique)
            }
          );
        }
      }

      return ret.ToImmutable();
    }

    private ImmutableDictionary<string, int> LoadExtraParts(IDbTransaction trans, string schId)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        var ret = ImmutableDictionary.CreateBuilder<string, int>();
        cmd.CommandText = "SELECT Part, Quantity FROM scheduled_parts WHERE ScheduleId == $sid";
        cmd.Parameters.Add("sid", SqliteType.Text).Value = schId;
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            ret.Add(reader.GetString(0), reader.GetInt32(1));
          }
        }
        return ret.ToImmutable();
      }
    }

    private string LatestJobScheduleId(IDbTransaction trans)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText =
          "SELECT MAX(ScheduleId) FROM jobs WHERE ScheduleId IS NOT NULL AND (Manual IS NULL OR Manual == 0)";

        string tag = "";

        object val = cmd.ExecuteScalar();
        if ((val != null))
        {
          tag = val.ToString();
        }

        return tag;
      }
    }

    private void CheckScheduleIdDoesNotExist(IDbTransaction trans, string schID)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText = "SELECT 1 FROM jobs WHERE ScheduleId = $sid LIMIT 1";
        cmd.Parameters.Add("sid", SqliteType.Text).Value = schID;

        object val = cmd.ExecuteScalar();
        if (val != null)
        {
          throw new BadRequestException($"Schedule ID {schID} already exists!");
        }
      }
    }

    private void EnsureScheduleIdsExist(IDbTransaction trans, IEnumerable<string> schIDs)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText =
          "SELECT EXISTS(SELECT 1 FROM jobs WHERE ScheduleId = $sid LIMIT 1) OR "
          + "EXISTS(SELECT 1 FROM unfilled_workorders WHERE ScheduleId = $sid LIMIT 1) OR "
          + "EXISTS(SELECT 1 FROM sim_day_usage WHERE SimId = $sid LIMIT 1)";
        var param = cmd.Parameters.Add("sid", SqliteType.Text);

        foreach (var schId in schIDs)
        {
          param.Value = schId;

          object val = cmd.ExecuteScalar();
          if (val == null || val == DBNull.Value || ((long)val == 0))
          {
            throw new ConflictRequestException($"Schedule ID {schId} does not exist");
          }
        }
      }
    }

    private ImmutableList<SimulatedStationUtilization> LoadSimulatedStationUse(
      IDbCommand cmd,
      IDbCommand partCmd
    )
    {
      var rows = new SortedDictionary<SimStatUseKey, SimStatUseRow>();

      using (var reader = cmd.ExecuteReader())
      {
        while (reader.Read())
        {
          var key = new SimStatUseKey()
          {
            ScheduleId = reader.GetString(0),
            StationGroup = reader.GetString(1),
            StationNum = reader.GetInt32(2),
            StartUTC = new DateTime(reader.GetInt64(3), DateTimeKind.Utc),
            EndUTC = new DateTime(reader.GetInt64(4), DateTimeKind.Utc),
          };
          var row = new SimStatUseRow() { PlanDown = !reader.IsDBNull(5) && reader.GetBoolean(5) };
          rows.Add(key, row);
        }
      }

      using (var reader = partCmd.ExecuteReader())
      {
        while (reader.Read())
        {
          var key = new SimStatUseKey()
          {
            ScheduleId = reader.GetString(0),
            StationGroup = reader.GetString(1),
            StationNum = reader.GetInt32(2),
            StartUTC = new DateTime(reader.GetInt64(3), DateTimeKind.Utc),
            EndUTC = new DateTime(reader.GetInt64(4), DateTimeKind.Utc),
          };
          if (rows.TryGetValue(key, out var row))
          {
            row.Parts.Add(
              new SimulatedStationPart()
              {
                JobUnique = reader.GetString(5),
                Process = reader.GetInt32(6),
                Path = reader.GetInt32(7),
              }
            );
          }
        }
      }

      return rows.Select(e => new SimulatedStationUtilization()
        {
          ScheduleId = e.Key.ScheduleId,
          StationGroup = e.Key.StationGroup,
          StationNum = e.Key.StationNum,
          StartUTC = e.Key.StartUTC,
          EndUTC = e.Key.EndUTC,
          PlanDown = e.Value.PlanDown ? true : null,
          Parts = e.Value.Parts.Count == 0 ? null : e.Value.Parts.ToImmutable()
        })
        .ToImmutableList();
    }

    private ImmutableList<SimulatedDayUsage> LoadSimDayUsage(string simId, SqliteTransaction trans)
    {
      if (string.IsNullOrEmpty(simId))
        return null;

      using var cmd = _connection.CreateCommand();
      cmd.Transaction = trans;
      cmd.CommandText = "SELECT Day, Station, Usage FROM sim_day_usage WHERE SimId = $simId";
      cmd.Parameters.Add("simId", SqliteType.Text).Value = simId;

      var ret = ImmutableList.CreateBuilder<SimulatedDayUsage>();
      using var reader = cmd.ExecuteReader();
      while (reader.Read())
      {
        ret.Add(
          new SimulatedDayUsage()
          {
            Day = DateOnly.FromDayNumber(reader.GetInt32(0)),
            MachineGroup = reader.GetString(1),
            Usage = reader.GetDouble(2)
          }
        );
      }
      return ret.Count > 0 ? ret.ToImmutable() : null;
    }

    private string LoadSimDayUsageWarning(string simId, SqliteTransaction trans)
    {
      if (string.IsNullOrEmpty(simId))
        return null;

      using var cmd = _connection.CreateCommand();
      cmd.Transaction = trans;
      cmd.CommandText = "SELECT Warning FROM sim_day_usage_warning WHERE SimId = $simId";
      cmd.Parameters.Add("simId", SqliteType.Text).Value = simId;

      var msg = cmd.ExecuteScalar();

      if (msg != null && msg != DBNull.Value)
      {
        return (string)msg;
      }
      else
      {
        return null;
      }
    }

    private RecentHistoricData LoadHistory(
      SqliteCommand createTempCmd,
      bool includeRecentSimDayUsage,
      IEnumerable<string> alreadyKnownSchIds
    )
    {
      using (var trans = _connection.BeginTransaction())
      {
        if (alreadyKnownSchIds != null)
        {
          EnsureScheduleIdsExist(trans, alreadyKnownSchIds);
        }

        createTempCmd.Transaction = trans;
        createTempCmd.ExecuteNonQuery();

        string simIdForDayUsage = null;
        if (includeRecentSimDayUsage)
        {
          using (var maxSimId = _connection.CreateCommand())
          {
            maxSimId.Transaction = trans;
            maxSimId.CommandText = "SELECT MAX(SimId) FROM sim_day_usage";
            var max = maxSimId.ExecuteScalar();
            if (
              max != null
              && max != DBNull.Value
              && (alreadyKnownSchIds == null || !alreadyKnownSchIds.Contains(max))
            )
            {
              simIdForDayUsage = (string)max;
            }
          }
        }

        using var jobCmd = _connection.CreateCommand();
        jobCmd.Transaction = trans;
        jobCmd.CommandText =
          "SELECT UniqueStr, Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual, AllocateAlg"
          + " FROM jobs WHERE ScheduleId IN temp_sch_ids";

        using var simCmd = _connection.CreateCommand();
        simCmd.Transaction = trans;
        simCmd.CommandText =
          "SELECT SimId, StationGroup, StationNum, StartUTC, EndUTC, PlanDown "
          + " FROM sim_station_use WHERE SimId IN temp_sch_ids";

        using var simPartCmd = _connection.CreateCommand();
        simPartCmd.Transaction = trans;
        simPartCmd.CommandText =
          "SELECT SimId, StationGroup, StationNum, StartUTC, EndUTC, JobUnique, Process, Path "
          + " FROM sim_station_use_parts WHERE SimId IN temp_sch_ids";

        if (alreadyKnownSchIds != null)
        {
          using (var delSchIds = _connection.CreateCommand())
          {
            delSchIds.Transaction = trans;
            delSchIds.CommandText = "DELETE FROM temp_sch_ids WHERE ScheduleId = $schId";
            delSchIds.Parameters.Add("schId", SqliteType.Text);
            foreach (var schId in alreadyKnownSchIds)
            {
              delSchIds.Parameters["schId"].Value = schId;
              delSchIds.ExecuteNonQuery();
            }
          }
        }

        var history = new RecentHistoricData()
        {
          Jobs = LoadJobsHelper(jobCmd, trans).ToImmutableDictionary(j => j.UniqueStr),
          StationUse = LoadSimulatedStationUse(simCmd, simPartCmd),
          MostRecentSimulationId = simIdForDayUsage,
          MostRecentSimDayUsage = LoadSimDayUsage(simIdForDayUsage, trans),
          MostRecentSimDayUsageWarning = LoadSimDayUsageWarning(simIdForDayUsage, trans)
        };

        trans.Rollback();

        return history;
      }
    }

    // --------------------------------------------------------------------------------
    // Public Loading API
    // --------------------------------------------------------------------------------

    public IReadOnlyList<HistoricJob> LoadUnarchivedJobs()
    {
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText =
          "SELECT UniqueStr, Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual, AllocateAlg FROM jobs WHERE Archived = 0";
        using (var trans = _connection.BeginTransaction())
        {
          cmd.Transaction = trans;
          return LoadJobsHelper(cmd, trans);
        }
      }
    }

    public IReadOnlyList<HistoricJob> LoadJobsNotCopiedToSystem(
      DateTime startUTC,
      DateTime endUTC,
      bool includeDecremented = true
    )
    {
      var cmdTxt =
        "SELECT UniqueStr, Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual, AllocateAlg"
        + " FROM jobs WHERE StartUTC <= $end AND EndUTC >= $start AND CopiedToSystem = 0";
      if (!includeDecremented)
      {
        cmdTxt +=
          " AND NOT EXISTS(SELECT 1 FROM job_decrements WHERE job_decrements.JobUnique = jobs.UniqueStr)";
      }
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText = cmdTxt;
        cmd.Parameters.Add("start", SqliteType.Integer).Value = startUTC.Ticks;
        cmd.Parameters.Add("end", SqliteType.Integer).Value = endUTC.Ticks;
        using (var trans = _connection.BeginTransaction())
        {
          cmd.Transaction = trans;
          return LoadJobsHelper(cmd, trans);
        }
      }
    }

    public HistoricData LoadJobHistory(
      DateTime startUTC,
      DateTime endUTC,
      IEnumerable<string> alreadyKnownSchIds = null
    )
    {
      using (var createTempCmd = _connection.CreateCommand())
      {
        createTempCmd.CommandText =
          "CREATE TEMP TABLE temp_sch_ids AS SELECT DISTINCT ScheduleId FROM jobs WHERE StartUTC <= $end AND EndUTC >= $start AND ScheduleId IS NOT NULL";
        createTempCmd.Parameters.Add("start", SqliteType.Integer).Value = startUTC.Ticks;
        createTempCmd.Parameters.Add("end", SqliteType.Integer).Value = endUTC.Ticks;
        return LoadHistory(
          createTempCmd,
          includeRecentSimDayUsage: false,
          alreadyKnownSchIds: alreadyKnownSchIds
        );
      }
    }

    public RecentHistoricData LoadRecentJobHistory(
      DateTime startUTC,
      IEnumerable<string> alreadyKnownSchIds = null
    )
    {
      using (var createTempCmd = _connection.CreateCommand())
      {
        createTempCmd.CommandText =
          "CREATE TEMP TABLE temp_sch_ids AS SELECT DISTINCT ScheduleId FROM jobs WHERE EndUTC >= $start AND ScheduleId IS NOT NULL";
        createTempCmd.Parameters.Add("start", SqliteType.Integer).Value = startUTC.Ticks;
        return LoadHistory(
          createTempCmd,
          includeRecentSimDayUsage: true,
          alreadyKnownSchIds: alreadyKnownSchIds
        );
      }
    }

    public ImmutableList<Workorder> WorkordersById(string workorderId)
    {
      return WorkordersById(new HashSet<string>(new[] { workorderId }))[workorderId];
    }

    public ImmutableDictionary<string, ImmutableList<Workorder>> WorkordersById(
      IReadOnlySet<string> workorderIds
    )
    {
      var byWorkId = ImmutableDictionary.CreateBuilder<string, ImmutableList<Workorder>>();
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText =
          "SELECT w.Part, w.Quantity, w.DueDate, w.Priority, p.ProcessNumber, p.StopIndex, p.ProgramName, p.Revision, w.SimulatedStartUTC, w.SimulatedFilledUTC"
          + " FROM unfilled_workorders w "
          + " LEFT OUTER JOIN workorder_programs p ON w.ScheduleId = p.ScheduleId AND w.Workorder = p.Workorder AND w.Part = p.Part "
          + " WHERE "
          + "    w.ScheduleId = (SELECT MAX(v.ScheduleId) FROM unfilled_workorders v WHERE v.Workorder = $work)"
          + "    AND w.Workorder = $work";
        cmd.Parameters.Add("work", SqliteType.Text);

        foreach (var workorderId in workorderIds)
        {
          cmd.Parameters[0].Value = workorderId;
          var byPart =
            new Dictionary<string, (Workorder work, ImmutableList<ProgramForJobStep>.Builder progs)>();

          using (IDataReader reader = cmd.ExecuteReader())
          {
            while (reader.Read())
            {
              var part = reader.GetString(0);

              if (!byPart.ContainsKey(part))
              {
                var workorder = new Workorder()
                {
                  WorkorderId = workorderId,
                  Part = part,
                  Quantity = reader.GetInt32(1),
                  DueDate = new DateTime(reader.GetInt64(2)),
                  Priority = reader.GetInt32(3),
                  SimulatedStart = reader.IsDBNull(8)
                    ? (DateOnly?)null
                    : DateOnly.FromDayNumber(reader.GetInt32(8)),
                  SimulatedFilled = reader.IsDBNull(9)
                    ? (DateOnly?)null
                    : DateOnly.FromDayNumber(reader.GetInt32(9))
                };
                byPart.Add(part, (work: workorder, progs: ImmutableList.CreateBuilder<ProgramForJobStep>()));
              }

              if (reader.IsDBNull(4))
                continue;

              // add the program
              byPart[part]
                .progs.Add(
                  new ProgramForJobStep()
                  {
                    ProcessNumber = reader.GetInt32(4),
                    StopIndex = reader.IsDBNull(5) ? (int?)null : (int?)reader.GetInt32(5),
                    ProgramName = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Revision = reader.IsDBNull(7) ? (int?)null : (int?)reader.GetInt32(7)
                  }
                );
            }
          }

          byWorkId.Add(
            workorderId,
            byPart
              .Values.Select(w =>
                w.progs.Count == 0 ? w.work : w.work with { Programs = w.progs.ToImmutable() }
              )
              .ToImmutableList()
          );
        }
      }

      return byWorkId.ToImmutable();
    }

    public PlannedSchedule LoadMostRecentSchedule()
    {
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText =
          "SELECT UniqueStr, Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual, AllocateAlg"
          + " FROM jobs WHERE ScheduleId = $sid";

        using (var trans = _connection.BeginTransaction())
        {
          var latestSchId = LatestJobScheduleId(trans);
          cmd.Parameters.Add("sid", SqliteType.Text).Value = latestSchId;
          cmd.Transaction = trans;
          return new PlannedSchedule()
          {
            LatestScheduleId = latestSchId,
            Jobs = LoadJobsHelper(cmd, trans),
            ExtraParts = LoadExtraParts(trans, latestSchId),
          };
        }
      }
    }

    public ImmutableList<HistoricJob> LoadJobsBetween(string startingUniqueStr, string endingUniqueStr)
    {
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText =
          "SELECT UniqueStr, Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual, AllocateAlg"
          + " FROM jobs WHERE UniqueStr BETWEEN $start AND $end";

        using (var trans = _connection.BeginTransaction())
        {
          cmd.Transaction = trans;
          cmd.Parameters.Add("start", SqliteType.Text).Value = startingUniqueStr;
          cmd.Parameters.Add("end", SqliteType.Text).Value = endingUniqueStr;
          return LoadJobsHelper(cmd, trans);
        }
      }
    }

    public HistoricJob LoadJob(string UniqueStr)
    {
      using (var cmd = _connection.CreateCommand())
      {
        HistoricJob job = null;

        cmd.CommandText =
          "SELECT Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual, AllocateAlg FROM jobs WHERE UniqueStr = $uniq";
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = UniqueStr;

        var trans = _connection.BeginTransaction();
        try
        {
          cmd.Transaction = trans;

          using (IDataReader reader = cmd.ExecuteReader())
          {
            if (reader.Read())
            {
              var details = LoadJobData(UniqueStr, trans);

              job = new HistoricJob()
              {
                UniqueStr = UniqueStr,
                PartName = reader.GetString(0),
                Comment = reader.IsDBNull(2) ? "" : reader.GetString(2),
                RouteStartUTC = reader.IsDBNull(3)
                  ? DateTime.MinValue
                  : new DateTime(reader.GetInt64(3), DateTimeKind.Utc),
                RouteEndUTC = reader.IsDBNull(4)
                  ? DateTime.MaxValue
                  : new DateTime(reader.GetInt64(4), DateTimeKind.Utc),
                Archived = reader.GetBoolean(5),
                CopiedToSystem = reader.IsDBNull(6) ? false : reader.GetBoolean(6),
                ScheduleId = reader.IsDBNull(7) ? null : reader.GetString(7),
                ManuallyCreated = !reader.IsDBNull(8) && reader.GetBoolean(8),
                AllocationAlgorithm = reader.IsDBNull(9) ? null : reader.GetString(9),
                Cycles = details.CyclesOnFirstProc.Sum(),
                Processes = details.Procs,
                BookingIds = details.Bookings,
                HoldJob = details.Hold?.ToHoldPattern(),
                Decrements = LoadDecrementsForJob(trans, UniqueStr)
              };
            }
          }

          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }

        return job;
      }
    }

    public bool DoesJobExist(string unique)
    {
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText = "SELECT COUNT(*) FROM jobs WHERE UniqueStr = $uniq";
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;

        object cnt = cmd.ExecuteScalar();
        if (cnt != null & Convert.ToInt32(cnt) > 0)
          return true;
        else
          return false;
      }
    }

    #endregion

    #region "Adding and deleting"

    public void AddJobs(NewJobs newJobs, string expectedPreviousScheduleId, bool addAsCopiedToSystem)
    {
      if (string.IsNullOrEmpty(newJobs.ScheduleId))
      {
        throw new BadRequestException("NewJobs must specify a ScheduleId");
      }

      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          if (!string.IsNullOrEmpty(expectedPreviousScheduleId))
          {
            var last = LatestJobScheduleId(trans);
            if (last != expectedPreviousScheduleId)
            {
              throw new BadRequestException(
                string.Format(
                  "Mismatch in previous schedule: expected '{0}' but got '{1}'",
                  expectedPreviousScheduleId,
                  last
                )
              );
            }
          }

          CheckScheduleIdDoesNotExist(trans, newJobs.ScheduleId);

          // add programs first so that the lookup of latest program revision will use newest programs
          var startingUtc = DateTime.UtcNow;
          if (newJobs.Jobs.Any())
          {
            startingUtc = newJobs.Jobs[0].RouteStartUTC;
          }

          var negRevisionMap = AddPrograms(trans, newJobs.Programs, startingUtc);

          foreach (var job in newJobs.Jobs)
          {
            AddJob(trans, job, negRevisionMap, addAsCopiedToSystem, newJobs.ScheduleId);
          }

          AddSimulatedStations(trans, newJobs.StationUse);
          AddSimDayUsage(newJobs.ScheduleId, newJobs.SimDayUsage, newJobs.SimDayUsageWarning, trans);

          if (newJobs.ExtraParts != null)
          {
            AddExtraParts(trans, newJobs.ScheduleId, newJobs.ExtraParts);
          }

          if (newJobs.CurrentUnfilledWorkorders != null)
          {
            AddUnfilledWorkorders(
              trans,
              newJobs.ScheduleId,
              newJobs.CurrentUnfilledWorkorders,
              negRevisionMap
            );
          }

          if (newJobs.DebugMessage != null)
          {
            using (var cmd = _connection.CreateCommand())
            {
              cmd.Transaction = trans;
              cmd.CommandText =
                "INSERT OR REPLACE INTO schedule_debug(ScheduleId, DebugMessage) VALUES ($sid,$debug)";
              cmd.Parameters.Add("sid", SqliteType.Text).Value = newJobs.ScheduleId;
              cmd.Parameters.Add("debug", SqliteType.Blob).Value = newJobs.DebugMessage;
              cmd.ExecuteNonQuery();
            }
          }

          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public void AddPrograms(IEnumerable<NewProgramContent> programs, DateTime startingUtc)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          AddPrograms(trans, programs, startingUtc);
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    private void AddJob(
      IDbTransaction trans,
      Job job,
      Dictionary<(string prog, long rev), long> negativeRevisionMap,
      bool addAsCopiedToSystem,
      string schId
    )
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText =
          "INSERT INTO jobs(UniqueStr, Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual, AllocateAlg) "
          + "VALUES($uniq,$part,$proc,$comment,$start,$end,$archived,$copied,$sid,$manual,$alg)";

        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("part", SqliteType.Text).Value = job.PartName;
        cmd.Parameters.Add("proc", SqliteType.Integer).Value = job.Processes.Count;
        if (string.IsNullOrEmpty(job.Comment))
          cmd.Parameters.Add("comment", SqliteType.Text).Value = DBNull.Value;
        else
          cmd.Parameters.Add("comment", SqliteType.Text).Value = job.Comment;
        cmd.Parameters.Add("start", SqliteType.Integer).Value = job.RouteStartUTC.Ticks;
        cmd.Parameters.Add("end", SqliteType.Integer).Value = job.RouteEndUTC.Ticks;
        cmd.Parameters.Add("archived", SqliteType.Integer).Value = job.Archived;
        cmd.Parameters.Add("copied", SqliteType.Integer).Value = addAsCopiedToSystem;
        if (string.IsNullOrEmpty(schId))
          cmd.Parameters.Add("sid", SqliteType.Text).Value = DBNull.Value;
        else
          cmd.Parameters.Add("sid", SqliteType.Text).Value = schId;
        cmd.Parameters.Add("manual", SqliteType.Integer).Value = job.ManuallyCreated;
        cmd.Parameters.Add("alg", SqliteType.Text).Value = string.IsNullOrEmpty(job.AllocationAlgorithm)
          ? DBNull.Value
          : job.AllocationAlgorithm;

        cmd.ExecuteNonQuery();

        if (job.BookingIds != null)
        {
          cmd.CommandText = "INSERT INTO scheduled_bookings(UniqueStr, BookingId) VALUES ($uniq,$booking)";
          cmd.Parameters.Clear();
          cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
          cmd.Parameters.Add("booking", SqliteType.Text);
          foreach (var b in job.BookingIds)
          {
            cmd.Parameters[1].Value = b;
            cmd.ExecuteNonQuery();
          }
        }

        // eventually move to store directly on job table, but leave here for backwards compatibility
        cmd.CommandText = "INSERT INTO planqty(UniqueStr, Path, PlanQty) VALUES ($uniq,$path,$plan)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("path", SqliteType.Integer).Value = 0;
        cmd.Parameters.Add("plan", SqliteType.Integer).Value = job.Cycles;
        cmd.ExecuteNonQuery();

        cmd.CommandText =
          "INSERT OR REPLACE INTO simulated_production(UniqueStr, Process, Path, TimeUTC, Quantity) VALUES ($uniq,$proc,$path,$time,$qty)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("time", SqliteType.Integer);
        cmd.Parameters.Add("qty", SqliteType.Integer);

        for (int i = 1; i <= job.Processes.Count; i++)
        {
          var proc = job.Processes[i - 1];
          for (int j = 1; j <= proc.Paths.Count; j++)
          {
            var path = proc.Paths[j - 1];
            if (path.SimulatedProduction == null)
              continue;
            foreach (var prod in path.SimulatedProduction)
            {
              cmd.Parameters[1].Value = i;
              cmd.Parameters[2].Value = j;
              cmd.Parameters[3].Value = prod.TimeUTC.Ticks;
              cmd.Parameters[4].Value = prod.Quantity;
              cmd.ExecuteNonQuery();
            }
          }
        }

        cmd.CommandText =
          "INSERT INTO pallets(UniqueStr, Process, Path, Pallet) VALUES ($uniq,$proc,$path,$pal)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("pal", SqliteType.Integer);

        for (int i = 1; i <= job.Processes.Count; i++)
        {
          var proc = job.Processes[i - 1];
          for (int j = 1; j <= proc.Paths.Count; j++)
          {
            var path = proc.Paths[j - 1];
            foreach (var pal in path.PalletNums)
            {
              cmd.Parameters[1].Value = i;
              cmd.Parameters[2].Value = j;
              cmd.Parameters[3].Value = pal;
              cmd.ExecuteNonQuery();
            }
          }
        }

        cmd.CommandText =
          "INSERT INTO pathdata(UniqueStr, Process, Path, StartingUTC, PartsPerPallet, SimAverageFlowTime,InputQueue,OutputQueue,LoadTime,UnloadTime,Fixture,Face,Casting) "
          + "VALUES ($uniq,$proc,$path,$start,$ppp,$flow,$iq,$oq,$lt,$ul,$fix,$face,$casting)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("start", SqliteType.Integer);
        cmd.Parameters.Add("ppp", SqliteType.Integer);
        cmd.Parameters.Add("flow", SqliteType.Integer);
        cmd.Parameters.Add("iq", SqliteType.Text);
        cmd.Parameters.Add("oq", SqliteType.Text);
        cmd.Parameters.Add("lt", SqliteType.Integer);
        cmd.Parameters.Add("ul", SqliteType.Integer);
        cmd.Parameters.Add("fix", SqliteType.Text);
        cmd.Parameters.Add("face", SqliteType.Integer);
        cmd.Parameters.Add("casting", SqliteType.Text);
        for (int i = 1; i <= job.Processes.Count; i++)
        {
          var proc = job.Processes[i - 1];
          for (int j = 1; j <= proc.Paths.Count; j++)
          {
            var path = proc.Paths[j - 1];
            cmd.Parameters[1].Value = i;
            cmd.Parameters[2].Value = j;
            cmd.Parameters[3].Value = path.SimulatedStartingUTC.Ticks;
            cmd.Parameters[4].Value = path.PartsPerPallet;
            cmd.Parameters[5].Value = path.SimulatedAverageFlowTime.Ticks;
            var iq = path.InputQueue;
            if (string.IsNullOrEmpty(iq))
              cmd.Parameters[6].Value = DBNull.Value;
            else
              cmd.Parameters[6].Value = iq;
            var oq = path.OutputQueue;
            if (string.IsNullOrEmpty(oq))
              cmd.Parameters[7].Value = DBNull.Value;
            else
              cmd.Parameters[7].Value = oq;
            cmd.Parameters[8].Value = path.ExpectedLoadTime.Ticks;
            cmd.Parameters[9].Value = path.ExpectedUnloadTime.Ticks;
            var (fix, face) = (path.Fixture, path.Face);
            cmd.Parameters[10].Value = string.IsNullOrEmpty(fix) ? DBNull.Value : (object)fix;
            cmd.Parameters[11].Value = face ?? 0;
            if (i == 1)
            {
              var casting = path.Casting;
              cmd.Parameters[12].Value = string.IsNullOrEmpty(casting) ? DBNull.Value : (object)casting;
            }
            else
            {
              cmd.Parameters[12].Value = DBNull.Value;
            }
            cmd.ExecuteNonQuery();
          }
        }

        cmd.CommandText =
          "INSERT INTO stops(UniqueStr, Process, Path, RouteNum, StatGroup, ExpectedCycleTime, Program, ProgramRevision) "
          + "VALUES ($uniq,$proc,$path,$route,$group,$cycle,$prog,$rev)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("route", SqliteType.Integer);
        cmd.Parameters.Add("group", SqliteType.Text);
        cmd.Parameters.Add("cycle", SqliteType.Integer);
        cmd.Parameters.Add("prog", SqliteType.Text);
        cmd.Parameters.Add("rev", SqliteType.Integer);

        for (int i = 1; i <= job.Processes.Count; i++)
        {
          var proc = job.Processes[i - 1];
          for (int j = 1; j <= proc.Paths.Count; j++)
          {
            var path = proc.Paths[j - 1];
            int routeNum = 0;
            foreach (var entry in path.Stops)
            {
              long? rev = null;
              if (!entry.ProgramRevision.HasValue || entry.ProgramRevision.Value == 0)
              {
                if (!string.IsNullOrEmpty(entry.Program))
                {
                  rev = LatestRevisionForProgram(trans, entry.Program);
                }
              }
              else if (entry.ProgramRevision.Value > 0)
              {
                rev = entry.ProgramRevision.Value;
              }
              else if (
                negativeRevisionMap.TryGetValue(
                  (prog: entry.Program, rev: entry.ProgramRevision.Value),
                  out long convertedRev
                )
              )
              {
                rev = convertedRev;
              }
              else
              {
                throw new BadRequestException(
                  $"Part {job.PartName}, process {i}, path {j}, stop {routeNum}, "
                    + "has a negative program revision but no matching negative program revision exists in the downloaded ProgramEntry list"
                );
              }
              cmd.Parameters[1].Value = i;
              cmd.Parameters[2].Value = j;
              cmd.Parameters[3].Value = routeNum;
              cmd.Parameters[4].Value = entry.StationGroup;
              cmd.Parameters[5].Value = entry.ExpectedCycleTime.Ticks;
              cmd.Parameters[6].Value = string.IsNullOrEmpty(entry.Program)
                ? DBNull.Value
                : (object)entry.Program;
              cmd.Parameters[7].Value = rev != null ? (object)rev : DBNull.Value;
              cmd.ExecuteNonQuery();
              routeNum += 1;
            }
          }
        }

        cmd.CommandText =
          "INSERT INTO stops_stations(UniqueStr, Process, Path, RouteNum, StatNum) "
          + "VALUES ($uniq,$proc,$path,$route,$num)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("route", SqliteType.Integer);
        cmd.Parameters.Add("num", SqliteType.Integer);

        for (int i = 1; i <= job.Processes.Count; i++)
        {
          var proc = job.Processes[i - 1];
          for (int j = 1; j <= proc.Paths.Count; j++)
          {
            var path = proc.Paths[j - 1];
            int routeNum = 0;
            foreach (var entry in path.Stops)
            {
              foreach (var stat in entry.Stations)
              {
                cmd.Parameters[1].Value = i;
                cmd.Parameters[2].Value = j;
                cmd.Parameters[3].Value = routeNum;
                cmd.Parameters[4].Value = stat;

                cmd.ExecuteNonQuery();
              }
              routeNum += 1;
            }
          }
        }

        cmd.CommandText =
          "INSERT INTO tools(UniqueStr, Process, Path, RouteNum, Tool, ExpectedUse) "
          + "VALUES ($uniq,$proc,$path,$route,$tool,$use)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("route", SqliteType.Integer);
        cmd.Parameters.Add("tool", SqliteType.Text);
        cmd.Parameters.Add("use", SqliteType.Integer);

        for (int i = 1; i <= job.Processes.Count; i++)
        {
          var proc = job.Processes[i - 1];
          for (int j = 1; j <= proc.Paths.Count; j++)
          {
            var path = proc.Paths[j - 1];
            int routeNum = 0;
            foreach (var entry in path.Stops)
            {
              if (entry.Tools != null)
              {
                foreach (var tool in entry.Tools)
                {
                  cmd.Parameters[1].Value = i;
                  cmd.Parameters[2].Value = j;
                  cmd.Parameters[3].Value = routeNum;
                  cmd.Parameters[4].Value = tool.Key;
                  cmd.Parameters[5].Value = tool.Value.Ticks;
                  cmd.ExecuteNonQuery();
                }
              }
              routeNum += 1;
            }
          }
        }

        cmd.CommandText =
          "INSERT INTO loadunload(UniqueStr,Process,Path,StatNum,Load) VALUES ($uniq,$proc,$path,$stat,$load)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("stat", SqliteType.Integer);
        cmd.Parameters.Add("load", SqliteType.Integer);

        for (int i = 1; i <= job.Processes.Count; i++)
        {
          var proc = job.Processes[i - 1];
          for (int j = 1; j <= proc.Paths.Count; j++)
          {
            var path = proc.Paths[j - 1];
            cmd.Parameters[1].Value = i;
            cmd.Parameters[2].Value = j;
            cmd.Parameters[4].Value = true;
            foreach (int statNum in path.Load)
            {
              cmd.Parameters[3].Value = statNum;
              cmd.ExecuteNonQuery();
            }
            cmd.Parameters[4].Value = false;
            foreach (int statNum in path.Unload)
            {
              cmd.Parameters[3].Value = statNum;
              cmd.ExecuteNonQuery();
            }
          }
        }

        cmd.CommandText =
          "INSERT INTO path_inspections(UniqueStr,Process,Path,InspType,Counter,MaxVal,TimeInterval,RandomFreq,ExpectedTime) "
          + "VALUES ($uniq,$proc,$path,$insp,$cnt,$max,$time,$freq,$expected)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("insp", SqliteType.Text);
        cmd.Parameters.Add("cnt", SqliteType.Text);
        cmd.Parameters.Add("max", SqliteType.Integer);
        cmd.Parameters.Add("time", SqliteType.Integer);
        cmd.Parameters.Add("freq", SqliteType.Real);
        cmd.Parameters.Add("expected", SqliteType.Integer);
        for (int i = 1; i <= job.Processes.Count; i++)
        {
          var proc = job.Processes[i - 1];
          for (int j = 1; j <= proc.Paths.Count; j++)
          {
            var path = proc.Paths[j - 1];
            if (path.Inspections != null)
            {
              cmd.Parameters[1].Value = i;
              cmd.Parameters[2].Value = j;

              foreach (var insp in path.Inspections)
              {
                cmd.Parameters[3].Value = insp.InspectionType;
                cmd.Parameters[4].Value = insp.Counter;
                cmd.Parameters[5].Value = insp.MaxVal > 0 ? (object)insp.MaxVal : DBNull.Value;
                cmd.Parameters[6].Value =
                  insp.TimeInterval.Ticks > 0 ? (object)insp.TimeInterval.Ticks : DBNull.Value;
                cmd.Parameters[7].Value = insp.RandomFreq > 0 ? (object)insp.RandomFreq : DBNull.Value;
                cmd.Parameters[8].Value =
                  insp.ExpectedInspectionTime.HasValue && insp.ExpectedInspectionTime.Value.Ticks > 0
                    ? (object)insp.ExpectedInspectionTime.Value.Ticks
                    : DBNull.Value;
                cmd.ExecuteNonQuery();
              }
            }
          }
        }
      }

      InsertHold(job.UniqueStr, -1, -1, false, job.HoldJob, trans);
      for (int i = 1; i <= job.Processes.Count; i++)
      {
        var proc = job.Processes[i - 1];
        for (int j = 1; j <= proc.Paths.Count; j++)
        {
          var path = proc.Paths[j - 1];
          InsertHold(job.UniqueStr, i, j, true, path.HoldLoadUnload, trans);
          InsertHold(job.UniqueStr, i, j, false, path.HoldMachining, trans);
        }
      }
    }

    private void AddSimulatedStations(IDbTransaction trans, IEnumerable<SimulatedStationUtilization> simStats)
    {
      if (simStats == null)
        return;

      using (var cmd = _connection.CreateCommand())
      using (var partCmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        ((IDbCommand)partCmd).Transaction = trans;

        cmd.CommandText =
          "INSERT OR REPLACE INTO sim_station_use(SimId, StationGroup, StationNum, StartUTC, EndUTC, PlanDown) "
          + " VALUES($simid,$group,$num,$start,$end,$plandown)";
        cmd.Parameters.Add("simid", SqliteType.Text);
        cmd.Parameters.Add("group", SqliteType.Text);
        cmd.Parameters.Add("num", SqliteType.Integer);
        cmd.Parameters.Add("start", SqliteType.Integer);
        cmd.Parameters.Add("end", SqliteType.Integer);
        cmd.Parameters.Add("plandown", SqliteType.Integer);

        partCmd.CommandText =
          "INSERT OR REPLACE INTO sim_station_use_parts(SimId, StationGroup, StationNum, StartUTC, EndUTC, JobUnique, Process, Path) "
          + " VALUES($simid,$group,$num,$start,$end,$job,$proc,$path)";
        partCmd.Parameters.Add("simid", SqliteType.Text);
        partCmd.Parameters.Add("group", SqliteType.Text);
        partCmd.Parameters.Add("num", SqliteType.Integer);
        partCmd.Parameters.Add("start", SqliteType.Integer);
        partCmd.Parameters.Add("end", SqliteType.Integer);
        partCmd.Parameters.Add("job", SqliteType.Text);
        partCmd.Parameters.Add("proc", SqliteType.Integer);
        partCmd.Parameters.Add("path", SqliteType.Integer);

        foreach (var sim in simStats)
        {
          cmd.Parameters[0].Value = sim.ScheduleId;
          cmd.Parameters[1].Value = sim.StationGroup;
          cmd.Parameters[2].Value = sim.StationNum;
          cmd.Parameters[3].Value = sim.StartUTC.Ticks;
          cmd.Parameters[4].Value = sim.EndUTC.Ticks;
          cmd.Parameters[5].Value = sim.PlanDown.HasValue ? sim.PlanDown.Value : DBNull.Value;
          cmd.ExecuteNonQuery();

          if (sim.Parts != null)
          {
            partCmd.Parameters[0].Value = sim.ScheduleId;
            partCmd.Parameters[1].Value = sim.StationGroup;
            partCmd.Parameters[2].Value = sim.StationNum;
            partCmd.Parameters[3].Value = sim.StartUTC.Ticks;
            partCmd.Parameters[4].Value = sim.EndUTC.Ticks;
            foreach (var part in sim.Parts)
            {
              partCmd.Parameters[5].Value = part.JobUnique;
              partCmd.Parameters[6].Value = part.Process;
              partCmd.Parameters[7].Value = part.Path;
              partCmd.ExecuteNonQuery();
            }
          }
        }
      }
    }

    private void AddSimDayUsage(
      string simId,
      ImmutableList<SimulatedDayUsage> dayUsage,
      string dayUsageWarning,
      IDbTransaction trans
    )
    {
      if (dayUsage != null && dayUsage.Count > 0)
      {
        using (var dayCmd = _connection.CreateCommand())
        {
          ((IDbCommand)dayCmd).Transaction = trans;

          dayCmd.CommandText =
            "INSERT OR REPLACE INTO sim_day_usage(SimId, Day, Station, Usage) VALUES ($simid,$day,$station,$usage)";
          dayCmd.Parameters.Add("simid", SqliteType.Text).Value = simId;
          dayCmd.Parameters.Add("day", SqliteType.Integer);
          dayCmd.Parameters.Add("station", SqliteType.Text);
          dayCmd.Parameters.Add("usage", SqliteType.Integer);

          foreach (var day in dayUsage)
          {
            dayCmd.Parameters[1].Value = day.Day.DayNumber;
            dayCmd.Parameters[2].Value = day.MachineGroup;
            dayCmd.Parameters[3].Value = day.Usage;
            dayCmd.ExecuteNonQuery();
          }
        }
      }

      if (!string.IsNullOrEmpty(dayUsageWarning))
      {
        using (var warningCmd = _connection.CreateCommand())
        {
          ((IDbCommand)warningCmd).Transaction = trans;

          warningCmd.CommandText =
            "INSERT OR REPLACE INTO sim_day_usage_warning(SimId, Warning) VALUES ($simid,$warning)";
          warningCmd.Parameters.Add("simid", SqliteType.Text).Value = simId;
          warningCmd.Parameters.Add("warning", SqliteType.Text).Value = dayUsageWarning;
          warningCmd.ExecuteNonQuery();
        }
      }
    }

    private void AddExtraParts(IDbTransaction trans, string scheduleId, IDictionary<string, int> extraParts)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText =
          "INSERT OR REPLACE INTO scheduled_parts(ScheduleId, Part, Quantity) VALUES ($sid,$part,$qty)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("sid", SqliteType.Text).Value = scheduleId;
        cmd.Parameters.Add("part", SqliteType.Text);
        cmd.Parameters.Add("qty", SqliteType.Integer);
        foreach (var p in extraParts)
        {
          cmd.Parameters[1].Value = p.Key;
          cmd.Parameters[2].Value = p.Value;
          cmd.ExecuteNonQuery();
        }
      }
    }

    private void AddUnfilledWorkorders(
      IDbTransaction trans,
      string scheduleId,
      IEnumerable<Workorder> workorders,
      Dictionary<(string prog, long rev), long> negativeRevisionMap
    )
    {
      using (var cmd = _connection.CreateCommand())
      using (var prgCmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        ((IDbCommand)prgCmd).Transaction = trans;

        cmd.CommandText =
          "INSERT OR REPLACE INTO unfilled_workorders(ScheduleId, Workorder, Part, Quantity, DueDate, Priority, Archived, SimulatedStartUTC, SimulatedFilledUTC) VALUES ($sid,$work,$part,$qty,$due,$pri,NULL,$start,$filled)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("sid", SqliteType.Text).Value = scheduleId;
        cmd.Parameters.Add("work", SqliteType.Text);
        cmd.Parameters.Add("part", SqliteType.Text);
        cmd.Parameters.Add("qty", SqliteType.Integer);
        cmd.Parameters.Add("due", SqliteType.Integer);
        cmd.Parameters.Add("pri", SqliteType.Integer);
        cmd.Parameters.Add("start", SqliteType.Integer);
        cmd.Parameters.Add("filled", SqliteType.Integer);

        prgCmd.CommandText =
          "INSERT OR REPLACE INTO workorder_programs(ScheduleId, Workorder, Part, ProcessNumber, StopIndex, ProgramName, Revision) VALUES ($sid,$work,$part,$proc,$stop,$name,$rev)";
        prgCmd.Parameters.Add("sid", SqliteType.Text).Value = scheduleId;
        prgCmd.Parameters.Add("work", SqliteType.Text);
        prgCmd.Parameters.Add("part", SqliteType.Text);
        prgCmd.Parameters.Add("proc", SqliteType.Integer);
        prgCmd.Parameters.Add("stop", SqliteType.Integer);
        prgCmd.Parameters.Add("name", SqliteType.Text);
        prgCmd.Parameters.Add("rev", SqliteType.Integer);

        foreach (var w in workorders)
        {
          cmd.Parameters[1].Value = w.WorkorderId;
          cmd.Parameters[2].Value = w.Part;
          cmd.Parameters[3].Value = w.Quantity;
          cmd.Parameters[4].Value = w.DueDate.Ticks;
          cmd.Parameters[5].Value = w.Priority;
          cmd.Parameters[6].Value = w.SimulatedStart.HasValue
            ? w.SimulatedStart.Value.DayNumber
            : DBNull.Value;
          cmd.Parameters[7].Value = w.SimulatedFilled.HasValue
            ? w.SimulatedFilled.Value.DayNumber
            : DBNull.Value;
          cmd.ExecuteNonQuery();

          if (w.Programs != null)
          {
            foreach (var prog in w.Programs)
            {
              long? rev = null;
              if (!prog.Revision.HasValue || prog.Revision.Value == 0)
              {
                if (!string.IsNullOrEmpty(prog.ProgramName))
                {
                  rev = LatestRevisionForProgram(trans, prog.ProgramName);
                }
              }
              else if (prog.Revision.Value > 0)
              {
                rev = prog.Revision.Value;
              }
              else if (
                negativeRevisionMap.TryGetValue(
                  (prog: prog.ProgramName, rev: prog.Revision.Value),
                  out long convertedRev
                )
              )
              {
                rev = convertedRev;
              }
              else
              {
                throw new BadRequestException(
                  $"Workorder {w.WorkorderId} "
                    + "has a negative program revision but no matching negative program revision exists in the downloaded ProgramEntry list"
                );
              }

              prgCmd.Parameters[1].Value = w.WorkorderId;
              prgCmd.Parameters[2].Value = w.Part;
              prgCmd.Parameters[3].Value = prog.ProcessNumber;
              prgCmd.Parameters[4].Value = prog.StopIndex.HasValue
                ? (object)prog.StopIndex.Value
                : DBNull.Value;
              prgCmd.Parameters[5].Value = prog.ProgramName;
              prgCmd.Parameters[6].Value = rev != null ? (object)rev : DBNull.Value;
              prgCmd.ExecuteNonQuery();
            }
          }
        }
      }
    }

    private void InsertHold(
      string unique,
      int proc,
      int path,
      bool load,
      HoldPattern newHold,
      IDbTransaction trans
    )
    {
      if (newHold == null)
        return;

      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText =
          "INSERT INTO holds(UniqueStr,Process,Path,LoadUnload,UserHold,UserHoldReason,HoldPatternStartUTC,HoldPatternRepeats) "
          + "VALUES ($uniq,$proc,$path,$load,$hold,$holdR,$holdT,$holdP)";
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
        cmd.Parameters.Add("proc", SqliteType.Integer).Value = proc;
        cmd.Parameters.Add("path", SqliteType.Integer).Value = path;
        cmd.Parameters.Add("load", SqliteType.Integer).Value = load;
        cmd.Parameters.Add("hold", SqliteType.Integer).Value = newHold.UserHold;
        cmd.Parameters.Add("holdR", SqliteType.Text).Value = newHold.ReasonForUserHold;
        cmd.Parameters.Add("holdT", SqliteType.Integer).Value = newHold.HoldUnholdPatternStartUTC.Ticks;
        cmd.Parameters.Add("holdP", SqliteType.Integer).Value = newHold.HoldUnholdPatternRepeats;
        cmd.ExecuteNonQuery();

        cmd.CommandText =
          "INSERT INTO hold_pattern(UniqueStr,Process,Path,LoadUnload,Idx,Span) "
          + "VALUES ($uniq,$proc,$path,$stat,$idx,$span)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
        cmd.Parameters.Add("proc", SqliteType.Integer).Value = proc;
        cmd.Parameters.Add("path", SqliteType.Integer).Value = path;
        cmd.Parameters.Add("stat", SqliteType.Integer).Value = load;
        cmd.Parameters.Add("idx", SqliteType.Integer);
        cmd.Parameters.Add("span", SqliteType.Integer);
        for (int i = 0; i < newHold.HoldUnholdPattern.Count; i++)
        {
          cmd.Parameters[4].Value = i;
          cmd.Parameters[5].Value = newHold.HoldUnholdPattern[i].Ticks;
          cmd.ExecuteNonQuery();
        }
      }
    }

    public void ArchiveJob(string UniqueStr)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          SetArchived(trans, new[] { UniqueStr }, archived: true);
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public void ArchiveJobs(
      IEnumerable<string> uniqueStrs,
      IEnumerable<NewDecrementQuantity> newDecrements = null,
      DateTime? nowUTC = null
    )
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          SetArchived(trans, uniqueStrs, archived: true);
          if (newDecrements != null)
          {
            AddNewDecrement(trans: trans, counts: newDecrements, removedBookings: null, nowUTC: nowUTC);
          }
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public void UnarchiveJob(string UniqueStr)
    {
      UnarchiveJobs(new[] { UniqueStr });
    }

    public void UnarchiveJobs(IEnumerable<string> uniqueStrs, DateTime? nowUTC = null)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          SetArchived(trans, uniqueStrs, archived: false);
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    private void SetArchived(IDbTransaction trans, IEnumerable<string> uniqs, bool archived)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "UPDATE jobs SET Archived = $archived WHERE UniqueStr = $uniq";
        var param = cmd.Parameters.Add("uniq", SqliteType.Text);
        cmd.Parameters.Add("archived", SqliteType.Integer).Value = archived ? 1 : 0;
        foreach (var uniqStr in uniqs)
        {
          param.Value = uniqStr;
          cmd.ExecuteNonQuery();
        }
      }
    }

    public void MarkJobCopiedToSystem(string UniqueStr)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          using (var cmd = _connection.CreateCommand())
          {
            cmd.CommandText = "UPDATE jobs SET CopiedToSystem = 1 WHERE UniqueStr = $uniq";
            cmd.Parameters.Add("uniq", SqliteType.Text).Value = UniqueStr;
            ((IDbCommand)cmd).Transaction = trans;
            cmd.ExecuteNonQuery();
            trans.Commit();
          }
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }
    #endregion

    #region "Modification of Jobs"
    public void SetJobComment(string unique, string comment)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          var trans = _connection.BeginTransaction();

          try
          {
            cmd.Transaction = trans;

            cmd.CommandText = "UPDATE jobs SET Comment = $comment WHERE UniqueStr = $uniq";
            cmd.Parameters.Add("comment", SqliteType.Text).Value = comment;
            cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
            cmd.ExecuteNonQuery();
            trans.Commit();
          }
          catch
          {
            trans.Rollback();
            throw;
          }
        }
      }
    }

    public void UpdateJobHold(string unique, HoldPattern newHold)
    {
      UpdateJobHoldHelper(unique, -1, -1, false, newHold);
    }

    public void UpdateJobMachiningHold(string unique, int proc, int path, HoldPattern newHold)
    {
      UpdateJobHoldHelper(unique, proc, path, false, newHold);
    }

    public void UpdateJobLoadUnloadHold(string unique, int proc, int path, HoldPattern newHold)
    {
      UpdateJobHoldHelper(unique, proc, path, true, newHold);
    }

    private void UpdateJobHoldHelper(string unique, int proc, int path, bool load, HoldPattern newHold)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();

        try
        {
          using (var cmd = _connection.CreateCommand())
          {
            cmd.Transaction = trans;

            cmd.CommandText =
              "DELETE FROM holds WHERE UniqueStr = $uniq AND Process = $proc AND Path = $path AND LoadUnload = $load";
            cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
            cmd.Parameters.Add("proc", SqliteType.Integer).Value = proc;
            cmd.Parameters.Add("path", SqliteType.Integer).Value = path;
            cmd.Parameters.Add("load", SqliteType.Integer).Value = load;
            cmd.ExecuteNonQuery();

            cmd.CommandText =
              "DELETE FROM hold_pattern WHERE UniqueStr = $uniq AND Process = $proc AND Path = $path AND LoadUnload = $load";
            cmd.ExecuteNonQuery();

            InsertHold(unique, proc, path, load, newHold, trans);

            trans.Commit();
          }
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }
    #endregion

    #region Decrement Counts
    public void AddNewDecrement(
      IEnumerable<NewDecrementQuantity> counts,
      DateTime? nowUTC = null,
      IEnumerable<RemovedBooking> removedBookings = null
    )
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          AddNewDecrement(trans: trans, counts: counts, removedBookings: removedBookings, nowUTC: nowUTC);
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    private void AddNewDecrement(
      IDbTransaction trans,
      IEnumerable<NewDecrementQuantity> counts,
      IEnumerable<RemovedBooking> removedBookings,
      DateTime? nowUTC
    )
    {
      var now = nowUTC ?? DateTime.UtcNow;
      long decrementId = 0;
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "SELECT MAX(DecrementId) FROM job_decrements";
        var lastDecId = cmd.ExecuteScalar();
        if (lastDecId != null && lastDecId != DBNull.Value)
        {
          decrementId = Convert.ToInt64(lastDecId) + 1;
        }
      }

      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText =
          "INSERT INTO job_decrements(DecrementId,JobUnique,Proc1Path,TimeUTC,Part,Quantity) VALUES ($id,$uniq,$path,$now,$part,$qty)";
        cmd.Parameters.Add("id", SqliteType.Integer);
        cmd.Parameters.Add("uniq", SqliteType.Text);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("now", SqliteType.Integer);
        cmd.Parameters.Add("part", SqliteType.Text);
        cmd.Parameters.Add("qty", SqliteType.Integer);

        foreach (var q in counts)
        {
          cmd.Parameters[0].Value = decrementId;
          cmd.Parameters[1].Value = q.JobUnique;
          cmd.Parameters[2].Value = 1; // For now, leave Proc1Path in the database
          cmd.Parameters[3].Value = now.Ticks;
          cmd.Parameters[4].Value = q.Part;
          cmd.Parameters[5].Value = q.Quantity;
          cmd.ExecuteNonQuery();
        }
      }

      if (removedBookings != null)
      {
        using (var cmd = _connection.CreateCommand())
        {
          ((IDbCommand)cmd).Transaction = trans;

          cmd.CommandText = "DELETE FROM scheduled_bookings WHERE UniqueStr = $u AND BookingId = $b";
          cmd.Parameters.Add("u", SqliteType.Text);
          cmd.Parameters.Add("b", SqliteType.Text);

          foreach (var b in removedBookings)
          {
            cmd.Parameters[0].Value = b.JobUnique;
            cmd.Parameters[1].Value = b.BookingId;
            cmd.ExecuteNonQuery();
          }
        }
      }
    }

    public ImmutableList<DecrementQuantity> LoadDecrementsForJob(string unique)
    {
      return LoadDecrementsForJob(trans: null, unique: unique);
    }

    private ImmutableList<DecrementQuantity> LoadDecrementsForJob(IDbTransaction trans, string unique)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "SELECT DecrementId,TimeUTC,Quantity FROM job_decrements WHERE JobUnique = $uniq";
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
        var ret = ImmutableList.CreateBuilder<DecrementQuantity>();
        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var j = new DecrementQuantity()
            {
              DecrementId = reader.GetInt64(0),
              TimeUTC = new DateTime(reader.GetInt64(1), DateTimeKind.Utc),
              Quantity = reader.GetInt32(2),
            };
            ret.Add(j);
          }
          return ret.ToImmutable();
        }
      }
    }

    public List<JobAndDecrementQuantity> LoadDecrementQuantitiesAfter(long afterId)
    {
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText =
          "SELECT DecrementId,JobUnique,TimeUTC,Part,Quantity FROM job_decrements WHERE DecrementId > $after";
        cmd.Parameters.Add("after", SqliteType.Integer).Value = afterId;
        return LoadDecrementQuantitiesHelper(cmd);
      }
    }

    public List<JobAndDecrementQuantity> LoadDecrementQuantitiesAfter(DateTime afterUTC)
    {
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText =
          "SELECT DecrementId,JobUnique,TimeUTC,Part,Quantity FROM job_decrements WHERE TimeUTC > $after";
        cmd.Parameters.Add("after", SqliteType.Integer).Value = afterUTC.Ticks;
        return LoadDecrementQuantitiesHelper(cmd);
      }
    }

    private List<JobAndDecrementQuantity> LoadDecrementQuantitiesHelper(IDbCommand cmd)
    {
      var ret = new List<JobAndDecrementQuantity>();
      using (var reader = cmd.ExecuteReader())
      {
        while (reader.Read())
        {
          ret.Add(
            new JobAndDecrementQuantity()
            {
              DecrementId = reader.GetInt64(0),
              JobUnique = reader.GetString(1),
              TimeUTC = new DateTime(reader.GetInt64(2), DateTimeKind.Utc),
              Part = reader.GetString(3),
              Quantity = reader.GetInt32(4),
            }
          );
        }
        return ret;
      }
    }
    #endregion

    #region Programs
    private Dictionary<(string prog, long rev), long> AddPrograms(
      IDbTransaction transaction,
      IEnumerable<NewProgramContent> programs,
      DateTime nowUtc
    )
    {
      if (programs == null || !programs.Any())
        return new Dictionary<(string prog, long rev), long>();

      var negativeRevisionMap = new Dictionary<(string prog, long rev), long>();

      using (var checkCmd = _connection.CreateCommand())
      using (var checkMaxCmd = _connection.CreateCommand())
      using (var checkByCommentCmd = _connection.CreateCommand())
      using (var addProgCmd = _connection.CreateCommand())
      {
        ((IDbCommand)checkCmd).Transaction = transaction;
        checkCmd.CommandText =
          "SELECT ProgramContent FROM program_revisions WHERE ProgramName = $name AND ProgramRevision = $rev";
        checkCmd.Parameters.Add("name", SqliteType.Text);
        checkCmd.Parameters.Add("rev", SqliteType.Integer);

        ((IDbCommand)checkMaxCmd).Transaction = transaction;
        checkMaxCmd.CommandText =
          "SELECT ProgramRevision, ProgramContent FROM program_revisions WHERE ProgramName = $prog ORDER BY ProgramRevision DESC LIMIT 1";
        checkMaxCmd.Parameters.Add("prog", SqliteType.Text);

        ((IDbCommand)checkByCommentCmd).Transaction = transaction;
        checkByCommentCmd.CommandText =
          "SELECT ProgramRevision, ProgramContent FROM program_revisions "
          + " WHERE ProgramName = $prog AND RevisionComment IS NOT NULL AND RevisionComment = $comment "
          + " ORDER BY ProgramRevision DESC LIMIT 1";
        checkByCommentCmd.Parameters.Add("prog", SqliteType.Text);
        checkByCommentCmd.Parameters.Add("comment", SqliteType.Text);

        ((IDbCommand)addProgCmd).Transaction = transaction;
        addProgCmd.CommandText =
          "INSERT INTO program_revisions(ProgramName, ProgramRevision, RevisionTimeUTC, RevisionComment, ProgramContent) "
          + " VALUES($name,$rev,$time,$comment,$prog)";
        addProgCmd.Parameters.Add("name", SqliteType.Text);
        addProgCmd.Parameters.Add("rev", SqliteType.Integer);
        addProgCmd.Parameters.Add("time", SqliteType.Integer).Value = nowUtc.Ticks;
        addProgCmd.Parameters.Add("comment", SqliteType.Text);
        addProgCmd.Parameters.Add("prog", SqliteType.Text);

        // positive revisions are either added or checked for match
        foreach (var prog in programs.Where(p => p.Revision > 0))
        {
          checkCmd.Parameters[0].Value = prog.ProgramName;
          checkCmd.Parameters[1].Value = prog.Revision;
          var content = checkCmd.ExecuteScalar();
          if (content != null && content != DBNull.Value)
          {
            if ((string)content != prog.ProgramContent)
            {
              throw new BadRequestException(
                "Program "
                  + prog.ProgramName
                  + " rev"
                  + prog.Revision.ToString()
                  + " has already been used and the program contents do not match."
              );
            }
            // if match, do nothing
          }
          else
          {
            addProgCmd.Parameters[0].Value = prog.ProgramName;
            addProgCmd.Parameters[1].Value = prog.Revision;
            addProgCmd.Parameters[3].Value = string.IsNullOrEmpty(prog.Comment)
              ? DBNull.Value
              : (object)prog.Comment;
            addProgCmd.Parameters[4].Value = string.IsNullOrEmpty(prog.ProgramContent)
              ? DBNull.Value
              : (object)prog.ProgramContent;
            addProgCmd.ExecuteNonQuery();
          }
        }

        // zero and negative revisions are allocated a new number
        foreach (var prog in programs.Where(p => p.Revision <= 0).OrderByDescending(p => p.Revision))
        {
          long lastRev;
          checkMaxCmd.Parameters[0].Value = prog.ProgramName;
          using (var reader = checkMaxCmd.ExecuteReader())
          {
            if (reader.Read())
            {
              lastRev = reader.GetInt64(0);
              var lastContent = reader.GetString(1);
              if (lastContent == prog.ProgramContent)
              {
                if (prog.Revision < 0)
                  negativeRevisionMap[(prog: prog.ProgramName, rev: prog.Revision)] = lastRev;
                continue;
              }
            }
            else
            {
              lastRev = 0;
            }
          }

          if (!string.IsNullOrEmpty(prog.Comment))
          {
            // check program matching the same comment
            checkByCommentCmd.Parameters[0].Value = prog.ProgramName;
            checkByCommentCmd.Parameters[1].Value = prog.Comment;
            using (var reader = checkByCommentCmd.ExecuteReader())
            {
              if (reader.Read())
              {
                var lastContent = reader.GetString(1);
                if (lastContent == prog.ProgramContent)
                {
                  if (prog.Revision < 0)
                    negativeRevisionMap[(prog: prog.ProgramName, rev: prog.Revision)] = reader.GetInt64(0);
                  continue;
                }
              }
            }
          }

          addProgCmd.Parameters[0].Value = prog.ProgramName;
          addProgCmd.Parameters[1].Value = lastRev + 1;
          addProgCmd.Parameters[3].Value = string.IsNullOrEmpty(prog.Comment)
            ? DBNull.Value
            : (object)prog.Comment;
          addProgCmd.Parameters[4].Value = string.IsNullOrEmpty(prog.ProgramContent)
            ? DBNull.Value
            : (object)prog.ProgramContent;
          addProgCmd.ExecuteNonQuery();

          if (prog.Revision < 0)
            negativeRevisionMap[(prog: prog.ProgramName, rev: prog.Revision)] = lastRev + 1;
        }
      }

      return negativeRevisionMap;
    }

    private long? LatestRevisionForProgram(IDbTransaction trans, string program)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "SELECT MAX(ProgramRevision) FROM program_revisions WHERE ProgramName = $prog";
        cmd.Parameters.Add("prog", SqliteType.Text).Value = program;
        var rev = cmd.ExecuteScalar();
        if (rev == null || rev == DBNull.Value)
        {
          return null;
        }
        else
        {
          return (long)rev;
        }
      }
    }

    public ProgramRevision ProgramFromCellControllerProgram(string cellCtProgName)
    {
      var trans = _connection.BeginTransaction();
      try
      {
        ProgramRevision prog = null;
        using (var cmd = _connection.CreateCommand())
        {
          cmd.Transaction = trans;
          cmd.CommandText =
            "SELECT ProgramName, ProgramRevision, RevisionComment FROM program_revisions WHERE CellControllerProgramName = $prog LIMIT 1";
          cmd.Parameters.Add("prog", SqliteType.Text).Value = cellCtProgName;
          using (var reader = cmd.ExecuteReader())
          {
            while (reader.Read())
            {
              prog = new ProgramRevision
              {
                ProgramName = reader.GetString(0),
                Revision = reader.GetInt64(1),
                Comment = reader.IsDBNull(2) ? null : reader.GetString(2),
                CellControllerProgramName = cellCtProgName
              };
              break;
            }
          }
        }
        trans.Commit();
        return prog;
      }
      catch
      {
        trans.Rollback();
        throw;
      }
    }

    public ProgramRevision LoadProgram(string program, long revision)
    {
      var trans = _connection.BeginTransaction();
      try
      {
        ProgramRevision prog = null;
        using (var cmd = _connection.CreateCommand())
        {
          cmd.Transaction = trans;
          cmd.CommandText =
            "SELECT RevisionComment, CellControllerProgramName FROM program_revisions WHERE ProgramName = $prog AND ProgramRevision = $rev";
          cmd.Parameters.Add("prog", SqliteType.Text).Value = program;
          cmd.Parameters.Add("rev", SqliteType.Integer).Value = revision;
          using (var reader = cmd.ExecuteReader())
          {
            while (reader.Read())
            {
              prog = new ProgramRevision
              {
                ProgramName = program,
                Revision = revision,
                Comment = reader.IsDBNull(0) ? null : reader.GetString(0),
                CellControllerProgramName = reader.IsDBNull(1) ? null : reader.GetString(1)
              };
              break;
            }
          }
        }
        trans.Commit();
        return prog;
      }
      catch
      {
        trans.Rollback();
        throw;
      }
    }

    public ImmutableList<ProgramRevision> LoadProgramRevisionsInDescendingOrderOfRevision(
      string program,
      int count,
      long? startRevision
    )
    {
      count = Math.Min(count, 100);
      using (var cmd = _connection.CreateCommand())
      using (var trans = _connection.BeginTransaction())
      {
        cmd.Transaction = trans;
        if (startRevision.HasValue)
        {
          cmd.CommandText =
            "SELECT ProgramRevision, RevisionComment, CellControllerProgramName FROM program_revisions "
            + " WHERE ProgramName = $prog AND ProgramRevision <= $rev "
            + " ORDER BY ProgramRevision DESC "
            + " LIMIT $cnt";
          cmd.Parameters.Add("prog", SqliteType.Text).Value = program;
          cmd.Parameters.Add("rev", SqliteType.Integer).Value = startRevision.Value;
          cmd.Parameters.Add("cnt", SqliteType.Integer).Value = count;
        }
        else
        {
          cmd.CommandText =
            "SELECT ProgramRevision, RevisionComment, CellControllerProgramName FROM program_revisions "
            + " WHERE ProgramName = $prog "
            + " ORDER BY ProgramRevision DESC "
            + " LIMIT $cnt";
          cmd.Parameters.Add("prog", SqliteType.Text).Value = program;
          cmd.Parameters.Add("cnt", SqliteType.Integer).Value = count;
        }

        using (var reader = cmd.ExecuteReader())
        {
          var ret = ImmutableList.CreateBuilder<ProgramRevision>();
          while (reader.Read())
          {
            ret.Add(
              new ProgramRevision
              {
                ProgramName = program,
                Revision = reader.GetInt64(0),
                Comment = reader.IsDBNull(1) ? null : reader.GetString(1),
                CellControllerProgramName = reader.IsDBNull(2) ? null : reader.GetString(2)
              }
            );
          }
          return ret.ToImmutable();
        }
      }
    }

    public ProgramRevision LoadMostRecentProgram(string program)
    {
      var trans = _connection.BeginTransaction();
      try
      {
        ProgramRevision prog = null;
        using (var cmd = _connection.CreateCommand())
        {
          cmd.Transaction = trans;
          cmd.CommandText =
            "SELECT ProgramRevision, RevisionComment, CellControllerProgramName FROM program_revisions WHERE ProgramName = $prog ORDER BY ProgramRevision DESC LIMIT 1";
          cmd.Parameters.Add("prog", SqliteType.Text).Value = program;
          using (var reader = cmd.ExecuteReader())
          {
            while (reader.Read())
            {
              prog = new ProgramRevision
              {
                ProgramName = program,
                Revision = reader.GetInt64(0),
                Comment = reader.IsDBNull(1) ? null : reader.GetString(1),
                CellControllerProgramName = reader.IsDBNull(2) ? null : reader.GetString(2)
              };
              break;
            }
          }
        }
        trans.Commit();
        return prog;
      }
      catch
      {
        trans.Rollback();
        throw;
      }
    }

    public string LoadProgramContent(string program, long revision)
    {
      var trans = _connection.BeginTransaction();
      try
      {
        string content = null;
        using (var cmd = _connection.CreateCommand())
        {
          cmd.Transaction = trans;
          cmd.CommandText =
            "SELECT ProgramContent FROM program_revisions WHERE ProgramName = $prog AND ProgramRevision = $rev";
          cmd.Parameters.Add("prog", SqliteType.Text).Value = program;
          cmd.Parameters.Add("rev", SqliteType.Integer).Value = revision;
          using (var reader = cmd.ExecuteReader())
          {
            while (reader.Read())
            {
              if (!reader.IsDBNull(0))
              {
                content = reader.GetString(0);
              }
              break;
            }
          }
        }
        trans.Commit();
        return content;
      }
      catch
      {
        trans.Rollback();
        throw;
      }
    }

    public List<ProgramRevision> LoadProgramsInCellController()
    {
      using (var cmd = _connection.CreateCommand())
      using (var trans = _connection.BeginTransaction())
      {
        cmd.Transaction = trans;
        cmd.CommandText =
          "SELECT ProgramName, ProgramRevision, RevisionComment, CellControllerProgramName FROM program_revisions "
          + " WHERE CellControllerProgramName IS NOT NULL";

        using (var reader = cmd.ExecuteReader())
        {
          var ret = new List<ProgramRevision>();
          while (reader.Read())
          {
            ret.Add(
              new ProgramRevision
              {
                ProgramName = reader.GetString(0),
                Revision = reader.GetInt64(1),
                Comment = reader.IsDBNull(2) ? null : reader.GetString(2),
                CellControllerProgramName = reader.IsDBNull(3) ? null : reader.GetString(3)
              }
            );
          }
          return ret;
        }
      }
    }

    public void SetCellControllerProgramForProgram(string program, long revision, string cellCtProgName)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          using (var cmd = _connection.CreateCommand())
          using (var checkCmd = _connection.CreateCommand())
          {
            if (!string.IsNullOrEmpty(cellCtProgName))
            {
              checkCmd.Transaction = trans;
              checkCmd.CommandText =
                "SELECT COUNT(*) FROM program_revisions WHERE CellControllerProgramName = $cell";
              checkCmd.Parameters.Add("cell", SqliteType.Text).Value = cellCtProgName;
              if ((long)checkCmd.ExecuteScalar() > 0)
              {
                throw new Exception("Cell program name " + cellCtProgName + " already in use");
              }
            }

            cmd.Transaction = trans;
            cmd.CommandText =
              "UPDATE program_revisions SET CellControllerProgramName = $cell WHERE ProgramName = $name AND ProgramRevision = $rev";
            cmd.Parameters.Add("cell", SqliteType.Text).Value = string.IsNullOrEmpty(cellCtProgName)
              ? DBNull.Value
              : (object)cellCtProgName;
            cmd.Parameters.Add("name", SqliteType.Text).Value = program;
            cmd.Parameters.Add("rev", SqliteType.Text).Value = revision;
            cmd.ExecuteNonQuery();
          }
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    #endregion
  }
}
