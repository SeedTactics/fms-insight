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

using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace BlackMaple.MachineWatchInterface
{
    public class MachineWatchHttpClient
    {
        protected HttpClient _client;
        protected string _host;
        protected string _token;

        public MachineWatchHttpClient(string host, string token)
        {
            _client = new HttpClient();
            _host = host;
            _token = token;
        }

        protected async Task Send(HttpMethod method, string path)
        {
            var msg = new HttpRequestMessage(method, "https://" + _host + path);
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            var resp = await _client.SendAsync(msg);
            resp.EnsureSuccessStatusCode();
        }

        protected async Task SendJson<T>(HttpMethod method, string path, T body)
        {
            var msg = new HttpRequestMessage(method, "https://" + _host + path);
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

            using (var ms = new System.IO.MemoryStream())
            {
                var ser = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T));
                ser.WriteObject(ms, body);
                msg.Content = new StringContent(
                    System.Text.Encoding.UTF8.GetString(ms.ToArray()),
                    Encoding.UTF8, "application/json");
            }

            var response = await _client.SendAsync(msg);
            response.EnsureSuccessStatusCode();
        }

        protected async Task<T> RecvJson<T>(HttpMethod method, string path)
        {
            var msg = new HttpRequestMessage(method, "https://" + _host + path);
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            var response = await _client.SendAsync(msg);
            response.EnsureSuccessStatusCode();
            var ser = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T));
            var stream = await response.Content.ReadAsStreamAsync();
            return (T)ser.ReadObject(stream);
        }

        protected async Task<Ret> SendRecvJson<T, Ret>(HttpMethod method, string path, T body)
        {
            var msg = new HttpRequestMessage(method, "https://" + _host + path);
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

            using (var ms = new System.IO.MemoryStream())
            {
                var ser = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T));
                ser.WriteObject(ms, body);
                msg.Content = new StringContent(
                    System.Text.Encoding.UTF8.GetString(ms.ToArray()),
                    Encoding.UTF8, "application/json");
            }

            var resp = await _client.SendAsync(msg);
            resp.EnsureSuccessStatusCode();

            var deSer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Ret));
            var stream = await resp.Content.ReadAsStreamAsync();
            return (Ret)deSer.ReadObject(stream);
        }
    }
}
