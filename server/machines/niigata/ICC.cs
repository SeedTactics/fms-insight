/* Copyright (c) 2019, John Lenz

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
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Niigata
{
  public class NiigataICC : INiigataCommunication
  {
    private JobDB _jobs;
    private string _programDir;

    public NiigataICC(JobDB j, string progDir)
    {
      _jobs = j;
      _programDir = progDir;
    }

    public NiigataStatus LoadStatus()
    {
      throw new NotImplementedException();
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