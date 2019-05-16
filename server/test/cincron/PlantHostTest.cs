/* Copyright (c) 2019, John Lenz

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
using System.Net.Sockets;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cincron;
using Xunit;
using FluentAssertions;

namespace MachineWatchTest.Cincron
{
  public static class ReponseExpectation
  {
    public static void ResponseShouldBe(this MessageResponse resp, MessageResponse expected)
    {
      resp.Fields.Should().BeEquivalentTo(expected.Fields);
    }
  }


  public class PlantHostTest
  {
    private async Task WithPlantHost(Func<PlantHostCommunication, Task> plant, Func<StreamReader, Action<MessageResponse>, Task> cell)
    {
      using (var host = new PlantHostCommunication(0))
      {
        await Task.Delay(TimeSpan.FromSeconds(0.5));
        using (var c = new TcpClient("localhost", host.Port))
        using (var ns = c.GetStream())
        using (var reader = new StreamReader(ns))
        using (var writer = new StreamWriter(ns))
        {
          var cts = new CancellationTokenSource();
          Exception e = null;
          await Task.WhenAny(new[] {
            Task.Run(() => { WaitHandle.WaitAny(new [] { cts.Token.WaitHandle}); }),
            Task.Run(async () => {
              await Task.WhenAll(new[] {
                Task.Run(async () => {
                  try {
                    await plant(host);
                  } catch (Exception ex) {
                    e = ex;
                    cts.Cancel();
                    throw;
                  }
                }),
                Task.Run(async () => {
                  try {
                    await cell(reader, msg => {
                      writer.Write(string.Join("\n", msg.RawLines) + "\n\n");
                      writer.Flush(); });
                  } catch (Exception ex) {
                    e = ex;
                    cts.Cancel();
                    throw;
                  }
                })
              });
              cts.Cancel();
            })
          });
          if (e != null)
          {
            throw e;
          }
        }
      }
    }

    private MessageResponse mkMessage(int trans, string respn, IReadOnlyDictionary<string, string> fields)
    {
      var f = new Dictionary<string, string>(fields);
      f["respn"] = respn;
      f["trans"] = trans.ToString();
      return new MessageResponse()
      {
        RawLines = f.Select(k => k.Key + ": " + k.Value).ToList(),
        Fields = f
      };
    }

    [Fact(Skip = "Fails on windows")]
    public async Task SendCommand()
    {
      var msg1 = mkMessage(2, "mytestcommand", new Dictionary<string, string> { { "a", "b" }, { "statu", "SUCCESS" } });

      await WithPlantHost(
        async host =>
        {
          (await host.Send("mytestcommand")).ResponseShouldBe(msg1);
        },
        async (reader, writer) =>
        {
          (await reader.ReadLineAsync())
            .Should().Be("mytestcommand -t 2");
          writer(msg1);
          (await reader.ReadLineAsync())
            .Should().Be("ackcel -r mytestcommand -t 2 -s SUCCESS");
        }
      );
    }

    [Fact(Skip = "fails on windows")]
    public async Task UnsolicitedResponse()
    {
      var msg = mkMessage(5004, "myevent", new Dictionary<string, string> { { "c", "d" }, { "eeee", "fff" } });
      var waitForSubscribe = new TaskCompletionSource<int>();
      await WithPlantHost(
        async host =>
        {
          var comp = new TaskCompletionSource<MessageResponse>();
          host.OnUnsolicitiedResponse += comp.SetResult;
          waitForSubscribe.SetResult(1);
          (await comp.Task).ResponseShouldBe(msg);
        },
        async (reader, writer) =>
        {
          await waitForSubscribe.Task;
          writer(msg);
          (await reader.ReadLineAsync())
            .Should().Be("ackcel -r myevent -t 5004 -s SUCCESS");
        }
      );
    }

    [Fact(Skip = "Fails on windows")]
    public async Task ThrowsError()
    {
      var msg1 = mkMessage(2, "mytestcommand", new Dictionary<string, string> { { "a", "b" }, { "statu", "ERROR" } });

      await WithPlantHost(
        async host =>
        {
          Func<Task> act = async () => { await host.Send("mytestcommand"); };
          await act.Should().ThrowAsync<Exception>();
        },
        async (reader, writer) =>
        {
          (await reader.ReadLineAsync())
            .Should().Be("mytestcommand -t 2");
          writer(msg1);
          (await reader.ReadLineAsync())
                  .Should().Be("ackcel -r mytestcommand -t 2 -s SUCCESS");
        }
      );

    }
  }

}