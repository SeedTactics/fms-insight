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
    private static Serilog.ILogger Log = Serilog.Log.ForContext<MazakPart>();

    public readonly JobPlan Job;
    public readonly int DownloadID;
    public readonly List<MazakProcess> Processes;

    public MazakPart(JobPlan j, int downID)
    {
      Job = j;
      DownloadID = downID;
      Processes = new List<MazakProcess>();
    }

    public void CreateDatabaseRow(MazakWriteData transSet)
    {
      var newPartRow = new MazakPartRow();

      newPartRow.Command = MazakWriteCommand.Add;
      newPartRow.PartName = PartName;
      newPartRow.Comment = Comment;
      newPartRow.Price = 0;
      newPartRow.MaterialName = "";
      newPartRow.TotalProcess = Processes.Count;
      transSet.Parts.Add(newPartRow);
    }

    public string PartName
    {
      get
      {
        return Job.PartName + ":" + DownloadID.ToString() + ":" + Processes.First().Path.ToString();
      }
    }

    public string Comment
    {
      get
      {
        return CreateComment(Job.UniqueStr, Processes.Select(p => p.Path), Job.ManuallyCreatedJob);
      }
    }

    public static string CreateComment(string unique, IEnumerable<int> paths, bool manual)
    {
        if (manual)
          return unique + "-Path" + string.Join("-", paths) + "-1";
        else
          return unique + "-Path" + string.Join("-", paths) + "-0";
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

    public interface IProcToPath
    {
      int PathForProc(int proc);
    }
    private class ProcToPath : IProcToPath
    {
      private string unique;
      private IReadOnlyList<int> paths;
      public ProcToPath(string u, IReadOnlyList<int> p) {
        unique = u;
        paths = p;
      }
      public int PathForProc(int proc) {
        if (proc <= paths.Count)
          return paths[proc-1];
        else {
          Log.Debug("Unable to find path for {uniq} proc {proc}", unique, proc);
          return 1;
        }
      }
    }

    public static void ParseComment(string comment, out string unique, out IProcToPath procToPath, out bool manual)
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
        string[] pathsStr = comment.Substring(idx + 5).Split('-');
        procToPath = new ProcToPath(unique, pathsStr.Select(p =>
          int.TryParse(p, out int pNum) ? pNum : 1
        ).ToList());
      }
      else
      {
        unique = comment;
        procToPath = new ProcToPath(unique, new[] {1});
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
    public readonly int Path;

    public JobPlan Job
    {
      get { return Part.Job; }
    }

    protected MazakProcess(MazakPart parent, int proc, int path)
    {
      Part = parent;
      ProcessNumber = proc;
      Path = path;
    }

    public abstract IEnumerable<string> Pallets();
    public abstract IEnumerable<JobPlan.FixtureFace> Fixtures();

    public abstract void CreateDatabaseRow(MazakPartRow newPart, string fixture, MazakDbType mazakTy);

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
    public MazakProcessFromJob(MazakPart parent, int process, int pth)
      : base(parent, process, pth)
    {
    }

    public override IEnumerable<string> Pallets()
    {
      return Job.PlannedPallets(ProcessNumber, Path);
    }

    public override IEnumerable<JobPlan.FixtureFace> Fixtures()
    {
      return Job.PlannedFixtures(ProcessNumber, Path);
    }

    public override void CreateDatabaseRow(MazakPartRow newPart, string fixture, MazakDbType mazakTy)
    {
      var newPartProcRow = new MazakPartProcessRow();
      newPartProcRow.PartName = Part.PartName;
      newPartProcRow.ProcessNumber = ProcessNumber;
      newPartProcRow.Fixture = fixture;
      newPartProcRow.FixQuantity = Math.Max(1, Job.PartsPerPallet(ProcessNumber, Path));

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

      if (mazakTy != MazakDbType.MazakVersionE)
      {
        newPartProcRow.FixLDS = ConvertStatStrV1ToV2(newPartProcRow.FixLDS).ToString();
        newPartProcRow.RemoveLDS = ConvertStatStrV1ToV2(newPartProcRow.RemoveLDS).ToString();
        newPartProcRow.CutMc = ConvertStatStrV1ToV2(newPartProcRow.CutMc).ToString();
      }

      newPart.Processes.Add(newPartProcRow);
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
    public readonly MazakPartProcessRow TemplateProcessRow;

    public MazakProcessFromTemplate(MazakPart parent, MazakPartProcessRow template, int path)
      : base(parent, template.ProcessNumber, path)
    {
      TemplateProcessRow = template;
    }

    public override IEnumerable<string> Pallets()
    {
      return Job.PlannedPallets(1, Path);
    }

    public override IEnumerable<JobPlan.FixtureFace> Fixtures() => Enumerable.Empty<JobPlan.FixtureFace>();

    public override void CreateDatabaseRow(MazakPartRow newPart, string fixture, MazakDbType mazakTy)
    {
      char[] FixLDS = { '0', '0', '0', '0', '0', '0', '0', '0', '0', '0' };
      char[] UnfixLDS = { '0', '0', '0', '0', '0', '0', '0', '0', '0', '0' };
      char[] Cut = { '0', '0', '0', '0', '0', '0', '0', '0' };

      foreach (var routeEntry in Job.GetMachiningStop(1, Path))
      {
        foreach (int statNum in routeEntry.Stations())
        {
          Cut[statNum - 1] = statNum.ToString()[0];
        }
      }

      foreach (int statNum in Job.LoadStations(1, Path))
      {
        FixLDS[statNum - 1] = statNum.ToString()[0];
      }

      foreach (int statNum in Job.UnloadStations(1, Path))
      {
        UnfixLDS[statNum - 1] = statNum.ToString()[0];
      }

      var newPartProcRow = TemplateProcessRow.Clone();
      newPartProcRow.PartName = Part.PartName;
      newPartProcRow.Fixture = fixture;
      newPartProcRow.ProcessNumber = ProcessNumber;

      newPartProcRow.FixLDS = new string(FixLDS);
      newPartProcRow.RemoveLDS = new string(UnfixLDS);
      newPartProcRow.CutMc = new string(Cut);

      if (mazakTy != MazakDbType.MazakVersionE)
      {
        newPartProcRow.FixLDS = ConvertStatStrV1ToV2(newPartProcRow.FixLDS).ToString();
        newPartProcRow.RemoveLDS = ConvertStatStrV1ToV2(newPartProcRow.RemoveLDS).ToString();
        newPartProcRow.CutMc = ConvertStatStrV1ToV2(newPartProcRow.CutMc).ToString();
      }

      newPart.Processes.Add(newPartProcRow);
    }

    public override string ToString()
    {
      return "CopyFromTemplate " + base.Job.PartName + "-" + base.ProcessNumber.ToString() +
        " path " + base.Path.ToString();
    }
  }

  //A group of parts and pallets where any part can run on any pallet.  In addition,
  //distinct processes can run at the same time on a pallet.
  public class MazakFixture
  {
    public string BaseFixtureName {get;set;}
    public int FixtureGroup {get;set;}
    public string Face {get;set;}
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
      MazakSchedulesPartsPallets mazakData,
      ISet<string> savedParts,
      MazakDbType MazakType,
      bool checkPalletsUsedOnce,
      IList<string> log)
    {
      var allParts = BuildMazakParts(jobs, downloadUID, mazakData, MazakType, log);
      var groups = GroupProcessesIntoFixtures(allParts, checkPalletsUsedOnce, log);

      var usedFixtures = AssignMazakFixtures(groups, downloadUID, mazakData, savedParts, log);

      return new MazakJobs() {
        AllParts = allParts,
        Fixtures = groups,
        UsedFixtures = usedFixtures
      };
    }

    #region Parts
    private static List<MazakPart> BuildMazakParts(IEnumerable<JobPlan> jobs, int downloadID, MazakSchedulesPartsPallets mazakData, MazakDbType mazakTy,
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
            var mazakPart = new MazakPart(job, downloadID);

            string error;
            BuildProcFromJobWithOneProc(job, path, mazakPart, mazakTy, mazakData, out error);
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
            var mazakPart = new MazakPart(job, downloadID);
            string error;
            BuildProcFromPathGroup(job, mazakPart, out error, mazakTy, mazakData,
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

    private static void BuildProcFromPathGroup(JobPlan job, MazakPart mazak, out string ErrorDuringCreate, MazakDbType mazakTy,
                                                   MazakSchedulesPartsPallets mazakData, MatchFunc matchPath)
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
                if (mazakTy == MazakDbType.MazakVersionE)
                {
                  int progNum;
                  if (!int.TryParse(p.Program, out progNum))
                  {
                    ErrorDuringCreate = "Part " + job.PartName + " program " + p.Program +
                        " is not an integer.";
                    return;
                  }
                }
                if (!mazakData.MainPrograms.Contains(p.Program)) {
                  ErrorDuringCreate = "Part " + job.PartName + " program " + p.Program +
                      " does not exist in the cell controller.";
                  return;
                }
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

    private static void BuildProcFromJobWithOneProc(JobPlan job, int proc1path, MazakPart mazak, MazakDbType mazakTy,
                                                    MazakSchedulesPartsPallets mazakData, out string ErrorDuringCreate)
    {
      ErrorDuringCreate = null;

      // first try building from the job
      string FromJobError;
      BuildProcFromPathGroup(job, mazak, out FromJobError, mazakTy, mazakData,
            (proc, path) => path == proc1path);  // proc will always equal 1.

      if (FromJobError == null || FromJobError == "")
        return; //Success

      mazak.Processes.Clear();

      // No programs, so check for a template row
      MazakPartRow TemplateRow = null;

      foreach (var pRow in mazakData.Parts)
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

      foreach (var pRow in TemplateRow.Processes)
      {
        mazak.Processes.Add(new MazakProcessFromTemplate(mazak, pRow, proc1path));
      }
    }
    #endregion

    #region Fixtures

    //group together the processes that use the same fixture
    private static List<MazakFixture> GroupProcessesIntoFixtures(IEnumerable<MazakPart> allParts, bool checkPalletsUsedOnce, IList<string> logMessages)
    {
      var fixtures = new List<MazakFixture>();

      string palsToFixGroup(IEnumerable<string> pals) {
          return "pals:" + string.Join(",", pals.OrderBy(p => p));
      }
      string jobFixtureToFixGroup(MazakProcess proc, IEnumerable<JobPlan.FixtureFace> fixs)
      {
        if (fixs.Count() > 1) {
          throw new BlackMaple.MachineFramework.BadRequestException(
            "Invalid fixtures for " + proc.Job.PartName + "-" + proc.ProcessNumber.ToString() +
            ".  There can be at most one fixture and face for the part.");
        }
        return "fix:" + fixs.First().Fixture;
      }

      // compute all fixture groups
      var fixtureGroups = new HashSet<string>();
      var seenPallets = new Dictionary<string, MazakProcess>();
      foreach (var part in allParts) {
        foreach (var proc in part.Processes) {

          string fixGroup;
          var plannedFixs = proc.Fixtures();
          if (plannedFixs.Any()) {
            fixGroup = jobFixtureToFixGroup(proc, plannedFixs);
          } else {
            // fallback to pallet groups
            fixGroup = palsToFixGroup(proc.Pallets());
          }

          if (checkPalletsUsedOnce && !fixtureGroups.Contains(fixGroup)) {
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
                    " is assigned to pallets " + string.Join(",", firstPart.Pallets()) +
                    " and " +
                    proc.Part.Job.PartName + "-" + proc.ProcessNumber.ToString() +
                    " is assigned to pallets " + string.Join(",", proc.Pallets()));

              }
              seenPallets.Add(p, proc);
            }
          }

          fixtureGroups.Add(fixGroup);
        }
      }

      //for each fixture group, add one mazak fixture for each face
      int groupNum = 0;
      foreach (var fixGroup in fixtureGroups) {
        var byFace = new Dictionary<string, MazakFixture>();

        foreach (var proc in allParts.SelectMany(p => p.Processes)) {

          var plannedFixes = proc.Fixtures();

          string face;
          string baseFixtureName;
          if (plannedFixes.Any()) {
            //check if correct fixture group
            if (fixGroup != jobFixtureToFixGroup(proc, plannedFixes))
              continue;
            face = plannedFixes.First().Face;
            baseFixtureName = plannedFixes.First().Fixture;
          } else {
            // check if pallets match
            if (fixGroup != palsToFixGroup(proc.Pallets()))
              continue;
            face = proc.ProcessNumber.ToString();
            baseFixtureName = groupNum.ToString() + ":" + proc.Pallets().First();
          }

          if (byFace.ContainsKey(face)) {
            byFace[face].Processes.Add(proc);
          } else {
            //start a new face
            var fix = new MazakFixture();
            fix.BaseFixtureName = baseFixtureName;
            fix.FixtureGroup = groupNum;
            fix.Face = face;
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
      IEnumerable<MazakFixture> groups, int downloadUID, MazakSchedulesPartsPallets mazakData, ISet<string> savedParts,
      IList<string> log)
    {
      //First calculate the available fixtures
      var usedFixtures = new HashSet<string>();
      foreach (var partProc in mazakData.Parts.SelectMany(p => p.Processes))
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
        CheckExistingFixture(group, usedFixtures, mazakData);

        if (string.IsNullOrEmpty(group.MazakFixtureName))
        {
          //create a new fixture
          var fixture =
            "Fixt:" +
            downloadUID.ToString() + ":" +
            group.BaseFixtureName + ":" +
            group.Face;
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
      MazakSchedulesPartsPallets mazakData)
    {
      //we need a fixture that exactly matches this pallet list and has all the processes.
      //Also, the fixture must be contained in a saved part.

      foreach (string fixture in availableFixtures)
      {
        int idx = fixture.LastIndexOf(':');
        if (idx < 0) continue; //skip, not one of our fixtures

        //try to parse face
        if (fixture.Substring(idx+1) != group.Face) continue;

        //check pallets match
        var onPallets = new HashSet<string>();
        foreach (var palRow in mazakData.Pallets) {
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

