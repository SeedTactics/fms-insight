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
using System.Linq;
using System.Collections.Generic;
using System.Data;
using BlackMaple.MachineWatchInterface;

namespace MazakMachineInterface
{
  //This class builds and holds the pallet->part mapping
  public class clsPalletPartMapping
  {
    //List of parts which are still running.  Any sail part not in this list is deleted.
    private readonly ISet<string> savedParts;

    private readonly MazakJobs mazakJobs;
    private readonly MazakAllData mazakData;
    private readonly int downloadUID;
    private readonly bool updateGlobalTag;
    private readonly string newGlobalTag;
    private readonly MazakDbType MazakType;

    public clsPalletPartMapping(IEnumerable<JobPlan> routes,
                                MazakAllData md, int uidVal, ISet<string> saved,
                                IList<string> log,
                                bool updateGlobal, string newGlobal,
                                bool checkPalletsUsedOnce,
                                MazakDbType mazakTy)
    {
      savedParts = saved;

      mazakData = md;
      downloadUID = uidVal;
      updateGlobalTag = updateGlobal;
      newGlobalTag = newGlobal;
      MazakType = mazakTy;

      //only allow numeric pallets
      foreach (JobPlan part in routes)
      {
        for (int proc = 1; proc <= part.NumProcesses; proc++)
        {
          for (int path = 1; path <= part.GetNumPaths(proc); path++)
          {
            foreach (string palName in part.PlannedPallets(proc, path))
            {
              int v;
              if (!int.TryParse(palName, out v))
              {
                throw new BlackMaple.MachineFramework.BadRequestException("Invalid pallet->part mapping. " + palName + " is not numeric.");
              }
            }
          }
        }
      }

      mazakJobs = ConvertJobsToMazakParts.JobsToMazak(routes,
        downloadUID,
        mazakData,
        savedParts,
        mazakTy,
        checkPalletsUsedOnce,
        log);
    }

    public int GetNumberProcesses(JobPlan part)
    {
      foreach (var p in mazakJobs.AllParts)
      {
        if (p.Job.UniqueStr == part.UniqueStr)
          return p.Processes.Count;
      }
      return 0;
    }

    //This deletes all the part and pallet data in the databases that are no longer used
    public void DeletePartPallets(MazakWriteData transSet)
    {
      foreach (var partRow in mazakData.Parts)
      {
        if (MazakPart.IsSailPart(partRow.PartName))
        {
          if (!savedParts.Contains(partRow.PartName))
          {
            var newPartRow = partRow.Clone();
            newPartRow.Command = MazakWriteCommand.Delete;
            transSet.Parts.Add(newPartRow);
          }
        }
      }

      foreach (var palRow in mazakData.Pallets)
      {
        int idx = palRow.Fixture.IndexOf(':');

        if (idx >= 0)
        {
          //check if this fixture is being used by a new schedule
          //or is a fixture used by a part in savedParts

          if (!mazakJobs.UsedFixtures.Contains(palRow.Fixture))
          {
            //not found, can delete it
            var newPalRow = palRow.Clone();
            newPalRow.Command = MazakWriteCommand.Delete;
            transSet.Pallets.Add(newPalRow);
          }
        }
      }
    }

    public void DeleteFixtures(MazakWriteData transSet)
    {
      foreach (var fixRow in mazakData.Fixtures)
      {
        int idx = fixRow.FixtureName.IndexOf(':');

        if (idx >= 0)
        {

          if (!mazakJobs.UsedFixtures.Contains(fixRow.FixtureName))
          {
            var newFixRow = fixRow.Clone();
            newFixRow.Command = MazakWriteCommand.Delete;
            transSet.Fixtures.Add(newFixRow);
          }
        }
      }
    }

    //This creates all the new fixtures in the databases
    public void AddFixtures(MazakWriteData transSet)
    {
      //first get the fixture table the way we want it
      foreach (string fixture in mazakJobs.UsedFixtures)
      {
        //check if this fixture exists already... could exist already if we reuse fixtures
        foreach (var fixRow in mazakData.Fixtures)
        {
          if (fixRow.FixtureName == fixture)
          {
            goto found;
          }
        }

        var newFixRow = new MazakFixtureRow();
        newFixRow.Command = MazakWriteCommand.Add;
        newFixRow.FixtureName = fixture;

        //the comment can not be empty, or the database kit blows up.
        if (updateGlobalTag)
        {
          newFixRow.Comment = newGlobalTag;
        }
        else
        {
          newFixRow.Comment = "SAIL";
        }
        transSet.Fixtures.Add(newFixRow);
      found:;

      }
    }

    private void CreatePalletRow(MazakWriteData transSet, string pallet, string fixture, int fixGroup)
    {
      int palNum = int.Parse(pallet);

      foreach (var palRow in mazakData.Pallets)
      {
        if (palRow.PalletNumber == palNum && palRow.Fixture == fixture)
        {
          return;
        }
      }

      //we have the + 1 because UIDs and graphs start at 0, and the user might add other fixture-pallet
      //on group 0.
      fixGroup = (downloadUID * 10 + (fixGroup % 10)) + 1;

      //Add rows to both V1 and V2.
      var newRow = new MazakPalletRow();
      newRow.Command = MazakWriteCommand.Add;
      newRow.PalletNumber = palNum;
      newRow.Fixture = fixture;
      newRow.RecordID = 0;
      newRow.FixtureGroupV2 = fixGroup;

      //combos with an angle in the range 0-999, and we don't want to conflict with that
      newRow.AngleV1 = (fixGroup * 1000);

      transSet.Pallets.Add(newRow);
    }

    //This creates all the new parts, pallets in the databases
    //Returns all the parts that were sucessfully created
    public void CreateRows(MazakWriteData transSet)
    {
      foreach (var p in mazakJobs.AllParts)
        p.CreateDatabaseRow(transSet);

      var byName = transSet.Parts.ToDictionary(p => p.PartName, p => p);

      foreach (var g in mazakJobs.Fixtures)
      {
        foreach (var p in g.Processes)
        {
          p.CreateDatabaseRow(byName[p.Part.PartName], g.MazakFixtureName, MazakType);
        }

        foreach (var p in g.Pallets)
          CreatePalletRow(transSet, p, g.MazakFixtureName, g.FixtureGroup);
      }
    }
  }
}
