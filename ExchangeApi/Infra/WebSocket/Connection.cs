﻿using Conditions;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ExchangeApi.WebSocket
{
    public class Connection : IConnection<ArraySegment<byte>, ArraySegment<byte>>
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

        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        readonly string _endpoint;

        ClientWebSocket _socket = null;
        State _state = State.Created;
        readonly object _monitor = new object();

        static Random _rng = new Random();

        public Connection(string endpoint)
        {
            Condition.Requires(endpoint, "endpoint").IsNotNullOrEmpty();
            _endpoint = endpoint;
        }

        // From IMessageStream.
        public bool Connected
        {
            get
            {
                lock (_monitor) return _state == State.Connected;
            }
        }

        // From IMessageStream.
        public event Action<TimestampedMsg<ArraySegment<byte>>> OnMessage;

        // From IMessageStream.
        public void Connect()
        {
            using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                Task t;
                lock (_monitor)
                {
                    if (_state != State.Created) throw new Exception("ActiveSocket.Connect() is disallowed");
                    _log.Info("Connecting to {0}", _endpoint);
                    _socket = new ClientWebSocket();
                    _state = State.Connected;
                    _socket.Options.SetBuffer(receiveBufferSize: 64 << 10, sendBufferSize: 1 << 10);
                    t = _socket.ConnectAsync(new Uri(_endpoint), cancel.Token);
                }
                t.Wait();
            }
            _log.Info("Connected to {0}", _endpoint);
            Task.Run(() =>
                {
                    try { ReadLoop(); }
                    catch (Exception e) { _log.Fatal(e, "Unexpected exception in ReadLoop()"); }
                });
        }

        // From IMessageStream.
        public void Send(ArraySegment<byte> message)
        {
            Task t;
            using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                lock (_monitor)
                {
                    if (_state != State.Connected) throw new Exception("ActiveSocket.Send() is disallowed");
                    _log.Info("OUT: {0}", DecodeForLogging(message));
                    t = _socket.SendAsync(message, WebSocketMessageType.Text, endOfMessage: true, cancellationToken: cancel.Token);
                }
                t.Wait();
            }
        }

        // From IMessageStream.
        // Blocks. Doesn't throw.
        public void Dispose()
        {
            Task t = null;
            CancellationTokenSource cancel = null;
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
                        cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        // The documentation says that CloseOutputAsync() and CloseAsync() are equivalent when
                        // used by the client. This is not true. CloseAsync() will occasionally hang despite the
                        // 10 second timeout.
                        try { t = _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", cancel.Token); }
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

            if (cancel != null) cancel.Dispose();

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
                WebSocketReceiveResult res = null;
                // If we aren't getting anything in 30 seconds, presume the connection broken.
                using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    Task<WebSocketReceiveResult> t = null;
                    lock (_monitor)
                    {
                        if (_state != State.Connected) break;
                        try { t = _socket.ReceiveAsync(buffer, cancel.Token); }
                        catch (Exception e) { _log.Warn(e, "Unable to read from ClientWebSocket"); }
                    }

                    if (t != null)
                    {
                        try
                        {
                            res = await t;
                        }
                        catch (Exception e)
                        {
                            // Don't spam logs with errors if we are in the process of disconnecting.
                            if (Connected) _log.Warn(e, "Unable to read from ClientWebSocket");
                        }
                    }
                }

                if (res == null)
                {
                    if (Connected) Notify(null);
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
                    var incoming = new TimestampedMsg<ArraySegment<byte>>()
                    {
                        Received = DateTime.UtcNow,
                        Value = new ArraySegment<byte>(message),
                    };
                    Notify(incoming);
                    message = null;
                }
            }
            _log.Info("Stopped reading data from ClientWebSocket");
        }

        void Notify(TimestampedMsg<ArraySegment<byte>> message)
        {
            if (message == null) _log.Info("IN: <ERROR>");
            else _log.Info("IN: {0}", DecodeForLogging(message.Value));

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
