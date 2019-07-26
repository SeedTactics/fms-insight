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

namespace BlackMaple.FMSInsight.Niigata
{
  public interface ISyncPallets
  {
    void RecheckPallets();
    void DecrementPlannedButNotStartedQty();
  }

  public class SyncPallets : ISyncPallets, IDisposable
  {
    private object _lock = new object();
    private JobDB _jobs;
    private IBuildCurrentStatus _curSt;

    public SyncPallets(JobDB jobs, IBuildCurrentStatus curSt)
    {
      _jobs = jobs;
      _curSt = curSt;
    }

    public void RecheckPallets()
    {
      // TODO: implement
    }

    public void DecrementPlannedButNotStartedQty()
    {
      // once a decrement is recorded here, the SyncPallets won't start any more new material
      // need to lock so that between loading jobs and recording the decrement, we dont't start a new material
      lock (_lock)
      {
        var decrs = new List<JobDB.NewDecrementQuantity>();
        foreach (var j in _jobs.LoadUnarchivedJobs().Jobs)
        {
          if (_jobs.LoadDecrementsForJob(j.UniqueStr).Count > 0) continue;
          var started = _curSt.CountStartedOrInProcess(j);
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

    public void Dispose() { }
  }
}