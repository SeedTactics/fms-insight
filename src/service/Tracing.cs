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
using IO = System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using BlackMaple.MachineWatchInterface;

namespace BlackMaple.MachineWatch
{
    public class Tracing : IDisposable
    {
        private List<TraceSource> traceSources = new List<TraceSource>();
        public TraceSource machineTrace = new TraceSource("Machine Watch", SourceLevels.All);
        private readonly TextWriterTraceListener errorLog;
        private readonly TextWriterTraceListener infoLog;
        private readonly string AppPath;

        public Tracing(string appPath,
            IServerBackend backend,
                                         TraceSource eventServer,
                                         IEnumerable<IBackgroundWorker> workers)
        {
            this.AppPath = appPath;

            string file = IO.Path.Combine(AppPath, "error.log");
            errorLog = new TraceListenerWithTime(file);
            errorLog.Filter = new System.Diagnostics.EventTypeFilter(SourceLevels.Error);
            machineTrace.Switch.Level = SourceLevels.All; //Fixes a bug in mono
            machineTrace.Listeners.Add(errorLog);

            System.Diagnostics.Trace.AutoFlush = true;

            infoLog = null;
            if (System.Configuration.ConfigurationManager.AppSettings["Output Trace Log"] == "1")
            {
                file = IO.Path.Combine(AppPath, "trace.log");
                infoLog = new TraceListenerWithTime(file);
                infoLog.Filter = new System.Diagnostics.EventTypeFilter(SourceLevels.Information);
                machineTrace.Listeners.Add(infoLog);
                machineTrace.TraceEvent(TraceEventType.Information, 0, "Machine Watch is starting");
            }

            foreach (var s in backend.TraceSources())
            {
                if (!traceSources.Contains(s))
                {
                    traceSources.Add(s);
                }
            }

            foreach (var w in workers)
            {
                var s = w.TraceSource;
                if (s != null && !traceSources.Contains(s))
                {
                    traceSources.Add(s);
                }
            }

            if (eventServer != null && !traceSources.Contains(eventServer))
            {
                traceSources.Add(eventServer);
            }

            //add new traces to the Listeners
            foreach (TraceSource s in traceSources)
            {
                s.Switch.Level = SourceLevels.All; //Fixes a bug in mono.
                s.Listeners.Add(errorLog);
                if (infoLog != null)
                {
                    s.Listeners.Add(infoLog);
                }
            }
        }

        public void Dispose()
        {
            foreach (var s in traceSources)
            {
                s.Listeners.Clear();
            }
            traceSources.Clear();
        }

        private class TraceListenerWithTime : TextWriterTraceListener
        {

            public TraceListenerWithTime(string f) : base(f)
            {

            }

            public override void WriteLine(string message)
            {
                string prefix = DateTime.Now.ToString() + "  ";
                message = message.Replace(Environment.NewLine, Environment.NewLine + new string(' ', prefix.Length));
                base.Write(prefix);
                base.WriteLine(message);
                base.WriteLine("");
                base.Flush();
            }

        }
    }
}

