/* Copyright (c) 2022, John Lenz

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

namespace MazakMachineInterface
{
  //A mazak part cooresponds to a (Job,PathGroup) pair.
  public class MazakPart
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<MazakPart>();

    public readonly record struct MazakCommentInfo(string Unique, bool IsSplit, int FmsProcess)
    {
      public bool HasUnique => !string.IsNullOrEmpty(Unique);

      public int JobProcessForMazakProcess(int mazakProcess)
      {
        return IsSplit ? FmsProcess : mazakProcess;
      }

      public int TotalProcesses(IRepository jobDB, int fallbackNumProcesses)
      {
        return IsSplit
          ? jobDB.LoadJob(Unique)?.Processes.Count ?? fallbackNumProcesses
          : fallbackNumProcesses;
      }
    }

    /*
      Mazak parts and schedules use two different comment formats to identify the unique:

      - Combined schedules (all processes on a single schedule) use `{uniqueStr}-Insight`

      - Split-schedule jobs use `{uniqueStr}-{proc}-{path}-InsightS`.

      We use an `S` suffix for "split" to allow parsing logic to distinguish split vs
      non-split.This guarantees that if an `InsightS` suffix is present, the entries
      for `-{proc}-{path}-` are always present and will not be confused as part of the
      unique string.
    */

    private const string CombinedInsightSuffix = "-Insight";
    private const string SplitInsightSuffix = "-InsightS";

    public readonly Job Job;
    public readonly int DownloadID;
    public readonly List<MazakProcess> Processes;
    public readonly int? SplitJobProcess;
    public readonly int? SplitPath;

    public MazakPart(Job j, int downID, int partIdx)
    {
      Job = j;
      DownloadID = downID;
      Processes = new List<MazakProcess>();
      PartName = Job.PartName + ":" + DownloadID.ToString() + ":" + partIdx.ToString();
    }

    public MazakPart(Job j, int downID, int partIdx, int splitJobProcess, int splitPath)
    {
      Job = j;
      DownloadID = downID;
      Processes = new List<MazakProcess>();
      SplitJobProcess = splitJobProcess;
      SplitPath = splitPath;
      PartName =
        Job.PartName
        + ":"
        + DownloadID.ToString()
        + ":"
        + partIdx.ToString()
        + ":"
        + splitJobProcess.ToString();
    }

    public MazakPartRow ToDatabaseRow()
    {
      var newPartRow = new MazakPartRow()
      {
        Command = MazakWriteCommand.Add,
        PartName = PartName,
        Comment = Comment,
        Price = 0,
        MaterialName = "",
        TotalProcess = Processes.Count,
      };
      return newPartRow;
    }

    public string PartName { get; }

    public bool IsSplitPart => SplitJobProcess.HasValue;

    public string Comment =>
      IsSplitPart
        ? Job.UniqueStr
          + "-"
          + SplitJobProcess.Value.ToString()
          + "-"
          + SplitPath.GetValueOrDefault(1).ToString()
          + SplitInsightSuffix
        : Job.UniqueStr + CombinedInsightSuffix;

    public static bool IsSplitComment(string comment)
    {
      return !string.IsNullOrEmpty(comment)
        && comment.EndsWith(SplitInsightSuffix, StringComparison.Ordinal);
    }

    public static bool IsSailPart(string partName, string comment)
    {
      if (partName == null || comment == null)
        return false;
      if (
        !(
          comment.EndsWith(CombinedInsightSuffix, StringComparison.Ordinal)
          || IsSplitComment(comment)
          || IsLegacyPathComment(comment)
        )
      )
        return false;

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

    private static bool IsLegacyPathComment(string comment)
    {
      var pathIdx = comment.LastIndexOf("-Path", StringComparison.Ordinal);
      if (pathIdx < 0)
        return false;

      var pieces = comment.Substring(pathIdx + 5).Split('-');
      return pieces.Length == 3 && pieces.All(p => int.TryParse(p, out _));
    }

    public static string ExtractPartNameFromMazakPartName(string mazakPartName)
    {
      int loc = mazakPartName.IndexOf(':');
      if (loc >= 0)
        mazakPartName = mazakPartName.Substring(0, loc);
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

    public static string UniqueFromComment(string comment)
    {
      if (comment.EndsWith(CombinedInsightSuffix, StringComparison.Ordinal))
      {
        return comment.Substring(0, comment.Length - CombinedInsightSuffix.Length);
      }

      if (IsSplitComment(comment))
      {
        var suffixStart = comment.Length - SplitInsightSuffix.Length;
        var pathSep = comment.LastIndexOf('-', suffixStart - 1);
        if (pathSep < 0)
          return comment;

        var procSep = comment.LastIndexOf('-', pathSep - 1);
        if (procSep < 0)
          return comment;

        return comment.Substring(0, procSep);
      }

      // Old FMS Insights had a -Path, strip it off for backwards compatibility
      int idx = comment.LastIndexOf("-Path");

      if (idx < 0)
      {
        return comment;
      }
      else
      {
        return comment.Substring(0, idx);
      }
    }

    public static int ProcessFromComment(string comment)
    {
      if (!IsSplitComment(comment))
        return 1;

      var suffixStart = comment.Length - SplitInsightSuffix.Length;
      var pathSep = comment.LastIndexOf('-', suffixStart - 1);
      if (pathSep < 0)
        return 1;

      var procSep = comment.LastIndexOf('-', pathSep - 1);
      if (procSep < 0)
        return 1;

      return int.TryParse(comment.Substring(procSep + 1, pathSep - procSep - 1), out var proc)
        ? proc
        : 1;
    }

    public static MazakCommentInfo ParseCommentInfo(string comment)
    {
      if (string.IsNullOrEmpty(comment))
        return new MazakCommentInfo(Unique: "", IsSplit: false, FmsProcess: 1);
      var isSplit = IsSplitComment(comment);
      return new MazakCommentInfo(
        Unique: UniqueFromComment(comment),
        IsSplit: isSplit,
        FmsProcess: isSplit ? ProcessFromComment(comment) : 1
      );
    }
  }

  public abstract class MazakProcess
  {
    public static string CreateMainProgramComment(string program, long revision)
    {
      return "Insight:" + revision.ToString() + ":" + program;
    }

    public static bool IsInsightMainProgram(string mainProgramComment)
    {
      return mainProgramComment.StartsWith("Insight:");
    }

    public static bool TryParseMainProgramComment(
      string mainProgramComment,
      out string program,
      out long rev
    )
    {
      if (mainProgramComment.StartsWith("Insight:"))
      {
        mainProgramComment = mainProgramComment.Substring(8);
        var idx = mainProgramComment.IndexOf(':');
        if (idx > 0)
        {
          if (long.TryParse(mainProgramComment.Substring(0, idx), out rev))
          {
            program = mainProgramComment.Substring(idx + 1);
            return true;
          }
        }
      }

      program = null;
      rev = 0;
      return false;
    }

    public readonly MazakPart Part;
    public readonly int ProcessNumber;
    public readonly int JobProcessNumber;
    public readonly int Path;

    public Job Job
    {
      get { return Part.Job; }
    }

    public ProcPathInfo PathInfo
    {
      get { return Part.Job.Processes[JobProcessNumber - 1].Paths[Path - 1]; }
    }

    protected MazakProcess(MazakPart parent, int proc, int jobProc, int path)
    {
      Part = parent;
      ProcessNumber = proc;
      JobProcessNumber = jobProc;
      Path = path;
    }

    public abstract IEnumerable<int> Pallets();
    public abstract (string fixture, int? face) FixtureFace();
    public abstract ProgramRevision PartProgram { get; set; }

    public abstract void CreateDatabaseRow(MazakPartRow newPart, string fixture, MazakConfig cfg);

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
    public override ProgramRevision PartProgram { get; set; }

    public MazakProcessFromJob(
      MazakPart parent,
      int process,
      int pth,
      ProgramRevision prog,
      int? jobProcess = null
    )
      : base(parent, process, jobProcess ?? parent.SplitJobProcess ?? process, pth)
    {
      PartProgram = prog;
    }

    public override IEnumerable<int> Pallets()
    {
      return PathInfo.PalletNums ?? Enumerable.Empty<int>();
    }

    public override (string fixture, int? face) FixtureFace()
    {
      return (PathInfo.Fixture, PathInfo.Face);
    }

    public override void CreateDatabaseRow(
      MazakPartRow newPart,
      string fixture,
      MazakConfig mazakCfg
    )
    {
      char[] FixLDS = { '0', '0', '0', '0', '0', '0', '0', '0', '0', '0' };
      char[] UnfixLDS = { '0', '0', '0', '0', '0', '0', '0', '0', '0', '0' };
      char[] Cut = { '0', '0', '0', '0', '0', '0', '0', '0' };

      foreach (var routeEntry in PathInfo.Stops)
      {
        foreach (int statNum in routeEntry.Stations)
        {
          int stat =
            mazakCfg.MachineNumbers == null
              ? statNum
              : mazakCfg.MachineNumbers.IndexOf(statNum) + 1;
          Cut[stat - 1] = stat.ToString()[0];
        }
      }

      foreach (int statNum in PathInfo.Load)
        FixLDS[statNum - 1] = statNum.ToString()[0];

      foreach (int statNum in PathInfo.Unload)
        UnfixLDS[statNum - 1] = statNum.ToString()[0];

      var newPartProcRow = new MazakPartProcessRow()
      {
        PartName = Part.PartName,
        ProcessNumber = ProcessNumber,
        Fixture = fixture,
        FixQuantity = Math.Max(1, PathInfo.PartsPerPallet),
        ContinueCut = 0,
        //newPartProcRow.FixPhoto = "";
        //newPartProcRow.RemovePhoto = "";
        WashType = 0,
        MainProgram = PartProgram.CellControllerProgramName,
        FixLDS =
          mazakCfg.DBType != MazakDbType.MazakVersionE
            ? ConvertStatStrV1ToV2(new string(FixLDS)).ToString()
            : new string(FixLDS),
        RemoveLDS =
          mazakCfg.DBType != MazakDbType.MazakVersionE
            ? ConvertStatStrV1ToV2(new string(UnfixLDS)).ToString()
            : new string(UnfixLDS),
        CutMc =
          mazakCfg.DBType != MazakDbType.MazakVersionE
            ? ConvertStatStrV1ToV2(new string(Cut)).ToString()
            : new string(Cut),
      };

      newPart.Processes.Add(newPartProcRow);
    }

    public override string ToString()
    {
      return "CopyFromJob "
        + base.Job.PartName
        + "-"
        + base.JobProcessNumber.ToString()
        + " path "
        + Path.ToString();
    }
  }

  //A group of parts and pallets where any part can run on any pallet.  In addition,
  //distinct processes can run at the same time on a pallet.
  public class MazakFixture
  {
    public int FixtureGroup { get; set; }
    public string JobFixtureName { get; set; }
    public string Face { get; set; }
    public List<MazakProcess> Processes = new List<MazakProcess>();
    public HashSet<int> Pallets = new HashSet<int>();
    public string MazakFixtureName { get; set; }

    public IEnumerable<MazakPalletRow> CreateDatabasePalletRows(
      MazakAllData oldData,
      MazakConfig cfg,
      int downloadUID
    )
    {
      var ret = new List<MazakPalletRow>();
      foreach (var palNum in Pallets.OrderBy(p => p))
      {
        // palNum is an FMS Insight pallet number; convert to Mazak-internal number
        var mazakPalNum = cfg.InversePalletNumber(palNum);

        bool foundExisting = false;
        foreach (var palRow in oldData.Pallets)
        {
          if (palRow.PalletNumber == mazakPalNum && palRow.Fixture == MazakFixtureName)
          {
            foundExisting = true;
            break;
          }
        }
        if (foundExisting)
          continue;

        //Add rows to both V1 and V2.
        var newRow = new MazakPalletRow()
        {
          Command = MazakWriteCommand.Add,
          PalletNumber = mazakPalNum,
          Fixture = MazakFixtureName,
          RecordID = 0,
          FixtureGroupV2 = cfg.OverrideFixtureGroupToZero ? 0 : FixtureGroup,
          //combos with an angle in the range 0-999, and we don't want to conflict with that
          AngleV1 = cfg.OverrideFixtureGroupToZero ? 0 : (FixtureGroup * 1000),
        };

        ret.Add(newRow);
      }
      return ret;
    }
  }

  public class MazakJobs
  {
    public MazakAllData OldMazakData { get; set; }
    public MazakDbType MazakType { get; set; }
    public int DownloadUID { get; set; }
    public ISet<string> SavedParts { get; set; }

    //The representation of the mazak parts and pallets used to create the database rows
    public IEnumerable<MazakPart> AllParts { get; set; }
    public IEnumerable<MazakFixture> Fixtures { get; set; }

    //fixtures used by either existing or new parts.  Fixtures in the Mazak not in this list
    //will be deleted and fixtures appearing in this list but not yet in Mazak will be added.
    public ISet<string> UsedFixtures { get; set; }

    //main programs used by either existing or new parts
    public ISet<string> UsedMainProgramComments { get; set; }

    public MazakWriteData DeleteFixtureAndProgramDatabaseRows()
    {
      var fixRows = new List<MazakFixtureRow>();
      var progs = new List<NewMazakProgram>();

      //delete unused fixtures
      foreach (var fixRow in OldMazakData.Fixtures)
      {
        if (fixRow.Comment == "Insight")
        {
          if (!UsedFixtures.Contains(fixRow.FixtureName))
          {
            var newFixRow = fixRow with { Command = MazakWriteCommand.Delete };
            fixRows.Add(newFixRow);
          }
        }
      }

      // delete old programs, but only if there is a newer revision for this program
      var maxRevForProg = OldMazakData
        .MainPrograms.Select(p =>
          MazakProcess.TryParseMainProgramComment(p.Comment, out string pName, out long rev)
            ? new { pName, rev }
            : null
        )
        .Where(p => p != null)
        .ToLookup(p => p.pName, p => p.rev)
        .ToDictionary(ps => ps.Key, ps => ps.Max());

      foreach (var prog in OldMazakData.MainPrograms)
      {
        if (!MazakProcess.IsInsightMainProgram(prog.Comment))
          continue;
        if (UsedMainProgramComments.Contains(prog.Comment))
          continue;
        if (!MazakProcess.TryParseMainProgramComment(prog.Comment, out string pName, out long rev))
          continue;
        if (rev >= maxRevForProg[pName])
          continue;

        progs.Add(
          new NewMazakProgram()
          {
            Command = MazakWriteCommand.Delete,
            MainProgram = prog.MainProgram,
            Comment = prog.Comment,
          }
        );
      }

      return new MazakWriteData()
      {
        Prefix = "Delete Fixtures",
        Fixtures = fixRows,
        Programs = progs,
      };
    }

    public MazakWriteData AddFixtureAndProgramDatabaseRows(
      Func<string, long, string> getProgramContent,
      string programDir
    )
    {
      var fixRows = new List<MazakFixtureRow>();

      //add new fixtures
      foreach (string fixture in UsedFixtures)
      {
        //check if this fixture exists already... could exist already if we reuse fixtures
        foreach (var fixRow in OldMazakData.Fixtures)
        {
          if (fixRow.FixtureName == fixture)
          {
            goto found;
          }
        }

        var newFixRow = new MazakFixtureRow()
        {
          Command = MazakWriteCommand.Add,
          FixtureName = fixture,
          Comment = "Insight",
        };
        fixRows.Add(newFixRow);
        found:
        ;
      }

      // add programs
      var newProgs = new Dictionary<(string name, long rev), NewMazakProgram>();
      foreach (var part in AllParts)
      {
        foreach (var proc in part.Processes)
        {
          if (
            string.IsNullOrEmpty(proc.PartProgram.CellControllerProgramName)
            && !string.IsNullOrEmpty(proc.PartProgram.ProgramName)
          )
          {
            NewMazakProgram newProg;
            if (newProgs.ContainsKey((proc.PartProgram.ProgramName, proc.PartProgram.Revision)))
            {
              newProg = newProgs[(proc.PartProgram.ProgramName, proc.PartProgram.Revision)];
            }
            else
            {
              newProg = new NewMazakProgram()
              {
                Command = MazakWriteCommand.Add,
                ProgramName = proc.PartProgram.ProgramName,
                ProgramRevision = proc.PartProgram.Revision,
                MainProgram = System.IO.Path.Combine(
                  programDir,
                  "rev" + proc.PartProgram.Revision.ToString(),
                  // filenames have a maximum length
                  proc.PartProgram.ProgramName + ".EIA"
                ),
                Comment = MazakProcess.CreateMainProgramComment(
                  proc.PartProgram.ProgramName,
                  proc.PartProgram.Revision
                ),
                ProgramContent = getProgramContent(
                  proc.PartProgram.ProgramName,
                  proc.PartProgram.Revision
                ),
              };
              newProgs[(newProg.ProgramName, newProg.ProgramRevision)] = newProg;
            }
            proc.PartProgram = proc.PartProgram with
            {
              CellControllerProgramName = newProg.MainProgram,
            };
          }
        }
      }

      return new MazakWriteData()
      {
        Prefix = "Add Fixtures",
        Fixtures = fixRows,
        Programs = newProgs.Values.ToArray(),
      };
    }

    public MazakWriteData CreatePartPalletDatabaseRows(MazakConfig cfg)
    {
      var partRows = new List<MazakPartRow>();
      var palRows = new List<MazakPalletRow>();

      foreach (var p in AllParts)
        partRows.Add(p.ToDatabaseRow());

      var byName = partRows.ToDictionary(p => p.PartName, p => p);

      foreach (var g in Fixtures)
      {
        foreach (var p in g.Processes)
        {
          p.CreateDatabaseRow(byName[p.Part.PartName], g.MazakFixtureName, cfg);
        }

        foreach (var p in g.CreateDatabasePalletRows(OldMazakData, cfg, DownloadUID))
          palRows.Add(p);
      }

      return new MazakWriteData()
      {
        Prefix = "Add Parts",
        Parts = partRows,
        Pallets = palRows,
      };
    }

    public MazakWriteData DeleteOldPartRows()
    {
      var partRows = new List<MazakPartRow>();
      foreach (var partRow in OldMazakData.Parts)
      {
        if (MazakPart.IsSailPart(partRow.PartName, partRow.Comment))
        {
          if (!SavedParts.Contains(partRow.PartName))
          {
            var newPartRow = partRow with
            {
              Command = MazakWriteCommand.Delete,
              TotalProcess = partRow.Processes.Count(),
              Processes =
                new List<MazakPartProcessRow>() // when deleting, don't need to add process rows
              ,
            };
            partRows.Add(newPartRow);
          }
        }
      }
      return new MazakWriteData() { Prefix = "Delete Parts", Parts = partRows };
    }

    public MazakWriteData DeleteOldPalletRows()
    {
      var palRows = new List<MazakPalletRow>();
      foreach (var palRow in OldMazakData.Pallets)
      {
        int idx = palRow.Fixture.IndexOf(':');

        if (idx >= 0)
        {
          //check if this fixture is being used by a new schedule
          //or is a fixture used by a part in savedParts

          if (!UsedFixtures.Contains(palRow.Fixture))
          {
            //not found, can delete it
            var newPalRow = palRow with
            {
              Command = MazakWriteCommand.Delete,
            };
            palRows.Add(newPalRow);
          }
        }
      }
      return new MazakWriteData() { Prefix = "Delete Pallets", Pallets = palRows };
    }
  }

  public static class ConvertJobsToMazakParts
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<MazakJobs>();

    //Allows plugins to customize the process creation
    public delegate MazakProcess ProcessFromJobDelegate(
      MazakPart parent,
      int process,
      int pth,
      ProgramRevision prog
    );

    public static MazakJobs JobsToMazak(
      IEnumerable<Job> jobs,
      int downloadUID,
      MazakAllData mazakData,
      ISet<string> savedParts,
      MazakConfig mazakCfg,
      BlackMaple.MachineFramework.FMSSettings fmsSettings,
      Func<string, long?, ProgramRevision> lookupProgram,
      IList<string> errors
    )
    {
      Validate(jobs, fmsSettings, errors);
      CheckReusedFixture(mazakData, errors);
      var allParts = BuildMazakParts(jobs, downloadUID, mazakData, mazakCfg, errors, lookupProgram);
      var usedMazakFixGroups = new HashSet<int>(mazakData.Pallets.Select(f => f.FixtureGroup));
      var groups = GroupProcessesIntoFixtures(
        allParts,
        usedMazakFixGroups,
        mazakCfg.UseStartingOffsetForDueDate,
        errors
      );

      var usedFixtures = AssignMazakFixtures(
        groups,
        downloadUID,
        mazakData,
        savedParts,
        mazakCfg.UseStartingOffsetForDueDate,
        errors
      );
      var usedProgs = CalculateUsedPrograms(mazakData, allParts);

      return new MazakJobs()
      {
        OldMazakData = mazakData,
        MazakType = mazakCfg.DBType,
        DownloadUID = downloadUID,
        SavedParts = savedParts,
        AllParts = allParts,
        Fixtures = groups,
        UsedFixtures = usedFixtures,
        UsedMainProgramComments = usedProgs,
      };
    }

    public static bool IsSplitScheduleJob(Job job)
    {
      if (job == null)
        return false;

      return AllProcessesHaveBasketStations(job) || AllConsecutiveQueuesDiffer(job);
    }

    #region Validate
    private static void Validate(
      IEnumerable<Job> jobs,
      BlackMaple.MachineFramework.FMSSettings fmsSettings,
      IList<string> errs
    )
    {
      //only allow numeric pallets and check queues
      foreach (Job part in jobs)
      {
        ValidateSplitScheduleRequirements(part, errs);

        for (int proc = 1; proc <= part.Processes.Count; proc++)
        {
          for (int path = 1; path <= part.Processes[proc - 1].Paths.Count; path++)
          {
            var info = part.Processes[proc - 1].Paths[path - 1];

            var inQueue = info.InputQueue;
            if (!string.IsNullOrEmpty(inQueue) && !fmsSettings.Queues.ContainsKey(inQueue))
            {
              errs.Add(
                " Job "
                  + part.UniqueStr
                  + " has an input queue "
                  + inQueue
                  + " which is not configured as a local queue in FMS Insight."
                  + " All input queues must be local queues, not an external queue."
              );
            }

            var outQueue = info.OutputQueue;
            if (proc == part.Processes.Count)
            {
              if (
                !string.IsNullOrEmpty(outQueue) && !fmsSettings.ExternalQueues.ContainsKey(outQueue)
              )
              {
                errs.Add(
                  "Output queues on the final process must be external queues."
                    + " Job "
                    + part.UniqueStr
                    + " has a queue "
                    + outQueue
                    + " on the final process which is not configured "
                    + " as an external queue"
                );
              }
            }
            else
            {
              if (!string.IsNullOrEmpty(outQueue) && !fmsSettings.Queues.ContainsKey(outQueue))
              {
                errs.Add(
                  " Job "
                    + part.UniqueStr
                    + " has an output queue "
                    + outQueue
                    + " which is not configured as a queue in FMS Insight."
                    + " Non-final processes must have a configured local queue, not an external queue"
                );
              }
            }
          }
        }
      }
    }

    private static void ValidateSplitScheduleRequirements(Job part, IList<string> errs)
    {
      var missingBasketProcesses = Enumerable
        .Range(1, part.Processes.Count)
        .Where(proc => !ProcessHasBasketStations(part.Processes[proc - 1]))
        .ToList();
      var hasAnyBasketStations = part.Processes.Any(ProcessHasAnyBasketStations);
      var hasAllBasketStations = missingBasketProcesses.Count == 0;

      if (hasAnyBasketStations && !hasAllBasketStations)
      {
        errs.Add(
          "Job "
            + part.UniqueStr
            + " must specify BasketLoadStations and BasketUnloadStations for every process or none. Missing basket stations on processes "
            + string.Join(", ", missingBasketProcesses)
            + "."
        );
      }

      var differingQueuePairs = Enumerable
        .Range(1, Math.Max(0, part.Processes.Count - 1))
        .Where(proc => ConsecutiveQueuesDiffer(part, proc))
        .ToList();
      if (differingQueuePairs.Count > 0 && differingQueuePairs.Count != part.Processes.Count - 1)
      {
        errs.Add(
          "Job "
            + part.UniqueStr
            + " must use differing output and input queues for every consecutive process pair or none when enabling split Mazak schedules. Differing pairs start at processes "
            + string.Join(", ", differingQueuePairs)
            + "."
        );
      }

      if (IsSplitScheduleJob(part))
      {
        var multiPathProcesses = Enumerable
          .Range(1, part.Processes.Count)
          .Where(proc => part.Processes[proc - 1].Paths.Count > 1)
          .ToList();
        if (multiPathProcesses.Count > 0)
        {
          errs.Add(
            "Job "
              + part.UniqueStr
              + " uses split Mazak schedules and must have exactly one path per process. Multiple paths found on processes "
              + string.Join(", ", multiPathProcesses)
              + "."
          );
        }
      }
    }

    private static bool AllProcessesHaveBasketStations(Job job)
    {
      return job.Processes.Count > 0 && job.Processes.All(ProcessHasBasketStations);
    }

    private static bool ProcessHasBasketStations(ProcessInfo process)
    {
      return HasStations(process.BasketLoadStations) && HasStations(process.BasketUnloadStations);
    }

    private static bool ProcessHasAnyBasketStations(ProcessInfo process)
    {
      return HasStations(process.BasketLoadStations) || HasStations(process.BasketUnloadStations);
    }

    private static bool HasStations(System.Collections.Generic.IReadOnlyCollection<int> stations)
    {
      return stations != null && stations.Count > 0;
    }

    private static bool AllConsecutiveQueuesDiffer(Job job)
    {
      return job.Processes.Count > 1
        && Enumerable
          .Range(1, job.Processes.Count - 1)
          .All(proc => ConsecutiveQueuesDiffer(job, proc));
    }

    private static bool ConsecutiveQueuesDiffer(Job job, int process)
    {
      if (process <= 0 || process >= job.Processes.Count)
        return false;
      if (job.Processes[process - 1].Paths.Count == 0 || job.Processes[process].Paths.Count == 0)
        return false;

      var outputQueue = job.Processes[process - 1].Paths[0].OutputQueue ?? "";
      var inputQueue = job.Processes[process].Paths[0].InputQueue ?? "";
      return !string.Equals(outputQueue, inputQueue, StringComparison.Ordinal);
    }

    private static void CheckReusedFixture(MazakAllData mazakData, IList<string> errors)
    {
      foreach (var part in mazakData.Parts)
      {
        var isInsight = MazakPart.IsSailPart(part.PartName, part.Comment);
        if (!isInsight)
        {
          foreach (var proc in part.Processes)
          {
            if (proc.Fixture.Contains(':'))
            {
              errors.Add(
                $"Non-Insight part {part.PartName} in the Mazak cell controller is using an Insight fixture {proc.Fixture}.  Please edit the part in the cell controller to not use an Insight fixture."
              );
            }
          }
        }
      }
    }
    #endregion

    #region Parts
    private static List<MazakPart> BuildMazakParts(
      IEnumerable<Job> jobs,
      int downloadID,
      MazakAllData mazakData,
      MazakConfig mazakCfg,
      IList<string> log,
      Func<string, long?, ProgramRevision> lookupProgram
    )
    {
      var ret = new List<MazakPart>();
      int partIdx = 1;

      foreach (var job in jobs)
      {
        for (int proc = 1; proc <= job.Processes.Count; proc++)
        {
          if (job.Processes[proc - 1].Paths.Count != 1)
          {
            log.Add(
              "Part "
                + job.PartName
                + " must use separate jobs per path. Multiple paths in a single job is not supported by the Mazak Cell controller"
            );
            goto skipPart;
          }
        }

        if (IsSplitScheduleJob(job))
        {
          for (int proc = 1; proc <= job.Processes.Count; proc++)
          {
            var mazakPart = new MazakPart(job, downloadID, partIdx, proc, 1);
            partIdx += 1;
            BuildProcFromJob(
              job,
              mazakPart,
              out string error,
              mazakCfg,
              mazakData,
              lookupProgram,
              onlyProcess: proc
            );

            if (string.IsNullOrEmpty(error))
              ret.Add(mazakPart);
            else
              log.Add(error);
          }
        }
        else
        {
          var mazakPart = new MazakPart(job, downloadID, partIdx);
          partIdx += 1;
          BuildProcFromJob(job, mazakPart, out string error, mazakCfg, mazakData, lookupProgram);

          if (error == null || error == "")
            ret.Add(mazakPart);
          else
            log.Add(error);
        }

        skipPart:
        ;
      }

      return ret;
    }

    //Func<int, int, bool> only introduced in .NET 3.5
    private delegate bool MatchFunc(int proc, int path);

    private static void BuildProcFromJob(
      Job job,
      MazakPart mazak,
      out string ErrorDuringCreate,
      MazakConfig mazakCfg,
      MazakAllData mazakData,
      Func<string, long?, ProgramRevision> lookupProgram,
      int? onlyProcess = null
    )
    {
      ErrorDuringCreate = null;

      var processes = onlyProcess.HasValue
        ? new[] { onlyProcess.Value }
        : Enumerable.Range(1, job.Processes.Count);

      foreach (var proc in processes)
      {
        // checked above that only a single path
        var info = job.Processes[proc - 1].Paths[0];

        //Check this proc and path has a program
        bool has1Stop = false;
        ProgramRevision prog = null;
        foreach (var stop in info.Stops)
        {
          has1Stop = true;
          if (stop.Stations.Count == 0)
          {
            ErrorDuringCreate = "Part " + job.PartName + " has no stations assigned.";
            return;
          }

          if (mazakCfg.MachineNumbers != null)
          {
            foreach (var stat in stop.Stations)
            {
              if (!mazakCfg.MachineNumbers.Contains(stat))
              {
                ErrorDuringCreate =
                  "Part " + job.PartName + " has an invalid machine number " + stat.ToString();
                return;
              }
            }
          }

          if (stop.Program == null || stop.Program == "")
          {
            ErrorDuringCreate = "Part " + job.PartName + " has no programs.";
            return;
          }
          if (mazakCfg.DBType == MazakDbType.MazakVersionE)
          {
            int progNum;
            if (!int.TryParse(stop.Program, out progNum))
            {
              ErrorDuringCreate =
                "Part " + job.PartName + " program " + stop.Program + " is not an integer.";
              return;
            }
          }
          if (mazakData.MainPrograms.Any(mp => mp.MainProgram == stop.Program))
          {
            prog = new ProgramRevision()
            {
              ProgramName = "",
              Revision = 0,
              CellControllerProgramName = stop.Program,
            };
          }
          else
          {
            prog = lookupProgram(stop.Program, stop.ProgramRevision);
            if (prog == null)
            {
              ErrorDuringCreate =
                "Part "
                + job.PartName
                + " program "
                + stop.Program
                + (
                  stop.ProgramRevision.HasValue
                    ? " rev" + stop.ProgramRevision.Value.ToString()
                    : ""
                )
                + " does not exist in the cell controller.";
              return;
            }
            // check if program already exists
            foreach (var mp in mazakData.MainPrograms)
            {
              if (
                MazakProcess.TryParseMainProgramComment(
                  mp.Comment,
                  out string mpProg,
                  out long mpRev
                )
              )
              {
                if (mpProg == prog.ProgramName && mpRev == prog.Revision)
                {
                  prog = prog with { CellControllerProgramName = mp.MainProgram };
                  break;
                }
              }
            }
          }
        }

        if (!has1Stop)
        {
          ErrorDuringCreate = "Part " + job.PartName + " has no machines assigned";
          return;
        }

        var mazakProc = onlyProcess.HasValue ? 1 : proc;
        if (mazakCfg.ProcessFromJob != null)
        {
          mazak.Processes.Add(mazakCfg.ProcessFromJob(mazak, mazakProc, 1, prog));
        }
        else
        {
          mazak.Processes.Add(new MazakProcessFromJob(mazak, mazakProc, 1, prog, jobProcess: proc));
        }
      }
    }

    private static ISet<string> CalculateUsedPrograms(
      MazakAllData oldData,
      IEnumerable<MazakPart> newParts
    )
    {
      var progsToComment = oldData
        .MainPrograms.Where(p => MazakProcess.IsInsightMainProgram(p.Comment))
        .ToDictionary(p => p.MainProgram, p => p.Comment);

      var used = new HashSet<string>();
      foreach (var part in oldData.Parts)
      {
        foreach (var proc in part.Processes)
        {
          if (
            !string.IsNullOrEmpty(proc.MainProgram) && progsToComment.ContainsKey(proc.MainProgram)
          )
          {
            used.Add(progsToComment[proc.MainProgram]);
          }
        }
      }
      foreach (var part in newParts)
      {
        foreach (var proc in part.Processes)
        {
          if (!string.IsNullOrEmpty(proc.PartProgram.ProgramName))
          {
            used.Add(
              MazakProcess.CreateMainProgramComment(
                proc.PartProgram.ProgramName,
                proc.PartProgram.Revision
              )
            );
          }
        }
      }

      return used;
    }
    #endregion

    #region Fixtures

    //group together the processes that use the same fixture
    private static List<MazakFixture> GroupProcessesIntoFixtures(
      IEnumerable<MazakPart> allParts,
      HashSet<int> usedFixGroups,
      bool useStartingOffsetForDueDate,
      IList<string> logMessages
    )
    {
      var fixtures = new List<MazakFixture>();

      var sortedProcs = allParts
        .SelectMany(p => p.Processes)
        .GroupBy(p => p.Job.UniqueStr)
        .OrderBy(paths =>
        {
          var minProc = paths.FirstOrDefault(p => p.ProcessNumber == 1);
          if (minProc != null)
          {
            var start = minProc.PathInfo.SimulatedStartingUTC;
            return start == DateTime.MinValue ? DateTime.MaxValue : start;
          }
          else
          {
            return DateTime.MaxValue;
          }
        })
        .SelectMany(paths => paths);

      foreach (var proc in sortedProcs)
      {
        var (jobFixtureName, faceN) = proc.FixtureFace();
        var pallets = new HashSet<int>(proc.Pallets());
        var face = !faceN.HasValue || faceN.Value <= 0 ? proc.ProcessNumber : faceN.Value;
        if (jobFixtureName == "")
          jobFixtureName = null;

        // search for previous fixture to reuse
        int? group = null;
        MazakFixture oldFixture = null;
        foreach (var fixture in fixtures.ReverseEnumerator())
        {
          if (useStartingOffsetForDueDate)
          {
            // Mazak uses a change in group number as the primary determinant of what to run next.
            // If a mazak schedule is currently running with group number X on a pallet, the next thing to
            // run on that pallet will first check everything with group number X.  (This overrides the
            // due date and priority.)  Thus, if we are using the due date and priority, we can't share
            // fixtures/groups unless there is a continuous range of identical pallet sets or disjoint
            // pallet sets.

            if (pallets.Any(fixture.Pallets.Contains))
            {
              // potentially shared pallets, break unless exactly equal
              if (!fixture.Pallets.SetEquals(pallets))
              {
                break;
              }
            }
            else
            {
              // disjoint pallet sets, continue to next fixture
              continue;
            }
          }
          else
          {
            // everything has identical due date and priority so just compare pallet sets for equality.
            if (!fixture.Pallets.SetEquals(pallets))
              continue;
          }

          if (fixture.JobFixtureName != jobFixtureName)
            continue;

          group = fixture.FixtureGroup;
          if (fixture.Face == face.ToString())
          {
            oldFixture = fixture;
            break;
          }
        }

        if (oldFixture != null)
        {
          oldFixture.Processes.Add(proc);
        }
        else
        {
          if (!group.HasValue)
          {
            group = Enumerable.Range(1, 998).First(i => !usedFixGroups.Contains(i));
            usedFixGroups.Add(group.Value);
          }
          var fix = new MazakFixture()
          {
            FixtureGroup = group.Value,
            JobFixtureName = jobFixtureName,
            Face = face.ToString(),
            Processes = new List<MazakProcess> { proc },
            Pallets = pallets,
          };
          fixtures.Add(fix);
        }
      }

      return fixtures;
    }

    private static ISet<string> AssignMazakFixtures(
      IEnumerable<MazakFixture> allFixtures,
      int downloadUID,
      MazakAllData mazakData,
      ISet<string> savedParts,
      bool useStartingOffsetForDueDate,
      IList<string> log
    )
    {
      //First calculate the available fixtures
      var usedMazakFixtureNames = new HashSet<string>();
      foreach (var partProc in mazakData.Parts.SelectMany(p => p.Processes))
      {
        if (partProc.PartName.IndexOf(':') >= 0)
        {
          if (savedParts.Contains(partProc.PartName))
          {
            usedMazakFixtureNames.Add(partProc.Fixture);
          }
        }
      }

      //For each fixture, create (or reuse) a mazak fixture and add parts and pallets using this fixture.
      foreach (var fixture in allFixtures)
      {
        //check if we can reuse an existing fixture
        if (!useStartingOffsetForDueDate)
        {
          CheckExistingFixture(fixture, usedMazakFixtureNames, mazakData);
        }

        if (string.IsNullOrEmpty(fixture.MazakFixtureName))
        {
          //create a new fixture
          var mazakFixtureName =
            "F:"
            + downloadUID.ToString()
            + ":"
            + fixture.FixtureGroup.ToString()
            + ":"
            + (string.IsNullOrEmpty(fixture.JobFixtureName) ? "" : fixture.JobFixtureName + ":")
            + fixture.Face;
          if (mazakFixtureName.Length > 20)
          {
            // take out the fixture name
            mazakFixtureName =
              "F:"
              + downloadUID.ToString()
              + ":"
              + fixture.FixtureGroup.ToString()
              + ":"
              + fixture.Face;
          }
          if (mazakFixtureName.Length > 20)
          {
            throw new BadRequestException(
              "Fixture " + mazakFixtureName + " is too long to fit in the Mazak databases"
            );
          }
          fixture.MazakFixtureName = mazakFixtureName;
        }

        usedMazakFixtureNames.Add(fixture.MazakFixtureName);
      }

      return usedMazakFixtureNames;
    }

    private static void CheckExistingFixture(
      MazakFixture fixture,
      ISet<string> availableMazakFixtures,
      MazakAllData mazakData
    )
    {
      //we need a fixture that exactly matches this pallet list and has all the processes.
      //Also, the fixture must be contained in a saved part.

      foreach (string mazakFixtureName in availableMazakFixtures)
      {
        int idx = mazakFixtureName.LastIndexOf(':');
        if (idx < 0)
          continue; //skip, not one of our fixtures

        //try to parse face
        if (mazakFixtureName.Substring(idx + 1) != fixture.Face)
          continue;

        //check pallets match
        var onPallets = new HashSet<int>();
        foreach (var palRow in mazakData.Pallets)
        {
          if (palRow.Fixture == mazakFixtureName)
          {
            onPallets.Add(palRow.PalletNumber);
          }
        }

        if (!onPallets.SetEquals(fixture.Pallets))
          continue;

        fixture.MazakFixtureName = mazakFixtureName;
        return;
      }
    }

    public static IEnumerable<T> ReverseEnumerator<T>(this IList<T> items)
    {
      for (int i = items.Count - 1; i >= 0; i--)
      {
        yield return items[i];
      }
    }
    #endregion
  }
}
