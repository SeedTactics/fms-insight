/* Copyright (c) 2023, John Lenz

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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Germinate;

namespace BlackMaple.MachineFramework
{
  [Draftable]
  public record MaterialToSendToExternalQueue
  {
    public required string Server { get; init; }
    public required string PartName { get; init; }
    public required string Queue { get; init; }
    public required string Serial { get; init; }
  }

  public static class SendMaterialToExternalQueue
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<MaterialToSendToExternalQueue>();

    public static async Task Post(IEnumerable<MaterialToSendToExternalQueue> mats)
    {
      foreach (var g in mats.GroupBy(m => m.Server))
      {
        await SendToServer(g);
      }
    }

    private static async Task SendToServer(IGrouping<string, MaterialToSendToExternalQueue> mats)
    {
      try
      {
        var builder = new UriBuilder(mats.Key);
        if (builder.Scheme == "")
          builder.Scheme = "http";
        if (builder.Port == 80 && !mats.Key.Contains(':'))
          builder.Port = 5000;

        using (var client = new HttpClient())
        {
          client.BaseAddress = builder.Uri;

          foreach (var mat in mats)
          {
            var q = "?queue=" + WebUtility.UrlEncode(mat.Queue) + "&pos=-1";

            Log.Debug("Sending {@mat} to external queue at {server}", mat, builder.Uri);

            var resp = await client.PostAsync(
              "/api/v1/jobs/part/" + WebUtility.UrlEncode(mat.PartName) + "/casting" + q,
              new StringContent(
                Newtonsoft.Json.JsonConvert.SerializeObject(mat.Serial),
                System.Text.Encoding.UTF8,
                "application/json"
              )
            );

            if (!resp.IsSuccessStatusCode)
            {
              var body = await resp.Content.ReadAsStringAsync();
              Log.Error(
                "Received error {code} when trying to add material {@mat} to external queue at {server}: {err}",
                resp.StatusCode,
                mat,
                builder.Uri,
                body
              );
            }
          }
        }
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error sending material to external queue");
      }
    }
  }
}
