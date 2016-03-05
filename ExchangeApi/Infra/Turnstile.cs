using Conditions;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi
{
    // Turnstile is a wrapper around DurableConnection for request-reply communication
    // where at most one inflight request can exist at any given time.
    //
    // Thread-safe.
    class Turnstile<Req, Resp>
    {
        class Callback
        {
            // Not null. Readonly.
            public Action<TimestampedMsg<Req>> Done;
        }

        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        // TODO: support per-request timeouts. Do it only after long timeouts stop having
        // negative effects on memory usage.

        // If we can't send a request within this time span, give up.
        static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(1);
        // If we don't receive a reply to our request within this time span, give up.
        static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(10);

        readonly DurableConnection<Req, Resp> _connection;
        Callback _inflight = null;
        // Protects _inflight and its value.
        readonly object _monitor = new object();
        readonly RequestQueue _queue;

        public Turnstile(DurableConnection<Req, Resp> connection)
        {
            Condition.Requires(connection, "connection").IsNotNull();
            _connection = connection;
            _connection.OnDisconnected += OnDisconnected;
            _queue = new RequestQueue(connection.Scheduler);
        }

        // If there are no inflight requests, sends the request right away.
        // Otherwise puts it in a queue. When the current inflight request finishes,
        // the next one will be sent.
        // Inflight requests finish either when OnReply() is called or they time out.
        public void Send(Resp msg, Action<TimestampedMsg<Req>> done)
        {
            DateTime deadline = DateTime.UtcNow + RequestTimeout;
            _queue.Send(() => TrySend(msg, deadline, done), deadline, (bool success) =>
            {
                if (!success)
                {
                    _log.Warn("Unable to send request to the exchange: timed out in Turnstile.");
                    done.Invoke(null);
                }
            });
        }

        // Processes the reply to the last request that we sent.
        // It's the caller's responsibility to guarantee that this message is indeed the
        // reply and not some other unrelated message.
        public void OnReply(TimestampedMsg<Req> msg)
        {
            Condition.Requires(msg, "msg").IsNotNull();
            Callback cb;
            lock (_monitor)
            {
                if (_inflight == null)
                {
                    _log.Error("Received response to a request we didn't send: ({0}) {1}", msg.Value.GetType(), msg.Value);
                    return;
                }
                cb = _inflight;
                _inflight = null;
            }
            _connection.Scheduler.Schedule(() => cb.Done(msg));
            _queue.TryProcess();
        }

        // Action `done` will be called exactly once in the scheduler thread if
        // and only if Send() returns true. Its argument is null on timeout.
        //
        // Returns false if there is already an inflight message.
        bool TrySend(Resp msg, DateTime deadline, Action<TimestampedMsg<Req>> done)
        {
            Condition.Requires(done, "done").IsNotNull();
            var cb = new Callback() { Done = done };
            lock (_monitor)
            {
                if (_inflight != null)
                {
                    return false;
                }
                _inflight = cb;
            }
            try
            {
                _connection.SendAsync(msg, deadline, (bool success) => OnSent(success, cb));
            }
            catch (Exception e)
            {
                _log.Fatal(e, "Unexpected unrecoverable exception");
                throw;
            }
            return true;
        }

        void OnDisconnected()
        {
            // We lost connection to the exchange. Reply to the inflight request won't come.
            Callback cb;
            lock (_monitor)
            {
                if (_inflight == null) return;
                cb = _inflight;
                _inflight = null;
            }
            _log.Warn("Disconnected before receiving a reply from the exchange.");
            try { cb.Done.Invoke(null); }
            catch (Exception e) { _log.Warn(e, "Ignoring exception from user callback"); }
            _queue.TryProcess();
        }

        void OnSent(bool success, Callback cb)
        {
            lock (_monitor)
            {
                // This can happen if we get a reply before we know our request is sent successfully.
                if (cb != _inflight) return;
                if (!success)
                {
                    _inflight = null;
                }
            }
            if (success)
            {
                _connection.Scheduler.Schedule(DateTime.UtcNow + ReplyTimeout, () => MaybeTimeout(cb));
            }
            else
            {
                _log.Warn("Unable to send request to the exchange: timed out in DurableConnection.");
                try { cb.Done.Invoke(null); }
                catch (Exception e) { _log.Warn(e, "Ignoring exception from user callback"); }
                _queue.TryProcess();
            }
        }

        void MaybeTimeout(Callback cb)
        {
            Condition.Requires(cb, "cb").IsNotNull();
            lock (_monitor)
            {
                if (cb != _inflight) return;  // Happy path: we already processed a reply to this request.
                _inflight = null;
                _log.Warn("Giving up waiting for response. Time out. Triggering reconnection.");
                // It's unsafe to keep using the same connection. If a reply to our request were
                // to come later, we could match it to a wrong future request.
                // We assume that replies to requests sent in one connection can't be delivered in another.
                _connection.Reconnect();
            }
            try { cb.Done.Invoke(null); }
            catch (Exception e) { _log.Warn(e, "Ignoring exception from user callback"); }
            _queue.TryProcess();
        }
    }
}
