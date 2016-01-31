﻿using Conditions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ExchangeApi
{
    class ReadyMessage<T>
    {
        // The payload.
        public T Message;
        // If true, there are no ready messages behind this one.
        // In other words, this is the last ready message.
        public bool Last;
    }

    // A queue of items with their expected processing time.
    // Thread-safe for multiple producers and single consumer.
    class ScheduledQueue<TValue>
    {
        readonly PriorityQueue<DateTime, TValue> _data = new PriorityQueue<DateTime, TValue>();
        readonly object _monitor = new object();
        // If not null, there may be an active Next() call that is waiting for the next value.
        Task _next = null;

        // Adds an element to the queue. It'll be ready for processing at the specified time.
        public void Push(TValue value, DateTime when)
        {
            lock (_monitor)
            {
                _data.Push(when, value);
                if (_next != null)
                {
                    Task next = _next;
                    _next = null;
                    Task.Run(() => next.RunSynchronously());
                }
            }
        }

        // Returns the next ready message when it becomes ready.
        // There must be at most one active task produced by Next().
        // In other words, it's illegal to call Next() before the task produced
        // by the previous call to Next() finishes.
        public async Task<ReadyMessage<TValue>> Next(CancellationToken cancel)
        {
            while (true)
            {
                // TimeSpan.FromMilliseconds(-1) is infinity for Task.Delay().
                TimeSpan delay = TimeSpan.FromMilliseconds(-1);
                Task next = null;
                lock (_monitor)
                {
                    if (_data.Any())
                    {
                        DateTime now = DateTime.UtcNow;
                        if (_data.Front().Key <= now)
                        {
                            // Note that we may leave _next in non-null state. That's OK.
                            return new ReadyMessage<TValue>()
                            {
                                Message = _data.Pop().Value,
                                Last = !_data.Any() || _data.Front().Key > now
                            };
                        }
                        delay = _data.Front().Key - now;
                        // Task.Delay() can't handle values above TimeSpan.FromMilliseconds(Int32.MaxValue).
                        if (delay > TimeSpan.FromMilliseconds(Int32.MaxValue))
                        {
                            delay = TimeSpan.FromMilliseconds(Int32.MaxValue);
                        }
                    }
                    _next = new Task(delegate { }, cancel);
                    next = _next;
                }
                // Task.WhenAny() returns Task<Task>, hence the double await.
                await await Task.WhenAny(Task.Delay(delay, cancel), next);
            }
        }
    }
}