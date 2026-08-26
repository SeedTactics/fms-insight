/* Copyright (c) 2024, John Lenz

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
using System.IO;

namespace MazakMachineInterface
{
  public static class LogCSVParsing
  {
    private static List<string> FindCSVFiles(string path)
    {
      var files = new List<string>(Directory.GetFiles(path, "*.csv"));
      files.Sort();
      return files;
    }

    private static FileStream WaitToOpenFile(string file)
    {
      int cnt = 0;
      while (cnt < 20)
      {
        try
        {
          return File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        catch (UnauthorizedAccessException ex)
        {
          // do nothing
          Serilog.Log.Debug(ex, "Error opening {file}", file);
        }
        catch (IOException ex)
        {
          // do nothing
          Serilog.Log.Debug(ex, "Error opening {file}", file);
        }
        Serilog.Log.Debug("Could not open file {file}, sleeping for 2 seconds", file);
        System.Threading.Thread.Sleep(TimeSpan.FromSeconds(2));
      }
      throw new Exception("Unable to open file " + file);
    }

    public static List<LogEntry> LoadLog(string lastForeignID, string path)
    {
      var files = FindCSVFiles(path);

      var ret = new List<LogEntry>();
      int skippedFiles = 0;
      int scannedFiles = 0;

      foreach (var f in files)
      {
        var filename = Path.GetFileName(f);
        if (filename.CompareTo(lastForeignID) <= 0)
        {
          skippedFiles += 1;
          continue;
        }

        scannedFiles += 1;
        int lineCount = 0;
        int eventCount = 0;
        using (var fstream = WaitToOpenFile(f))
        using (var stream = new StreamReader(fstream))
        {
          while (stream.Peek() >= 0)
          {
            var s = stream.ReadLine().Split(',');
            lineCount += 1;
            if (s.Length < 18)
              continue;

            int code;
            if (!int.TryParse(s[6], out code))
            {
              Serilog.Log.Debug("Unable to parse code from log message {msg}", s);
              continue;
            }
            if (!Enum.IsDefined(typeof(LogCode), code))
            {
              Serilog.Log.Verbose("Unused log message {msg}", s);
              continue;
            }

            string fullPartName = s[10].Trim();
            int idx = fullPartName.IndexOf(':');

            var e = new LogEntry()
            {
              ForeignID = filename,
              TimeUTC = new DateTime(
                int.Parse(s[0]),
                int.Parse(s[1]),
                int.Parse(s[2]),
                int.Parse(s[3]),
                int.Parse(s[4]),
                int.Parse(s[5]),
                DateTimeKind.Local
              ).ToUniversalTime(),
              Code = (LogCode)code,
              Pallet = (int.TryParse(s[13], out var pal)) ? pal : -1,
              FullPartName = fullPartName,
              JobPartName = (idx > 0) ? fullPartName.Substring(0, idx) : fullPartName,
              Process = int.TryParse(s[11], out var proc) ? proc : 0,
              FixedQuantity = int.TryParse(s[12], out var fixQty) ? fixQty : 0,
              Program = s[14],
              StationNumber = int.TryParse(s[8], out var statNum) ? statNum : 0,
              FromPosition = s[16],
              TargetPosition = s[17],
            };

            ret.Add(e);
            eventCount += 1;
            Serilog.Log.Verbose(
              "Parsed Mazak CSV event from {file}: code {code}, time {timeUTC}, pallet {pallet}, station {station}, part {part}, process {process}",
              filename,
              e.Code,
              e.TimeUTC,
              e.Pallet,
              e.StationNumber,
              e.FullPartName,
              e.Process
            );
          }
        }

        Serilog.Log.Verbose(
          "Read Mazak CSV file {file}: {lineCount} rows, {eventCount} recognized events",
          filename,
          lineCount,
          eventCount
        );
      }

      if (ret.Count > 0)
        Serilog.Log.Debug(
          "Scanned {scannedFiles} new Mazak CSV files in {path} after foreign ID {lastForeignID}: parsed {eventCount} events and skipped {skippedFiles} older files",
          scannedFiles,
          path,
          lastForeignID,
          ret.Count,
          skippedFiles
        );

      return ret;
    }

    public static void DeleteLog(string lastForeignID, string path)
    {
      var files = FindCSVFiles(path);
      int deletedFiles = 0;
      foreach (var f in files)
      {
        var filename = Path.GetFileName(f);
        if (filename.CompareTo(lastForeignID) > 0)
          break;

        try
        {
          File.Delete(f);
          deletedFiles += 1;
          Serilog.Log.Verbose("Deleted Mazak CSV log file {file}", filename);
        }
        catch (Exception ex)
        {
          Serilog.Log.Error(ex, "Error deleting file: " + f);
        }
      }

      if (deletedFiles > 0)
        Serilog.Log.Debug(
          "Deleted {deletedFiles} Mazak CSV log files through foreign ID {lastForeignID}",
          deletedFiles,
          lastForeignID
        );
    }
  }
}
