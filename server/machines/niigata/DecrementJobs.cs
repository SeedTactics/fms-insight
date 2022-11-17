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

namespace BlackMaple.FMSInsight.Niigata
{
  public interface IDecrementJobs
  {
    bool DecrementJobs(IRepository jobDB, CellState cellSt);
  }

  public class DecrementNotYetStartedJobs : IDecrementJobs
  {
    private Func<Job, bool> _jobCanBeDecremented;

    public DecrementNotYetStartedJobs(Func<Job, bool> jobCanBeDecremented = null)
    {
      _jobCanBeDecremented = jobCanBeDecremented;
    }

    private static Serilog.ILogger Log = Serilog.Log.ForContext<DecrementNotYetStartedJobs>();

    public bool DecrementJobs(IRepository jobDB, CellState cellSt)
    {
      var decrs = new List<NewDecrementQuantity>();
      foreach (var j in cellSt.CurrentStatus.Jobs.Values)
      {
        if (j.ManuallyCreated || j.Decrements?.Count > 0) continue;

        if (_jobCanBeDecremented != null && !_jobCanBeDecremented(j)) continue;

        int toStart = (int)(j.RemainingToStart ?? 0);
        if (toStart > 0)
        {
          decrs.Add(new NewDecrementQuantity()
          {
            JobUnique = j.UniqueStr,
            Part = j.PartName,
            Quantity = toStart
          });
        }
      }

      if (decrs.Count > 0)
      {
        jobDB.AddNewDecrement(decrs, nowUTC: cellSt?.Status?.TimeOfStatusUTC);
        return true;
      }
      else
      {
        return false;
      }
    }
  }

}