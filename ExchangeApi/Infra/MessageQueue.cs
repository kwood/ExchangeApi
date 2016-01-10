using Conditions;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi
{
    public class MessageQueue<Message>
    {
        class Entry
        {
            public Message Payload;
            public bool Consumed = false;
        }

        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        // MessageQueue can be in three states:
        //
        // 1. Initial: _all != null && _sink == null.
        //    All methods may be called. Pushed messages are buffered. They can be consumed from _unconsumed.
        // 2. Sink setup: _all != null && _sink != null.
        //    Only Push() may be called. SinkTo() is running in this state. Pushed messages are buffered.
        //    They are passed to the sink by SinkTo().
        // 3. Sink direct: _all == null && _sink != null.
        //    Only Push() may be called. SinkTo() has finished running. Pushed messages are passed directly
        //    to the sink.
        Queue<Entry> _all = new Queue<Entry>();
        readonly Queue<Entry> _unconsumed = new Queue<Entry>();
        Action<Message> _sink = null;
        readonly object _monitor = new object();

        public void Push(Message msg)
        {
            lock (_monitor)
            {
                if (_all != null)
                {
                    var entry = new Entry() { Payload = msg };
                    _all.Enqueue(entry);
                    if (_sink == null)
                    {
                        _unconsumed.Enqueue(entry);
                        System.Threading.Monitor.PulseAll(_monitor);
                    }
                    return;
                }
            }
            Condition.Requires(_sink, "_sink").IsNotNull("Internal error");
            try { _sink.Invoke(msg); }
            catch (Exception e) { _log.Warn(e, "Ignoring exception from message sink"); }
        }

        // It's illegal to call SinkTo() while Peek() is running.
        public Message Peek()
        {
            Message msg;
            bool res = PeekWithTimeout(TimeSpan.FromSeconds(-1), out msg);
            Condition.Requires(res).IsTrue("Internal error");
            return msg;
        }

        // It's illegal to call SinkTo() while PeekWithTimeout() is running.
        public bool PeekWithTimeout(TimeSpan timeout, out Message msg)
        {
            DateTime? deadline = null;
            if (timeout >= TimeSpan.Zero) deadline = DateTime.UtcNow + timeout;
            lock (_monitor)
            {
                Condition.Requires(_sink, "_sink").IsNull("Peeking is illegal after calling SinkTo()");
                while (_unconsumed.Count == 0)
                {
                    if (deadline.HasValue)
                    {
                        TimeSpan t = deadline.Value - DateTime.UtcNow;
                        if (t <= TimeSpan.Zero || !System.Threading.Monitor.Wait(_monitor, t))
                        {
                            msg = default(Message);
                            return false;
                        }
                    }
                    else
                    {
                        System.Threading.Monitor.Wait(_monitor);
                    }
                }
                msg = _unconsumed.Peek().Payload;
                return true;
            }
        }

        public void Skip()
        {
            lock (_monitor)
            {
                Condition.Requires(_sink, "_sink").IsNull("Skip() is illegal after calling SinkTo()");
                Condition.Requires(_unconsumed, "_unconsumed").IsNotEmpty("Nothing to Skip()");
                _unconsumed.Dequeue();
            }
        }

        public void Consume()
        {
            lock (_monitor)
            {
                Condition.Requires(_sink, "_sink").IsNull("Skip() is illegal after calling SinkTo()");
                Condition.Requires(_unconsumed, "_unconsumed").IsNotEmpty("Nothing to Consume()");
                _unconsumed.Dequeue().Consumed = true;
                if (_all.Count > 2 * _unconsumed.Count)
                {
                    _all = new Queue<Entry>(_all.Where(e => !e.Consumed));
                }
            }
        }

        // Can be called at most once.
        public void SinkTo(Action<Message> sink)
        {
            Condition.Requires(sink, "sink").IsNotNull();
            lock (_monitor)
            {
                Condition.Requires(_sink, "_sink").IsNull("SinkTo() can only be called once");
                // Switch to the "Sink setup" state.
                _sink = sink;
            }
            while (true)
            {
                Queue<Entry> q = null;
                lock (_monitor)
                {
                    if (_all.Count == 0)
                    {
                        // Switch to the "Sink direct" state.
                        _all = null;
                        return;
                    }
                    q = _all;
                    _all = new Queue<Entry>();
                }
                foreach (Entry entry in q)
                {
                    if (!entry.Consumed)
                    {
                        try { sink.Invoke(entry.Payload); }
                        catch (Exception e) { _log.Warn(e, "Ignoring exception from message sink"); }
                    }
                }
            }
        }
    }
}
