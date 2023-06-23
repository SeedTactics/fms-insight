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
using System.Xml;
using System.Collections.Generic;
using BlackMaple.MachineFramework;
using System.IO;

namespace Makino
{
  public class OrderXML
  {
    public static void WriteOrderXML(string filename, IEnumerable<Job> jobs, bool onlyOrders)
    {
      string tempFile = Path.GetTempFileName();
      try
      {
        WriteFile(tempFile, jobs, onlyOrders);

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

        if (File.Exists(filename))
          File.Delete(filename);
        File.Move(tempFile, filename);
      }
      finally
      {
        try
        {
          if (File.Exists(tempFile))
            File.Delete(tempFile);
        }
        catch
        {
          //Do nothing
        }
      }
    }

    private struct JobAndProc
    {
      public Job job;
      public int proc;

      public JobAndProc(Job j, int p)
      {
        job = j;
        proc = p;
      }
    }

    private static void WriteFile(string filename, IEnumerable<Job> jobs, bool onlyOrders)
    {
      using (var xml = new XmlTextWriter(filename, System.Text.Encoding.UTF8))
      {
        xml.Formatting = Formatting.Indented;
        xml.WriteStartDocument();
        xml.WriteStartElement("MASData");

        if (!onlyOrders)
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
                fixName = "P" + palNum.ToString().PadLeft(2, '0');
                fixName += "-" + j.PartName + "-" + proc.ToString();
                if (!allFixtures.ContainsKey(fixName))
                {
                  var lst = new List<JobAndProc>();
                  lst.Add(new JobAndProc(j, proc));
                  allFixtures.Add(fixName, lst);
                }
                else
                {
                  allFixtures[fixName].Add(new JobAndProc(j, proc));
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
          WriteOrder(xml, j, onlyOrders);
        xml.WriteEndElement();

        xml.WriteStartElement("OrderQuantities");
        foreach (var j in jobs)
          WriteOrderQty(xml, j);
        xml.WriteEndElement();

        xml.WriteEndElement(); //MASData
        xml.WriteEndDocument();
      }
    }

    private static void WritePart(XmlTextWriter xml, Job j)
    {
      xml.WriteStartElement("Part");
      xml.WriteAttributeString("action", "ADD");
      xml.WriteAttributeString("name", j.UniqueStr);
      xml.WriteAttributeString("revision", "SAIL");

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
        xml.WriteAttributeString("unclampMultiplier", pathInfo.PartsPerPallet.ToString());
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
        xml.WriteAttributeString("partName", j.job.UniqueStr);
        xml.WriteAttributeString("revision", "SAIL");
        xml.WriteAttributeString("processNumber", j.proc.ToString());
        xml.WriteEndElement(); //Process
      }

      xml.WriteEndElement(); //Processes
      xml.WriteEndElement(); //CommonFixture
    }

    private static void WriteOrder(XmlTextWriter xml, Job j, bool onlyOrders)
    {
      string partName = onlyOrders ? j.PartName : j.UniqueStr;

      xml.WriteStartElement("Order");
      xml.WriteAttributeString("action", "ADD");
      xml.WriteAttributeString("name", j.UniqueStr);

      xml.WriteElementString("Comment", j.PartName);
      xml.WriteElementString("PartName", partName);
      xml.WriteElementString("Revision", "SAIL");
      xml.WriteElementString("Quantity", j.Cycles.ToString());
      xml.WriteElementString("Priority", "10");
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

    private static T Head<T>(IEnumerable<T> lst)
    {
      foreach (var x in lst)
        return x;
      return default(T);
    }

    private static string Join<T>(string sep, IEnumerable<T> lst)
    {
      var builder = new System.Text.StringBuilder();
      bool first = true;
      foreach (var x in lst)
      {
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
