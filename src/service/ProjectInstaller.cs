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
