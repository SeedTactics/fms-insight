using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Tests;

public sealed class CSVExportSpec
{
  private const string Header =
    "Counter,EndTimeUTC,LogType,LocationName,LocationNum,StartOfCycle,Pallet,Program,Result,ElapsedTime,ActiveOperationTime,MaterialID,JobUniqueStr,PartName,Process,NumProcesses,Face,ProgramDetails\r\n";

  [Test]
  public async Task EmptyExportWritesTheHeaderAndLeavesTheWriterOpen()
  {
    using var writer = new StringWriter { NewLine = "\r\n" };
    CSVLogConverter.WriteCSV(writer, ImmutableList<LogEntry>.Empty);
    await Assert.That(writer.ToString()).IsEqualTo(Header);
    writer.Write("still open");
    await Assert.That(writer.ToString()).IsEqualTo(Header + "still open");
  }

  [Test]
  [Arguments("en-US")]
  [Arguments("fr-FR")]
  public async Task ExportPreservesColumnOrderValuesAndMaterialExpansion(string culture)
  {
    var material = new LogMaterial
    {
      MaterialID = 23,
      JobUniqueStr = "job",
      PartName = "Widget, \"quoted\"",
      Process = 2,
      NumProcesses = 3,
      Face = 4,
    };
    var entry = new LogEntry(
      123,
      [material, material with { MaterialID = 24, PartName = "other", Process = 1, Face = 5 }],
      5,
      LogType.MachineCycle,
      "MC",
      6,
      "P123",
      false,
      new DateTime(2026, 10, 28, 12, 34, 56, DateTimeKind.Utc),
      "ok\nnext",
      TimeSpan.FromSeconds(70.5),
      TimeSpan.FromSeconds(60.25)
    )
    {
      ProgramDetails = ImmutableDictionary<string, string>.Empty.Add("tool", "drill, large"),
    };
    using var writer = new StringWriter { NewLine = "\r\n" };
    var previousCulture = CultureInfo.CurrentCulture;
    try
    {
      CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
      CSVLogConverter.WriteCSV(
        writer,
        [
          entry,
          entry with
          {
            Counter = 124,
            Material = ImmutableList<LogMaterial>.Empty,
            StartOfCycle = true,
            Program = "",
            Result = "",
            ProgramDetails = null,
          },
        ]
      );
    }
    finally
    {
      CultureInfo.CurrentCulture = previousCulture;
    }

    await Assert
      .That(writer.ToString())
      .IsEqualTo(
        Header
          + "123,2026-10-28T12:34:56Z,MachineCycle,MC,6,False,5,P123,\"ok\nnext\",00:01:10.5000000,00:01:00.2500000,23,job,\"Widget, \"\"quoted\"\"\",2,3,4,\"tool=drill, large\"\r\n"
          + "123,2026-10-28T12:34:56Z,MachineCycle,MC,6,False,5,P123,\"ok\nnext\",00:01:10.5000000,00:01:00.2500000,24,job,other,1,3,5,\"tool=drill, large\"\r\n"
          + "124,2026-10-28T12:34:56Z,MachineCycle,MC,6,True,5,,,00:01:10.5000000,00:01:00.2500000,-1,,,0,0,,\r\n"
      );
  }
}
