/* Copyright (c) 2023, John Lenz

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
using System.Linq;
using nietras.SeparatedValues;

namespace BlackMaple.MachineFramework
{
  public record CSVLogEntry
  {
    public required long Counter { get; init; }
    public required string EndTimeUTC { get; init; }
    public required LogType LogType { get; init; }
    public required string LocationName { get; init; }
    public required int LocationNum { get; init; }

    public required bool StartOfCycle { get; init; }
    public required int Pallet { get; init; }
    public required string Program { get; init; }
    public required string Result { get; init; }

    public required TimeSpan ElapsedTime { get; init; }
    public required TimeSpan ActiveOperationTime { get; init; }

    public required long MaterialID { get; init; }
    public required string JobUniqueStr { get; init; }
    public required string PartName { get; init; }
    public required int Process { get; init; }
    public required int NumProcesses { get; init; }
    public required string Face { get; init; }

    public required string ProgramDetails { get; init; }
  }

  public static class CSVLogConverter
  {
    private static string BuildProgramDetails(LogEntry e)
    {
      if (e.ProgramDetails == null)
      {
        return "";
      }
      else
      {
        return string.Join(";", e.ProgramDetails.Select(k => k.Key + "=" + k.Value));
      }
    }

    public static IEnumerable<CSVLogEntry> ConvertLogToCSV(LogEntry e)
    {
      if (e.Material.Any())
      {
        return e.Material.Select(mat => new CSVLogEntry()
        {
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
          Face = mat.Face.ToString(),
          ProgramDetails = BuildProgramDetails(e),
        });
      }
      else
      {
        return new[]
        {
          new CSVLogEntry()
          {
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
          },
        };
      }
    }

    public static void WriteCSV(TextWriter writer, IEnumerable<LogEntry> es)
    {
      using var csv = Sep.New(',').Writer().Strict().To(writer, leaveOpen: true);
      csv.Header.Add([
        nameof(CSVLogEntry.Counter),
        nameof(CSVLogEntry.EndTimeUTC),
        nameof(CSVLogEntry.LogType),
        nameof(CSVLogEntry.LocationName),
        nameof(CSVLogEntry.LocationNum),
        nameof(CSVLogEntry.StartOfCycle),
        nameof(CSVLogEntry.Pallet),
        nameof(CSVLogEntry.Program),
        nameof(CSVLogEntry.Result),
        nameof(CSVLogEntry.ElapsedTime),
        nameof(CSVLogEntry.ActiveOperationTime),
        nameof(CSVLogEntry.MaterialID),
        nameof(CSVLogEntry.JobUniqueStr),
        nameof(CSVLogEntry.PartName),
        nameof(CSVLogEntry.Process),
        nameof(CSVLogEntry.NumProcesses),
        nameof(CSVLogEntry.Face),
        nameof(CSVLogEntry.ProgramDetails),
      ]);
      csv.Header.Write();

      foreach (var entry in es.SelectMany(ConvertLogToCSV))
      {
        using var row = csv.NewRow();
        row[nameof(entry.Counter)].Format(entry.Counter);
        row[nameof(entry.EndTimeUTC)].Set(entry.EndTimeUTC);
        row[nameof(entry.LogType)].Set(entry.LogType.ToString());
        row[nameof(entry.LocationName)].Set(entry.LocationName);
        row[nameof(entry.LocationNum)].Format(entry.LocationNum);
        row[nameof(entry.StartOfCycle)].Set(entry.StartOfCycle.ToString());
        row[nameof(entry.Pallet)].Format(entry.Pallet);
        row[nameof(entry.Program)].Set(entry.Program);
        row[nameof(entry.Result)].Set(entry.Result);
        row[nameof(entry.ElapsedTime)].Format(entry.ElapsedTime);
        row[nameof(entry.ActiveOperationTime)].Format(entry.ActiveOperationTime);
        row[nameof(entry.MaterialID)].Format(entry.MaterialID);
        row[nameof(entry.JobUniqueStr)].Set(entry.JobUniqueStr);
        row[nameof(entry.PartName)].Set(entry.PartName);
        row[nameof(entry.Process)].Format(entry.Process);
        row[nameof(entry.NumProcesses)].Format(entry.NumProcesses);
        row[nameof(entry.Face)].Set(entry.Face);
        row[nameof(entry.ProgramDetails)].Set(entry.ProgramDetails);
      }
    }
  }
}
