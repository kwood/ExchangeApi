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
    class Program
    {
        private static readonly NLog.Logger _log = LogManager.GetCurrentClassLogger();

        static void Main(string[] args)
        {
            try
            {
                var socket = new ClientWebSocket();
                socket.ConnectAsync(new Uri("wss://real.okcoin.com:10440/websocket/okcoinapi"), CancellationToken.None).Wait();
                socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait();
            }
            catch (Exception e)
            {
                _log.Fatal(e, "Unhandled exception");
            }
        }
    }
}
