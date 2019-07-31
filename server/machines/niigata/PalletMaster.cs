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
using System.Collections.Generic;

namespace BlackMaple.FMSInsight.Niigata
{
  public abstract class RouteStep { }

  public class LoadStep : RouteStep
  {
    public List<int> LoadStations { get; set; } = new List<int>();
  }

  public class ReclampStep : RouteStep
  {
    public List<int> Reclamp { get; set; } = new List<int>();
  }

  public class UnloadStep : RouteStep
  {
    public List<int> UnloadStations { get; set; } = new List<int>();
    public int CompletedPartCount { get; set; } = 1;
    // up to 3 pallets can be marked as next pallets.  When part is unloaded, the next pallet(s)
    // has its remaining pallet cycles increased by 1.
    public List<int> NextPallets { get; set; } = new List<int>();
  }

  public class MachiningStep : RouteStep
  {
    public List<int> Machines { get; set; } = new List<int>();

    public class Program
    {
      public string ProgramName { get; set; }
      public bool Skip { get; set; } = false;
      public List<bool> BlockSkip { get; set; } // contains 8 entries
    }

    public class Face
    {
      public List<Program> Programs { get; set; } = new List<Program>();
      public bool Skip { get; set; } = true; // if true, entire face is skipped
    }

    public List<Face> Faces { get; set; } = new List<Face>();
  }

  public class WashStep : RouteStep
  {
    public List<int> WashStations { get; set; } = new List<int>();
    public int WashingPattern { get; set; } = 0;
  }

  // Each pallet has a trackinginfo, which captures what has been done on the pallet.
  // Can be before or after each possible route step.
  //
  // If tracking is MC before, then not allowed to be located at load station
  // If tracking is LD before, then not allowed to be located at machine
  public class TrackingInfo
  {
    public int RouteNum { get; set; } = 1; // 1-indexed, so 1 is the first route in the list
    public bool Before { get; set; }
  }

  public abstract class NiigataPalletLocation { }

  public class LoadUnloadLoc : NiigataPalletLocation
  {
    public int LoadStation { get; set; }
  }

  public class MachineOrWashLoc : NiigataPalletLocation
  {
    public enum Rotary
    {
      Input,
      Output,
      Worktable
    }
    public int Station { get; set; }
    public Rotary Position { get; set; }
  }

  public class StockerLoc : NiigataPalletLocation { }

  public class CartLoc : NiigataPalletLocation { }

  public class PalletMaster
  {
    public int PalletNum { get; set; }
    public string Comment { get; set; } = null;
    public int NumFaces { get; set; } = 1; // can be 1, 4, or 8.
    public int RemainingPalletCycles { get; set; } = 0; // 999 indicates run forever
    public int Priority { get; set; } = 0; // 9 is lowest, 1 is highest, 0 is no priority

    // When true, no workpiece is loaded.  This gets set when the user presses the "Unload" button
    // at the load station
    public bool NoWork { get; set; } = false;
    public bool Skip { get; set; } = false; // if true, pallet is on hold and ignored
    public bool Alarm { get; set; } = false;
    public bool PerformProgramDownload { get; set; } = false;
    public List<RouteStep> Routes { get; set; } = new List<RouteStep>();
  }

  public class NiigataPallet
  {
    public PalletMaster Master { get; set; }
    public TrackingInfo Tracking { get; set; }
    public NiigataPalletLocation Loc { get; set; } = new StockerLoc();
  }

  public interface ILoadNiigataPallets
  {
    IList<NiigataPallet> LoadPallets();
  }
}
