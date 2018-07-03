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
using System.Linq;

namespace Makino
{
	internal class MakinoToJobMap
	{
		/*Maps we have:
		 *
		 * part id => JobPlan
		 *
		 * process id => index/process number
		 *
		 * process id => part id
		 *
		 * jobid => index/job number
		 *
		 * job id => process id
		 *
		 * job id => list of programs
		 *
		 * job id => machining stop
		 *
		 * fixture id => list of process ids
		 *
		 */
		private Dictionary<int, JobPlan> _byPartID = new Dictionary<int, JobPlan>();
		private Dictionary<int, int> _procIDToProcNum = new Dictionary<int, int>();
		private Dictionary<int, int> _procIDToPartID = new Dictionary<int, int>();
		private Dictionary<int, int> _jobIDToNum = new Dictionary<int, int>();
		private Dictionary<int, int> _jobIDToProcID = new Dictionary<int, int>();
		private Dictionary<int, List<string> > _programs = new Dictionary<int, List<string> >();
		private Dictionary<int, JobMachiningStop > _stops = new Dictionary<int, JobMachiningStop>();
		private Dictionary<int, InProcessJob> _byOrderID = new Dictionary<int, InProcessJob>();

        private BlackMaple.MachineFramework.JobLogDB _logDb;

        public MakinoToJobMap(BlackMaple.MachineFramework.JobLogDB log)
        {
            _logDb = log;
        }

        public IEnumerable<InProcessJob> Jobs
		{
			get { return _byOrderID.Values; }
		}

		public void AddProcess(int partID, int processNum, int processID)
		{
			_procIDToProcNum.Add(processID, processNum);
			_procIDToPartID.Add(processID, partID);
		}

		public JobPlan CreateJob(string unique, int partID)
		{
			int numProc = 1;
			foreach (var p in _procIDToPartID) {
				if (p.Value == partID) {
					numProc = Math.Max(numProc, _procIDToProcNum[p.Key]);
				}
			}
			var job = new JobPlan(unique, numProc);

			_byPartID.Add(partID, job);

			return job;
		}

		public void AddJobToProcess(int processID, int jobNumber, int jobID)
		{
			_jobIDToNum.Add(jobID, jobNumber);
			_jobIDToProcID.Add(jobID, processID);
		}

		public void AddProgramToJob(int jobID, string program)
		{
			if (!_programs.ContainsKey(jobID))
				_programs.Add(jobID, new List<string>());
			_programs[jobID].Add(program);
		}

		public void AddAllowedStationToJob(int jobID, PalletLocation loc)
		{
			if (loc.Location == PalletLocationEnum.LoadUnload) {
				var jobNum = _jobIDToNum[jobID];
				var proc = _procIDToProcNum[_jobIDToProcID[jobID]];
				var job = _byPartID[_procIDToPartID[_jobIDToProcID[jobID]]];

				if (jobNum == 1)
					job.AddLoadStation(proc, 1, loc.Num);
				else
					job.AddUnloadStation(proc, 1, loc.Num);

			} else {
				if (!_stops.ContainsKey(jobID)) {
					_stops.Add(jobID, new JobMachiningStop("MC"));
				}

				if (_programs.ContainsKey(jobID)) {
					foreach (var prog in _programs[jobID]) {
						_stops[jobID].AddProgram(loc.Num, prog);
					}
				}
			}
		}

		public void CompleteStations()
		{
			foreach (var proc in _procIDToPartID) {

				var procNum = _procIDToProcNum[proc.Key];
				var job = _byPartID[proc.Value];

				var stops = new SortedList<int, JobMachiningStop>();

				foreach (var jobStop in _stops) {
					if (_jobIDToProcID[jobStop.Key] == proc.Key) { //Filter only the jobs on this processID
						stops.Add(_jobIDToNum[jobStop.Key], jobStop.Value);
					}
				}

				if (stops.Count > 0)
					job.AddMachiningStops(procNum, 1, stops.Values);
			}
		}

		public void AddFixtureToProcess(int processID, int fixtureID, IEnumerable<int> pals)
		{
			var procNum = _procIDToProcNum[processID];
			var job = _byPartID[_procIDToPartID[processID]];

			if (pals == null) return;

			foreach (var pal in pals)
				job.AddProcessOnPallet(procNum, 1, pal.ToString());
		}

		public InProcessJob DuplicateForOrder(int orderID, string order, int partID)
		{
			var job = _byPartID[partID];
			var newJob = new InProcessJob(new JobPlan(job, order));
			_byOrderID.Add(orderID, newJob);
			return newJob;
		}

		public void AddQuantityToProcess(int orderID, int processID, int completed)
		{
			var job = _byOrderID[orderID];
			var procNum = _procIDToProcNum[processID];

            job.SetCompleted(procNum, 1, completed);
		}

		public InProcessMaterial CreateMaterial(int orderID, int processID, int jobID, int palletNum, int face, long matID)
		{
			var job = _byOrderID[orderID];
			var program = "";
			if (_programs.ContainsKey(jobID) && _programs[jobID].Count > 0)
				program = _programs[jobID][0];

			var matDetails = _logDb.GetMaterialDetails(matID);
            return new InProcessMaterial()
            {
                MaterialID = matID,
                JobUnique = job.UniqueStr,
                Process = _procIDToProcNum[processID],
                Path = 1,
                PartName = job.PartName,
                Serial = matDetails?.Serial,
                WorkorderId = matDetails?.Workorder,
                SignaledInspections =
                    _logDb.LookupInspectionDecisions(matID)
                        .Where(x => x.Inspect)
                        .Select(x => x.InspType)
						.Distinct()
                        .ToList(),
                Action = new InProcessMaterialAction()
                {
                    Type = InProcessMaterialAction.ActionType.Waiting,
                    Program = program
                },
                Location = new InProcessMaterialLocation()
                {
                    Type = InProcessMaterialLocation.LocType.OnPallet,
                    Pallet = palletNum.ToString(),
                    Face = face
                }
            };
		}

		public InProcessJob JobForOrder(int orderID)
		{
			if (_byOrderID.ContainsKey(orderID))
				return _byOrderID[orderID];
			else
				return null;
		}

		public int ProcessForJobID(int jobID)
		{
			var procID = _jobIDToProcID[jobID];
			return _procIDToProcNum[procID];
		}
	}

	internal class MakinoToPalletMap
	{
		/* Maps
		 *
		 * fixturePalletId => index/fixture num on the pallet
		 *
		 * fixturePalletID => fixtureID
		 *
		 * fixturePalletId => PalletNum
         *
         * fixturePalletId => list of material
		 *
		 * fixtureID => list of pallets
		 *
		 * pallet => PalletStatus
		 */

		private Dictionary<int, int> _fixPalIDToFixNum = new Dictionary<int, int>();
		private Dictionary<int, int> _fixPalIDToFixID = new Dictionary<int, int>();
		private Dictionary<int, int> _fixPalIDToPalNum = new Dictionary<int, int>();
        private Dictionary<int, List<InProcessMaterial>> _fixPalIDToMaterial = new Dictionary<int, List<InProcessMaterial>>();
		private Dictionary<int, List<int> > _fixIDToPallets = new Dictionary<int, List<int>>();
		private Dictionary<string, PalletStatus> _pallets = new Dictionary<string, PalletStatus>();

		public IDictionary<string, PalletStatus> Pallets
		{
			get { return _pallets; }
		}

        public IEnumerable<InProcessMaterial> Material
        {
            get { return _fixPalIDToMaterial.SelectMany(x => x.Value); }
        }

		public void AddPalletInfo(int fixutrePalletID, int fixtureNum, int fixtureID, int palletNum, PalletLocation loc)
		{
			_fixPalIDToFixNum.Add(fixutrePalletID, fixtureNum);
			_fixPalIDToFixID.Add(fixutrePalletID, fixtureID);
			_fixPalIDToPalNum.Add(fixutrePalletID, palletNum);
			if (!_fixIDToPallets.ContainsKey(fixtureID))
				_fixIDToPallets.Add(fixtureID, new List<int>());
			_fixIDToPallets[fixtureID].Add(palletNum);

			if (_pallets.ContainsKey(palletNum.ToString()))
            {
                var p = _pallets[palletNum.ToString()];
                p.NumFaces = Math.Max(p.NumFaces, fixtureNum);
                return;
            }

			PalletStatus pal;

            pal = new PalletStatus()
            {
                Pallet = palletNum.ToString(),
                CurrentPalletLocation = loc,
                NumFaces = fixtureNum,
                OnHold = false,
            };

			_pallets.Add(palletNum.ToString(), pal);
		}

		public IEnumerable<int> PalletsForFixture(int fixtureID)
		{
			if (_fixIDToPallets.ContainsKey(fixtureID))
				return _fixIDToPallets[fixtureID];
			else
				return new int[] {};
		}

		public void PalletLocInfo(int fixturePalletID, out int palletNum, out int fixNum)
		{
			palletNum = _fixPalIDToPalNum[fixturePalletID];
			fixNum = _fixPalIDToFixNum[fixturePalletID];
		}

		public void AddMaterial(int fixturePalletID, InProcessMaterial mat)
		{
            List<InProcessMaterial> ms;
            if (_fixPalIDToMaterial.ContainsKey(fixturePalletID))
                ms = _fixPalIDToMaterial[fixturePalletID];
            else
            {
                ms = new List<InProcessMaterial>();
                _fixPalIDToMaterial.Add(fixturePalletID, ms);
            }
            ms.Add(mat);

			var palletNum = _fixPalIDToPalNum[fixturePalletID];
			var pal = _pallets[palletNum.ToString()];

            if (pal.CurrentPalletLocation.Location == PalletLocationEnum.Machine && mat.Action.Program != "")
            {
                mat.Action.Type = InProcessMaterialAction.ActionType.Machining;
            }
            else
            {
                mat.Action.Program = "";
            }
		}

		public void SetMaterialAsUnload(int fixturePalletID, bool completed)
		{
			var palletNum = _fixPalIDToPalNum[fixturePalletID];
			var pal = _pallets[palletNum.ToString()];
			var face = _fixPalIDToFixNum[fixturePalletID].ToString();

            if (_fixPalIDToMaterial.ContainsKey(fixturePalletID))
            {
                foreach (var mat in _fixPalIDToMaterial[fixturePalletID])
                {
                    mat.Action = new InProcessMaterialAction()
                    {
                        Type = completed
                            ? InProcessMaterialAction.ActionType.UnloadToCompletedMaterial
                            : InProcessMaterialAction.ActionType.UnloadToInProcess
                    };
                }
            }
		}

		public void AddMaterialToLoad(int fixturePalletID, string unique, string partName, int procNum, int qty)
		{
			var palletNum = _fixPalIDToPalNum[fixturePalletID];
			var pal = _pallets[palletNum.ToString()];
			var face = _fixPalIDToFixNum[fixturePalletID];

            List<InProcessMaterial> ms;
            if (_fixPalIDToMaterial.ContainsKey(fixturePalletID))
                ms = _fixPalIDToMaterial[fixturePalletID];
            else
            {
                ms = new List<InProcessMaterial>();
                _fixPalIDToMaterial.Add(fixturePalletID, ms);
            }

            var mat = new InProcessMaterial()
            {
                MaterialID = -1,
                JobUnique = unique,
                PartName = partName,
                Process = procNum,
                Path = 1,
                Location = new InProcessMaterialLocation()
                {
                    Type = InProcessMaterialLocation.LocType.Free,
                },
                Action = new InProcessMaterialAction()
                {
                    Type = InProcessMaterialAction.ActionType.Loading,
                    ProcessAfterLoad = procNum,
                    PathAfterLoad = 1,
                    LoadOntoFace = face,
                    LoadOntoPallet = pal.Pallet
                }
            };

            ms.Add(mat);
		}
	}
}