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
using System.Linq;
using System.IO;
using BlackMaple.MachineWatchInterface;

namespace BlackMaple.MachineFramework
{
  public class CSVLogEntry
  {
    public long Counter { get; set; }
    public string EndTimeUTC { get; set; }
    public LogType LogType { get; set; }
    public string LocationName {get; set;}
    public int LocationNum {get; set;}

    public bool StartOfCycle { get; set; }
    public string Pallet { get; set; }
    public string Program { get; set; }
    public string Result { get; set; }

    public TimeSpan ElapsedTime { get; set; }
    public TimeSpan ActiveOperationTime { get; set; }

    public long MaterialID { get; set; }
    public string JobUniqueStr { get; set; }
    public string PartName { get; set; }
    public int Process { get; set; }
    public int NumProcesses { get; set; }
    public string Face { get; set; }

    public string ProgramDetails {get; set;}
  }

  public static class CSVLogConverter
  {
    private static string BuildProgramDetails(LogEntry e)
    {
      if (e.ProgramDetails == null) {
        return "";
      } else {
        return string.Join(";",
          e.ProgramDetails.Select(k => k.Key + "=" + k.Value)
        );
      }
    }

    public static IEnumerable<CSVLogEntry> ConvertLogToCSV(LogEntry e)
    {
      if (e.Material.Any()) {
        return e.Material.Select(mat => new CSVLogEntry() {
          Counter = e.Counter,
          EndTimeUTC = e.EndTimeUTC.ToString("yyyy-MM-ddTHH:mm:ssZ"),
          LogType = e.LogType,
          LocationName = e.LocationName,
          LocationNum = e.LocationNum,

          StartOfCycle = e.StartOfCycle,
          Pallet = e.Pallet,
          Program = e.Program,
          Result = e.Result,

          ElapsedTime = e.ElapsedTime,
          ActiveOperationTime = e.ActiveOperationTime,

          MaterialID = mat.MaterialID,
          JobUniqueStr = mat.JobUniqueStr,
          PartName = mat.PartName,
          Process = mat.Process,
          NumProcesses = mat.NumProcesses,
          Face = mat.Face,

          ProgramDetails = BuildProgramDetails(e),
        });
      } else {
        return new[] {
          new CSVLogEntry() {
            Counter = e.Counter,
            EndTimeUTC = e.EndTimeUTC.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            LogType = e.LogType,
            LocationName = e.LocationName,
            LocationNum = e.LocationNum,

            StartOfCycle = e.StartOfCycle,
            Pallet = e.Pallet,
            Program = e.Program,
            Result = e.Result,

            ElapsedTime = e.ElapsedTime,
            ActiveOperationTime = e.ActiveOperationTime,

            MaterialID = -1,
            JobUniqueStr = "",
            PartName = "",
            Process = 0,
            NumProcesses = 0,
            Face = "",

            ProgramDetails = BuildProgramDetails(e),
          }
        };
      }
    }

    public static void WriteCSV(TextWriter writer, IEnumerable<LogEntry> es)
    {
      var csv = new CsvHelper.CsvWriter(writer, System.Globalization.CultureInfo.InvariantCulture);
      csv.WriteRecords(es.SelectMany(ConvertLogToCSV));
    }
  }

  public static class CSVWorkorderConverter
  {
    public static void WriteCSV(TextWriter writer, IEnumerable<WorkorderSummary> ws)
    {
      var csv = new CsvHelper.CsvWriter(writer, System.Globalization.CultureInfo.InvariantCulture);
      csv.WriteField("ID");
      csv.WriteField("CompletedTimeUTC");
      csv.WriteField("Part");
      csv.WriteField("Quantity");
      csv.WriteField("Serials");

      var activeStations = new HashSet<string>();
      var elapsedStations = new HashSet<string>();
      foreach (var p in ws.SelectMany(w => w.Parts))
      {
          foreach (var k in p.ActiveStationTime.Keys)
              activeStations.Add(k);
          foreach (var k in p.ElapsedStationTime.Keys)
              elapsedStations.Add(k);
      }

      var actualKeys = activeStations.OrderBy(x => x).ToList();
      foreach (var k in actualKeys)
      {
          csv.WriteField("Active " + k + " (minutes)");
      }
      var plannedKeys = elapsedStations.OrderBy(x => x).ToList();
      foreach (var k in plannedKeys)
      {
          csv.WriteField("Elapsed " + k + " (minutes)");
      }
      csv.NextRecord();

      foreach (var w in ws)
      {
        foreach (var p in w.Parts)
        {
          csv.WriteField(w.WorkorderId);
          if (w.FinalizedTimeUTC.HasValue)
            csv.WriteField(w.FinalizedTimeUTC.Value.ToString("yyyy-MM-ddTHH:mm:ssZ"));
          else
            csv.WriteField("");
          csv.WriteField(p.Part);
          csv.WriteField(p.PartsCompleted);
          csv.WriteField(string.Join(";", w.Serials));

          foreach (var k in actualKeys)
          {
              if (p.ActiveStationTime.ContainsKey(k))
                  csv.WriteField(p.ActiveStationTime[k].TotalMinutes);
              else
                  csv.WriteField(0);
          }
          foreach (var k in plannedKeys)
          {
              if (p.ElapsedStationTime.ContainsKey(k))
                  csv.WriteField(p.ElapsedStationTime[k].TotalMinutes);
              else
                  csv.WriteField(0);
          }
          csv.NextRecord();
        }
      }
    }
  }
}