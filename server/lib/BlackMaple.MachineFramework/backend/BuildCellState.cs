/* Copyright (c) 2023, John Lenz

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

namespace BlackMaple.MachineFramework;

public static class BuildCellState
{
  public record MaterialInQueue
  {
    public required QueuedMaterial QMat { get; init; }
    public required InProcessMaterial InProc { get; init; }
    public required Job? Job { get; init; }
  }

  public static ImmutableList<MaterialInQueue> AllQueuedMaterial(
    IRepository db,
    IJobCache? jobCache
  )
  {
    var mats = ImmutableList.CreateBuilder<MaterialInQueue>();
    var queuedMats = db.GetMaterialInAllQueues();
    var insps = db.LookupInspectionDecisions(queuedMats.Select(m => m.MaterialID));

    foreach (var mat in queuedMats)
    {
      var lastProc = (mat.NextProcess ?? 1) - 1;

      mats.Add(
        new MaterialInQueue()
        {
          QMat = mat,
          Job =
            string.IsNullOrEmpty(mat.Unique) || jobCache == null
              ? null
              : jobCache.Lookup(mat.Unique),
          InProc = new InProcessMaterial()
          {
            MaterialID = mat.MaterialID,
            JobUnique = mat.Unique,
            PartName = mat.PartNameOrCasting,
            Process = lastProc,
            Path =
              mat.Paths != null && mat.Paths.TryGetValue(Math.Max(1, lastProc), out var path)
                ? path
                : 1,
            Serial = mat.Serial,
            WorkorderId = mat.Workorder,
            SignaledInspections = insps[mat.MaterialID]
              .Where(x => x.Inspect)
              .Select(x => x.InspType)
              .Distinct()
              .ToImmutableList(),
            QuarantineAfterUnload = null,
            Location = new InProcessMaterialLocation()
            {
              Type = InProcessMaterialLocation.LocType.InQueue,
              CurrentQueue = mat.Queue,
              QueuePosition = mat.Position,
            },
            Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.Waiting,
            },
          },
        }
      );
    }

    return mats.ToImmutable();
  }

  public static ImmutableDictionary<string, QueueInfo> CalcQueueRoles(
    IEnumerable<Job> jobs,
    FMSSettings settings,
    IRepository db
  )
  {
    var (dbRawMat, dbInProc) = db.QueuesOnMostRecentSchedule();
    var rawMatQueues = dbRawMat.ToBuilder();
    var inProcQueues = dbInProc.ToBuilder();

    foreach (var j in jobs)
    {
      for (int proc = 1; proc <= j.Processes.Count; proc++)
      {
        foreach (var path in j.Processes[proc - 1].Paths)
        {
          if (!string.IsNullOrEmpty(path.InputQueue))
          {
            if (proc == 1)
            {
              rawMatQueues.Add(path.InputQueue);
            }
            else
            {
              inProcQueues.Add(path.InputQueue);
            }
          }
          if (!string.IsNullOrEmpty(path.OutputQueue))
          {
            inProcQueues.Add(path.OutputQueue);
          }
        }
      }
    }

    return settings.Queues.ToImmutableDictionary(
      k => k.Key,
      k =>
        k.Value with
        {
          Role =
            rawMatQueues.Contains(k.Key) ? QueueRole.RawMaterial
            : inProcQueues.Contains(k.Key) ? QueueRole.InProcessTransfer
            : k.Value.Role,
        }
    );
  }
}
