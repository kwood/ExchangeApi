using ExchangeApi;
using ExchangeApi.OkCoin;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Example
{
    class Program
    {
        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        static ArraySegment<byte> Encode(string s)
        {
            return new ArraySegment<byte>(Encoding.ASCII.GetBytes(s));
        }

        static void RawConnection()
        {
            var connector = new ExchangeApi.WebSocket.Connector(Instance.OkCoinCom);
            using (var connection = new DurableConnection<ArraySegment<byte>?, ArraySegment<byte>>(connector))
            {
                connection.OnConnection += (IWriter<ArraySegment<byte>> writer) =>
                {
                    writer.Send(Encode("{'event':'addChannel','channel':'ok_btcusd_trades_v1'}"));
                };
                connection.OnMessage += (ArraySegment<byte>? msg) =>
                {
                    _log.Info("OnMessage: {0} byte(s)", msg.Value.Count);
                };
                connection.Connect();
                Thread.Sleep(5000);
            }
            Thread.Sleep(2000);
        }

        static void StructuredConnection()
        {
            var connector = new CodingConnector<IMessageIn, IMessageOut>(
                    new ExchangeApi.WebSocket.Connector(Instance.OkCoinCom),
                    new Codec());
            using (var connection = new DurableConnection<IMessageIn, IMessageOut>(connector))
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
                connection.OnMessage += (IMessageIn msg) =>
                {
                    _log.Info("OnMessage: ({0}) {1}", msg.GetType(), msg);
                };
                connection.Connect();
                Thread.Sleep(60000);
            }
            Thread.Sleep(2000);
        }

        static void Main(string[] args)
        {
            try
            {
                // RawConnection();
                StructuredConnection();
            }
            catch (Exception e)
            {
                _log.Fatal(e, "Unhandled exception");
            }
        }
    }
}
