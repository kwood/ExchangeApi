using Conditions;
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
        public Dictionary<FutureType, List<FuturePosition>> FuturePositions(Currency currency, CoinType coin)
        {
            try
            {
                var param = new KV[]
                {
                    new KV("symbol", Serialization.AsString(coin, currency)),
                    new KV("type", "1"),
                };
                string content = SendRequest(HttpMethod.Post, "future_position_4fix.do", Authenticated(param));
                var root = JObject.Parse(content);
                CheckErrorCode(root);
                var res = new Dictionary<FutureType, List<FuturePosition>>();
                foreach (var e in Util.Enum.Values<FutureType>())
                {
                    res.Add(e, new List<FuturePosition>());
                }
                foreach (JObject data in (JArray)root["holding"])
                {
                    Action<PositionType, string> AddPosition = (PositionType type, string prefix) =>
                    {
                        var quantity = data[prefix + "_amount"].AsDecimal();
                        if (quantity == 0) return;
                        FutureType ft = Serialization.ParseFutureType((string)data["contract_type"]);
                        string contractId = (string)data["contract_id"];
                        VerifyFutureType(ft, contractId);
                        res[ft].Add(new FuturePosition()
                        {
                            Quantity = quantity,
                            PositionType = type,
                            AvgPrice = data[prefix + "_price_avg"].AsDecimal(),
                            ContractId = contractId,
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

        public HashSet<long> FutureOrders(Future future)
        {
            try
            {
                var res = new HashSet<long>();
                for (int page = 0; true; ++page)
                {
                    var param = new KV[]
                    {
                        new KV("symbol", Serialization.AsString(future.CoinType, future.Currency)),
                        new KV("contract_type", Serialization.AsString(future.FutureType)),
                        new KV("status", "1"),        // unfilled orders
                        new KV("order_id", "-1"),     // all matching orders
                        new KV("page_length", "50"),  // this is the maximum supported value
                        new KV("current_page", page.ToString()),
                    };
                    string content = SendRequest(HttpMethod.Post, "future_order_info.do", Authenticated(param));
                    var root = JObject.Parse(content);
                    CheckErrorCode(root);

                    int total_orders = 0;
                    int new_orders = 0;
                    foreach (JObject data in (JArray)root["orders"])
                    {
                        int status = (int)data["status"];
                        // 0 is unfilled, 1 is partially filled.
                        if (status == 0 || status == 1)
                        {
                            if (res.Add((long)data["order_id"])) ++new_orders;
                        }
                        ++total_orders;
                    }
                    // Pagination on OKCoin is weird. They don't tell us if there are more results, so we have to guess.
                    // We can't just go over the pages until we get an empty one -- they always return the last page if
                    // current_page is too large.
                    if (total_orders < 50 || new_orders == 0) break;
                }
                return res;
            }
            catch (Exception e)
            {
                _log.Warn(e, "RestClient.FutureOrders() failed");
                throw;
            }
        }

        public HashSet<long> SpotOrders(Spot spot)
        {
            try
            {
                var res = new HashSet<long>();
                var param = new KV[]
                {
                    new KV("symbol", Serialization.AsString(spot.CoinType, spot.Currency)),
                    new KV("order_id", "-1")  // all open orders
                };
                string content = SendRequest(HttpMethod.Post, "order_info.do", Authenticated(param));
                var root = JObject.Parse(content);
                CheckErrorCode(root);

                foreach (JObject data in (JArray)root["orders"])
                {
                    int status = (int)data["status"];
                    // 0 is unfilled, 1 is partially filled.
                    if (status == 0 || status == 1) res.Add((long)data["order_id"]);
                }
                return res;
            }
            catch (Exception e)
            {
                _log.Warn(e, "RestClient.SpotOrders() failed");
                throw;
            }
        }

        // Verifies that FutureTypeFromContractId(contractId) matches `actual`.
        void VerifyFutureType(FutureType actual, string contractId)
        {
            DateTime now = DateTime.UtcNow;
            FutureType deduced = Settlement.FutureTypeFromContractId(contractId, now - TimeSpan.FromMinutes(1));
            // FutureTypeFromContractId() is unreliable very close to settlement time.
            // If it gives us the same value at now - 1m and now + 1m, we expect the actual contract_type set
            // by the exchange to be equal to what we deduce from contract_id. If it's not the case, it means our
            // algorithm is broken and the positions received from WebSocket can't be trusted.
            if (deduced != actual &&
                deduced == Settlement.FutureTypeFromContractId(contractId, now + TimeSpan.FromMinutes(2)))
            {
                _log.Fatal("Unexpected {{contract_type, contract_id}} pair: {{{0}, {1}}}. Expected contract_type: {2}.",
                           actual, contractId, deduced);
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
