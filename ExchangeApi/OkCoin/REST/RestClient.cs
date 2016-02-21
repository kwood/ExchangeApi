﻿using Conditions;
using ExchangeApi.Util;
using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using KV = System.Collections.Generic.KeyValuePair<string, string>;

namespace ExchangeApi.OkCoin.REST
{
    public class RestClient : IDisposable
    {
        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        readonly Keys _keys;
        readonly HttpClient _http;

        public RestClient(string endpoint, Keys keys)
        {
            Condition.Requires(endpoint, "endpoint").IsNotNullOrEmpty();
            Condition.Requires(keys, "keys").IsNotNull();
            _keys = keys;
            _http = new HttpClient();
            _http.BaseAddress = new Uri(endpoint);
            _http.Timeout = TimeSpan.FromSeconds(10);
        }

        public void Dispose()
        {
            try { _http.Dispose(); }
            catch (Exception e) { _log.Warn(e, "Ignoring exception from HttpClient.Dispose()"); }
        }

        // Throws on HTTP timeouts, HTTP errors, parse errors and
        // application errors (when OkCoin gives us error_code).
        public List<FuturePosition> FuturePosition(Future future)
        {
            try
            {
                var param = new KV[]
                {
                    new KV("symbol", Serialization.AsString(future.CoinType, future.Currency)),
                    new KV("contract_type", Serialization.AsString(future.FutureType)),
                };
                string content = SendRequest(HttpMethod.Post, "future_position_4fix.do", Authenticated(param));
                var root = JObject.Parse(content);
                CheckErrorCode(root);
                var res = new List<FuturePosition>();
                foreach (JObject data in (JArray)root["holding"])
                {
                    Action<PositionType, string> AddPosition = (PositionType type, string prefix) =>
                    {
                        var quantity = (decimal)data[prefix + "_amount"];
                        if (quantity == 0) return;
                        res.Add(new FuturePosition()
                        {
                            Quantity = quantity,
                            PositionType = type,
                            AvgPrice = (decimal)data[prefix + "_price_avg"],
                            ContractId = (string)data["contract_id"],
                            Leverage = Serialization.ParseLeverage((string)data["lever_rate"]),
                        });
                    };
                    AddPosition(PositionType.Long, "buy");
                    AddPosition(PositionType.Short, "sell");
                }
                return res;
            }
            catch (Exception e)
            {
                _log.Warn(e, "RestClient.FuturePosition() failed");
                throw;
            }
        }

        void CheckErrorCode(JObject root)
        {
            string error = (string)root["error_code"];
            if (error != null)
            {
                throw new Exception(String.Format("REST response contains error code: {0}",
                                                  ErrorCode.Describe(int.Parse(error))));
            }
        }

        // Adds api_key and sign to the parameters. The result aliases the input.
        // `param` may be null (means empty). The result is never null.
        IEnumerable<KV> Authenticated(IEnumerable<KV> param)
        {
            param = param ?? new KV[0];
            param = param.Append(new KV("api_key", _keys.ApiKey));
            // Signature is added last because its value depends on all other parameters.
            return param.Append(new KV("sign", Authenticator.Sign(_keys, param)));
        }

        // Throws on timeouts and server errors. Never returns null.
        string SendRequest(HttpMethod method, string relativeUri, IEnumerable<KV> param)
        {
            if (param.Any())
            {
                _log.Info("OUT: {0} {1} {{{2}}}", method.ToString().ToUpper(), relativeUri,
                          String.Join(", ", param.Select(kv => String.Format("{0}={1}", kv.Key, kv.Value))));
            }
            else
            {
                _log.Info("OUT: {0} {1}", method.ToString().ToUpper(), relativeUri);
            }
            try
            {
                var form = new FormUrlEncodedContent(param);
                var req = new HttpRequestMessage();
                req.Method = method;
                if (method == HttpMethod.Get)
                {
                    string query = form.ReadAsStringAsync().Result;
                    if (query.Length > 0)
                    {
                        Condition.Requires(relativeUri, "relativeUri").DoesNotContain("?").DoesNotContain("#");
                        relativeUri = String.Format("{0}?{1}", relativeUri, query);
                    }
                }
                else if (method == HttpMethod.Post)
                {
                    req.Content = form;
                }
                req.RequestUri = new Uri(relativeUri, UriKind.Relative);
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