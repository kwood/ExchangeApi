using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Coinbase.REST
{
    using Conditions;
    using Newtonsoft.Json.Linq;
    using System.Net;
    using System.Net.Http;
    using KV = KeyValuePair<string, string>;

    public static class HttpStatusCodeExtension
    {
        public static void EnsureSuccess(this HttpStatusCode status)
        {
            int code = (int)status;
            if (code < 200 || code >= 300) throw new Exception("HTTP error: " + status);
        }
    }

    public interface IResponse
    {
        void Parse(HttpStatusCode status, string content);
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

        public void Parse(HttpStatusCode status, string s)
        {
            status.EnsureSuccess();
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
        public string Product { get; set; }

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

        public void Parse(HttpStatusCode status, string s)
        {
            status.EnsureSuccess();
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
        public void Parse(HttpStatusCode status, string s)
        {
            status.EnsureSuccess();
            // The response contains order IDs of the cancelled orders. We don't need them.
        }
    }

    public class CancelAllRequest : Util.Printable<CancelAllRequest>, IRequest<CancelAllResponse>
    {
        // See https://api.exchange.coinbase.com/products for the full list of products.
        // One example is "BTC-USD".
        //
        // If null, cancel orders for all products.
        public string Product { get; set; }

        public HttpMethod HttpMethod { get { return HttpMethod.Delete; } }
        public bool IsAuthenticated { get { return true; } }
        public string RelativeUrl { get { return "/orders"; } }

        public IEnumerable<KV> Parameters()
        {
            yield return new KV("product_id", Product);
        }
    }

    public enum NewOrderResult
    {
        Success,
        Reject,
    }

    public class NewOrderResponse : Util.Printable<NewOrderResponse>, IResponse
    {
        public string OrderId { get; private set; }
        public NewOrderResult Result { get; private set; }

        public void Parse(HttpStatusCode httpStatus, string s)
        {
            httpStatus.EnsureSuccess();
            JObject root = Json.ParseObject(s);
            OrderId = (string)root["id"];
            var status = (string)root["status"];
            Result = (string)root["status"] == "rejected" ? NewOrderResult.Reject : NewOrderResult.Success;
        }
    }

    public enum TimeInForce
    {
        // Good till canceled orders remain open on the book until canceled. This is the default behavior if
        // no policy is specified.
        GTC,
        // Good till time orders remain open on the book until canceled or the allotted cancel_after is
        // depleted on the matching engine. GTT orders are guaranteed to cancel before any other order is
        // processed after the CancelAfter timestamp which is returned by the API. A day is considered 24 hours.
        GTT,
        // Immediate or cancel orders instantly cancel the remaining size of the limit order instead of
        // opening it on the book.
        IOC,
        // Fill or kill orders are rejected if the entire size cannot be matched.
        FOK,
    }

    public enum CancelAfter
    {
        Min,   // One minute.
        Hour,  // One hour.
        Day,   // 24 hours.
    }

    public enum SelfTradePrevention
    {
        DC,  // Decrease and Cancel (default).
        CO,  // Cancel oldest.
        CN,  // Cancel newest.
        CB,  // Cancel both.
    }

    // Only limit orders are currently supported. Market and stop orders can be supported if necessary.
    public class NewOrderRequest : Util.Printable<NewOrderRequest>, IRequest<NewOrderResponse>
    {
        // [optional] Order ID selected by you to identify your order.
        public string ClientOrderId { get; set; }
        public Side Side { get; set; }
        // See https://api.exchange.coinbase.com/products for the full list of products.
        // One example is "BTC-USD".
        public string ProductId { get; set; }
        // [optional] Self-trade prevention flag.
        public SelfTradePrevention SelfTradePrevention { get; set; }
        // Price per bitcoin.
        public decimal Price { get; set; }
        // Amount of BTC to buy or sell.
        public decimal Size { get; set; }
        // [optional] GTC, GTT, IOC, or FOK (default is GTC).
        public TimeInForce TimeInForce { get; set; }
        // Requires TimeInForce to be GTT.
        public CancelAfter? CancelAfter { get; set; }
        // [optional] Invalid when TimeInForce is IOC or FOK.
        public bool PostOnly { get; set; }

        public HttpMethod HttpMethod { get { return HttpMethod.Post; } }
        public bool IsAuthenticated { get { return true; } }
        public string RelativeUrl { get { return "/orders"; } }

        public IEnumerable<KV> Parameters()
        {
            Condition.Requires(ProductId, "ProductId").IsNotNull();
            if (TimeInForce == TimeInForce.GTT)
                Condition.Requires(CancelAfter, "CancelAfter").IsNotNull();
            if (CancelAfter.HasValue)
                Condition.Requires(TimeInForce, "TimeInForce").IsEqualTo(TimeInForce.GTT);
            if (TimeInForce == TimeInForce.IOC || TimeInForce == TimeInForce.FOK)
                Condition.Requires(PostOnly, "PostOnly").IsFalse();
            yield return new KV("client_oid", ClientOrderId);
            yield return new KV("side", Side.ToString().ToLower());
            yield return new KV("product_id", ProductId);
            yield return new KV("stp", SelfTradePrevention.ToString().ToLower());
            yield return new KV("price", Price.ToString());
            yield return new KV("size", Size.ToString());
            yield return new KV("time_in_force", TimeInForce.ToString().ToUpper());
            if (CancelAfter.HasValue) yield return new KV("cancel_after", CancelAfter.ToString().ToLower());
            if (PostOnly) yield return new KV("post_only", "true");
        }
    }

    public enum CancelOrderResult
    {
        Success,
        InvalidOrder,
    }

    public class CancelOrderResponse : Util.Printable<CancelOrderResponse>, IResponse
    {
        public CancelOrderResult Result;

        public void Parse(HttpStatusCode status, string s)
        {
            // We need to differentiate between two types of errors:
            // 1. Order can't be cancelled because it's already done (filled or cancelled).
            // 2. All other errors.
            //
            // Coinbase gives us HTTP 400 plus a JSON object with a specific message string
            // in case of (1) but the error strings aren't documented. We can either rely on the
            // exact undocumented strings or assume that HTTP 400 always means invalid order.
            // In the absence of bugs the latter assumption should hold true, so we go with it.
            if (status == HttpStatusCode.BadRequest)
            {
                Result = CancelOrderResult.InvalidOrder;
                return;
            }
            status.EnsureSuccess();
            Result = CancelOrderResult.Success;
        }
    }

    public class CancelOrderRequest : Util.Printable<CancelOrderRequest>, IRequest<CancelOrderResponse>
    {
        // The server-assigned order id and not the optional ClientOrderId.
        public string OrderId { get; set; }

        public HttpMethod HttpMethod { get { return HttpMethod.Delete; } }
        public bool IsAuthenticated { get { return true; } }
        public IEnumerable<KV> Parameters() { return null; }

        public string RelativeUrl
        {
            get
            {
                Condition.Requires(OrderId, "OrderId").IsNotNullOrEmpty();
                return String.Format("/orders/{0}", OrderId);
            }
        }
    }
}
