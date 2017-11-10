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

