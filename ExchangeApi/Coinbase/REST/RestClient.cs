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

namespace ExchangeApi.Coinbase.REST
{
    // See https://docs.exchange.coinbase.com/#api.
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
            // Coinbase returns HTTP 400 (Bad Request) if User-Agent isn't set.
            _http.DefaultRequestHeaders.Add("User-Agent", "romkatv.github.com");
        }

        // Throws on timeouts, server and parse errors. Never returns null.
        //
        // Order book can be pretty big. 500Kb HTTP response with 8k orders in it is normal. Pulling
        // it takes about 1 second. Don't call this method too frequently or Coinbase will ban you.
        //
        // From https://docs.exchange.coinbase.com/#get-product-order-book:
        //
        //   Level 3 is only recommended for users wishing to maintain a full real-time order book
        //   using the websocket stream. Abuse of Level 3 via polling will cause your access to be
        //   limited or blocked.
        //
        // See https://api.exchange.coinbase.com/products for the full list of products.
        // One example is "BTC-USD".
        public OrderBook GetProductOrderBook(string product)
        {
            Condition.Requires(product, "product").IsNotNullOrEmpty();
            string content = SendRequest(HttpMethod.Get, String.Format("/products/{0}/book?level=3", product));
            // {
            //   "sequence": 12345,
            //   "bids": [[ "295.96", "0.05", "3b0f1225-7f84-490b-a29f-0faef9de823a" ]...],
            //   "asks": [[ "296.12", "0.17", "da863862-25f4-4868-ac41-005d11ab0a5f" ]...],
            // }
            JObject root = JObject.Parse(content);
            var res = new OrderBook() { Sequence = (long)root["sequence"], Orders = new List<Order>() };
            Action<string, Side> ParseOrders = (field, side) =>
            {
                JArray orders = (JArray)root[field];
                Condition.Requires(orders, "orders").IsNotNull();
                foreach (var order in orders)
                {
                    res.Orders.Add(new Order()
                    {
                        Id = (string)order[2],
                        Amount = new Amount()
                        {
                            Side = side,
                            Price = order[0].AsDecimal(),
                            Quantity = order[1].AsDecimal(),
                        }
                    });
                }
            };
            ParseOrders("bids", Side.Buy);
            ParseOrders("asks", Side.Sell);
            return res;
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
                // Truncate() to avoid logging 500Kb of data.
                _log.Info("IN: {0}", Util.Strings.Truncate(content));
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
