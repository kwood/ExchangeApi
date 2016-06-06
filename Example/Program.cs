using Conditions;
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
                connection.OnMessage += (TimestampedMsg<ArraySegment<byte>> msg) =>
                {
                    _log.Info("OnMessage(IsLast={0}): {1} byte(s)", !connection.Scheduler.HasReady(), msg.Value.Count);
                };
                connection.Connect();
                Thread.Sleep(5000);
            }
            Thread.Sleep(2000);
        }

        static void OkCoinClient()
        {
            var cfg = new ExchangeApi.OkCoin.Config()
            {
                Endpoint = ExchangeApi.OkCoin.Instance.OkCoinCom,
                Keys = new ExchangeApi.OkCoin.Keys()
                {
                    ApiKey = "MY_KEY",
                    SecretKey = "MY_SECRET",
                },
                Products = new List<ExchangeApi.OkCoin.Product>()
                {
                    // ExchangeApi.OkCoin.Instrument.Parse("btc_usd_spot"),
                    ExchangeApi.OkCoin.Instrument.Parse("btc_usd_this_week"),
                },
                EnableMarketData = false,
                EnableTrading = true,
            };
            using (var client = new ExchangeApi.OkCoin.Client(cfg))
            {
                client.OnProductDepth += (TimestampedMsg<ExchangeApi.OkCoin.ProductDepth> msg) =>
                {
                    _log.Info("OnProductDepth(IsLast={0}): {1}", !client.Scheduler.HasReady(), msg.Value);
                };
                client.OnProductTrades += (TimestampedMsg<ExchangeApi.OkCoin.ProductTrades> msg) =>
                {
                    _log.Info("OnProductTrades(IsLast={0}): {1}", !client.Scheduler.HasReady(), msg.Value);
                };
                client.OnOrderUpdate += (TimestampedMsg<ExchangeApi.OkCoin.MyOrderUpdate> msg) =>
                {
                    _log.Info("OnOrderUpdate(IsLast={0}): {1}", !client.Scheduler.HasReady(), msg.Value);
                };
                client.OnFuturePositionsUpdate += (TimestampedMsg<ExchangeApi.OkCoin.FuturePositionsUpdate> msg) =>
                {
                    _log.Info("OnFuturePositionsUpdate(IsLast={0}): {1}", !client.Scheduler.HasReady(), msg.Value);
                };
                client.OnSpotPositionsUpdate += (TimestampedMsg<ExchangeApi.OkCoin.SpotPositionsUpdate> msg) =>
                {
                    _log.Info("OnSpotPositionsUpdate(IsLast={0}): {1}", !client.Scheduler.HasReady(), msg.Value);
                };
                Action<TimestampedMsg<ExchangeApi.OkCoin.NewOrderResponse>> OnNewOrder = msg =>
                {
                    // Null msg means timeout.
                    _log.Info("OnNewOrder(IsLast={0}): {1}", !client.Scheduler.HasReady(), msg?.Value);
                };
                Action<TimestampedMsg<ExchangeApi.OkCoin.CancelOrderResponse>> OnCancelOrder = msg =>
                {
                    // Null msg means timeout.
                    _log.Info("OnCancelOrder(IsLast={0}): {1}", !client.Scheduler.HasReady(), msg?.Value);
                };
                client.Connect();
                Thread.Sleep(7000);
                var req = new ExchangeApi.OkCoin.NewFutureRequest()
                {
                    Amount = new ExchangeApi.OkCoin.Amount()
                    {
                        Side = ExchangeApi.OkCoin.Side.Buy,
                        Price = 588.44m,
                        Quantity = 1m,
                    },
                    Product = ExchangeApi.OkCoin.Future.FromInstrument("btc_usd_this_week"),
                    Leverage = ExchangeApi.OkCoin.Leverage.x10,
                    OrderType = ExchangeApi.OkCoin.OrderType.Limit,
                    PositionType = ExchangeApi.OkCoin.PositionType.Long,
                };
                client.Send(req, OnNewOrder);
                while (true) Thread.Sleep(5000);
            }
            Thread.Sleep(2000);
        }

        static void OkCoinRest()
        {
            var keys = new ExchangeApi.OkCoin.Keys()
            {
                ApiKey = "MY_API_KEY",
                SecretKey = "MY_SECRET_KEY",
            };
            string endpoint = ExchangeApi.OkCoin.Instance.OkCoinCom.REST;
            using (var client = new ExchangeApi.OkCoin.REST.RestClient(endpoint, keys))
            {
                var future = ExchangeApi.OkCoin.Future.FromInstrument("btc_usd_this_week");
                _log.Info("Open future orders: {0}", string.Join(", ", client.OpenOrders(future).Select(id => id.ToString())));
                var spot = ExchangeApi.OkCoin.Spot.FromInstrument("btc_usd_spot");
                _log.Info("Open spot orders: {0}", string.Join(", ", client.OpenOrders(spot).Select(id => id.ToString())));
            }
        }

        static void CoinbaseClient()
        {
            var keys = new ExchangeApi.Coinbase.Keys()
            {
                Key = "MY_KEY",
                Secret = "MY_SECRET",
                Passphrase = "MY_PASSPHRASE",
            };
            var cfg = new ExchangeApi.Coinbase.Config()
            {
                Endpoint = ExchangeApi.Coinbase.Instance.Prod,
                Products = new List<string>() { "BTC-USD" },
                Keys = keys,
            };
            using (var client = new ExchangeApi.Coinbase.Client(cfg))
            {
                client.OnOrderBook += (string product, TimestampedMsg<ExchangeApi.Coinbase.OrderBookDelta> msg) =>
                {
                    if (msg.Value.Bids.Count + msg.Value.Asks.Count > 10)
                    {
                        _log.Info("OnOrderBook({0}, IsLast={1}): {2} bid(s), {3} ask(s)",
                                  product, !client.Scheduler.HasReady(), msg.Value.Bids.Count, msg.Value.Asks.Count);
                    }
                    else
                    {
                        _log.Info("OnOrderBook({0}, IsLast={1}): {2}", product, !client.Scheduler.HasReady(), msg.Value);
                    }
                };
                client.OnTrade += (string product, TimestampedMsg<ExchangeApi.Coinbase.Trade> msg) =>
                {
                    _log.Info("OnTrade({0}, IsLast={1}): {2}", product, !client.Scheduler.HasReady(), msg.Value);
                };
                Action<TimestampedMsg<ExchangeApi.Coinbase.OrderUpdate>> OnOrder = msg =>
                {
                    _log.Error("OnOrder(IsLast={0}): {1}", !client.Scheduler.HasReady(), msg);
                };
                client.Connect();
                Thread.Sleep(5000);
                client.Send(new ExchangeApi.Coinbase.NewOrder()
                    {
                        Price = 584.33m,
                        Size = 0.01m,
                        ProductId = "BTC-USD",
                        Side = ExchangeApi.Coinbase.Side.Sell,
                    },
                    OnOrder);
                while (true) Thread.Sleep(1000);

            }
            Thread.Sleep(2000);
        }

        static void CoinbaseRest()
        {
            var keys = new ExchangeApi.Coinbase.Keys()
            {
                Key = "MY_KEY",
                Secret = "MY_SECRET",
                Passphrase = "MY_PASSPHRASE",
            };
            using (var client = new ExchangeApi.Coinbase.REST.RestClient(ExchangeApi.Coinbase.Instance.Prod.REST, keys))
            {
                client.SendRequest(new ExchangeApi.Coinbase.REST.CancelAllRequest()).Wait();
                string orderId = client.SendRequest(new ExchangeApi.Coinbase.REST.NewOrderRequest()
                {
                    ClientOrderId = Guid.NewGuid().ToString(),
                    Side = ExchangeApi.Coinbase.Side.Sell,
                    ProductId = "BTC-EUR",
                    Price = 1000m,
                    Size = 0.01m,
                    TimeInForce = ExchangeApi.Coinbase.REST.TimeInForce.GTT,
                    CancelAfter = ExchangeApi.Coinbase.REST.CancelAfter.Min,
                    PostOnly = true,
                }).Result.OrderId;
                client.SendRequest(new ExchangeApi.Coinbase.REST.CancelOrderRequest()
                {
                    OrderId = orderId,
                }).Wait();
                client.SendRequest(new ExchangeApi.Coinbase.REST.CancelOrderRequest()
                {
                    OrderId = orderId,
                }).Wait();
            }
        }

        static void Main(string[] args)
        {
            try
            {
                // RawConnection();
                // OkCoinClient();
                // OkCoinRest();
                CoinbaseClient();
                // CoinbaseRest();
            }
            catch (Exception e)
            {
                _log.Fatal(e, "Unhandled exception");
            }
        }
    }
}
