/* Copyright (c) 2018, John Lenz

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
using BlackMaple.MachineWatchInterface;
using BlackMaple.MachineFramework;
using Germinate;

namespace Makino
{
  public class Jobs : IJobControl, IOldJobDecrement
  {
    private MakinoDB _db;
    private Func<IRepository> _openJobDB;
    private Action<NewJobs> _onNewJobs;
    private Action _onJobCommentChange;
    private string _xmlPath;
    private bool _onlyOrders;

    public Jobs(MakinoDB db, Func<IRepository> jdb, string xmlPath, bool onlyOrders, Action<NewJobs> onNewJob, Action onJobCommentChange)
    {
      _db = db;
      _openJobDB = jdb;
      _xmlPath = xmlPath;
      _onlyOrders = onlyOrders;
      _onNewJobs = onNewJob;
      _onJobCommentChange = onJobCommentChange;
    }

    public CurrentStatus GetCurrentStatus()
    {
      if (_db == null)
        return new CurrentStatus();
      else
        return _db.LoadCurrentInfo();
    }

    public List<string> CheckValidRoutes(IEnumerable<Job> newJobs)
    {
      return new List<string>();
    }

    public void AddJobs(NewJobs newJ, string expectedPreviousScheduleId, bool waitForCopyToCell)
    {
      var newJobs = new List<JobPlan>();
      using (var jdb = _openJobDB())
      {
        newJ = newJ with
        {
          Jobs = newJ.Jobs.Select(j => j.AdjustAllPaths(path =>
          {
            path.Stops.AdjustAll(stop => stop.StationGroup = "MC");
          })).ToImmutableList(),
        };
        foreach (var j in newJ.Jobs)
        {
          newJobs.Add(LegacyJobConversions.ToLegacyJob(j, copiedToSystem: true, scheduleId: newJ.ScheduleId));
        }

        jdb.AddJobs(newJ, expectedPreviousScheduleId, addAsCopiedToSystem: true);
      }
      OrderXML.WriteOrderXML(System.IO.Path.Combine(_xmlPath, "sail.xml"), newJobs, _onlyOrders);
      _onNewJobs(newJ);
    }

    void IJobControl.SetJobComment(string jobUnique, string comment)
    {
      using (var jdb = _openJobDB())
      {
        jdb.SetJobComment(jobUnique, comment);
      }
      _onJobCommentChange();
    }

    #region Decrement
    List<JobAndDecrementQuantity> IJobControl.DecrementJobQuantites(long loadDecrementsStrictlyAfterDecrementId)
    {
      return new List<JobAndDecrementQuantity>();
    }

    public List<JobAndDecrementQuantity> DecrementJobQuantites(DateTime loadDecrementsAfterTimeUTC)
    {
      return new List<JobAndDecrementQuantity>();
    }

    public Dictionary<JobAndPath, int> OldDecrementJobQuantites()
    {
      return new Dictionary<JobAndPath, int>();
    }

    public void OldFinalizeDecrement()
    {
      //do nothing
    }

    public void ReplaceWorkordersForSchedule(string scheduleId, IEnumerable<PartWorkorder> newWorkorders, IEnumerable<ProgramEntry> programs)
    {
      // do nothing
    }
    #endregion

    #region Queues
    public InProcessMaterial AddUnallocatedPartToQueue(string partName, string queue, string serial, string operatorName = null)
    {
      //do nothing
      return null;
    }
    public List<InProcessMaterial> AddUnallocatedCastingToQueue(string casting, int qty, string queue, IList<string> serial, string operatorName = null)
    {
      //do nothing
      return new List<InProcessMaterial>();
    }

    public InProcessMaterial AddUnprocessedMaterialToQueue(string jobUnique, int process, string queue, int position, string serial, string operatorName = null)
    {
      //do nothing
      return null;
    }

    public void SetMaterialInQueue(long materialId, string queue, int position, string operatorName = null)
    {
      //do nothing
    }

    public void RemoveMaterialFromAllQueues(IList<long> materialId, string operatorName = null)
    {
      //do nothing
    }

    public void SignalMaterialForQuarantine(long materialId, string queue, string operatorName = null)
    {
      // do nothing
    }

    public void SwapMaterialOnPallet(string pallet, long oldMatId, long newMatId, string operatorName = null)
    {
      // do nothing
    }

    public void InvalidatePalletCycle(long matId, int process, string oldMatPutInQueue = null, string operatorName = null)
    {
      // do nothing
    }
    #endregion
  }
}

