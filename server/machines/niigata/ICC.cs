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
using System.Linq;
using System.Collections.Generic;
using BlackMaple.MachineFramework;
using Npgsql;
using Dapper;

namespace BlackMaple.FMSInsight.Niigata
{
  public class NiigataICC : INiigataCommunication
  {
    private JobDB _jobs;
    private string _programDir;
    private string _connStr;

    public NiigataICC(JobDB j, string progDir, string connectionStr)
    {
      _jobs = j;
      _programDir = progDir;
      _connStr = connectionStr;
    }

    private class StatusIcc
    {
      public int Mode { get; set; }
      public bool Alarm { get; set; }
    }

    private class CurrentStationNum
    {
      public int? StationNum { get; set; }
    }

    private class StatusRouteStep
    {
      public enum RouteTypeE
      {
        Load = 1,
        Unload = 2,
        Reclamp = 3,
        Machine = 4,
        Wash = 5
      }
      public int PalletNum { get; set; }
      public int RouteNum { get; set; }
      public RouteTypeE RouteType { get; set; }
      public int CompletedPartCount { get; set; }
      public int WashingPattern { get; set; }
      public int ExecutedStationNum { get; set; }
    }

    private class RouteStepStation
    {
      public int PalletNum { get; set; }
      public int RouteNum { get; set; }
      public int StationNum { get; set; }
    }

    private class RouteStepPrograms
    {
      public int PalletNum { get; set; }
      public int RouteNum { get; set; }
      public int ProgramNumber { get; set; }
    }

    public NiigataStatus LoadNiigataStatus()
    {
      using (var conn = new NpgsqlConnection(_connStr))
      {
        conn.Open();
        using (var trans = conn.BeginTransaction())
        {

          var status = conn.QueryFirst<NiigataStatus>(
            $@"SELECT mode AS {nameof(NiigataStatus.Mode)},
                      alarm AS {nameof(NiigataStatus.Alarm)}
                FROM status_icc
            ",
            transaction: trans
          );
          status.TimeOfStatusUTC = DateTime.UtcNow;

          status.LoadStations =
            conn.Query<LoadStatus>(
              $@"SELECT lul_no AS {nameof(LoadStatus.LoadNumber)},
                        pallet_exist AS {nameof(LoadStatus.PalletExists)}
                  FROM status_load_station",
              transaction: trans
            )
            .ToDictionary(l => l.LoadNumber);

          status.Machines =
            conn.Query<MachineStatus>(
              $@"SELECT mc_no AS {nameof(MachineStatus.MachineNumber)},
                        power AS {nameof(MachineStatus.Power)},
                        link_mode AS {nameof(MachineStatus.FMSLinkMode)},
                        working AS {nameof(MachineStatus.Machining)},
                        o_no AS {nameof(MachineStatus.CurrentlyExecutingProgram)},
                        alarm AS {nameof(MachineStatus.Alarm)}
                  FROM status_mc",
              transaction: trans
            )
            .ToDictionary(k => k.MachineNumber);

          var pallets =
            conn.Query<PalletMaster, TrackingInfo, CurrentStationNum, PalletStatus>(
                $@"SELECT status_pallet_route.pallet_no AS {nameof(PalletMaster.PalletNum)},
                          comment AS {nameof(PalletMaster.Comment)},
                          remaining_pallet_cycle AS {nameof(PalletMaster.RemainingPalletCycles)},
                          priority AS {nameof(PalletMaster.Priority)},
                          no_work AS {nameof(PalletMaster.NoWork)},
                          pallet_skip AS {nameof(PalletMaster.Skip)},
                          program_download AS {nameof(PalletMaster.PerformProgramDownload)},
                          for_long_tool_maintenance AS {nameof(PalletMaster.ForLongToolMaintenance)},
                          route_invalid AS {nameof(TrackingInfo.RouteInvalid)},
                          dummy_pallet AS {nameof(TrackingInfo.DummyPallet)},
                          alarm AS {nameof(TrackingInfo.Alarm)},
                          alarm_code AS {nameof(TrackingInfo.AlarmCode)},
                          current_step_no AS {nameof(TrackingInfo.CurrentStepNum)},
                          current_control_no AS {nameof(TrackingInfo.CurrentControlNum)},
                          station_no AS {nameof(CurrentStationNum.StationNum)}
                    FROM status_pallet_route
                    LEFT OUTER JOIN tracking ON status_pallet_route.pallet_no = tracking.pallet_no
                ",
                (master, tracking, curStat) => new PalletStatus()
                {
                  Master = master,
                  Tracking = tracking,
                  CurStation = new NiigataStationNum(curStat.StationNum ?? 1)
                },
                splitOn: $"{nameof(TrackingInfo.RouteInvalid)},{nameof(CurrentStationNum.StationNum)}",
                transaction: trans
              )
              .ToDictionary(p => p.Master.PalletNum);

          var routeStations =
            conn.Query<RouteStepStation>(
              $@"SELECT pallet_no as {nameof(RouteStepStation.PalletNum)},
                        route_no AS {nameof(RouteStepStation.RouteNum)},
                        station_no AS {nameof(RouteStepStation.StationNum)}
                  FROM status_pallet_route_step_station
              ",
              transaction: trans
            )
            .ToLookup(r => (pal: r.PalletNum, step: r.RouteNum), r => r.StationNum);

          var routePrograms =
            conn.Query<RouteStepPrograms>(
              $@"SELECT pallet_no AS {nameof(RouteStepPrograms.PalletNum)},
                        route_no AS {nameof(RouteStepPrograms.RouteNum)},
                        o_no AS {nameof(RouteStepPrograms.ProgramNumber)}
                  FROM status_pallet_route_step_program
                  ORDER BY program_order
              ",
              transaction: trans
            )
            .ToLookup(
              p => (pal: p.PalletNum, step: p.RouteNum),
              p => p.ProgramNumber
            );

          foreach (var step in
            conn.Query<StatusRouteStep>(
              $@"SELECT pallet_no AS {nameof(StatusRouteStep.PalletNum)},
                        route_no AS {nameof(StatusRouteStep.RouteNum)},
                        route_type AS {nameof(StatusRouteStep.RouteType)},
                        completed_part_count AS {nameof(StatusRouteStep.CompletedPartCount)},
                        washing_pattern AS {nameof(StatusRouteStep.WashingPattern)},
                        executed_station_no AS {nameof(StatusRouteStep.ExecutedStationNum)}
                  FROM status_pallet_route_step
                  ORDER BY pallet_no,route_no
                ",
              transaction: trans
            ))
          {

            if (!pallets.ContainsKey(step.PalletNum)) continue;
            var palAndStep = (pal: step.PalletNum, step: step.RouteNum);
            var stats = routeStations.Contains(palAndStep) ? routeStations[palAndStep] : Enumerable.Empty<int>();
            RouteStep route;
            switch (step.RouteType)
            {
              case StatusRouteStep.RouteTypeE.Load:
                route = new LoadStep()
                {
                  LoadStations = stats.ToList()
                };
                break;

              case StatusRouteStep.RouteTypeE.Unload:
                route = new UnloadStep()
                {
                  UnloadStations = stats.ToList(),
                  CompletedPartCount = step.CompletedPartCount
                };
                break;

              case StatusRouteStep.RouteTypeE.Reclamp:
                route = new ReclampStep()
                {
                  Reclamp = stats.ToList()
                };
                break;

              case StatusRouteStep.RouteTypeE.Machine:
                var progs = routePrograms.Contains(palAndStep) ? routePrograms[palAndStep] : Enumerable.Empty<int>();
                route = new MachiningStep()
                {
                  Machines = stats.ToList(),
                  ProgramNumsToRun = progs.ToList()
                };
                break;

              case StatusRouteStep.RouteTypeE.Wash:
                route = new WashStep()
                {
                  WashStations = stats.ToList(),
                  WashingPattern = step.WashingPattern
                };
                break;

              default:
                route = null;
                break;
            }

            var pal = pallets[step.PalletNum];
            pal.Master.Routes.Add(route);
            pal.Tracking.ExecutedStationNumber.Add(step.ExecutedStationNum);
          }

          status.Pallets = pallets.Values.ToList();

          status.Programs = conn.Query<ProgramEntry>(
            $@"SELECT o_no AS {nameof(ProgramEntry.ProgramNum)},
                      comment AS {nameof(ProgramEntry.Comment)},
                      work_base_time AS {nameof(ProgramEntry.WorkBaseTimeSeconds)}
                FROM status_program
            ",
            transaction: trans
          )
          .ToDictionary(p => p.ProgramNum);

          foreach (var tool in
            conn.Query<(int o_no, int tool_no)>(
              "SELECT o_no, tool_no FROM status_program_tool",
              transaction: trans
            )
          )
          {
            if (!status.Programs.ContainsKey(tool.o_no)) continue;
            status.Programs[tool.o_no].Tools.Add(tool.tool_no);
          }

          trans.Commit();

          return status;
        }
      }
    }

    public void PerformAction(NiigataAction a)
    {
      switch (a)
      {
        case NewPalletRoute newRoute:
          // TODO: send to icc
          break;

        case UpdatePalletQuantities update:
          // TODO: send to icc
          break;

        case NewProgram add:
          // it is possible that a program was deleted from the ICC but the server crashed/stopped before setting the cell controller program null
          // in the job database.  The Assignment code guarantees that a new program number it picks does not exist in the icc so if it exists
          // in the database, it is old leftover from a failed delete and should be cleared.
          var oldProg = _jobs.ProgramFromCellControllerProgram(add.ProgramNum.ToString());
          if (oldProg != null)
          {
            _jobs.SetCellControllerProgramForProgram(oldProg.ProgramName, oldProg.Revision, null);
          }

          // write (or overwrite) the file to disk
          var progCt = _jobs.LoadProgramContent(add.ProgramName, add.ProgramRevision);
          System.IO.File.WriteAllText(System.IO.Path.Combine(_programDir, add.ProgramName + "_rev" + add.ProgramRevision.ToString() + ".EIA"), progCt);

          // TODO: send to icc

          // if we crash at this point, the icc will have the program but it won't be recorded into the job database.  The next time
          // Insight starts, it will add another program with a new ICC number (but identical file).  The old program will eventually be
          // cleaned up since it isn't in use.
          _jobs.SetCellControllerProgramForProgram(add.ProgramName, add.ProgramRevision, add.ProgramNum.ToString());
          break;

        case DeleteProgram delete:
          // TODO: send to icc

          // if we crash after deleting from the icc but before clearing the cell controller program, the above NewProgram check will
          // clear it later.
          _jobs.SetCellControllerProgramForProgram(delete.ProgramName, delete.ProgramRevision, null);
          break;
      }
      throw new NotImplementedException();
    }
  }
}