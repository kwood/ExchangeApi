using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OkCoinApi
{
    interface IConnector<T> where T : IDisposable
    {
        // May block for a few seconds but not indefinitely.
        // Must throw or return null if unable to establish a connection.
        // Doesn't need to be thread safe.
        T NewConnection();
    }

    class Locked<T> : IDisposable where T : class
    {
        readonly T _value;
        readonly object _monitor;

        // Requires: value and monitor aren't null; monitor is locked.
        public Locked(T value, object monitor)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (monitor == null) throw new ArgumentNullException("monitor");
            if (!System.Threading.Monitor.IsEntered(monitor)) throw new ArgumentException("monitor");
            _value = value;
            _monitor = monitor;
        }

        // Guarantees: Value != null.
        public T Value { get { return _value; } }

        public void Dispose()
        {
            System.Threading.Monitor.Exit(_monitor);
        }
    }

    class DurableConnection<T> : IDisposable where T : class, IDisposable
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

        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        // Not null.
        readonly IConnector<T> _connector;

        // Protected by _stateMonitor, which is never held for a long time.
        State _state = State.Disconnected;
        readonly object _stateMonitor = new object();

        // Protected by _connectionMonitor.
        T _connection = null;
        readonly object _connectionMonitor = new object();

        public DurableConnection(IConnector<T> connector)
        {
            if (connector == null) throw new ArgumentNullException();
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

        // Doesn't block.
        //
        // Usage:
        //
        //   DurableConnection<MyConnection> dc = ...;
        //   using (Locked<MyConnection> c = dc.TryLock())
        //   {
        //       if (c != null)
        //       {
        //           // We are connected and are holding an exclusive lock on the connection.
        //       }
        //   }
        //
        // It's OK to call methods of DurableConnection while holding the lock.
        public Locked<T> TryLock()
        {
            lock (_stateMonitor)
            {
                if (_state != State.Connected) return null;
                // We are grabbing two locks here: first _stateMonitor, then _connectionMonitor.
                // The former is released immediately; the latter will be released by the caller.
                // Note that grabbing either of the locks is a non-blocking operation.
                System.Threading.Monitor.Enter(_connectionMonitor);
                return new Locked<T>(_connection, _connectionMonitor);
            }
        }

        // Doesn't block.
        public void Connect()
        {
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
                if (shouldDisconnect && _connection != null)
                {
                    try { _connection.Dispose(); }
                    catch (Exception e) { _log.Error(e, "Unexpected exception from T.Dispose(). Swallowing it."); }
                    _connection = null;
                }
                if (shouldConnect && _connection == null)
                {
                    // Note: if NewConnection() returns null, we treat it as connection error (just like exception).
                    try { _connection = _connector.NewConnection(); }
                    catch (Exception e) { _log.Warn(e, "Unable to connect. Will retry."); }
                }
                lock (_stateMonitor)
                {
                    if (!Transitioning) throw new InvalidConnectionStateException(_state);
                    bool connected = _connection != null;
                    if (_state == State.Connecting && connected)
                        _state = State.Connected;
                    else if (_state == State.Disconnecting && !connected)
                        _state = State.Disconnected;
                    else
                        ManageConnectionAfter(TimeSpan.FromSeconds(shouldConnect && !connected ? 1 : 0));
                }
            }
            catch (Exception e)
            {
                _log.Fatal(e, "Unexpected exception in DurableConnection.ManageConnection");
            }
        }
    }
}
