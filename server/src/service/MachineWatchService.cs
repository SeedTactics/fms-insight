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

using System;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.ServiceProcess;
using System.Runtime.Remoting;
using IO = System.IO;
using BlackMaple.MachineWatchInterface;
using BlackMaple.MachineFramework;
using Serilog;

namespace BlackMaple.MachineWatch
{
    public class ConfigWrapper : IConfig
    {
        public T GetValue<T>(string section, string key)
        {
            string val = System.Configuration.ConfigurationManager.AppSettings[section + " " + key];
            if (string.IsNullOrEmpty(val)) {
                // try without section (for backwards compatability)
                val = System.Configuration.ConfigurationManager.AppSettings[key];
            }
            var converter = System.ComponentModel.TypeDescriptor.GetConverter(typeof(T));
            if (converter.CanConvertFrom(typeof(string))) {
                try {
                    return (T)converter.ConvertFromInvariantString(val);
                } catch {
                    return default(T);
                }
            } else {
                throw new Exception("Unable to convert string to " + typeof(T).ToString());
            }
        }
    }

    public class MachineWatchService : System.ServiceProcess.ServiceBase
    {
        public MachineWatchService() : base()
        {
            this.ServiceName = "Machine Watch";
        }

        #region Main Function
        // The main entry point for the process
        public static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                System.ServiceProcess.ServiceBase[] ServicesToRun = null;
                ServicesToRun = new System.ServiceProcess.ServiceBase[] { new MachineWatchService() };
                System.ServiceProcess.ServiceBase.Run(ServicesToRun);
            }
            else
            {
                switch (args[0])
                {
                    case "-install":
                        ProjectInstaller.InstallService();
                        ProjectInstaller.StartService();
                        break;
                    case "-uninstall":
                        ProjectInstaller.StopService();
                        ProjectInstaller.UninstallService();
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
        }
        #endregion

        #region Starting And Stopping
        private RemotingServer _server;
        private Tracing _trace;

        private static RemotingServer.MachineWatchPlugin LoadPlugin()
        {
            //try and load all the plugins we can find
            List<string> assemblies;
			string PluginDir = IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
			assemblies = new List<string>(IO.Directory.GetFiles(PluginDir, "*.dll"));

            IServerBackend serverBackend = null;
            var workers = new List<IBackgroundWorker>();
            System.Reflection.AssemblyName assName = null;
            System.Diagnostics.FileVersionInfo assFileVer = null;

            foreach (string ass in assemblies)
            {
                System.Reflection.Assembly asm = System.Reflection.Assembly.LoadFrom(ass);

                foreach (var t in asm.GetTypes())
                {
                    foreach (var iface in t.GetInterfaces())
                    {

                        if (serverBackend == null && iface.Equals(typeof(IServerBackend)))
                        {
                            serverBackend = (IServerBackend)Activator.CreateInstance(t);
                            assName = asm.GetName();
                            assFileVer = System.Diagnostics.FileVersionInfo.GetVersionInfo(asm.Location);
                        }

                        if (iface.Equals(typeof(IBackgroundWorker)))
                        {
                            workers.Add((IBackgroundWorker)Activator.CreateInstance(t));
                        }
                    }
                }
            }

            return serverBackend == null ? null :
                new RemotingServer.MachineWatchPlugin(serverBackend, assName, assFileVer, workers);
        }

        private static string CalculateDataDir()
        {
            var commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            //check old cms research data directory
            var dataDir = System.IO.Path.Combine(
                commonData, System.IO.Path.Combine("CMS Research", "MachineWatch"));
            if (!System.IO.Directory.Exists(dataDir))
            {
                //use new seedtactics directory
                dataDir = System.IO.Path.Combine(
                    commonData, System.IO.Path.Combine("SeedTactics", "MachineWatch"));
                if (!System.IO.Directory.Exists(dataDir))
                    System.IO.Directory.CreateDirectory(dataDir);

            }
            return dataDir;
        }

        protected override void OnStart(string[] args)
        {
            var dataDir = CalculateDataDir();

            var logPath = System.IO.Path.Combine(
                dataDir, typeof(RemotingServer).Assembly.GetName().Version.ToString());
            if (!System.IO.Directory.Exists(logPath))
                System.IO.Directory.CreateDirectory(logPath);

            _trace = new Tracing(logPath, forceTrace:false);

            var logConfig = new LoggerConfiguration();

            if (_trace.EnableTracing)
            {
                logConfig = logConfig.MinimumLevel.Debug();
            } else {
                logConfig = logConfig.MinimumLevel.Information();
            }

            Log.Logger = logConfig
                .WriteTo.File(System.IO.Path.Combine(logPath, "events.txt"), rollingInterval: RollingInterval.Month)
                .CreateLogger();

            try {

                var plugin = LoadPlugin();

                if (plugin == null)
                {
                    throw new Exception("Unable to find Machine Watch plugin");
                }

                _trace.AddSources(plugin.serverBackend, plugin.workers);

			    string serialPerMaterial = System.Configuration.ConfigurationManager.AppSettings["Assign Serial Per Material"];
                var SerialSettings = new SerialSettings() {
                    SerialType = SerialType.NoAutomaticSerials,
                    SerialLength = 10,
                };
                if (!string.IsNullOrEmpty (serialPerMaterial)) {
                    bool result;
                    if (bool.TryParse(serialPerMaterial, out result)) {
                        if (result)
                            SerialSettings.SerialType = SerialType.AssignOneSerialPerMaterial;
                        else
                            SerialSettings.SerialType = SerialType.AssignOneSerialPerCycle;
                    }
                }

                plugin.serverBackend.Init(dataDir, new ConfigWrapper(), SerialSettings);
                foreach (IBackgroundWorker w in plugin.workers)
                    w.Init(plugin.serverBackend);

                // net461 on linux can't reference MachineFramework built using netstandard, and I don't
                // yet have the latest mono which supports net471 installed yet.  Once net471 mono is installed
                // the following hack can be removed.
                IStoreSettings settings = null;
                #if USE_TRACE
                settings = new MachineFramework.SettingStore(dataDir);
                #endif

                _server = new RemotingServer(plugin, dataDir, settings);

            } catch (Exception ex) {
                _trace.machineTrace.TraceEvent(TraceEventType.Error, 0, ex.ToString());
                throw ex;
            }
        }

        protected override void OnStop()
        {
            if (_server != null)
            {
                _server.Dispose();
                try {
                    foreach (IBackgroundWorker w in _server.plugin.workers)
                        w.Halt();

                    if (_server.plugin.serverBackend != null)
                        _server.plugin.serverBackend.Halt();
                }
                catch
                {
                    //do nothing
                }
                _trace.Dispose();
            }
            _server = null;
        }
        #endregion
    }
}
