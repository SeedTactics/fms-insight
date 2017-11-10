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

namespace BlackMaple.MachineWatch
{

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
        private Server _server;

        private Server.MachineWatchPlugin LoadPlugin()
        {
            //try and load all the plugins we can find		
            List<string> assemblies;
			string PluginDir = IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");			
			assemblies = new List<string>(IO.Directory.GetFiles(PluginDir, "*.dll"));

            IServerBackend serverBackend = null;
            var workers = new List<IBackgroundWorker>();
            System.Reflection.AssemblyName assName = null;

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
                        }

                        if (iface.Equals(typeof(IBackgroundWorker)))
                        {
                            workers.Add((IBackgroundWorker)Activator.CreateInstance(t));
                        }
                    }
                }
            }

            return serverBackend == null ? null :
                new Server.MachineWatchPlugin(serverBackend, assName, workers);
        }

        protected override void OnStart(string[] args)
        {
            _server = new Server(LoadPlugin());
        }

        protected override void OnStop()
        {
            if (_server != null) _server.Dispose();
            _server = null;
        }
        #endregion
    }
}
