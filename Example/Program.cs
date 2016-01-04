using ExchangeApi.OkCoin;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ExchangeApi.Example
{
    class Program
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        static void Main(string[] args)
        {
            try
            {
                var connector = new CodingConnector<ArraySegment<byte>?, IMessageOut>(
                    new WebSocket.Connector("wss://real.okcoin.com:10440/websocket/okcoinapi"),
                    new Codec());
                using (var connection = new DurableConnection<ArraySegment<byte>?, IMessageOut>(connector))
                {
                    connection.OnConnection += (IWriter<IMessageOut> writer) =>
                    {
                        foreach (var coin in new[] { CoinType.Btc, CoinType.Ltc })
                        foreach (var chan in new[] { ChanelType.Depth60, ChanelType.Trades })
                        {
                                // Subscribe to market data for spots.
                                var spot = new Spot() { Currency = Currency.Usd, CoinType = coin };
                                writer.Send(new SubscribeRequest() { Product = spot, ChannelType = chan });
                                // Subscribe to market data for futures.
                                foreach (var settlement in new[] { FutureType.ThisWeek, FutureType.NextWeek, FutureType.Quarter })
                                {
                                    var future = new Future() { Currency = Currency.Usd, CoinType = coin, FutureType = settlement };
                                    writer.Send(new SubscribeRequest() { Product = future, ChannelType = chan });
                                }
                        }
                    };
                    connection.OnMessage += (ArraySegment<byte>? bytes) =>
                    {
                        _log.Info("OnMessage: {0} byte(s)", bytes.Value.Count);
                    };
                    connection.Connect();
                    Thread.Sleep(5000);
                }
                Thread.Sleep(2000);
            }
            catch (Exception e)
            {
                _log.Fatal(e, "Unhandled exception");
            }
        }
    }
}
