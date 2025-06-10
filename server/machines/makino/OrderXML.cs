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
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Xml;
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Makino
{
  public static class OrderXML
  {
    public static void WriteNewJobs(MakinoSettings makinoCfg, IEnumerable<Job> jobs, IRepository db)
    {
      WriteXML(makinoCfg, tempFile => WriteJobsXML(tempFile, jobs, makinoCfg, db));
    }

    public static void WriteDecrement(MakinoSettings makinoCfg, IEnumerable<RemainingToRun> decrs)
    {
      WriteXML(makinoCfg, tempFile => WriteDecrementXML(tempFile, decrs));
    }

    private static void WriteXML(MakinoSettings makinoCfg, Action<string> writeTempFile)
    {
      var fileName = "insight" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".xml";
      string tempFile = Path.GetTempFileName();
      try
      {
        writeTempFile(tempFile);

        if (Serilog.Log.IsEnabled(Serilog.Events.LogEventLevel.Debug))
        {
          Serilog.Log.Debug(
            "Copying temp {tempFile} to {adePath} as {fileName}: {content}",
            tempFile,
            makinoCfg.ADEPath,
            fileName,
            File.ReadAllText(tempFile, System.Text.Encoding.UTF8)
          );
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
          var accControl = new System.Security.AccessControl.FileSecurity();
          accControl.AddAccessRule(
            new System.Security.AccessControl.FileSystemAccessRule(
              identity: new System.Security.Principal.SecurityIdentifier(
                System.Security.Principal.WellKnownSidType.WorldSid,
                null
              ),
              fileSystemRights: System.Security.AccessControl.FileSystemRights.FullControl,
              type: System.Security.AccessControl.AccessControlType.Allow
            )
          );
          var finfo = new FileInfo(tempFile);
          finfo.SetAccessControl(accControl);
        }

        // now move the file to the ADE path and monitor for it being deleted

        var tcs = new TaskCompletionSource<bool>();
        using var fw = new FileSystemWatcher(makinoCfg.ADEPath, fileName);
        fw.Deleted += (s, e) =>
        {
          if (e.Name == fileName)
          {
            Serilog.Log.Debug("Makino ADE processed new jobs: {fileName}", fileName);
            tcs.SetResult(true);
          }
        };
        fw.EnableRaisingEvents = true;

        File.Move(tempFile, Path.Combine(makinoCfg.ADEPath, fileName));

        if (Task.WaitAny([tcs.Task, Task.Delay(TimeSpan.FromSeconds(10))]) == 1)
        {
          Serilog.Log.Error("Makino did not process new jobs, perhaps the Makino software is not running?");
          throw new Exception("Unable to copy orders to Makino: check that the Makino software is running");
        }
      }
      finally
      {
        File.Delete(Path.Combine(makinoCfg.ADEPath, fileName));
        File.Delete(tempFile);
      }
    }

    private record JobAndProc
    {
      public required Job Job { get; init; }
      public required int Proc { get; init; }
    }

    public static void WriteJobsXML(
      string filename,
      IEnumerable<Job> jobs,
      MakinoSettings settings,
      IRepository db
    )
    {
      using var xml = new XmlTextWriter(filename, System.Text.Encoding.UTF8);
      xml.Formatting = Formatting.Indented;
      xml.WriteStartDocument();
      xml.WriteStartElement("MASData");

      if (!settings.DownloadOnlyOrders)
      {
        xml.WriteStartElement("Parts");
        foreach (var j in jobs)
          WritePart(xml, j);
        xml.WriteEndElement();

        var allFixtures = new Dictionary<string, List<JobAndProc>>();
        foreach (var j in jobs)
        {
          for (var proc = 1; proc <= j.Processes.Count; proc++)
          {
            foreach (var palNum in j.Processes[proc - 1].Paths[0].PalletNums)
            {
              string fixName;
              if (!string.IsNullOrEmpty(j.Processes[proc - 1].Paths[0].Fixture))
              {
                fixName = j.Processes[proc - 1].Paths[0].Fixture!;
              }
              else
              {
                fixName = "P" + palNum.ToString().PadLeft(2, '0');
                fixName += "-" + j.PartName + "-" + proc.ToString();
              }
              if (allFixtures.TryGetValue(fixName, out var value))
              {
                value.Add(new JobAndProc() { Job = j, Proc = proc });
              }
              else
              {
                var lst = new List<JobAndProc>
                {
                  new() { Job = j, Proc = proc },
                };
                allFixtures.Add(fixName, lst);
              }
            }
          }
        }

        xml.WriteStartElement("CommonFixtures");
        foreach (var fix in allFixtures)
          WriteFixture(xml, fix.Key, fix.Value);
        xml.WriteEndElement();
      }

      xml.WriteStartElement("Orders");
      foreach (var j in jobs)
        WriteOrder(xml, j, settings, db);
      xml.WriteEndElement();

      xml.WriteStartElement("OrderQuantities");
      foreach (var j in jobs)
        WriteOrderQty(xml, j);
      xml.WriteEndElement();

      xml.WriteEndElement(); //MASData
      xml.WriteEndDocument();
    }

    private static void WritePart(XmlTextWriter xml, Job j)
    {
      xml.WriteStartElement("Part");
      xml.WriteAttributeString("action", "ADD");
      xml.WriteAttributeString("name", j.UniqueStr);
      xml.WriteAttributeString("revision", "Insight");

      xml.WriteElementString("Comment", j.PartName);

      xml.WriteStartElement("Processes");

      for (int proc = 1; proc <= j.Processes.Count; proc++)
      {
        var pathInfo = j.Processes[proc - 1].Paths[0];
        xml.WriteStartElement("Process");
        xml.WriteAttributeString("number", proc.ToString());

        xml.WriteElementString("Name", j.UniqueStr + "-" + proc.ToString());
        xml.WriteElementString("Comment", j.UniqueStr);

        xml.WriteStartElement("Operations");
        xml.WriteStartElement("Operation");
        xml.WriteAttributeString("number", "1");
        xml.WriteAttributeString("clampQuantity", pathInfo.PartsPerPallet.ToString());
        xml.WriteAttributeString("unclampMultiplier", "1");
        xml.WriteEndElement(); //Operation
        xml.WriteEndElement(); //Operations

        xml.WriteStartElement("Jobs");

        xml.WriteStartElement("Job");
        xml.WriteAttributeString("number", "1");
        xml.WriteAttributeString("type", "WSS");
        xml.WriteElementString("FeasibleDevice", Join(",", pathInfo.Load));
        xml.WriteEndElement(); //Job

        int jobNum = 2;

        foreach (var stop in pathInfo.Stops)
        {
          xml.WriteStartElement("Job");
          xml.WriteAttributeString("number", jobNum.ToString());
          xml.WriteAttributeString("type", "MCW");
          xml.WriteElementString("FeasibleDevice", Join(",", stop.Stations));
          xml.WriteElementString("NCProgram", stop.Program);
          xml.WriteEndElement(); //Job

          jobNum += 1;
        }

        xml.WriteStartElement("Job");
        xml.WriteAttributeString("number", jobNum.ToString());
        xml.WriteAttributeString("type", "WSS");
        xml.WriteElementString("FeasibleDevice", Join(",", pathInfo.Unload));
        xml.WriteEndElement(); //Job

        xml.WriteEndElement(); //Jobs
        xml.WriteEndElement(); //Process
      }

      xml.WriteEndElement(); //Processes
      xml.WriteEndElement(); //Part
    }

    private static void WriteFixture(XmlTextWriter xml, string fix, IEnumerable<JobAndProc> jobs)
    {
      xml.WriteStartElement("CommonFixture");
      xml.WriteAttributeString("action", "UPDATE");
      xml.WriteAttributeString("name", fix);

      xml.WriteStartElement("Processes");

      foreach (var j in jobs)
      {
        xml.WriteStartElement("Process");
        xml.WriteAttributeString("action", "ADD");
        xml.WriteAttributeString("partName", j.Job.UniqueStr);
        xml.WriteAttributeString("revision", "Insight");
        xml.WriteAttributeString("processNumber", j.Proc.ToString());
        xml.WriteEndElement(); //Process
      }

      xml.WriteEndElement(); //Processes
      xml.WriteEndElement(); //CommonFixture
    }

    public record OrderDetails
    {
      public required string Comment { get; init; }
      public required string Revision { get; init; }
      public required int Priority { get; init; }
    }

    private static void WriteOrder(XmlTextWriter xml, Job j, MakinoSettings settings, IRepository db)
    {
      string partName;
      if (settings.DownloadOnlyOrders)
      {
        partName =
          j.Processes.SelectMany(p => p.Paths)
            .SelectMany(p => p.Stops)
            .Select(s => s.Program)
            .FirstOrDefault(prog => !string.IsNullOrEmpty(prog)) ?? j.PartName;
      }
      else
      {
        partName = j.UniqueStr;
      }

      var details = settings.CustomOrderDetails?.Invoke(j, db);

      xml.WriteStartElement("Order");
      xml.WriteAttributeString("action", "ADD");
      xml.WriteAttributeString("name", j.UniqueStr);

      xml.WriteElementString("Comment", details?.Comment ?? j.PartName);
      xml.WriteElementString("PartName", partName);
      xml.WriteElementString("Revision", details?.Revision ?? "Insight");
      xml.WriteElementString("Quantity", j.Cycles.ToString());
      xml.WriteElementString("Priority", details?.Priority.ToString() ?? "10");
      xml.WriteElementString("Status", "0");

      xml.WriteEndElement(); // Order
    }

    private static void WriteOrderQty(XmlTextWriter xml, Job j)
    {
      xml.WriteStartElement("OrderQuantity");
      xml.WriteAttributeString("orderName", j.UniqueStr);

      int qty = j.Cycles;
      //qty /= j.PartsPerPallet(1, 1);

      xml.WriteElementString("ProcessNumber", "1");
      xml.WriteElementString("RemainQuantity", qty.ToString());

      xml.WriteEndElement(); // OrderQuantity
    }

    public static void WriteDecrementXML(string tempFile, IEnumerable<RemainingToRun> decrs)
    {
      using var xml = new XmlTextWriter(tempFile, System.Text.Encoding.UTF8);
      xml.Formatting = Formatting.Indented;
      xml.WriteStartDocument();
      xml.WriteStartElement("MASData");
      xml.WriteStartElement("OrderQuantities");

      foreach (var decr in decrs)
      {
        xml.WriteStartElement("OrderQuantity");
        xml.WriteAttributeString("orderName", decr.JobUnique);
        xml.WriteElementString("ProcessNumber", "1");
        xml.WriteElementString("RemainQuantity", (-decr.RemainingQuantity).ToString()); // +/- value to affect quantity
        xml.WriteEndElement(); // OrderQuantity
      }

      xml.WriteEndElement(); // OrderQuantities
      xml.WriteEndElement(); // MASData
      xml.WriteEndDocument();
    }

    private static string Join<T>(string sep, IEnumerable<T> lst)
    {
      var builder = new System.Text.StringBuilder();
      bool first = true;
      foreach (var x in lst)
      {
        if (x == null)
          continue;
        if (first)
          first = false;
        else
          builder.Append(sep);
        builder.Append(x.ToString());
      }
      return builder.ToString();
    }
  }
}
