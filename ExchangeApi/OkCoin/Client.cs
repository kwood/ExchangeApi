using Conditions;
using ExchangeApi.Util;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.OkCoin
{
    public class Config
    {
        // URI of the exchange.
        public string Endpoint = Instance.OkCoinCom;
        // If null, authenticated requests won't work.
        public Keys Keys = null;
        // The list of products the client is interested in.
        public List<Product> Products = new List<Product>();
        // If true, receive market data for Products.
        public bool EnableMarketData = false;
        // If true, enable authenticated trading requests for Products.
        // Requires valid Keys.
        public bool EnableTrading = false;
        // Client will fire all events on this Scheduler.
        public Scheduler Scheduler = new Scheduler();
    }

    // Thread-safe. All events fire on the Scheduler thread and therefore are serialized.
    // While at least one event handler is running, no other events may fire.
    public class Client : IDisposable
    {
        // Wait at most this long for the first reply when subscribing to a channel.
        static readonly TimeSpan SubscribeTimeout = TimeSpan.FromSeconds(10);

        readonly DurableConnection<IMessageIn, IMessageOut> _connection;
        readonly Product[] _marketDataSubscriptions = new Product[0];
        readonly HashSet<Tuple<ProductType, Currency>> _tradingSubscriptions =
            new HashSet<Tuple<ProductType, Currency>>();
        readonly Gateway _gateway;

        public Client(Config cfg)
        {
            Condition.Requires(cfg.Endpoint, "cfg.Endpoint").IsNotNullOrWhiteSpace();
            Condition.Requires(cfg.Products, "cfg.Products").IsNotNull();
            Condition.Requires(cfg.Scheduler, "cfg.Scheduler").IsNotNull();
            if (cfg.Keys != null)
            {
                Condition.Requires(cfg.Keys.ApiKey, "cfg.Keys.ApiKey").IsNotNullOrWhiteSpace();
                Condition.Requires(cfg.Keys.SecretKey, "cfg.Keys.SecretKey").IsNotNullOrWhiteSpace();
            }
            if (cfg.EnableMarketData)
            {
                _marketDataSubscriptions =
                    cfg.Products.GroupBy(p => p.Instrument).Select(p => p.First().Clone()).ToArray();
            }
            if (cfg.EnableTrading)
            {
                Condition.Requires(cfg.Keys, "cfg.Keys").IsNotNull();
                _tradingSubscriptions =
                    cfg.Products.Select(p => Tuple.Create(p.ProductType, p.Currency))
                       .GroupBy(p => p).Select(p => p.First()).ToHashSet();
            }
            Keys keys = cfg.Keys ?? new Keys() { ApiKey = "NONE", SecretKey = "NONE" };
            var connector = new CodingConnector<IMessageIn, IMessageOut>(
                new WebSocket.Connector(cfg.Endpoint), new Codec(keys));
            _connection = new DurableConnection<IMessageIn, IMessageOut>(connector, cfg.Scheduler);
            _gateway = new Gateway(_connection);
            _connection.OnConnection += OnConnection;
            _connection.OnMessage += OnMessage;
        }

        // Asynchronous. Events may fire even after Dispose() returns.
        public void Dispose()
        {
            _gateway.Dispose();
            _connection.Dispose();
        }

        // See comments in DurableSubscriber.
        public void Connect() { _connection.Connect(); }
        public void Disconnect() { _connection.Disconnect(); }
        public void Reconnect() { _connection.Reconnect(); }
        // Note that Connected == true doesn't mean we have an active connection to
        // the exchange. It merely means that the Client is in the "connected" state and
        // is trying to talk to the exchange.
        public bool Connected { get { return _connection.Connected; } }

        // Messages are never null.
        public event Action<TimestampedMsg<ProductDepth>, bool> OnProductDepth;
        public event Action<TimestampedMsg<ProductTrades>, bool> OnProductTrades;
        public event Action<TimestampedMsg<MyOrderUpdate>, bool> OnOrderUpdate;
        // When our future order gets filled, OnOrderUpdate triggers first, followed by OnFuturePositionsUpdate.
        // This isn't documented but seems to be the case in practice.
        public event Action<TimestampedMsg<FuturePositionsUpdate>, bool> OnFuturePositionsUpdate;

        // Action `done` will be called exactly once in the scheduler thread if
        // and only if Send() returns true. Its first argument is null on timeout.
        // The scond argument is `isLast` from Scheduler.
        //
        // Send() returns false in the following cases:
        //   * We aren't currently connected to the exchange.
        //   * There is an inflight message with the same channel.
        //   * IO error while sending.
        //
        // Send() throws iif req is null. It blocks until the data is sent.
        public bool Send(NewFutureRequest req, Action<TimestampedMsg<NewOrderResponse>, bool> done)
        {
            return _gateway.Send(req, CastCallback(done));
        }

        // See Send() above.
        public bool Send(CancelOrderRequest req, Action<TimestampedMsg<CancelOrderResponse>, bool> done)
        {
            return _gateway.Send(req, CastCallback(done));
        }

        Action<TimestampedMsg<IMessageIn>, bool> CastCallback<T>(Action<TimestampedMsg<T>, bool> action)
        {
            return (msg, isLast) =>
            {
                if (action == null) return;
                if (msg == null)
                {
                    action(null, isLast);
                    return;
                }
                action(new TimestampedMsg<T>() { Received = msg.Received, Value = (T)msg.Value }, isLast);
            };
        }

        void OnMessage(TimestampedMsg<IMessageIn> msg, bool isLast)
        {
            Condition.Requires(msg, "msg").IsNotNull();
            Condition.Requires(msg.Value, "msg.Value").IsNotNull();
            msg.Value.Visit(new MessageHandler(this, msg.Received, isLast));
        }

        void Subscribe(IReader<IMessageIn> reader, IWriter<IMessageOut> writer, IMessageOut req, bool consumeFirst)
        {
            writer.Send(req);
            string channel = Channels.FromMessage(req);
            var deadline = DateTime.UtcNow + SubscribeTimeout;
            while (true)
            {
                TimestampedMsg<IMessageIn> resp;
                if (!reader.PeekWithTimeout(DateTime.UtcNow - deadline, out resp))
                {
                    throw new Exception(String.Format(
                        "Timed out waiting for response to our subscription request: ({0}) {1}", req.GetType(), req));
                }
                if (channel == Channels.FromMessage(resp.Value))
                {
                    if (resp.Value.Error.HasValue)
                    {
                        throw new Exception(String.Format(
                            "Exchange returned error to our subscription request. Request: ({0}) {1}. Response: ({2}) {3}",
                            req.GetType(), req, resp.Value.GetType(), resp.Value));
                    }
                    if (consumeFirst) reader.Consume();
                    break;
                }
                reader.Skip();
            }
        }

        void OnConnection(IReader<IMessageIn> reader, IWriter<IMessageOut> writer)
        {
            foreach (Product p in _marketDataSubscriptions)
            {
                Subscribe(reader, writer, new MarketDataRequest() { Product = p, MarketData = MarketData.Depth60 },
                          consumeFirst: false);
                Subscribe(reader, writer, new MarketDataRequest() { Product = p, MarketData = MarketData.Trades },
                          consumeFirst: true);
            }
            foreach (var p in _tradingSubscriptions)
            {
                Subscribe(reader, writer, new MyOrdersRequest() { ProductType = p.Item1, Currency = p.Item2 },
                          consumeFirst: true);
            }
            if (_tradingSubscriptions.Select(p => p.Item1 == ProductType.Future).Any())
            {
                Subscribe(reader, writer, new FuturePositionsRequest(), consumeFirst: true);
            }
            Subscribe(reader, writer, new PingRequest(), consumeFirst: true);
        }

        class MessageHandler : IVisitorIn<object>
        {
            static readonly Logger _log = LogManager.GetCurrentClassLogger();

            readonly Client _client;
            readonly DateTime _received;
            readonly bool _isLast;

            public MessageHandler(Client client, DateTime received, bool isLast)
            {
                Condition.Requires(client, "client").IsNotNull();
                _client = client;
                _received = received;
                _isLast = isLast;
            }

            // These are handled by Gateway.
            public object Visit(NewOrderResponse msg) { return null; }
            public object Visit(CancelOrderResponse msg) { return null; }

            public object Visit(MyOrderUpdate msg)
            {
                try
                {
                    _client.OnOrderUpdate?.Invoke(
                        new TimestampedMsg<MyOrderUpdate>() { Received = _received, Value = msg }, _isLast);
                }
                catch (Exception e)
                {
                    _log.Warn(e, "Ignoring exception from OnOrderUpdate");
                }
                return null;
            }

            public object Visit(FuturePositionsUpdate msg)
            {
                try
                {
                    _client.OnFuturePositionsUpdate?.Invoke(
                        new TimestampedMsg<FuturePositionsUpdate>() { Received = _received, Value = msg }, _isLast);
                }
                catch (Exception e)
                {
                    _log.Warn(e, "Ignoring exception from OnFuturePositionsUpdate");
                }
                return null;
            }

            public object Visit(ProductTrades msg)
            {
                try
                {
                    _client.OnProductTrades?.Invoke(
                        new TimestampedMsg<ProductTrades>() { Received = _received, Value = msg }, _isLast);
                }
                catch (Exception e)
                {
                    _log.Warn(e, "Ignoring exception from OnProductTrades");
                }
                return null;
            }

            public object Visit(ProductDepth msg)
            {
                try
                {
                    _client.OnProductDepth?.Invoke(
                        new TimestampedMsg<ProductDepth>() { Received = _received, Value = msg }, _isLast);
                }
                catch (Exception e)
                {
                    _log.Warn(e, "Ignoring exception from OnProductDepth");
                }
                return null;
            }

            public object Visit(PingResponse msg)
            {
                return null;
            }
        }
    }
}
