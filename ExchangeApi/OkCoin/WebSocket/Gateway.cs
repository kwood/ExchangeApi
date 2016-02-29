using Conditions;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.OkCoin.WebSocket
{
    // Thread-safe.
    class Gateway
    {
        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        readonly DurableConnection<IMessageIn, IMessageOut> _connection;
        readonly Dictionary<string, Turnstile<IMessageIn, IMessageOut>> _channels =
            new Dictionary<string, Turnstile<IMessageIn, IMessageOut>>();
        // Protects _channels.
        readonly object _monitor = new object();

        public Gateway(DurableConnection<IMessageIn, IMessageOut> connection)
        {
            Condition.Requires(connection, "connection").IsNotNull();
            _connection = connection;
            _connection.OnMessage += OnMessage;
        }

        // Action `done` will be called exactly once in the scheduler. Its argument will be null on timeout.
        public void Send(IMessageOut msg, Action<TimestampedMsg<IMessageIn>> done)
        {
            Condition.Requires(msg, "msg").IsNotNull();
            Condition.Requires(done, "done").IsNotNull();
            string channel = Channels.FromMessage(msg);
            Turnstile<IMessageIn, IMessageOut> turnstile;
            lock (_monitor)
            {
                if (!_channels.TryGetValue(channel, out turnstile))
                {
                    turnstile = new Turnstile<IMessageIn, IMessageOut>(_connection);
                    _channels.Add(channel, turnstile);
                }
            }
            turnstile.Send(msg, done);
        }

        void OnMessage(TimestampedMsg<IMessageIn> msg)
        {
            Condition.Requires(msg, "msg").IsNotNull();
            string channel = Channels.FromMessage(msg.Value);
            Turnstile<IMessageIn, IMessageOut> turnstile;
            lock (_monitor)
            {
                if (!_channels.TryGetValue(channel, out turnstile))
                {
                    // Market data, ping or somesuch.
                    return;
                }
            }
            turnstile.OnReply(msg);
        }
    }
}
