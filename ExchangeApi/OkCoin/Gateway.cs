using Conditions;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.OkCoin
{
    // Thread-safe.
    class Gateway : IDisposable
    {
        class Callback
        {
            // Null iif Done has already been called.
            public Action<TimestampedMsg<IMessageIn>, bool> Done;
        }

        static readonly Logger _log = LogManager.GetCurrentClassLogger();
        static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        readonly DurableConnection<IMessageIn, IMessageOut> _connection;
        // Invariant:
        //
        //   foreach (var kv in _inflight)
        //   {
        //     assert(kv.Value != null);
        //     assert(kv.Value.Done != null);
        //   }
        readonly Dictionary<string, Callback> _inflight = new Dictionary<string, Callback>();
        // Protects _inflight and all individual Callback instances.
        readonly object _monitor = new object();

        public Gateway(DurableConnection<IMessageIn, IMessageOut> connection)
        {
            Condition.Requires(connection, "connection").IsNotNull();
            _connection = connection;
            _connection.OnDisconnected += OnDisconnected;
            _connection.OnMessage += OnMessage;
        }

        // Action `done` will be called exactly once in the scheduler thread if
        // and only if Send() returns true. Its first argument is null on timeout.
        // The scond argument is `isLast` from Scheduler.
        //
        // Send() returns false in the following cases:
        //   * We aren't currently connected to the exchange.
        //   * There is an inflight message with the same channel.
        //   * IO error while sending.
        //
        // Send() throws iif any of the arguments are null. It blocks until the data is sent.
        public bool Send(IMessageOut msg, Action<TimestampedMsg<IMessageIn>, bool> done)
        {
            Condition.Requires(msg, "msg").IsNotNull();
            Condition.Requires(done, "done").IsNotNull();
            string channel = Channels.FromMessage(msg);
            var cb = new Callback() { Done = done };
            lock (_monitor)
            {
                if (_inflight.ContainsKey(channel))
                {
                    _log.Info("Channel {0} is busy. Can't send message: ({1}) {2}", channel, msg.GetType(), msg);
                    return false;
                }
                _inflight.Add(channel, cb);
            }
            using (IWriter<IMessageOut> writer = _connection.TryLock())
            {
                if (writer == null)
                {
                    lock (_monitor)
                    {
                        if (cb.Done == null)
                        {
                            _log.Fatal("Received reply to a request we didn't send. Request: ({0}) {1}",
                                       msg.GetType(), msg);
                            return true;
                        }
                        if (!_inflight.ContainsKey(channel) || !Object.ReferenceEquals(_inflight[channel], cb))
                        {
                            _log.Fatal("Broken invariant. Channel: {0}.", channel);
                        }
                        _log.Info("Not connected. Can't send message: ({0}) {1}", msg.GetType(), msg);
                        _inflight.Remove(channel);
                        cb.Done = null;
                        return false;
                    }
                }
                try
                {
                    writer.Send(msg);
                }
                catch (Exception e)
                {
                    lock (_monitor)
                    {
                        // If Done is null, it has already been called.
                        // For this to happen, our message should be successfully sent despite the
                        // fact that we got an IO error on our end. This is extremely unlikely but
                        // theoretically possible.
                        if (cb.Done == null) return true;
                        if (!_inflight.ContainsKey(channel) || !Object.ReferenceEquals(_inflight[channel], cb))
                        {
                            _log.Fatal("Broken invariant. Channel: {0}.", channel);
                        }
                        _log.Warn(e, "Error while sending message: ({0}) {1}", msg.GetType(), msg);
                        _inflight.Remove(channel);
                        cb.Done = null;
                        return false;
                    }
                }
            }
            _connection.Scheduler.Schedule(DateTime.UtcNow + RequestTimeout,
                                           isLast => MaybeTimeout(channel, cb, isLast));
            return true;
        }

        // Asynchronous. Callbacks may still fire after Dispose() returns.
        public void Dispose()
        {
            TimeoutEverything();
        }

        void TimeoutEverything()
        {
            lock (_monitor)
            {
                // Trigger timeout for all requests ASAP.
                foreach (var kv in _inflight)
                {
                    _connection.Scheduler.Schedule(isLast => MaybeTimeout(kv.Key, kv.Value, isLast));
                }
                _inflight.Clear();
            }
        }

        void OnDisconnected()
        {
            // We lost connection to the exchange. Replies to the inflight requests won't come.
            TimeoutEverything();
        }

        void OnMessage(TimestampedMsg<IMessageIn> msg, bool isLast)
        {
            Condition.Requires(msg, "msg").IsNotNull();
            string channel = Channels.FromMessage(msg.Value);
            Action<TimestampedMsg<IMessageIn>, bool> done;
            lock (_monitor)
            {
                Callback cb;
                if (!_inflight.TryGetValue(channel, out cb))
                {
                    // Not the kind of request that we care about (e.g., market data).
                    return;
                }
                _inflight.Remove(channel);
                done = cb.Done;
                cb.Done = null;
            }
            Condition.Requires(done, "done").IsNotNull();
            done.Invoke(msg, isLast);
        }

        void MaybeTimeout(string channel, Callback cb, bool isLast)
        {
            Condition.Requires(channel, "channel").IsNotNull();
            Condition.Requires(cb, "cb").IsNotNull();

            Action<TimestampedMsg<IMessageIn>, bool> done;
            lock (_monitor)
            {
                // Happy path: already received a reply.
                if (cb.Done == null) return;
                // This condition may be false if we are timing out requests due to a disconnect.
                // See OnDisconnected() above.
                if (_inflight.ContainsKey(channel) && Object.ReferenceEquals(_inflight[channel], cb))
                {
                    _log.Warn("Request timed out. Channel: {0}. Triggering reconnection.", channel);
                    _inflight.Remove(channel);
                    // It's unsafe to keep using the same connection. If a reply to our request were
                    // to come later, we could match it to a wrong future request.
                    _connection.Reconnect();
                }
                else
                {
                    _log.Warn("Disconnected before receiving a reply. Channel: {0}.", channel);
                }
                done = cb.Done;
                cb.Done = null;
            }
            done.Invoke(null, isLast);
        }
    }
}
