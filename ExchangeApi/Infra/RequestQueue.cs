using Conditions;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi
{
    public class RequestQueue
    {
        class Entry
        {
            public Func<bool> Request;
            public DateTime Deadline;
            public Action<bool> Done;
        }

        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        readonly Scheduler _scheduler;
        Queue<Entry> _queue = new Queue<Entry>();
        long _numActiveRequests = 0;
        readonly object _monitor = new object();

        public RequestQueue(Scheduler scheduler)
        {
            Condition.Requires(scheduler, "scheduler").IsNotNull();
            _scheduler = scheduler;
        }

        // The request can be invoked zero or more times. As soon as it returns true,
        // done(true) is called and the request is never called again. If the request doesn't
        // return true by the deadline, done(false) is called.
        //
        // Request can be called from any thread. Done is always called from the scheduler thread.
        public void Send(Func<bool> request, DateTime deadline, Action<bool> done)
        {
            Condition.Requires(request, "request").IsNotNull();
            Condition.Requires(done, "done").IsNotNull();
            var entry = new Entry() { Request = request, Deadline = deadline, Done = done };
            lock (_monitor)
            {
                _queue.Enqueue(entry);
                ++_numActiveRequests;
                if (_queue.Count == 1) _scheduler.Schedule(TrySend);
            }
            _scheduler.Schedule(deadline, () => TryTimeout(entry));
        }

        public void TryProcess()
        {
            lock (_monitor)
            {
                if (_queue.Count > 0) _scheduler.Schedule(TrySend);
            }
        }

        // Runs in the scheduler thread.
        void TryTimeout(Entry entry)
        {
            if (entry.Request == null) return;
            Finish(entry, success: false);
            bool atHead;
            lock (_monitor)
            {
                Condition.Requires(_queue, "_queue").IsNotEmpty();
                atHead = Object.ReferenceEquals(entry, _queue.Peek());
            }
            if (atHead) TrySend();
        }

        // Runs in the scheduler thread.
        void Finish(Entry entry, bool success)
        {
            Condition.Requires(entry.Request, "entry.Request").IsNotNull();
            Condition.Requires(entry.Done, "entry.Done").IsNotNull();
            var done = entry.Done;
            entry.Request = null;
            entry.Done = null;
            lock (_monitor) --_numActiveRequests;
            try { done.Invoke(success); }
            catch (Exception e) { _log.Warn(e, "Ignoring exception from user callback"); }
        }

        // Runs in the scheduler thread.
        void TrySend()
        {
            Entry head;
            lock (_monitor)
            {
                while (true)
                {
                    if (_queue.Count == 0) return;
                    if (_queue.Peek().Request != null)
                    {
                        head = _queue.Peek();
                        break;
                    }
                    _queue.Dequeue();
                }
            }
            // TryTimeout will get rid of this entry soon.
            if (head.Deadline < DateTime.UtcNow) return;
            bool success = false;
            try { success = head.Request.Invoke(); }
            catch (Exception e) { _log.Warn(e, "Ignoring exception from user request"); }
            if (!success) return;
            Finish(head, success);
            lock (_monitor)
            {
                _queue.Dequeue();
                if (_queue.Count > 0) _scheduler.Schedule(TrySend);
            }
        }
    }
}
