using Conditions;
using ExchangeApi.Util;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Coinbase
{
    public class Config
    {
        // URI of the exchange.
        public Instance Endpoint = Instance.Prod;
        // The list of products for which we want market data.
        // See https://api.exchange.coinbase.com/products for the full list of products.
        // One example is "BTC-USD".
        public List<string> Products = new List<string>();
        // Client will fire all events on this Scheduler.
        public Scheduler Scheduler = new Scheduler();
        public Keys Keys = null;

        public Config Clone()
        {
            var res = new Config()
            {
                Endpoint = Endpoint,  // It's immutable.
                Scheduler = Scheduler,
                Keys = Keys,
            };
            if (Products != null) res.Products = Products.ToList();
            return res;
        }
    }

    // Thread-safe. All events fire on the Scheduler thread and therefore are serialized.
    // While at least one event handler is running, no other events may fire.
    public class Client : IDisposable
    {
        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        readonly Config _cfg;
        readonly DurableConnection<WebSocket.IMessageIn, WebSocket.IMessageOut> _connection;
        readonly REST.RestClient _restClient;
        readonly OrderManager _orderManager;

        readonly Dictionary<string, OrderBookBuilder> _products = new Dictionary<string, OrderBookBuilder>();

        public Client(Config cfg)
        {
            Condition.Requires(cfg.Endpoint, "cfg.Endpoint").IsNotNull();
            Condition.Requires(cfg.Products, "cfg.Products").IsNotNull();
            Condition.Requires(cfg.Scheduler, "cfg.Scheduler").IsNotNull();
            _cfg = cfg.Clone();
            var connector = new CodingConnector<WebSocket.IMessageIn, WebSocket.IMessageOut>(
                new ExchangeApi.WebSocket.Connector(_cfg.Endpoint.WebSocket), new WebSocket.Codec());
            _connection = new DurableConnection<WebSocket.IMessageIn, WebSocket.IMessageOut>(connector, _cfg.Scheduler);
            _connection.OnConnection += OnConnection;
            _connection.OnMessage += OnMessage;
            _restClient = new REST.RestClient(_cfg.Endpoint.REST, _cfg.Keys);
            _orderManager = new OrderManager(_cfg.Scheduler);
            foreach (string product in _cfg.Products)
            {
                _products.Add(product, new OrderBookBuilder());
            }
        }

        // Asynchronous. Events may fire even after Dispose() returns.
        public void Dispose() { _connection.Dispose(); }

        // See comments in DurableSubscriber.
        public void Connect() { _connection.Connect(); }
        public void Disconnect() { _connection.Disconnect(); }
        public void Reconnect() { _connection.Reconnect(); }

        // Note that Connected == true doesn't mean we have an active connection to
        // the exchange. It merely means that the Client is in the "connected" state and
        // is trying to talk to the exchange.
        public bool Connected { get { return _connection.Connected; } }

        public Scheduler Scheduler { get { return _cfg.Scheduler; } }

        // Arguments are never null. The first is product ID.
        // If there is a trade, OnTrade triggers first, followed immediately by OnOrderBook.
        // The order book includes our own orders.
        public event Action<string, TimestampedMsg<OrderBookDelta>> OnOrderBook;
        // Doesn't include our own trades.
        public event Action<string, TimestampedMsg<Trade>> OnTrade;

        // The callback is called indeterminate number of times. Its argument is never null. The last
        // call is made either with TimestampedMsg.Value = null or with OrderUpdate.Finished = true.
        // The former happens in the following cases:
        //
        //   1. Unable to send request (e.g., due to exceeding the rate limits).
        //   2. The exchange didn't reply to our request for a long time and probably never will.
        //   3. After we attempted to cancel the order, the exchange told us it's invalid.
        public void Send(NewOrder req, Action<TimestampedMsg<OrderUpdate>> cb)
        {
            Condition.Requires(req, "req").IsNotNull();
            Condition.Requires(req.ProductId, "req.ProductId").IsNotNullOrEmpty();
            Condition.Requires(req.Price, "req.Price").IsGreaterThan(0m);
            Condition.Requires(req.Size, "req.Size").IsGreaterThan(0m);
            Condition.Requires(cb, "cb").IsNotNull();
            _cfg.Scheduler.Schedule(() =>
            {
                var clientOrderId = Guid.NewGuid().ToString();
                // We send our request from the scheduler thread in order to guarantee that "OrderReceived"
                // notification doesn't arrive before we call OrderManager.Add().
                Task<REST.NewOrderResponse> resp = _restClient.TrySendRequest(new REST.NewOrderRequest()
                {
                    ClientOrderId = clientOrderId,
                    Side = req.Side,
                    ProductId = req.ProductId,
                    Price = req.Price,
                    Size = req.Size,
                    TimeInForce = REST.TimeInForce.GTT,
                    CancelAfter = REST.CancelAfter.Min,  // Auto-cancel after 1 minute.
                    PostOnly = true,  // Don't take liquidity to avoid fees.
                    SelfTradePrevention = REST.SelfTradePrevention.DC,  // Decrease and cancel.
                });
                if (resp == null)
                {
                    // Rate limited.
                    try { cb(new TimestampedMsg<OrderUpdate>() { Received = DateTime.UtcNow, Value = null }); }
                    catch (Exception e) { _log.Warn(e, "Ignoring exception from order callback"); }
                    return;
                }
                _orderManager.Add(clientOrderId, cb);
                resp.ContinueWith(t =>
                {
                    // We don't care about failures here. We always act as if our request succeeds.
                    // We could in theory handle some types of errors specially: if we know the order defintely
                    // didn't succeed (e.g., HTTP 400), we don't really need to wait for 30 seconds to find that
                    // out. But in most cases the errors are inconclusive (e.g., timeout), and we have to wait.
                    if (t.IsFaulted) return;
                    REST.NewOrderResponse res = resp.Result;
                    // Rejected orders don't appear in the websocket feed.
                    if (res.Result == REST.NewOrderResult.Reject)
                        _cfg.Scheduler.Schedule(() => _orderManager.Reject(clientOrderId));
                });
                
            });
        }

        public void Send(CancelOrder order)
        {
            Condition.Requires(order, "order").IsNotNull();
            Condition.Requires(order.OrderId, "order.OrderId").IsNotNull();
            _cfg.Scheduler.Schedule(() =>
            {
                if (!_orderManager.CanCancel(order.OrderId))
                {
                    _log.Info(
                        "Ignoring cancellation request for an order that's already being cancelled: OrderID = {0}",
                        order.OrderId);
                    return;
                }
                Task<REST.CancelOrderResponse> resp =
                    _restClient.TrySendRequest(new REST.CancelOrderRequest() { OrderId = order.OrderId });
                if (resp == null)
                {
                    // Rate limited.
                    return;
                }
                _orderManager.Cancel(order.OrderId);
                resp.ContinueWith((Task<REST.CancelOrderResponse> t) =>
                {
                    // Ignore REST errors. See comments at the bottom of the other overload of Send().
                    if (t.IsFaulted) return;
                    if (t.Result.Result == REST.CancelOrderResult.InvalidOrder)
                        _cfg.Scheduler.Schedule(() => _orderManager.Invalidate(order.OrderId));
                });
            });
        }

        void OnMessage(TimestampedMsg<WebSocket.IMessageIn> msg)
        {
            Condition.Requires(msg, "msg").IsNotNull();
            Condition.Requires(msg.Value, "msg.Value").IsNotNull();
            Condition.Requires(msg.Value.ProductId, "msg.Value.ProductId").IsNotNullOrEmpty();
            Condition.Requires(_products.ContainsKey(msg.Value.ProductId));

            bool myFill = _orderManager.OnMessage(msg);

            OrderBookBuilder book = _products[msg.Value.ProductId];
            OrderBookDelta delta = null;
            Trade trade = null;
            bool ok = false;
            try
            {
                ok = book.OnOrderUpdate(msg.Value, out delta, out trade);
            }
            catch (Exception e)
            {
                _log.Error(e, "Unable to process order update");
            }

            if (ok)
            {
                if (!myFill && trade != null && trade.Size > 0m)
                {
                    try
                    {
                        OnTrade?.Invoke(
                            msg.Value.ProductId,
                            new TimestampedMsg<Trade>() { Received = msg.Received, Value = trade });
                    }
                    catch (Exception e)
                    {
                        _log.Warn(e, "Ignoring exception from OnTrade");
                    }
                }
                if (delta != null && (delta.Bids.Any() || delta.Asks.Any()))
                {
                    try
                    {
                        OnOrderBook?.Invoke(
                            msg.Value.ProductId,
                            new TimestampedMsg<OrderBookDelta>() { Received = msg.Received, Value = delta });
                    }
                    catch (Exception e)
                    {
                        _log.Warn(e, "Ignoring exception from OnOrderBook");
                    }
                }
            }
            else
            {
                RefreshOrderBook(msg.Value.ProductId, book);
            }
        }

        void OnConnection(IReader<WebSocket.IMessageIn> reader, IWriter<WebSocket.IMessageOut> writer)
        {
            foreach (var p in _products)
            {
                writer.Send(new WebSocket.SubscribeRequest() { ProductId = p.Key });
            }
            // This request will miss orders that aren't open yet on the exchange.
            _restClient.SendRequest(new REST.CancelAllRequest() { }).Wait();
            foreach (var p in _products)
            {
                RefreshOrderBook(p.Key, p.Value);
            }
        }

        void RefreshOrderBook(string product, OrderBookBuilder book)
        {
            // Coinbase doesn't give us server time together with the full order book,
            // so we retrieve it with a separate request BEFORE requesting the order book.
            DateTime serverTime = _restClient.SendRequest(new REST.TimeRequest()).Result.Time;
            REST.OrderBookResponse snapshot =
                _restClient.SendRequest(new REST.OrderBookRequest() { Product = product }).Result;
            DateTime received = DateTime.UtcNow;
            OrderBookDelta delta = book.OnSnapshot(serverTime, snapshot);  // Throws if the snapshot is malformed.
            if (delta != null && (delta.Bids.Any() || delta.Asks.Any()))
            {
                try
                {
                    OnOrderBook?.Invoke(
                        product,
                        new TimestampedMsg<OrderBookDelta>() { Received = received, Value = delta });
                }
                catch (Exception e)
                {
                    _log.Warn(e, "Ignoring exception from OnOrderBook");
                }
            }
        }
    }
}
