using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OkCoinApi.Example
{
    class State
    {
        public Connector Connector;
        public DurableConnection<WebSocket> Connection;
    }

    class Program
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        static ArraySegment<byte> Encode(string s)
        {
            return new ArraySegment<byte>(Encoding.UTF8.GetBytes(s));
        }

        static void Initialize(State state, WebSocket socket)
        {
            socket.OnMessage += (ArraySegment<byte> bytes) =>
            {
                if (bytes.Array == null) state.Connection.Reconnect();
            };
            socket.Send(Encode("{'event':'addChannel','channel':'ok_btcusd_future_depth_this_week_60'}"));
            socket.Send(Encode("{'event':'addChannel','channel':'ok_btcusd_future_trade_v1_this_week'}"));
        }

        static void Main(string[] args)
        {
            try
            {
                var state = new State();
                state.Connector = new Connector("wss://real.okcoin.com:10440/websocket/okcoinapi", sock => Initialize(state, sock));
                state.Connection = new DurableConnection<WebSocket>(state.Connector);
                state.Connection.Connect();
                while (true) Thread.Sleep(1000);
            }
            catch (Exception e)
            {
                _log.Fatal(e, "Unhandled exception");
            }
        }
    }
}
