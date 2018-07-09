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
using System.Linq;
using System.Text;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;

namespace Cincron
{
    public class MessageWatcher
    {
        private static Serilog.ILogger Log = Serilog.Log.ForContext<MessageWatcher>();
        private JobLogDB _log;
        private string _msgFile;
        private object _lock;
        private System.Timers.Timer _timer;

        public MessageWatcher(string msgFile, JobLogDB log)
        {
            _msgFile = msgFile;
            _log = log;
            _lock = new object();
            _timer = new System.Timers.Timer(TimeSpan.FromMinutes(1).TotalMilliseconds);
            _timer.Elapsed += CheckMessages;
        }

        public void Start()
        {
            _timer.Start();
        }

        public void Halt()
        {
            _timer.Stop();
        }

        #region "Message Timer"
        //State accumulated as we walk through the messages
        private class MessageState
        {
            public CincronMessage LastSeenMessage;
            public List<CincronMessage.PartCompleted> PartCompletedMessages
                = new List<CincronMessage.PartCompleted>();
            public PalletLocation LastUnloadStation;
        }

        public void CheckMessages(object sender, System.Timers.ElapsedEventArgs e)
        {
            lock (_lock) {
                try {
                    Log.Debug("Starting to read {file}", _msgFile);

                    var msgs = LoadMessages();
                    var state = new MessageState();

                    for (int i = 0; i < msgs.Count; i++) {
                        var msg = msgs[i];
                        int repeatCount = 1;
                        if (i < msgs.Count - 1) {
                            var nextMsg = msgs[i + 1];
                            if (nextMsg is CincronMessage.PreviousMessageRepeated) {
                                repeatCount = ((CincronMessage.PreviousMessageRepeated)nextMsg).NumRepeated + 1;
                            }
                        }
                        Log.Debug("Processing message {@msg} repeated {cnt}", msg, repeatCount);

                        HandleMessage(state, msg, repeatCount);

                        state.LastSeenMessage = msg;
                    }

                    //TODO: store state to filesystem
                    //for now, the next time this runs the events will be re-processed

                } catch (Exception ex) {
                    Log.Error(ex, "Unhandled error in message file watcher");
                }
            }
        }

        private IList<CincronMessage> LoadMessages()
        {
            var max = _log.MaxForeignID();
            if (max != null && max != "") {
                var expected = _log.OriginalMessageByForeignID(max);
                if (expected != null && expected != "") {
                    var maxParts = max.Split('-'); //year-month-day-hour-min-sec-fileoffset
                    int offset;
                    if (maxParts.Length >= 7 && int.TryParse(maxParts[6], out offset)) {
                        Log.Debug("Starting read at offset {offset} with message {msg}", offset, expected);
                        return MessageParser.ExtractMessages(_msgFile, offset, expected);
                    }
                }
            }

            Log.Debug("Starting reading message file from beginning");
            return MessageParser.ExtractMessages(_msgFile, 0, "");
        }
        #endregion

        #region "Single Message Processing"
        private void HandleMessage(MessageState state, CincronMessage msg, int repeatCount)
        {
            var queueChange = msg as CincronMessage.QueuePositionChange;

            //machine cycle start.  For now it is pallet rotating into machine
            if (queueChange != null
                && queueChange.CurrentLocation.Location == PalletLocationEnum.Machine
                && queueChange.NewQueuePosition == "10010") {

                _log.RecordMachineStart(
                    mats: FindMaterial(queueChange.Pallet),
                    pallet: queueChange.Pallet,
                    statName: "MC",
                    statNum: queueChange.CurrentLocation.Num,
                    program: "",
                    timeUTC: queueChange.TimeUTC,
                    foreignId: ForeignId(msg),
                    originalMessage: msg.LogMessage
                );
            }

            //machine cycle end.  StepNo changing to 5 signals cycle end.
            var stepChange = msg as CincronMessage.PartNewStep;
            if (stepChange != null && stepChange.StepNo == 5) {
                var machineCycleStart = FindMachineStart(_log.CurrentPalletLog(stepChange.Pallet));

                if (machineCycleStart != null) {
                    _log.RecordMachineEnd(
                        mats: machineCycleStart.Material,
                        pallet: stepChange.Pallet,
                        statName: "MC",
                        statNum: machineCycleStart.LocationNum,
                        program: "",
                        timeUTC: stepChange.TimeUTC,
                        result: "",
                        elapsed: stepChange.TimeUTC.Subtract(machineCycleStart.EndTimeUTC),
                        active: TimeSpan.Zero,
                        foreignId: ForeignId(msg),
                        originalMessage: msg.LogMessage
                    );
                }
            }

            //program end.  FindMachineStart correctly returns null if we have already recorded
            //cycle end.
            var progEnd = msg as CincronMessage.ProgramFinished;
            if (progEnd != null)
            {
                var machineCycleStart = FindMachineStart(_log.CurrentPalletLog(progEnd.Pallet));

                if (machineCycleStart != null)
                {
                    _log.RecordMachineEnd(
                        mats: machineCycleStart.Material,
                        pallet: progEnd.Pallet,
                        statName: "MC",
                        statNum: machineCycleStart.LocationNum,
                        program: "",
                        timeUTC: progEnd.TimeUTC,
                        result: "",
                        elapsed: progEnd.TimeUTC.Subtract(machineCycleStart.EndTimeUTC),
                        active: TimeSpan.Zero,
                        foreignId: ForeignId(msg),
                        originalMessage: msg.LogMessage
                    );
                }
            }

            //part completed message.  Store in memory since typically there is an Unload Start event
            //which happens right afterwords.
            var comp = msg as CincronMessage.PartCompleted;
            if (comp != null) {
                for (int i = 0; i < repeatCount; i++) {
                    state.PartCompletedMessages.Add(comp);
                }
            }

            //move to unload.  Store in memory, typically there is an UnloadStart event soon
            if (queueChange != null
                && queueChange.CurrentLocation.Location == PalletLocationEnum.LoadUnload
                && queueChange.NewQueuePosition == "10010") {
                state.LastUnloadStation = queueChange.CurrentLocation;
            }

            //unload start.  Use the completed parts and last unload station from the state.
            var unloadStart = msg as CincronMessage.PartUnloadStart;
            if (unloadStart != null) {
                _log.RecordUnloadStart(
                    mats: CreateUnloadMaterial(state, unloadStart.Pallet),
                    pallet: unloadStart.Pallet,
                    lulNum: state.LastUnloadStation.Num,
                    timeUTC: unloadStart.TimeUTC,
                    foreignId: ForeignId(msg),
                    originalMessage: msg.LogMessage
                );
                state.PartCompletedMessages.Clear();
            }

            var loadStart = msg as CincronMessage.PartLoadStart;
            if (loadStart != null) {
                _log.RecordLoadStart(
                    mats: CreateLoadMaterial(loadStart),
                    pallet: loadStart.Pallet,
                    lulNum: state.LastUnloadStation.Num,
                    timeUTC: loadStart.TimeUTC,
                    foreignId: ForeignId(msg),
                    originalMessage: msg.LogMessage
                );
            }

            //end of load and unload on step change to 2
            if (stepChange != null && stepChange.StepNo == 2) {

                //create end unload, then pallet cycle, then end load.
                var oldEvts = _log.CurrentPalletLog(stepChange.Pallet);
                var loadStartCycle = FindLoadStart(oldEvts);
                var unloadStartCycle = FindUnloadStart(oldEvts);

                if (unloadStartCycle != null) {
                    _log.RecordUnloadEnd(
                        mats: unloadStartCycle.Material,
                        pallet: stepChange.Pallet,
                        lulNum: unloadStartCycle.LocationNum,
                        timeUTC: stepChange.TimeUTC,
                        elapsed: stepChange.TimeUTC.Subtract(unloadStartCycle.EndTimeUTC),
                        active: TimeSpan.Zero,
                        foreignId: ForeignId(msg),
                        originalMessage: msg.LogMessage
                    );
                }

                var mats = new Dictionary<string, IEnumerable<LogMaterial>>();

                if (loadStartCycle != null) {
                    _log.AddPendingLoad(
                        pal: stepChange.Pallet,
                        key: stepChange.Pallet,
                        load: loadStartCycle.LocationNum,
                        elapsed: stepChange.TimeUTC.Subtract(loadStartCycle.EndTimeUTC),
                        active: TimeSpan.Zero,
                        foreignID: ForeignId(msg)
                    );
                    mats[stepChange.Pallet] = loadStartCycle.Material;
                }

                _log.CompletePalletCycle(stepChange.Pallet, stepChange.TimeUTC, ForeignId(msg), mats, SerialType.NoAutomaticSerials, 10);
            }
        }

        private string ForeignId(CincronMessage msg)
        {
            return msg.TimeOfFirstEntryInLogFileUTC.ToString("yyyy-MM-dd-HH-mm-ss") + "-" + msg.LogFileOffset.ToString("000000000000");
        }

        private LogEntry FindMachineStart(IList<LogEntry> oldEvents)
        {
            LogEntry ret = null;
            foreach (var c in oldEvents) {
                if (c.LogType == LogType.MachineCycle && c.StartOfCycle)
                    ret = c;
                if (c.LogType == LogType.MachineCycle && !c.StartOfCycle)
                    return null;  //immedietly return null because there is already an end
            }
            return ret;
        }

        private LogEntry FindUnloadStart(IList<LogEntry> oldEvents)
        {
            foreach (var c in oldEvents) {
                if (c.LogType == LogType.LoadUnloadCycle
                    && c.StartOfCycle
                    && c.Result == "UNLOAD")
                    return c;
            }
            return null;
        }

        private LogEntry FindLoadStart(IList<LogEntry> oldEvents)
        {
            foreach (var c in oldEvents) {
                if (c.LogType == LogType.LoadUnloadCycle
                    && c.StartOfCycle
                    && c.Result == "LOAD")
                    return c;
            }
            return null;
        }
        #endregion

        #region "Material"
        //since we don't know the quantity on the pallet until the very end, can just
        //create a single material ID.  At the very end, when we do know the count (and part name),
        //can add exactly that many material ids as long as the original material id is included.

        private IEnumerable<LogMaterial> CreateLoadMaterial(CincronMessage.PartLoadStart load)
        {
            var matId = _log.AllocateMaterialID(load.WorkId, "", 1);
            Log.Debug("Creating new material id {matid} for load event with work id {workId}", matId, load.WorkId);
            return new LogMaterial[] {
                new LogMaterial(
                    matID: matId,
                    uniq: load.WorkId,
                    proc: 1,
                    part: "",
                    numProc: 1)
            };
        }

        private IList<LogMaterial> FindMaterial(string pal)
        {
            var oldEvts = _log.CurrentPalletLog(pal);
            for (int i = oldEvts.Count - 1; i >= 0; i--) {
                if (oldEvts[i].Material.Count() > 0) {
                    return new List<LogMaterial>(oldEvts[i].Material);
                }
            }

            Log.Warning("Unable to find existing material for pallet {pal}", pal);
            var matId = _log.AllocateMaterialID("", "", 1);
            return new LogMaterial[] {
                new LogMaterial(
                    matID: matId,
                    uniq: "",
                    proc: 1,
                    part: "",
                    numProc: 1)
                };
        }

        private IEnumerable<LogMaterial> CreateUnloadMaterial(MessageState state, string pal)
        {
            var oldMat = FindMaterial(pal)[0];
            var ret = new List<LogMaterial>();
            string partName = "";
            if (state.PartCompletedMessages.Count > 0)
                partName = state.PartCompletedMessages[0].PartName;
            _log.SetDetailsForMaterialID(oldMat.MaterialID, oldMat.JobUniqueStr, partName, 1);

            ret.Add(new LogMaterial(
                matID: oldMat.MaterialID,
                uniq: oldMat.JobUniqueStr,
                proc: 1,
                part: partName,
                numProc: 1));

            Log.Debug("During unload, found {cnt} parts that were unloaded/completed",
                state.PartCompletedMessages.Count);

            //allocate new materials, one per completed part in addition to the existing one
            //Seems that multiple part completed messages are not multiple completed parts?
            //for (int i = 1; i < state.PartCompletedMessages.Count; i++) {
            //    var newId = _log.AllocateMaterialID(oldMat.JobUniqueStr);
            //    ret.Add(new LogMaterial(
            //       matID: newId,
            //       uniq: oldMat.JobUniqueStr,
            //       proc: 1,
            //       part: partName,
            //       numProc: 1));
            //}

            return ret;
        }


        #endregion

    }
}
