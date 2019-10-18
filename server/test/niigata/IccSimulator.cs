/* Copyright (c) 2019, John Lenz

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
using System.Linq;
using System.Collections.Generic;
using BlackMaple.MachineWatchInterface;

namespace BlackMaple.FMSInsight.Niigata.Tests
{
  public class IccSimulator : INiigataCommunication
  {
    private readonly TimeSpan RotarySwapTime = TimeSpan.FromSeconds(10);
    private readonly TimeSpan CartTravelTime = TimeSpan.FromMinutes(1);
    private readonly TimeSpan LoadTime = TimeSpan.FromMinutes(5);

    private NiigataStatus _status;

    private Dictionary<int, DateTime> _lastPalTransition = new Dictionary<int, DateTime>();
    private Dictionary<int, DateTime> _lastMachineTransition = new Dictionary<int, DateTime>();
    private Dictionary<int, HashSet<int>> _programsRunOnMachine = new Dictionary<int, HashSet<int>>();
    private Dictionary<int, TimeSpan> _programTimes = new Dictionary<int, TimeSpan>();

    public NiigataStatus LoadStatus() => _status;

    public void PerformAction(NiigataAction a)
    {
      switch (a)
      {
        case NewPalletRoute route:
          _status.Pallets[route.NewMaster.PalletNum - 1].Master = route.NewMaster;
          _status.Pallets[route.NewMaster.PalletNum - 1].Tracking.CurrentStepNum = 1;
          _status.Pallets[route.NewMaster.PalletNum - 1].Tracking.CurrentControlNum = 1;
          break;

        case UpdatePalletQuantities update:
          _status.Pallets[update.Pallet - 1].Master.Priority = update.Priority;
          _status.Pallets[update.Pallet - 1].Master.NoWork = update.NoWork;
          _status.Pallets[update.Pallet - 1].Master.Skip = update.Skip;
          _status.Pallets[update.Pallet - 1].Master.RemainingPalletCycles = update.Cycles;
          break;
      }
    }

    public IccSimulator(int numPals, int numMachines, int numLoads)
    {
      _status = new NiigataStatus();
      _status.TimeOfStatusUTC = DateTime.UtcNow.AddDays(-1);

      _status.Machines = new Dictionary<int, MachineStatus>();
      for (int mach = 1; mach <= numMachines; mach++)
      {
        _status.Machines.Add(mach, new MachineStatus()
        {
          MachineNumber = mach,
          Machining = false,
          CurrentlyExecutingProgram = 0
        });
        _lastMachineTransition[mach] = _status.TimeOfStatusUTC;
        _programsRunOnMachine[mach] = new HashSet<int>();
      }

      _status.Pallets = new List<PalletStatus>();
      for (int pal = 1; pal <= numPals; pal++)
      {
        _status.Pallets.Add(new PalletStatus()
        {
          Master = new PalletMaster()
          {
            PalletNum = pal,
            NoWork = true
          },
          CurStation = NiigataStationNum.Buffer(pal),
          Tracking = new TrackingInfo()
        });
        _lastPalTransition[pal] = _status.TimeOfStatusUTC;
      }

      _status.LoadStations = new Dictionary<int, LoadStatus>();
      for (int load = 1; load <= numLoads; load++)
      {
        _status.LoadStations.Add(load, new LoadStatus()
        {
          LoadNumber = load,
          PalletExists = false
        });
      }
    }

    public void SetProgramTime(int program, TimeSpan time) => _programTimes[program] = time;

    private class NextTransition
    {
      public DateTime Time { get; set; }
      public Action<DateTime> UpdateStatus { get; set; }
    }

    public void Step()
    {
      var transitions = new List<NextTransition>();

      foreach (var mach in _status.Machines)
      {
        if (mach.Value.Machining)
        {
          transitions.Add(new NextTransition()
          {
            Time = _lastMachineTransition[mach.Key].Add(_programTimes[mach.Value.CurrentlyExecutingProgram]),
            UpdateStatus = now =>
            {
              _lastMachineTransition[mach.Key] = now;
              _status.Machines[mach.Key].Machining = false;
            }
          });
        }
      }

      bool cartInUse = _status.Pallets.FirstOrDefault(x => x.CurStation.Location.Location == PalletLocationEnum.Cart) != null;
      IEnumerable<int> openLoads =
        _status.LoadStations.Values
        .Where(l => !l.PalletExists)
        .Select(l => l.LoadNumber)
        .OrderBy(x => x)
        .ToList()
        ;
      IEnumerable<int> openMach =
        _status.Machines.Keys
        .Where(m =>
          _status.Pallets.FirstOrDefault(x => x.CurStation.Location.Location == PalletLocationEnum.MachineQueue && x.CurStation.Location.Num == m)
          ==
          null
        )
        .OrderBy(x => x)
        .ToList()
        ;

      foreach (var pal in _status.Pallets)
      {
        bool beforeStep = pal.Tracking.BeforeCurrentStep;

        // switch on current step
        switch (pal.CurrentStep)
        {
          // ---------------------------------------- Load Step ---------------------------------
          case LoadStep load:
            // if currently at load, add transition to finish load
            if (beforeStep && pal.CurStation.Location.Location == PalletLocationEnum.LoadUnload)
            {
              transitions.Add(new NextTransition()
              {
                Time = _lastPalTransition[pal.Master.PalletNum].Add(LoadTime),
                UpdateStatus = now =>
                {
                  _lastPalTransition[pal.Master.PalletNum] = now;
                  pal.Tracking.CurrentControlNum += 1;
                }
              });
            }
            // if in the buffer, add transition to put on cart
            else if (beforeStep && !cartInUse && pal.CurStation.Location.Location == PalletLocationEnum.Buffer)
            {
              var lul = openLoads.FirstOrDefault(load.LoadStations.Contains);
              if (lul > 0)
              {
                transitions.Add(new NextTransition()
                {
                  Time = _status.TimeOfStatusUTC,
                  UpdateStatus = now =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = now;
                    pal.CurStation = NiigataStationNum.Cart();
                  }
                });
              }
            }
            // if on the cart, add transition to drop off
            else if (beforeStep && pal.CurStation.Location.Location == PalletLocationEnum.Cart)
            {
              var lul = openLoads.FirstOrDefault(load.LoadStations.Contains);
              if (lul > 0)
              {
                transitions.Add(new NextTransition()
                {
                  Time = _lastPalTransition[pal.Master.PalletNum].Add(CartTravelTime),
                  UpdateStatus = now =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = now;
                    pal.CurStation = NiigataStationNum.LoadStation(lul);
                    _status.LoadStations[lul].PalletExists = true;
                  }
                });
              }
              else
              {
                throw new Exception("Before-LD pallet put on cart without open load");
              }
            }
            //after load, if currently at load move to cart
            else if (!beforeStep && !cartInUse && pal.CurStation.Location.Location == PalletLocationEnum.LoadUnload)
            {
              transitions.Add(new NextTransition()
              {
                Time = _status.TimeOfStatusUTC,
                UpdateStatus = now =>
                {
                  _lastPalTransition[pal.Master.PalletNum] = now;
                  pal.CurStation = NiigataStationNum.Cart();
                  _status.LoadStations[pal.CurStation.Location.Num].PalletExists = false;
                }
              });

            }
            //if on cart, move to machine or buffer
            else if (!beforeStep && pal.CurStation.Location.Location == PalletLocationEnum.Cart)
            {
              var nextStep = (MachiningStep)pal.Master.Routes[pal.Tracking.CurrentStepNum - 1 + 1];
              var mach = openMach.FirstOrDefault(nextStep.Machines.Contains);
              if (mach > 0)
              {
                transitions.Add(new NextTransition()
                {
                  Time = _lastPalTransition[pal.Master.PalletNum].Add(CartTravelTime),
                  UpdateStatus = now =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = now;
                    pal.CurStation = NiigataStationNum.MachineQueue(mach);
                    pal.Tracking.CurrentControlNum += 1;
                    pal.Tracking.CurrentStepNum += 1;
                  }
                });
              }
              else
              {
                transitions.Add(new NextTransition()
                {
                  Time = _lastPalTransition[pal.Master.PalletNum].Add(CartTravelTime),
                  UpdateStatus = now =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = now;
                    pal.CurStation = NiigataStationNum.Buffer(pal.Master.PalletNum);
                    pal.Tracking.CurrentControlNum += 1;
                    pal.Tracking.CurrentStepNum += 1;
                  }
                });
              }
            }
            break;

          // ---------------------------------------- Machining Step ---------------------------------
          case MachiningStep mach:

            // if at buffer and there is open machine, move to cart
            if (beforeStep && pal.CurStation.Location.Location == PalletLocationEnum.Buffer)
            {
              var mc = openMach.FirstOrDefault(mach.Machines.Contains);
              if (mc > 0)
              {
                transitions.Add(new NextTransition()
                {
                  Time = _status.TimeOfStatusUTC,
                  UpdateStatus = now =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = now;
                    pal.CurStation = NiigataStationNum.Cart();
                  }
                });
              }
            }
            // if on cart, drop at machine
            else if (beforeStep && pal.CurStation.Location.Location == PalletLocationEnum.Cart)
            {
              var mc = openMach.FirstOrDefault(mach.Machines.Contains);
              if (mc > 0)
              {
                transitions.Add(new NextTransition()
                {
                  Time = _lastPalTransition[pal.Master.PalletNum].Add(CartTravelTime),
                  UpdateStatus = now =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = now;
                    pal.CurStation = NiigataStationNum.MachineQueue(mc);
                  }
                });
              }
              else
              {
                throw new Exception("Before-MC pallet put on cart without open machine");
              }
            }
            // if on rotary queue, consider swap
            else if (beforeStep && pal.CurStation.Location.Location == PalletLocationEnum.MachineQueue)
            {
              //TODO: rotary swap
            }
            // if on the machine, consider starting machine or setting after-MC
            else if (beforeStep && pal.CurStation.Location.Location == PalletLocationEnum.Machine)
            {
              var mc = pal.CurStation.Location.Num;
              if (!_status.Machines[mc].Machining)
              {
                var prog = mach.ProgramNumsToRun.Where(p => !_programsRunOnMachine[mc].Contains(p)).FirstOrDefault();
                if (prog > 0)
                {
                  transitions.Add(new NextTransition()
                  {
                    Time = _status.TimeOfStatusUTC,
                    UpdateStatus = now =>
                    {
                      _lastMachineTransition[mc] = now;
                      _status.Machines[mc].Machining = true;
                      _status.Machines[mc].CurrentlyExecutingProgram = prog;
                    }
                  });
                }
                else
                {
                  //nothing more to run, set After-Mc
                  transitions.Add(new NextTransition()
                  {
                    Time = _status.TimeOfStatusUTC,
                    UpdateStatus = now =>
                    {
                      _lastPalTransition[pal.Master.PalletNum] = now;
                      pal.Tracking.CurrentControlNum += 1;
                    }
                  });
                }
              }
            }
            //if After-MC and still in machine, consider swap
            else if (!beforeStep && pal.CurStation.Location.Location == PalletLocationEnum.Machine)
            {
              //TODO: swap
            }
            // if after-mc and cart not in use, move to cart
            else if (!beforeStep && !cartInUse && pal.CurStation.Location.Location == PalletLocationEnum.MachineQueue)
            {
              transitions.Add(new NextTransition()
              {
                Time = _status.TimeOfStatusUTC,
                UpdateStatus = now =>
                {
                  _lastPalTransition[pal.Master.PalletNum] = now;
                  pal.CurStation = NiigataStationNum.Cart();
                }
              });
            }
            // if after-mc and on cart, move to load or buffer
            else if (!beforeStep && pal.CurStation.Location.Location == PalletLocationEnum.Cart)
            {
              var next = (UnloadStep)pal.Master.Routes[pal.Tracking.CurrentStepNum - 1 + 1];
              var lul = openLoads.FirstOrDefault(next.UnloadStations.Contains);
              if (lul > 0)
              {
                transitions.Add(new NextTransition()
                {
                  Time = _lastPalTransition[pal.Master.PalletNum].Add(CartTravelTime),
                  UpdateStatus = now =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = now;
                    pal.CurStation = NiigataStationNum.LoadStation(lul);
                    pal.Tracking.CurrentStepNum += 1;
                    pal.Tracking.CurrentControlNum += 1;
                  }
                });
              }
              else
              {
                transitions.Add(new NextTransition()
                {
                  Time = _lastPalTransition[pal.Master.PalletNum].Add(CartTravelTime),
                  UpdateStatus = now =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = now;
                    pal.CurStation = NiigataStationNum.Buffer(pal.Master.PalletNum);
                    pal.Tracking.CurrentStepNum += 1;
                    pal.Tracking.CurrentControlNum += 1;
                  }
                });

              }
            }
            break;

          // ---------------------------------------- Unload Step ---------------------------------
          case UnloadStep unload:
            // if currently at load, add transition to finish load
            if (beforeStep && pal.CurStation.Location.Location == PalletLocationEnum.LoadUnload)
            {
              transitions.Add(new NextTransition()
              {
                Time = _lastPalTransition[pal.Master.PalletNum].Add(LoadTime),
                UpdateStatus = now =>
                {
                  _lastPalTransition[pal.Master.PalletNum] = now;
                  pal.Tracking.CurrentStepNum = 1;
                  pal.Tracking.CurrentControlNum = 2;
                  if (pal.Master.RemainingPalletCycles > 1)
                  {
                    pal.Master.RemainingPalletCycles -= 1;
                  }
                  else
                  {
                    pal.Master.NoWork = true;
                  }
                }
              });
            }
            // if in the buffer, add transition to put on cart
            else if (beforeStep && !cartInUse && pal.CurStation.Location.Location == PalletLocationEnum.Buffer)
            {
              var lul = openLoads.FirstOrDefault(unload.UnloadStations.Contains);
              if (lul > 0)
              {
                transitions.Add(new NextTransition()
                {
                  Time = _status.TimeOfStatusUTC,
                  UpdateStatus = now =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = now;
                    pal.CurStation = NiigataStationNum.Cart();
                  }
                });
              }
            }
            // if on the cart, add transition to drop off
            else if (beforeStep && pal.CurStation.Location.Location == PalletLocationEnum.Cart)
            {
              var lul = openLoads.FirstOrDefault(unload.UnloadStations.Contains);
              if (lul > 0)
              {
                transitions.Add(new NextTransition()
                {
                  Time = _lastPalTransition[pal.Master.PalletNum].Add(CartTravelTime),
                  UpdateStatus = now =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = now;
                    pal.CurStation = NiigataStationNum.LoadStation(lul);
                    _status.LoadStations[lul].PalletExists = true;
                  }
                });
              }
              else
              {
                throw new Exception("Before-UL pallet put on cart without open load");
              }
            }
            break;
        }
      }
    }

  }
}