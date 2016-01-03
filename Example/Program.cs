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
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        static ArraySegment<byte> Encode(string s)
        {
            return new ArraySegment<byte>(Encoding.UTF8.GetBytes(s));
        }

        static void Main(string[] args)
        {
            try
            {
                var connection = new DurableConnection<ArraySegment<byte>?, ArraySegment<byte>>(
                    new Connector("wss://real.okcoin.com:10440/websocket/okcoinapi"));
                connection.OnConnection += (IWriter<ArraySegment<byte>> writer) =>
                {
                    writer.Send(Encode("{'event':'addChannel','channel':'ok_btcusd_future_depth_this_week_60'}"));
                    writer.Send(Encode("{'event':'addChannel','channel':'ok_btcusd_future_trade_v1_this_week'}"));
                };
                connection.OnMessage += (ArraySegment<byte>? bytes) =>
                {
                    _log.Info("OnMessage: {0} byte(s)", bytes.Value.Count);
                };
                connection.Connect();
                while (true) Thread.Sleep(1000);
            }
            catch (Exception e)
            {
                _log.Fatal(e, "Unhandled exception");
            }
        }
    }
}
