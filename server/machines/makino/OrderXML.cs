using System;
using System.Xml;
using System.Collections.Generic;
using BlackMaple.MachineWatchInterface;

namespace Makino
{
	public class OrderXML
	{
		public static void WriteOrderXML(string filename, IEnumerable<JobPlan> jobs, bool onlyOrders)
		{
			string tempFile = System.IO.Path.GetTempFileName();
			try {

				WriteFile(tempFile, jobs, onlyOrders);
				if (System.IO.File.Exists(filename))
					System.IO.File.Delete(filename);
				System.IO.File.Move(tempFile, filename);

			} finally {
				try {
					if (System.IO.File.Exists(tempFile))
						System.IO.File.Delete(tempFile);
				} catch {
					//Do nothing
				}
			}
		}

		private struct JobAndProc
		{
			public JobPlan job;
			public int proc;
			public JobAndProc(JobPlan j, int p) {
				job = j;
				proc = p;
			}
		}

		private static void WriteFile(string filename, IEnumerable<JobPlan> jobs, bool onlyOrders)
		{
			using (var xml = new XmlTextWriter(filename, System.Text.Encoding.UTF8)) {
				xml.Formatting = Formatting.Indented;
				xml.WriteStartDocument();
				xml.WriteStartElement("MASData");
			
				if (!onlyOrders) {
					xml.WriteStartElement("Parts");
					foreach (var j in jobs)
						WritePart(xml, j);
					xml.WriteEndElement();
			
					var allFixtures = new Dictionary<string, List<JobAndProc>>();			
					foreach (var j in jobs) {
						for (var proc = 1; proc <= j.NumProcesses; proc++) {
							foreach (var pal in j.PlannedPallets(proc, 1)) {
								string fixName;
								int palNum;
								if (int.TryParse(pal, out palNum)) {
									fixName = "P" + palNum.ToString().PadLeft(2, '0');
								} else {
									fixName = "P" + pal;
								}
								fixName += "-" + j.PartName + "-" + proc.ToString();
								if (!allFixtures.ContainsKey(fixName)) {
									var lst = new List<JobAndProc>();
									lst.Add(new JobAndProc (j, proc));
									allFixtures.Add(fixName, lst);
								} else {
									allFixtures [fixName].Add(new JobAndProc (j, proc));
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
		
		private static void WritePart(XmlTextWriter xml, JobPlan j)
		{
			xml.WriteStartElement("Part");
			xml.WriteAttributeString("action", "ADD");
			xml.WriteAttributeString("name", j.UniqueStr);
			xml.WriteAttributeString("revision", "SAIL");
			
			xml.WriteElementString("Comment", j.PartName);
			
			xml.WriteStartElement("Processes");
			
			for (int proc = 1; proc <= j.NumProcesses; proc++) {
				xml.WriteStartElement("Process");
				xml.WriteAttributeString("number", proc.ToString());
				
				xml.WriteElementString("Name", j.UniqueStr + "-" + proc.ToString());
				xml.WriteElementString("Comment", j.UniqueStr);
				
				xml.WriteStartElement("Operations");
				xml.WriteStartElement("Operation");
				xml.WriteAttributeString("number", "1");
				xml.WriteAttributeString("clampQuantity", j.PartsPerPallet(proc, 1).ToString());
				xml.WriteAttributeString("unclampMultiplier", j.PartsPerPallet(proc, 1).ToString());
				xml.WriteEndElement(); //Operation
				xml.WriteEndElement(); //Operations
				
				xml.WriteStartElement("Jobs");
				
				xml.WriteStartElement("Job");
				xml.WriteAttributeString("number", "1");
				xml.WriteAttributeString("type", "WSS");
				xml.WriteElementString("FeasibleDevice", Join(",", j.LoadStations(proc, 1)));
				xml.WriteEndElement(); //Job
				
				int jobNum = 2;
				
				foreach (var stop in j.GetMachiningStop(proc, 1)) {
					xml.WriteStartElement("Job");
					xml.WriteAttributeString("number", jobNum.ToString());
					xml.WriteAttributeString("type", "MCW");
					xml.WriteElementString("FeasibleDevice", Join(",", stop.Stations()));
					xml.WriteElementString("NCProgram", Head(stop.AllPrograms()).Program);
					xml.WriteEndElement(); //Job
					
					jobNum += 1;
				}
				
				xml.WriteStartElement("Job");
				xml.WriteAttributeString("number", jobNum.ToString());
				xml.WriteAttributeString("type", "WSS");
				xml.WriteElementString("FeasibleDevice", Join(",", j.UnloadStations(proc, 1)));
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
			
			foreach (var j in jobs) {
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
		
		private static void WriteOrder(XmlTextWriter xml, JobPlan j, bool onlyOrders)
		{
			string partName = onlyOrders ? j.PartName : j.UniqueStr;
			
			xml.WriteStartElement("Order");
			xml.WriteAttributeString("action", "ADD");
			xml.WriteAttributeString("name", j.UniqueStr);
			
			xml.WriteElementString("Comment", j.PartName);
			xml.WriteElementString("PartName", partName);
			xml.WriteElementString("Revision", "SAIL");
			xml.WriteElementString("Quantity", j.GetPlannedCyclesOnFirstProcess(1).ToString());
			xml.WriteElementString("Priority", j.Priority.ToString());
			xml.WriteElementString("Status", "0");
			
			xml.WriteEndElement(); // Order
		}
		
		private static void WriteOrderQty(XmlTextWriter xml, JobPlan j)
		{
			xml.WriteStartElement("OrderQuantity");
			xml.WriteAttributeString("orderName", j.UniqueStr);
			
			int qty = j.GetPlannedCyclesOnFirstProcess(1);
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
			foreach (var x in lst) {
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

