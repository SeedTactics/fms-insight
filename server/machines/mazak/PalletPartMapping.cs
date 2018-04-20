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
    public readonly int Uid;

    //The representation of the mazak parts and pallets used to create the database rows
    private IList<MazakPart> allParts;
    private IList<PartPalletGroup> groups;

    //List of parts which are still running.  Any sail part not in this list is deleted.
    private IList<string> savedParts;

    //fixtures used by either existing or new parts.  Fixtures in the Mazak not in this list
    //will be deleted and fixtures appearing in this list but not yet in Mazak will be added.
    private Dictionary<string, bool> usedFixtures;

    //Some global settings for this download
    private readonly ReadOnlyDataSet currentSet;
    private readonly int downloadUID;
    private readonly bool updateGlobalTag;
    private readonly string newGlobalTag;
    private readonly bool checkPalletsUsedOnce;
    private readonly DatabaseAccess.MazakDbType MazakType;

    public clsPalletPartMapping(IEnumerable<JobPlan> routes,
                                ReadOnlyDataSet readOnlySet, int uidVal, IList<string> saved,
                                IList<string> log, IList<string> trace,
                                bool updateGlobal, string newGlobal,
                                bool checkPalletsUsedOnce,
                                DatabaseAccess.MazakDbType mazakTy)
    {
      Uid = uidVal;
      allParts = null;
      groups = null;
      savedParts = saved;
      usedFixtures = new Dictionary<string, bool>();

      currentSet = readOnlySet;
      downloadUID = Uid;
      updateGlobalTag = updateGlobal;
      newGlobalTag = newGlobal;
      this.checkPalletsUsedOnce = checkPalletsUsedOnce;
      this.MazakType = mazakTy;

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
                throw new Exception("Invalid pallet->part mapping. " + palName + " is not numeric.");
              }
            }
          }
        }
      }

      BuildPartPalletMapping(routes, log, trace);
    }

    public int GetNumberProcesses(JobPlan part)
    {
      foreach (var p in allParts)
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

      foreach (ReadOnlyDataSet.PartRow partRow in currentSet.Part.Rows)
      {
        if (MazakPart.IsSailPart(partRow.PartName))
        {
          if (!savedParts.Contains(partRow.PartName))
          {
            newPartRow = transSet.Part_t.NewPart_tRow();
            newPartRow.Command = DatabaseAccess.DeleteCommand;
            newPartRow.PartName = partRow.PartName;
            newPartRow.TotalProcess = partRow.GetPartProcessRows().Length;
            transSet.Part_t.AddPart_tRow(newPartRow);
          }
        }
      }

      foreach (ReadOnlyDataSet.PalletRow palRow in currentSet.Pallet.Rows)
      {
        int idx = palRow.Fixture.IndexOf(':');

        if (idx >= 0)
        {
          //check if this fixture is being used by a new schedule
          //or is a fixture used by a part in savedParts

          if (!usedFixtures.ContainsKey(palRow.Fixture))
          {
            if (MazakType != DatabaseAccess.MazakDbType.MazakVersionE)
            {
              //not found, we can delete it
              newPalRowV2 = transSet.Pallet_tV2.NewPallet_tV2Row();
              newPalRowV2.Command = DatabaseAccess.DeleteCommand;
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
              newPalRowV1.Command = DatabaseAccess.DeleteCommand;
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

      foreach (ReadOnlyDataSet.FixtureRow fixRow in currentSet.Fixture.Rows)
      {
        int idx = fixRow.FixtureName.IndexOf(':');

        if (idx >= 0)
        {

          if (!usedFixtures.ContainsKey(fixRow.FixtureName) &&
              fixRow.FixtureName.ToLower() != "fixture:uniquestr")
          {

            newFixRow = transSet.Fixture_t.NewFixture_tRow();
            newFixRow.Command = DatabaseAccess.DeleteCommand;
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
      foreach (string fixture in usedFixtures.Keys)
      {
        //check if this fixture exists already... could exist already if we reuse fixtures
        foreach (ReadOnlyDataSet.FixtureRow fixRow in currentSet.Fixture.Rows)
        {
          if (fixRow.FixtureName == fixture)
          {
            goto found;
          }
        }

        TransactionDataSet.Fixture_tRow newFixRow = transSet.Fixture_t.NewFixture_tRow();
        newFixRow.Command = DatabaseAccess.AddCommand;
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

    //This creates all the new parts, pallets in the databases
    //Returns all the parts that were sucessfully created
    public void CreateRows(TransactionDataSet transSet)
    {
      foreach (var p in allParts)
        p.CreateDatabaseRow(transSet);

      int gNum = 0;
      foreach (var g in groups)
      {

        var procs = new Dictionary<int, bool>();

        foreach (var p in g.Parts)
        {
          p.CreateDatabaseRow(transSet, g.Fixture, MazakType);
          procs[p.ProcessNumber] = true;
        }


        foreach (var proc in procs.Keys)
        {
          foreach (var p in g.Pallets)
            CreatePartPalletRows.CreatePalletRow(transSet, currentSet, p,
                                                 g.Fixture + ":" + proc.ToString(),
                                                 gNum, downloadUID);
        }

        gNum += 1;
      }
    }

    #region Creating Part Pallet Mapping

    //A group of parts and pallets where any part can run on any pallet.  In addition,
    //distinct processes can run at the same time on a pallet.
    private class PartPalletGroup
    {
      public List<MazakProcess> Parts = new List<MazakProcess>();
      public List<string> Pallets = new List<string>();

      //The mazak fixture name will be the name stored here with a :proc
      //appended to the end.
      public string Fixture;
    }

    //This builds up the current part->fixture and pallet->fixture mapping
    private void BuildPartPalletMapping(IEnumerable<JobPlan> routes,
                                        IList<string> log, IList<string> trace)
    {
      //We view each part pallet group as a bipartite graph between Parts and Pallets.
      //A fixture can represent any complete bipartite subgraph, so we need
      //to divide the bipartite graph into a union of complete bipartite graphs.

      //The only restriction to what can be represented in mazak is that while pallets
      //can be repeated, a part can be in only one component.  But optionally we would
      //like to give an error message if a pallet is repeated because repeated
      //pallets sometimes represents a user configuration error.

      //First calculate the available fixtures
      var availableFixtures = CalculateAvailableFixtures();
      trace.Add("Available Fixtures: " + DatabaseAccess.Join(availableFixtures.Keys, ", "));

      //Next, decompose the bipartite graph into complete bipartite graphs.
      CalculateGraphs(routes, log);

      if (checkPalletsUsedOnce)
        CheckPalletsUsedOnce();

      //For each graph, create (or reuse) a fixture and add parts and pallets using this fixture.
      int graphNum = 0;
      foreach (var graph in groups)
      {
        graph.Pallets.Sort();

        trace.Add("PartPalletGroup");

        trace.Add("    Pallets: " + DatabaseAccess.Join(graph.Pallets, ", "));
        var t = "    Parts: ";
        foreach (var p in graph.Parts)
          t += ", " + p.ToString();
        trace.Add(t);

        //check if we can reuse an existing fixture
        CheckExistingFixture(graph, availableFixtures, trace);

        if (graph.Fixture == null || graph.Fixture == "")
        {
          //create a new fixture
          var fixture = "Fixt:" + downloadUID.ToString() + ":" + graphNum.ToString();
          //only add the first pallet to the name
          fixture += ":" + graph.Pallets[0].ToString();
          graph.Fixture = fixture;
          trace.Add("    Creating new fixture: " + fixture);
        }
        else
        {
          trace.Add("    Using existing fixture: " + graph.Fixture);
        }

        //Mark fixtures as used.
        foreach (var p in graph.Parts)
        {
          usedFixtures[graph.Fixture + ":" + p.ProcessNumber.ToString()] = true;
        }

        graphNum += 1;
      }
    }

    private void CalculateGraphs(IEnumerable<JobPlan> routes, IList<string> logMessages)
    {
      //Divide the full bipartite graph into complete bipartite graphs.
      //As mentioned above, each part can be in only one component so
      //we maintain a list of parts yet to process.
      groups = new List<PartPalletGroup>();

      allParts = CreatePartPalletRows.BuildMazakParts(routes, downloadUID, currentSet, MazakType, logMessages);

      var remainingParts = new List<MazakProcess>();
      foreach (var p in allParts)
        remainingParts.AddRange(p.Processes);

      while (remainingParts.Count > 0)
      {
        var pPath = remainingParts[0];
        remainingParts.RemoveAt(0);

        //we start a new graph for this part
        var graph = new PartPalletGroup();
        graph.Parts.Add(pPath);
        groups.Add(graph);

        //Add the pallets assigned to this part.
        foreach (string palName in pPath.Pallets())
        {
          graph.Pallets.Add(palName);
        }

        //Since the graph must be complete bipartite, we can't add any more pallets.
        //But it is possible to add more parts if they are assigned to these pallets
        foreach (var part2 in new List<MazakProcess>(remainingParts))
        {
          if (CheckListEqual(graph.Pallets, part2.Pallets()))
          {

            graph.Parts.Add(part2);
            remainingParts.Remove(part2);
          }
        }
      }
    }

    private Dictionary<string, bool> CalculateAvailableFixtures()
    {
      var availableFixtures = new Dictionary<string, bool>();
      foreach (ReadOnlyDataSet.PartProcessRow partProc in currentSet.PartProcess.Rows)
      {
        if (partProc.PartName.IndexOf(':') >= 0)
        {
          if (savedParts.Contains(partProc.PartName))
          {
            string fix = partProc.Fixture;

            //add this fixture to the fixtures to save/create
            usedFixtures[fix] = true;

            //now strip off the process number (the final entry in the fixture)
            //and add to available fixtures
            int idx = fix.LastIndexOf(':');
            if (idx >= 0)
            {
              fix = fix.Substring(0, idx);
              availableFixtures[fix] = true;
            }
          }
        }
      }

      return availableFixtures;
    }

    private void CheckExistingFixture(PartPalletGroup graph, Dictionary<string, bool> availableFixtures,
                                      IList<string> trace)
    {
      //we need a fixture that exactly matches this pallet list and has all the processes.
      //Also, the fixture must be contained in a saved part.

      //calculate the list of (pallet,process) pairs which must exist
      var positionsFound = new Dictionary<string, bool>();

      //Add all positions.
      foreach (var pal in graph.Pallets)
      {
        foreach (var part in graph.Parts)
        {
          positionsFound[pal + "^%##$" + part.ProcessNumber.ToString()] = false;
        }
      }

      foreach (string fixture in availableFixtures.Keys)
      {

        //reset found positions
        foreach (var key in new List<string>(positionsFound.Keys))
          positionsFound[key] = false;

        foreach (ReadOnlyDataSet.PalletRow palRow in currentSet.Pallet.Rows)
        {
          int idx = palRow.Fixture.LastIndexOf(':');
          if (idx >= 0 && palRow.Fixture.Substring(0, idx) == fixture)
          {

            var key = palRow.PalletNumber.ToString() + "^%##$" + palRow.Fixture.Substring(idx + 1);

            if (positionsFound.ContainsKey(key))
            {
              positionsFound[key] = true;
            }
            else
            {
              trace.Add("    Skipping fixture " + fixture + " because the fixture has pallet " +
                              palRow.PalletNumber.ToString() + " proc " + palRow.Fixture.Substring(idx + 1));
              goto nextFixture;
            }

          }
        }

        bool useFixture = true;
        foreach (var key in positionsFound.Keys)
        {
          if (!positionsFound[key])
          {
            useFixture = false;
            trace.Add("    Skipping fixture " + fixture + " because the fixture is missing " + key);
            break;
          }
        }

        if (useFixture)
        {
          graph.Fixture = fixture;
          return;
        }

      nextFixture:;
      }
    }

    private void CheckPalletsUsedOnce()
    {
      var usedPallets = new Dictionary<string, PartPalletGroup>();

      foreach (var graph in groups)
      {
        foreach (var pal in graph.Pallets)
        {
          if (usedPallets.ContainsKey(pal))
          {

            var firstGraph = usedPallets[pal];
            var firstPart = firstGraph.Parts[0].Job.PartName;
            var secondPart = graph.Parts[0].Job.PartName;
            var firstPallets = DatabaseAccess.Join(firstGraph.Pallets, ",");
            var secondPallets = DatabaseAccess.Join(graph.Pallets, ",");

            throw new Exception(
                "Invalid pallet->part mapping. " + firstPart + " and " + secondPart + " do not " +
                "have matching pallet lists.  " + firstPart + " is assigned to " + firstPallets +
                " and " + secondPart + " is assigned to " + secondPallets);
          }

          usedPallets.Add(pal, graph);
        }
      }
    }

    private bool CheckListEqual<T>(IEnumerable<T> l1, IEnumerable<T> l2)
    {
      var l1Copy = new List<T>(l1);
      foreach (T i in l2)
      {
        if (l1Copy.Contains(i))
        {
          l1Copy.Remove(i);
        }
        else
        {
          return false;
        }
      }

      if (l1Copy.Count == 0)
      {
        return true;
      }
      else
      {
        return false;
      }
    }
    #endregion
  }
}
