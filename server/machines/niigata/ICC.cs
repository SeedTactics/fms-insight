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

  // NOTES:
  //   - no program delete? only overwrite?
  //   - change_request_palette_route has a value long_tool_replacement_mc, is that
  //     supposed to be the specific mc that needs replacmenet?


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

    private IEnumerable<int> StepToStations(RouteStep step)
    {
      switch (step)
      {
        case LoadStep load:
          return load.LoadStations;
        case UnloadStep unload:
          return unload.UnloadStations;
        case ReclampStep reclamp:
          return reclamp.Reclamp;
        case WashStep wash:
          return wash.WashStations;
        case MachiningStep machining:
          return machining.Machines;
        default:
          throw new Exception("Invalid route step type");
      }
    }

    private IEnumerable<int> StepToPrograms(RouteStep step)
    {
      switch (step)
      {
        case MachiningStep machining:
          return machining.ProgramNumsToRun;
        default:
          return Enumerable.Empty<int>();
      }
    }

    public void PerformAction(NiigataAction a)
    {
      switch (a)
      {
        case NewPalletRoute newRoute:
          using (var conn = new NpgsqlConnection(_connStr))
          {
            conn.Open();
            using (var trans = conn.BeginTransaction())
            {

              var palParams = new DynamicParameters();
              palParams.Add("ProposalId", newRoute.ProposalID);
              palParams.Add("Delete", false);
              palParams.AddDynamicParams(newRoute.NewMaster);

              conn.Execute(
                $@"INSERT INTO proposal_pallet_route(
                    proposal_id,
                    pallet_no,
                    comment,
                    remaining_palette_cycle,
                    priority,
                    no_work,
                    pallet_skip,
                    program_download,
                    for_long_tool_maintenance,
                    delete
                  ) VALUES (
                    @ProposalId,
                    @{nameof(PalletMaster.PalletNum)},
                    @{nameof(PalletMaster.Comment)},
                    @{nameof(PalletMaster.RemainingPalletCycles)},
                    @{nameof(PalletMaster.Priority)},
                    @{nameof(PalletMaster.NoWork)},
                    @{nameof(PalletMaster.Skip)},
                    @{nameof(PalletMaster.PerformProgramDownload)},
                    @{nameof(PalletMaster.ForLongToolMaintenance)},
                    @Delete
                  )
                ",
                param: palParams,
                transaction: trans
              );

              conn.Execute(
                $@"INSERT INTO proposal_pallet_route_step(
                    proposal_id,
                    route_no,
                    route_type,
                    completed_part_count,
                    washing_pattern
                  ) VALUES (
                    ProposalId,
                    RouteNo,
                    RouteType,
                    CompletedCount,
                    WashingPattern
                  )",
                  transaction: trans,
                  param: newRoute.NewMaster.Routes.Select((step, idx) =>
                  {
                    switch (step)
                    {
                      case LoadStep load:
                        return new
                        {
                          ProposalId = newRoute.ProposalID,
                          RouteNo = idx + 1,
                          RouteType = StatusRouteStep.RouteTypeE.Load,
                          CompletedCount = 0,
                          WashingPattern = 0
                        };
                      case UnloadStep unload:
                        return new
                        {
                          ProposalId = newRoute.ProposalID,
                          RouteNo = idx + 1,
                          RouteType = StatusRouteStep.RouteTypeE.Unload,
                          CompletedCount = unload.CompletedPartCount,
                          WashingPattern = 0
                        };
                      case ReclampStep reclamp:
                        return new
                        {
                          ProposalId = newRoute.ProposalID,
                          RouteNo = idx + 1,
                          RouteType = StatusRouteStep.RouteTypeE.Reclamp,
                          CompletedCount = 0,
                          WashingPattern = 0
                        };
                      case MachiningStep machine:
                        return new
                        {
                          ProposalId = newRoute.ProposalID,
                          RouteNo = idx + 1,
                          RouteType = StatusRouteStep.RouteTypeE.Machine,
                          CompletedCount = 0,
                          WashingPattern = 0
                        };
                      case WashStep wash:
                        return new
                        {
                          ProposalId = newRoute.ProposalID,
                          RouteNo = idx + 1,
                          RouteType = StatusRouteStep.RouteTypeE.Machine,
                          CompletedCount = 0,
                          WashingPattern = wash.WashingPattern
                        };
                      default:
                        throw new Exception("Unknown route step");
                    }
                  })
              );

              conn.Execute(
                $@"INSERT INTO proposal_route_step_station(
                    proposal_id,
                    route_no,
                    station_no
                  ) VALUES (
                    @ProposalId,
                    @RouteNo,
                    @StationNo
                  )
                ",
                transaction: trans,
                param:
                  newRoute.NewMaster.Routes
                  .SelectMany((step, idx) =>
                    StepToStations(step)
                    .Select(stat => new
                    {
                      PropsoalId = newRoute.ProposalID,
                      RouteNo = idx + 1,
                      StationNo = stat
                    })
                  )
              );

              conn.Execute(
                $@"INSERT INTO proposal_route_step_program(
                    proposal_id,
                    route_no,
                    program_order,
                    o_no
                  ) VALUES (
                    @ProposalId,
                    @RouteNo,
                    @ProgramOrder,
                    @ProgramNum
                  )
                ",
                transaction: trans,
                param:
                  newRoute.NewMaster.Routes
                  .SelectMany((step, stepIdx) =>
                    StepToPrograms(step)
                    .Select((prog, progIdx) => new
                    {
                      PropsoalId = newRoute.ProposalID,
                      RouteNo = stepIdx + 1,
                      ProgramOrder = progIdx + 1,
                      ProgramNum = prog
                    })
                  )
              );

              trans.Commit();
            }
          }
          break;

        case UpdatePalletQuantities update:
          using (var conn = new NpgsqlConnection(_connStr))
          {
            conn.Open();
            using (var trans = conn.BeginTransaction())
            {
              var updateParams = new DynamicParameters();
              updateParams.Add("ChangeId", 10000); // TODO: fix me
              updateParams.AddDynamicParams(update);

              conn.Execute(
                $@"INSERT INTO change_request_palette_route(
                    change_id,
                    pallet_no,
                    remaining_palette_cycle,
                    priority,
                    no_work,
                    pallet_skip,
                    long_tool_replacement_mc,
                  ) VALUES (
                    @ChangeId,
                    @{nameof(UpdatePalletQuantities.Pallet)},
                    @{nameof(UpdatePalletQuantities.Cycles)},
                    @{nameof(UpdatePalletQuantities.Priority)},
                    @{nameof(UpdatePalletQuantities.NoWork)},
                    @{nameof(UpdatePalletQuantities.Skip)},
                    @{nameof(UpdatePalletQuantities.LongToolMachine)}
                  )
                ",
                param: updateParams,
                transaction: trans
              );
              trans.Commit();
            }
          }
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
          var fullPath = System.IO.Path.Combine(_programDir, add.ProgramName + "_rev" + add.ProgramRevision.ToString() + ".EIA");
          System.IO.File.WriteAllText(fullPath, progCt);

          using (var conn = new NpgsqlConnection(_connStr))
          {
            conn.Open();
            using (var trans = conn.BeginTransaction())
            {
              conn.Execute(
                $@"INSERT INTO register_program(
                    registration_id,
                    file_name,
                    o_no,
                    comment,
                    work_base_time,
                    overwrite
                  ) VALUES (
                    @RegId,
                    @FileName,
                    @ProgNum,
                    @Comment,
                    @WorkTime,
                    @Overwrite
                  )
                  ",
                param: new
                {
                  RegId = 100,
                  FileName = fullPath,
                  ProgNum = add.ProgramNum,
                  Comment = add.IccProgramComment,
                  WorkTime = 0,
                  Overwrite = true
                },
                transaction: trans
              );

              conn.Execute(
                $@"INSERT INTO register_program_tool(
                    registration_id,
                    tool_no
                  ) VALUES (
                    @RegId,
                    @ToolNo
                  )
                ",
                param: add.Tools.Select(t => new
                {
                  RegId = 100,
                  ToolNo = t
                }),
                transaction: trans
              );

              trans.Commit();
            }
          }

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