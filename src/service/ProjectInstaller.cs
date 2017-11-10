using System;
using System.Collections;
using System.Diagnostics;
using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;

namespace BlackMaple.MachineWatch
{
	[RunInstaller(true)]
	public class ProjectInstaller : System.Configuration.Install.Installer
	{
      public ProjectInstaller() : base()
      {
          var procInst = new System.ServiceProcess.ServiceProcessInstaller();
          procInst.Account = System.ServiceProcess.ServiceAccount.LocalSystem;
          procInst.Password = null;
          procInst.Username = null;
          this.Installers.Add(procInst);

          var serviceInst = new System.ServiceProcess.ServiceInstaller();
          serviceInst.ServiceName = "Machine Watch";
          serviceInst.Description = "Service to monitor the cell controller and log activity";
          serviceInst.StartType = System.ServiceProcess.ServiceStartMode.Automatic;
          this.Installers.Add(serviceInst);
      }

      private static bool IsInstalled()
      {
          using (ServiceController controller = new ServiceController("Machine Watch")) {
            try {
              ServiceControllerStatus status = controller.Status;
            } catch {
              return false;
            }
            return true;
          }
      }

      private static AssemblyInstaller GetInstaller()
      {
          AssemblyInstaller installer = new AssemblyInstaller(
              typeof(ProjectInstaller).Assembly, null);
          installer.UseNewContext = true;
          return installer;
      }

      public static void InstallService()
      {
          if (IsInstalled()) return;
          using (AssemblyInstaller installer = GetInstaller()) {
              IDictionary state = new Hashtable();
              try {
                  installer.Install(state);
                  installer.Commit(state);
              } catch {
                  try {
                      installer.Rollback(state);
                  } catch { }
                  throw;
              }
          }
      }

      public static void UninstallService()
      {
          if (!IsInstalled() ) return;
          using ( AssemblyInstaller installer = GetInstaller() ) {
              IDictionary state = new Hashtable();
              try {
                  installer.Uninstall( state );
              } catch {
                  throw;
              }
          }
      }

    public static void StartService()
    {
        if ( !IsInstalled() ) return;

        using (ServiceController controller = new ServiceController("Machine Watch")) {
            if ( controller.Status != ServiceControllerStatus.Running ) {
                controller.Start();
                controller.WaitForStatus( ServiceControllerStatus.Running,
                    TimeSpan.FromSeconds( 10 ) );
            }
        }
    }

    public static void StopService()
    {
        if ( !IsInstalled() ) return;
        using ( ServiceController controller = new ServiceController("Machine Watch")) {
            if ( controller.Status != ServiceControllerStatus.Stopped ) {
                controller.Stop();
                controller.WaitForStatus( ServiceControllerStatus.Stopped,
                    TimeSpan.FromSeconds( 10 ) );
            }
        }
    }
	}
}
