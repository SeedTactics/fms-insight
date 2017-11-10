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
