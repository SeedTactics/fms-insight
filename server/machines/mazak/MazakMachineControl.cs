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
using BlackMaple.MachineWatchInterface;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface
{
  public class MazakMachineControl : IMachineControl
  {
    private JobDB.Config _jobDbCfg;
    private IReadDataAccess _readData;
    private IMachineGroupName _machGroupName;

    public MazakMachineControl(JobDB.Config jobDbCfg, IReadDataAccess readData, IMachineGroupName machineGroupName)
    {
      _jobDbCfg = jobDbCfg;
      _readData = readData;
      _machGroupName = machineGroupName;
    }

    public List<ProgramInCellController> CurrentProgramsInCellController()
    {
      using (var jobDb = _jobDbCfg.OpenConnection())
      {
        return _readData.LoadPrograms().Select(p =>
        {
          var prog = jobDb.ProgramFromCellControllerProgram(p.MainProgram);
          return new ProgramInCellController()
          {
            MachineGroupName = _machGroupName.MachineGroupName,
            CellControllerProgramName = p.MainProgram,
            ProgramName = prog?.ProgramName ?? p.MainProgram,
            Revision = prog?.Revision,
            Comment = p.Comment
          };
        }).ToList();
      }
    }

    public List<ToolInMachine> CurrentToolsInMachines()
    {
      return _readData.LoadTools()
        .Where(t => t.MachineNumber.HasValue && (t.IsToolDataValid ?? false) && t.PocketNumber.HasValue && !string.IsNullOrEmpty(t.GroupNo))
        .Select(t => new ToolInMachine()
        {
          MachineGroupName = _machGroupName.MachineGroupName,
          MachineNum = t.MachineNumber.Value,
          Pocket = t.PocketNumber.Value,
          ToolName = t.GroupNo,
          CurrentUse = TimeSpan.FromSeconds(t.LifeUsed ?? 0),
          TotalLifeTime = TimeSpan.FromSeconds(t.LifeSpan ?? 0)
        }).ToList();
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
      }

      if (_readData.CheckProgramExists(programName) && System.IO.File.Exists(programName))
      {
        return System.IO.File.ReadAllText(programName);
      }

      return "";
    }

    public List<ProgramRevision> ProgramRevisionsInDecendingOrderOfRevision(string programName, int count, long? revisionToStart)
    {
      using (var jobDb = _jobDbCfg.OpenConnection())
      {
        return jobDb.LoadProgramRevisionsInDescendingOrderOfRevision(programName, count, revisionToStart);
      }
    }
  }
}