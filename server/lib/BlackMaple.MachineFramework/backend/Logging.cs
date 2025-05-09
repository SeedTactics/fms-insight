/* Copyright (c) 2023, John Lenz

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
using Serilog;
using Serilog.Events;

namespace BlackMaple.MachineFramework
{
  public static class InsightLogging
  {
    public class CompressSerilogDebugLog : Serilog.Sinks.File.FileLifecycleHooks
    {
      public override void OnFileDeleting(string origTxtFile)
      {
        try
        {
          var newGzFile = Path.Combine(
            Path.GetDirectoryName(origTxtFile) ?? ".",
            Path.GetFileName(origTxtFile) + ".gz"
          );

          using (
            var sourceStream = new FileStream(origTxtFile, FileMode.Open, FileAccess.Read, FileShare.Read)
          )
          using (
            var targetStream = new FileStream(
              newGzFile,
              FileMode.OpenOrCreate,
              FileAccess.Write,
              FileShare.None
            )
          )
          using (
            var compressStream = new System.IO.Compression.GZipStream(
              targetStream,
              System.IO.Compression.CompressionLevel.Optimal
            )
          )
          {
            sourceStream.CopyTo(compressStream);
          }

          // delete after 30 days
          var rx = new System.Text.RegularExpressions.Regex(@"fmsinsight-debug(\d+)\.txt\.gz");
          foreach (
            var existingGzFile in Directory.GetFiles(Path.GetDirectoryName(origTxtFile) ?? ".", "*.gz")
          )
          {
            var m = rx.Match(Path.GetFileName(existingGzFile));
            if (m != null && m.Success && m.Groups.Count >= 2)
            {
              if (
                DateTime.TryParseExact(
                  m.Groups[1].Value,
                  "yyyyMMdd",
                  System.Globalization.CultureInfo.InvariantCulture,
                  System.Globalization.DateTimeStyles.None,
                  out var d
                )
              )
              {
                if (DateTime.Today.Subtract(d) > TimeSpan.FromDays(30))
                {
                  System.IO.File.Delete(existingGzFile);
                }
              }
            }
          }
        }
        catch (Exception ex)
        {
          Serilog.Debugging.SelfLog.WriteLine(
            "Error while archiving debug log " + origTxtFile + ": " + ex.ToString()
          );
        }
      }
    }

    private static Serilog.Core.LoggingLevelSwitch _logLevel { get; } =
      new Serilog.Core.LoggingLevelSwitch(Serilog.Events.LogEventLevel.Information);

    public static void EnableSerilog(ServerSettings serverSt, bool enableEventLog)
    {
      if (serverSt.EnableDebugLog)
      {
        _logLevel.MinimumLevel = Serilog.Events.LogEventLevel.Debug;
      }

      var logConfig = new LoggerConfiguration()
        .MinimumLevel.ControlledBy(_logLevel)
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .Destructure.ToMaximumDepth(7)
        .WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information);

      if (
        enableEventLog
        && System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
          System.Runtime.InteropServices.OSPlatform.Windows
        )
      )
      {
        logConfig = logConfig.WriteTo.EventLog(
          "FMS Insight",
          manageEventSource: true,
          restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information
        );
      }

      if (serverSt.EnableDebugLog)
      {
        logConfig = logConfig.WriteTo.File(
          new Serilog.Formatting.Compact.CompactJsonFormatter(),
          Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SeedTactics",
            "FMSInsight",
            "fmsinsight-debug.txt"
          ),
          rollingInterval: RollingInterval.Day,
          hooks: new InsightLogging.CompressSerilogDebugLog(),
          restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Verbose,
          retainedFileCountLimit: 3
        );
      }

      Log.Logger = logConfig.CreateLogger();
    }

    private static System.Threading.Lock _verboseLoggingLock = new();
    private static Serilog.Events.LogEventLevel _oldLogLevel = Serilog.Events.LogEventLevel.Information;
    private static DateTime? _timeToReenableOldLogLevel = null;

    public static void EnableVerboseLoggingFor(TimeSpan duration)
    {
      lock (_verboseLoggingLock)
      {
        if (_timeToReenableOldLogLevel.HasValue)
        {
          // task is currently running, user wants to extend
          _timeToReenableOldLogLevel = DateTime.UtcNow + duration;
          Serilog.Log.Debug("Adding {duration} to verbose logging", duration);
        }
        else
        {
          // start a new task
          _oldLogLevel = _logLevel.MinimumLevel;
          _timeToReenableOldLogLevel = DateTime.UtcNow + duration;

          _logLevel.MinimumLevel = Serilog.Events.LogEventLevel.Verbose;
          Serilog.Log.Debug("Enabling verbose logging for {duration}", duration);

          System.Threading.Tasks.Task.Run(async () =>
          {
            while (true)
            {
              await System.Threading.Tasks.Task.Delay(_timeToReenableOldLogLevel.Value - DateTime.UtcNow);

              lock (_verboseLoggingLock)
              {
                if (DateTime.UtcNow.AddSeconds(5) >= _timeToReenableOldLogLevel.Value)
                {
                  Serilog.Log.Debug("Returning to logging level {level}", _oldLogLevel);
                  _logLevel.MinimumLevel = _oldLogLevel;
                  _timeToReenableOldLogLevel = null;
                  break;
                }
              }
            }
          });
        }
      }
    }
  }
}
