using ExchangeApi;
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
            var connector = new ExchangeApi.WebSocket.Connector(ExchangeApi.Coinbase.Instance.Prod.WebSocket);
            using (var connection = new DurableConnection<ArraySegment<byte>, ArraySegment<byte>>(connector, new Scheduler()))
            {
                connection.OnConnection += (IReader<ArraySegment<byte>> reader, IWriter<ArraySegment<byte>> writer) =>
                {
                    writer.Send(Encode("{ \"type\": \"subscribe\", \"product_id\": \"BTC-USD\" }"));
                };
                connection.OnMessage += (TimestampedMsg<ArraySegment<byte>> msg, bool isLast) =>
                {
                    _log.Info("OnMessage(IsLast={0}): {1} byte(s)", isLast, msg.Value.Count);
                };
                connection.Connect();
                Thread.Sleep(5000);
            }
            Thread.Sleep(2000);
        }

        static void StructuredConnection()
        {
            using (var client = new ExchangeApi.OkCoin.Client(ExchangeApi.OkCoin.Instance.OkCoinCom))
            {
                client.OnConnection += (IReader<ExchangeApi.OkCoin.IMessageIn> reader,
                                        IWriter<ExchangeApi.OkCoin.IMessageOut> writer) =>
                {
                    // Subscribe to depths and trades on BTC/USD spot.
                    ExchangeApi.OkCoin.Product product = ExchangeApi.OkCoin.Instrument.Parse("btc_usd_spot");
                    writer.Send(new ExchangeApi.OkCoin.SubscribeRequest() {
                        Product = product, ChannelType = ExchangeApi.OkCoin.ChanelType.Depth60 });
                    writer.Send(new ExchangeApi.OkCoin.SubscribeRequest() {
                        Product = product, ChannelType = ExchangeApi.OkCoin.ChanelType.Trades });

                    // Subscribe to depths and trades on BTC/USD future with settlement this week.
                    product = ExchangeApi.OkCoin.Instrument.Parse("btc_usd_this_week");
                    writer.Send(new ExchangeApi.OkCoin.SubscribeRequest() {
                        Product = product, ChannelType = ExchangeApi.OkCoin.ChanelType.Depth60 });
                    writer.Send(new ExchangeApi.OkCoin.SubscribeRequest() {
                        Product = product, ChannelType = ExchangeApi.OkCoin.ChanelType.Trades });
                };
                client.OnMessage += (TimestampedMsg<ExchangeApi.OkCoin.IMessageIn> msg, bool isLast) =>
                {
                    _log.Info("OnMessage(IsLast={0}): ({1}) {2}", isLast, msg.GetType(), msg);
                };
                client.Connect();
                Thread.Sleep(3000);
            }
            Thread.Sleep(2000);
        }

        static void CoinbaseRest()
        {
            using (var client = new ExchangeApi.Coinbase.RestClient(ExchangeApi.Coinbase.Instance.Prod.REST))
            {
                ExchangeApi.Coinbase.OrderBook book = client.GetProductOrderBook("BTC-USD");
                _log.Info("Order book sequence: {0}", book.Sequence);
                _log.Info("Order book has {0} order(s)", book.Orders.Count);
            }
        }

        static void Main(string[] args)
        {
            try
            {
                // RawConnection();
                StructuredConnection();
                // CoinbaseRest();
            }
            catch (Exception e)
            {
                _log.Fatal(e, "Unhandled exception");
            }
        }
    }
}
