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

#nullable enable

namespace BlackMaple.MachineFramework
{
  public interface IMachineControl
  {
    ImmutableList<ToolInMachine> CurrentToolsInMachines();
    ImmutableList<ToolInMachine> CurrentToolsInMachine(string machineGroup, int machineNum);
    ImmutableList<ProgramInCellController> CurrentProgramsInCellController();
    ImmutableList<ProgramRevision> ProgramRevisionsInDecendingOrderOfRevision(
      string programName,
      int count,
      long? revisionToStart
    );
    string GetProgramContent(string programName, long? revision);
  }

  public interface ICellState
  {
    CurrentStatus CurrentStatus { get; }
    bool StateUpdated { get; }
    TimeSpan TimeUntilNextRefresh { get; }
  }

  public interface ISynchronizeCellState<St>
    where St : ICellState
  {
    event Action NewCellState;

    IEnumerable<string> CheckNewJobs(IRepository db, NewJobs jobs);
    St CalculateCellState(IRepository db);
    bool ApplyActions(IRepository db, St st);
    bool DecrementJobs(IRepository db, St st);

    public bool AddJobsAsCopiedToSystem { get; }
    public bool AllowQuarantineToCancelLoad { get; }
  }

  public interface IAdditionalCheckJobs
  {
    IEnumerable<string> CheckNewJobs(IRepository db, NewJobs jobs);
  }

  public interface IPrintLabelForMaterial
  {
    public void PrintLabel(long materialId, int process, Uri httpReferer);
  }

  public interface ICalculateInstructionPath
  {
    public string? InstructionPath(
      string part,
      int? process,
      string type,
      long? materialID,
      string? operatorName,
      int pallet
    );
  }

  public interface IParseBarcode
  {
    public ScannedMaterial? ParseBarcode(string barcode, Uri httpReferer);
  }

  public interface ICheckLicense
  {
    public DateTime? LicenseExpires();
  }

  public interface IProgramContentForJobs
  {
    public ImmutableList<NewProgramContent>? ProgramsForJobs(IEnumerable<Job> jobs);
  }
}
