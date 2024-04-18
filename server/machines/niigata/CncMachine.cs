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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;

namespace BlackMaple.FMSInsight.Niigata
{
  public interface ICncMachineConnection
  {
    T WithConnection<T>(int machine, Func<ushort, T> f);
  }

  public class CncMachineConnection : ICncMachineConnection
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<CncMachineConnection>();

    private List<IPEndPoint> _machines;

    public CncMachineConnection(IEnumerable<string> machines)
    {
      _machines = machines.Select(CreateIPEndPoint).ToList();
    }

    public T WithConnection<T>(int machine, Func<ushort, T> f)
    {
      if (machine < 1 || machine > _machines.Count)
      {
        throw new Exception("Invalid machine number " + machine.ToString());
      }
      ushort handle;
      Log.Debug(
        "Connecting to machine {mc} at {ip} on port {port}",
        machine,
        _machines[machine - 1].Address.ToString(),
        _machines[machine - 1].Port
      );
      var ret = cnc_allclibhndl3(
        _machines[machine - 1].Address.ToString(),
        (ushort)_machines[machine - 1].Port,
        10 /* seconds */
        ,
        out handle
      );
      if (ret != 0)
      {
        Log.Error("Code {code} when connecting to machine {machine}", ret, machine);
        throw new Exception("Error connecting to CNC machine " + machine.ToString());
      }
      try
      {
        return f(handle);
      }
      finally
      {
        cnc_freelibhndl(handle);
      }
    }

    public static void LogAndThrowError(int machine, ushort handle, short ret, bool cnc = true)
    {
      if (ret == 0)
      {
        return;
      }
      else
      {
        ErrorDetail errDetail;
        short errRet;
        if (cnc)
        {
          errRet = cnc_getdtailerr(handle, out errDetail);
        }
        else
        {
          errRet = pmc_getdtailerr(handle, out errDetail);
        }
        if (ret == SocketError)
        {
          Log.Warning(
            "Unable to communicate with machine #{machine}: {ret}, {errRet}, {errNo}, {errDtno}",
            machine,
            ret,
            errRet,
            errDetail.err_no,
            errDetail.err_dtno
          );
        }
        else
        {
          Log.Error(
            "Received error {ret} when communicating with machine #{machine}: {errRet}, {errNo}, {errDtno}",
            machine,
            ret,
            errRet,
            errDetail.err_no,
            errDetail.err_dtno
          );
        }
        throw new Exception("Unable to communicate with machine " + machine.ToString());
      }
    }

    [DllImport("fwlib32.dll")]
    private static extern short cnc_allclibhndl3(
      [MarshalAs(UnmanagedType.LPStr)] string ip,
      ushort port,
      int timeout,
      out ushort handle
    );

    [DllImport("fwlib32.dll")]
    private static extern short cnc_freelibhndl(ushort handle);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ErrorDetail
    {
      public short err_no;
      public short err_dtno;
    }

    [DllImport("fwlib32.dll")]
    private static extern short cnc_getdtailerr(ushort handle, out ErrorDetail a);

    [DllImport("fwlib32.dll")]
    private static extern short pmc_getdtailerr(ushort handle, out ErrorDetail a);

    private const short SocketError = -16;

    private static System.Net.IPEndPoint CreateIPEndPoint(string endPoint)
    {
      string[] ep = endPoint.Split(':');
      System.Net.IPAddress ip;
      int port;
      if (ep.Length > 2)
      {
        throw new FormatException("Too many colons in ip address and port, IPv6 not supported");
      }
      else if (ep.Length == 2)
      {
        if (!System.Net.IPAddress.TryParse(ep[0], out ip))
        {
          throw new FormatException("Invalid ip-adress");
        }
        if (!int.TryParse(ep[1], out port))
        {
          throw new FormatException("Invalid port");
        }
      }
      else
      {
        if (!System.Net.IPAddress.TryParse(ep[0], out ip))
        {
          throw new FormatException("Invalid ip-adress");
        }
        port = 8193;
      }
      return new System.Net.IPEndPoint(ip, port);
    }
  }
}
