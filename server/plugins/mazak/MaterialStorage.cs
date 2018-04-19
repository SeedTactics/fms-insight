using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using BlackMaple.MachineWatchInterface;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface
{

	public class MaterialStorage : IBackgroundWorker
	{
		
		private DatabaseAccess database;
		private LoadOperations loadOperations;
		
		private string _outputPath;
        private bool _onlyPullOrders;

		public void Init(IServerBackend back, string dataDir, IConfig cfg, SerialSettings serSettings)
		{
			_outputPath = cfg.GetValue<string>("Mazak", "Material Storage Path");

			if (string.IsNullOrEmpty(_outputPath))
				return;
			
			database = ((Backend) back).Database;
			loadOperations = ((Backend) back).LoadOperations;

            var pullOnly = cfg.GetValue<string>("Mazak", "Pull Only Storage Commands");
            _onlyPullOrders = false;
            if (pullOnly != null && Convert.ToBoolean(pullOnly))
                _onlyPullOrders = true;
			
			loadOperations.LoadActions += HandleLoadOperationsLoadActions;
		}

		public void Halt()
		{
			if (loadOperations != null)
				loadOperations.LoadActions -= HandleLoadOperationsLoadActions;
		}

		private static System.Diagnostics.TraceSource trace = new System.Diagnostics.TraceSource("Material Storage", SourceLevels.All);
		public System.Diagnostics.TraceSource TraceSource {
			get { return trace; }
		}
			
		private void HandleLoadOperationsLoadActions(int loadStation, IEnumerable<LoadAction> actions)
		{
			try {
				var numProc = LoadNumProc();
				LoadAction loadA = null;
				LoadAction unloadA = null;
			
				foreach (var a in actions) {
					if (a.LoadEvent && a.Process == 1) {
						loadA = a;
					} else if (!a.LoadEvent && numProc.ContainsKey(a.Part) && a.Process == numProc[a.Part]) {
						unloadA = a;
					} else if (!a.LoadEvent && !numProc.ContainsKey(a.Part) && a.Process == 1) {
						trace.TraceEvent(TraceEventType.Error, 0,
						                 "Unable to determine the number of processes for part " + a.Part + "." +
						                 " Defaulting to 1");
						unloadA = a;
					}
				}
			
				string outputFile = System.IO.Path.Combine(_outputPath,
				                                           DateTime.Now.ToString("yyMMddHHmmss") + ".csv");
			
				WriteFile(outputFile, loadA, unloadA);
				
			} catch (Exception ex) {
				trace.TraceEvent(TraceEventType.Error, 0,
				                 "Unhandled error when processing load events: " + ex.ToString());
			}
		}

		private IDictionary<string, int> LoadNumProc()
		{
			Dictionary<string, int> ret = new Dictionary<string, int>();

			ReadOnlyDataSet currentSet = null;

			if (!database.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(2), false)) {
				throw new Exception("Unable to obtain mazak database lock");
			}
			try {
				currentSet = database.LoadReadOnly();
			} finally {
				database.MazakTransactionLock.ReleaseMutex();
			}

			foreach (ReadOnlyDataSet.PartRow part in currentSet.Part.Rows) {
				ret[part.PartName] = part.GetPartProcessRows().Length;
			}

			return ret;
		}

		private void WriteFile(string output, LoadAction load, LoadAction unload)
		{
			System.IO.FileStream f = System.IO.File.Open(output, System.IO.FileMode.Create,
			                                             System.IO.FileAccess.Write, System.IO.FileShare.None);
			try {
				System.IO.StreamWriter writer = new System.IO.StreamWriter(f);
				try {
					if (load == null) {
						writer.WriteLine(",,0,");
					} else {
						writer.Write(load.Part);
						writer.Write("/BLAN");
						writer.Write(",");
						writer.Write(load.Part);
						writer.Write("/BLAN");
						writer.Write(",");
						writer.Write(-load.Qty);
						writer.WriteLine(",");
					}

                    if (!_onlyPullOrders) {
						if (unload == null) {
							writer.WriteLine(",,0,");
						} else {
							writer.Write(unload.Part);
							writer.Write(",");
							writer.Write(unload.Part);
							writer.Write(",");
							writer.Write(unload.Qty);
							writer.WriteLine(",");
						}
                    }
				} finally {
					writer.Close();
				}
			} finally {
				f.Close();
			}
		}
	}
}
