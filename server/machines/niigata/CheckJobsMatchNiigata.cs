/* Copyright (c) 2022, John Lenz

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

namespace BlackMaple.FMSInsight.Niigata
{
  public sealed class CheckJobsMatchNiigata : ICheckJobsValid
  {
    private readonly FMSSettings _settings;
    private readonly NiigataStationNames _statNames;
    private readonly bool _requireProgramsInJobs;
    private readonly INiigataCommunication _icc;

    public CheckJobsMatchNiigata(
      FMSSettings settings,
      NiigataStationNames statNames,
      bool requireProgramsInJobs,
      INiigataCommunication icc
    )
    {
      _settings = settings;
      _statNames = statNames;
      _requireProgramsInJobs = requireProgramsInJobs;
      _icc = icc;
    }

    public IReadOnlyList<string> CheckNewJobs(IRepository jobDB, NewJobs jobs)
    {
      var errors = new List<string>();
      var programs = _icc.LoadPrograms().Values;
      foreach (var j in jobs.Jobs)
      {
        for (var proc = 1; proc <= j.Processes.Count; proc++)
        {
          for (var path = 1; path <= j.Processes[proc - 1].Paths.Count; path++)
          {
            var pathData = j.Processes[proc - 1].Paths[path - 1];
            if (!pathData.Load.Any())
            {
              errors.Add("Part " + j.PartName + " does not have any assigned load stations");
            }
            if (!pathData.Unload.Any())
            {
              errors.Add("Part " + j.PartName + " does not have any assigned load stations");
            }
            if (string.IsNullOrEmpty(pathData.Fixture))
            {
              errors.Add("Part " + j.PartName + " does not have an assigned fixture");
            }
            if (!pathData.PalletNums.Any())
            {
              errors.Add("Part " + j.PartName + " does not have any pallets");
            }
            if (
              !string.IsNullOrEmpty(pathData.InputQueue) && !_settings.Queues.ContainsKey(pathData.InputQueue)
            )
            {
              errors.Add(
                " Part "
                  + j.PartName
                  + " has an input queue "
                  + pathData.InputQueue
                  + " which is not configured as a local queue in FMS Insight."
              );
            }
            if (
              !string.IsNullOrEmpty(pathData.OutputQueue)
              && !_settings.Queues.ContainsKey(pathData.OutputQueue)
            )
            {
              errors.Add(
                " Part "
                  + j.PartName
                  + " has an output queue "
                  + pathData.OutputQueue
                  + " which is not configured as a queue in FMS Insight."
              );
            }

            foreach (var stop in pathData.Stops)
            {
              if (_statNames != null && _statNames.ReclampGroupNames.Contains(stop.StationGroup))
              {
                if (!stop.Stations.Any())
                {
                  errors.Add(
                    "Part "
                      + j.PartName
                      + " does not have any assigned load stations for intermediate load stop"
                  );
                }
              }
              else
              {
                if (string.IsNullOrEmpty(stop.Program))
                {
                  if (_requireProgramsInJobs)
                  {
                    errors.Add("Part " + j.PartName + " has no assigned program");
                  }
                }
                else
                {
                  CheckProgram(
                    stop.Program,
                    stop.ProgramRevision,
                    jobs.Programs,
                    jobDB,
                    programs,
                    "Part " + j.PartName,
                    errors
                  );
                }
              }
            }
          }
        }
      }

      foreach (var w in jobs.CurrentUnfilledWorkorders ?? Enumerable.Empty<Workorder>())
      {
        if (w.Programs != null)
        {
          foreach (var prog in w.Programs)
          {
            CheckProgram(
              prog.ProgramName,
              prog.Revision,
              jobs.Programs,
              jobDB,
              programs,
              "Workorder " + w.WorkorderId,
              errors
            );
          }
        }
      }

      return errors;
    }

    private void CheckProgram(
      string programName,
      long? rev,
      IEnumerable<MachineFramework.NewProgramContent> newPrograms,
      IRepository jobDB,
      IEnumerable<ProgramEntry> programsInCellCtrl,
      string errHdr,
      IList<string> errors
    )
    {
      if (rev.HasValue && rev.Value > 0)
      {
        var existing = jobDB.LoadProgram(programName, rev.Value) != null;
        var newProg =
          newPrograms != null
          && newPrograms.Any(p => p.ProgramName == programName && p.Revision == rev.Value);
        if (!existing && !newProg)
        {
          errors.Add(errHdr + " program " + programName + " rev" + rev.Value.ToString() + " is not found");
        }
      }
      else
      {
        var existing = jobDB.LoadMostRecentProgram(programName) != null;
        var newProg = newPrograms != null && newPrograms.Any(p => p.ProgramName == programName);
        if (!existing && !newProg)
        {
          if (int.TryParse(programName, out int progNum))
          {
            if (
              !programsInCellCtrl.Any(
                p => p.ProgramNum == progNum && !AssignNewRoutesOnPallets.IsInsightProgram(p)
              )
            )
            {
              errors.Add(
                errHdr
                  + " program "
                  + programName
                  + " is neither included in the download nor found in the cell controller"
              );
            }
          }
          else
          {
            errors.Add(
              errHdr + " program " + programName + " is neither included in the download nor is an integer"
            );
          }
        }
      }
    }

    public IReadOnlyList<string> CheckWorkorders(
      IRepository db,
      IEnumerable<Workorder> newWorkorders,
      IEnumerable<NewProgramContent> programs
    )
    {
      var errors = new List<string>();
      var iccProgs = _icc.LoadPrograms().Values;
      foreach (var w in newWorkorders ?? Enumerable.Empty<Workorder>())
      {
        if (w.Programs != null)
        {
          foreach (var prog in w.Programs)
          {
            CheckProgram(
              prog.ProgramName,
              prog.Revision,
              programs,
              db,
              iccProgs,
              "Workorder " + w.WorkorderId,
              errors
            );
          }
        }
      }
      return errors;
    }

    public bool ExcludeJobFromDecrement(IRepository db, Job j)
    {
      return false;
    }
  }
}
