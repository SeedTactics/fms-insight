/* Copyright (c) 2020, John Lenz

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

    public NiigataStatus LoadNiigataStatus() => _status;

    public event Action<NewProgram> OnNewProgram;
    public event Action NewCurrentStatus;

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

        case NewProgram newprog:
          OnNewProgram?.Invoke(newprog);
          _status.Programs[newprog.ProgramNum] = new ProgramEntry()
          {
            ProgramNum = newprog.ProgramNum,
            Comment = "Comment " + newprog.ProgramName + " rev" + newprog.ProgramRevision.ToString(),
            CycleTime = TimeSpan.FromMinutes(newprog.ProgramRevision),
            Tools = new List<int>()
          };
          break;
      }
      NewCurrentStatus?.Invoke();
    }

    public IccSimulator(int numPals, int numMachines, int numLoads)
    {
      _status = new NiigataStatus();
      _status.TimeOfStatusUTC = DateTime.UtcNow.AddDays(-1);
      _status.Programs = new Dictionary<int, ProgramEntry>();

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
      public Action UpdateStatus { get; set; }
    }

    public bool Step()
    {
      var transitions = new List<NextTransition>();

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
        if (pal.Master.Routes.Count == 0) continue;
        if (pal.Master.Skip) continue;
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
                UpdateStatus = () =>
                {
                  _lastPalTransition[pal.Master.PalletNum] = _lastPalTransition[pal.Master.PalletNum].Add(LoadTime);
                  pal.Tracking.CurrentControlNum += 1;
                }
              });
            }
            // if in the buffer, add transition to put on cart
            else if (beforeStep && pal.CurStation.Location.Location == PalletLocationEnum.Buffer)
            {
              var lul = openLoads.FirstOrDefault(load.LoadStations.Contains);
              if (!cartInUse && lul > 0)
              {
                transitions.Add(new NextTransition()
                {
                  Time = _status.TimeOfStatusUTC,
                  UpdateStatus = () =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = _status.TimeOfStatusUTC;
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
                  UpdateStatus = () =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = _lastPalTransition[pal.Master.PalletNum].Add(CartTravelTime);
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
            else if (!beforeStep && pal.CurStation.Location.Location == PalletLocationEnum.LoadUnload)
            {
              if (!cartInUse)
              {
                transitions.Add(new NextTransition()
                {
                  Time = _status.TimeOfStatusUTC,
                  UpdateStatus = () =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = _status.TimeOfStatusUTC;
                    pal.CurStation = NiigataStationNum.Cart();
                    _status.LoadStations[pal.CurStation.Location.Num].PalletExists = false;
                  }
                });
              }

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
                  UpdateStatus = () =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = _lastPalTransition[pal.Master.PalletNum].Add(CartTravelTime);
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
                  UpdateStatus = () =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = _lastPalTransition[pal.Master.PalletNum].Add(CartTravelTime);
                    pal.CurStation = NiigataStationNum.Buffer(pal.Master.PalletNum);
                    pal.Tracking.CurrentControlNum += 1;
                    pal.Tracking.CurrentStepNum += 1;
                  }
                });
              }
            }
            else
            {
              throw new Exception("Invalid LoadStep state");
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
                  UpdateStatus = () =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = _status.TimeOfStatusUTC;
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
                  UpdateStatus = () =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = _lastPalTransition[pal.Master.PalletNum].Add(CartTravelTime);
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
              var mc = pal.CurStation.Location.Num;
              var palInsideMachine = _status.Pallets.FirstOrDefault(p =>
                p.CurStation.Location.Location == PalletLocationEnum.Machine && p.CurStation.Location.Num == mc
              );
              DateTime swapTime;
              if (palInsideMachine != null)
              {
                swapTime =
                  new DateTime(
                    Math.Max(_lastPalTransition[pal.Master.PalletNum].Ticks, _lastPalTransition[palInsideMachine.Master.PalletNum].Ticks),
                    _lastPalTransition[pal.Master.PalletNum].Kind
                  ).Add(RotarySwapTime);
              }
              else
              {
                swapTime = _lastPalTransition[pal.Master.PalletNum].Add(RotarySwapTime);
              }
              if (!_status.Machines[mc].Machining)
              {
                transitions.Add(new NextTransition()
                {
                  Time = swapTime,
                  UpdateStatus = () =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = swapTime;
                    pal.CurStation = NiigataStationNum.Machine(mc);
                    _lastMachineTransition[mc] = swapTime;
                    _programsRunOnMachine[mc].Clear();
                    _status.Machines[mc].Machining = true;
                    _status.Machines[mc].CurrentlyExecutingProgram = mach.ProgramNumsToRun.First();
                    if (palInsideMachine != null)
                    {
                      _lastPalTransition[palInsideMachine.Master.PalletNum] = swapTime;
                      palInsideMachine.CurStation = NiigataStationNum.MachineQueue(mc);
                    }
                  }
                });
              }
            }
            // if on the machine, consider starting next program or setting after-MC
            else if (beforeStep && pal.CurStation.Location.Location == PalletLocationEnum.Machine)
            {
              var mc = pal.CurStation.Location.Num;
              if (!_status.Machines[mc].Machining)
              {
                throw new Exception("Pallet in Before-MC but not machining");
              }
              var curProg = _status.Machines[mc].CurrentlyExecutingProgram;
              var time = _programTimes.ContainsKey(curProg) ? _programTimes[curProg] : TimeSpan.FromMinutes(10);
              transitions.Add(new NextTransition()
              {
                Time = _lastMachineTransition[mc].Add(time),
                UpdateStatus = () =>
                {
                  _lastMachineTransition[mc] = _lastMachineTransition[mc].Add(time);
                  _programsRunOnMachine[mc].Add(curProg);
                  // either go to After-MC or next program
                  var nextProg = mach.ProgramNumsToRun.Where(p => !_programsRunOnMachine[mc].Contains(p)).FirstOrDefault();
                  if (nextProg > 0)
                  {
                    _status.Machines[mc].CurrentlyExecutingProgram = nextProg;
                  }
                  else
                  {
                    _lastPalTransition[pal.Master.PalletNum] = _lastMachineTransition[mc].Add(time);
                    pal.Tracking.CurrentControlNum += 1;
                    _status.Machines[mc].Machining = false;
                  }
                }
              });
            }
            //if After-MC and still in machine, consider swap
            else if (!beforeStep && pal.CurStation.Location.Location == PalletLocationEnum.Machine)
            {
              var mc = pal.CurStation.Location.Num;
              var palOnQueue = _status.Pallets.FirstOrDefault(p =>
                p.CurStation.Location.Location == PalletLocationEnum.MachineQueue && p.CurStation.Location.Num == mc
              );
              DateTime swapTime;
              if (palOnQueue != null)
              {
                swapTime =
                  new DateTime(
                    Math.Max(_lastPalTransition[pal.Master.PalletNum].Ticks, _lastPalTransition[palOnQueue.Master.PalletNum].Ticks),
                    _lastPalTransition[pal.Master.PalletNum].Kind
                  ).Add(RotarySwapTime);
              }
              else
              {
                swapTime = _lastPalTransition[pal.Master.PalletNum].Add(RotarySwapTime);
              }
              if (!_status.Machines[mc].Machining && (palOnQueue == null || (palOnQueue.CurrentStep is MachiningStep && palOnQueue.Tracking.BeforeCurrentStep)))
              {
                transitions.Add(new NextTransition()
                {
                  Time = swapTime,
                  UpdateStatus = () =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = swapTime;
                    pal.CurStation = NiigataStationNum.MachineQueue(mc);
                    if (palOnQueue != null)
                    {
                      var palOnQueueMach = (MachiningStep)palOnQueue.CurrentStep;
                      _lastMachineTransition[mc] = swapTime;
                      _programsRunOnMachine[mc].Clear();
                      _status.Machines[mc].Machining = true;
                      _status.Machines[mc].CurrentlyExecutingProgram = palOnQueueMach.ProgramNumsToRun.First();
                      _lastPalTransition[palOnQueue.Master.PalletNum] = swapTime;
                      palOnQueue.CurStation = NiigataStationNum.Machine(mc);
                    }
                  }
                });
              }
            }
            // if after-mc and cart not in use, move to cart
            else if (!beforeStep && pal.CurStation.Location.Location == PalletLocationEnum.MachineQueue)
            {
              if (!cartInUse)
              {
                transitions.Add(new NextTransition()
                {
                  Time = _status.TimeOfStatusUTC,
                  UpdateStatus = () =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = _status.TimeOfStatusUTC;
                    pal.CurStation = NiigataStationNum.Cart();
                  }
                });
              }
            }
            // if after-mc and on cart, move to load or buffer
            else if (!beforeStep && pal.CurStation.Location.Location == PalletLocationEnum.Cart)
            {
              int toLoad = 0;
              var next = pal.Master.Routes[pal.Tracking.CurrentStepNum - 1 + 1];
              if (next is UnloadStep)
              {
                toLoad = openLoads.FirstOrDefault(((UnloadStep)next).UnloadStations.Contains);
              }
              if (toLoad > 0)
              {
                transitions.Add(new NextTransition()
                {
                  Time = _lastPalTransition[pal.Master.PalletNum].Add(CartTravelTime),
                  UpdateStatus = () =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = _lastPalTransition[pal.Master.PalletNum].Add(CartTravelTime);
                    pal.CurStation = NiigataStationNum.LoadStation(toLoad);
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
                  UpdateStatus = () =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = _lastPalTransition[pal.Master.PalletNum].Add(CartTravelTime);
                    pal.CurStation = NiigataStationNum.Buffer(pal.Master.PalletNum);
                    pal.Tracking.CurrentStepNum += 1;
                    pal.Tracking.CurrentControlNum += 1;
                  }
                });

              }
            }
            else
            {
              throw new Exception("Invalid MachiningStep state");
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
                UpdateStatus = () =>
                {
                  _lastPalTransition[pal.Master.PalletNum] = _lastPalTransition[pal.Master.PalletNum].Add(LoadTime);
                  pal.Master.RemainingPalletCycles -= 1;
                  if (pal.Master.RemainingPalletCycles == 0)
                  {
                    pal.Master.NoWork = true;
                    pal.Tracking.CurrentControlNum += 1;
                  }
                  else
                  {
                    pal.Tracking.CurrentStepNum = 1;
                    pal.Tracking.CurrentControlNum = 2;
                  }
                }
              });
            }
            // if in the buffer, add transition to put on cart
            else if (beforeStep && pal.CurStation.Location.Location == PalletLocationEnum.Buffer)
            {
              var lul = openLoads.FirstOrDefault(unload.UnloadStations.Contains);
              if (!cartInUse && lul > 0)
              {
                transitions.Add(new NextTransition()
                {
                  Time = _status.TimeOfStatusUTC,
                  UpdateStatus = () =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = _status.TimeOfStatusUTC;
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
                  UpdateStatus = () =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = _lastPalTransition[pal.Master.PalletNum].Add(CartTravelTime);
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
            else if (!beforeStep && pal.Master.NoWork && pal.CurStation.Location.Location == PalletLocationEnum.LoadUnload)
            {
              if (!cartInUse)
              {
                transitions.Add(new NextTransition()
                {
                  Time = _status.TimeOfStatusUTC,
                  UpdateStatus = () =>
                  {
                    _lastPalTransition[pal.Master.PalletNum] = _status.TimeOfStatusUTC;
                    pal.CurStation = NiigataStationNum.Cart();
                  }
                });
              }
            }
            else if (!beforeStep && pal.Master.NoWork && pal.CurStation.Location.Location == PalletLocationEnum.Cart)
            {
              transitions.Add(new NextTransition()
              {
                Time = _lastPalTransition[pal.Master.PalletNum].Add(CartTravelTime),
                UpdateStatus = () =>
                {
                  _lastPalTransition[pal.Master.PalletNum] = _lastPalTransition[pal.Master.PalletNum].Add(CartTravelTime);
                  pal.CurStation = NiigataStationNum.Buffer(pal.Master.PalletNum);
                }
              });
            }
            else if (!beforeStep && pal.Master.NoWork && pal.CurStation.Location.Location == PalletLocationEnum.Buffer)
            {
              // do nothing
            }
            else
            {
              throw new Exception("Invalid UnloadStep state");
            }
            break;
        }
      }

      if (transitions.Any())
      {
        var t = transitions.OrderBy(e => e.Time).First();
        _status.TimeOfStatusUTC = t.Time;
        t.UpdateStatus();
        NewCurrentStatus?.Invoke();
        return true;
      }
      else
      {
        return false;
      }
    }

    public string DebugPrintStatus()
    {
      var output = new System.Text.StringBuilder();

      output.AppendLine(_status.TimeOfStatusUTC.ToString());

      foreach (var p in _status.Pallets)
      {
        output.AppendFormat("Pal {0} - {1} {2} - [cycles: {3}, pri: {4}, nowork: {5}, skip: {6}] - ",
          p.Master.PalletNum, p.CurStation.Location.Location, p.CurStation.Location.Num,
          p.Master.RemainingPalletCycles, p.Master.Priority, p.Master.NoWork, p.Master.Skip
        );
        output.AppendJoin(" -> ", p.Master.Routes.Select(r =>
        {
          string before = r == p.CurrentStep && p.Tracking.BeforeCurrentStep ? "*" : "";
          string after = r == p.CurrentStep && !p.Tracking.BeforeCurrentStep ? "*" : "";
          switch (r)
          {
            case LoadStep load:
              return before + "LD[" + string.Join(',', load.LoadStations) + "]" + after;
            case UnloadStep load:
              return before + "UL[" + string.Join(',', load.UnloadStations) + "]" + after;
            case MachiningStep mach:
              return before + "MC[" + string.Join(',', mach.Machines) + "][" + string.Join(',', mach.ProgramNumsToRun) + "]" + after;
            default:
              return before + "ZZ" + after;
          }
        }));
        output.AppendLine();
      }

      foreach (var m in _status.Machines.Keys.OrderBy(x => x))
      {
        if (_status.Machines[m].Machining)
        {
          output.AppendFormat("Mach {0} {1}", m, _status.Machines[m].CurrentlyExecutingProgram);
          output.AppendLine();
        }
        else
        {
          output.AppendFormat("Mach {0} off", m);
          output.AppendLine();
        }
      }

      return output.ToString();
    }
  }
}