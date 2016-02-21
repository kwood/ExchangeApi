using Conditions;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.OkCoin.REST
{
    public class RestClient : IDisposable
    {
        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        private readonly HttpClient _http;

        public RestClient(string endpoint)
        {
            Condition.Requires(endpoint, "endpoint").IsNotNullOrEmpty();
            _http = new HttpClient();
            _http.BaseAddress = new Uri(endpoint);
            _http.Timeout = TimeSpan.FromSeconds(10);
        }

        public void Dispose()
        {
            try { _http.Dispose(); }
            catch (Exception e) { _log.Warn(e, "Ignoring exception from HttpClient.Dispose()"); }
        }

        // Throws on timeouts and server errors. Never returns null.
        string SendRequest(HttpMethod method, string relativeUri)
        {
            _log.Info("OUT: {0} {1}", method.ToString().ToUpper(), relativeUri);
            try
            {
                var req = new HttpRequestMessage(method, relativeUri);
                HttpResponseMessage resp = _http.SendAsync(req, HttpCompletionOption.ResponseContentRead).Result;
                string content = resp.EnsureSuccessStatusCode().Content.ReadAsStringAsync().Result;
                _log.Info("IN: {0}", content);
                return content;
            }
            catch (Exception e)
            {
                _log.Warn(e, "IO error");
                throw;
            }
        }
    }
}
