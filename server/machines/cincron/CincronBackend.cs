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
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;

namespace Cincron
{
  public class CincronBackend : IFMSBackend, IDisposable
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<CincronBackend>();

    private JobLogDB _log;
    private MessageWatcher _msgWatcher;

    public CincronBackend()
    {
      try
      {

        string msgFile;
#if DEBUG
        var path = System.Reflection.Assembly.GetExecutingAssembly().Location;
        msgFile = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(path), "..\\..\\..\\test\\Cincron\\parker-example-messages");
#else
                msgFile = cfg.GetValue<string>("Cincron", "Message File");
#endif

        Log.Information("Starting cincron backend with message file {file}", msgFile);

        if (!System.IO.File.Exists(msgFile))
        {
          Log.Error("Message file {file} does not exist", msgFile);
        }

        _log = new JobLogDB();

        _log.Open(
            System.IO.Path.Combine(Program.ServerSettings.DataDirectory, "log.db"),
            firstSerialOnEmpty: Program.FMSSettings.StartingSerial
        );
        _msgWatcher = new MessageWatcher(msgFile, _log);
        _msgWatcher.Start();

      }
      catch (Exception ex)
      {
        Log.Error(ex, "Unhandled exception when initializing cincron backend");
      }
    }

    public void Dispose()
    {
      if (_msgWatcher != null) _msgWatcher.Halt();
      if (_log != null) _log.Close();
      _msgWatcher = null;
      _log = null;
    }

    public IInspectionControl InspectionControl()
    {
      return _log;
    }

    public IJobDatabase JobDatabase()
    {
      return null;
    }

    public IOldJobDecrement OldJobDecrement()
    {
      return null;
    }

    public IJobControl JobControl()
    {
      return null;
    }

    public ILogDatabase LogDatabase()
    {
      return _log;
    }
  }

  public static class CincronProgram
  {
    public static void Main()
    {
#if DEBUG
      var useService = false;
#else
            var useService = true;
#endif
      Program.Run(useService, () =>
        new FMSImplementation()
        {
          Backend = new CincronBackend(),
          NameAndVersion = new FMSNameAndVersion()
          {
            Name = "Cincron",
            Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),
          }
        });
    }
  }
}
