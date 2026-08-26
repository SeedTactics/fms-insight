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

// A few methods with the same API as Serilog but implemented on .NET 3.5

using System;
using System.Diagnostics;

namespace Serilog
{
  public interface ILogger
  {
    public void Error(string message);
    public void Information(string message);
    public void Debug(string messageTemplate, params object[] propertyValues);
    public void Verbose(string messageTemplate, params object[] propertyValues);
  }

  public static class EventLogConfig
  {
    public const string SourceName = "FMS Insight Mazak Proxy";
    public const string LogName = "Application";
  }

  public class Log : ILogger
  {
    private static readonly object DebugFileLock = new object();
    private static readonly object EventLogLock = new object();
    private static DateTime _lastLogMaintenanceDate = DateTime.MinValue;

    public static ILogger ForContext<T>()
    {
      return new Log();
    }

    public static void Error(string message)
    {
      Error(null, message);
    }

    void ILogger.Error(string message)
    {
      Log.Error(message);
    }

    public static void Error(Exception ex, string message)
    {
      string msg = message;
      if (ex != null)
        msg += Environment.NewLine + ex;
      TryWriteEventLog(msg, EventLogEntryType.Error);

      // Also to debug
      Debug(ex, message);
    }

    void ILogger.Information(string message)
    {
      Log.Information(message);
    }

    public static void Information(string message)
    {
      TryWriteEventLog(message, EventLogEntryType.Information);

      // Also to debug
      Debug(message);
    }

    public static void Debug(string messageTemplate, params object[] propertyValues)
    {
      Debug(null, messageTemplate, propertyValues);
    }

    void ILogger.Debug(string messageTemplate, params object[] propertyValues)
    {
      Log.Debug(messageTemplate, propertyValues);
    }

    void ILogger.Verbose(string messageTemplate, params object[] propertyValues)
    {
      Log.Verbose(messageTemplate, propertyValues);
    }

    public static void Verbose(string messageTemplate, params object[] propertyValues)
    {
      // Per-row parsing details are intentionally disabled in the always-on proxy log. Request
      // boundaries and aggregate scan results retain the operational chronology without allowing
      // ordinary Mazak CSV traffic to dominate disk usage.
    }

    public class DebugMessage
    {
      public DateTime UtcNow { get; set; }
      public string Message { get; set; }
      public string Exception { get; set; }
      public object[] Properties { get; set; }
    }

    public static void Debug(Exception ex, string messageTemplate, params object[] propertyValues)
    {
      try
      {
        lock (DebugFileLock)
        {
          var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "FMS Insight Mazak Proxy"
          );
          if (!System.IO.Directory.Exists(dir))
            System.IO.Directory.CreateDirectory(dir);

          var today = DateTime.Today;
          if (_lastLogMaintenanceDate != today)
          {
            try
            {
              ProxyLogRetention.Maintain(dir, today);
            }
            catch (Exception maintenanceException)
            {
              TryWriteEventLog(
                "Error maintaining Mazak proxy debug logs"
                  + Environment.NewLine
                  + maintenanceException,
                EventLogEntryType.Error
              );
            }
            _lastLogMaintenanceDate = today;
          }

          var path = System.IO.Path.Combine(dir, $"debug{today:yyyy-MM-dd}.txt");
          using (
            var stream = new System.IO.FileStream(
              path,
              System.IO.FileMode.Append,
              System.IO.FileAccess.Write,
              System.IO.FileShare.ReadWrite
            )
          )
          using (var file = new System.IO.StreamWriter(stream))
          {
            var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
            file.WriteLine(
              ser.Serialize(
                new DebugMessage
                {
                  UtcNow = DateTime.UtcNow,
                  Message = messageTemplate,
                  Exception = ex?.ToString(),
                  Properties = propertyValues,
                }
              )
            );
          }
        }
      }
      catch (Exception logException)
      {
        // Logging must never terminate the proxy. Use the independent Windows event log as the
        // final fallback, but swallow its failure as well.
        TryWriteEventLog(
          "Error writing Mazak proxy debug log" + Environment.NewLine + logException,
          EventLogEntryType.Error
        );
      }
    }

    private static void TryWriteEventLog(string message, EventLogEntryType type)
    {
      try
      {
        lock (EventLogLock)
        {
          if (!EventLog.SourceExists(EventLogConfig.SourceName))
            EventLog.CreateEventSource(EventLogConfig.SourceName, EventLogConfig.LogName);
          using (var ev = new EventLog(EventLogConfig.LogName, ".", EventLogConfig.SourceName))
            ev.WriteEntry(message, type);
        }
      }
      catch
      {
        // A diagnostics failure must not become a process failure.
      }
    }
  }
}
