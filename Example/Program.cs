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
            var cfg = new ExchangeApi.OkCoin.Config()
            {
                Endpoint = ExchangeApi.OkCoin.Instance.OkCoinCom,
                Keys = new ExchangeApi.OkCoin.Keys()
                {
                    ApiKey = "MY_API_KEY",
                    SecretKey = "MY_SECRET_KEY",
                },
                Products = new List<ExchangeApi.OkCoin.Product>()
                {
                    ExchangeApi.OkCoin.Instrument.Parse("btc_usd_this_week"),
                },
                EnableMarketData = false,
                EnableTrading = true,
            };
            using (var client = new ExchangeApi.OkCoin.Client(cfg))
            {
                client.OnProductDepth += (TimestampedMsg<ExchangeApi.OkCoin.ProductDepth> msg, bool isLast) =>
                {
                    _log.Info("OnProductDepth(IsLast={0}): {1}", isLast, msg.Value);
                };
                client.OnProductTrades += (TimestampedMsg<ExchangeApi.OkCoin.ProductTrades> msg, bool isLast) =>
                {
                    _log.Info("OnProductTrades(IsLast={0}): {1}", isLast, msg.Value);
                };
                client.OnOrderUpdate += (TimestampedMsg<ExchangeApi.OkCoin.MyOrderUpdate> msg, bool isLast) =>
                {
                    _log.Info("OnOrderUpdate(IsLast={0}): {1}", isLast, msg.Value);
                };
                Action<TimestampedMsg<ExchangeApi.OkCoin.NewOrderResponse>, bool> OnNewOrder = (msg, isLast) =>
                {
                    // Null msg means timeout.
                    _log.Info("OnNewOrder(IsLast={0}): {1}", isLast, msg?.Value);
                };
                Action<TimestampedMsg<ExchangeApi.OkCoin.CancelOrderResponse>, bool> OnCancelOrder = (msg, isLast) =>
                {
                    // Null msg means timeout.
                    _log.Info("OnCancelOrder(IsLast={0}): {1}", isLast, msg?.Value);
                };
                client.Connect();
                while (true)
                {
                    Thread.Sleep(1000);
                    var req = new ExchangeApi.OkCoin.NewFutureRequest()
                    {
                        Amount = new ExchangeApi.OkCoin.Amount()
                        {
                            Side = ExchangeApi.OkCoin.Side.Buy,
                            Price = 370.15m,
                            Quantity = 2m,
                        },
                        Product = (ExchangeApi.OkCoin.Future)ExchangeApi.OkCoin.Instrument.Parse("btc_usd_this_week"),
                        Leverage = ExchangeApi.OkCoin.Leverage.x10,
                        OrderType = ExchangeApi.OkCoin.OrderType.Limit,
                        PositionType = ExchangeApi.OkCoin.PositionType.Long,
                    };
                    if (client.Send(req, OnNewOrder)) break;
                }
                Thread.Sleep(5000);
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
