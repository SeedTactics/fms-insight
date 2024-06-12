/* Copyright (c) 2024, John Lenz

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
using System.IO;
using BlackMaple.MachineFramework;
using Microsoft.Extensions.Configuration;

namespace BlackMaple.FMSInsight.Makino
{
  public sealed class MakinoBackend : IFMSBackend, IDisposable
  {
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<MakinoBackend>();

    public RepositoryConfig? RepoConfig { get; }
    public IJobAndQueueControl? JobControl => _jobs;
    public IMachineControl? MachineControl { get; }

    private readonly JobsAndQueuesFromDb<MakinoCellState>? _jobs;
    private readonly MakinoSync? _sync;

    public MakinoBackend(IConfiguration config, FMSSettings st, SerialSettings serialSt)
    {
      try
      {
        var settings = new MakinoSettings(config, st);

        if (File.Exists(Path.Combine(st.DataDirectory, "log.db")))
        {
          File.Move(Path.Combine(st.DataDirectory, "log.db"), Path.Combine(st.DataDirectory, "makino.db"));
        }

        RepoConfig = RepositoryConfig.InitializeEventDatabase(
          serialSt,
          Path.Combine(st.DataDirectory, "makino.db"),
          Path.Combine(st.DataDirectory, "inspections.db"),
          Path.Combine(st.DataDirectory, "jobs.db")
        );

        MachineControl = new MakinoMachines(settings);

        _sync = new MakinoSync(settings);

        _jobs = new JobsAndQueuesFromDb<MakinoCellState>(RepoConfig, st, _sync);
        _jobs.StartThread();

        try
        {
          using var db = settings.OpenMakinoConnection();
          db.CheckForQueryNotification();
        }
        catch (Exception ex)
        {
          Log.Error(ex, "Error when checking query notifaction");
        }
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error when initializing makino backend");
      }
    }

    void IDisposable.Dispose()
    {
      _jobs?.Dispose();
    }
  }
}
