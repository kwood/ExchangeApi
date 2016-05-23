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
        public event Action<string, TimestampedMsg<OrderBookDelta>> OnOrderBook;
        public event Action<string, TimestampedMsg<Trade>> OnTrade;

        void OnMessage(TimestampedMsg<WebSocket.IMessageIn> msg)
        {
            Condition.Requires(msg, "msg").IsNotNull();
            Condition.Requires(msg.Value, "msg.Value").IsNotNull();
            Condition.Requires(msg.Value.ProductId, "msg.Value.ProductId").IsNotNullOrEmpty();
            Condition.Requires(_products.ContainsKey(msg.Value.ProductId));

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
                if (trade != null && trade.Size > 0m)
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
