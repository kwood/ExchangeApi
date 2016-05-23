using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Coinbase.REST
{
    using Conditions;
    using Newtonsoft.Json.Linq;
    using System.Net.Http;
    using KV = KeyValuePair<string, string>;

    public interface IResponse
    {
        void Parse(string s);
    }

    public interface IRequest<TResponse> where TResponse : IResponse, new()
    {
        HttpMethod HttpMethod { get; }
        string RelativeUrl { get; }
        bool IsAuthenticated { get; }
        IEnumerable<KV> Parameters();
    }

    public class Order : Util.Printable<Order>
    {
        public string Id { get; set; }
        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
    }

    // Order book can be pretty big. 500Kb HTTP response with 8k orders in it is normal. Pulling
    // it takes about 1 second. Don't call this method too frequently or Coinbase will ban you.
    //
    // From https://docs.exchange.coinbase.com/#get-product-order-book:
    //
    //   Level 3 is only recommended for users wishing to maintain a full real-time order book
    //   using the websocket stream. Abuse of Level 3 via polling will cause your access to be
    //   limited or blocked.
    public class OrderBookResponse : Util.Printable<OrderBookResponse>, IResponse
    {
        // Server time. Coinbase doesn't give us server time together with the full order book,
        // so we retrieve it with a separate request BEFORE requesting the order book.
        // public DateTime Time { get; set; }
        public long Sequence { get; private set; }
        public List<Order> Bids { get; private set; }
        public List<Order> Asks { get; private set; }

        public void Parse(string s)
        {
            // {
            //   "sequence": 12345,
            //   "bids": [[ "295.96", "0.05", "3b0f1225-7f84-490b-a29f-0faef9de823a" ]...],
            //   "asks": [[ "296.12", "0.17", "da863862-25f4-4868-ac41-005d11ab0a5f" ]...],
            // }
            JObject root = Json.ParseObject(s);
            Func<JArray, List<Order>> ParseOrders = (orders) =>
            {
                Condition.Requires(orders, "orders").IsNotNull();
                var res = new List<Order>();
                foreach (var order in orders)
                {
                    res.Add(new Order()
                    {
                        Id = (string)order[2],
                        Price = (decimal)order[0],
                        Quantity = (decimal)order[1],
                    });
                }
                return res;
            };
            Sequence = (long)root["sequence"];
            Bids = ParseOrders((JArray)root["bids"]);
            Asks = ParseOrders((JArray)root["asks"]);
        }
    }

    public class OrderBookRequest : Util.Printable<OrderBookRequest>, IRequest<OrderBookResponse>
    {
        // See https://api.exchange.coinbase.com/products for the full list of products.
        // One example is "BTC-USD".
        //
        // Must not be null.
        public string Product;

        public HttpMethod HttpMethod { get { return HttpMethod.Get; } }
        public bool IsAuthenticated { get { return false; } }
        public IEnumerable<KV> Parameters() { return null; }

        public string RelativeUrl
        {
            get
            {
                Condition.Requires(Product, "Product").IsNotNullOrEmpty();
                return String.Format("/products/{0}/book?level=3", Product);
            }
        }
    }

    public class TimeResponse : Util.Printable<TimeResponse>, IResponse
    {
        public DateTime Time { get; private set; }

        public void Parse(string s)
        {
            Time = (DateTime)Json.ParseObject(s)["iso"];
        }
    }

    public class TimeRequest : Util.Printable<TimeRequest>, IRequest<TimeResponse>
    {
        public HttpMethod HttpMethod { get { return HttpMethod.Get; } }
        public bool IsAuthenticated { get { return false; } }
        public string RelativeUrl { get { return "/time"; } }
        public IEnumerable<KV> Parameters() { return null; }
    }

    public class CancelAllResponse : Util.Printable<CancelAllResponse>, IResponse
    {
        public void Parse(string s)
        {
            // The response contains order IDs of the cancelled orders. We don't need them.
        }
    }

    public class CancelAllRequest : Util.Printable<CancelAllRequest>, IRequest<CancelAllResponse>
    {
        // See https://api.exchange.coinbase.com/products for the full list of products.
        // One example is "BTC-USD".
        //
        // If null, cancel orders for all products.
        public string Product;

        public HttpMethod HttpMethod { get { return HttpMethod.Delete; } }
        public bool IsAuthenticated { get { return true; } }
        public string RelativeUrl { get { return "/orders"; } }

        public IEnumerable<KV> Parameters()
        {
            yield return new KV("product_id", Product);
        }
    }
}
