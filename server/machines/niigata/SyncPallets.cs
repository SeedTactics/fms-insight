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
using System.Linq;
using System.Collections.Generic;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;
using System.Threading;

namespace BlackMaple.FMSInsight.Niigata
{
  public class JobPallet
  {
    public PalletMaster Master { get; set; }
    public TrackingInfo Tracking { get; set; }
    public NiigataPalletLocation Loc { get; set; } = new StockerLoc();
    public List<InProcessMaterial> Material { get; set; }
  }
  public delegate void NewJobPallets(IList<JobPallet> allPallets);
  public interface ISyncPallets
  {
    void JobsOrQueuesChanged();
    void DecrementPlannedButNotStartedQty();
    event NewJobPallets OnPalletsChanged;
  }

  public class SyncPallets : ISyncPallets, IDisposable
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<SyncPallets>();
    private JobDB _jobs;
    private JobLogDB _log;

    public SyncPallets(JobDB jobs, JobLogDB log)
    {
      _jobs = jobs;
      _log = log;
      _thread = new Thread(new ThreadStart(Thread));
      _thread.Start();
    }

    #region Thread and Messages
    private System.Threading.Thread _thread;
    private AutoResetEvent _shutdown = new AutoResetEvent(false);
    private AutoResetEvent _recheck = new AutoResetEvent(false);
    private object _changeLock = new object();

    void IDisposable.Dispose()
    {
      _shutdown.Set();
      if (_thread != null && !_thread.Join(TimeSpan.FromSeconds(15)))
        _thread.Abort();
    }

    public void JobsOrQueuesChanged()
    {
      _recheck.Set();
    }

    private void Thread()
    {
      while (true)
      {
        try
        {

          var sleepTime = TimeSpan.FromMinutes(1);
          Log.Debug("Sleeping for {mins} minutes", sleepTime.TotalMinutes);
          var ret = WaitHandle.WaitAny(new WaitHandle[] { _shutdown, _recheck }, sleepTime, false);
          if (ret == 0)
          {
            Log.Debug("Thread shutdown");
            return;
          }

          try
          {
            lock (_changeLock)
            {
              CheckPalletsMatch(raiseNewStatus: ret == 1);
            }
          }
          catch (Exception ex)
          {
            Log.Error(ex, "Error checking pallets");
          }

        }
        catch (Exception ex)
        {
          Log.Error(ex, "Unexpected error during thread processing");
        }
      }
    }
    #endregion

    public event NewJobPallets OnPalletsChanged;

    private void CheckPalletsMatch(bool raiseNewStatus)
    {
      var allPals = new List<JobPallet>();

      // TODO: write me

      if (raiseNewStatus)
      {
        OnPalletsChanged?.Invoke(allPals);
      }
    }

    public void DecrementPlannedButNotStartedQty()
    {
      // lock prevents decrement from occuring at the same time as the CheckPalletsMatch function
      // is deciding what to put onto a pallet
      lock (_changeLock)
      {
        var decrs = new List<JobDB.NewDecrementQuantity>();
        foreach (var j in _jobs.LoadUnarchivedJobs().Jobs)
        {
          if (_jobs.LoadDecrementsForJob(j.UniqueStr).Count > 0) continue;
          var started = CountStartedOrInProcess(j);
          var planned = Enumerable.Range(1, j.GetNumPaths(process: 1)).Sum(path => j.GetPlannedCyclesOnFirstProcess(path));
          if (started < planned)
          {
            decrs.Add(new JobDB.NewDecrementQuantity()
            {
              JobUnique = j.UniqueStr,
              Part = j.PartName,
              Quantity = planned - started
            });
          }
        }

        if (decrs.Count > 0)
        {
          _jobs.AddNewDecrement(decrs);
        }
      }
    }

    private int CountStartedOrInProcess(JobPlan job)
    {
      var mats = new HashSet<long>();

      foreach (var e in _log.GetLogForJobUnique(job.UniqueStr))
      {
        foreach (var mat in e.Material)
        {
          if (mat.JobUniqueStr == job.UniqueStr)
          {
            mats.Add(mat.MaterialID);
          }
        }
      }

      //TODO: add material for pallet which is moving to load station to receive new part to load?

      return mats.Count;
    }


  }
}