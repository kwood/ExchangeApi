using Conditions;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi
{
    public interface IWriter<Out> : IDisposable
    {
        // Blocks. Throws on error.
        void Send(Out message);
    }

    // Requires: default(In) == null.
    // It means that In must be either a class or Nullable<T>.
    public class DurableConnection<In, Out> : IDisposable
    {
        enum State
        {
            Disconnected,
            Connected,
            Connecting,
            Disconnecting,
            Reconnecting,
        }

        // This exception is thrown only if there are bugs in the code.
        // Users shouldn't catch it.
        class InvalidConnectionStateException : Exception
        {
            public InvalidConnectionStateException(State state)
                : base(String.Format("Internal error. Invalid connection state: {0}", state))
            { }
        }

        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        // Not null.
        readonly IConnector<In, Out> _connector;

        // Protected by _stateMonitor, which is never held for a long time.
        State _state = State.Disconnected;
        readonly object _stateMonitor = new object();

        // Protected by _connectionMonitor.
        IConnection<In, Out> _connection = null;
        readonly object _connectionMonitor = new object();

        static DurableConnection()
        {
            if (default(In) != null)
            {
                throw new Exception("Invalid `In` type parameter in CodingConnector<In, Out>: " + typeof(In));
            }
        }

        // This event fires whenever a message is received.
        //
        // TODO: consider adding another argument: Func<bool>, which returns
        // the Connected property of the IConnection that gave us this message.
        // This can be useful if we ever decide to call reconnect based on the
        // content of a received message.
        public event Action<In> OnMessage;

        // This event is fired whenever a new IConnection is created. If it throws,
        // the connection is deemed broken and gets discarded.
        //
        // Some protocols may require an exchange of messages when establishing a
        // connection. When the need arises, the argument of this action will have to
        // become something more sophisticated than IWriter.
        public event Action<IWriter<Out>> OnConnection;

        public DurableConnection(IConnector<In, Out> connector)
        {
            Condition.Requires(connector, "connector").IsNotNull();
            _connector = connector;
        }

        public void Dispose()
        {
            // Note that disconnect() is asynchronous. If there is a live connection, it may be
            // destroyed after Dispose() returns.
            Disconnect();
        }

        // Doesn't block.
        public bool Connected
        {
            get
            {
                lock (_stateMonitor)
                {
                    switch (_state)
                    {
                        case State.Disconnected:
                        case State.Disconnecting:
                            return false;
                        case State.Connected:
                        case State.Connecting:
                        case State.Reconnecting:
                            return true;
                    }
                }
                throw new InvalidConnectionStateException(_state);
            }
        }

        // Blocks if another thread is currently operating on the connection that was obtained
        // via TryLock(), Lock() or LockWithTimeout(). If no one is holding an instance of IWriter<Out>,
        // then this method doesn't block.
        //
        // Usage:
        //
        //   DurableConnection<In, Out> dc = ...;
        //   using (IWriter<Out> w = dc.TryLock())
        //   {
        //       if (w != null)
        //       {
        //           // We are connected and are holding an exclusive lock on the connection.
        //       }
        //   }
        //
        // It's OK to call methods of DurableConnection while holding the lock.
        public IWriter<Out> TryLock()
        {
            return LockWithTimeout(TimeSpan.Zero);
        }

        // Similar to TryLock() but waits for the connection to be established.
        // Negative timeout means infinity. Lock(TimeSpan.Zero) is essentially TryLock().
        //
        // Usage:
        //
        //   DurableConnection<In, Out> dc = ...;
        //   using (IWriter<Out> w = dc.Lock())
        //   {
        //       // We are connected and are holding an exclusive lock on the connection.
        //   }
        public IWriter<Out> Lock()
        {
            return LockWithTimeout(TimeSpan.FromMilliseconds(-1));
        }

        // Negative timeout means infinity. Lock(TimeSpan.Zero) is essentially TryLock().
        public IWriter<Out> LockWithTimeout(TimeSpan timeout)
        {
            DateTime? deadline = null;
            if (timeout >= TimeSpan.Zero) deadline = DateTime.UtcNow + timeout;
            // We are grabbing two locks here: first _connectionMonitor, then _stateMonitor.
            // The order is important because the caller will retain the lock on
            // _connectionMonitor and may call methods that grab _stateMonitor.
            System.Threading.Monitor.Enter(_connectionMonitor);
            lock (_stateMonitor)
            {
                while (_state != State.Connected)
                {
                    if (deadline.HasValue)
                    {
                        TimeSpan t = deadline.Value - DateTime.UtcNow;
                        if (t <= TimeSpan.Zero || !System.Threading.Monitor.Wait(_stateMonitor, t))
                        {
                            System.Threading.Monitor.Exit(_connectionMonitor);
                            return null;
                        }
                    }
                    else
                    {
                        System.Threading.Monitor.Wait(_stateMonitor);
                    }
                }
                return new ExclusiveWriter<In, Out>(this, _connection, _connectionMonitor);
            }
        }

        // Doesn't block.
        public void Connect()
        {
            _log.Info("Connect requested");
            lock (_stateMonitor)
            {
                if (Connected) return;
                if (!Transitioning) ManageConnectionAfter(TimeSpan.Zero);
                _state = State.Connecting;
            }
        }

        // Doesn't block.
        public void Disconnect()
        {
            _log.Info("Disconnect requested");
            lock (_stateMonitor)
            {
                if (!Connected) return;
                if (!Transitioning) ManageConnectionAfter(TimeSpan.Zero);
                _state = State.Disconnecting;
            }
        }

        // Doesn't block.
        public void Reconnect()
        {
            _log.Info("Reconnect requested");
            lock (_stateMonitor)
            {
                if (!Transitioning) ManageConnectionAfter(TimeSpan.Zero);
                _state = State.Reconnecting;
            }
        }

        // Invariants:
        //   a) If Transitioning is true there is exactly one pending call to ManageConnection().
        //   b) If Transitioning is false there are no pending calls to ManageConnection().
        //
        // This invariant is maintained under _stateMonitor.
        bool Transitioning
        {
            get
            {
                lock (_stateMonitor)
                {
                    switch (_state)
                    {
                        case State.Disconnected:
                        case State.Connected:
                            return false;
                        case State.Disconnecting:
                        case State.Connecting:
                        case State.Reconnecting:
                            return true;
                    }
                }
                throw new InvalidConnectionStateException(_state);
            }
        }

        // Doesn't block.
        void ManageConnectionAfter(TimeSpan delay)
        {
            _log.Info("Scheduling connection management in {0}", delay);
            Task.Delay(delay).ContinueWith(t => ManageConnection());
        }

        // Blocks.
        void ManageConnection()
        {
            try
            {
                bool shouldDisconnect = false;
                bool shouldConnect = false;
                lock (_stateMonitor)
                {
                    switch (_state)
                    {
                        case State.Disconnecting:
                            shouldDisconnect = true;
                            break;
                        case State.Connecting:
                            shouldConnect = true;
                            break;
                        case State.Reconnecting:
                            shouldDisconnect = true;
                            shouldConnect = true;
                            // If Reconnect() gets called after we release _stateMonitor and before
                            // we reacquire it, we'll destroy the newly opened connection and create a new one.
                            _state = State.Connecting;
                            break;
                        default:
                            throw new InvalidConnectionStateException(_state);
                    }
                }
                // Note that we don't need to *hold* the lock while manipulating _connection. No one can grab
                // it after we release it because:
                //   a) Transitioning is true.
                //   b) ManageConnection is the only function that can flip it to false.
                //   c) There is at most one instance of ManageConnection() running at a time.
                //   d) TryLock() attempts to grab _connectionMonitor only if Transitioning is false.
                lock (_connectionMonitor) { }
                _log.Info("Changing connection state. ShouldDisconnect: {0}, ShouldConnect: {1}.",
                          shouldDisconnect, shouldConnect);
                if (shouldDisconnect && _connection != null)
                {
                    try { _connection.Dispose(); }
                    catch (Exception e) { _log.Error(e, "Unexpected exception from T.Dispose(). Swallowing it."); }
                    _connection = null;
                }
                if (shouldConnect && _connection == null)
                {
                    // Doesn't throw. Null on error.
                    _connection = NewConnection();
                }
                lock (_stateMonitor)
                {
                    if (!Transitioning) throw new InvalidConnectionStateException(_state);
                    bool connected = _connection != null;
                    if (_state == State.Connecting && connected)
                    {
                        _log.Info("Changing connection state to Connected");
                        _state = State.Connected;
                        System.Threading.Monitor.PulseAll(_stateMonitor);
                    }
                    else if (_state == State.Disconnecting && !connected)
                    {
                        _log.Info("Changing connection state to Disconnected");
                        _state = State.Disconnected;
                    }
                    else
                    {
                        ManageConnectionAfter(TimeSpan.FromSeconds(shouldConnect && !connected ? 1 : 0));
                    }
                }
            }
            catch (Exception e)
            {
                _log.Fatal(e, "Unexpected exception in DurableConnection.ManageConnection");
            }
        }

        // Doesn't throw. Null on error.
        IConnection<In, Out> NewConnection()
        {
            IConnection<In, Out> res = null;
            try
            {
                res = _connector.NewConnection();
                Condition.Requires(res).IsNotNull();
                res.OnMessage += (In message) =>
                {
                    if (message == null) Reconnect();
                    else OnMessage?.Invoke(message);
                };
                res.Connect();
                OnConnection?.Invoke(new SimpleWriter<In, Out>(res));
                return res;
            }
            catch (Exception e)
            {
                _log.Warn(e, "Unable to connect. Will retry.");
                if (res != null) res.Dispose();
                return null;
            }
        }
    }

    class ExclusiveWriter<In, Out> : IWriter<Out>
    {
        readonly DurableConnection<In, Out> _durable;
        readonly IConnection<In, Out> _connection;
        readonly object _monitor;

        public ExclusiveWriter(DurableConnection<In, Out> durable, IConnection<In, Out> connection, object monitor)
        {
            Condition.Requires(durable, "durable")
                .IsNotNull();
            Condition.Requires(connection, "connection")
                .IsNotNull();
            Condition.Requires(monitor, "monitor")
                .IsNotNull()
                .Evaluate(System.Threading.Monitor.IsEntered(monitor));
            _durable = durable;
            _connection = connection;
            _monitor = monitor;
        }

        public void Send(Out message)
        {
            try
            {
                _connection.Send(message);
            }
            catch
            {
                _durable.Reconnect();
                throw;
            }
        }

        public void Dispose()
        {
            System.Threading.Monitor.Exit(_monitor);
        }
    }

    class SimpleWriter<In, Out> : IWriter<Out>
    {
        readonly IConnection<In, Out> _connection;

        public SimpleWriter(IConnection<In, Out> connection)
        {
            Condition.Requires(connection, "connection").IsNotNull();
            _connection = connection;
        }

        public void Send(Out message)
        {
            _connection.Send(message);
        }

        public void Dispose()
        {
        }
    }
}
