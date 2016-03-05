using Conditions;
using ExchangeApi.Util;
using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.OkCoin.WebSocket
{
    public class MessageParser : IVisitorIn<IMessageIn>
    {
        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        readonly JToken _data;

        public MessageParser(JToken data)
        {
            _data = data;
        }

        public IMessageIn Visit(NewOrderResponse msg)
        {
            Condition.Requires(_data, "data").IsNotNull();
            // {"order_id":"1476459990","result":"true"}
            if ((string)_data["result"] != "true")
            {
                _log.Error("Unexpected response to new future request. Ignoring it.");
                return null;
            }
            msg.OrderId = (long)_data["order_id"];
            return msg;
        }

        public IMessageIn Visit(CancelOrderResponse msg)
        {
            Condition.Requires(_data, "data").IsNotNull();
            // {"order_id":"1476459990","result":"true"}
            if ((string)_data["result"] != "true")
            {
                _log.Error("Unexpected response to new future request. Ignoring it.");
                return null;
            }
            msg.OrderId = (long)_data["order_id"];
            return msg;
        }

        public IMessageIn Visit(ProductTrades msg)
        {
            Condition.Requires(_data, "data").IsNotNull();
            // [["78270746", "431.3", "0.01", "22:02:41", "ask"], ...]
            //
            // Each element is [TradeId, Price, Quantity, Time, Side].
            msg.Trades = new List<Trade>();
            foreach (JToken elem in _data)
            {
                msg.Trades.Add(new Trade()
                {
                    Id = (long)elem[0],
                    Timestamp = Util.Time.FromDayTime((TimeSpan)elem[3], TimeSpan.FromHours(8)),
                    Amount = new Amount()
                    {
                        Price = elem[1].AsDecimal(),
                        Quantity = elem[2].AsDecimal(),
                        Side = Serialization.ParseSide((string)elem[4]),
                    }
                });
            }
            return msg;
        }

        public IMessageIn Visit(ProductDepth msg)
        {
            Condition.Requires(_data, "data").IsNotNull();
            // {
            //   "bids": [[432.76, 3.55],...],
            //   "asks": [[440.01, 3.15],...],
            //   "timestamp":"1451920248246",
            // }
            msg.Timestamp = Util.Time.FromUnixMillis((long)_data["timestamp"]);
            msg.Orders = new List<Amount>();
            Action<string, Side> ParseOrders = (field, side) =>
            {
                JArray orders = (JArray)_data[field];
                Condition.Requires(orders, "orders").IsNotNull();
                foreach (var pair in orders)
                {
                    msg.Orders.Add(new Amount()
                    {
                        Side = side,
                        Price = pair[0].AsDecimal(),
                        Quantity = pair[1].AsDecimal(),
                    });
                }
            };
            ParseOrders("bids", Side.Buy);
            ParseOrders("asks", Side.Sell);
            return msg;
        }

        public IMessageIn Visit(MyOrderUpdate msg)
        {
            if (_data == null)
            {
                // OkCoin sends an empty message without data in response to
                // our subscription request.
                return msg;
            }
            switch (msg.ProductType)
            {
                case ProductType.Future:
                    msg.Order = ParseFutureState(_data, msg.Currency);
                    break;
                case ProductType.Spot:
                    msg.Order = ParseSpotState(_data, msg.Currency);
                    break;
                default:
                    throw new ArgumentException("Invalid ProductType: " + msg.ProductType);
            }
            return msg;
        }

        public IMessageIn Visit(FuturePositionsUpdate msg)
        {
            // "symbol": "btc_usd",
            // "positions": [
            //   {
            //     "contract_id": "20160219013",
            //     "contract_name": "BTC0219",
            //     "avgprice": "413.15435805",
            //     "balance": "0.04855328",
            //     "bondfreez": "0",
            //     "costprice": "413.15435805",
            //     "eveningup": "2",
            //     "forcedprice": "379.0405725",
            //     "position": "1",
            //     "profitreal": "-1.4522E-4",
            //     "fixmargin": "0.04840806",
            //     "hold_amount": "2",
            //     "lever_rate": "10",
            //     "position_id": "9018065"
            //   },
            // ]
            if (_data == null)
            {
                // OkCoin sends an empty message without data in response to
                // our subscription request.
                return msg;
            }

            DateTime now = DateTime.UtcNow;
            string symbol = (string)_data["symbol"];
            string[] parts = symbol.Split(new char[] { '_' }, 2);
            Condition.Requires(parts, "parts").HasLength(2);
            msg.CoinType = Serialization.ParseCoinType(parts[0]);
            msg.Currency = Serialization.ParseCurrency(parts[1]);

            msg.Positions = new List<FuturePosition>();
            foreach (JToken elem in _data["positions"])
            {
                decimal quantity = elem["hold_amount"].AsDecimal();
                if (quantity != 0)
                {
                    string contractId = (string)elem["contract_id"];
                    msg.Positions.Add(new FuturePosition()
                    {
                        Leverage = Serialization.ParseLeverage((string)elem["lever_rate"]),
                        PositionType = Serialization.ParsePositionType((string)elem["position"]),
                        ContractId = contractId,
                        // Figuring out FutureType around the time of settlement is tricky.
                        // We assume the following:
                        //   1. Our local time when parsing a message is less than one minute ahead of the server time
                        //      when the message was produced. Basically, latency + time skey must be under a minute.
                        //   2. When settlement kicks in, trading is stopped for more than a minute plus time skew.
                        //      OkCoin docs say they stop all trading for around 10 minutes, so it seems reasonable.
                        FutureType = Settlement.FutureTypeFromContractId(contractId, now - TimeSpan.FromMinutes(1)),
                        Quantity = quantity,
                        AvgPrice = elem["avgprice"].AsDecimal(),
                    });
                }
            }
            return msg;
        }

        public IMessageIn Visit(PingResponse msg)
        {
            return msg;
        }

        static FutureState ParseFutureState(JToken data, Currency currency)
        {
            // {
            //   "amount":"1",
            //   "contract_id":"20160212034",
            //   "contract_name":"BTC0212",
            //   "contract_type":"this_week",
            //   "create_date":"1454855333918",
            //   "create_date_str":"2016-02-07 22:28:53",
            //   "deal_amount":"0",
            //   "fee":"0",
            //   "lever_rate":"10",
            //   "orderid":1502057218,
            //   "price":"375",
            //   "price_avg":"0",
            //   "status":"0",
            //   "type":"1",
            //   "unit_amount":"100"
            // }
            var res = new FutureState()
            {
                Timestamp = Util.Time.FromUnixMillis((long)data["create_date"]),
                OrderId = (long)data["orderid"],
                OrderStatus = Serialization.ParseOrderStatus((int)data["status"]),
                Product = new Future()
                {
                    Currency = currency,
                    FutureType = Serialization.ParseFutureType((string)data["contract_type"]),
                },
                Amount = new Amount()
                {
                    Price = data["price"].AsDecimal(),
                    Quantity = data["amount"].AsDecimal(),
                },
                CumFillQuantity = data["deal_amount"].AsDecimal(),
                AvgFillPrice = data["price_avg"].AsDecimal(),
                Fee = data["fee"].AsDecimal(),
                ContractId = (string)data["contract_id"],
            };

            // Infer CoinType from "contract_name". E.g., "BTC0212" => CoinType.Btc.
            string contract = (string)data["contract_name"];
            Condition.Requires(contract, "contract").IsNotNullOrEmpty();
            if (contract.StartsWith("BTC")) res.Product.CoinType = CoinType.Btc;
            else if (contract.StartsWith("LTC")) res.Product.CoinType = CoinType.Ltc;
            else throw new ArgumentException("Unknown value of `contract_name`: " + contract);

            // Decompose "type" into Side and PositionType.
            int type = (int)data["type"];
            switch (type)
            {
                case 1:
                    res.Amount.Side = Side.Buy;
                    res.PositionType = PositionType.Long;
                    break;
                case 2:
                    res.Amount.Side = Side.Buy;
                    res.PositionType = PositionType.Short;
                    break;
                case 3:
                    res.Amount.Side = Side.Sell;
                    res.PositionType = PositionType.Long;
                    break;
                case 4:
                    res.Amount.Side = Side.Sell;
                    res.PositionType = PositionType.Short;
                    break;
                default:
                    throw new ArgumentException("Unknown `type`: " + type);
            }
            return res;
        }

        static SpotState ParseSpotState(JToken data, Currency currency)
        {
            // {
            //   "averagePrice": "439.42",
            //   "completedTradeAmount": "0.01",
            //   "createdDate": 1456152164000,
            //   "id": 199990737,
            //   "orderId": 199990737,
            //   "sigTradeAmount": "0.01",   // May be missing. Means zero.
            //   "sigTradePrice": "439.42",  // May be missing. Means zero.
            //   "status": 2,
            //   "symbol": "btc_usd",
            //   "tradeAmount": "0.01",
            //   "tradePrice": "4.39",
            //   "tradeType": "buy",
            //   "tradeUnitPrice": "439.51",
            //   "unTrade": "0"
            // }
            var res = new SpotState()
            {
                Timestamp = Util.Time.FromUnixMillis((long)data["createdDate"]),
                OrderId = (long)data["orderId"],
                OrderStatus = Serialization.ParseOrderStatus((int)data["status"]),
                Product = new Spot() { Currency = currency },
                Amount = new Amount()
                {
                    Price = data["tradeUnitPrice"].AsDecimal(),
                    Quantity = data["tradeAmount"].AsDecimal(),
                },
                CumFillQuantity = data["completedTradeAmount"].AsDecimal(),
                AvgFillPrice = data["averagePrice"].AsDecimal(),
            };

            // Infer CoinType from "symbol". E.g., "btc_usd" => CoinType.Btc.
            string symbol = (string)data["symbol"];
            Condition.Requires(symbol, "symbol").IsNotNullOrEmpty();
            if (symbol.StartsWith("btc")) res.Product.CoinType = CoinType.Btc;
            else if (symbol.StartsWith("ltc")) res.Product.CoinType = CoinType.Ltc;
            else throw new ArgumentException("Unknown value of `symbol`: " + symbol);

            string type = (string)data["tradeType"];
            Condition.Requires(type, "type").IsNotNullOrEmpty();
            if (type == "buy" || type == "market_buy") res.Amount.Side = Side.Buy;
            else if (type == "sell" || type == "market_sell") res.Amount.Side = Side.Sell;
            else throw new ArgumentException("Unknown value of `type`: " + type);

            // sigTradeAmount and sigTradePrice are optional fields.
            JToken fillQuantity = data["sigTradeAmount"];
            if (fillQuantity != null) res.FillQuantity = fillQuantity.AsDecimal();
            JToken fillPrice = data["sigTradePrice"];
            if (fillPrice != null) res.FillPrice = fillPrice.AsDecimal();

            return res;
        }
    }

    public static class ResponseParser
    {
        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        // Channel name => constructor for the message that prepopulates all fields whose values
        // are determined by the channel name.
        static readonly Dictionary<string, Func<IMessageIn>> _messageCtors =
            new Dictionary<string, Func<IMessageIn>>();

        static ResponseParser()
        {
            _messageCtors.Add(Channels.FuturePositions(), () => new FuturePositionsUpdate() { });
            Action<Product> Subscribe = (Product product) =>
            {
                _messageCtors.Add(Channels.MarketData(product, MarketData.Depth60),
                                  () => new ProductDepth() { Product = product.Clone() });
                _messageCtors.Add(Channels.MarketData(product, MarketData.Trades),
                                  () => new ProductTrades() { Product = product.Clone() });
            };
            foreach (var currency in Util.Enum.Values<Currency>())
            {
                foreach (var product in Util.Enum.Values<ProductType>())
                {
                    _messageCtors.Add(Channels.NewOrder(product, currency),
                                      () => new NewOrderResponse() { ProductType = product, Currency = currency });
                    _messageCtors.Add(Channels.CancelOrder(product, currency),
                                      () => new CancelOrderResponse() { ProductType = product, Currency = currency });
                    _messageCtors.Add(Channels.MyOrders(product, currency),
                                      () => new MyOrderUpdate() { ProductType = product, Currency = currency });
                }
                foreach (var coin in Util.Enum.Values<CoinType>())
                {
                    Subscribe(new Spot() { Currency = currency, CoinType = coin });
                    foreach (var ft in Util.Enum.Values<FutureType>())
                    {
                        Subscribe(new Future() { Currency = currency, CoinType = coin, FutureType = ft });
                    }
                }
            }
        }

        public static IEnumerable<IMessageIn> Parse(string serialized)
        {
            // Ping response:
            //   {"event":"pong"}
            // Error (subscribing to inexisting channel "bogus"):
            //   [{"channel":"bogus","errorcode":"10015","success":"false"}]
            // Success (trade notification):
            //   [{ "channel":"ok_btcusd_trades_v1","data":[["78270746","431.3","0.01","22:02:41","ask"]]}]
            //
            // There may be more than one object in the list.
            var parsed = JToken.Parse(serialized);
            Condition.Requires(parsed, "parsed").IsNotNull();
            switch (parsed.Type)
            {
                case JTokenType.Array:
                    return parsed.Select(ParseMessage).Where(msg => msg != null).ToArray();
                case JTokenType.Object:
                    Condition.Requires((string)parsed["event"], "event").IsEqualTo("pong");
                    IMessageIn pong = new PingResponse();
                    pong.Visit(new MessageParser(null));
                    return new IMessageIn[] { pong };
                default:
                    throw new ArgumentException("Unexpected JSON type: " + parsed.Type);
            }
        }

        static IMessageIn ParseMessage(JToken root)
        {
            // If `root` isn't a JSON object, throws.
            // If there is no such field, we get a null.
            var channel = (string)root["channel"];
            if (channel == null)
            {
                _log.Error("Incoming message without a channel. Ignoring it.");
                return null;
            }
            Func<IMessageIn> ctor;
            if (!_messageCtors.TryGetValue(channel, out ctor))
            {
                _log.Error("Incoming message with unknown `channel`: '{0}'. Ignoring it.", channel);
                return null;
            }
            IMessageIn msg = ctor.Invoke();
            string errorcode = (string)root["errorcode"];
            if (errorcode != null)
            {
                msg.Error = (ErrorCode)int.Parse(errorcode);
                return msg;
            }
            // Note that data can be null. It expected for some messages.
            // MessageParser will throw if it doesn't like null data.
            JToken data = root["data"];
            try
            {
                return msg.Visit(new MessageParser(data));
            }
            catch (Exception e)
            {
                _log.Error(e, "Unable to parse incoming message. Ignoring it.");
                return null;
            }
        }
    }
}
