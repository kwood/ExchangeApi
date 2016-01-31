using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ExchangeApi
{
    // Scheduler runs all actions asynchronously and serially. Different actions may
    // run in different threads but they won't run concurrently.
    // Actions can be scheduled in the future. The execution order is what you
    // would expect: stable sorting by the scheduled time.
    class Scheduler : IDisposable
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        readonly ScheduledQueue<Action<bool>> _actions = new ScheduledQueue<Action<bool>>();
        readonly CancellationTokenSource _dispose = new CancellationTokenSource();
        readonly Task _loop;

        // Launches a background task. Call Dispose() to stop it.
        public Scheduler()
        {
            _loop = Task.Run(ActionLoop);
        }

        // Schedules the specified action to run at the specified time.
        // The argument of the action is true if and only if there are no ready
        // actions scheduled after it. In other words, it indicates the last ready
        // action.
        public void Schedule(Action<bool> action, DateTime when)
        {
            _actions.Push(action, when);
        }

        // Schedules the specified action to run ASAP.
        public void Schedule(Action<bool> action)
        {
            Schedule(action, DateTime.UtcNow);
        }

        // Blocks until the background thread is stopped.
        public void Dispose()
        {
            _log.Info("Disposing of ExchangeApi.Scheduler");
            _dispose.Cancel();
            try { _loop.Wait(); } catch { }
            _log.Info("ExchangeApi.Scheduler successfully disposed of");
        }

        async Task ActionLoop()
        {
            while (true)
            {
                ReadyMessage<Action<bool>> action = await _actions.Next(_dispose.Token);
                try
                {
                    action.Message.Invoke(action.Last);
                }
                catch (Exception e)
                {
                    _log.Warn(e, "Ignoring exception from the user action");
                }
            }
        }
    }
}
