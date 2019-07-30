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
using System.Threading.Tasks;

namespace BlackMaple.FMSInsight.Niigata
{
  public class JobPallet
  {
    public PalletMaster Master { get; set; }
    public TrackingInfo Tracking { get; set; }
    public NiigataPalletLocation Loc { get; set; } = new StockerLoc();
    public List<InProcessMaterial> Material { get; set; }
  }
  public interface ISyncPallets
  {
    Task RecheckPallets();
    Task<IEnumerable<JobPallet>> CurrentPallets();
    Task DecrementPlannedButNotStartedQty();
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
    private Queue<Message> _msgQueue = new Queue<Message>();
    private AutoResetEvent _newMsg = new AutoResetEvent(false);

    private abstract class Message { }

    private class RecheckPalletsMessage : Message
    {
      public TaskCompletionSource<bool> Comp { get; } = new TaskCompletionSource<bool>();
    }

    public Task RecheckPallets()
    {
      var msg = new RecheckPalletsMessage();
      lock (_msgQueue)
      {
        _msgQueue.Enqueue(msg);
      }
      _newMsg.Set();
      return msg.Comp.Task;
    }

    private class CurrentPalletsMessage : Message
    {
      public TaskCompletionSource<IEnumerable<JobPallet>> Comp { get; } = new TaskCompletionSource<IEnumerable<JobPallet>>();
    }

    public Task<IEnumerable<JobPallet>> CurrentPallets()
    {
      var msg = new CurrentPalletsMessage();
      lock (_msgQueue)
      {
        _msgQueue.Enqueue(msg);
      }
      return msg.Comp.Task;
    }

    private class DecrementMessage : Message
    {
      public TaskCompletionSource<bool> Comp { get; } = new TaskCompletionSource<bool>();
    }

    public Task DecrementPlannedButNotStartedQty()
    {
      var msg = new DecrementMessage();
      lock (_msgQueue)
      {
        _msgQueue.Enqueue(msg);
      }
      return msg.Comp.Task;
    }
    public void Dispose()
    {
      _shutdown.Set();
      if (_thread != null && !_thread.Join(TimeSpan.FromSeconds(15)))
        _thread.Abort();
    }

    private void Thread()
    {
      while (true)
      {
        try
        {

          var sleepTime = TimeSpan.FromMinutes(1);
          Log.Debug("Sleeping for {mins} minutes", sleepTime.TotalMinutes);
          var ret = WaitHandle.WaitAny(new WaitHandle[] { _shutdown, _newMsg }, sleepTime, false);
          if (ret == 0)
          {
            Log.Debug("Thread shutdown");
            return;
          }

          Exception checkError = null;
          IEnumerable<JobPallet> pallets = Enumerable.Empty<JobPallet>();
          try
          {
            pallets = CheckPalletsMatch();
          }
          catch (Exception ex)
          {
            ex = checkError;
            Log.Error(ex, "Error checking pallets");
          }

          while (true)
          {
            Message msg = null;
            lock (_msgQueue)
            {
              if (!_msgQueue.TryDequeue(out msg))
              {
                break;
              }
            }
            if (msg == null) break;

            switch (msg)
            {
              case RecheckPalletsMessage m:
                if (checkError == null)
                {
                  m.Comp.SetResult(true);
                }
                else
                {
                  m.Comp.SetException(checkError);
                }
                break;
              case CurrentPalletsMessage m:
                if (checkError == null)
                {
                  m.Comp.SetResult(pallets);
                }
                else
                {
                  m.Comp.SetException(checkError);
                }
                break;
              case DecrementMessage m:
                if (checkError == null)
                {
                  DecrementHelper();
                  m.Comp.SetResult(true);
                }
                else
                {
                  m.Comp.SetException(checkError);
                }
                break;

              default:
                Log.Error("Unknown message {@msg}", msg);
                break;
            }
          }
        }
        catch (Exception ex)
        {
          Log.Error(ex, "Unexpected error during thread processing");
        }
      }
    }

    #endregion

    private IEnumerable<JobPallet> CheckPalletsMatch()
    {
      // TODO: write me
      return Enumerable.Empty<JobPallet>();
    }

    private void DecrementHelper()
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