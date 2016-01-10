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
                connection.OnConnection += (IReader<ArraySegment<byte>?> reader, IWriter<ArraySegment<byte>> writer) =>
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
            using (var client = new Client(Instance.OkCoinCom))
            {
                client.OnConnection += (IReader<IMessageIn> reader, IWriter<IMessageOut> writer) =>
                {
                    // Subscribe to depths and trades on BTC/USD spot.
                    Product product = Instrument.Parse("btc_usd_spot");
                    writer.Send(new SubscribeRequest() { Product = product, ChannelType = ChanelType.Depth60 });
                    writer.Send(new SubscribeRequest() { Product = product, ChannelType = ChanelType.Trades });

                    // Subscribe to depths and trades on BTC/USD future with settlement this week.
                    product = Instrument.Parse("btc_usd_this_week");
                    writer.Send(new SubscribeRequest() { Product = product, ChannelType = ChanelType.Depth60 });
                    writer.Send(new SubscribeRequest() { Product = product, ChannelType = ChanelType.Trades });
                };
                client.OnMessage += (IMessageIn msg) =>
                {
                    _log.Info("OnMessage: ({0}) {1}", msg.GetType(), msg);
                };
                client.Connect();
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
