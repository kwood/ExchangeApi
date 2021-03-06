﻿using Conditions;
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
        public Instance Endpoint = Instance.OkCoinCom;
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

        public Config Clone()
        {
            var res = new Config()
            {
                Endpoint = Endpoint,  // It's immutable.
                EnableMarketData = EnableMarketData,
                EnableTrading = EnableTrading,
                Scheduler = Scheduler,
            };
            if (Keys != null)
            {
                res.Keys = new Keys() { ApiKey = Keys.ApiKey, SecretKey = Keys.SecretKey };
            }
            if (Products != null)
            {
                res.Products = Products.Select(p => p.Clone()).ToList();
            }
            return res;
        }
    }

    // Thread-safe. All events fire on the Scheduler thread and therefore are serialized.
    // While at least one event handler is running, no other events may fire.
    public class Client : IDisposable
    {
        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        // Wait at most this long for the first reply when subscribing to a channel.
        static readonly TimeSpan SubscribeTimeout = TimeSpan.FromSeconds(10);
        // Wait at most this long when cancalling an order during connection.
        static readonly TimeSpan CancelTimeout = TimeSpan.FromSeconds(10);
        // Send pings every this often. If we don't receive anything from the remote side in
        // 30 seconds, we close the connection and open a new one. Pinging every 10 seconds
        // works well with that.
        static readonly TimeSpan PingPeriod = TimeSpan.FromSeconds(10);
        // How often to poll for spot positions.
        static readonly TimeSpan SpotPositionPollPeriod = TimeSpan.FromSeconds(60);

        readonly Config _cfg;
        readonly DurableConnection<IMessageIn, IMessageOut> _connection;
        readonly WebSocket.Gateway _gateway;
        readonly REST.RestClient _restClient;
        readonly PeriodicAction _pinger;
        readonly FuturePositionPoller _futurePositionPoller;
        readonly PeriodicAction _spotPositionPoller;

        public Client(Config cfg)
        {
            Condition.Requires(cfg.Endpoint, "cfg.Endpoint").IsNotNull();
            Condition.Requires(cfg.Products, "cfg.Products").IsNotNull();
            Condition.Requires(cfg.Scheduler, "cfg.Scheduler").IsNotNull();
            if (cfg.Keys != null)
            {
                Condition.Requires(cfg.Keys.ApiKey, "cfg.Keys.ApiKey").IsNotNullOrWhiteSpace();
                Condition.Requires(cfg.Keys.SecretKey, "cfg.Keys.SecretKey").IsNotNullOrWhiteSpace();
            }
            if (cfg.EnableTrading)
            {
                Condition.Requires(cfg.Keys, "cfg.Keys").IsNotNull();
            }
            _cfg = cfg.Clone();
            _cfg.Keys = _cfg.Keys ?? new Keys() { ApiKey = "NONE", SecretKey = "NONE" };
            var connector = new CodingConnector<IMessageIn, IMessageOut>(
                new ExchangeApi.WebSocket.Connector(_cfg.Endpoint.WebSocket), new WebSocket.Codec(_cfg.Keys));
            _connection = new DurableConnection<IMessageIn, IMessageOut>(connector, _cfg.Scheduler);
            _gateway = new WebSocket.Gateway(_connection);
            _connection.OnConnection += OnConnection;
            _connection.OnMessage += OnMessage;
            _restClient = new REST.RestClient(_cfg.Endpoint.REST, _cfg.Keys);
            _pinger = new PeriodicAction(_cfg.Scheduler, PingPeriod, PingPeriod, Ping);
            _futurePositionPoller = new FuturePositionPoller(
                _restClient, _cfg.Scheduler,
                _cfg.EnableTrading ? _cfg.Products : new List<Product>());
            _futurePositionPoller.OnFuturePositions += msg => OnFuturePositionsUpdate?.Invoke(msg);
            _spotPositionPoller = new PeriodicAction(_cfg.Scheduler, SpotPositionPollPeriod, SpotPositionPollPeriod, PollSpotPositions);
        }

        // Asynchronous. Events may fire even after Dispose() returns.
        public void Dispose()
        {
            _futurePositionPoller.Dispose();
            _pinger.Dispose();
            _connection.Dispose();
        }

        // See comments in DurableSubscriber.
        public void Connect()
        {
            _futurePositionPoller.Connect();
            _connection.Connect();
        }
        public void Disconnect()
        {
            _connection.Disconnect();
            _futurePositionPoller.Disconnect();
        }
        public void Reconnect()
        {
            _futurePositionPoller.Connect();
            _connection.Reconnect();
        }

        // Note that Connected == true doesn't mean we have an active connection to
        // the exchange. It merely means that the Client is in the "connected" state and
        // is trying to talk to the exchange.
        public bool Connected { get { return _connection.Connected; } }

        public Scheduler Scheduler { get { return _cfg.Scheduler; } }

        // Messages are never null.
        public event Action<TimestampedMsg<ProductDepth>> OnProductDepth;
        public event Action<TimestampedMsg<ProductTrades>> OnProductTrades;
        public event Action<TimestampedMsg<MyOrderUpdate>> OnOrderUpdate;
        // When our future order gets filled, OnOrderUpdate triggers first, followed by OnFuturePositionsUpdate.
        // This isn't documented but seems to be the case in practice.
        //
        // This event may fire even if our possition didn't change. In fact, it fires each time we place an
        // order.
        //
        // On rare occasions a stale position may be delivered. In such cases the fresh position is always
        // delivered soon afterwards. For example, if the position has changed at times T0, T1, T2, it's
        // possible that OnFuturePositionsUpdate will see the following updates: T1, T0, T1, T2.
        // After T0 is delivered, the following T1 is delivered immediately.
        public event Action<TimestampedMsg<FuturePositionsUpdate>> OnFuturePositionsUpdate;

        public event Action<TimestampedMsg<SpotPositionsUpdate>> OnSpotPositionsUpdate;

        // Action `done` will be called exactly once in the scheduler thread.
        // Its argument will be null on timeout.
        //
        // Send() throws iff req is null. It doesn't block.
        public void Send(NewSpotRequest req, Action<TimestampedMsg<NewOrderResponse>> done)
        {
            _gateway.Send(req, CastCallback(done));
        }

        // See Send() above.
        public void Send(NewFutureRequest req, Action<TimestampedMsg<NewOrderResponse>> done)
        {
            _gateway.Send(req, CastCallback(done));
        }

        // See Send() above.
        public void Send(CancelOrderRequest req, Action<TimestampedMsg<CancelOrderResponse>> done)
        {
            _gateway.Send(req, CastCallback(done));
        }

        Action<TimestampedMsg<IMessageIn>> CastCallback<T>(Action<TimestampedMsg<T>> action)
        {
            return (msg) =>
            {
                if (action == null) return;
                if (msg == null)
                {
                    action(null);
                    return;
                }
                action(new TimestampedMsg<T>() { Received = msg.Received, Value = (T)msg.Value });
            };
        }

        void OnMessage(TimestampedMsg<IMessageIn> msg)
        {
            Condition.Requires(msg, "msg").IsNotNull();
            Condition.Requires(msg.Value, "msg.Value").IsNotNull();
            msg.Value.Visit(new MessageHandler(this, msg.Received));
        }

        void Subscribe(IReader<IMessageIn> reader, IWriter<IMessageOut> writer, IMessageOut req, bool consumeFirst)
        {
            writer.Send(req);
            string channel = WebSocket.Channels.FromMessage(req);
            var deadline = DateTime.UtcNow + SubscribeTimeout;
            while (true)
            {
                TimestampedMsg<IMessageIn> resp;
                if (!reader.PeekWithTimeout(DateTime.UtcNow - deadline, out resp))
                {
                    throw new Exception(String.Format(
                        "Timed out waiting for response to our subscription request: ({0}) {1}", req.GetType(), req));
                }
                if (channel == WebSocket.Channels.FromMessage(resp.Value))
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

        void CancelOrder(IReader<IMessageIn> reader, IWriter<IMessageOut> writer, Product product, long orderId)
        {
            var req = new CancelOrderRequest() { Product = product, OrderId = orderId };
            writer.Send(req);
            var deadline = DateTime.UtcNow + CancelTimeout;
            while (true)
            {
                TimestampedMsg<IMessageIn> resp;
                if (!reader.PeekWithTimeout(DateTime.UtcNow - deadline, out resp))
                {
                    throw new Exception("Timed out waiting for response to our order cancellation request");
                }
                var reply = resp.Value as CancelOrderResponse;
                if (reply != null)
                {
                    if (reply.Error.HasValue &&
                        reply.Error.Value != ErrorCode.OrderDoesNotExist1 &&
                        reply.Error.Value != ErrorCode.OrderDoesNotExist2)
                    {
                        throw new Exception(String.Format("Unable to cancel order {0}: {1}", orderId, reply));
                    }
                    // We need to consume the response so that it doesn't confuse Gateway.
                    reader.Consume();
                    break;
                }
                reader.Skip();
            }
        }

        void OnConnection(IReader<IMessageIn> reader, IWriter<IMessageOut> writer)
        {
            if (_cfg.EnableMarketData)
            {
                foreach (Product p in _cfg.Products.DedupBy(p => p.Instrument))
                {
                    Subscribe(reader, writer, new MarketDataRequest() { Product = p, MarketData = MarketData.Depth60 },
                              consumeFirst: false);
                    Subscribe(reader, writer, new MarketDataRequest() { Product = p, MarketData = MarketData.Trades },
                              consumeFirst: true);
                }
            }
            if (_cfg.EnableTrading)
            {
                // {ProductType, Currency} pairs without duplicates.
                var trading = _cfg.Products
                    .Select(p => Tuple.Create(p.ProductType, p.Currency))
                    .Dedup();
                foreach (var p in trading)
                {
                    Subscribe(reader, writer, new MyOrdersRequest() { ProductType = p.Item1, Currency = p.Item2 },
                              consumeFirst: true);
                }
                foreach (Currency c in _cfg.Products.Where(p => p.ProductType == ProductType.Spot)
                                           .Select(p => p.Currency).Dedup())
                {
                    Subscribe(reader, writer, new SpotPositionsRequest() {
                        Currency = c, RequestType = RequestType.Subscribe }, consumeFirst: true);
                    Subscribe(reader, writer, new SpotPositionsRequest() {
                        Currency = c, RequestType = RequestType.Poll }, consumeFirst: false);
                }
                if (trading.Where(p => p.Item1 == ProductType.Future).Any())
                {
                    Subscribe(reader, writer, new FuturePositionsRequest(), consumeFirst: true);
                }
                foreach (Product p in _cfg.Products.DedupBy(p => p.Instrument))
                {
                    foreach (long orderId in _restClient.OpenOrders(p))
                    {
                        CancelOrder(reader, writer, p, orderId);
                    }
                }
                // Note that positions may be delivered to OnFuturePositionsUpdate out of order.
                // Via polling we may get positions that were valid at time T1; at the same time,
                // we might have an incremental position update in the queue for T0, which will be
                // delivered later. However, when such reversion happens, there is aways an
                // incremental position update for T1 in the queue as well. This means that whenever
                // positions get delivered out of order, the mistake gets immediately corrected.
                _futurePositionPoller.PollNow();
            }
        }

        void Ping()
        {
            using (var writer = _connection.TryLock())
            {
                if (writer == null) return;
                try { writer.Send(new PingRequest()); }
                catch (Exception e) { _log.Info(e, "Can't send ping. No biggie."); }
            }
        }

        void PollSpotPositions()
        {
            using (var writer = _connection.TryLock())
            {
                if (writer == null) return;
                IEnumerable<Currency> currencies = _cfg.Products
                    .Where(p => p.ProductType == ProductType.Spot)
                    .Select(p => p.Currency)
                    .Dedup();
                foreach (Currency c in currencies)
                {
                    var req = new SpotPositionsRequest()
                    {
                        Currency = c,
                        RequestType = RequestType.Poll,
                    };
                    try
                    {
                        writer.Send(req);
                    }
                    catch (Exception e)
                    {
                        _log.Warn(e, "Can't poll spot positions.");
                        return;
                    }
                }
            }
        }

        class MessageHandler : IVisitorIn<object>
        {
            static readonly Logger _log = LogManager.GetCurrentClassLogger();

            readonly Client _client;
            readonly DateTime _received;

            public MessageHandler(Client client, DateTime received)
            {
                Condition.Requires(client, "client").IsNotNull();
                _client = client;
                _received = received;
            }

            // These are handled by Gateway.
            public object Visit(NewOrderResponse msg) { return null; }
            public object Visit(CancelOrderResponse msg) { return null; }

            public object Visit(MyOrderUpdate msg)
            {
                try
                {
                    _client.OnOrderUpdate?.Invoke(
                        new TimestampedMsg<MyOrderUpdate>() { Received = _received, Value = msg });
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
                        new TimestampedMsg<FuturePositionsUpdate>() { Received = _received, Value = msg });
                }
                catch (Exception e)
                {
                    _log.Warn(e, "Ignoring exception from OnFuturePositionsUpdate");
                }
                return null;
            }

            public object Visit(SpotPositionsUpdate msg)
            {
                try
                {
                    _client.OnSpotPositionsUpdate?.Invoke(
                        new TimestampedMsg<SpotPositionsUpdate>() { Received = _received, Value = msg });
                }
                catch (Exception e)
                {
                    _log.Warn(e, "Ignoring exception from OnSpotPositionsUpdate");
                }
                return null;
            }

            public object Visit(ProductTrades msg)
            {
                try
                {
                    _client.OnProductTrades?.Invoke(
                        new TimestampedMsg<ProductTrades>() { Received = _received, Value = msg });
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
                        new TimestampedMsg<ProductDepth>() { Received = _received, Value = msg });
                }
                catch (Exception e)
                {
                    _log.Warn(e, "Ignoring exception from OnProductDepth");
                }
                return null;
            }

            public object Visit(PingResponse msg)
            {
                // We send pings every 10 seconds. If we don't receive anything from the remote
                // side in 30 seconds, we close the connection and open a new one.
                // We don't do anything special when receiving a pong. Particularly, if the exchange
                // doesn't send us pongs, that's fine as long as they send us *something*.
                return null;
            }
        }
    }
}
