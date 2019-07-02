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
using BlackMaple.MachineWatchInterface;

namespace BlackMaple.FMSInsight.Niigata
{
  public class BuildCurrentStatus
  {
    private JobDB _jobs;
    private JobLogDB _log;
    private ICurrentPallets _curPal;
    private FMSSettings fmsSettings;

    public BuildCurrentStatus(JobDB j, JobLogDB l, FMSSettings s, ICurrentPallets c)
    {
      _jobs = j;
      _log = l;
      _curPal = c;
      fmsSettings = s;
    }

    public CurrentStatus GetCurrentStatus()
    {
      var curStatus = new CurrentStatus();
      foreach (var k in fmsSettings.Queues) curStatus.QueueSizes[k.Key] = k.Value;

      // jobs
      var jobs = _jobs.LoadUnarchivedJobs();
      foreach (var j in jobs.Jobs)
      {
        var curJob = new InProcessJob(j);
        curStatus.Jobs.Add(curJob.UniqueStr, curJob);
        var evts = _log.GetLogForJobUnique(j.UniqueStr);
        foreach (var e in evts)
        {
          if (e.LogType == LogType.LoadUnloadCycle && e.Result == "UNLOAD")
          {
            foreach (var mat in e.Material)
            {
              if (mat.JobUniqueStr == j.UniqueStr)
              {
                curJob.AdjustCompleted(mat.Process, path: 1, x => x + 1);
              }
            }
          }
        }
      }

      // pallets
      var palMaster = _curPal.LoadPallets();
      foreach (var pal in palMaster.Values)
      {
        curStatus.Pallets.Add(pal.PalletNum.ToString(), new PalletStatus()
        {
          Pallet = pal.PalletNum.ToString(),
          FixtureOnPallet = "",
          OnHold = pal.Skip,
          CurrentPalletLocation = BuildCurrentLocation(pal),
          NumFaces = pal.NumFaces
        });
      }

      // material


      return curStatus;
    }

    private static PalletLocation BuildCurrentLocation(PalletMaster p)
    {
      switch (p.Loc)
      {
        case LoadUnloadLoc l:
          return new PalletLocation(PalletLocationEnum.LoadUnload, "L/U", l.LoadStation);
        case MachineOrWashLoc m:
          switch (m.Position)
          {
            case MachineOrWashLoc.Rotary.Input:
            case MachineOrWashLoc.Rotary.Output:
              return new PalletLocation(PalletLocationEnum.MachineQueue, "MC", m.Station);
            case MachineOrWashLoc.Rotary.Worktable:
              return new PalletLocation(PalletLocationEnum.Machine, "MC", m.Station);
          }
          break;
        case StockerLoc s:
          return new PalletLocation(PalletLocationEnum.Buffer, "Buffer", p.PalletNum);
        case CartLoc c:
          return new PalletLocation(PalletLocationEnum.Cart, "Cart", 1);
      }
      return new PalletLocation(PalletLocationEnum.Buffer, "Buffer", 0);
    }

  }
}