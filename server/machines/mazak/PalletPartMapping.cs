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
    public void DeletePartPallets(TransactionDataSet transSet)
    {
      TransactionDataSet.Part_tRow newPartRow = null;
      TransactionDataSet.Pallet_tV1Row newPalRowV1 = null;
      TransactionDataSet.Pallet_tV2Row newPalRowV2 = null;

      foreach (var partRow in mazakData.Parts)
      {
        if (MazakPart.IsSailPart(partRow.PartName))
        {
          if (!savedParts.Contains(partRow.PartName))
          {
            newPartRow = transSet.Part_t.NewPart_tRow();
            newPartRow.Command = OpenDatabaseKitTransactionDB.DeleteCommand;
            newPartRow.PartName = partRow.PartName;
            newPartRow.TotalProcess = partRow.Processes.Count;
            transSet.Part_t.AddPart_tRow(newPartRow);
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
            if (MazakType != MazakDbType.MazakVersionE)
            {
              //not found, we can delete it
              newPalRowV2 = transSet.Pallet_tV2.NewPallet_tV2Row();
              newPalRowV2.Command = OpenDatabaseKitTransactionDB.DeleteCommand;
              newPalRowV2.PalletNumber = palRow.PalletNumber;
              newPalRowV2.Fixture = palRow.Fixture;
              newPalRowV2.RecordID = palRow.RecordID;
              newPalRowV2.FixtureGroup = palRow.FixtureGroupV2;
              transSet.Pallet_tV2.AddPallet_tV2Row(newPalRowV2);

            }
            else
            {
              //not found, we can delete it
              newPalRowV1 = transSet.Pallet_tV1.NewPallet_tV1Row();
              newPalRowV1.Command = OpenDatabaseKitTransactionDB.DeleteCommand;
              newPalRowV1.PalletNumber = palRow.PalletNumber;
              newPalRowV1.Fixture = palRow.Fixture;
              newPalRowV1.RecordID = palRow.RecordID;
              newPalRowV1.Angle = palRow.AngleV1;
              transSet.Pallet_tV1.AddPallet_tV1Row(newPalRowV1);
            }

          }
        }
      }
    }

    public void DeleteFixtures(TransactionDataSet transSet)
    {
      TransactionDataSet.Fixture_tRow newFixRow = null;

      foreach (var fixRow in mazakData.Fixtures)
      {
        int idx = fixRow.FixtureName.IndexOf(':');

        if (idx >= 0)
        {

          if (!mazakJobs.UsedFixtures.Contains(fixRow.FixtureName))
          {

            newFixRow = transSet.Fixture_t.NewFixture_tRow();
            newFixRow.Command = OpenDatabaseKitTransactionDB.DeleteCommand;
            newFixRow.FixtureName = fixRow.FixtureName;
            transSet.Fixture_t.AddFixture_tRow(newFixRow);
          }
        }
      }
    }

    //This creates all the new fixtures in the databases
    public void AddFixtures(TransactionDataSet transSet)
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

        TransactionDataSet.Fixture_tRow newFixRow = transSet.Fixture_t.NewFixture_tRow();
        newFixRow.Command = OpenDatabaseKitTransactionDB.AddCommand;
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
        transSet.Fixture_t.AddFixture_tRow(newFixRow);
      found:;

      }
    }

    private void CreatePalletRow(TransactionDataSet transSet, string pallet, string fixture, int fixGroup)
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
      TransactionDataSet.Pallet_tV2Row newRow2 = transSet.Pallet_tV2.NewPallet_tV2Row();
      newRow2.Command = OpenDatabaseKitTransactionDB.AddCommand;
      newRow2.PalletNumber = palNum;
      newRow2.Fixture = fixture;
      newRow2.RecordID = 0;
      newRow2.FixtureGroup = fixGroup;
      transSet.Pallet_tV2.AddPallet_tV2Row(newRow2);

      TransactionDataSet.Pallet_tV1Row newRow1 = transSet.Pallet_tV1.NewPallet_tV1Row();
      newRow1.Command = OpenDatabaseKitTransactionDB.AddCommand;
      newRow1.PalletNumber = palNum;
      newRow1.Fixture = fixture;

      //combos with an angle in the range 0-999, and we don't want to conflict with that
      newRow1.Angle = (fixGroup * 1000);

      transSet.Pallet_tV1.AddPallet_tV1Row(newRow1);
    }

    //This creates all the new parts, pallets in the databases
    //Returns all the parts that were sucessfully created
    public void CreateRows(TransactionDataSet transSet)
    {
      foreach (var p in mazakJobs.AllParts)
        p.CreateDatabaseRow(transSet);

      foreach (var g in mazakJobs.Fixtures)
      {
        foreach (var p in g.Processes)
        {
          p.CreateDatabaseRow(transSet, g.MazakFixtureName, MazakType);
        }

        foreach (var p in g.Pallets)
          CreatePalletRow(transSet, p, g.MazakFixtureName, g.FixtureGroup);
      }
    }
  }
}
