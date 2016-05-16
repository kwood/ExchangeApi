using Conditions;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi
{
    public class RateLimiter
    {
        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        readonly TimeSpan _window;
        readonly int _rate;
        // Invariant: _passed.Count <= _rate.
        readonly Queue<DateTime> _passed = new Queue<DateTime>();
        // Invariant: (_numWaiting > 0) => (_passed.Count == _rate).
        int _numWaiting = 0;
        readonly object _monitor = new object();

        public RateLimiter(TimeSpan window, int rate)
        {
            Condition.Requires(window, "window").IsGreaterThan(TimeSpan.Zero);
            Condition.Requires(rate, "rate").IsGreaterOrEqual(0);
            _window = window;
            _rate = rate;
        }

        // Doesn't block.
        //
        //   var limiter = new RateLimiter(TimeSpan.FromSeconds(1), 5);  // 5 QPS.
        //   while (true)
        //   {
        //     if (limiter.TryRequest()) SendRequest();
        //     else Console.WriteLine("Throttled");
        //   }
        public bool TryRequest()
        {
            try
            {
                DateTime now = DateTime.UtcNow;
                lock (_monitor)
                {
                    if (_numWaiting > 0) return false;
                    DequeOld(now - _window);
                    if (_passed.Count == _rate) return false;
                    Condition.Requires(_passed.Count, "_passed.Count").IsLessThan(_rate);
                    _passed.Enqueue(now);
                    return true;
                }
            }
            catch (Exception e)
            {
                _log.Fatal(e, "Broken invariant");
                throw;
            }
        }

        // Doesn't block. Never returns null.
        //
        //   var limiter = new RateLimiter(TimeSpan.FromSeconds(1), 5);  // 5 QPS.
        //   while (true)
        //   {
        //     limiter.Request().Wait();
        //     SendRequest();
        //   }
        public Task Request()
        {
            try
            {
                DateTime now = DateTime.UtcNow;
                TimeSpan delay = new TimeSpan(-1);
                lock (_monitor)
                {
                    if (_numWaiting == 0) DequeOld(now - _window);
                    if (_passed.Count == _rate)
                    {
                        delay = _passed.Peek() + _window - now;
                        // This can only happen if clock goes backwards.
                        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
                        ++_numWaiting;
                    }
                    else
                    {
                        Condition.Requires(_passed.Count, "_passed.Count").IsLessThan(_rate);
                        Condition.Requires(_numWaiting, "_numWaiting").IsEqualTo(0);
                        _passed.Enqueue(now);
                    }
                }
                // This condition corresponds to the two branches of `if` above.
                return delay >= TimeSpan.Zero ? Wait(delay) : Task.Run(delegate { });
            }
            catch (Exception e)
            {
                _log.Fatal(e, "Broken invariant");
                throw;
            }
        }

        Task Wait(TimeSpan delay)
        {
            Task res = new Task(delegate { });
            Task.Delay(delay).ContinueWith(task =>
            {
                DateTime now = DateTime.UtcNow;
                try
                {
                    lock (_monitor)
                    {
                        Condition.Requires(_numWaiting, "_numWaiting").IsGreaterThan(0);
                        Condition.Requires(_passed.Count, "_passed.Count").IsEqualTo(_rate);
                        --_numWaiting;
                        _passed.Dequeue();
                        _passed.Enqueue(now);
                    }
                }
                catch (Exception e)
                {
                    _log.Fatal(e, "Broken invariant");
                }
                res.RunSynchronously();
            });
            return res;
        }

        // Must be called under a lock.
        void DequeOld(DateTime cutoff)
        {
            while (_passed.Any() && _passed.Peek() <= cutoff) _passed.Dequeue();
        }
    }
}
