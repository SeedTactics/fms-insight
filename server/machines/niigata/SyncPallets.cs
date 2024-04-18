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

#nullable enable

using System;
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Niigata
{
  public sealed class SyncNiigataPallets : ISynchronizeCellState<CellState>
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<SyncNiigataPallets>();

    private readonly INiigataCommunication _icc;
    private readonly IBuildCellState _createLog;
    private readonly IAssignPallets _assign;
    private readonly Func<ActiveJob, bool>? _decrementJobFilter;

    public SyncNiigataPallets(
      INiigataCommunication icc,
      IBuildCellState createLog,
      IAssignPallets assign,
      Func<ActiveJob, bool>? decrementJobFilter
    )
    {
      _icc = icc;
      _createLog = createLog;
      _assign = assign;
      _decrementJobFilter = decrementJobFilter;
    }

    public event Action NewCellState
    {
      add { _icc.NewCurrentStatus += value; }
      remove { _icc.NewCurrentStatus -= value; }
    }

    public CellState CalculateCellState(IRepository db)
    {
      var status = _icc.LoadNiigataStatus();
      Log.Debug("Loaded pallets {@status}", status);
      return _createLog.BuildCellState(db, status);
    }

    public bool ApplyActions(IRepository db, CellState st)
    {
      var action = _assign.NewPalletChange(st);
      if (action != null)
      {
        Log.Debug("Executing action pallet to {@change}", action);
        _icc.PerformAction(db, action);
        return true;
      }
      else
      {
        return false;
      }
    }

    public bool DecrementJobs(IRepository db, CurrentStatus st)
    {
      var newDecrs = st.BuildJobsToDecrement(db, decrementJobFilter: _decrementJobFilter);

      if (newDecrs.Count > 0)
      {
        db.AddNewDecrement(newDecrs, nowUTC: st?.TimeOfCurrentStatusUTC);
        return true;
      }
      else
      {
        return false;
      }
    }
  }
}
