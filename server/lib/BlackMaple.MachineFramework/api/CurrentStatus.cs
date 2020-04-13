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
using System.Linq;
using System.Runtime.Serialization;

namespace BlackMaple.MachineWatchInterface
{
  ///Stores what is currently happening to a piece of material.
  [Serializable, DataContract]
  public class InProcessMaterialAction
  {
    // This should be a sum type, and while C# sum types can work with some helper code it doesn't work
    // well for serialization.

    [Serializable, DataContract]
    public enum ActionType
    {
      Waiting = 0,
      Loading,
      UnloadToInProcess, // unload, but keep the material around because more processes must be machined
      UnloadToCompletedMaterial, // unload and the material has been completed
      Machining
    }
    [DataMember(IsRequired = true)] public ActionType Type { get; set; }

    // If Type = Loading
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public string LoadOntoPallet { get; set; }
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public int? LoadOntoFace { get; set; }
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public int? ProcessAfterLoad { get; set; }
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public int? PathAfterLoad { get; set; }

    //If Type = UnloadToInProcess
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public string UnloadIntoQueue { get; set; }

    //If Type = Loading or UnloadToInProcess or UnloadToCompletedMaterial
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public TimeSpan? ElapsedLoadUnloadTime { get; set; }

    // If Type = Machining
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public string Program { get; set; }
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public TimeSpan? ElapsedMachiningTime { get; set; }
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public TimeSpan? ExpectedRemainingMachiningTime { get; set; }
  }

  ///Stores the current location of a piece of material.  If a transfer operation is currently in process
  ///(such as unloading), the location will store the previous location and the action will store the new location.
  [Serializable, DataContract]
  public class InProcessMaterialLocation
  {
    //Again, this should be a sum type.
    [Serializable, DataContract]
    public enum LocType
    {
      Free = 0,
      OnPallet,
      InQueue,
    }
    [DataMember(IsRequired = true)] public LocType Type { get; set; }

    //If Type == OnPallet
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public string Pallet { get; set; }
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public int? Face { get; set; }

    //If Type == InQueue
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public string CurrentQueue { get; set; }

    //If Type == InQueue or Type == Free
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public int? QueuePosition { get; set; }
  }

  //Stores information about a piece of material, where it is, and what is happening to it.
  [Serializable, DataContract]
  public class InProcessMaterial
  {
    // Information about the material
    [DataMember(IsRequired = true)] public long MaterialID { get; set; }
    [DataMember(IsRequired = true)] public string JobUnique { get; set; }
    [DataMember(IsRequired = true)] public string PartName { get; set; }
    [DataMember(IsRequired = true)] public int Process { get; set; }  // When in a queue, the process is the last completed process
    [DataMember(IsRequired = true)] public int Path { get; set; }
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public string Serial { get; set; }
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public string WorkorderId { get; set; }
    [DataMember(IsRequired = true)] public List<string> SignaledInspections { get; set; } = new List<string>();

    // 0-based index into the JobPlan.MachiningStops array for the last completed stop.  Null or negative values
    // indicate no machining stops have yet completed.
    [DataMember(IsRequired = false)] public int? LastCompletedMachiningRouteStopIndex { get; set; }

    // Where is the material?
    [DataMember(IsRequired = true)] public InProcessMaterialLocation Location { get; set; }

    // What is currently happening to the material?
    [DataMember(IsRequired = true)] public InProcessMaterialAction Action { get; set; }
  }

  [Serializable, DataContract]
  public enum PalletLocationEnum
  {
    [EnumMember] LoadUnload,
    [EnumMember] Machine,
    [EnumMember] MachineQueue,
    [EnumMember] Buffer,
    [EnumMember] Cart
  }

  [Serializable, DataContract]
  public struct PalletLocation : IComparable
  {

    [DataMember(Name = "loc", IsRequired = true)]
    public PalletLocationEnum Location { get; set; }

    [DataMember(Name = "group", IsRequired = true)]
    public string StationGroup { get; set; }

    [DataMember(Name = "num", IsRequired = true)]
    public int Num { get; set; }

    public PalletLocation(PalletLocationEnum l, string group)
    {
      Location = l;
      StationGroup = group;
      Num = 1;
    }

    public PalletLocation(PalletLocationEnum l, string group, int n)
    {
      Location = l;
      StationGroup = group;
      Num = n;
    }

    public int CompareTo(object obj)
    {
      PalletLocation other = (PalletLocation)obj;
      int cmp = Location.CompareTo(other.Location);
      if (cmp < 0)
        return -1;
      if (cmp > 0)
        return 1;
      cmp = StationGroup.CompareTo(other.StationGroup);
      if (cmp < 0)
        return -1;
      if (cmp > 0)
        return 1;
      return Num.CompareTo(other.Num);
    }
  }

  [Serializable, DataContract]
  public class PalletStatus
  {
    [DataMember(IsRequired = true)] public string Pallet { get; set; }
    [DataMember(IsRequired = true)] public string FixtureOnPallet { get; set; }
    [DataMember(IsRequired = true)] public bool OnHold { get; set; }
    [DataMember(IsRequired = true)] public PalletLocation CurrentPalletLocation { get; set; }

    // If the pallet is at a load station and a new fixture should be loaded, this is filled in.
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public string NewFixture { get; set; }

    // num faces on new fixture, or current fixture if no change
    [DataMember(IsRequired = true)] public int NumFaces { get; set; }

    //If CurrentPalletLocation is Cart, the following two fields will be filled in.
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public PalletLocation? TargetLocation { get; set; }
    [DataMember(IsRequired = false, EmitDefaultValue = false)] public decimal? PercentMoveCompleted { get; set; }
  }

  [Serializable, DataContract]
  public class InProcessJobDecrement
  {
    [DataMember(IsRequired = true)] public long DecrementId { get; set; }
    [DataMember(IsRequired = true)] public DateTime TimeUTC { get; set; }
    [DataMember(IsRequired = true)] public int Quantity { get; set; }
  }

  [SerializableAttribute, DataContract]
  public class InProcessJob : JobPlan
  {
    public int GetCompleted(int process, int path)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        return _completed[process - 1][path - 1];
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public void SetCompleted(int process, int path, int comp)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        _completed[process - 1][path - 1] = comp;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public void AdjustCompleted(int process, int path, Func<int, int> f)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        _completed[process - 1][path - 1] = f(_completed[process - 1][path - 1]);
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }

    public long GetPrecedence(int process, int path)
    {
      if (_precedence == null) return -1;
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        return _precedence[process - 1][path - 1];
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }

    public void SetPrecedence(int process, int path, long precedence)
    {
      if (_precedence == null)
      {
        _precedence = new long[base.NumProcesses][];
        for (int proc = 1; proc <= base.NumProcesses; proc++)
        {
          _precedence[proc - 1] = Enumerable.Repeat(-1L, base.GetNumPaths(proc)).ToArray();
        }
      }
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        _precedence[process - 1][path - 1] = precedence;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }

    public IList<InProcessJobDecrement> Decrements
    {
      get
      {
        if (_decrements == null)
          _decrements = new List<InProcessJobDecrement>();
        return _decrements;
      }
    }

    public InProcessJob(string unique, int numProc, int[] numPaths = null) : base(unique, numProc, numPaths)
    {
      _completed = new int[base.NumProcesses][];
      for (int proc = 1; proc <= base.NumProcesses; proc++)
      {
        _completed[proc - 1] = Enumerable.Repeat(0, base.GetNumPaths(proc)).ToArray();
      }
    }
    public InProcessJob(JobPlan job) : base(job)
    {
      _completed = new int[base.NumProcesses][];
      for (int proc = 1; proc <= base.NumProcesses; proc++)
      {
        _completed[proc - 1] = Enumerable.Repeat(0, base.GetNumPaths(proc)).ToArray();
      }
    }

    private InProcessJob() { } //for json deserialization

    [DataMember(Name = "Completed", IsRequired = false)] private int[][] _completed;

    [DataMember(Name = "Decrements", IsRequired = false), OptionalField] private List<InProcessJobDecrement> _decrements;

    // a number reflecting the order in which the cell controller will consider the processes and paths for activation.
    // lower numbers come first, while -1 means no-data.
    [DataMember(Name = "Precedence", IsRequired = false), OptionalField] private long[][] _precedence;
  }

  [SerializableAttribute, DataContract]
  public class CurrentStatus
  {
    [DataMember(IsRequired = true)] public DateTime TimeOfCurrentStatusUTC { get; set; }
    public IDictionary<string, InProcessJob> Jobs => _jobs;
    public IDictionary<string, PalletStatus> Pallets => _pals;
    public IList<InProcessMaterial> Material => _material;
    public IList<string> Alarms => _alarms;
    public Dictionary<string, QueueSize> QueueSizes => _queues;

    public CurrentStatus(DateTime? nowUTC = null)
    {
      TimeOfCurrentStatusUTC = nowUTC ?? DateTime.UtcNow;
      _jobs = new Dictionary<string, InProcessJob>();
      _pals = new Dictionary<string, PalletStatus>();
      _material = new List<InProcessMaterial>();
      _alarms = new List<string>();
      _queues = new Dictionary<string, QueueSize>();
    }

    [DataMember(Name = "Jobs", IsRequired = true)]
    private Dictionary<string, InProcessJob> _jobs;

    [DataMember(Name = "Pallets", IsRequired = true)]
    private Dictionary<string, PalletStatus> _pals;

    [DataMember(Name = "Material", IsRequired = true)]
    private List<InProcessMaterial> _material;

    [DataMember(Name = "Alarms", IsRequired = true)]
    private List<string> _alarms;

    [DataMember(Name = "Queues", IsRequired = true)]
    private Dictionary<string, QueueSize> _queues;
  }

  [Serializable, DataContract]
  public struct JobAndDecrementQuantity
  {
    [DataMember(IsRequired = true)] public long DecrementId { get; set; }
    [DataMember(IsRequired = true)] public string JobUnique { get; set; }
    [DataMember(IsRequired = true)] public DateTime TimeUTC { get; set; }
    [DataMember(IsRequired = true)] public string Part { get; set; }
    [DataMember(IsRequired = true)] public int Quantity { get; set; }
  }

  // The following is only used for old decrement for backwards compatibility,
  // and shouldn't be used for anything else.
  [Serializable]
  public class JobAndPath : IEquatable<JobAndPath>
  {
    public readonly string UniqueStr;
    public readonly int Path;

    public JobAndPath(string unique, int path)
    {
      UniqueStr = unique;
      Path = path;
    }

    public bool Equals(JobAndPath other)
    {
      return (UniqueStr == other.UniqueStr && Path == other.Path);
    }
  }
}