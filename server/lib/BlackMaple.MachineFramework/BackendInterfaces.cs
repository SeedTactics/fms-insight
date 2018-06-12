/* Copyright (c) 2017, John Lenz

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

using System.Collections.Generic;
using System.Runtime.Serialization;
using BlackMaple.MachineWatchInterface;

namespace BlackMaple.MachineFramework
{
    public interface IConfig
    {
        T GetValue<T>(string section, string key);
    }

    public interface IFMSBackend
    {
        void Init(string dataDirectory, IConfig config, FMSSettings settings);
        void Halt();

        //Trace listeners are registered before Init is called
        //so that errors during Init can be recorded.
        IEnumerable<System.Diagnostics.TraceSource> TraceSources();

        ILogDatabase LogDatabase();
        IJobDatabase JobDatabase();
        IJobControl JobControl();
        IInspectionControl InspectionControl();

        IOldJobDecrement OldJobDecrement();
    }

    public interface IBackgroundWorker
    {
        //The init function should initialize a timer or spawn a thread and then return
        void Init(IFMSBackend backend, string dataDirectory, IConfig config, FMSSettings settings);

        //Once the halt function is called, the class is garbage collected.
        //A new class will be created when the service starts again
        void Halt();

        System.Diagnostics.TraceSource TraceSource { get; }
    }

    [DataContract]
    public class FMSInfo
    {
        [DataMember] public string Name {get;set;}
        [DataMember] public string Version {get;set;}
    }

    public interface IFMSImplementation
    {
        FMSInfo Info {get;}
        IFMSBackend Backend {get;}
        IList<IBackgroundWorker> Workers {get;}
    }

}
