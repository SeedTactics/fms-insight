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
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Niigata
{
  public class SizedQueues : IAssignPallets
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<SizedQueues>();

    private readonly IReadOnlyDictionary<string, MachineFramework.QueueInfo> _queueSizes;

    public SizedQueues(IReadOnlyDictionary<string, MachineFramework.QueueInfo> queues)
    {
      if (
        queues != null
        && queues.Values.Any(
          q => q.MaxSizeBeforeStopUnloading.HasValue && q.MaxSizeBeforeStopUnloading.Value > 0
        )
      )
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
      if (_queueSizes == null)
        return null;

      var sizedQueues = new HashSet<string>(
        _queueSizes
          .Where(
            q => q.Value.MaxSizeBeforeStopUnloading.HasValue && q.Value.MaxSizeBeforeStopUnloading.Value > 0
          )
          .Select(q => q.Key)
      );

      var palletsToCheckForHold = cellState.Pallets
        .Where(
          pal =>
            pal.Material.Count > 0
            && pal.Material.Any(
              m =>
                m.Mat.Process >= 1
                && sizedQueues.Contains(m.Job.Processes[m.Mat.Process - 1].Paths[m.Mat.Path - 1].OutputQueue)
            )
        )
        .OrderBy(
          pal =>
            pal.Material.Min(
              m =>
                m.Mat.Process >= 1
                  ? m.Job.Processes[m.Mat.Process - 1].Paths[m.Mat.Path - 1].SimulatedStartingUTC
                  : DateTime.MaxValue
            )
        )
        .ToList();

      // ensure everything that could be held is marked as hold
      foreach (var pal in palletsToCheckForHold.Where(IsExecutingFinalStep))
      {
        if (pal.Status.Master.Skip == false && SetHoldAllowed(pal.Status))
        {
          return SetPalletHold(pal, true);
        }
      }

      // see if anything can be unheld
      var queueRemaining = CalculateQueueRemainingPositions(cellState, palletsToCheckForHold);
      foreach (var pal in palletsToCheckForHold.Where(IsPalletReadyToUnload))
      {
        if (
          pal.Status.Master.Skip
          && IsAvailableSpaceForPallet(cellState, pal, queueRemaining)
          && SetHoldAllowed(pal.Status)
        )
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
      if (curRouteIdx == pal.Status.Master.Routes.Count - 2 && pal.Status.Tracking.BeforeCurrentStep)
      {
        switch (pal.Status.CurrentStep)
        {
          case MachiningStep machStep:
            return pal.Material.All(
              m =>
                m.Mat.LastCompletedMachiningRouteStopIndex
                  == m.Job.Processes[m.Mat.Process - 1].Paths[m.Mat.Path - 1].Stops.Count() - 1
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
        return pal.Status.Tracking.BeforeCurrentStep
          && pal.Status.CurStation.Location.Location == PalletLocationEnum.Buffer;
      }
      else
      {
        return false;
      }
    }

    private IReadOnlyDictionary<string, int> CalculateQueueRemainingPositions(
      CellState cellState,
      IEnumerable<PalletAndMaterial> palsToCheck
    )
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
        if (m.Mat.Location.Type == InProcessMaterialLocation.LocType.InQueue)
        {
          if (remain.ContainsKey(m.Mat.Location.CurrentQueue))
          {
            remain[m.Mat.Location.CurrentQueue] -= 1;
          }
        }
      }

      // also take out anything on a pallet
      // - currently loading
      // - currently unloading
      // - moving to be unloaded
      // - has a pallet inbound to load material
      foreach (var pal in palsToCheck.Where(p => !p.Status.Master.Skip))
      {
        var curRouteIdx = pal.Status.Tracking.CurrentStepNum - 1;
        if (curRouteIdx == pal.Status.Master.Routes.Count - 2 && !pal.Status.Tracking.BeforeCurrentStep)
        {
          // the pallet currently not on hold and ready to move to the unload station
          foreach (var mat in pal.Material)
          {
            if (mat.Mat.Process >= 1)
            {
              var queue = mat.Job.Processes[mat.Mat.Process - 1].Paths[mat.Mat.Path - 1].OutputQueue;
              if (!string.IsNullOrEmpty(queue) && remain.ContainsKey(queue))
              {
                remain[queue] -= 1;
              }
            }
          }
        }
        else
        {
          // take out anything being loaded or unloaded
          foreach (var mat in pal.Material)
          {
            if (mat.Mat.Action.Type == InProcessMaterialAction.ActionType.UnloadToInProcess)
            {
              var queue = mat.Mat.Action.UnloadIntoQueue;
              if (!string.IsNullOrEmpty(queue) && remain.ContainsKey(queue))
              {
                remain[queue] -= 1;
              }
            }
            else if (mat.Mat.Action.Type == InProcessMaterialAction.ActionType.Loading)
            {
              var queue = mat.Mat.Location.CurrentQueue;
              if (!string.IsNullOrEmpty(queue) && remain.ContainsKey(queue))
              {
                remain[queue] -= 1;
              }
            }
          }
        }
      }

      return remain;
    }

    private bool IsAvailableSpaceForPallet(
      CellState cellState,
      PalletAndMaterial pallet,
      IReadOnlyDictionary<string, int> remainingQueuePos
    )
    {
      foreach (
        var matGroup in pallet.Material.GroupBy(
          mat =>
            mat.Mat.Process >= 1
              ? mat.Job.Processes[mat.Mat.Process - 1].Paths[mat.Mat.Path - 1].OutputQueue
              : null
        )
      )
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

    private bool AvailablePalletForPickup(
      CellState cellState,
      IEnumerable<InProcessMaterialAndJob> mats,
      string queue
    )
    {
      var availPallets = new HashSet<string>(
        cellState.Pallets
          .Where(pal => !pal.Status.HasWork && !pal.ManualControl)
          .Select(pal => pal.Status.Master.PalletNum.ToString())
      );

      return mats.All(mat =>
      {
        if (mat.Mat.Process == mat.Job.Processes.Count)
          return true;
        if (mat.Mat.Process < 1)
          return true;

        // search for an available pallet to pickup this material
        int nextProc = mat.Mat.Process + 1;
        var nextJobProcInfo = mat.Job.Processes[nextProc - 1];
        foreach (var pathInfo in nextJobProcInfo.Paths)
        {
          if (pathInfo.InputQueue == queue && pathInfo.Pallets.Any(p => availPallets.Contains(p)))
          {
            return true;
          }
        }

        return false;
      });
    }

    private bool SetHoldAllowed(PalletStatus pal)
    {
      // currently only allowed to hold at load station or stocker
      if (pal.CurStation.Location.Location == PalletLocationEnum.Buffer)
        return true;
      if (pal.CurStation.Location.Location == PalletLocationEnum.LoadUnload)
        return true;
      return false;
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
