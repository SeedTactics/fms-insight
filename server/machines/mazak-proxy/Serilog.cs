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
  }

  public static class EventLogConfig
  {
    public const string SourceName = "FMS Insight Mazak Proxy";
    public const string LogName = "Log";

    static EventLogConfig()
    {
      if (!EventLog.SourceExists(SourceName))
        EventLog.CreateEventSource(SourceName, LogName);
    }
  }

  public class Log : ILogger
  {
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
      using (var ev = new EventLog(EventLogConfig.LogName, ".", EventLogConfig.SourceName))
      {
        string msg = message;
        if (ex != null)
        {
          msg += Environment.NewLine + ex.ToString();
        }
        ev.WriteEntry(msg, EventLogEntryType.Error);
      }

      // Also to debug
      Debug(ex, message);
    }

    void ILogger.Information(string message)
    {
      Log.Information(message);
    }

    public static void Information(string message)
    {
      using (var ev = new EventLog(EventLogConfig.LogName, ".", EventLogConfig.SourceName))
      {
        ev.WriteEntry(message, EventLogEntryType.Information);
      }
    }

    public static void Debug(string messageTemplate, params object[] propertyValues)
    {
      Debug(null, messageTemplate, propertyValues);
    }

    void ILogger.Debug(string messageTemplate, params object[] propertyValues)
    {
      Log.Debug(messageTemplate, propertyValues);
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
      var dir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FMS Insight Mazak Proxy"
      );
      if (!System.IO.Directory.Exists(dir))
      {
        System.IO.Directory.CreateDirectory(dir);
      }

      // delete old files
      foreach (var fname in System.IO.Directory.GetFiles(dir, "debug*.txt"))
      {
        if (System.IO.File.GetLastWriteTime(fname) < DateTime.Now.AddDays(-7))
        {
          System.IO.File.Delete(fname);
        }
      }

      var today = DateTime.Today.ToString("yyyy-MM-dd");
      using var file = System.IO.File.AppendText(System.IO.Path.Combine(dir, $"debug{today}.txt"));
      var ser = new System.Xml.Serialization.XmlSerializer(typeof(DebugMessage));
      ser.Serialize(
        file,
        new DebugMessage
        {
          UtcNow = DateTime.UtcNow,
          Message = messageTemplate,
          Exception = ex?.ToString(),
          Properties = propertyValues,
        }
      );
    }
  }
}
