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
using BlackMaple.MachineWatchInterface;

namespace MazakMachineInterface
{
  //A mazak part cooresponds to a (Job,PathGroup) pair.
  public class MazakPart
  {
    public readonly JobPlan Job;
    public readonly int Proc1Path;
    public readonly int DownloadID;
    public readonly List<MazakProcess> Processes;

    public MazakPart(JobPlan j, int proc1Path, int downID)
    {
      Job = j;
      Proc1Path = proc1Path;
      DownloadID = downID;
      Processes = new List<MazakProcess>();
    }

    public void CreateDatabaseRow(TransactionDataSet transSet)
    {
      var newPartRow = transSet.Part_t.NewPart_tRow();

      newPartRow.Command = TransactionDatabaseAccess.AddCommand;
      newPartRow.PartName = PartName;
      newPartRow.Comment = Comment;
      newPartRow.Price = 0;
      newPartRow.TotalProcess = Processes.Count;

      transSet.Part_t.AddPart_tRow(newPartRow);
    }

    public string PartName
    {
      get
      {
        return Job.PartName + ":" + DownloadID.ToString() + ":" + Proc1Path.ToString();
      }
    }

    public string Comment
    {
      get
      {
        return CreateComment(Job.UniqueStr, Proc1Path, Job.ManuallyCreatedJob);
      }
    }

    public static string CreateComment(string unique, int proc1Path, bool manual)
    {
        if (manual)
          return unique + "-Path" + proc1Path.ToString() + "-1";
        else
          return unique + "-Path" + proc1Path.ToString() + "-0";
    }

    public static bool IsSailPart(string partName)
    {
      //sail parts are those with a colon and a positive integer after the colon
      string[] sSplit = partName.Split(':');
      if (sSplit.Length >= 2)
      {
        int v;
        if (int.TryParse(sSplit[1], out v) && v >= 0)
        {
          return true;
        }
      }

      return false;
    }

    public static string ExtractPartNameFromMazakPartName(string mazakPartName)
    {
        int loc = mazakPartName.IndexOf(':');
        if (loc >= 0) mazakPartName = mazakPartName.Substring(0, loc);
        return mazakPartName;
    }

    public static int ParseUID(string str)
    {
      string[] sSplit = str.Split(':');
      int v;
      if (sSplit.Length >= 2 && int.TryParse(sSplit[1], out v))
      {
        return v;
      }
      else
      {
        return -1;
      }
    }

    public static int ParsePathFromPart(string str)
    {
      string[] sSplit = str.Split(':');
      int v;
      if (sSplit.Length >= 3 && int.TryParse(sSplit[2], out v))
      {
        return v;
      }
      else
      {
        return 1;
      }
    }

    public static void ParseComment(string comment, out string unique, out int path, out bool manual)
    {
      int idx = comment.LastIndexOf("-Path");
      int lastDash = comment.LastIndexOf("-");

      if (lastDash < 0 || idx == lastDash || lastDash == comment.Length - 1)
      {
        //this is an old schedule without the entry
        manual = false;
      }
      else
      {
        if (comment[lastDash + 1] == '1')
          manual = true;
        else
          manual = false;
        comment = comment.Substring(0, lastDash);
      }

      if (idx >= 0)
      {
        unique = comment.Substring(0, idx);
        if (!int.TryParse(comment.Substring(idx + 5), out path))
        {
          path = 1;
        }
      }
      else
      {
        unique = comment;
        path = 1;
      }
    }
  }

  //There are two kinds of MazakProcesses
  //  - Processes which are copied from template rows (this is the old way, from
  //    2005 until early 2012)
  //  - Once path groups were added (2012), the processes can be created from the job.  All installs
  //    should use this from now on, but we support the old method for now until all the existing installs
  //    are converted over.

  public abstract class MazakProcess
  {
    public readonly MazakPart Part;
    public readonly int ProcessNumber;

    public JobPlan Job
    {
      get { return Part.Job; }
    }

    protected MazakProcess(MazakPart parent, int p)
    {
      Part = parent;
      ProcessNumber = p;
    }

    public abstract IEnumerable<string> Pallets();

    public abstract TransactionDataSet.PartProcess_tRow
      CreateDatabaseRow(TransactionDataSet transSet, string fixture, DatabaseAccess.MazakDbType mazakTy);

    protected static int ConvertStatStrV1ToV2(string v1str)
    {
      int ret = 0;
      for (int i = 0; i <= v1str.Length - 1; i++)
      {
        if (v1str[i] != '0')
        {
          ret += Convert.ToInt32(Math.Pow(2, i));
        }
      }
      return ret;
    }
  }

  public class MazakProcessFromJob : MazakProcess
  {
    //The path in the job for this process.
    public readonly int Path;

    public MazakProcessFromJob(MazakPart parent, int process, int pth)
      : base(parent, process)
    {
      Path = pth;
    }

    public override IEnumerable<string> Pallets()
    {
      return Job.PlannedPallets(ProcessNumber, Path);
    }

    public override TransactionDataSet.PartProcess_tRow CreateDatabaseRow(TransactionDataSet transSet, string fixture, DatabaseAccess.MazakDbType mazakTy)
    {
      var newPartProcRow = transSet.PartProcess_t.NewPartProcess_tRow();
      newPartProcRow.PartName = Part.PartName;
      newPartProcRow.ProcessNumber = ProcessNumber;
      newPartProcRow.Fixture = fixture;
      newPartProcRow.FixQuantity = Math.Max(1, Job.PartsPerPallet(ProcessNumber, Path)).ToString();

      newPartProcRow.ContinueCut = 0;
      //newPartProcRow.FixPhoto = "";
      //newPartProcRow.RemovePhoto = "";
      newPartProcRow.WashType = 0;

      char[] FixLDS = { '0', '0', '0', '0', '0', '0', '0', '0', '0', '0' };
      char[] UnfixLDS = { '0', '0', '0', '0', '0', '0', '0', '0', '0', '0' };
      char[] Cut = { '0', '0', '0', '0', '0', '0', '0', '0' };

      string program = "";
      foreach (var routeEntry in Job.GetMachiningStop(ProcessNumber, Path))
      {
        foreach (int statNum in routeEntry.Stations())
        {
          Cut[statNum - 1] = statNum.ToString()[0];
          program = routeEntry.Program(statNum);
        }
      }

      foreach (int statNum in Job.LoadStations(ProcessNumber, Path))
        FixLDS[statNum - 1] = statNum.ToString()[0];

      foreach (int statNum in Job.UnloadStations(ProcessNumber, Path))
        UnfixLDS[statNum - 1] = statNum.ToString()[0];

      newPartProcRow.MainProgram = program;
      newPartProcRow.FixLDS = new string(FixLDS);
      newPartProcRow.RemoveLDS = new string(UnfixLDS);
      newPartProcRow.CutMc = new string(Cut);

      if (mazakTy != DatabaseAccess.MazakDbType.MazakVersionE)
      {
        newPartProcRow.FixLDS = ConvertStatStrV1ToV2(newPartProcRow.FixLDS).ToString();
        newPartProcRow.RemoveLDS = ConvertStatStrV1ToV2(newPartProcRow.RemoveLDS).ToString();
        newPartProcRow.CutMc = ConvertStatStrV1ToV2(newPartProcRow.CutMc).ToString();
      }

      transSet.PartProcess_t.AddPartProcess_tRow(newPartProcRow);

      return newPartProcRow;
    }

    public override string ToString()
    {
      return "CopyFromJob " + base.Job.PartName + "-" + base.ProcessNumber.ToString() +
        " path " + Path.ToString();
    }
  }

  public class MazakProcessFromTemplate : MazakProcess
  {
    //Here, jobs have only one process.  The number of processes are copied from the template.
    public readonly ReadOnlyDataSet.PartProcessRow TemplateProcessRow;

    public MazakProcessFromTemplate(MazakPart parent, ReadOnlyDataSet.PartProcessRow template)
      : base(parent, template.ProcessNumber)
    {
      TemplateProcessRow = template;
    }

    public override IEnumerable<string> Pallets()
    {
      return Job.PlannedPallets(1, Part.Proc1Path);
    }

    public override TransactionDataSet.PartProcess_tRow CreateDatabaseRow(TransactionDataSet transSet, string fixture, DatabaseAccess.MazakDbType mazakTy)
    {
      char[] FixLDS = { '0', '0', '0', '0', '0', '0', '0', '0', '0', '0' };
      char[] UnfixLDS = { '0', '0', '0', '0', '0', '0', '0', '0', '0', '0' };
      char[] Cut = { '0', '0', '0', '0', '0', '0', '0', '0' };

      foreach (var routeEntry in Job.GetMachiningStop(1, Part.Proc1Path))
      {
        foreach (int statNum in routeEntry.Stations())
        {
          Cut[statNum - 1] = statNum.ToString()[0];
        }
      }

      foreach (int statNum in Job.LoadStations(1, Part.Proc1Path))
      {
        FixLDS[statNum - 1] = statNum.ToString()[0];
      }

      foreach (int statNum in Job.UnloadStations(1, Part.Proc1Path))
      {
        UnfixLDS[statNum - 1] = statNum.ToString()[0];
      }

      var newPartProcRow = transSet.PartProcess_t.NewPartProcess_tRow();
      TransactionDatabaseAccess.BuildPartProcessRow(newPartProcRow, TemplateProcessRow);
      newPartProcRow.PartName = Part.PartName;
      newPartProcRow.Fixture = fixture;
      newPartProcRow.ProcessNumber = ProcessNumber;

      newPartProcRow.FixLDS = new string(FixLDS);
      newPartProcRow.RemoveLDS = new string(UnfixLDS);
      newPartProcRow.CutMc = new string(Cut);

      if (mazakTy != DatabaseAccess.MazakDbType.MazakVersionE)
      {
        newPartProcRow.FixLDS = ConvertStatStrV1ToV2(newPartProcRow.FixLDS).ToString();
        newPartProcRow.RemoveLDS = ConvertStatStrV1ToV2(newPartProcRow.RemoveLDS).ToString();
        newPartProcRow.CutMc = ConvertStatStrV1ToV2(newPartProcRow.CutMc).ToString();
      }

      transSet.PartProcess_t.AddPartProcess_tRow(newPartProcRow);

      return newPartProcRow;
    }

    public override string ToString()
    {
      return "CopyFromTemplate " + base.Job.PartName + "-" + base.ProcessNumber.ToString() +
        " path " + base.Part.Proc1Path.ToString();
    }
  }

  //A group of parts and pallets where any part can run on any pallet.  In addition,
  //distinct processes can run at the same time on a pallet.
  public class MazakFixture
  {
    public int FixtureGroup {get;set;}
    public int Face {get;set;}
    public List<MazakProcess> Processes = new List<MazakProcess>();
    public List<string> Pallets = new List<string>();

    public string MazakFixtureName {get;set;}
  }

  public class MazakJobs
  {
    //The representation of the mazak parts and pallets used to create the database rows
    public IEnumerable<MazakPart> AllParts {get;set;}
    public IEnumerable<MazakFixture> Fixtures {get;set;}

    //fixtures used by either existing or new parts.  Fixtures in the Mazak not in this list
    //will be deleted and fixtures appearing in this list but not yet in Mazak will be added.
    public ISet<string> UsedFixtures {get;set;}
  }

  public static class ConvertJobsToMazakParts
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<MazakJobs>();

    //Allows plugins to customize the process creation
    public delegate MazakProcess ProcessFromJobDelegate(MazakPart parent, int process, int pth);
    public static ProcessFromJobDelegate ProcessFromJob
      = (parent, process, pth) => new MazakProcessFromJob(parent, process, pth);

    public static MazakJobs JobsToMazak(
      IEnumerable<JobPlan> jobs,
      int downloadUID,
      ReadOnlyDataSet currentSet,
      ISet<string> savedParts,
      DatabaseAccess.MazakDbType MazakType,
      bool checkPalletsUsedOnce,
      IList<string> log)
    {
      var allParts = BuildMazakParts(jobs, downloadUID, currentSet, MazakType, log);
      var groups = GroupProcessesIntoFixtures(allParts, checkPalletsUsedOnce, log);

      var usedFixtures = AssignMazakFixtures(groups, downloadUID, currentSet, savedParts, log);

      return new MazakJobs() {
        AllParts = allParts,
        Fixtures = groups,
        UsedFixtures = usedFixtures
      };
    }

    #region Parts
    private static List<MazakPart> BuildMazakParts(IEnumerable<JobPlan> jobs, int downloadID, ReadOnlyDataSet currentSet, DatabaseAccess.MazakDbType mazakTy,
                                                  IList<string> log)
    {
      var ret = new List<MazakPart>();

      foreach (var job in jobs)
      {
        if (job.NumProcesses == 1)
        {

          //each path gets a MazakPart
          for (int path = 1; path <= job.GetNumPaths(1); path++)
          {
            var mazakPart = new MazakPart(job, path, downloadID);

            string error;
            BuildProcFromJobWithOneProc(job, mazakPart, mazakTy, currentSet, out error);
            if (error == null || error == "")
              ret.Add(mazakPart);
            else
              log.Add(error + " Skipping part.");
          }

        }
        else
        {

          //each path group gets a MazakPart

          //maps process 1 paths to groups
          var pathGroups = new Dictionary<int, int>();

          for (int path = 1; path <= job.GetNumPaths(1); path++)
          {
            var grp = job.GetPathGroup(1, path);

            if (pathGroups.ContainsValue(grp))
            {
              log.Add("Part " + job.PartName + " has a path group where process 1 " +
                "has more than one part in a group.  This configuration is not supported by" +
                " the Mazak cell controller.  Skipping part.");
              goto skipPart;
            }

            pathGroups.Add(path, grp);
          }

          //check each path group has exactly one process for procs >= 2
          for (int proc = 2; proc <= job.NumProcesses; proc++)
          {
            foreach (int pGroup in pathGroups.Values)
            {
              int count = 0;
              for (int path = 1; path <= job.GetNumPaths(proc); path++)
              {
                if (job.GetPathGroup(proc, path) == pGroup)
                  count += 1;
              }

              if (count != 1)
              {
                log.Add("Part " + job.PartName + " has a part group where process " +
                        proc.ToString() + " has more than one path in the group.  This " +
                        "configuration is not supported by the Mazak cell controller. " +
                        "Skipping this part");
                goto skipPart;
              }
            }
          }

          foreach (var grp in pathGroups)
          {
            //label the part by the path number on process 1.
            var mazakPart = new MazakPart(job, grp.Key, downloadID);
            string error;
            BuildProcFromPathGroup(job, mazakPart, out error, mazakTy, currentSet,
                 (proc, path) => grp.Value == job.GetPathGroup(proc, path));

            if (error == null || error == "")
              ret.Add(mazakPart);
            else
              log.Add(error + " Skipping part.");
          }

        }

      skipPart:;
      }

      return ret;
    }

    //Func<int, int, bool> only introduced in .NET 3.5
    private delegate bool MatchFunc(int proc, int path);

    private static void BuildProcFromPathGroup(JobPlan job, MazakPart mazak, out string ErrorDuringCreate, DatabaseAccess.MazakDbType mazakTy,
                                                   ReadOnlyDataSet currentSet, MatchFunc matchPath)
    {
      ErrorDuringCreate = null;

      for (int proc = 1; proc <= job.NumProcesses; proc++)
      {
        for (int path = 1; path <= job.GetNumPaths(proc); path++)
        {
          if (matchPath(proc, path))
          {

            //Check this proc and path has a program
            bool has1Stop = false;
            foreach (var stop in job.GetMachiningStop(proc, path))
            {
              has1Stop = true;
              var lst = new List<JobMachiningStop.ProgramEntry>(stop.AllPrograms());

              if (lst.Count == 0)
              {
                ErrorDuringCreate = "Part " + job.PartName + " has no stations assigned.";
                return;
              }

              foreach (var p in lst)
              {
                if (p.Program == null || p.Program == "")
                {
                  ErrorDuringCreate = "Part " + job.PartName + " has no programs.";
                  return;
                }
                if (mazakTy == DatabaseAccess.MazakDbType.MazakVersionE)
                {
                  int progNum;
                  if (!int.TryParse(p.Program, out progNum))
                  {
                    ErrorDuringCreate = "Part " + job.PartName + " program " + p.Program +
                        " is not an integer.";
                    return;
                  }
                }
                foreach (ReadOnlyDataSet.MainProgramRow mRow in currentSet.MainProgram.Rows)
                {
                  if (!mRow.IsMainProgramNull() && mRow.MainProgram == p.Program)
                    goto foundProg;
                }
                ErrorDuringCreate = "Part " + job.PartName + " program " + p.Program +
                    " does not exist in the cell controller.";
                return;
              foundProg:;
              }
            }

            if (!has1Stop)
            {
              ErrorDuringCreate = "Part " + job.PartName + " has no machines assigned";
              return;
            }

            mazak.Processes.Add(ProcessFromJob(mazak, proc, path));
          }
        }
      }
    }

    private static void BuildProcFromJobWithOneProc(JobPlan job, MazakPart mazak, DatabaseAccess.MazakDbType mazakTy,
                                                    ReadOnlyDataSet currentSet, out string ErrorDuringCreate)
    {
      ErrorDuringCreate = null;

      // first try building from the job
      string FromJobError;
      BuildProcFromPathGroup(job, mazak, out FromJobError, mazakTy, currentSet,
            (proc, path) => path == mazak.Proc1Path);  // proc will always equal 1.

      if (FromJobError == null || FromJobError == "")
        return; //Success

      mazak.Processes.Clear();

      // No programs, so check for a template row
      ReadOnlyDataSet.PartRow TemplateRow = null;

      foreach (ReadOnlyDataSet.PartRow pRow in currentSet.Part.Rows)
      {
        if (pRow.PartName == job.PartName)
        {
          TemplateRow = pRow;
          break;
        }
      }

      if (TemplateRow == null)
      {
        ErrorDuringCreate = FromJobError + "  Also, no template row for " + job.PartName + " was found.";
        return;
      }

      foreach (ReadOnlyDataSet.PartProcessRow pRow in TemplateRow.GetPartProcessRows())
      {
        mazak.Processes.Add(new MazakProcessFromTemplate(mazak, pRow));
      }
    }
    #endregion

    #region Fixtures

    //group together the processes that use the same fixture
    private static List<MazakFixture> GroupProcessesIntoFixtures(IEnumerable<MazakPart> allParts, bool checkPalletsUsedOnce, IList<string> logMessages)
    {
      var fixtures = new List<MazakFixture>();

      // compute all pallet groups
      var palletGroups = new HashSet<string>();
      var seenPallets = new Dictionary<string, MazakProcess>();
      foreach (var part in allParts) {
        foreach (var proc in part.Processes) {
          var palGroup = string.Join(',', proc.Pallets().OrderBy(p => p));

          if (checkPalletsUsedOnce && !palletGroups.Contains(palGroup)) {
            foreach (var p in proc.Pallets()) {
              if (seenPallets.ContainsKey(p)) {
                var firstPart = seenPallets[p];
                throw new BlackMaple.MachineFramework.BadRequestException(
                    "Invalid pallet->part mapping. " +
                    firstPart.Part.Job.PartName + "-" + firstPart.ProcessNumber.ToString() +
                    " and " +
                    proc.Part.Job.PartName + "-" + proc.ProcessNumber.ToString() +
                    " do not have matching pallet lists.  " +
                    firstPart.Part.Job.PartName + "-" + firstPart.ProcessNumber.ToString() +
                    " is assigned to pallets " + string.Join(',', firstPart.Pallets()) +
                    " and " +
                    proc.Part.Job.PartName + "-" + proc.ProcessNumber.ToString() +
                    " is assigned to pallets " + palGroup);

              }
              seenPallets.Add(p, proc);
            }
          }

          palletGroups.Add(palGroup);
        }
      }

      //for each pallet group, add one fixture for each face
      int groupNum = 0;
      foreach (var palGroup in palletGroups) {
        var byFace = new Dictionary<int, MazakFixture>();

        foreach (var proc in allParts.SelectMany(p => p.Processes)) {

          // check if pallets match
          if (palGroup != string.Join(',', proc.Pallets().OrderBy(p => p)))
            continue;

          if (byFace.ContainsKey(proc.ProcessNumber)) {
            byFace[proc.ProcessNumber].Processes.Add(proc);
          } else {
            //start a new face
            var fix = new MazakFixture();
            fix.FixtureGroup = groupNum;
            fix.Face = proc.ProcessNumber;
            fix.Processes.Add(proc);
            fix.Pallets.AddRange(proc.Pallets());
            byFace.Add(fix.Face, fix);
            fixtures.Add(fix);
          }
        }

        groupNum += 1;
      }

      return fixtures;
    }

    private static ISet<string> AssignMazakFixtures(
      IEnumerable<MazakFixture> groups, int downloadUID, ReadOnlyDataSet currentSet, ISet<string> savedParts,
      IList<string> log)
    {
      //First calculate the available fixtures
      var usedFixtures = new HashSet<string>();
      foreach (ReadOnlyDataSet.PartProcessRow partProc in currentSet.PartProcess.Rows)
      {
        if (partProc.PartName.IndexOf(':') >= 0)
        {
          if (savedParts.Contains(partProc.PartName))
          {
            usedFixtures.Add(partProc.Fixture);
          }
        }
      }
      Log.Debug("Available Fixtures: {fixs}", usedFixtures);

      //For each group, create (or reuse) a fixture and add parts and pallets using this fixture.
      foreach (var group in groups)
      {
        group.Pallets.Sort();

        Log.Debug("Searching for fixtures for group {@group}", group);

        //check if we can reuse an existing fixture
        CheckExistingFixture(group, usedFixtures, currentSet);

        if (string.IsNullOrEmpty(group.MazakFixtureName))
        {
          //create a new fixture
          var fixture =
            "Fixt:" +
            downloadUID.ToString() + ":" +
            group.FixtureGroup.ToString() + ":" +
            group.Pallets[0].ToString() + ":" +
            group.Face.ToString();
          group.MazakFixtureName = fixture;
          Log.Debug("Creating new fixture {fix} for group {@group}", fixture, group);
        }

        usedFixtures.Add(group.MazakFixtureName);
      }

      return usedFixtures;
    }

    private static void CheckExistingFixture(
      MazakFixture group,
      ISet<string> availableFixtures,
      ReadOnlyDataSet currentSet)
    {
      //we need a fixture that exactly matches this pallet list and has all the processes.
      //Also, the fixture must be contained in a saved part.

      foreach (string fixture in availableFixtures)
      {
        int idx = fixture.LastIndexOf(':');
        if (idx < 0) continue; //skip, not one of our fixtures

        //try to parse face
        if (!int.TryParse(fixture.Substring(idx+1), out int face)) continue;
        if (face != group.Face) continue;

        //check pallets match
        var onPallets = new HashSet<string>();
        foreach (ReadOnlyDataSet.PalletRow palRow in currentSet.Pallet) {
          if (palRow.Fixture == fixture) {
            onPallets.Add(palRow.PalletNumber.ToString());
          }
        }

        if (!CheckListEqual(onPallets, group.Pallets)) continue;

        group.MazakFixtureName = fixture;
        Log.Debug("Assigning existing fixture {fix} to group {@group}", fixture, group);
        return;
      }
    }

    private static bool CheckListEqual<T>(IEnumerable<T> l1, IEnumerable<T> l2)
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

