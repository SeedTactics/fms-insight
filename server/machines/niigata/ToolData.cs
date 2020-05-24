/* Copyright (c) 2020, John Lenz

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
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.IO;

namespace BlackMaple.FMSInsight.Niigata
{
  public static class LoadToolData
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<CncMachineConnection>();

    public static void ToolsForMachine(this ICncMachineConnection cnc, int machine)
    {
      try
      {
        cnc.WithConnection<int>(machine, handle =>
        {
          byte[] buff = new byte[10084];
          CncMachineConnection.LogAndThrowError(machine, handle, cnc: false,
            ret: pmc_rdkpm(handle, 51, buff, 10084)
          );

          using (var mem = new MemoryStream(buff))
          using (var rawReader = new BinaryReader(mem))
          {
            var beReader = new BigEndianBinaryReader(rawReader);

            var numTools = beReader.ReadUInt16();
            Log.Debug("Loading {numTools} tools from {@arr}", numTools, buff.Take(4));
            if (numTools < 0) numTools = 0;
            if (numTools > 360) numTools = 360;

            // two bytes are igored
            beReader.ReadByte(); beReader.ReadByte();

            for (int i = 0; i < numTools; i++)
            {
              var toolNum = beReader.ReadUInt32();
              var gNum = beReader.ReadInt16();
              beReader.ReadInt16(); // dummy
              var lifeTm = beReader.ReadInt32();
              var restTm = beReader.ReadInt32();
              var loadMax = beReader.ReadInt16();
              var loadMore = beReader.ReadInt16();
              var meas = beReader.ReadByte();
              var lifeAlrm = beReader.ReadByte();
              var brokenAlrm = beReader.ReadByte();
              var cuttingAlrm = beReader.ReadByte();
              var checkingAlrm = beReader.ReadByte();
              var lifeKind = beReader.ReadByte();
              beReader.ReadByte(); // dummy
              beReader.ReadByte(); // dummy

              var serialNo = toolNum % 100;
              var groupNo = toolNum / 100;

              Log.Debug("Tool data for {i}: {@raw} " +
                "{toolNum}, {gNum}, {lifeTm}, {restTm}, {loadMax}, {loadMore}, {meas}, {lifeAlrm}, {brokenAlrm}, {cuttingAlrm}, {checkingAlrm}, {lifeKind}",
                (new Span<byte>(buff, 4 + i * 28, 28)).ToArray(),
                toolNum, gNum, lifeTm, restTm, loadMax, loadMore, meas, lifeAlrm, brokenAlrm, cuttingAlrm, checkingAlrm, lifeKind
              );
            }

            return numTools;
          }
        });
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error communicatig with machine {machine}", machine);
      }
    }


    [DllImport("fwlib32.dll")]
    private static extern short pmc_rdkpm(ushort handle, ulong offset, [Out] byte[] data, ushort length);

    public class BigEndianBinaryReader
    {
      private BinaryReader _reader;
      public BigEndianBinaryReader(BinaryReader r) => _reader = r;

      private byte[] ReadAndConvert(int count)
      {
        var bytes = _reader.ReadBytes(count);
        if (BitConverter.IsLittleEndian)
        {
          Array.Reverse(bytes);
        }
        return bytes;
      }

      public byte ReadByte() => _reader.ReadByte();
      public short ReadInt16() => BitConverter.ToInt16(ReadAndConvert(sizeof(short)), 0);
      public ushort ReadUInt16() => BitConverter.ToUInt16(ReadAndConvert(sizeof(ushort)), 0);
      public int ReadInt32() => BitConverter.ToUInt16(ReadAndConvert(sizeof(int)), 0);
      public uint ReadUInt32() => BitConverter.ToUInt16(ReadAndConvert(sizeof(uint)), 0);
    }

  }
}