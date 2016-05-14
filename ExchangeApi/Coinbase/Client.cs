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
        // The list of products the client is interested in.
        // See https://api.exchange.coinbase.com/products for the full list of products.
        // One example is "BTC-USD".
        public List<string> Products = new List<string>();
        // If true, receive market data for Products.
        public bool EnableMarketData = false;
        // Client will fire all events on this Scheduler.
        public Scheduler Scheduler = new Scheduler();

        public Config Clone()
        {
            var res = new Config()
            {
                Endpoint = Endpoint,  // It's immutable.
                EnableMarketData = EnableMarketData,
                Scheduler = Scheduler,
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
        readonly DurableConnection<IMessageIn, IMessageOut> _connection;
        readonly REST.RestClient _restClient;

        readonly Dictionary<string, long> _productSeqNums = new Dictionary<string, long>();

        public Client(Config cfg)
        {
            Condition.Requires(cfg.Endpoint, "cfg.Endpoint").IsNotNull();
            Condition.Requires(cfg.Products, "cfg.Products").IsNotNull();
            Condition.Requires(cfg.Scheduler, "cfg.Scheduler").IsNotNull();
            _cfg = cfg.Clone();
            var connector = new CodingConnector<IMessageIn, IMessageOut>(
                new ExchangeApi.WebSocket.Connector(_cfg.Endpoint.WebSocket), new WebSocket.Codec());
            _connection = new DurableConnection<IMessageIn, IMessageOut>(connector, _cfg.Scheduler);
            _connection.OnConnection += OnConnection;
            _connection.OnMessage += OnMessage;
            _restClient = new REST.RestClient(_cfg.Endpoint.REST);
        }

        // Asynchronous. Events may fire even after Dispose() returns.
        public void Dispose()
        {
            _connection.Dispose();
        }

        // See comments in DurableSubscriber.
        public void Connect()
        {
            _connection.Connect();
        }
        public void Disconnect()
        {
            _connection.Disconnect();
        }
        public void Reconnect()
        {
            _connection.Reconnect();
        }

        // Note that Connected == true doesn't mean we have an active connection to
        // the exchange. It merely means that the Client is in the "connected" state and
        // is trying to talk to the exchange.
        public bool Connected { get { return _connection.Connected; } }

        public Scheduler Scheduler { get { return _cfg.Scheduler; } }

        // Messages are never null.
        public event Action<TimestampedMsg<REST.OrderBook>> OnOrderBook;

        public event Action<TimestampedMsg<OrderReceived>> OnOrderReceived;
        public event Action<TimestampedMsg<OrderOpen>> OnOrderOpen;
        public event Action<TimestampedMsg<OrderMatch>> OnOrderMatch;
        public event Action<TimestampedMsg<OrderDone>> OnOrderDone;
        public event Action<TimestampedMsg<OrderChange>> OnOrderChange;

        void OnMessage(TimestampedMsg<IMessageIn> msg)
        {
            Condition.Requires(msg, "msg").IsNotNull();
            Condition.Requires(msg.Value, "msg.Value").IsNotNull();
            msg.Value.Visit(new MessageHandler(this, msg.Received));
        }

        void OnConnection(IReader<IMessageIn> reader, IWriter<IMessageOut> writer)
        {
            if (_cfg.EnableMarketData)
            {
                foreach (string product in _cfg.Products)
                {
                    writer.Send(new SubscribeRequest() { ProductId = product });
                    RefreshOrderBook(product);
                }
            }
        }

        void RefreshOrderBook(string product)
        {
            REST.OrderBook orders = _restClient.GetProductOrderBook(product);
            _productSeqNums[product] = orders.Sequence;
            try
            {
                OnOrderBook?.Invoke(new TimestampedMsg<REST.OrderBook>() { Received = DateTime.Now, Value = orders });
            }
            catch (Exception e)
            {
                _log.Warn(e, "Ignoring exception from OnOrderBook");
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

            public object Visit(OrderReceived msg)
            {
                if (!CheckSeqNum(msg.ProductId, msg.Sequence)) return null;
                try
                {
                    _client.OnOrderReceived?.Invoke(
                        new TimestampedMsg<OrderReceived>() { Received = _received, Value = msg });
                }
                catch (Exception e)
                {
                    _log.Warn(e, "Ignoring exception from OnOrderReceived");
                }
                return null;
            }

            public object Visit(OrderOpen msg)
            {
                if (!CheckSeqNum(msg.ProductId, msg.Sequence)) return null;
                try
                {
                    _client.OnOrderOpen?.Invoke(
                        new TimestampedMsg<OrderOpen>() { Received = _received, Value = msg });
                }
                catch (Exception e)
                {
                    _log.Warn(e, "Ignoring exception from OnOrderOpen");
                }
                return null;
            }

            public object Visit(OrderMatch msg)
            {
                if (!CheckSeqNum(msg.ProductId, msg.Sequence)) return null;
                try
                {
                    _client.OnOrderMatch?.Invoke(
                        new TimestampedMsg<OrderMatch>() { Received = _received, Value = msg });
                }
                catch (Exception e)
                {
                    _log.Warn(e, "Ignoring exception from OnOrderMatch");
                }
                return null;
            }

            public object Visit(OrderDone msg)
            {
                if (!CheckSeqNum(msg.ProductId, msg.Sequence)) return null;
                try
                {
                    _client.OnOrderDone?.Invoke(
                        new TimestampedMsg<OrderDone>() { Received = _received, Value = msg });
                }
                catch (Exception e)
                {
                    _log.Warn(e, "Ignoring exception from OnOrderDone");
                }
                return null;
            }

            public object Visit(OrderChange msg)
            {
                if (!CheckSeqNum(msg.ProductId, msg.Sequence)) return null;
                try
                {
                    _client.OnOrderChange?.Invoke(
                        new TimestampedMsg<OrderChange>() { Received = _received, Value = msg });
                }
                catch (Exception e)
                {
                    _log.Warn(e, "Ignoring exception from OnOrderChange");
                }
                return null;
            }

            bool CheckSeqNum(string product, long seq)
            {
                long prev = _client._productSeqNums[product];
                if (seq == prev + 1)
                {
                    _client._productSeqNums[product] = seq;
                    return true;
                }
                if (seq <= prev)
                {
                    _log.Info("Ignoring message with sequence {0} for {1}: already at {2}", seq, product, prev);
                    return false;
                }
                _log.Warn("Detected a gap in sequence numbers for {0}: {1} => {2}. Fetching the full order book.",
                          product, prev, seq);
                _client.RefreshOrderBook(product);
                return false;
            }
        }
    }
}
