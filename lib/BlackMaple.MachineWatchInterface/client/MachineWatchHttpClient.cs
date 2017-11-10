using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;

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
            msg.Content = new StringContent(JsonConvert.SerializeObject(body).ToString(),
                Encoding.UTF8, "application/json");
            var resp = await _client.SendAsync(msg);
            resp.EnsureSuccessStatusCode();
        }

        protected async Task<T> RecvJson<T>(HttpMethod method, string path)
        {
            var msg = new HttpRequestMessage(method, "https://" + _host + path);
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            var response = await _client.SendAsync(msg);
            response.EnsureSuccessStatusCode();
            var str = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<T>(str);            
        }

        protected async Task<Ret> SendRecvJson<T, Ret>(HttpMethod method, string path, T body)
        {
            var msg = new HttpRequestMessage(method, "https://" + _host + path);
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            msg.Content = new StringContent(JsonConvert.SerializeObject(body).ToString(),
                Encoding.UTF8, "application/json");
            var resp = await _client.SendAsync(msg);
            resp.EnsureSuccessStatusCode();
            var str = await resp.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<Ret>(str);                        
        }
    }
}
