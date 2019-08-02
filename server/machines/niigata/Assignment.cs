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
using System.Linq;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;

namespace BlackMaple.FMSInsight.Niigata
{
  public interface IAssignPallets
  {
    NiigataAction NewPalletChange(IEnumerable<PalletAndMaterial> existingPallets, PlannedSchedule sch);
  }

  public class AssignPallets : IAssignPallets
  {
    private JobLogDB _log;

    public AssignPallets(JobLogDB l)
    {
      _log = l;
    }

    private const int LoadRouteNum = 1;
    private const int MachineRouteNum = 2;
    private const int UnloadRouteNum = 3;

    public NiigataAction NewPalletChange(IEnumerable<PalletAndMaterial> existingPallets, PlannedSchedule sch)
    {
      // only need to decide on a single change, SyncPallets will call in a loop until no changes are needed.

      // first, check if pallet at load station being unloaded needs something loaded
      /*
      foreach (var pal in existingPallets)
      {
        if (!(pal.Pallet.Loc is LoadUnloadLoc)) continue;
        if (!(pal.Pallet.Tracking.Before && pal.Pallet.Tracking.RouteNum == UnloadRouteNum)) continue;
        if (pal.Material.Any(m => m.Action.Type == InProcessMaterialAction.ActionType.Loading)) continue;
      }
      */

      return null;
    }

    private /* record */ class JobPath
    {
      public JobPlan Job { get; set; }
      public int Process { get; set; }
      public int Path { get; set; }
    }

    /*
    private IList<JobPath> FindPathsForPallet(PlannedSchedule sch, int pallet, int loadStation)
    {

      if (!(potentialNewPath.Job.LoadStations(potentialNewPath.Process, potentialNewPath.Path).Contains(loadStation)))
        return false;

    }
    */

    private bool PathAllowedOnPallet(IEnumerable<JobPath> alreadyLoading, JobPath potentialNewPath)
    {
      var seenFaces = new HashSet<string>();
      var newFixture = potentialNewPath.Job.PlannedFixtures(potentialNewPath.Process, potentialNewPath.Path).FirstOrDefault();
      foreach (var otherPath in alreadyLoading)
      {
        var otherFixFace = otherPath.Job.PlannedFixtures(otherPath.Process, otherPath.Path).FirstOrDefault();
        if (!string.IsNullOrEmpty(newFixture.Fixture) && newFixture.Fixture != otherFixFace.Fixture)
          return false;
        seenFaces.Add(otherFixFace.Face);
      }

      if (seenFaces.Contains(newFixture.Face))
        return false;

      return true;
    }

    private int ProcessForMatID(long matID)
    {
      return _log.GetLogForMaterial(matID)
        .SelectMany(m => m.Material)
        .Where(m => m.MaterialID == matID)
        .Max(m => m.Process);
    }

    private bool CheckMaterialForPathExists(JobPath path)
    {
      var fixFace = path.Job.PlannedFixtures(path.Process, path.Path).FirstOrDefault();
      int.TryParse(fixFace.Face, out int faceNum);

      var inputQueue = path.Job.GetInputQueue(path.Process, path.Path);
      if (path.Process == 1 && string.IsNullOrEmpty(inputQueue))
      {
        // no input queue, just new parts
        return true;
      }
      else if (path.Process == 1 && !string.IsNullOrEmpty(inputQueue))
      {
        // load castings
        var castings =
          _log.GetMaterialInQueue(inputQueue)
          .Where(m => (string.IsNullOrEmpty(m.Unique) && m.PartName == path.Job.PartName)
                   || (!string.IsNullOrEmpty(m.Unique) && m.Unique == path.Job.UniqueStr)
          ).Where(m => ProcessForMatID(m.MaterialID) == 0)
          .Count();

        return castings >= path.Job.PartsPerPallet(path.Process, path.Path);
      }
      else if (path.Process > 1)
      {
        // TODO: finish this (check transfer on pallet, load from queue, path groups)

      }

      return false;
    }

    private IList<JobPath> FindMaterialToLoad(IEnumerable<JobPath> allPaths)
    {
      List<JobPath> paths = null;
      foreach (var path in allPaths)
      {
        if (!CheckMaterialForPathExists(path)) continue;
        if (paths == null)
        {
          // first path with material gets set
          paths = new List<JobPath> { path };
        }
        else
        {
          // later paths need to make sure they are compatible.
          if (PathAllowedOnPallet(paths, path))
          {
            paths.Add(path);
          }
        }
      }
      return paths;
    }
  }
}