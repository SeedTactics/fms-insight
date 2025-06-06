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

namespace MazakMachineInterface
{
  public class MazakMachineControl(RepositoryConfig jobDbCfg, IMazakDB readData, MazakConfig mazakCfg)
    : IMachineControl
  {
    public ImmutableList<ProgramInCellController> CurrentProgramsInCellController()
    {
      var programs = readData.LoadPrograms();
      using (var jobDb = jobDbCfg.OpenConnection())
      {
        return programs
          .Select(p =>
          {
            ProgramRevision prog = null;
            if (
              MazakProcess.TryParseMainProgramComment(
                p.Comment,
                out var progFromComent,
                out var revFromComment
              )
            )
            {
              prog = jobDb.LoadProgram(progFromComent, revFromComment);
              if (prog == null)
              {
                prog = new ProgramRevision() { ProgramName = progFromComent, Revision = revFromComment };
              }
            }
            return new ProgramInCellController()
            {
              ProgramName = prog?.ProgramName ?? p.MainProgram,
              CellControllerProgramName = p.MainProgram,
              Revision = prog?.Revision,
              Comment = prog?.Comment ?? p.Comment,
            };
          })
          .ToImmutableList();
      }
    }

    public ImmutableList<ToolInMachine> CurrentToolsInMachines()
    {
      string machineGroupName;
      using (var db = jobDbCfg.OpenConnection())
      {
        machineGroupName = BuildCurrentStatus.FindMachineGroupName(db);
      }

      return readData
        .LoadTools()
        .Where(t =>
          t.MachineNumber.HasValue
          && (t.IsToolDataValid ?? false)
          && t.PocketNumber.HasValue
          && !string.IsNullOrEmpty(t.GroupNo)
        )
        .Select(t => new ToolInMachine()
        {
          MachineGroupName = machineGroupName,
          MachineNum = t.MachineNumber.Value,
          Pocket = t.PocketNumber.Value,
          ToolName = mazakCfg?.ExtractToolName == null ? t.GroupNo : mazakCfg.ExtractToolName(t),
          Serial = null,
          CurrentUse = TimeSpan.FromSeconds(t.LifeUsed ?? 0),
          TotalLifeTime = TimeSpan.FromSeconds(t.LifeSpan ?? 0),
          CurrentUseCount = null,
          TotalLifeCount = null,
        })
        .ToImmutableList();
    }

    public ImmutableList<ToolInMachine> CurrentToolsInMachine(string machineGroup, int machineNum)
    {
      return CurrentToolsInMachines()
        .Where(t => t.MachineGroupName == machineGroup && t.MachineNum == machineNum)
        .ToImmutableList();
    }

    public string GetProgramContent(string programName, long? revision)
    {
      using (var jobDb = jobDbCfg.OpenConnection())
      {
        if (!revision.HasValue)
        {
          revision = jobDb.LoadMostRecentProgram(programName)?.Revision;
        }
        if (revision.HasValue)
        {
          return jobDb.LoadProgramContent(programName, revision.Value);
        }
      }

      if (System.IO.File.Exists(programName))
      {
        return System.IO.File.ReadAllText(programName);
      }

      return "";
    }

    public ImmutableList<ProgramRevision> ProgramRevisionsInDecendingOrderOfRevision(
      string programName,
      int count,
      long? revisionToStart
    )
    {
      using (var jobDb = jobDbCfg.OpenConnection())
      {
        return jobDb.LoadProgramRevisionsInDescendingOrderOfRevision(programName, count, revisionToStart);
      }
    }
  }
}
