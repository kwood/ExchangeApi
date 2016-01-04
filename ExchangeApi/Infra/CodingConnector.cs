﻿using Conditions;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi
{
    // Requires: default(In) == null.
    // It means that In must be either a class or Nullable<T>.
    public class CodingConnector<In, Out> : IConnector<In, Out>
    {
        readonly IConnector<ArraySegment<byte>?, ArraySegment<byte>> _connector;
        readonly ICodec<In, Out> _codec;

        static CodingConnector()
        {
            if (default(In) != null)
            {
                throw new Exception("Invalid `In` type parameter in CodingConnector<In, Out>: " + typeof(In));
            }
        }

        class Connection : IConnection<In, Out>
        {
            static readonly Logger _log = LogManager.GetCurrentClassLogger();

            readonly IConnection<ArraySegment<byte>?, ArraySegment<byte>> _connection;
            readonly ICodec<In, Out> _codec;

            public Connection(IConnection<ArraySegment<byte>?, ArraySegment<byte>> connection, ICodec<In, Out> codec)
            {
                Condition.Requires(connection, "connection").IsNotNull();
                Condition.Requires(codec, "codec").IsNotNull();
                _connection = connection;
                _codec = codec;
                _connection.OnMessage += (ArraySegment<byte>? bytes) =>
                {
                    if (bytes == null)
                    {
                        OnMessage?.Invoke(default(In));
                        return;
                    }
                    IEnumerable<In> messages;
                    try
                    {
                        messages = _codec.Parse(bytes.Value);
                    }
                    catch (Exception e)
                    {
                        _log.Warn(e, "Unable to parse an incoming message. Ignoring it.");
                        return;
                    }
                    if (messages == null)
                    {
                        _log.Info("Ignoring incoming message");
                        return;
                    }
                    foreach (In msg in messages)
                    {
                        OnMessage?.Invoke(msg);
                    }
                };
            }

            public bool Connected { get { return _connection.Connected; } }
            public void Connect() { _connection.Connect(); }
            public void Dispose() { _connection.Dispose(); }

            public event Action<In> OnMessage;

            public void Send(Out message)
            {
                _connection.Send(_codec.Serialize(message));
            }
        }

        public CodingConnector(IConnector<ArraySegment<byte>?, ArraySegment<byte>> connector, ICodec<In, Out> codec)
        {
            Condition.Requires(connector, "connector").IsNotNull();
            Condition.Requires(codec, "codec").IsNotNull();
            _connector = connector;
            _codec = codec;
        }

        public IConnection<In, Out> NewConnection()
        {
            return new Connection(_connector.NewConnection(), _codec);
        }
    }
}
