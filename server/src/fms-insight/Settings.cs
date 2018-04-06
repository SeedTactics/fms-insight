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

using Microsoft.Extensions.Configuration;

namespace MachineWatchApiServer {

    public class ServerSettings
    {
      public string DataDirectory {get;set;} = null;
      public bool EnableDebugLog {get;set;} = false;
      public bool IPv6 {get;set;} = true;
      public int Port {get;set;} = 5000;
      public string TLSCertFile {get;set;} = null;

      static public ServerSettings Load(IConfiguration config)
      {
        var s = config.GetSection("SERVER").Get<ServerSettings>();
        if (s == null)
            s = new ServerSettings();

        if (string.IsNullOrEmpty(s.DataDirectory)) {
          s.DataDirectory = CalculateDataDir();
        }
        return s;
      }

      private static string CalculateDataDir()
      {
        #if USE_SERVICE
          var commonData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData);

          //check old cms research data directory
          var dataDir = System.IO.Path.Combine(commonData, "CMS Research", "MachineWatch");
          if (System.IO.Directory.Exists(dataDir))
            return dataDir;

          //try new seedtactics directory
          dataDir = System.IO.Path.Combine(commonData, "SeedTactics", "MachineWatch");
          if (System.IO.Directory.Exists(dataDir))
            return dataDir;

          //now new seedtactics directory
          dataDir = System.IO.Path.Combine(commonData, "SeedTactics", "FMSInsight");
          if (!System.IO.Directory.Exists(dataDir))
            System.IO.Directory.CreateDirectory(dataDir);
          return dataDir;

        #else
          return System.IO.Directory.GetCurrentDirectory();
        #endif
      }
    }

    public enum WorkorderAssignmentType
    {
      AssignWorkorderAtUnload,
      AssignWorkorderAtWash,
      NoAutomaticWorkorderAssignment,
    }

    public class FMSSettings
    {
      public string PluginFile {get;set;} = null;
      public bool AutomaticSerials {get;set;} = false;
      public int SerialLength {get;set;} = 10;
      public WorkorderAssignmentType WorkorderAssignment {get;set;} = WorkorderAssignmentType.NoAutomaticWorkorderAssignment;

      static public FMSSettings Load(IConfiguration config)
      {
        var s = config.GetSection("FMS").Get<FMSSettings>();
        if (s == null)
            s = new FMSSettings();
        return s;
      }
    }

}