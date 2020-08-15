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
using BlackMaple.MachineWatchInterface;
using System.Threading;

namespace BlackMaple.FMSInsight.Niigata
{
  public interface ISyncPallets
  {
    CellState CurrentCellState();
    void JobsOrQueuesChanged();
    void DecrementPlannedButNotStartedQty(JobDB jobDB, EventLogDB logDB);
  }

  public class SyncPallets : ISyncPallets, IDisposable
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<SyncPallets>();
    private JobDB.Config _jobDbCfg;
    private EventLogDB.Config _logDbCfg;
    private INiigataCommunication _icc;
    private IAssignPallets _assign;
    private IBuildCellState _createLog;
    private Action<CurrentStatus> _onNewCurrentStatus;
    private FMSSettings _settings;

    private object _curStLock = new object();
    private CellState _lastCellState = null;

    public CellState CurrentCellState()
    {
      lock (_curStLock)
      {
        return _lastCellState ?? new CellState();
      }
    }

    public SyncPallets(JobDB.Config jobDbCfg, EventLogDB.Config log, INiigataCommunication icc, IAssignPallets assign, IBuildCellState create, FMSSettings settings, Action<CurrentStatus> onNewCurrentStatus)
    {
      _settings = settings;
      _onNewCurrentStatus = onNewCurrentStatus;
      _jobDbCfg = jobDbCfg;
      _logDbCfg = log;
      _icc = icc;
      _assign = assign;
      _createLog = create;
      _icc.NewCurrentStatus += NewCurrentStatus;
      _thread = new Thread(new ThreadStart(Thread));
    }

    public void StartThread()
    {
      _thread.Start();
    }

    #region Thread and Messages
    private System.Threading.Thread _thread;
    private AutoResetEvent _shutdown = new AutoResetEvent(false);
    private AutoResetEvent _recheck = new AutoResetEvent(false);
    private ManualResetEvent _newCurStatus = new ManualResetEvent(false);
    private object _changeLock = new object();

    public void Dispose()
    {
      _icc.NewCurrentStatus -= NewCurrentStatus;
      _shutdown.Set();
      if (_thread != null && _thread.IsAlive && !_thread.Join(TimeSpan.FromSeconds(15)))
        _thread.Abort();
    }

    public void JobsOrQueuesChanged()
    {
      _recheck.Set();
    }

    private void NewCurrentStatus()
    {
      _newCurStatus.Set();
    }

    private void Thread()
    {
      bool raisePalletChanged = true; // very first run should raise new pallet state
      while (true)
      {
        try
        {
          try
          {
            SynchronizePallets(raisePalletChanged);
          }
          catch (Exception ex)
          {
            Log.Error(ex, "Error syncing pallets");
          }

          var sleepTime = TimeSpan.FromMinutes(5);
          var ret = WaitHandle.WaitAny(new WaitHandle[] { _shutdown, _recheck, _newCurStatus }, sleepTime, false);
          if (ret == 0)
          {
            Log.Debug("Thread shutdown");
            return;
          }
          else if (ret == 1)
          {
            Log.Debug("Recalculating cell state due to job changes");
            // recheck and guarantee pallet changed event even if nothing changed
            raisePalletChanged = true;
          }
          else if (ret == 2)
          {
            // reload status from Niigata ICC
            Log.Debug("Reloading status from ICC");
            raisePalletChanged = true;
            // new current status events come in batches when many tables are changed simultaniously, so wait briefly
            // so we only recalculate once
            System.Threading.Thread.Sleep(TimeSpan.FromMilliseconds(500));
          }
          else
          {
            Log.Debug("Timeout, rechecking cell state");
            // timeout
            raisePalletChanged = false;
          }
        }
        catch (Exception ex)
        {
          Log.Fatal(ex, "Unexpected error during thread processing");
        }
      }
    }
    #endregion

    internal void SynchronizePallets(bool raisePalletChanged)
    {
      NiigataStatus status;
      CellState cellSt;

      lock (_changeLock)
      {

        // 1. Load pallet status from Niigata
        // 2. Load unarchived jobs from DB
        // 3. Check if new log entries need to be recorded
        // 4. decide what is currently on each pallet and is currently loaded/unloaded
        // 5. Decide on any changes to pallet routes or planned quantities.

        using (var jdb = _jobDbCfg.OpenConnection())
        using (var logDB = _logDbCfg.OpenConnection())
        {

          NiigataAction action = null;
          do
          {
            _newCurStatus.Reset();
            status = _icc.LoadNiigataStatus();
            var jobs = jdb.LoadUnarchivedJobs();

            Log.Debug("Loaded pallets {@status} and jobs {@jobs}", status, jobs);

            cellSt = _createLog.BuildCellState(jdb, logDB, status, jobs);
            raisePalletChanged = raisePalletChanged || cellSt.PalletStateUpdated;

            Log.Debug("Computed cell state {@pals}", cellSt);

            action = _assign.NewPalletChange(cellSt);

            if (action != null)
            {
              Log.Debug("Executing action pallet to {@change}", action);
              raisePalletChanged = true;
              _icc.PerformAction(jdb, logDB, action);
            }
          } while (action != null);

          lock (_curStLock)
          {
            _lastCellState = cellSt;
          }

          if (raisePalletChanged)
          {
            _onNewCurrentStatus(BuildCurrentStatus.Build(jdb, logDB, cellSt, _settings));
          }
        }
      }

    }

    public void DecrementPlannedButNotStartedQty(JobDB jobDB, EventLogDB logDB)
    {
      // lock prevents decrement from occuring at the same time as the thread
      // is deciding what to put onto a pallet
      lock (_changeLock)
      {
        var jobs = jobDB.LoadUnarchivedJobs();
        var cellSt = _createLog.BuildCellState(jobDB, logDB, _icc.LoadNiigataStatus(), jobs);

        var decrs = new List<JobDB.NewDecrementQuantity>();
        foreach (var j in jobs)
        {
          if (jobDB.LoadDecrementsForJob(j.UniqueStr).Count > 0) continue;

          int qtyToDecr = 0;
          for (int path = 1; path <= j.GetNumPaths(process: 1); path++)
          {
            if (cellSt.JobQtyRemainingOnProc1.TryGetValue((uniq: j.UniqueStr, proc1path: path), out var qty) && qty > 0)
            {
              qtyToDecr += qty;
            }
          }

          Log.Debug("Job {unique} part {part} calculated {qtyToDecr} to decrement", j.UniqueStr, j.PartName, qtyToDecr);
          if (qtyToDecr > 0)
          {
            decrs.Add(new JobDB.NewDecrementQuantity()
            {
              JobUnique = j.UniqueStr,
              Part = j.PartName,
              Quantity = qtyToDecr
            });
          }
        }

        if (decrs.Count > 0)
        {
          jobDB.AddNewDecrement(decrs);
        }

        if (decrs.Count > 0 || cellSt.PalletStateUpdated)
        {
          _onNewCurrentStatus(BuildCurrentStatus.Build(jobDB, logDB, cellSt, _settings));
        }
      }
    }
  }
}