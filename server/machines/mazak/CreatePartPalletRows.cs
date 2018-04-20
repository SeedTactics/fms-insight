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
        if (Job.ManuallyCreatedJob)
          return Job.UniqueStr + "-Path" + Proc1Path.ToString() + "-1";
        else
          return Job.UniqueStr + "-Path" + Proc1Path.ToString() + "-0";
      }
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
      newPartProcRow.Fixture = fixture + ":" + ProcessNumber.ToString();
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
      newPartProcRow.Fixture = fixture + ":" + ProcessNumber.ToString();
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

  public class CreatePartPalletRows
  {
    //Allows plugins to customize the process creation
    public delegate MazakProcess ProcessFromJobDelegate(MazakPart parent, int process, int pth);
    public static ProcessFromJobDelegate ProcessFromJob
      = (parent, process, pth) => new MazakProcessFromJob(parent, process, pth);

    #region Parts
    public static List<MazakPart> BuildMazakParts(IEnumerable<JobPlan> jobs, int downloadID, ReadOnlyDataSet currentSet, DatabaseAccess.MazakDbType mazakTy,
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

    #region Pallets
    public static void CreatePalletRow(TransactionDataSet transSet, ReadOnlyDataSet currentSet,
                                       string pallet, string fixture, int graph, int downloadID)
    {
      int palNum = int.Parse(pallet);

      foreach (ReadOnlyDataSet.PalletRow palRow in currentSet.Pallet.Rows)
      {
        if (palRow.PalletNumber == palNum && palRow.Fixture == fixture)
        {
          return;
        }
      }

      //we have the + 1 because UIDs and graphs start at 0, and the user might add other fixture-pallet
      //on group 0.
      int fixGroup = (downloadID * 10 + (graph % 10)) + 1;

      //Add rows to both V1 and V2.
      TransactionDataSet.Pallet_tV2Row newRow2 = transSet.Pallet_tV2.NewPallet_tV2Row();
      newRow2.Command = TransactionDatabaseAccess.AddCommand;
      newRow2.PalletNumber = palNum;
      newRow2.Fixture = fixture;
      newRow2.RecordID = 0;
      newRow2.FixtureGroup = fixGroup;
      transSet.Pallet_tV2.AddPallet_tV2Row(newRow2);

      TransactionDataSet.Pallet_tV1Row newRow1 = transSet.Pallet_tV1.NewPallet_tV1Row();
      newRow1.Command = TransactionDatabaseAccess.AddCommand;
      newRow1.PalletNumber = palNum;
      newRow1.Fixture = fixture;

      //combos with an angle in the range 0-999, and we don't want to conflict with that
      newRow1.Angle = (fixGroup * 1000);

      transSet.Pallet_tV1.AddPallet_tV1Row(newRow1);
    }
    #endregion
  }
}

