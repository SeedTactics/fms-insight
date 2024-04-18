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
using System.Collections.Immutable;
using System.Linq;
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Niigata
{
  public class NiigataMachineControl : IMachineControl
  {
    private RepositoryConfig _jobDbCfg;
    private INiigataCommunication _icc;
    private ICncMachineConnection _cnc;
    private NiigataStationNames _statNames;

    public NiigataMachineControl(
      RepositoryConfig jobDbCfg,
      INiigataCommunication icc,
      ICncMachineConnection cnc,
      NiigataStationNames statNames
    )
    {
      _jobDbCfg = jobDbCfg;
      _icc = icc;
      _cnc = cnc;
      _statNames = statNames;
    }

    public ImmutableList<ProgramInCellController> CurrentProgramsInCellController()
    {
      var programs = _icc.LoadPrograms();
      using (var jobDb = _jobDbCfg.OpenConnection())
      {
        return programs
          .Values.Select(p =>
          {
            if (AssignNewRoutesOnPallets.TryParseProgramComment(p, out string progName, out long rev))
            {
              var jobProg = jobDb.LoadProgram(progName, rev);
              return new ProgramInCellController()
              {
                CellControllerProgramName = p.ProgramNum.ToString(),
                ProgramName = progName,
                Revision = rev,
                Comment = jobProg?.Comment
              };
            }
            else
            {
              return new ProgramInCellController()
              {
                CellControllerProgramName = p.ProgramNum.ToString(),
                ProgramName = p.ProgramNum.ToString(),
                Comment = p.Comment
              };
            }
          })
          .ToImmutableList();
      }
    }

    public ImmutableList<ToolInMachine> CurrentToolsInMachines()
    {
      return _statNames
        .IccMachineToJobMachNames.SelectMany(k =>
          (_cnc.ToolsForMachine(k.Key) ?? new List<NiigataToolData>()).Select(t =>
            t.ToToolInMachine(k.Value.group, k.Value.num)
          )
        )
        .ToImmutableList();
    }

    public ImmutableList<ToolInMachine> CurrentToolsInMachine(string machineGroup, int machineNum)
    {
      var iccNum = _statNames.JobMachToIcc(machineGroup, machineNum);
      return (_cnc.ToolsForMachine(iccNum) ?? new List<NiigataToolData>())
        .Select(t => t.ToToolInMachine(machineGroup, machineNum))
        .ToImmutableList();
    }

    public string GetProgramContent(string programName, long? revision)
    {
      using (var jobDb = _jobDbCfg.OpenConnection())
      {
        if (!revision.HasValue)
        {
          revision = jobDb.LoadMostRecentProgram(programName)?.Revision;
        }
        if (revision.HasValue)
        {
          return jobDb.LoadProgramContent(programName, revision.Value);
        }
        else
        {
          return "";
        }
      }
    }

    public ImmutableList<ProgramRevision> ProgramRevisionsInDecendingOrderOfRevision(
      string programName,
      int count,
      long? revisionToStart
    )
    {
      using (var jobDb = _jobDbCfg.OpenConnection())
      {
        return jobDb.LoadProgramRevisionsInDescendingOrderOfRevision(programName, count, revisionToStart);
      }
    }
  }
}
