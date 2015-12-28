using Conditions;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OkCoinApi
{
    public class ActiveSocket : IDisposable
    {
        enum State
        {
            // Neither Connect() nor Dispose() have been called yet.
            Created,
            // Connect() has been called, Dispose() hasn't been called.
            Connected,
            // Dispose() is in progress.
            Disposing,
            // Dispose() has finished.
            Disposed,
        }

        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        ClientWebSocket _socket = null;
        State _state = State.Created;
        readonly object _monitor = new object();

        public ActiveSocket()
        {
        }

        public bool Connected
        {
            get
            {
                lock (_monitor) return _state == State.Connected;
            }
        }

        // While the event handler is running, nothing is getting read from the
        // socket.
        //
        // On read error this event is raised with null as the argument.
        // However, the event isn't raised if the error happened after the first
        // call to Dispose().
        public event Action<ArraySegment<byte>> OnMessage;

        // Blocks. Throws on error. Must be called at most once.
        public void Connect(string endpoint)
        {
            Condition.Requires(endpoint, "endpoint").IsNotNullOrEmpty();
            Task t;
            lock (_monitor)
            {
                if (_state != State.Created) throw new Exception("ActiveSocket.Connect() is disallowed");
                _log.Info("Connecting to {0}", endpoint);
                _socket = new ClientWebSocket();
                _state = State.Connected;
                _socket.Options.SetBuffer(receiveBufferSize: 64 << 10, sendBufferSize: 1 << 10);
                t = _socket.ConnectAsync(new Uri(endpoint), TimeoutSec(10));
            }
            t.Wait();
            _log.Info("Connected to {0}", endpoint);
            Task.Run(() =>
                {
                    try { ReadLoop(); }
                    catch (Exception e) { _log.Fatal(e, "Unexpected exception in ReadLoop()"); }
                });
        }

        // Blocks. Throws on error.
        public void Send(ArraySegment<byte> message)
        {
            Task t;
            lock (_monitor)
            {
                if (_state != State.Connected) throw new Exception("ActiveSocket.Connect() is disallowed");
                _log.Info("OUT: {0}", DecodeForLogging(message));
                t = _socket.SendAsync(message, WebSocketMessageType.Text, endOfMessage: true, cancellationToken: TimeoutSec(10));
            }
            t.Wait();
        }

        // Blocks. Doesn't throw.
        public void Dispose()
        {
            Task t = null;
            lock (_monitor)
            {
                switch (_state)
                {
                    case State.Disposed:
                        return;
                    case State.Created:
                        _state = State.Disposed;
                        return;
                    case State.Disposing:
                        do
                        {
                            Monitor.Wait(_monitor);
                        } while (_state == State.Disposing);
                        return;
                    case State.Connected:
                        _state = State.Disposing;
                        _log.Info("Disconnecting...");
                        try { t = _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", TimeoutSec(10)); }
                        catch (Exception e) { _log.Warn(e, "Unable to cleanly close ClientWebSocket"); }
                        break;
                }
            }

            if (t != null)
            {
                try { t.Wait(); }
                catch (Exception e) { _log.Warn(e, "Unable to cleanly close ClientWebSocket"); }
                _log.Info("Disconnected");
            }

            lock (_monitor)
            {
                if (_socket != null)
                {
                    try { _socket.Dispose(); }
                    catch (Exception e) { _log.Warn(e, "Unable to cleanly close ClientWebSocket"); }
                }
                _socket = null;
                _state = State.Disposed;
                Monitor.PulseAll(_monitor);
            }
        }

        async void ReadLoop()
        {
            byte[] message = null;
            var buffer = new ArraySegment<byte>(new byte[128 << 10]);
            while (true)
            {
                Task<WebSocketReceiveResult> t = null;
                lock (_monitor)
                {
                    if (_state != State.Connected) break;
                    // If we aren't getting anything in 10 seconds, presume the connection broken.
                    // TODO: remove the timeout here and implement proper pings to detect broken connections.
                    try { t = _socket.ReceiveAsync(buffer, TimeoutSec(10)); }
                    catch (Exception e) { _log.Warn(e, "Unable to read from ClientWebSocket"); }
                }

                WebSocketReceiveResult res = null;
                if (t != null)
                {
                    try { res = await t; }
                    catch (Exception e) { _log.Warn(e, "Unable to read from ClientWebSocket"); }
                }

                if (res == null)
                {
                    if (Connected) Notify(new ArraySegment<byte>());
                    break;
                }

                if (res.CloseStatus.HasValue)
                {
                    _log.Info("The remote side is closing connection: {0}, {1}", res.CloseStatus, res.CloseStatusDescription);
                }

                if (res.Count > 0)
                    Append(ref message, buffer.Array, buffer.Offset, res.Count);

                if (res.EndOfMessage && message != null)
                {
                    Notify(new ArraySegment<byte>(message));
                    message = null;
                }
            }
            _log.Info("Stopped reading data from ClientWebSocket");
        }

        void Notify(ArraySegment<byte> message)
        {
            if (message.Array == null) _log.Info("IN: <ERROR>");
            else _log.Info("IN: {0}", DecodeForLogging(message));

            try { OnMessage?.Invoke(message); }
            catch (Exception e) { _log.Warn(e, "Error while handling a message from ClientWebSocket"); }
        }

        static void Append(ref byte[] dest, byte[] src, int offset, int count)
        {
            if (dest == null)
            {
                dest = new byte[count];
                Array.Copy(src, offset, dest, 0, count);
            }
            else
            {
                var prefix = dest;
                dest = new byte[prefix.Length + count];
                Array.Copy(prefix, dest, prefix.Length);
                Array.Copy(src, offset, dest, prefix.Length, count);
            }
        }

        static CancellationToken TimeoutSec(int seconds)
        {
            return new CancellationTokenSource(TimeSpan.FromSeconds(seconds)).Token;
        }

        static string DecodeForLogging(ArraySegment<byte> bytes)
        {
            try
            {
                return Encoding.UTF8.GetString(bytes.Array, bytes.Offset, bytes.Count);
            }
            catch
            {
                return "<BINARY>";
            }
        }
    }
}
