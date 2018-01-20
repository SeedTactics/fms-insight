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
using System.Runtime.Serialization;
using BlackMaple.MachineWatchInterface;
using BlackMaple.MachineFramework;
using Microsoft.Extensions.Configuration;
using System.Runtime.Loader;
using System.Reflection;
using Microsoft.Extensions.DependencyModel;
using System.IO;
using System.Linq;

namespace MachineWatchApiServer
{
    [DataContract]
    public struct PluginInfo
    {
        [DataMember] public string Name {get;set;}
        [DataMember] public string Version {get;set;}
    }

    public class MockBackend : IServerBackend, IJobControl
    {
        public JobLogDB LogDB {get;private set;}
        public JobDB JobDB {get; private set;}
        public InspectionDB InspectionDB {get; private set;}

        public void Init(string path)
        {
            string dbFile(string f) => System.IO.Path.Combine(path, f + ".db");

            if (path != null)
            {
                if (System.IO.File.Exists(dbFile("log"))) System.IO.File.Delete(dbFile("log"));
                LogDB = new JobLogDB();
                LogDB.Open(dbFile("log"));

                if (System.IO.File.Exists(dbFile("insp"))) System.IO.File.Delete(dbFile("insp"));
                InspectionDB = new InspectionDB(LogDB);
                InspectionDB.Open(dbFile("insp"));

                if (System.IO.File.Exists(dbFile("job"))) System.IO.File.Delete(dbFile("job"));
                JobDB = new JobDB();
                JobDB.Open(dbFile("job"));
            }
            else
            {
                var conn = SqliteExtensions.ConnectMemory();
                conn.Open();
                LogDB = new JobLogDB(conn);
                LogDB.CreateTables();

                conn = SqliteExtensions.ConnectMemory();
                conn.Open();
                InspectionDB = new InspectionDB(LogDB, conn);
                InspectionDB.CreateTables();

                conn = SqliteExtensions.ConnectMemory();
                conn.Open();
                JobDB = new JobDB(conn);
                JobDB.CreateTables();
            }
        }

        public IEnumerable<System.Diagnostics.TraceSource> TraceSources()
        {
            return new System.Diagnostics.TraceSource[] {};
        }

        public void Halt()
        {
            JobDB.Close();
            InspectionDB.Close();
            LogDB.Close();
        }

        public IInspectionControl InspectionControl()
        {
            return InspectionDB;
        }

        public IJobControl JobControl()
        {
            return this;
        }

        public ILogDatabase LogDatabase()
        {
            return LogDB;
        }

        public IJobDatabase JobDatabase()
        {
            return JobDB;
        }

        public CurrentStatus GetCurrentStatus()
        {
            return new CurrentStatus(
                new JobCurrentInformation[] {},
                new Dictionary<string, PalletStatus>(),
                "",
                new Dictionary<string, int>()
            );
        }

        public List<string> CheckValidRoutes(IEnumerable<JobPlan> newJobs)
        {
            return new List<string>();
        }

        public void AddJobs(NewJobs jobs, string expectedPreviousScheduleId)
        {
            JobDB.AddJobs(jobs, expectedPreviousScheduleId);
        }

        public List<JobAndDecrementQuantity> DecrementJobQuantites(string loadDecrementsStrictlyAfterDecrementId)
        {
            throw new NotImplementedException();
        }

        public List<JobAndDecrementQuantity> DecrementJobQuantites(DateTime loadDecrementsAfterTimeUTC)
        {
            throw new NotImplementedException();
        }

        public IOldJobDecrement OldJobDecrement()
        {
            throw new NotImplementedException();
        }
    }

    public class Plugin : AssemblyLoadContext
    {
        public PluginInfo PluginInfo {get; private set;}
        public string SettingsPath {get;private set;}
        public IServerBackend Backend {get;private set;}

        private string depPath;

        public Plugin(string settingsPath)
        {
            SettingsPath = settingsPath;
            Backend = new MockBackend();
            PluginInfo = new PluginInfo() {
                Name = Assembly.GetExecutingAssembly().GetName().Name,
                Version = "1.2.3.4"
            };
        }

        public Plugin(string settingsPath, string pluginFile)
        {
            SettingsPath = settingsPath;
            depPath = Path.GetDirectoryName(pluginFile);

            var a = LoadFromAssemblyPath(Path.GetFullPath(pluginFile));
            foreach (var t in a.GetTypes())
            {
                foreach (var i in t.GetInterfaces())
                {
                    if (i == typeof(IServerBackend))
                    {
                        Backend = (IServerBackend) Activator.CreateInstance(t);
                        PluginInfo = new PluginInfo() {
                            Name = a.GetName().Name,
                            Version = System.Diagnostics.FileVersionInfo.GetVersionInfo(a.Location).ToString()
                        };
                        return;
                    }
                }
            }
            throw new Exception("Plugin does not contain implementation of IServerBackend");
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            var deps = DependencyContext.Default;
            var compileLibs = deps.CompileLibraries.Where(d => d.Name.Contains(assemblyName.Name));
            if (compileLibs.Any())
            {
                return Assembly.Load(new AssemblyName(compileLibs.First().Name));
            }

            var depFullPath = Path.Combine(depPath, assemblyName.Name + ".dll");
            if (File.Exists(depFullPath))
            {
                return LoadFromAssemblyPath(depFullPath);
            }

            return Assembly.Load(assemblyName);
        }
    }

}