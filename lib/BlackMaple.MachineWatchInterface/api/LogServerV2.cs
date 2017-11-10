using System;
using System.Collections.Generic;

namespace BlackMaple.MachineWatchInterface
{
    [Serializable]
    public enum SerialType
    {
        NoSerials,
        OneSerialPerMaterial,  // assign a different serial to each piece of material
        OneSerialPerCycle,     // assign a single serial to all the material on each cycle
        SerialDeposit          // deposit serial into machine file to be scribed on part
    }

    [Serializable]
    public class SerialSettings
    {
        public SerialType SerialType {get;}
        public int SerialLength {get;}

        //settings only for serial deposit
        public int DepositOnProcess {get;}
        public string FilenameTemplate {get;}
        public string ProgramTemplate {get;}

        public SerialSettings(SerialType t, int len)
        {
            SerialType = t;
            SerialLength = len;
            DepositOnProcess = 1;
            FilenameTemplate = null;
            ProgramTemplate = null;
        }
        public SerialSettings(int len, int proc, string fileTemplate, string progTemplate)
        {
            SerialType = SerialType.SerialDeposit;
            SerialLength = len;
            DepositOnProcess = proc;
            FilenameTemplate = fileTemplate;
            ProgramTemplate = progTemplate;
        }
    }

    public interface ILogServerV2
    {
        List<LogEntry> GetLogEntries(DateTime startUTC, DateTime endUTC);
        List<LogEntry> GetLog(long lastSeenCounter);
        List<LogEntry> GetLogForMaterial(long materialID);
        List<LogEntry> GetLogForSerial(string serial);
        List<LogEntry> GetLogForWorkorder(string workorder);
        List<LogEntry> GetCompletedPartLogs(DateTime startUTC, DateTime endUTC);
        List<WorkorderSummary> GetWorkorderSummaries(IEnumerable<string> workorderIds);

        LogEntry RecordSerialForMaterialID(LogMaterial mat, string serial);
        LogEntry RecordWorkorderForMaterialID(LogMaterial mat, string workorder);
        LogEntry RecordFinalizedWorkorder(string workorder);

        SerialSettings GetSerialSettings();
        void SetSerialSettings(SerialSettings s);
    }
}

