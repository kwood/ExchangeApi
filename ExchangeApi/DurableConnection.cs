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

    // This interface is used by the DurableConnection.OnConnection event handler.
    // It allows for arbitrary exchange of messages between the server and the client when
    // establishing a connection.
    public interface IReader<In>
    {
        // Returns message at the head of the queue.
        // May block indefinitely waiting for data.
        // Doesn't remove the message from the head of the queue.
        // Throws on IO error.
        TimestampedMsg<In> Peek();
        // Variant of Peek() with timeout. Negative timeout means infinity.
        // Zero timeout will succeed if there is already a message in the queue (it won't read from the network).
        // Throws on IO error.
        bool PeekWithTimeout(TimeSpan timeout, out TimestampedMsg<In> msg);
        // Discards the message at the top of the queue. This message will NOT be delivered to
        // the DurableConnection.OnMessage event handler after the connection is established.
        void Consume();
        // Skips the message at the top of the queue. This message WILL be delivered to
        // the DurableConnection.OnMessage event handler after the connection is established.
        void Skip();
    }

    public class ConnectionReadError : Exception
    {
        public ConnectionReadError()
            : base("Read error while trying to establish a connection") { }
    }

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

        // Not null. All events fire on this scheduler.
        readonly Scheduler _scheduler;

        // All events are serialized.
        //
        // The lifetime of a successful connection:
        //
        //   1. OnConnection doesn't throw.
        //   2. OnConnected.
        //   3. Zero or more OnMessage.
        //   4. OnDisconnected.
        //
        // The lifetime of an unsuccessful connection:
        //
        //   1. OnConnection throws.
        //
        // The lifetime of a DurableConnection consists of a sequence of zero or
        // more scenarious described above.
        //
        // All events may fire even after the event handler is unsubscribed and after
        // the Disconnect() or Dispose() call.

        // This event is fired whenever a new IConnection is created. If it throws,
        // the connection is deemed broken and gets discarded. It's OK to block there.
        // OnMessage won't fire while OnConnection is running: messages get buffered and
        // delivered only when and if OnConnection successfully returns.
        //
        // If it doesn't throw, OnConnected is the next message that fires.
        public event Action<IReader<In>, IWriter<Out>> OnConnection;

        // This event fires whenever a message is received.
        // If OnConnection event handler throws, events from that connection aren't delivered.
        // Events for which OnConnection called IReader.Consume() are also not delivered (they are deemed
        // consumed by OnConnection).
        public event Action<TimestampedMsg<In>, bool> OnMessage;

        // Fires when a connection is lost. The only event that may fire immediately
        // after it is OnConnection. Does NOT fire as a result of OnConnection throwing.
        public event Action OnDisconnected;

        // Fires when a connection is established, which happens immediately after OnConnection.
        public event Action OnConnected;

        // Does not take ownership of `scheduler`: DurableConnection.Dispose() won't call Scheduler.Dispose().
        public DurableConnection(IConnector<In, Out> connector, Scheduler scheduler)
        {
            Condition.Requires(connector, "connector").IsNotNull();
            Condition.Requires(scheduler, "scheduler").IsNotNull();
            _connector = connector;
            _scheduler = scheduler;
        }

        // Does not dispose of the scheduler.
        public void Dispose()
        {
            // Note that disconnect() is asynchronous. If there is a live connection, it may be
            // destroyed after Dispose() returns.
            Disconnect();
        }

        public Scheduler Scheduler { get { return _scheduler; } }

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
            while (true)
            {
                // We are grabbing two locks here: first _connectionMonitor, then _stateMonitor.
                // The order is important because the caller will retain the lock on
                // _connectionMonitor and may call methods that grab _stateMonitor.
                System.Threading.Monitor.Enter(_connectionMonitor);
                lock (_stateMonitor)
                {
                    if (_state == State.Connected)
                    {
                        return new ExclusiveWriter<In, Out>(this, _connection, _connectionMonitor);
                    }
                    // Unlock _connectionMonitor before going into waiting. We'll reacquire it afterwards.
                    System.Threading.Monitor.Exit(_connectionMonitor);
                    if (deadline.HasValue)
                    {
                        TimeSpan t = deadline.Value - DateTime.UtcNow;
                        if (t <= TimeSpan.Zero || !System.Threading.Monitor.Wait(_stateMonitor, t))
                        {
                            return null;
                        }
                    }
                    else
                    {
                        System.Threading.Monitor.Wait(_stateMonitor);
                    }
                }
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
                if (_state == State.Disconnected)
                {
                    _state = State.Connecting;
                }
                else if (_state == State.Disconnecting)
                {
                    // Calling Disconnect() + Connect() should be equivalent to calling Reconnect().
                    // If we set _state to Connecting here, Disconnect() + Connect() could result in a
                    // no-op if ManageConnection() gets delayed.
                    _state = State.Reconnecting;
                }
                else
                {
                    throw new InvalidConnectionStateException(_state);
                }
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
            _scheduler.Schedule(DateTime.UtcNow + delay, isLast => ManageConnection());
        }

        // Blocks.
        // Runs in the scheduler thread.
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
                    catch (Exception e) { _log.Warn(e, "Ignoring exception from IConnection.Dispose()"); }
                    _connection = null;
                    try { OnDisconnected?.Invoke(); }
                    catch (Exception e) { _log.Warn(e, "Ignoring exception from OnDisconnected"); }
                }
                Reader<In> reader = null;
                if (shouldConnect && _connection == null)
                {
                    // Doesn't throw. Sets connection and reader to null on error.
                    NewConnection(out _connection, out reader);
                }
                switch (UpdateState())
                {
                    case State.Connecting:
                    case State.Disconnecting:
                    case State.Reconnecting:
                        // Attempts to connect are repeated with 1 second delay.
                        // All other transitions can happen instantly.
                        ManageConnectionAfter(TimeSpan.FromSeconds(shouldConnect && _connection == null ? 1 : 0));
                        break;
                    case State.Connected:
                        try { OnConnected?.Invoke(); }
                        catch (Exception e) { _log.Warn(e, "Ignoring exception from OnConnected"); }
                        if (reader != null)
                        {
                            // Send buffered and all future messages to OnMessage.
                            var connection = _connection;
                            reader.SinkTo((TimestampedMsg<In> msg) =>
                            {
                                _scheduler.Schedule(isLast => HandleMessage(connection, msg, isLast));
                            });
                        }
                        break;
                }
            }
            catch (Exception e)
            {
                _log.Fatal(e, "Unexpected exception in DurableConnection.ManageConnection");
            }
        }

        // Runs in the scheduler thread.
        State UpdateState()
        {
            lock (_stateMonitor)
            {
                if (!Transitioning) throw new InvalidConnectionStateException(_state);
                bool connected = _connection != null;
                if (_state == State.Connecting && connected)
                {
                    _log.Info("Changing connection state to Connected");
                    _state = State.Connected;
                    // LockWithTimeout() might be waiting for _state to become Connected.
                    System.Threading.Monitor.PulseAll(_stateMonitor);
                }
                else if (_state == State.Disconnecting && !connected)
                {
                    _log.Info("Changing connection state to Disconnected");
                    _state = State.Disconnected;
                }
                return _state;
            }
        }

        // Runs in the scheduler thread.
        // Doesn't throw. In case of error both `connection` and `reader` are set to null.
        // Otherwise both are non-null.
        void NewConnection(out IConnection<In, Out> connection, out Reader<In> reader)
        {
            connection = null;
            var r = new Reader<In>();
            try
            {
                connection = _connector.NewConnection();
                Condition.Requires(connection).IsNotNull();
                connection.OnMessage += (TimestampedMsg<In> msg) => r.Push(msg);
                connection.Connect();
                OnConnection?.Invoke(r, new SimpleWriter<In, Out>(connection));
                // If OnConnection() handler swallowed read error exceptions, CheckHealth() will throw.
                r.CheckHealth();
                reader = r;
            }
            catch (Exception e)
            {
                _log.Warn(e, "Unable to connect. Will retry.");
                if (connection != null)
                {
                    try { connection.Dispose(); }
                    catch (Exception ex) { _log.Error(ex, "Ignoring exception from IConnection.Dispose()"); }
                    connection = null;
                }
                reader = null;
            }
        }

        // Runs in the scheduler thread.
        void HandleMessage(IConnection<In, Out> connection, TimestampedMsg<In> msg, bool isLast)
        {
            // We can read _connection without a lock because we are in the scheduler thread.
            // _connection can't be modified while we are running.
            if (!Object.ReferenceEquals(connection, _connection))
            {
                _log.Info("Message belongs to a stale connection. Dropping it.");
                return;
            }
            if (msg == null)
            {
                // Null message means unrecoverable network read error.
                Reconnect();
            }
            else
            {
                OnMessage?.Invoke(msg, isLast);
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

    class Reader<In> : IReader<In>
    {
        readonly MessageQueue<TimestampedMsg<In>> _queue = new MessageQueue<TimestampedMsg<In>>();

        bool _broken = false;

        public void CheckHealth()
        {
            if (_broken) throw new ConnectionReadError();
        }

        public void Push(TimestampedMsg<In> msg)
        {
            _queue.Push(msg);
        }

        public void SinkTo(Action<TimestampedMsg<In>> sink)
        {
            _queue.SinkTo(sink);
        }

        public void Consume()
        {
            if (_broken) throw new ConnectionReadError();
            _queue.Consume();
        }

        public TimestampedMsg<In> Peek()
        {
            if (_broken) throw new ConnectionReadError();
            TimestampedMsg<In> msg = _queue.Peek();
            if (msg == null)
            {
                _broken = true;
                throw new ConnectionReadError();
            }
            return msg;
        }

        public bool PeekWithTimeout(TimeSpan timeout, out TimestampedMsg<In> msg)
        {
            if (_broken) throw new ConnectionReadError();
            if (!_queue.PeekWithTimeout(timeout, out msg)) return false;
            if (msg == null)
            {
                _broken = true;
                throw new ConnectionReadError();
            }
            return true;
        }

        public void Skip()
        {
            if (_broken) throw new ConnectionReadError();
            _queue.Skip();
        }
    }
}
