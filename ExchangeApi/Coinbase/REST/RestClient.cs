using Conditions;
using ExchangeApi.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Coinbase.REST
{
    using KV = KeyValuePair<string, string>;

    // See https://docs.exchange.coinbase.com/#api.
    public class RestClient : IDisposable
    {
        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        readonly Authenticator _authenticator;
        readonly HttpClient _http;
        // From https://docs.exchange.coinbase.com/#rate-limits:
        //
        //   We throttle public endpoints by IP: 3 requests per second, up to 6 requests per second in bursts.
        //   We throttle private endpoints by user ID: 5 requests per second, up to 10 requests per second in bursts.
        readonly RateLimiter _publicRateLimiter = new RateLimiter(TimeSpan.FromSeconds(1), 3);
        readonly RateLimiter _privateRateLimiter = new RateLimiter(TimeSpan.FromSeconds(1), 5);

        // `keys` may be null, in which case authenticated requests won't be supported.
        public RestClient(string endpoint, Keys keys)
        {
            Condition.Requires(endpoint, "endpoint").IsNotNullOrEmpty();
            _authenticator = new Authenticator(keys);
            _http = new HttpClient();
            _http.BaseAddress = new Uri(endpoint);
            _http.Timeout = TimeSpan.FromSeconds(10);
            // Coinbase returns HTTP 400 (Bad Request) if User-Agent isn't set.
            _http.DefaultRequestHeaders.Add("User-Agent", "romkatv.github.com");
        }

        public void Dispose()
        {
            try { _http.Dispose(); }
            catch (Exception e) { _log.Warn(e, "Ignoring exception from HttpClient.Dispose()"); }
        }

        // Throws asynchronously on timeouts, server errors. Never returns null and never throws synchronously.
        //
        // If it's not possible to send the request right away due to rate limits, waits.
        public async Task<TResponse> SendRequest<TResponse>(IRequest<TResponse> req) where TResponse : IResponse, new()
        {
            RateLimiter limiter = req.IsAuthenticated ? _privateRateLimiter : _publicRateLimiter;
            await limiter.Request();
            return await DoSendRequest(req);
        }

        // Throws asynchronously on timeouts, server errors and rate limits. Never returns null and never throws synchronously.
        //
        // If it's not possible to send the request right away due to rate limits, fails immediately.
        public async Task<TResponse> TrySendRequest<TResponse>(IRequest<TResponse> req) where TResponse : IResponse, new()
        {
            RateLimiter limiter = req.IsAuthenticated ? _privateRateLimiter : _publicRateLimiter;
            if (!limiter.TryRequest())
            {
                await Task.Run(() => { throw new ArgumentException("Rate limited"); });
            }
            return await DoSendRequest(req);
        }

        // Throws on timeouts and server errors. Never returns null.
        async Task<TResponse> DoSendRequest<TResponse>(IRequest<TResponse> req) where TResponse : IResponse, new()
        {
            try
            {
                var msg = new HttpRequestMessage(req.HttpMethod, req.RelativeUrl);
                string relativeUrl = req.RelativeUrl;
                string body = null;
                if (req.HttpMethod == HttpMethod.Post)
                {
                    body = ToJsonString(req.Parameters());
                }
                else
                {
                    relativeUrl = AppendQueryParams(relativeUrl, ToUrlQuery(req.Parameters()));
                }

                if (body != null) msg.Content = new StringContent(body, Encoding.UTF8, "application/json");
                if (req.IsAuthenticated)
                {
                    // If the keys weren't specified in the constructor, authenticator will throw.
                    _authenticator.Sign(req.HttpMethod, req.RelativeUrl, body, msg.Headers);
                }
                _log.Info("OUT: {0} {1} {2}", req.HttpMethod.ToString().ToUpper(), relativeUrl, body);
                HttpResponseMessage resp = await _http.SendAsync(msg, HttpCompletionOption.ResponseContentRead);
                string content = await resp.Content.ReadAsStringAsync();
                // Truncate() to avoid logging 500Kb of data.
                _log.Info("IN: HTTP {0} ({1}): {2}", (int)resp.StatusCode, resp.StatusCode, Util.Strings.Truncate(content));
                var res = new TResponse();
                res.Parse(resp.StatusCode, content);
                return res;
            }
            catch (Exception e)
            {
                _log.Warn(e, "IO error");
                throw;
            }
        }

        static string ToJsonString(IEnumerable<KV> param)
        {
            if (param == null) return null;
            string obj = String.Join(", ", param.Where(p => p.Value != null)
                                                .Select(p => String.Format("\"{0}\": \"{1}\"", p.Key, p.Value)));
            return String.Format("{{{0}}}", obj);
        }

        static string ToUrlQuery(IEnumerable<KV> param)
        {
            if (param == null) return null;
            return String.Join("&", param.Where(p => p.Value != null)
                                         .Select(p => String.Format("{0}={1}", p.Key, p.Value)));
        }

        static string AppendQueryParams(string url, string query)
        {
            if (String.IsNullOrEmpty(query)) return url;
            string delim = url.Contains("?") ? "&" : "?";
            return String.Format("{0}{1}{2}", url, delim, query);
        }
    }
}
