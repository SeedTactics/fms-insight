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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;
using System.Runtime.Remoting;

namespace BlackMaple.MachineWatch
{
    public class Server : IDisposable
    {
        private readonly MachineWatchPlugin plugin;
        private readonly SettingStore settingsServer;
        private readonly Tracing trace;
        private readonly RemoteSingletons singletons;

        private class MachineWatchVersion : IMachineWatchVersion
        {
            private string _ver;
            private string _plugin;
            public MachineWatchVersion(System.Reflection.AssemblyName n, System.Diagnostics.FileVersionInfo v)
            {
                _ver = v.ProductVersion;
                _plugin = n.Name;
            }
            public string Version() { return _ver; }
            public string PluginName() { return _plugin; }
        }

        public class MachineWatchPlugin
        {
            public IServerBackend serverBackend { get; }
            public IMachineWatchVersion serverVersion { get; }
            public IEnumerable<IBackgroundWorker> workers { get; }

            public MachineWatchPlugin(IServerBackend b, System.Reflection.AssemblyName n, System.Diagnostics.FileVersionInfo v, IEnumerable<IBackgroundWorker> ws)
            {
                serverBackend = b;
                serverVersion = new MachineWatchVersion(n, v);
                workers = ws;
            }
        }

        public Server(MachineWatchPlugin p, bool forceTrace = false)
        {
            plugin = p;

            string logPath, dataDir;

            var commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            //check old cms research data directory
            dataDir = System.IO.Path.Combine(
                commonData, System.IO.Path.Combine("CMS Research", "MachineWatch"));
            if (!System.IO.Directory.Exists(dataDir))
            {
                //use new seedtactics directory
                dataDir = System.IO.Path.Combine(
                    commonData, System.IO.Path.Combine("SeedTactics", "MachineWatch"));
                if (!System.IO.Directory.Exists(dataDir))
                    System.IO.Directory.CreateDirectory(dataDir);

            }

            logPath = System.IO.Path.Combine(
                dataDir, typeof(Server).Assembly.GetName().Version.ToString());
            if (!System.IO.Directory.Exists(logPath))
                System.IO.Directory.CreateDirectory(logPath);

            try
            {
                var eventTrace = new TraceSource("EventServer", SourceLevels.All);
                trace = new Tracing(logPath, plugin?.serverBackend, eventTrace, plugin?.workers, forceTrace);

                if (plugin == null)
                {
                    trace.machineTrace.TraceEvent(TraceEventType.Error, 0, "Unable to find machine watch backend");
                    return;
                }

                settingsServer = new SettingStore(dataDir);

                //Configure .NET Remoting
                if (System.IO.File.Exists(AppDomain.CurrentDomain.SetupInformation.ConfigurationFile))
                {
                    RemotingConfiguration.Configure(AppDomain.CurrentDomain.SetupInformation.ConfigurationFile, false);
                }
                if (System.Runtime.Remoting.Channels.ChannelServices.RegisteredChannels.Count() == 0)
                {
                    var clientFormatter = new System.Runtime.Remoting.Channels.BinaryClientFormatterSinkProvider();
                    var serverFormatter = new System.Runtime.Remoting.Channels.BinaryServerFormatterSinkProvider();
                    serverFormatter.TypeFilterLevel = System.Runtime.Serialization.Formatters.TypeFilterLevel.Full;
                    var props = new System.Collections.Hashtable();
                    props["port"] = 8086;
                    System.Runtime.Remoting.Channels.ChannelServices.RegisterChannel(new System.Runtime.Remoting.Channels.Tcp.TcpChannel(props, clientFormatter, serverFormatter), false);
                
                    System.Runtime.Remoting.Lifetime.LifetimeServices.LeaseTime = TimeSpan.FromMinutes(3);
                    System.Runtime.Remoting.Lifetime.LifetimeServices.SponsorshipTimeout = TimeSpan.FromMinutes(2);
                    System.Runtime.Remoting.Lifetime.LifetimeServices.RenewOnCallTime = TimeSpan.FromMinutes(1);
                    System.Runtime.Remoting.Lifetime.LifetimeServices.LeaseManagerPollTime = TimeSpan.FromSeconds(10);
                    System.Runtime.Remoting.RemotingConfiguration.CustomErrorsMode = System.Runtime.Remoting.CustomErrorsModes.Off;
                }

                plugin.serverBackend.Init(dataDir);

                foreach (IBackgroundWorker w in plugin.workers)
                    w.Init(plugin.serverBackend);

                var jobDb = plugin.serverBackend.JobDatabase();
                var logDb = plugin.serverBackend.LogDatabase();
                var inspServer = plugin.serverBackend.InspectionControl();
                var jobControl = plugin.serverBackend.JobControl();
                var oldJob = plugin.serverBackend.OldJobDecrement();

                singletons = new RemoteSingletons();

                singletons.RemoteSingleton(typeof(IJobDatabase),
                                           "JobDB",
                                           jobDb);
                singletons.RemoteSingleton(typeof(ILogDatabase),
                                           "LogDB",
                                           logDb);
                singletons.RemoteSingleton(typeof(IInspectionControl),
                                           "InspectionControl",
                                           inspServer);
                singletons.RemoteSingleton(typeof(IJobControl),
                                           "JobControl",
                                           jobControl);
                singletons.RemoteSingleton(typeof(IOldJobDecrement),
                                           "OldJobDecrement",
                                           oldJob);
                singletons.RemoteSingleton(typeof(IMachineWatchVersion),
                           "Version",
                           plugin.serverVersion);
                singletons.RemoteSingleton(typeof(IStoreSettings),
                            "Settings",
                            settingsServer);
            }
            catch (Exception ex)
            {
                trace.machineTrace.TraceEvent(TraceEventType.Error, 0, "Error starting machine watch" + Environment.NewLine + ex.ToString());
            }
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            if (!disposedValue)
            {
                try
                {
                    foreach (IBackgroundWorker w in plugin.workers)
                        w.Halt();

                    if (plugin.serverBackend != null)
                        plugin.serverBackend.Halt();
                }
                catch
                {
                }
                finally
                {
                    if (singletons != null) singletons.Disconnect();
                    trace.Dispose();
                }

                GC.Collect();
                disposedValue = true;
            }
        }
        #endregion
    }
}
