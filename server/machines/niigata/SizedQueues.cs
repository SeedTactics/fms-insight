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
using System.Linq;
using BlackMaple.MachineWatchInterface;

namespace BlackMaple.FMSInsight.Niigata
{
  public class SizedQueues : IAssignPallets
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<SizedQueues>();

    private readonly IReadOnlyDictionary<string, MachineWatchInterface.QueueSize> _queueSizes;
    public SizedQueues(IReadOnlyDictionary<string, MachineWatchInterface.QueueSize> queues)
    {
      if (queues.Values.Any(q => q.MaxSizeBeforeStopUnloading.HasValue && q.MaxSizeBeforeStopUnloading.Value > 0))
      {
        _queueSizes = queues;
      }
      else
      {
        _queueSizes = null;
      }
    }

    public NiigataAction NewPalletChange(CellState cellState)
    {
      if (_queueSizes == null) return null;

      var sizedQueues = _queueSizes
        .Where(q => q.Value.MaxSizeBeforeStopUnloading.HasValue && q.Value.MaxSizeBeforeStopUnloading.Value > 0)
        .Select(q => q.Key)
        .ToHashSet();

      var palletsToCheckForHold =
        cellState.Pallets
        .Where(pal =>
          pal.Material.Count > 0
          &&
          pal.Material.Any(m => sizedQueues.Contains(m.Job.GetOutputQueue(m.Mat.Process, m.Mat.Path)))
        )
        .OrderBy(pal => pal.Material.Min(m => m.Job.GetSimulatedStartingTimeUTC(m.Mat.Process, m.Mat.Path)))
        .ToList();


      // ensure everything that could be held is marked as hold
      foreach (var pal in palletsToCheckForHold.Where(IsExecutingFinalStep))
      {
        if (pal.Status.Master.Skip == false)
        {
          return SetPalletHold(pal, true);
        }
      }

      // see if anything can be unheld
      var queueRemaining = CalculateQueueRemainingPositions(cellState, palletsToCheckForHold);
      foreach (var pal in palletsToCheckForHold.Where(IsPalletReadyToUnload))
      {
        if (pal.Status.Master.Skip && IsAvailableSpaceForPallet(cellState, pal, queueRemaining))
        {
          return SetPalletHold(pal, false);
        }
      }

      return null;
    }

    private bool IsExecutingFinalStep(PalletAndMaterial pal)
    {
      // everything currently executing on the second-to-last-stop is marked as on-hold
      var curRouteIdx = pal.Status.Tracking.CurrentStepNum - 1;
      if (curRouteIdx == pal.Status.Master.Routes.Count - 2)
      {
        switch (pal.Status.CurrentStep)
        {
          case MachiningStep machStep:
            return
              pal.Material.All(m =>
                   m.Mat.LastCompletedMachiningRouteStopIndex == m.Job.GetMachiningStop(m.Mat.Process, m.Mat.Path).Count() - 1
                || m.Mat.Action.Type == InProcessMaterialAction.ActionType.Machining
              );

          case ReclampStep reclamp:
            return pal.Material.All(m => m.Mat.Action.Type == InProcessMaterialAction.ActionType.Loading);
        }
      }

      return false;
    }

    private bool IsPalletReadyToUnload(PalletAndMaterial pal)
    {
      // check after the second-to-last-stop or before the final unload.
      var curRouteIdx = pal.Status.Tracking.CurrentStepNum - 1;
      if (curRouteIdx == pal.Status.Master.Routes.Count - 2)
      {
        return !pal.Status.Tracking.BeforeCurrentStep;
      }
      else if (curRouteIdx == pal.Status.Master.Routes.Count - 1)
      {
        return pal.Status.Tracking.BeforeCurrentStep;
      }
      else
      {
        return false;
      }
    }

    private IReadOnlyDictionary<string, int> CalculateQueueRemainingPositions(CellState cellState, IEnumerable<PalletAndMaterial> palsToCheck)
    {
      var remain = new Dictionary<string, int>();

      // initially the size of the queue
      foreach (var q in _queueSizes)
      {
        if (q.Value.MaxSizeBeforeStopUnloading.HasValue && q.Value.MaxSizeBeforeStopUnloading.Value > 0)
        {
          remain.Add(q.Key, q.Value.MaxSizeBeforeStopUnloading.Value);
        }
      }

      // take out anything currently in the queue
      foreach (var m in cellState.QueuedMaterial)
      {
        if (m.Location.Type == InProcessMaterialLocation.LocType.InQueue && m.Action.Type != InProcessMaterialAction.ActionType.Loading)
        {
          if (remain.ContainsKey(m.Location.CurrentQueue))
          {
            remain[m.Location.CurrentQueue] -= 1;
          }
        }
      }

      // also take out anything on a pallet currently unloading or moving to be unloaded.
      foreach (var pal in palsToCheck)
      {
        if (!pal.Status.Master.Skip && IsPalletReadyToUnload(pal))
        {
          foreach (var mat in pal.Material)
          {
            var queue = mat.Job.GetOutputQueue(mat.Mat.Process, mat.Mat.Path);
            if (!string.IsNullOrEmpty(queue) && remain.ContainsKey(queue))
            {
              remain[queue] -= 1;
            }
          }
        }
      }

      return remain;
    }

    private bool IsAvailableSpaceForPallet(CellState cellState, PalletAndMaterial pallet, IReadOnlyDictionary<string, int> remainingQueuePos)
    {
      foreach (var matGroup in pallet.Material.GroupBy(mat => mat.Job.GetOutputQueue(mat.Mat.Process, mat.Mat.Path)))
      {
        if (matGroup.Key != null && remainingQueuePos.TryGetValue(matGroup.Key, out var remain))
        {
          var cntOnPallet = matGroup.Count();
          if (cntOnPallet > remain)
          {
            return false;
          }
          else if (cntOnPallet == remain && !AvailablePalletForPickup(cellState, matGroup, matGroup.Key))
          {
            // don't fill up the queue if there isn't another pallet ready to empty it
            return false;
          }
        }
      }

      return true;
    }

    private bool AvailablePalletForPickup(CellState cellState, IEnumerable<InProcessMaterialAndJob> mats, string queue)
    {
      var availPallets = cellState.Pallets
        .Where(pal => pal.Status.Master.NoWork)
        .Select(pal => pal.Status.Master.PalletNum.ToString())
        .ToHashSet();

      return mats.All(mat =>
      {
        if (mat.Mat.Process == mat.Job.NumProcesses) return true;

        // search for an available pallet to pickup this material
        int nextProc = mat.Mat.Process + 1;
        int group = mat.Job.GetPathGroup(mat.Mat.Process, mat.Mat.Path);
        for (int path = 1; path <= mat.Job.GetNumPaths(nextProc); path++)
        {
          if (
              mat.Job.GetPathGroup(nextProc, path) == group
           && mat.Job.GetInputQueue(nextProc, path) == queue
           && mat.Job.PlannedPallets(nextProc, path).Any(availPallets.Contains)
          )
          {
            return true;
          }
        }

        return false;
      });
    }

    private NiigataAction SetPalletHold(PalletAndMaterial pal, bool hold)
    {
      return new UpdatePalletQuantities()
      {
        Pallet = pal.Status.Master.PalletNum,
        Priority = pal.Status.Master.Priority,
        Cycles = pal.Status.Master.RemainingPalletCycles,
        NoWork = false,
        Skip = hold,
        LongToolMachine = 0
      };
    }

  }
}