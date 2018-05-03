using System;
using System.Collections.Generic;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;

namespace Cincron
{
    public class CincronBackend : IFMSBackend, IFMSImplementation
    {
        private System.Diagnostics.TraceSource trace =
           new System.Diagnostics.TraceSource("Cincron", System.Diagnostics.SourceLevels.All);

        private InspectionDB _inspectDB;
        private JobLogDB _log;
        private MessageWatcher _msgWatcher;

        public FMSInfo Info => new FMSInfo()
            {
                Name = "Cincron",
                Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString()
            };
        public IFMSBackend Backend => this;
        public IList<IBackgroundWorker> Workers => new List<IBackgroundWorker>();

        public void Init(string dataDirectory, IConfig cfg, SerialSettings ser)
        {
            try {

                string msgFile;
#if DEBUG
                var path = System.Reflection.Assembly.GetExecutingAssembly().Location;
                msgFile = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(path), "..\\..\\..\\test\\Cincron\\parker-example-messages");
                dataDirectory = System.IO.Path.Combine(dataDirectory, "cincron");
                if (!System.IO.Directory.Exists(dataDirectory)) {
                    System.IO.Directory.CreateDirectory(dataDirectory);
                }
#else
                msgFile = cfg.GetValue<string>("Cincron", "Message File");
#endif

                trace.TraceEvent(System.Diagnostics.TraceEventType.Warning, 0,
                    "Starting cincron backend" + Environment.NewLine +
                    "    message file: " + msgFile);

                if (!System.IO.File.Exists(msgFile)) {
                    trace.TraceEvent(System.Diagnostics.TraceEventType.Error, 0,
                        "Message file " + msgFile + " does not exist");
                }

                _log = new JobLogDB();

                _log.Open(System.IO.Path.Combine(dataDirectory, "log.db"));
                _inspectDB = new InspectionDB(_log);
                _inspectDB.Open(System.IO.Path.Combine(dataDirectory, "inspections.db"));
                _msgWatcher = new MessageWatcher(msgFile, _log, trace);
                _msgWatcher.Start();

            } catch (Exception ex) {
                trace.TraceEvent(System.Diagnostics.TraceEventType.Error, 0,
                    "Unhandled exception when initializing cincron backend" + Environment.NewLine +
                    ex.ToString());
            }
        }

        public IEnumerable<System.Diagnostics.TraceSource> TraceSources()
        {
            return new System.Diagnostics.TraceSource[] { trace };
        }

        public void Halt()
        {
            if (_msgWatcher != null) _msgWatcher.Halt();
            if (_inspectDB != null) _inspectDB.Close();
            if (_log != null) _log.Close();
        }

        public IInspectionControl InspectionControl()
        {
            return _inspectDB;
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
            Program.Run(useService, new CincronBackend());
        }
    }
}
